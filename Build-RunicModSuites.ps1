[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseId = 'Full-1.2.27_Client-1.0.17_Server-1.0.13-Valheim10'
$releaseRoot = Join-Path $repoRoot "artifacts\Thunderstore\SuiteVariants\$releaseId"
$packagesRoot = Join-Path $releaseRoot 'Packages'
$stagingRoot = Join-Path $releaseRoot 'Staging'
$entryNames = @('CHANGELOG.md', 'icon.png', 'manifest.json', 'README.md')
$timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

$specs = @(
    [pscustomobject]@{
        Directory = 'RunicModSuite'
        Name = 'RunicModSuite'
        Version = '1.2.27'
        Role = 'Full'
        Dependencies = @(
            'Chazman-RunicAgriculture-1.0.3'
            'Chazman-RunicAwareness-1.0.2'
            'Chazman-RunicBuildCamera-1.0.3'
            'Chazman-RunicCharacterVault-1.0.2'
            'Chazman-RunicCrafting-1.0.6'
            'Chazman-RunicDisplayStands-1.3.4'
            'Chazman-RunicExploration-1.0.2'
            'Chazman-RunicInteraction-1.0.4'
            'Chazman-RunicInventory-1.1.1'
            'Chazman-RunicPortals-1.2.4'
            'Chazman-RunicPrecisionBuildTool-2.0.4'
            'Chazman-RunicProduction-1.0.5'
            'Chazman-RunicSafety-1.0.4'
            'Chazman-RunicSentinel-1.3.2'
            'Chazman-RunicStorage-1.0.6'
            'Chazman-RunicVelocity-1.0.2'
            'Chazman-RunicWorldEngine-1.1.2'
            'shudnal-ConfigurationManager-1.1.17'
        )
    }
    [pscustomobject]@{
        Directory = 'RunicModClientSuite'
        Name = 'RunicModClientSuite'
        Version = '1.0.17'
        Role = 'Client'
        Dependencies = @(
            'Chazman-RunicAgriculture-1.0.3'
            'Chazman-RunicAwareness-1.0.2'
            'Chazman-RunicBuildCamera-1.0.3'
            'Chazman-RunicCharacterVault-1.0.2'
            'Chazman-RunicCrafting-1.0.6'
            'Chazman-RunicDisplayStands-1.3.4'
            'Chazman-RunicExploration-1.0.2'
            'Chazman-RunicInteraction-1.0.4'
            'Chazman-RunicInventory-1.1.1'
            'Chazman-RunicPortals-1.2.4'
            'Chazman-RunicPrecisionBuildTool-2.0.4'
            'Chazman-RunicProduction-1.0.5'
            'Chazman-RunicSafety-1.0.4'
            'Chazman-RunicSentinelClient-1.0.1'
            'Chazman-RunicStorage-1.0.6'
            'Chazman-RunicVelocity-1.0.2'
            'Chazman-RunicWorldEngine-1.1.2'
            'shudnal-ConfigurationManager-1.1.17'
        )
    }
    [pscustomobject]@{
        Directory = 'RunicModServerSuite'
        Name = 'RunicModServerSuite'
        Version = '1.0.13'
        Role = 'Server'
        Dependencies = @(
            'Chazman-RunicCharacterVault-1.0.2'
            'Chazman-RunicDisplayStands-1.3.4'
            'Chazman-RunicPortals-1.2.4'
            'Chazman-RunicProduction-1.0.5'
            'Chazman-RunicSafety-1.0.4'
            'Chazman-RunicSentinelServer-1.0.2'
            'Chazman-RunicVelocity-1.0.2'
            'Chazman-RunicWorldEngine-1.1.2'
        )
    }
)

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
        "$Path already exists; release evidence is immutable.")
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $writer = [System.IO.StreamWriter]::new($stream, $encoding)
        try { $writer.Write($Content) }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
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

function Assert-Utf8WithoutBom {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    Require (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) (
        "$Path must be UTF-8 without a byte-order mark.")
    $decoder = [System.Text.UTF8Encoding]::new($false, $true)
    try { $null = $decoder.GetString($bytes) }
    catch { throw "$Path is not strict UTF-8: $($_.Exception.Message)" }
}

