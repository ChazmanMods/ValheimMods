[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$name = 'RunicBuildCamera'
$displayName = 'Runic Build Camera'
$guid = 'chazman.RunicBuildCamera'
$version = '1.0.0'
$assemblyVersion = '1.0.0.0'
$projectPath = Join-Path $repoRoot 'RunicBuildCamera\RunicBuildCamera.csproj'
$testProjectPath = Join-Path $repoRoot 'RunicBuildCamera.Tests\RunicBuildCamera.Tests.csproj'
$sourceRoot = Join-Path $repoRoot 'RunicBuildCamera'
$sourceDll = Join-Path $sourceRoot 'bin\Release\netstandard2.1\RunicBuildCamera.dll'
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\RunicBuildCamera'))
$zipName = 'Chazman-RunicBuildCamera-1.0.0.zip'
$finalZipPath = Join-Path $artifactRoot $zipName
$expectedDependencies = @(
    'denikson-BepInExPack_Valheim-5.4.2333'
)
$expectedEntries = @(
    'RunicBuildCamera.dll'
    'manifest.json'
    'README.md'
    'icon.png'
    'CHANGELOG.md'
    'RunicBuildCamera.cfg.example'
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

function Get-DeterministicZipTime {
    param([Parameter(Mandatory = $true)][string]$PackageIdentity)

    # BepInEx 5 considers DLL modification time when caching plugin metadata. A
    # content-derived time keeps identical inputs reproducible while changing when
    # any same-version package byte changes. ZIP stores time at two-second precision.
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

function Test-SameZipClockTime {
    param(
        [Parameter(Mandatory = $true)][DateTimeOffset]$Actual,
        [Parameter(Mandatory = $true)][DateTimeOffset]$Expected
    )

    return $Actual.Year -eq $Expected.Year -and
        $Actual.Month -eq $Expected.Month -and
        $Actual.Day -eq $Expected.Day -and
        $Actual.Hour -eq $Expected.Hour -and
        $Actual.Minute -eq $Expected.Minute -and
        $Actual.Second -eq $Expected.Second
}

function New-DeterministicArchive {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string[]]$EntryNames,
        [Parameter(Mandatory = $true)][DateTimeOffset]$Timestamp
    )

    $zipStream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $zipWriter = [System.IO.Compression.ZipArchive]::new(
            $zipStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            # Explicit entry order, compression, timestamps, and source bytes exclude
            # filesystem enumeration order and mtimes from the archive.
            foreach ($entryName in $EntryNames) {
                $entry = $zipWriter.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $Timestamp
                $sourceStream = [System.IO.File]::OpenRead(
                    (Join-Path $SourceDirectory $entryName))
                try {
                    $entryStream = $entry.Open()
                    try {
                        $sourceStream.CopyTo($entryStream)
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }
                finally {
                    $sourceStream.Dispose()
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
}

function Get-SingleAssemblyAttributeString {
    param(
        [Parameter(Mandatory = $true)]$Assembly,
        [Parameter(Mandatory = $true)][string]$AttributeTypeName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $attributes = @($Assembly.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -ceq $AttributeTypeName
    })
    if ($attributes.Count -ne 1) {
        throw "$Label must occur exactly once in the compiled assembly; found $($attributes.Count)."
    }
    $arguments = @($attributes[0].ConstructorArguments)
    if ($arguments.Count -ne 1) {
        throw "$Label must have exactly one constructor value."
    }
    return [string]$arguments[0].Value
}

function Assert-CompiledMetadata {
    param([Parameter(Mandatory = $true)][string]$Path)

    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        if ($assembly.Name.Name -cne $name -or
            $assembly.Name.Version.ToString() -cne $assemblyVersion) {
            throw "Compiled assembly identity drifted; found $($assembly.Name.FullName)."
        }

        $fileVersion = Get-SingleAssemblyAttributeString `
            -Assembly $assembly `
            -AttributeTypeName 'System.Reflection.AssemblyFileVersionAttribute' `
            -Label 'AssemblyFileVersionAttribute'
        if ($fileVersion -cne $assemblyVersion) {
            throw "Compiled file version drifted; expected $assemblyVersion, found $fileVersion."
        }

        $informationalVersion = Get-SingleAssemblyAttributeString `
            -Assembly $assembly `
            -AttributeTypeName 'System.Reflection.AssemblyInformationalVersionAttribute' `
            -Label 'AssemblyInformationalVersionAttribute'
        if ($informationalVersion -cne $version) {
            throw "Compiled informational version drifted; expected $version, found $informationalVersion."
        }

        $versionResource = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
        if ([string]$versionResource.FileVersion -cne $assemblyVersion) {
            throw "Compiled file-version resource drifted; expected $assemblyVersion, found $($versionResource.FileVersion)."
        }
        if ([string]$versionResource.ProductVersion -cne $version) {
            throw "Compiled informational/product-version resource drifted; expected $version, found $($versionResource.ProductVersion)."
        }
        if ([string]$versionResource.ProductName -cne $displayName) {
            throw "Compiled product name drifted; expected '$displayName', found '$($versionResource.ProductName)'."
        }

        $pluginAttributes = @(
            foreach ($type in @($assembly.MainModule.Types)) {
                foreach ($attribute in @($type.CustomAttributes)) {
                    if ($attribute.AttributeType.FullName -ceq 'BepInEx.BepInPlugin') {
                        [PSCustomObject]@{
                            Type = $type
                            Attribute = $attribute
                        }
                    }
                }
            }
        )
        if ($pluginAttributes.Count -ne 1) {
            throw "Compiled DLL must contain exactly one BepInPlugin declaration; found $($pluginAttributes.Count)."
        }
        if ($pluginAttributes[0].Type.FullName -cne 'RunicBuildCamera.Plugin') {
            throw "BepInPlugin declaration moved to unexpected type '$($pluginAttributes[0].Type.FullName)'."
        }
        $pluginArguments = @($pluginAttributes[0].Attribute.ConstructorArguments)
        if ($pluginArguments.Count -ne 3) {
            throw 'Compiled BepInPlugin declaration must have exactly three constructor values.'
        }
        $actualPluginIdentity = @(
            [string]$pluginArguments[0].Value
            [string]$pluginArguments[1].Value
            [string]$pluginArguments[2].Value
        )
        $expectedPluginIdentity = @($guid, $displayName, $version)
        Assert-ExactOrderedStrings `
            -Actual $actualPluginIdentity `
            -Expected $expectedPluginIdentity `
            -Label 'Compiled BepInPlugin identity'

        return [PSCustomObject]@{
            AssemblyVersion = $assembly.Name.Version.ToString()
            FileVersion = $fileVersion
            InformationalVersion = $informationalVersion
            PluginGuid = $actualPluginIdentity[0]
            PluginName = $actualPluginIdentity[1]
            PluginVersion = $actualPluginIdentity[2]
        }
    }
    finally {
        $assembly.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Runic Build Camera project is missing: $projectPath"
}
if (-not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
    throw "Runic Build Camera test project is missing; packaging is forbidden until it exists: $testProjectPath"
}

Invoke-Dotnet @('build', $projectPath, '-c', 'Release')
Write-Output 'BUILD_OK PROJECT=RunicBuildCamera CONFIGURATION=Release'
if (-not $SkipTests) {
    Invoke-Dotnet @('run', '--project', $testProjectPath, '-c', 'Release')
    Write-Output 'TESTS_OK PROJECT=RunicBuildCamera.Tests CONFIGURATION=Release'
}
else {
    Write-Output 'TESTS_SKIPPED PROJECT=RunicBuildCamera.Tests'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$bepInExProfile = [Environment]::GetEnvironmentVariable('BEPINEX_PROFILE')
if ([string]::IsNullOrWhiteSpace($bepInExProfile)) {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $profileNodes = @($projectXml.Project.PropertyGroup.BEPINEX_PROFILE)
    $projectProfile = if ($profileNodes.Count -eq 0) {
        $null
    }
    elseif ($profileNodes[0] -is [System.Xml.XmlElement]) {
        $profileNodes[0].InnerText
    }
    else {
        [string]$profileNodes[0]
    }
    if ([string]::IsNullOrWhiteSpace($projectProfile)) {
        throw 'Could not locate the BepInEx profile needed for compiled metadata validation.'
    }
    $bepInExProfile = $projectProfile
}
if (-not [System.IO.Path]::IsPathRooted($bepInExProfile)) {
    $bepInExProfile = Join-Path $repoRoot $bepInExProfile
}
$cecilPath = Join-Path ([System.IO.Path]::GetFullPath($bepInExProfile)) 'core\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecilPath -PathType Leaf)) {
    throw "Mono.Cecil is missing from the BepInEx profile: $cecilPath"
}
Add-Type -Path $cecilPath

$requiredSources = [ordered]@{
    'RunicBuildCamera.dll' = $sourceDll
    'manifest.json' = (Join-Path $sourceRoot 'manifest.json')
    'README.md' = (Join-Path $sourceRoot 'README.md')
    'icon.png' = (Join-Path $sourceRoot 'icon.png')
    'CHANGELOG.md' = (Join-Path $sourceRoot 'CHANGELOG.md')
    'RunicBuildCamera.cfg.example' = (Join-Path $sourceRoot 'RunicBuildCamera.cfg.example')
}
foreach ($entryName in $expectedEntries) {
    $source = [string]$requiredSources[$entryName]
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required Runic Build Camera package file is missing: $source"
    }
}

$manifest = Get-Content -LiteralPath $requiredSources['manifest.json'] -Raw | ConvertFrom-Json
if ([string]$manifest.name -cne $name -or [string]$manifest.version_number -cne $version) {
    throw "Manifest identity drift; expected $name $version."
}
if ($null -eq $manifest.PSObject.Properties['dependencies']) {
    throw 'Runic Build Camera manifest has no dependencies array.'
}
$actualDependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
Assert-ExactOrderedStrings `
    -Actual $actualDependencies `
    -Expected $expectedDependencies `
    -Label 'Runic Build Camera manifest dependencies'

$icon = [System.Drawing.Image]::FromFile($requiredSources['icon.png'])
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
        throw 'Runic Build Camera icon must be exactly 256x256 pixels.'
    }
    if ($icon.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Runic Build Camera icon must be a PNG image.'
    }
}
finally {
    $icon.Dispose()
}

$compiledMetadata = Assert-CompiledMetadata -Path $sourceDll
Write-Output (
    "METADATA_OK ASSEMBLY=$($compiledMetadata.AssemblyVersion) " +
    "PLUGIN_GUID=$($compiledMetadata.PluginGuid) " +
    "PLUGIN_NAME='$($compiledMetadata.PluginName)' " +
    "PLUGIN_VERSION=$($compiledMetadata.PluginVersion) " +
    "FILE=$($compiledMetadata.FileVersion) INFO=$($compiledMetadata.InformationalVersion)")
Write-Output "MANIFEST_OK NAME=$name VERSION=$version DEPENDENCIES=$($expectedDependencies -join ',')"
Write-Output 'ICON_OK WIDTH=256 HEIGHT=256 FORMAT=PNG'

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$stageRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactRoot ('.stage-' + [Guid]::NewGuid().ToString('N'))))
$artifactPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $stageRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create a package stage outside the Runic Build Camera artifact directory: $stageRoot"
}

try {
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    $packageStage = Join-Path $stageRoot $name
    New-Item -ItemType Directory -Path $packageStage | Out-Null

    foreach ($entryName in $expectedEntries) {
        $source = [string]$requiredSources[$entryName]
        $destination = Join-Path $packageStage $entryName
        Copy-Item -LiteralPath $source -Destination $destination
        if ((Get-FileSha256Hex -Path $source) -cne (Get-FileSha256Hex -Path $destination)) {
            throw "Staged '$entryName' is not byte-equal to its validated source."
        }
    }

    $nestedStageEntries = @(Get-ChildItem -LiteralPath $packageStage -Directory)
    if ($nestedStageEntries.Count -ne 0) {
        throw 'Runic Build Camera package stage contains a nested directory.'
    }
    $stagedEntries = @(Get-ChildItem -LiteralPath $packageStage -File | ForEach-Object { $_.Name })
    Assert-ExactRootEntries `
        -Actual $stagedEntries `
        -Expected $expectedEntries `
        -Label 'Runic Build Camera package stage'

    $timestampIdentityParts = [System.Collections.Generic.List[string]]::new()
    foreach ($entryName in $expectedEntries) {
        $entryHash = Get-FileSha256Hex -Path (Join-Path $packageStage $entryName)
        $timestampIdentityParts.Add($entryName + '=' + $entryHash)
    }
    $timestampIdentity = $zipName + '|' + [string]::Join('|', $timestampIdentityParts)
    $normalizedZipTime = Get-DeterministicZipTime -PackageIdentity $timestampIdentity
    if ($normalizedZipTime.Second % 2 -ne 0 -or $normalizedZipTime.Millisecond -ne 0) {
        throw 'Content-derived ZIP timestamp is not at exact even-second precision.'
    }

    $stagedZipPath = Join-Path $stageRoot $zipName
    $repeatZipPath = Join-Path $stageRoot ('repeat-' + $zipName)
    New-DeterministicArchive `
        -SourceDirectory $packageStage `
        -DestinationPath $stagedZipPath `
        -EntryNames $expectedEntries `
        -Timestamp $normalizedZipTime
    New-DeterministicArchive `
        -SourceDirectory $packageStage `
        -DestinationPath $repeatZipPath `
        -EntryNames $expectedEntries `
        -Timestamp $normalizedZipTime

    $stagedZipHash = Get-FileSha256Hex -Path $stagedZipPath
    $repeatZipHash = Get-FileSha256Hex -Path $repeatZipPath
    if ($stagedZipHash -cne $repeatZipHash) {
        throw "Deterministic repeatability failed; archive hashes differ: $stagedZipHash versus $repeatZipHash."
    }
    if ((Get-Item -LiteralPath $stagedZipPath).Length -ne
        (Get-Item -LiteralPath $repeatZipPath).Length) {
        throw 'Deterministic repeatability failed; archive lengths differ.'
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($stagedZipPath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
        Assert-ExactRootEntries `
            -Actual $entryNames `
            -Expected $expectedEntries `
            -Label $zipName

        $dllEntries = @($archive.Entries | Where-Object {
            [System.IO.Path]::GetExtension($_.FullName) -ieq '.dll'
        })
        if ($dllEntries.Count -ne 1 -or
            $dllEntries[0].FullName -cne 'RunicBuildCamera.dll') {
            throw "$zipName must contain exactly one DLL, RunicBuildCamera.dll."
        }

        foreach ($expectedEntry in $expectedEntries) {
            $matchingEntries = @($archive.Entries | Where-Object {
                $_.FullName -ceq $expectedEntry
            })
            if ($matchingEntries.Count -ne 1) {
                throw "$zipName must contain exactly one '$expectedEntry' entry."
            }
            if (-not (Test-SameZipClockTime `
                -Actual $matchingEntries[0].LastWriteTime `
                -Expected $normalizedZipTime)) {
                throw "$zipName entry '$expectedEntry' has a non-deterministic timestamp."
            }
            if ($matchingEntries[0].LastWriteTime.Second % 2 -ne 0) {
                throw "$zipName entry '$expectedEntry' is not stored at ZIP even-second precision."
            }

            $archiveEntryStream = $matchingEntries[0].Open()
            try {
                $archiveEntryHash = Get-StreamSha256Hex -Stream $archiveEntryStream
            }
            finally {
                $archiveEntryStream.Dispose()
            }
            $sourceEntryHash = Get-FileSha256Hex -Path ([string]$requiredSources[$expectedEntry])
            if ($archiveEntryHash -cne $sourceEntryHash) {
                throw "$zipName entry '$expectedEntry' is not byte-equal to its validated source."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $timestampText = $normalizedZipTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
    Write-Output "LAYOUT_OK ROOT_ENTRIES=6 DLLS=1 BYTE_EQUAL=6 TIMESTAMP=$timestampText"
    Write-Output "REPEATABLE $zipName ZIP_SHA256=$stagedZipHash"
    Write-Output "VALIDATED $zipName ZIP_SHA256=$stagedZipHash DLL_SHA256=$(Get-FileSha256Hex -Path $sourceDll)"

    if (-not $SkipTests) {
        # Promotion occurs only after the Release build, test run, metadata checks,
        # archive validation, and repeatability comparison have all succeeded.
        Copy-Item -LiteralPath $stagedZipPath -Destination $finalZipPath -Force
        $promotedHash = Get-FileSha256Hex -Path $finalZipPath
        if ($promotedHash -cne $stagedZipHash) {
            throw 'Promoted Runic Build Camera ZIP is not byte-equal to the validated archive.'
        }
        Write-Output "PACKAGED $finalZipPath ZIP_SHA256=$promotedHash DLL_SHA256=$(Get-FileSha256Hex -Path $sourceDll)"
    }
    else {
        Write-Output 'Validated Runic Build Camera without promotion because -SkipTests was requested.'
    }
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
    $safeParent = [string]::Equals(
        (Split-Path -Parent $resolvedStage),
        $artifactRoot,
        [StringComparison]::OrdinalIgnoreCase)
    $safeLeaf = (Split-Path -Leaf $resolvedStage).StartsWith(
        '.stage-',
        [StringComparison]::Ordinal)
    if (-not $resolvedStage.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $safeParent -or -not $safeLeaf) {
        throw "Refusing unsafe Runic Build Camera stage cleanup: $resolvedStage"
    }
    if (Test-Path -LiteralPath $resolvedStage) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $resolvedStage) {
        throw "Runic Build Camera package stage cleanup failed: $resolvedStage"
    }
    Write-Output 'STAGE_CLEANED RunicBuildCamera'
}

if ($SkipTests) {
    Write-Output 'Runic Build Camera build and package validation completed; tests and artifact promotion were skipped by request.'
}
else {
    Write-Output 'Runic Build Camera build, tests, package validation, and deterministic promotion completed.'
}
