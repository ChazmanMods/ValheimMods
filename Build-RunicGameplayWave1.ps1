[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\RunicGameplayWave1'))
$modules = @(
    @{
        Name = 'RunicStorage'
        Version = '1.0.0'
        Project = 'RunicStorage\RunicStorage.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
            'Chazman-RunicTransactions-1.0.0'
            'Chazman-RunicInventory-1.0.0'
        )
    }
    @{
        Name = 'RunicCrafting'
        Version = '1.0.0'
        Project = 'RunicCrafting\RunicCrafting.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
            'Chazman-RunicPermissions-1.0.0'
            'Chazman-RunicTransactions-1.0.0'
        )
    }
    @{
        Name = 'RunicAgriculture'
        Version = '1.0.0'
        Project = 'RunicAgriculture\RunicAgriculture.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicTransactions-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
        )
    }
    @{
        Name = 'RunicProduction'
        Version = '1.0.0'
        Project = 'RunicProduction\RunicProduction.csproj'
        Dependencies = @(
            'denikson-BepInExPack_Valheim-5.4.2333'
            'Chazman-RunicCore-1.0.0'
            'Chazman-RunicPermissions-1.0.0'
            'Chazman-RunicTransactions-1.0.0'
            'Chazman-RunicPersistence-1.0.0'
        )
    }
)
$tests = @(
    'RunicStorage\Tests\RunicStorage.Tests.csproj'
    'RunicCrafting\Tests\RunicCrafting.Tests.csproj'
    'RunicAgriculture\Tests\RunicAgriculture.Tests.csproj'
    'RunicProduction\Tests\RunicProduction.Tests.csproj'
    'RunicGameplayWave1.Tests\RunicGameplayWave1.Tests.csproj'
)
$foundationReleases = @(
    @{ Name = 'RunicCore'; Version = '1.0.0' }
    @{ Name = 'RunicPermissions'; Version = '1.0.0' }
    @{ Name = 'RunicTransactions'; Version = '1.0.0'; AssemblyVersion = '1.0.0.0' }
    @{ Name = 'RunicPersistence'; Version = '1.0.0' }
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

function Test-DirectChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    return [string]::Equals(
        (Split-Path -Parent $resolvedChild),
        $resolvedParent,
        [StringComparison]::OrdinalIgnoreCase)
}

function Get-CollisionSafeObsoletePath {
    param(
        [Parameter(Mandatory = $true)][string]$ObsoleteRoot,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$SourceHash
    )

    $sourceLeaf = [System.IO.Path]::GetFileNameWithoutExtension($SourcePath)
    $extension = [System.IO.Path]::GetExtension($SourcePath)
    if ([string]::IsNullOrWhiteSpace($sourceLeaf) -or $extension -cne '.zip' -or
        $SourceHash.Length -ne 64) {
        throw "Cannot construct a safe obsolete-package identity for $SourcePath."
    }
    $timestamp = [DateTime]::UtcNow.ToString(
        'yyyyMMddTHHmmssfffZ',
        [Globalization.CultureInfo]::InvariantCulture)
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $nonce = [Guid]::NewGuid().ToString('N').Substring(0, 12)
        $leaf = $sourceLeaf + '.superseded-' + $timestamp + '-' +
            $SourceHash.Substring(0, 12) + '-' + $nonce + $extension
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $ObsoleteRoot $leaf))
        if (-not (Test-DirectChildPath -Parent $ObsoleteRoot -Child $candidate)) {
            throw "Refusing to archive a package outside the Obsolete directory: $candidate"
        }
        if (-not (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    throw "Could not allocate a collision-safe obsolete-package name for $SourcePath."
}

foreach ($module in $modules) {
    $projectPath = Join-Path $repoRoot $module.Project
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Gameplay project is missing: $projectPath"
    }
    Invoke-Dotnet @('build', $projectPath, '-c', 'Release')
}

if (-not $SkipTests) {
    foreach ($test in $tests) {
        $testPath = Join-Path $repoRoot $test
        if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) {
            throw "Gameplay test project is missing: $testPath"
        }
        Invoke-Dotnet @('run', '--project', $testPath, '-c', 'Release')
    }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Gameplay projects compile against sibling foundation projects. Require those exact Release DLLs