function Assert-Icon {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $signature = @(137, 80, 78, 71, 13, 10, 26, 10)
    Require ($bytes.Length -ge 24) "$Path is too short to be a PNG."
    for ($index = 0; $index -lt $signature.Count; $index++) {
        Require ($bytes[$index] -eq $signature[$index]) "$Path has an invalid PNG signature."
    }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor
        ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor
        ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    Require ($width -eq 256 -and $height -eq 256) (
        "$Path is ${width}x${height}; Thunderstore requires 256x256.")
}

function Assert-SourcePackage {
    param([Parameter(Mandatory = $true)]$Spec)
    $sourceRoot = Join-Path $repoRoot $Spec.Directory
    Require (Test-Path -LiteralPath $sourceRoot -PathType Container) (
        "Missing package source directory: $sourceRoot")
    $actualEntries = @(Get-ChildItem -LiteralPath $sourceRoot -File |
        ForEach-Object { $_.Name } | Sort-Object)
    Assert-ExactSequence $actualEntries $entryNames "$($Spec.Name) source entries"
    Require (@(Get-ChildItem -LiteralPath $sourceRoot -Directory).Count -eq 0) (
        "$($Spec.Name) source may not contain nested directories.")

    foreach ($textName in @('CHANGELOG.md', 'manifest.json', 'README.md')) {
        Assert-Utf8WithoutBom (Join-Path $sourceRoot $textName)
    }
    Assert-Icon (Join-Path $sourceRoot 'icon.png')

    $manifest = Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Raw |
        ConvertFrom-Json
    Require ([string]$manifest.name -ceq $Spec.Name) (
        "$($Spec.Name) manifest identity differs from its package name.")
    Require ([string]$manifest.version_number -ceq $Spec.Version) (
        "$($Spec.Name) version differs from the release specification.")
    Require ([string]$manifest.version_number -match '^[0-9]+\.[0-9]+\.[0-9]+$') (
        "$($Spec.Name) version is not three-part semantic versioning.")
    Require (-not [string]::IsNullOrWhiteSpace([string]$manifest.description)) (
        "$($Spec.Name) description is empty.")
    Require ([string]$manifest.description.Length -le 250) (
        "$($Spec.Name) description exceeds 250 characters.")
    $dependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
    Assert-ExactSequence $dependencies $Spec.Dependencies "$($Spec.Name) dependencies"
    Require (($dependencies | Sort-Object -Unique).Count -eq $dependencies.Count) (
        "$($Spec.Name) dependencies are not unique.")
    Require (@($dependencies | Where-Object {
        $_ -match '^Chazman-Runic(Core|Permissions|Persistence|Transactions)-'
    }).Count -eq 0) "$($Spec.Name) includes a retired Foundation dependency."
}

