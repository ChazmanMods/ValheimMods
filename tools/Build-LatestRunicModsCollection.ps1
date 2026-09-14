[CmdletBinding()]
param(
    [string]$Destination = 'E:\Valheim Mods\Latest Runic Mods'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $repoRoot))
$expectedDestination = [System.IO.Path]::GetFullPath(
    (Join-Path $workspaceRoot 'Latest Runic Mods'))
$destinationRoot = [System.IO.Path]::GetFullPath($Destination)
$fixedTimestamp = [DateTimeOffset]::new(
    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Require {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Write-NewUtf8Text {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    Require (-not (Test-Path -LiteralPath $Path)) (
        "$Path already exists; collection evidence is immutable.")
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Assert-ExactSequence {
    param(
        [Parameter(Mandatory = $true)][object[]]$Actual,
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Require ($Actual.Count -eq $Expected.Count) (
        "$Label count is $($Actual.Count); expected $($Expected.Count).")
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Require ([string]$Actual[$index] -ceq [string]$Expected[$index]) (
            "$Label differs at index ${index}: '$($Actual[$index])' != '$($Expected[$index])'.")
    }
}

function Read-ZipEntryBytes {
    param([Parameter(Mandatory = $true)]$Entry)

    $input = $Entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $input.CopyTo($memory)
        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $input.Dispose()
    }
}

function Assert-Png256Bytes {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $signature = @(137, 80, 78, 71, 13, 10, 26, 10)
    Require ($Bytes.Length -ge 24) "$Label is too short to be a PNG."
    for ($index = 0; $index -lt $signature.Count; $index++) {
        Require ($Bytes[$index] -eq $signature[$index]) (
            "$Label has an invalid PNG signature.")
    }
    $width = ([int]$Bytes[16] -shl 24) -bor ([int]$Bytes[17] -shl 16) -bor
        ([int]$Bytes[18] -shl 8) -bor [int]$Bytes[19]
    $height = ([int]$Bytes[20] -shl 24) -bor ([int]$Bytes[21] -shl 16) -bor
        ([int]$Bytes[22] -shl 8) -bor [int]$Bytes[23]
    Require ($width -eq 256 -and $height -eq 256) (
        "$Label is ${width}x${height}; Thunderstore requires 256x256.")
}

function Assert-ZipPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$ExpectedSha256,
        [string[]]$ExactEntries
    )

    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Missing package: $Path"
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        Require ((Get-Sha256Hex $Path) -ceq $ExpectedSha256) (
            "SHA-256 mismatch for $(Split-Path -Leaf $Path).")
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        Require ($names.Count -gt 0) "$(Split-Path -Leaf $Path) is empty."
        if ($null -ne $ExactEntries -and $ExactEntries.Count -gt 0) {
            Assert-ExactSequence $names $ExactEntries "$(Split-Path -Leaf $Path) entries"
        }
        foreach ($entryName in $names) {
            $normalized = $entryName.Replace('\', '/')
            Require (-not $entryName.Contains('\')) (
                "$(Split-Path -Leaf $Path) contains a backslash path: $entryName")
            Require (-not $normalized.StartsWith('/') -and
                -not $normalized.Contains(':')) (
                "$(Split-Path -Leaf $Path) contains an absolute archive path: $entryName")
            $segments = @($normalized.Split('/') | Where-Object { $_.Length -gt 0 })
            Require (-not ($segments -contains '..')) (
                "$(Split-Path -Leaf $Path) contains path traversal: $entryName")
        }

        $manifestEntry = $archive.GetEntry('manifest.json')
        $iconEntry = $archive.GetEntry('icon.png')
        $readmeEntry = $archive.GetEntry('README.md')
        Require ($null -ne $manifestEntry) "$(Split-Path -Leaf $Path) has no root manifest.json."
        Require ($null -ne $iconEntry) "$(Split-Path -Leaf $Path) has no root icon.png."
        Require ($null -ne $readmeEntry) "$(Split-Path -Leaf $Path) has no root README.md."

        $manifestBytes = Read-ZipEntryBytes $manifestEntry
        $manifestText = $utf8NoBom.GetString($manifestBytes)
        $manifest = $manifestText | ConvertFrom-Json
        Require ([string]$manifest.name -ceq $Name) (
            "$(Split-Path -Leaf $Path) manifest name is '$($manifest.name)', expected '$Name'.")
        Require ([string]$manifest.version_number -ceq $Version) (
            "$(Split-Path -Leaf $Path) manifest version is '$($manifest.version_number)', expected '$Version'.")
        Require (-not [string]::IsNullOrWhiteSpace([string]$manifest.description)) (
            "$(Split-Path -Leaf $Path) has an empty description.")
        Require ([string]$manifest.description.Length -le 250) (
            "$(Split-Path -Leaf $Path) description exceeds 250 characters.")
        Assert-Png256Bytes (Read-ZipEntryBytes $iconEntry) (
            "$(Split-Path -Leaf $Path) icon.png")

        return [pscustomobject]@{
            Dependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
            EntryCount = $names.Count
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Specialized.OrderedDictionary]$Sources,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Require (-not (Test-Path -LiteralPath $Destination)) (
        "$Destination already exists; deterministic package creation never overwrites.")
    $output = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $output,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($entryName in $Sources.Keys) {
                $entry = $archive.CreateEntry(
                    [string]$entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $input = [System.IO.File]::OpenRead([string]$Sources[$entryName])
                $entryStream = $entry.Open()
                try { $input.CopyTo($entryStream) }
                finally {
                    $entryStream.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $output.Dispose() }
}

function New-LocalModulePackage {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string[]]$Dependencies,
        [Parameter(Mandatory = $true)][string]$PackagesRoot
    )

    $moduleRoot = Join-Path $repoRoot $Name
    $dllPath = Join-Path $moduleRoot "bin\Release\netstandard2.1\$Name.dll"
    $sources = [ordered]@{
        "$Name.dll" = $dllPath
        'manifest.json' = (Join-Path $moduleRoot 'manifest.json')
        'README.md' = (Join-Path $moduleRoot 'README.md')
        'icon.png' = (Join-Path $moduleRoot 'icon.png')
        'CHANGELOG.md' = (Join-Path $moduleRoot 'CHANGELOG.md')
        "$Name.cfg.example" = (Join-Path $moduleRoot "$Name.cfg.example")
    }
    foreach ($source in $sources.Values) {
        Require (Test-Path -LiteralPath $source -PathType Leaf) (
            "$Name package source is missing: $source")
    }

    $sourceManifest = Get-Content -LiteralPath $sources['manifest.json'] -Raw |
        ConvertFrom-Json
    Require ([string]$sourceManifest.name -ceq $Name) "$Name source manifest identity drifted."
    Require ([string]$sourceManifest.version_number -ceq $Version) "$Name source version drifted."
    Assert-ExactSequence @($sourceManifest.dependencies | ForEach-Object { [string]$_ }) `
        $Dependencies "$Name source dependencies"
    Assert-Png256Bytes ([System.IO.File]::ReadAllBytes($sources['icon.png'])) "$Name source icon"

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath)
    Require ($assemblyName.Name -ceq $Name) (
        "$Name DLL assembly identity is '$($assemblyName.Name)'.")
    Require ($assemblyName.Version.ToString() -ceq ($Version + '.0')) (
        "$Name DLL version is '$($assemblyName.Version)', expected '$Version.0'.")

    $leaf = "Chazman-$Name-$Version.zip"
    $first = Join-Path $PackagesRoot ('.' + $leaf + '.first')
    $second = Join-Path $PackagesRoot ('.' + $leaf + '.second')
    $final = Join-Path $PackagesRoot $leaf
    New-DeterministicZip $sources $first
    New-DeterministicZip $sources $second
    $firstHash = Get-Sha256Hex $first
    $secondHash = Get-Sha256Hex $second
    Require ($firstHash -ceq $secondHash) "$Name did not package reproducibly."
    $exactEntries = @($sources.Keys | ForEach-Object { [string]$_ })
    $inspection = Assert-ZipPackage $first $Name $Version $firstHash $exactEntries
    Assert-ExactSequence @($inspection.Dependencies) $Dependencies "$Name ZIP dependencies"
    Move-Item -LiteralPath $first -Destination $final
    Remove-Item -LiteralPath $second -Force

    return [pscustomobject]@{
        Name = $Name
        Version = $Version
        File = $leaf
        Path = $final
        Origin = 'Current repository build'
        Sha256 = Get-Sha256Hex $final
        DllSha256 = Get-Sha256Hex $dllPath
        Dependencies = $Dependencies
        EntryCount = $inspection.EntryCount
    }
}

function Copy-ValidatedPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [Parameter(Mandatory = $true)][string]$Origin,
        [Parameter(Mandatory = $true)][string]$PackagesRoot,
        [string[]]$ExactEntries
    )

    $inspection = Assert-ZipPackage $Source $Name $Version $ExpectedSha256 $ExactEntries
    $leaf = "Chazman-$Name-$Version.zip"
    Require ((Split-Path -Leaf $Source) -ceq $leaf) (
        "$Name source filename does not match its manifest identity and version.")
    $destination = Join-Path $PackagesRoot $leaf
    Require (-not (Test-Path -LiteralPath $destination)) (
        "Duplicate package destination: $destination")
    Copy-Item -LiteralPath $Source -Destination $destination
    Require ((Get-Sha256Hex $destination) -ceq $ExpectedSha256) (
        "$Name changed while being copied into the collection stage.")

    return [pscustomobject]@{
        Name = $Name
        Version = $Version
        File = $leaf
        Path = $destination
        Origin = $Origin
        Sha256 = $ExpectedSha256
        DllSha256 = $null
        Dependencies = @($inspection.Dependencies)
        EntryCount = $inspection.EntryCount
    }
}

Require ($destinationRoot -ceq $expectedDestination) (
    "This release builder only writes the requested path: $expectedDestination")
Require (-not (Test-Path -LiteralPath $destinationRoot)) (
    "$destinationRoot already exists. It was not modified; archive or rename it before rebuilding.")

$publishedRoot = Join-Path $repoRoot (
    'artifacts\LatestRunicMods\Candidate-20260909T050330Z-eadb8651')
$publishedPackagesRoot = Join-Path $publishedRoot 'Published'
$publishedMetadataPath = Join-Path $publishedRoot 'PUBLISHED-METADATA.json'
$inventoryRelease = Join-Path $repoRoot (
    'artifacts\RunicInventory\Release\Chazman-RunicInventory-1.0.2.zip')
$suiteReleaseRoot = Join-Path $repoRoot (
    'artifacts\Thunderstore\SuiteVariants\Full-1.2.10_Client-1.0.1_Server-1.0.1-RoleIcons20260909\Packages')
foreach ($requiredPath in @(
    $publishedPackagesRoot, $publishedMetadataPath, $inventoryRelease, $suiteReleaseRoot)) {
    Require (Test-Path -LiteralPath $requiredPath) "Missing release input: $requiredPath"
}

$scratchId = 'Collection-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$scratchRoot = Join-Path $repoRoot "artifacts\LatestRunicMods\$scratchId"
$packagesRoot = Join-Path $scratchRoot 'Packages'
Require (-not (Test-Path -LiteralPath $scratchRoot)) "Scratch collision: $scratchRoot"
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null

$publishedExpected = [ordered]@{
    RunicAgriculture = '1.0.1'
    RunicAwareness = '1.0.1'
    RunicBuildCamera = '1.0.1'
    RunicCrafting = '1.0.1'
    RunicDisplayStands = '1.3.2'
    RunicExploration = '1.0.1'
    RunicInteraction = '1.0.1'
    RunicPrecisionBuildTool = '2.0.2'
    RunicProduction = '1.0.1'
    RunicSafety = '1.0.1'
    RunicStorage = '1.0.2'
    RunicVelocity = '1.0.1'
    RunicWorldEngine = '1.1.1'
}
$publishedMetadata = @(Get-Content -LiteralPath $publishedMetadataPath -Raw |
    ConvertFrom-Json)
Require ($publishedMetadata.Count -eq 13) 'Thunderstore snapshot must contain exactly 13 packages.'
Assert-ExactSequence @($publishedMetadata | ForEach-Object { [string]$_.name } | Sort-Object) `
    @($publishedExpected.Keys | Sort-Object) 'Thunderstore snapshot identities'

$records = @()
foreach ($metadata in $publishedMetadata) {
    $name = [string]$metadata.name
    $version = [string]$metadata.version
    Require ([string]$publishedExpected[$name] -ceq $version) (
        "$name Thunderstore snapshot version drifted to $version.")
    $source = Join-Path $publishedPackagesRoot ([string]$metadata.file)
    $records += Copy-ValidatedPackage `
        -Name $name `
        -Version $version `
        -Source $source `
        -ExpectedSha256 ([string]$metadata.sha256) `
        -Origin 'Thunderstore exact download' `
        -PackagesRoot $packagesRoot
}

$records += Copy-ValidatedPackage `
    -Name 'RunicInventory' `
    -Version '1.0.2' `
    -Source $inventoryRelease `
    -ExpectedSha256 '4934140525CDF2FF914B87C9B2CE4096DD91B4AF0BFAD56D5050101B4D8A0CF3' `
    -Origin 'Local signed release artifact' `
    -PackagesRoot $packagesRoot `
    -ExactEntries @(
        'RunicInventory.dll', 'manifest.json', 'README.md', 'icon.png',
        'CHANGELOG.md', 'RunicInventory.cfg.example')

$bepInEx = @('denikson-BepInExPack_Valheim-5.4.2333')
$sentinelAuthorityDependencies = @(
    'denikson-BepInExPack_Valheim-5.4.2333',
    'Chazman-RunicSafety-1.0.1')
$records += New-LocalModulePackage `
    -Name 'RunicPortals' -Version '1.2.0' -Dependencies $bepInEx -PackagesRoot $packagesRoot
$records += New-LocalModulePackage `
    -Name 'RunicSentinel' -Version '1.3.0' `
    -Dependencies $sentinelAuthorityDependencies -PackagesRoot $packagesRoot
$records += New-LocalModulePackage `
    -Name 'RunicSentinelClient' -Version '1.0.0' `
    -Dependencies $bepInEx -PackagesRoot $packagesRoot
$records += New-LocalModulePackage `
    -Name 'RunicSentinelServer' -Version '1.0.0' `
    -Dependencies $sentinelAuthorityDependencies -PackagesRoot $packagesRoot

$suiteSpecs = @(
    [pscustomobject]@{
        Name = 'RunicModSuite'; Version = '1.2.10'
        Sha256 = '98498484B34336059C975B4BDE8CAEE742B6D2E75CBD51F6ABFBF4A2FD0A17FC'
        Dependencies = @(
            'Chazman-RunicAgriculture-1.0.1', 'Chazman-RunicAwareness-1.0.1',
            'Chazman-RunicBuildCamera-1.0.1', 'Chazman-RunicCrafting-1.0.1',
            'Chazman-RunicDisplayStands-1.3.2', 'Chazman-RunicExploration-1.0.1',
            'Chazman-RunicInteraction-1.0.1', 'Chazman-RunicInventory-1.0.2',
            'Chazman-RunicPortals-1.2.0', 'Chazman-RunicPrecisionBuildTool-2.0.2',
            'Chazman-RunicProduction-1.0.1', 'Chazman-RunicSafety-1.0.1',
            'Chazman-RunicSentinel-1.3.0', 'Chazman-RunicStorage-1.0.2',
            'Chazman-RunicVelocity-1.0.1', 'Chazman-RunicWorldEngine-1.1.1',
            'shudnal-ConfigurationManager-1.1.17')
    }
    [pscustomobject]@{
        Name = 'RunicModClientSuite'; Version = '1.0.1'
        Sha256 = '6581EA4244D80E8D445FCAF4304E2F334C853FCF5C55B034D5ECDD73C3A1233E'
        Dependencies = @(
            'Chazman-RunicAgriculture-1.0.1', 'Chazman-RunicAwareness-1.0.1',
            'Chazman-RunicBuildCamera-1.0.1', 'Chazman-RunicCrafting-1.0.1',
            'Chazman-RunicDisplayStands-1.3.2', 'Chazman-RunicExploration-1.0.1',
            'Chazman-RunicInteraction-1.0.1', 'Chazman-RunicInventory-1.0.2',
            'Chazman-RunicPortals-1.2.0', 'Chazman-RunicPrecisionBuildTool-2.0.2',
            'Chazman-RunicProduction-1.0.1', 'Chazman-RunicSafety-1.0.1',
            'Chazman-RunicSentinelClient-1.0.0', 'Chazman-RunicStorage-1.0.2',
            'Chazman-RunicVelocity-1.0.1', 'Chazman-RunicWorldEngine-1.1.1',
            'shudnal-ConfigurationManager-1.1.17')
    }
    [pscustomobject]@{
        Name = 'RunicModServerSuite'; Version = '1.0.1'
        Sha256 = 'E080D5786E0FD56BAE7F3AD62F7B9A6F229D3837DC06E491E580EB266746CBBB'
        Dependencies = @(
            'Chazman-RunicDisplayStands-1.3.2', 'Chazman-RunicPortals-1.2.0',
            'Chazman-RunicProduction-1.0.1', 'Chazman-RunicSafety-1.0.1',
            'Chazman-RunicSentinelServer-1.0.0', 'Chazman-RunicVelocity-1.0.1',
            'Chazman-RunicWorldEngine-1.1.1')
    }
)
$suiteEntries = @('CHANGELOG.md', 'icon.png', 'manifest.json', 'README.md')
foreach ($suite in $suiteSpecs) {
    $source = Join-Path $suiteReleaseRoot (
        "Chazman-$($suite.Name)-$($suite.Version).zip")
    $record = Copy-ValidatedPackage `
        -Name $suite.Name `
        -Version $suite.Version `
        -Source $source `
        -ExpectedSha256 $suite.Sha256 `
        -Origin 'Local suite release artifact' `
        -PackagesRoot $packagesRoot `
        -ExactEntries $suiteEntries
    Assert-ExactSequence @($record.Dependencies) @($suite.Dependencies) (
        "$($suite.Name) dependency pins")
    $records += $record
}

$expectedIdentities = @(
    'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCrafting',
    'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
    'RunicModClientSuite', 'RunicModServerSuite', 'RunicModSuite', 'RunicPortals',
    'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety', 'RunicSentinel',
    'RunicSentinelClient', 'RunicSentinelServer', 'RunicStorage', 'RunicVelocity',
    'RunicWorldEngine') | Sort-Object
$actualIdentities = @($records | ForEach-Object { $_.Name } | Sort-Object)
Assert-ExactSequence $actualIdentities $expectedIdentities 'collection package identities'
Require ($records.Count -eq 21) 'The final collection must contain exactly 21 unique ZIP packages.'

$canonicalIdentities = @(
    'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCrafting',
    'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
    'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
    'RunicSentinel', 'RunicStorage', 'RunicVelocity', 'RunicWorldEngine') | Sort-Object
$actualCanonical = @($records | Where-Object {
    $_.Name -notmatch '^RunicMod' -and $_.Name -notmatch '^RunicSentinel(Client|Server)$'
} | ForEach-Object { $_.Name } | Sort-Object)
Assert-ExactSequence $actualCanonical $canonicalIdentities 'canonical 16 Runic mods'

$packageVersions = @{}
foreach ($record in $records) { $packageVersions[$record.Name] = $record.Version }
foreach ($suite in $suiteSpecs) {
    foreach ($dependency in $suite.Dependencies) {
        if ($dependency -notmatch '^Chazman-(?<name>Runic.+)-(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') {
            continue
        }
        Require ($packageVersions.ContainsKey($Matches.name)) (
            "$($suite.Name) references missing collection package $($Matches.name).")
        Require ([string]$packageVersions[$Matches.name] -ceq $Matches.version) (
            "$($suite.Name) pins $($Matches.name) $($Matches.version), but the collection has $($packageVersions[$Matches.name]).")
    }
}
$dependencyText = (@($suiteSpecs | ForEach-Object { $_.Dependencies }) -join "`n")
Require ($dependencyText -notmatch 'RunicInventory-1\.0\.1|RunicPortals-1\.1\.4|RunicSentinel-1\.2\.5') (
    'A suite still contains a known stale dependency pin.')

$finalStageRoot = Join-Path $workspaceRoot (
    '.Latest Runic Mods.stage-' + [Guid]::NewGuid().ToString('N'))
Require ([System.IO.Path]::GetFullPath((Split-Path -Parent $finalStageRoot)) -ceq
    $workspaceRoot) 'Final collection stage escaped the intended workspace.'
Require (-not (Test-Path -LiteralPath $finalStageRoot)) (
    "Final collection staging path already exists: $finalStageRoot")
New-Item -ItemType Directory -Path $finalStageRoot | Out-Null
foreach ($record in $records) {
    $destination = Join-Path $finalStageRoot $record.File
    Copy-Item -LiteralPath $record.Path -Destination $destination
    Require ((Get-Sha256Hex $destination) -ceq $record.Sha256) (
        "$($record.File) changed while being copied to the requested directory.")
}

$checksumLines = @($records | Sort-Object File | ForEach-Object {
    $_.Sha256 + '  ' + $_.File
})
Write-NewUtf8Text `
    -Path (Join-Path $finalStageRoot 'SHA256SUMS.txt') `
    -Content (($checksumLines -join "`n") + "`n")

$readme = @'
# Latest Runic Mods

This folder is the complete 2026-09-09 Runic package set: the 16 canonical Runic mods, all three Sentinel forms, and all three Mod Suite forms. There are 21 unique Thunderstore ZIPs; Full variants are included once under their normal package names rather than duplicated.

## Choose one suite

- `RunicModSuite 1.2.10` — Full: all 16 canonical mods with combined RunicSentinel 1.3.0. Best for solo games, listen hosts, or an all-in-one admin profile.
- `RunicModClientSuite 1.0.1` — Client: the 15 shared player mods plus RunicSentinelClient 1.0.0 and Configuration Manager.
- `RunicModServerSuite 1.0.1` — Server: the seven server-relevant packages, including RunicSentinelServer 1.0.0 and no client GUI.

Never install Full Sentinel and Sentinel Server together; those two authority packages explicitly reject one another. Sentinel Client may coexist with Full Sentinel in a remote administrator profile, or with Sentinel Server on a listen host.

## Canonical 16

| Mod | Version |
|---|---:|
| RunicAgriculture | 1.0.1 |
| RunicAwareness | 1.0.1 |
| RunicBuildCamera | 1.0.1 |
| RunicCrafting | 1.0.1 |
| RunicDisplayStands | 1.3.2 |
| RunicExploration | 1.0.1 |
| RunicInteraction | 1.0.1 |
| RunicInventory | 1.0.2 |
| RunicPortals | 1.2.0 |
| RunicPrecisionBuildTool | 2.0.2 |
| RunicProduction | 1.0.1 |
| RunicSafety | 1.0.1 |
| RunicSentinel (Full) | 1.3.0 |
| RunicStorage | 1.0.2 |
| RunicVelocity | 1.0.1 |
| RunicWorldEngine | 1.1.1 |

## Extra variants

| Package | Version |
|---|---:|
| RunicSentinelClient | 1.0.0 |
| RunicSentinelServer | 1.0.0 |
| RunicModClientSuite | 1.0.1 |
| RunicModServerSuite | 1.0.1 |
| RunicModSuite (Full) | 1.2.10 |

Client packages use a cool-blue connected-player badge. Server packages use a gold-and-ember
fortified-network badge, making the role immediately visible without changing the Full icons.

Thirteen unchanged mod ZIPs are byte-for-byte Thunderstore downloads. RunicInventory 1.0.2 is the signed local release artifact. RunicPortals 1.2.0 and all three Sentinel forms use the current tested repository builds. The three suite ZIPs are deterministic local release artifacts and pin the exact versions in this folder.

Automated build, focused tests, archive integrity, manifest identity, icon size, version, checksum, and suite dependency checks passed. A dedicated-server boot smoke with the exact RunicSentinelServer DLL also passed: BepInEx loaded it, authority services started, the world loaded, and Steam connected without Sentinel or BepInEx errors. No Thunderstore upload was performed. A separate live RunicSentinelClient connection in Required mode is still recommended before first publication of the new server package.

Use `SHA256SUMS.txt` to verify every ZIP. `COLLECTION-EVIDENCE.json` records provenance, dependencies, test totals, and hashes.
'@
Write-NewUtf8Text `
    -Path (Join-Path $finalStageRoot 'README.md') `
    -Content ($readme.TrimStart() + "`n")

$packageEvidence = @($records | Sort-Object Name | ForEach-Object {
    [ordered]@{
        name = $_.Name
        version = $_.Version
        file = $_.File
        origin = $_.Origin
        sha256 = $_.Sha256
        dll_sha256 = $_.DllSha256
        dependency_count = $_.Dependencies.Count
        dependencies = @($_.Dependencies)
        zip_entry_count = $_.EntryCount
    }
})
$evidence = [ordered]@{
    schema = 'latest-runic-mods-collection/v1'
    status = 'PASS'
    created_utc = [DateTime]::UtcNow.ToString('o')
    destination = $destinationRoot
    canonical_runic_mod_count = 16
    unique_zip_count = 21
    sentinel_forms = @('RunicSentinel', 'RunicSentinelClient', 'RunicSentinelServer')
    suite_forms = @('RunicModSuite', 'RunicModClientSuite', 'RunicModServerSuite')
    thunderstore_exact_download_count = 13
    thunderstore_snapshot_sha256 = Get-Sha256Hex $publishedMetadataPath
    thunderstore_snapshot_date = '2026-09-09'
    thunderstore_upload_performed = $false
    live_valheim_pairing_performed_for_new_server_variant = $false
    dedicated_server_smoke = [ordered]@{
        status = 'PASS'
        exact_server_dll_sha256 = '358E11000E98373B1DC7DAF2ACC68D7B8D2D25DA46163CD9C4CE7CFAA49BB6F3'
        bepinex_loaded = $true
        authority_started = $true
        world_loaded = $true
        steam_game_server_connected = $true
        sentinel_or_bepinex_errors = 0
        evidence_directory = 'E:\Valheim Mods\ChazmanModsRepo\artifacts\RunicSentinelServer\LiveSmoke-1.0.0\20260909T052137Z-938bf795'
    }
    role_icon_refresh = [ordered]@{
        status = 'PASS'
        created_date = '2026-09-09'
        generation_mode = 'built-in image generation edit'
        full_icons_changed = $false
        artifact_directory = 'E:\Valheim Mods\ChazmanModsRepo\artifacts\IconVariants\ClientServerRoleIcons-20260909-Final'
        icons = [ordered]@{
            RunicSentinelClient = 'C1AD577906E98B1726308A4AFB069FA1F358BB59C2289CDEA42299B7512A0678'
            RunicSentinelServer = '5CEB659608F3343C01CF64E8B7336F26640D96B14C9ECE44FBB08A2D1E2588C6'
            RunicModClientSuite = '59A54AF15E869085FFA3CABA4DF34EEBDF0E61F714E8D71EA8E6E6B667D84A95'
            RunicModServerSuite = '93DBDEF685E615F3450AC679BC9A70EC4FA0F87D336383D35F6831C6A298403F'
        }
    }
    automated_tests = [ordered]@{
        RunicPortals = '34/34 PASS'
        RunicSentinel = '30/30 PASS'
        RunicSentinelClient = '10/10 PASS'
        RunicSentinelServer = '16/16 PASS'
    }
    packages = $packageEvidence
    checks = @(
        'all 16 canonical Runic identities are present exactly once'
        'all three Sentinel forms are present with distinct package identities'
        'all three suite forms are present and pin collection versions exactly'
        'known stale Inventory, Portals, and Sentinel dependency pins are absent'
        'every ZIP has safe paths and exact manifest identity/version'
        'every ZIP has a 256x256 root icon and a root README'
        'fresh local packages reproduced byte-for-byte across two builds'
        'all copied ZIP hashes match their validated inputs'
    )
}
$evidenceJson = $evidence | ConvertTo-Json -Depth 10
Write-NewUtf8Text `
    -Path (Join-Path $finalStageRoot 'COLLECTION-EVIDENCE.json') `
    -Content ($evidenceJson + "`n")

$actualZipNames = @(Get-ChildItem -LiteralPath $finalStageRoot -File -Filter '*.zip' |
    ForEach-Object { $_.Name } | Sort-Object)
$expectedZipNames = @($records | ForEach-Object { $_.File } | Sort-Object)
Assert-ExactSequence $actualZipNames $expectedZipNames 'final ZIP filenames'
Require ($actualZipNames.Count -eq 21) 'Final directory does not contain exactly 21 ZIPs.'
foreach ($record in $records) {
    $finalPath = Join-Path $finalStageRoot $record.File
    Require ((Get-Sha256Hex $finalPath) -ceq $record.Sha256) (
        "Final checksum mismatch: $($record.File)")
    $null = Assert-ZipPackage $finalPath $record.Name $record.Version $record.Sha256
}
$topLevelFiles = @(Get-ChildItem -LiteralPath $finalStageRoot -File)
Require ($topLevelFiles.Count -eq 24) (
    "Final directory contains $($topLevelFiles.Count) files; expected 24.")

Require (-not (Test-Path -LiteralPath $destinationRoot)) (
    "$destinationRoot appeared while the collection was staged; it was not modified.")
Move-Item -LiteralPath $finalStageRoot -Destination $destinationRoot
Require (Test-Path -LiteralPath $destinationRoot -PathType Container) (
    'The validated collection could not be moved into the requested destination.')
foreach ($record in $records) {
    Require ((Get-Sha256Hex (Join-Path $destinationRoot $record.File)) -ceq $record.Sha256) (
        "Post-move checksum mismatch: $($record.File)")
}

[pscustomobject]@{
    Status = 'PASS'
    Destination = $destinationRoot
    CanonicalMods = 16
    SentinelForms = 3
    SuiteForms = 3
    UniqueZips = $actualZipNames.Count
    TopLevelFiles = $topLevelFiles.Count
    ScratchEvidence = $scratchRoot
} | Format-List