# to match the already validated foundation packages users will actually install; same-version
# source drift must not pass integration tests and then fail only in the game profile.
foreach ($foundation in $foundationReleases) {
    $foundationName = [string]$foundation.Name
    $foundationVersion = [string]$foundation.Version
    $foundationDll = Join-Path $repoRoot (
        $foundationName + '\bin\Release\netstandard2.1\' + $foundationName + '.dll')
    $foundationZip = Join-Path $repoRoot (
        'artifacts\RunicFoundation\Chazman-' + $foundationName + '-' + $foundationVersion + '.zip')
    if (-not (Test-Path -LiteralPath $foundationDll -PathType Leaf)) {
        throw "Foundation Release DLL is missing: $foundationDll"
    }
    if (-not (Test-Path -LiteralPath $foundationZip -PathType Leaf)) {
        throw "Validated foundation package is missing: $foundationZip"
    }
    $expectedFoundationAssemblyVersion = if ($foundation.ContainsKey('AssemblyVersion')) {
        [string]$foundation['AssemblyVersion']
    }
    else {
        $foundationVersion + '.0'
    }
    $foundationIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($foundationDll)
    if ($foundationIdentity.Name -cne $foundationName -or
        $foundationIdentity.Version.ToString() -cne $expectedFoundationAssemblyVersion) {
        throw "Foundation assembly identity drifted: $($foundationIdentity.FullName)"
    }
    $foundationSourceHash = Get-FileSha256Hex -Path $foundationDll
    $foundationArchive = [System.IO.Compression.ZipFile]::OpenRead($foundationZip)
    try {
        $foundationEntries = @($foundationArchive.Entries | Where-Object {
            $_.FullName -ceq ($foundationName + '.dll')
        })
        if ($foundationEntries.Count -ne 1) {
            throw "$foundationZip must contain exactly one root $foundationName.dll."
        }
        $foundationEntryStream = $foundationEntries[0].Open()
        try {
            $foundationPackageHash = Get-StreamSha256Hex -Stream $foundationEntryStream
        }
        finally {
            $foundationEntryStream.Dispose()
        }
    }
    finally {
        $foundationArchive.Dispose()
    }
    if ($foundationSourceHash -cne $foundationPackageHash) {
        throw "$foundationName source build does not match its released $foundationVersion package."
    }
    Write-Output "FOUNDATION_MATCH $foundationName $foundationVersion DLL_SHA256=$foundationSourceHash"
}

if ($SkipTests) {
    # Dry validation must not create, archive, overwrite, or remove anything in the release
    # artifact tree. Build outputs still update normally, but the transient package stage lives
    # in the operating-system temp directory and is deleted in the guarded finally block.
    $stageParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $stageLeafPrefix = 'RunicGameplayWave1Validation-'
}
else {
    $parityGatePath = Join-Path $repoRoot 'RUNIC_DEDICATED_PARITY.md'
    if (-not (Test-Path -LiteralPath $parityGatePath -PathType Leaf)) {
        throw "The dedicated-player parity gate is missing: $parityGatePath"
    }
    $parityGateText = Get-Content -LiteralPath $parityGatePath -Raw
    if (-not [regex]::IsMatch(
            $parityGateText,
            '(?m)^Status: \*\*GO - exact 18-module dedicated and remote-player parity verified\*\*$')) {
        throw "Gameplay publication is blocked until RUNIC_DEDICATED_PARITY.md records exact 18-module GO."
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
        throw "Gameplay publication is blocked by an incomplete module row in the parity matrix."
    }
    $parityGoRows = [regex]::Matches(
        $parityMatrixText,
        '(?m)^\| [^|]+ \|[^\r\n]+\| \*\*GO\*\* \|').Count
    if ($parityGoRows -ne 18) {
        throw "The dedicated-player parity matrix must contain exactly 18 GO module rows; found $parityGoRows."
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    $stageParent = $artifactRoot
    $stageLeafPrefix = '.stage-'
}
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $stageParent (
    $stageLeafPrefix + [Guid]::NewGuid().ToString('N'))))