function Copy-StagePackage {
    param([Parameter(Mandatory = $true)]$Spec)
    $sourceRoot = Join-Path $repoRoot $Spec.Directory
    $stageRoot = Join-Path $stagingRoot $Spec.Directory
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $actualEntries = @(Get-ChildItem -LiteralPath $stageRoot -File |
        ForEach-Object { $_.Name } | Sort-Object)
    if ($actualEntries.Count -gt 0) {
        Assert-ExactSequence $actualEntries $entryNames "$($Spec.Name) existing stage entries"
    }
    foreach ($entryName in $entryNames) {
        $source = Join-Path $sourceRoot $entryName
        $destination = Join-Path $stageRoot $entryName
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            Require ((Get-Sha256Hex $source) -ceq (Get-Sha256Hex $destination)) (
                "$destination differs from source; archive the old stage before rebuilding.")
        }
        else {
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
    return $stageRoot
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    Add-Type -AssemblyName System.IO.Compression
    $stream = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($entryName in $entryNames) {
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $input = [System.IO.File]::OpenRead((Join-Path $SourceRoot $entryName))
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-Zip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        Assert-ExactSequence $names $entryNames "$(Split-Path -Leaf $ZipPath) entries"
        foreach ($entry in $archive.Entries) {
            Require ($entry.FullName.IndexOfAny([char[]]@('/', '\')) -lt 0) (
                "$($entry.FullName) is not a root archive entry.")
            Require ($entry.LastWriteTime.DateTime -eq
                [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)) (
                "$($entry.FullName) has a non-deterministic ZIP timestamp.")
            $entryStream = $entry.Open()
            $algorithm = [System.Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = (($algorithm.ComputeHash($entryStream) |
                    ForEach-Object { $_.ToString('X2') }) -join '')
            }
            finally {
                $algorithm.Dispose()
                $entryStream.Dispose()
            }
            Require ($entryHash -ceq (Get-Sha256Hex (Join-Path $SourceRoot $entry.FullName))) (
                "$($entry.FullName) differs from its staged source.")
        }
    }
    finally { $archive.Dispose() }
}

foreach ($spec in $specs) { Assert-SourcePackage $spec }

$fullDependencies = @($specs[0].Dependencies)
$clientDependencies = @($specs[1].Dependencies)
$serverDependencies = @($specs[2].Dependencies)
Require ($fullDependencies -ccontains 'Chazman-RunicSentinel-1.3.2') (
    'Full Suite must install combined Runic Sentinel.')
Require ($fullDependencies -cnotcontains 'Chazman-RunicSentinelClient-1.0.1') (
    'Full Suite may not install Runic Sentinel Client separately.')
Require ($fullDependencies -cnotcontains 'Chazman-RunicSentinelServer-1.0.2') (
    'Full Suite may not install Runic Sentinel Server separately.')
Require ($clientDependencies -cnotcontains 'Chazman-RunicSentinel-1.3.2') (
    'Client Suite may not install combined Runic Sentinel.')
Require ($clientDependencies -ccontains 'Chazman-RunicSentinelClient-1.0.1') (
    'Client Suite must install Runic Sentinel Client.')
Require ($clientDependencies -cnotcontains 'Chazman-RunicSentinelServer-1.0.2') (
    'Client Suite may not install Runic Sentinel Server.')
Require ($serverDependencies -ccontains 'Chazman-RunicSentinelServer-1.0.2') (
    'Server Suite must install Runic Sentinel Server.')
Require ($serverDependencies -cnotcontains 'Chazman-RunicSentinelClient-1.0.1') (
    'Server Suite may not install Runic Sentinel Client.')
Require ($serverDependencies -cnotcontains 'Chazman-RunicSentinel-1.3.2') (
    'Server Suite may not install combined Runic Sentinel.')
Require ($fullDependencies -ccontains 'shudnal-ConfigurationManager-1.1.17') (
    'Full Suite must include Configuration Manager.')
Require ($clientDependencies -ccontains 'shudnal-ConfigurationManager-1.1.17') (
    'Client Suite must include Configuration Manager.')
Require ($serverDependencies -cnotcontains 'shudnal-ConfigurationManager-1.1.17') (
    'Server Suite may not install Configuration Manager.')
foreach ($dependencies in @($fullDependencies, $clientDependencies, $serverDependencies)) {
    Require ($dependencies -ccontains 'Chazman-RunicCharacterVault-1.0.2') (
        'Every suite role must install Runic Character Vault on its participating process.')
}

$runicPattern = '^Chazman-(?<name>Runic.+)-[0-9]+\.[0-9]+\.[0-9]+$'
$allRunic = @($fullDependencies + $clientDependencies + $serverDependencies | ForEach-Object {
    if ($_ -match $runicPattern) { $Matches.name }
} | Sort-Object -Unique)
$expectedRunic = @(
    'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCharacterVault', 'RunicCrafting',
    'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
    'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
    'RunicSentinel', 'RunicSentinelClient', 'RunicSentinelServer', 'RunicStorage',
    'RunicVelocity', 'RunicWorldEngine'
) | Sort-Object
Assert-ExactSequence $allRunic $expectedRunic 'combined Runic package coverage'

$sentinelPattern = '^Chazman-RunicSentinel(?:Client|Server)?-[0-9]+\.[0-9]+\.[0-9]+$'
foreach ($spec in $specs) {
    $sentinelDependencies = @($spec.Dependencies | Where-Object { $_ -match $sentinelPattern })
    Require ($sentinelDependencies.Count -eq 1) (
        "$($spec.Name) must contain exactly one Sentinel package identity.")
}

Require (-not (Test-Path -LiteralPath $releaseRoot)) (
    "$releaseRoot already exists. This builder never overwrites or reuses a prior release root.")
New-Item -ItemType Directory -Path (Split-Path -Parent $releaseRoot) -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
New-Item -ItemType Directory -Path $packagesRoot | Out-Null
$results = @()
foreach ($spec in $specs) {
    $stageRoot = Copy-StagePackage $spec
    $leaf = "Chazman-$($spec.Name)-$($spec.Version).zip"
    $final = Join-Path $packagesRoot $leaf
    $first = Join-Path $packagesRoot ('.' + $leaf + '.' + [Guid]::NewGuid().ToString('N') + '.first')
    $second = Join-Path $packagesRoot ('.' + $leaf + '.' + [Guid]::NewGuid().ToString('N') + '.second')
    try {
        New-DeterministicZip $stageRoot $first
        New-DeterministicZip $stageRoot $second
        $firstHash = Get-Sha256Hex $first
        $secondHash = Get-Sha256Hex $second
        Require ($firstHash -ceq $secondHash) "$leaf did not build reproducibly."
        Assert-Zip $stageRoot $first
        Require (-not (Test-Path -LiteralPath $final)) (
            "$final already exists; this release builder never overwrites package artifacts.")
        Move-Item -LiteralPath $first -Destination $final
        Assert-Zip $stageRoot $final
        $results += [pscustomobject]@{
            Name = $spec.Name
            Version = $spec.Version
            Role = $spec.Role
            Dependencies = $spec.Dependencies.Count
            Path = $final
            Sha256 = Get-Sha256Hex $final
        }
    }
    finally {
        foreach ($temporary in @($first, $second)) {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }
}

$checksumLines = @($results | Sort-Object Name | ForEach-Object {
    $_.Sha256 + '  ' + [System.IO.Path]::GetFileName($_.Path)
})
Write-NewUtf8Text `
    -Path (Join-Path $releaseRoot 'SHA256SUMS.txt') `
    -Content (($checksumLines -join "`n") + "`n")

$signoff = @(
    '# Runic Mod Suite Variants'
    ''
    '- Status: PASS'
    '- Release date: 2026-09-10'
    '- Full Suite 1.2.27: all 17 canonical Runic mods, combined Sentinel 1.3.2, and Configuration Manager 1.1.17.'
    '- Client Suite 1.0.17: all 17 canonical Runic mods, Sentinel Client 1.0.1, and Configuration Manager 1.1.17.'
    '- Server Suite 1.0.13: 8 server-relevant Runic mods and Sentinel Server 1.0.2; Configuration Manager absent.'
    '- Sentinel separation: each suite contains exactly one of the combined, client-only, or server-only Sentinel identities.'
    '- Combined coverage: all 16 shared Runic package identities and all 3 Sentinel package identities were validated.'
    '- Client Suite: 17 Runic mods plus Configuration Manager; lightweight Sentinel Client present and full Sentinel absent.'
    '- Archive shape: exactly four deterministic root entries per package.'
    '- Reproducibility: two independent archives matched byte-for-byte for each package.'
    '- Publication: this builder created local artifacts only and did not upload anything to Thunderstore.'
) -join "`n"
Write-NewUtf8Text `
    -Path (Join-Path $releaseRoot 'RELEASE-SIGNOFF.md') `
    -Content ($signoff + "`n")

$packageEvidence = @($results | Sort-Object Name | ForEach-Object {
    [ordered]@{
        name = $_.Name
        version = $_.Version
        role = $_.Role
        dependency_count = $_.Dependencies
        file = [System.IO.Path]::GetFileName($_.Path)
        sha256 = $_.Sha256
    }
})
$signoffEvidence = [ordered]@{
    schema = 'runic-mod-suite-variants-release/v1'
    status = 'PASS'
    release_date = '2026-09-10'
    release_id = $releaseId
    thunderstore_upload_performed = $false
    shared_runic_identity_count = 16
    sentinel_identities = @('RunicSentinel', 'RunicSentinelClient', 'RunicSentinelServer')
    package_count = $results.Count
    packages = $packageEvidence
    checks = @(
        'source manifests matched the immutable release specification'
        'each suite contained exactly one matching Sentinel identity'
        'Configuration Manager was present only in Full and Client suites'
        'all package sources and ZIPs contained exactly four root entries'
        'two independent builds of every ZIP matched byte-for-byte'
        'ZIP entry bytes matched staged source bytes'
        'all earlier artifact directories were left untouched'
    )
}
$signoffJson = $signoffEvidence | ConvertTo-Json -Depth 8
Write-NewUtf8Text `
    -Path (Join-Path $releaseRoot 'RELEASE-SIGNOFF.json') `
    -Content ($signoffJson + "`n")

$results | Format-Table Name, Version, Role, Dependencies, Sha256, Path -AutoSize
