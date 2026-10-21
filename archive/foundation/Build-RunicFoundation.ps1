[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\RunicFoundation'))
$modules = @(
    @{
        Name = 'RunicCore'
        Version = '1.0.0'
        Project = 'RunicCore\RunicCore.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
        )
    }
    @{
        Name = 'RunicPermissions'
        Version = '1.0.0'
        Project = 'RunicPermissions\RunicPermissions.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
        )
    }
    @{
        Name = 'RunicTransactions'
        Version = '1.0.0'
        AssemblyVersion = '1.0.0.0'
        Project = 'RunicTransactions\RunicTransactions.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
        )
    }
    @{
        Name = 'RunicPersistence'
        Version = '1.0.0'
        Project = 'RunicPersistence\RunicPersistence.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
        )
    }
)
$tests = @(
    'RunicCore\Tests\RunicCore.Tests.csproj'
    'RunicPermissions\Tests\RunicPermissions.Tests.csproj'
    'RunicTransactions\Tests\RunicTransactions.Tests.csproj'
    'RunicPersistence.Tests\RunicPersistence.Tests.csproj'
    'RunicFoundation.Tests\RunicFoundation.Tests.csproj'
)

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-ExactOrderedStrings {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw "$Label count drifted; expected $($Expected.Count), found $($Actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($Actual[$index] -cne $Expected[$index]) {
            throw "$Label drifted at index $index; expected '$($Expected[$index])', found '$($Actual[$index])'."
        }
    }
}

function Assert-ExactRootEntries {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw "$Label root entry count drifted; expected $($Expected.Count), found $($Actual.Count)."
    }
    foreach ($entry in $Actual) {
        if ($entry.IndexOf('/') -ge 0 -or $entry.IndexOf('\') -ge 0) {
            throw "$Label contains a non-root archive entry: $entry"
        }
        if ($Expected -cnotcontains $entry) {
            throw "$Label contains unexpected root entry: $entry"
        }
    }
    foreach ($entry in $Expected) {
        if ($Actual -cnotcontains $entry) {
            throw "$Label is missing root entry: $entry"
        }
    }
}

function Get-DeterministicZipTime {
    param([Parameter(Mandatory = $true)][string]$PackageIdentity)

    # BepInEx 5 keys its type-loader metadata cache partly by the plugin file's
    # modification time. A single constant ZIP timestamp can therefore leave an
    # upgraded DLL running with stale cached plugin-version metadata. Derive a
    # stable, even-second timestamp from the package identity plus the validated
    # entry hashes. Repeated builds of identical inputs remain deterministic,
    # while either a version change or a corrected same-version payload invalidates
    # the cache after extraction.
    $identityBytes = [System.Text.Encoding]::UTF8.GetBytes($PackageIdentity)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash($identityBytes)
    }
    finally {
        $sha256.Dispose()
    }
    [uint64]$twoSecondSlots = [System.BitConverter]::ToUInt32($digest, 0)
    $twoSecondSlots = $twoSecondSlots % [uint64]315360000
    return [DateTimeOffset]::new(
        2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero).AddSeconds([double]($twoSecondSlots * 2))
}

