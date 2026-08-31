[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'RunicStorage',
        'RunicCrafting',
        'RunicAgriculture',
        'RunicProduction',
        'RunicPrecisionBuildTool',
        'RunicInventory',
        'RunicPortals',
        'RunicExploration',
        'RunicAwareness',
        'RunicInteraction',
        'RunicSafety',
        'RunicVelocity',
        'RunicSentinel',
        'RunicWorldEngine')]
    [string]$Module,

    [Parameter(Mandatory = $true)]
    [string]$LiveEvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$LiveDllPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+/[0-9]+$')]
    [string]$FocusedTestResult,

    [Parameter(Mandatory = $true)]
    [ValidateSet('0 warnings, 0 errors')]
    [string]$CleanBuildResult
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$moduleRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Module))
$artifactModuleRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot ('artifacts\' + $Module)))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactModuleRoot 'Release'))

function Test-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    return $resolvedChild.StartsWith(
        $resolvedParent + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return (($algorithm.ComputeHash($stream) | ForEach-Object {
            $_.ToString('X2', [Globalization.CultureInfo]::InvariantCulture)
        }) -join '')
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadRoot,
        [Parameter(Mandatory = $true)][string[]]$Entries,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $stream = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($entryName in $Entries) {
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $source = [System.IO.File]::OpenRead((Join-Path $PayloadRoot $entryName))
                $destinationStream = $entry.Open()
                try {
                    $source.CopyTo($destinationStream)
                }
                finally {
                    $destinationStream.Dispose()
                    $source.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ExactArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$PayloadRoot,
        [Parameter(Mandatory = $true)][string[]]$ExpectedEntries
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -ne $ExpectedEntries.Count) {
            throw "Archive entry count mismatch for $ArchivePath."
        }
        for ($index = 0; $index -lt $ExpectedEntries.Count; $index++) {
            $expectedName = $ExpectedEntries[$index]
            $entry = $archive.Entries[$index]
            if ($entry.FullName -cne $expectedName -or
                $entry.FullName.IndexOf('/') -ge 0 -or
                $entry.FullName.IndexOf('\') -ge 0) {
                throw "Archive root entry mismatch at index $index."
            }

            $entryStream = $entry.Open()
            $sourceStream = [System.IO.File]::OpenRead((Join-Path $PayloadRoot $expectedName))
            $algorithm = [System.Security.Cryptography.SHA256]::Create()
            try {
                if ($entry.Length -ne $sourceStream.Length) {
                    throw "Archive length mismatch for $expectedName."
                }
                $entryHash = (($algorithm.ComputeHash($entryStream) | ForEach-Object {
                    $_.ToString('X2', [Globalization.CultureInfo]::InvariantCulture)
                }) -join '')
                $sourceHash = (($algorithm.ComputeHash($sourceStream) | ForEach-Object {
                    $_.ToString('X2', [Globalization.CultureInfo]::InvariantCulture)
                }) -join '')
                if ($entryHash -cne $sourceHash) {
                    throw "Archive bytes mismatch for $expectedName."
                }
            }
            finally {
                $algorithm.Dispose()
                $sourceStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (-not (Test-ChildPath -Parent $repositoryRoot -Child $moduleRoot) -or
    -not (Test-ChildPath -Parent $artifactModuleRoot -Child $releaseRoot)) {
    throw 'Resolved module or release path is outside its intended root.'
}

$manifestPath = Join-Path $moduleRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]$manifest.name -cne $Module) {
    throw "$Module manifest identity drifted to $($manifest.name)."
}
$version = [string]$manifest.version_number
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "$Module manifest version is not canonical semantic versioning."
}

$iconRelativePath = if ($Module -ceq 'RunicPrecisionBuildTool') {
    'media\icon.png'
}
else {
    'icon.png'
}
$dllPath = Join-Path $moduleRoot ('bin\Release\netstandard2.1\' + $Module + '.dll')
$sources = [ordered]@{
    ($Module + '.dll') = $dllPath
    'manifest.json' = $manifestPath
    'README.md' = (Join-Path $moduleRoot 'README.md')
    'icon.png' = (Join-Path $moduleRoot $iconRelativePath)
    'CHANGELOG.md' = (Join-Path $moduleRoot 'CHANGELOG.md')
    ($Module + '.cfg.example') = (Join-Path $moduleRoot ($Module + '.cfg.example'))
}
foreach ($source in $sources.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $source.Value -PathType Leaf)) {
        throw "$Module package source is missing: $($source.Value)"
    }
}

$identity = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath)
if ($identity.Name -cne $Module -or $identity.Version.ToString() -cne ($version + '.0')) {
    throw "$Module assembly identity $($identity.FullName) does not match package version $version."
}

$icon = [System.Drawing.Image]::FromFile($sources['icon.png'])
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
        throw "$Module icon must be exactly 256x256 pixels."
    }
}
finally {
    $icon.Dispose()
}