if (-not (Test-DirectChildPath -Parent $stageParent -Child $stageRoot)) {
    throw "Refusing to create a package stage outside its exact parent: $stageRoot"
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
        foreach ($extraName in @('CHANGELOG.md', ($name + '.cfg.example'))) {
            $extra = Join-Path $sourceRoot $extraName
            if (-not (Test-Path -LiteralPath $extra -PathType Leaf)) {
                throw "Required $name package file is missing: $extra"
            }
            Copy-Item -LiteralPath $extra -Destination $packageStage
            $expectedEntries += $extraName
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
        $assemblyIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($stagedDll)
        if ($assemblyIdentity.Name -cne $name -or $assemblyIdentity.Version.ToString() -cne ($version + '.0')) {
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
        $obsoleteRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'Obsolete'))
        if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $obsoleteRoot)) {
            throw "Refusing to use an Obsolete directory outside the gameplay artifact root."
        }
        $modulePrefixes = @($modules | ForEach-Object {
            'Chazman-' + [string]$_.Name + '-'
        })
        $pendingByDestination = [System.Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($package in $pendingPackages) {
            $pendingByDestination.Add(
                [System.IO.Path]::GetFullPath([string]$package.Destination),
                $package)
        }
        $superseded = @()
        foreach ($existing in @(Get-ChildItem -LiteralPath $artifactRoot -File -Filter '*.zip')) {
            $isGameplayPackage = $false
            foreach ($prefix in $modulePrefixes) {
                if ($existing.Name.StartsWith($prefix, [StringComparison]::Ordinal)) {
                    $isGameplayPackage = $true
                    break
                }
            }
            if (-not $isGameplayPackage) {
                continue
            }
            if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to archive a reparse-point package: $($existing.FullName)"
            }
            $existingPath = [System.IO.Path]::GetFullPath($existing.FullName)
            $existingHash = Get-FileSha256Hex -Path $existingPath
            if ($pendingByDestination.ContainsKey($existingPath) -and
                $existingHash -ceq [string]$pendingByDestination[$existingPath].ZipHash) {
                continue
            }
            $superseded += [pscustomobject]@{
                Original = $existingPath
                Hash = $existingHash
            }
        }

        $archivedPackages = @()
        $promotedDestinations = @()
        try {
            # Archive and verify every superseded Wave1 root ZIP before copying even one new ZIP.
            # Older versions and different-byte same-version packages remain recoverable.
            if ($superseded.Count -gt 0) {
                New-Item -ItemType Directory -Path $obsoleteRoot -Force | Out-Null
            }
            foreach ($oldPackage in $superseded) {
                $archivePath = Get-CollisionSafeObsoletePath `
                    -ObsoleteRoot $obsoleteRoot `
                    -SourcePath $oldPackage.Original `
                    -SourceHash $oldPackage.Hash
                Move-Item -LiteralPath $oldPackage.Original -Destination $archivePath
                # Record the move before verification so even a verification exception enters
                # the same rollback path and the original root location is restored.
                $archivedPackages += [pscustomobject]@{
                    Original = [string]$oldPackage.Original
                    Archive = $archivePath
                    Hash = [string]$oldPackage.Hash
                }
                if ((Test-Path -LiteralPath $oldPackage.Original) -or
                    -not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
                    (Get-FileSha256Hex -Path $archivePath) -cne $oldPackage.Hash) {
                    throw "Obsolete-package archive verification failed for $($oldPackage.Original)."
                }
                Write-Output "ARCHIVED $($oldPackage.Original) -> $archivePath ZIP_SHA256=$($oldPackage.Hash)"
            }

            foreach ($package in $pendingPackages) {
                if (Test-Path -LiteralPath $package.Destination -PathType Leaf) {
                    $existingFinalHash = Get-FileSha256Hex -Path $package.Destination
                    if ($existingFinalHash -cne $package.ZipHash) {
                        throw "A non-archived destination blocks promotion for $($package.Name)."
                    }
                }
                else {
                    Copy-Item -LiteralPath $package.Source -Destination $package.Destination
                    $promotedDestinations += [string]$package.Destination
                }
                $promotedHash = Get-FileSha256Hex -Path $package.Destination
                if ($promotedHash -cne $package.ZipHash) {
                    throw "Promoted ZIP hash mismatch for $($package.Name)."
                }
                Write-Output "PACKAGED $($package.Destination) ZIP_SHA256=$promotedHash DLL_SHA256=$($package.DllHash)"
            }

            $actualGameplayRoots = @(Get-ChildItem -LiteralPath $artifactRoot -File -Filter '*.zip' |
                Where-Object {
                    $candidateName = $_.Name
                    $matched = $false
                    foreach ($prefix in $modulePrefixes) {
                        if ($candidateName.StartsWith($prefix, [StringComparison]::Ordinal)) {
                            $matched = $true
                            break
                        }
                    }
                    $matched
                } | ForEach-Object { [System.IO.Path]::GetFullPath($_.FullName) })
            if ($actualGameplayRoots.Count -ne $pendingPackages.Count) {
                throw "Promoted Wave1 root set is not exact; expected $($pendingPackages.Count), found $($actualGameplayRoots.Count)."
            }
            foreach ($package in $pendingPackages) {
                if ($actualGameplayRoots -cnotcontains [System.IO.Path]::GetFullPath(
                        [string]$package.Destination)) {
                    throw "Promoted Wave1 root set is missing $($package.Destination)."
                }
            }
        }
        catch {
            $promotionFailure = $_
            $archivedOriginals = @($archivedPackages | ForEach-Object { $_.Original })
            foreach ($destination in $promotedDestinations) {
                if ($archivedOriginals -cnotcontains $destination -and
                    (Test-Path -LiteralPath $destination -PathType Leaf)) {
                    Remove-Item -LiteralPath $destination -Force
                }
            }
            foreach ($oldPackage in $archivedPackages) {
                Copy-Item -LiteralPath $oldPackage.Archive -Destination $oldPackage.Original -Force
                if ((Get-FileSha256Hex -Path $oldPackage.Original) -cne $oldPackage.Hash) {
                    throw "Promotion failed and rollback could not restore $($oldPackage.Original). Original failure: $promotionFailure"
                }
            }
            throw $promotionFailure
        }
    }
    else {
        Write-Output 'Validated all gameplay packages without promotion because -SkipTests was requested.'
    }
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
    $safeParent = Test-DirectChildPath -Parent $stageParent -Child $resolvedStage
    $safeLeaf = (Split-Path -Leaf $resolvedStage).StartsWith(
        $stageLeafPrefix,
        [StringComparison]::Ordinal)
    if ($safeParent -and $safeLeaf) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SkipTests) {
    Write-Output 'Runic gameplay Wave 1 build and package validation completed; tests and artifact promotion were skipped by request.'
}
else {
    Write-Output 'Runic gameplay Wave 1 build, tests, integration checks, and package validation completed.'
}