function Get-StreamSha256Hex {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $algorithm.ComputeHash($Stream)
        return (($bytes | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return Get-StreamSha256Hex -Stream $stream
    }
    finally {
        $stream.Dispose()
    }
}

function Get-CollisionSafeArchivePath {
    param(
        [Parameter(Mandatory = $true)][string]$ArchiveDirectory,
        [Parameter(Mandatory = $true)][string]$SourceName,
        [Parameter(Mandatory = $true)][string]$SourceHash
    )

    $candidate = Join-Path $ArchiveDirectory $SourceName
    if (-not (Test-Path -LiteralPath $candidate)) {
        return $candidate
    }

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($SourceName)
    $extension = [System.IO.Path]::GetExtension($SourceName)
    $hashSuffix = $SourceHash.Substring(0, 12).ToLowerInvariant()
    $candidate = Join-Path $ArchiveDirectory ($stem + '.superseded-' + $hashSuffix + $extension)
    if (-not (Test-Path -LiteralPath $candidate)) {
        return $candidate
    }

    for ($counter = 2; $counter -le 100000; $counter++) {
        $candidate = Join-Path $ArchiveDirectory (
            $stem + '.superseded-' + $hashSuffix + '-' + $counter + $extension)
        if (-not (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    throw "Could not allocate a collision-safe archive name for $SourceName."
}

function Move-SupersededFoundationPackages {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][array]$Packages
    )

    # This function is called only from the promotion branch. Validation-only runs never create
    # the archive directory or move an existing published artifact.
    $obsoleteRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'Obsolete'))
    $expectedObsoleteParent = [System.IO.Path]::GetFullPath($artifactRoot)
    if (-not [string]::Equals(
            (Split-Path -Parent $obsoleteRoot),
            $expectedObsoleteParent,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to archive outside the foundation artifact directory: $obsoleteRoot"
    }

    $superseded = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($candidate in @(Get-ChildItem -LiteralPath $artifactRoot -File -Filter '*.zip')) {
        $isFoundationPackage = $false
        foreach ($module in $modules) {
            $prefix = 'Chazman-' + [string]$module.Name + '-'
            if ($candidate.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
                $candidate.Extension.Equals('.zip', [StringComparison]::OrdinalIgnoreCase)) {
                $isFoundationPackage = $true
                break
            }
        }
        if (-not $isFoundationPackage) { continue }

        $matchingCurrent = @($Packages | Where-Object {
            [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$_.Destination),
                [System.IO.Path]::GetFullPath($candidate.FullName),
                [StringComparison]::OrdinalIgnoreCase)
        })
        if ($matchingCurrent.Count -eq 1 -and
            (Get-FileSha256Hex -Path $candidate.FullName) -ceq
                [string]$matchingCurrent[0].ZipHash) {
            # The already-published file is byte-identical to the validated release and is not
            # superseded. Leaving it in place also avoids a needless timestamp mutation.
            continue
        }
        if ($matchingCurrent.Count -gt 1) {
            throw "Multiple pending packages target $($candidate.FullName)."
        }
        $superseded.Add($candidate)
    }

    if ($superseded.Count -eq 0) { return }
    New-Item -ItemType Directory -Path $obsoleteRoot -Force | Out-Null
    foreach ($source in @($superseded | Sort-Object Name)) {
        $resolvedSource = [System.IO.Path]::GetFullPath($source.FullName)
        if (-not [string]::Equals(
                (Split-Path -Parent $resolvedSource),
                $artifactRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to move a non-root package: $resolvedSource"
        }

        $sourceHash = Get-FileSha256Hex -Path $resolvedSource
        $archivePath = [System.IO.Path]::GetFullPath((Get-CollisionSafeArchivePath `
            -ArchiveDirectory $obsoleteRoot `
            -SourceName $source.Name `
            -SourceHash $sourceHash))
        if (-not [string]::Equals(
                (Split-Path -Parent $archivePath),
                $obsoleteRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to archive outside the Obsolete directory: $archivePath"
        }

        Move-Item -LiteralPath $resolvedSource -Destination $archivePath
        if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
            (Get-FileSha256Hex -Path $archivePath) -cne $sourceHash) {
            throw "Archived package verification failed for $resolvedSource."
        }
        Write-Output "ARCHIVED $resolvedSource -> $archivePath ZIP_SHA256=$sourceHash"
    }
}

foreach ($module in $modules) {
    $projectPath = Join-Path $repoRoot $module.Project
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Foundation project is missing: $projectPath"
    }
    Invoke-Dotnet @('build', $projectPath, '-c', 'Release')
}

if (-not $SkipTests) {
    foreach ($test in $tests) {
        $testPath = Join-Path $repoRoot $test
        if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) {
            throw "Foundation test project is missing: $testPath"
        }
        Invoke-Dotnet @('run', '--project', $testPath, '-c', 'Release')
    }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $SkipTests) {
    $parityGatePath = Join-Path $repoRoot 'RUNIC_DEDICATED_PARITY.md'
    if (-not (Test-Path -LiteralPath $parityGatePath -PathType Leaf)) {
        throw "The dedicated-player parity gate is missing: $parityGatePath"
    }
    $parityGateText = Get-Content -LiteralPath $parityGatePath -Raw
    if (-not [regex]::IsMatch(
            $parityGateText,
            '(?m)^Status: \*\*GO - exact 18-module dedicated and remote-player parity verified\*\*$')) {
        throw "Foundation publication is blocked until RUNIC_DEDICATED_PARITY.md records exact 18-module GO."
    }
    $parityMatrixMatch = [regex]::Match(
        $parityGateText,
        '(?s)## Required feature matrix\s+(?<matrix>.*?)\s+## Network and compatibility acceptance')
    if (-not $parityMatrixMatch.Success) {
        throw "The dedicated-player parity matrix is missing or malformed."
    }
    $parityMatrixText = $parityMatrixMatch.Groups['matrix'].Value
    if ([regex]::IsMatch(
            $parityMatrixText,
            '\*\*(OPEN|PARTIAL|IN PROGRESS|SOURCE CHECKPOINT|HOLD)[^*]*\*\*')) {
        throw "Foundation publication is blocked by an incomplete module row in the parity matrix."
    }
    $parityGoRows = [regex]::Matches(
        $parityMatrixText,
        '(?m)^\| [^|]+ \|[^\r\n]+\| \*\*GO\*\* \|').Count
    if ($parityGoRows -ne 18) {
        throw "The dedicated-player parity matrix must contain exactly 18 GO module rows; found $parityGoRows."
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
}
$stageParent = if ($SkipTests) {
    [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
}
else {
    $artifactRoot
}
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $stageParent ('.stage-runic-foundation-' + [Guid]::NewGuid().ToString('N'))))
$stagePrefix = $stageParent.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $stageRoot.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create a package stage outside its validated parent: $stageRoot"
}

try {
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    $pendingPackages = @()

    foreach ($module in $modules) {
        $name = [string]$module.Name
        $version = [string]$module.Version
        $sourceRoot = Join-Path $repoRoot $name
        $packageStage = Join-Path $stageRoot $name
        New-Item -ItemType Directory -Path $packageStage | Out-Null

        $sourceDll = Join-Path (Join-Path $sourceRoot 'bin\Release\netstandard2.1') ($name + '.dll')
        $requiredSources = @(
            $sourceDll
            (Join-Path $sourceRoot 'manifest.json')
            (Join-Path $sourceRoot 'README.md')
            (Join-Path $sourceRoot 'icon.png')
        )
        foreach ($source in $requiredSources) {
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Required $name package file is missing: $source"
            }
            Copy-Item -LiteralPath $source -Destination $packageStage
        }

        $expectedEntries = @(
            ($name + '.dll')
            'manifest.json'
            'README.md'
            'icon.png'
        )
        foreach ($optionalName in @('CHANGELOG.md', ($name + '.cfg.example'))) {
            $optional = Join-Path $sourceRoot $optionalName
            if (Test-Path -LiteralPath $optional -PathType Leaf) {
                Copy-Item -LiteralPath $optional -Destination $packageStage
                $expectedEntries += $optionalName
            }
        }

        $nestedStageEntries = @(Get-ChildItem -LiteralPath $packageStage -Directory)
        if ($nestedStageEntries.Count -ne 0) {
            throw "$name package stage contains a nested directory."
        }
        $stagedEntries = @(Get-ChildItem -LiteralPath $packageStage -File | ForEach-Object { $_.Name })
        Assert-ExactRootEntries -Actual $stagedEntries -Expected $expectedEntries -Label "$name package stage"

        $manifestPath = Join-Path $packageStage 'manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.name -cne $name -or [string]$manifest.version_number -cne $version) {
            throw "Manifest identity drift for $name; expected $name $version."
        }
        if ($null -eq $manifest.PSObject.Properties['dependencies']) {
            throw "$name manifest has no dependencies array."
        }
        $actualDependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
        $expectedDependencies = @($module.Dependencies | ForEach-Object { [string]$_ })
        Assert-ExactOrderedStrings -Actual $actualDependencies -Expected $expectedDependencies -Label "$name manifest dependencies"

        $iconPath = Join-Path $packageStage 'icon.png'
        $icon = [System.Drawing.Image]::FromFile($iconPath)
        try {
            if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
                throw "$name icon must be exactly 256x256 pixels."
            }
        }
        finally {
            $icon.Dispose()
        }

        $stagedDll = Join-Path $packageStage ($name + '.dll')
        $expectedAssemblyVersion = if ($module.ContainsKey('AssemblyVersion')) {
            [string]$module['AssemblyVersion']
        }
        else {
            $version + '.0'
        }
        $assemblyIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($stagedDll)
        if ($assemblyIdentity.Name -cne $name -or
            $assemblyIdentity.Version.ToString() -cne $expectedAssemblyVersion) {
            throw "$name packaged assembly identity drifted; found $($assemblyIdentity.FullName)."
        }
        $sourceDllHash = Get-FileSha256Hex -Path $sourceDll
        $stagedDllHash = Get-FileSha256Hex -Path $stagedDll
        if ($sourceDllHash -cne $stagedDllHash) {
            throw "$name staged DLL hash does not match its Release build output."
        }

        $zipName = 'Chazman-' + $name + '-' + $version + '.zip'
        $stagedZipPath = Join-Path $stageRoot $zipName
        $finalZipPath = Join-Path $artifactRoot $zipName
        $zipStream = [System.IO.File]::Open(
            $stagedZipPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        try {
            $zipWriter = [System.IO.Compression.ZipArchive]::new(
                $zipStream,
                [System.IO.Compression.ZipArchiveMode]::Create,
                $false)
            try {
                $timestampIdentityParts = [System.Collections.Generic.List[string]]::new()
                foreach ($entryName in $expectedEntries) {
                    $entryHash = Get-FileSha256Hex -Path (Join-Path $packageStage $entryName)
                    $timestampIdentityParts.Add($entryName + '=' + $entryHash)
                }
                $timestampIdentity = $zipName + '|' +
                    [string]::Join('|', $timestampIdentityParts)
                $normalizedZipTime = Get-DeterministicZipTime -PackageIdentity $timestampIdentity
                # Exact entry order and timestamps make packaging deterministic for identical
                # validated inputs; source filesystem enumeration and mtimes never enter the ZIP.
                # The content-aware identity also invalidates BepInEx's cached plugin metadata
                # when a corrected same-version package replaces an interim deterministic archive.
                foreach ($entryName in $expectedEntries) {
                    $entry = $zipWriter.CreateEntry(
                        $entryName,
                        [System.IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $normalizedZipTime
                    $sourceEntryStream = [System.IO.File]::OpenRead(
                        (Join-Path $packageStage $entryName))
                    try {
                        $archiveEntryStream = $entry.Open()
                        try {
                            $sourceEntryStream.CopyTo($archiveEntryStream)
                        }
                        finally {
                            $archiveEntryStream.Dispose()
                        }
                    }
                    finally {
                        $sourceEntryStream.Dispose()
                    }
                }
            }
            finally {
                $zipWriter.Dispose()
            }
        }
        finally {
            $zipStream.Dispose()
        }

        $archive = [System.IO.Compression.ZipFile]::OpenRead($stagedZipPath)
        try {
            $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
            Assert-ExactRootEntries -Actual $entryNames -Expected $expectedEntries -Label $zipName

            $dllEntries = @($archive.Entries | Where-Object {
                [System.IO.Path]::GetExtension($_.FullName) -ieq '.dll'
            })
            if ($dllEntries.Count -ne 1 -or $dllEntries[0].FullName -cne ($name + '.dll')) {
                throw "$zipName must contain only its own root DLL; game, foundation, and other module DLLs are forbidden."
            }

            foreach ($expectedEntry in $expectedEntries) {
                $matchingEntries = @($archive.Entries | Where-Object {
                    $_.FullName -ceq $expectedEntry
                })
                if ($matchingEntries.Count -ne 1) {
                    throw "$zipName must contain exactly one '$expectedEntry' entry."
                }
                $archiveEntryStream = $matchingEntries[0].Open()
                try {
                    $archiveEntryHash = Get-StreamSha256Hex -Stream $archiveEntryStream
                }
                finally {
                    $archiveEntryStream.Dispose()
                }
                $stagedEntryHash = Get-FileSha256Hex -Path (Join-Path $packageStage $expectedEntry)
                if ($archiveEntryHash -cne $stagedEntryHash) {
                    throw "$zipName entry '$expectedEntry' does not match the validated package stage."
                }
            }

            $entryStream = $dllEntries[0].Open()
            try {
                $archiveDllHash = Get-StreamSha256Hex -Stream $entryStream
            }
            finally {
                $entryStream.Dispose()
            }
            if ($archiveDllHash -cne $sourceDllHash) {
                throw "$zipName DLL hash does not match the Release build output."
            }
        }
        finally {
            $archive.Dispose()
        }

        $stagedZipHash = Get-FileSha256Hex -Path $stagedZipPath
        $pendingPackages += @{
            Name = $name
            Source = $stagedZipPath
            Destination = $finalZipPath
            DllHash = $sourceDllHash
            ZipHash = $stagedZipHash
        }
        Write-Output "VALIDATED $zipName ZIP_SHA256=$stagedZipHash DLL_SHA256=$sourceDllHash"
    }

    # Final artifacts remain untouched until every build, test, manifest, layout, icon,
    # assembly identity, and byte-for-byte DLL validation has succeeded.
    if (-not $SkipTests) {
        Move-SupersededFoundationPackages -Packages @($pendingPackages)
        foreach ($package in $pendingPackages) {
            if ((Test-Path -LiteralPath $package.Destination -PathType Leaf) -and
                (Get-FileSha256Hex -Path $package.Destination) -ceq $package.ZipHash) {
                Write-Output "CURRENT $($package.Destination) ZIP_SHA256=$($package.ZipHash) DLL_SHA256=$($package.DllHash)"
                continue
            }
            Copy-Item -LiteralPath $package.Source -Destination $package.Destination -Force
            $promotedHash = Get-FileSha256Hex -Path $package.Destination
            if ($promotedHash -cne $package.ZipHash) {
                throw "Promoted ZIP hash mismatch for $($package.Name)."
            }
            Write-Output "PACKAGED $($package.Destination) ZIP_SHA256=$promotedHash DLL_SHA256=$($package.DllHash)"
        }
    }
    else {
        Write-Output 'Validated all foundation packages in a disposable temporary stage; the Foundation artifact tree was not changed because -SkipTests was requested.'
    }
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
    $safeParent = [string]::Equals(
        (Split-Path -Parent $resolvedStage),
        $stageParent,
        [StringComparison]::OrdinalIgnoreCase)
    $safeLeaf = (Split-Path -Leaf $resolvedStage).StartsWith(
        '.stage-runic-foundation-',
        [StringComparison]::Ordinal)
    if ($resolvedStage.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        $safeParent -and $safeLeaf) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SkipTests) {
    Write-Output 'Runic foundation build and package validation completed; tests, archival, artifact-directory mutation, and package promotion were skipped by request.'
}
else {
    Write-Output 'Runic foundation build, tests, integration checks, and package validation completed.'
}