$resolvedLiveEvidence = [System.IO.Path]::GetFullPath($LiveEvidencePath)
$resolvedLiveDll = [System.IO.Path]::GetFullPath($LiveDllPath)
if (-not (Test-ChildPath -Parent $artifactModuleRoot -Child $resolvedLiveEvidence) -or
    -not (Test-ChildPath -Parent $artifactModuleRoot -Child $resolvedLiveDll)) {
    throw 'Live evidence must be contained by the module artifact root.'
}
$liveEvidence = Get-Content -LiteralPath $resolvedLiveEvidence -Raw | ConvertFrom-Json
if ([string]$liveEvidence.status -cne 'PASS') {
    throw "$Module live evidence is not PASS."
}
$dllHash = Get-FileSha256Hex -Path $dllPath
$liveDllHash = Get-FileSha256Hex -Path $resolvedLiveDll
if ($dllHash -cne $liveDllHash) {
    throw "$Module current DLL does not match the live-tested DLL."
}

$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot (
    'runic-module-release-' + [Guid]::NewGuid().ToString('N'))))
if (-not (Test-ChildPath -Parent $temporaryRoot -Child $stageRoot) -or
    -not (Split-Path -Leaf $stageRoot).StartsWith(
        'runic-module-release-',
        [System.StringComparison]::Ordinal)) {
    throw "Refusing unsafe package stage: $stageRoot"
}

New-Item -ItemType Directory -Path $stageRoot | Out-Null
try {
    $payloadRoot = Join-Path $stageRoot 'payload'
    New-Item -ItemType Directory -Path $payloadRoot | Out-Null
    foreach ($source in $sources.GetEnumerator()) {
        Copy-Item -LiteralPath $source.Value -Destination (Join-Path $payloadRoot $source.Key)
    }
    $entries = @($sources.Keys)
    $actualEntries = @(Get-ChildItem -LiteralPath $payloadRoot -File | ForEach-Object { $_.Name })
    if ($actualEntries.Count -ne $entries.Count -or
        @($actualEntries | Where-Object { $entries -cnotcontains $_ }).Count -ne 0) {
        throw "$Module package stage is not the exact six-file surface."
    }

    $firstZip = Join-Path $stageRoot 'first.zip'
    $secondZip = Join-Path $stageRoot 'second.zip'
    New-DeterministicZip -PayloadRoot $payloadRoot -Entries $entries -Destination $firstZip
    New-DeterministicZip -PayloadRoot $payloadRoot -Entries $entries -Destination $secondZip
    Assert-ExactArchive -ArchivePath $firstZip -PayloadRoot $payloadRoot -ExpectedEntries $entries
    Assert-ExactArchive -ArchivePath $secondZip -PayloadRoot $payloadRoot -ExpectedEntries $entries
    $firstZipHash = Get-FileSha256Hex -Path $firstZip
    $secondZipHash = Get-FileSha256Hex -Path $secondZip
    if ($firstZipHash -cne $secondZipHash) {
        throw "$Module package bytes are not reproducible."
    }

    if (Test-Path -LiteralPath $releaseRoot) {
        $existing = @(Get-ChildItem -LiteralPath $releaseRoot -Force)
        if ($existing.Count -ne 0) {
            throw "Refusing to overwrite a nonempty frozen release directory: $releaseRoot"
        }
    }
    else {
        New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    }

    $zipName = 'Chazman-' + $Module + '-' + $version + '.zip'
    $releaseZip = Join-Path $releaseRoot $zipName
    Copy-Item -LiteralPath $secondZip -Destination $releaseZip
    if ((Get-FileSha256Hex -Path $releaseZip) -cne $secondZipHash) {
        throw "$Module promoted package hash mismatch."
    }

    $sourceHashes = [ordered]@{}
    foreach ($source in $sources.GetEnumerator()) {
        $sourceHashes[$source.Key] = Get-FileSha256Hex -Path $source.Value
    }
    $releaseEvidence = [ordered]@{
        schema = 'runic-gameplay-module-release/v1'
        status = 'PASS'
        module = $Module
        version = $version
        completed_utc = [DateTime]::UtcNow.ToString(
            'o',
            [Globalization.CultureInfo]::InvariantCulture)
        clean_release_build = $CleanBuildResult
        focused_tests = $FocusedTestResult
        dll_sha256 = $dllHash
        zip_sha256 = $secondZipHash
        zip_entries = $entries
        source_sha256 = $sourceHashes
        dependencies = @($manifest.dependencies)
        live_evidence = $resolvedLiveEvidence
        live_evidence_sha256 = Get-FileSha256Hex -Path $resolvedLiveEvidence
        live_dll = $resolvedLiveDll
        live_dll_sha256 = $liveDllHash
        live_status = 'PASS'
        live_completed_utc = if ($liveEvidence.PSObject.Properties.Name -contains 'completed_utc') {
            [string]$liveEvidence.completed_utc
        }
        else {
            (Get-Item -LiteralPath $resolvedLiveEvidence).LastWriteTimeUtc.ToString(
                'o',
                [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    $evidencePath = Join-Path $releaseRoot 'RELEASE-EVIDENCE.json'
    $evidenceJson = $releaseEvidence | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $evidencePath,
        $evidenceJson + "`n",
        [System.Text.UTF8Encoding]::new($false))

    Write-Output "PACKAGED $releaseZip ZIP_SHA256=$secondZipHash DLL_SHA256=$dllHash"
    Write-Output "EVIDENCE $evidencePath SHA256=$(Get-FileSha256Hex -Path $evidencePath)"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
        if (-not (Test-ChildPath -Parent $temporaryRoot -Child $resolvedStage) -or
            -not (Split-Path -Leaf $resolvedStage).StartsWith(
                'runic-module-release-',
                [System.StringComparison]::Ordinal)) {
            throw "Refusing unsafe package-stage cleanup: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
