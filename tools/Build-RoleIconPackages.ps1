[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot (
    'artifacts\IconVariants\ClientServerRoleIcons-20260909-Final')
$packagesRoot = Join-Path $artifactRoot 'Packages'
$previousRoot = Join-Path $artifactRoot 'PreviousPackages'
$collectionRoot = [System.IO.Path]::GetFullPath(
    (Join-Path (Split-Path -Parent $repoRoot) 'Latest Runic Mods'))
$suitePackagesRoot = Join-Path $repoRoot (
    'artifacts\Thunderstore\SuiteVariants\' +
    'Full-1.2.10_Client-1.0.1_Server-1.0.1-RoleIcons20260909\Packages')
$timestamp = [DateTimeOffset]::new(
    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

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

function Get-StreamSha256Hex {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($algorithm.ComputeHash($Stream) | ForEach-Object {
            $_.ToString('X2')
        }) -join '')
    }
    finally { $algorithm.Dispose() }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Specialized.OrderedDictionary]$Sources,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Require (-not (Test-Path -LiteralPath $Destination)) (
        "$Destination already exists; refusing to overwrite it.")
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
                $entry.LastWriteTime = $timestamp
                $input = [System.IO.File]::OpenRead([string]$Sources[$entryName])
                $entryOutput = $entry.Open()
                try { $input.CopyTo($entryOutput) }
                finally {
                    $entryOutput.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $output.Dispose() }
}

function Assert-ExactZip {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Collections.Specialized.OrderedDictionary]$Sources
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries)
        $expectedNames = @($Sources.Keys | ForEach-Object { [string]$_ })
        Require ($entries.Count -eq $expectedNames.Count) (
            "$Path contains $($entries.Count) entries; expected $($expectedNames.Count).")
        for ($index = 0; $index -lt $expectedNames.Count; $index++) {
            $entry = $entries[$index]
            $expectedName = $expectedNames[$index]
            Require ($entry.FullName -ceq $expectedName) (
                "$Path entry $index is '$($entry.FullName)', expected '$expectedName'.")
            Require ($entry.FullName.IndexOfAny([char[]]@('/', '\')) -lt 0) (
                "$Path contains a non-root entry: $($entry.FullName)")
            $input = $entry.Open()
            try { $actualHash = Get-StreamSha256Hex $input }
            finally { $input.Dispose() }
            Require ($actualHash -ceq (Get-Sha256Hex ([string]$Sources[$expectedName]))) (
                "$Path entry bytes differ from source: $expectedName")
        }
    }
    finally { $archive.Dispose() }
}

Require (Test-Path -LiteralPath $artifactRoot -PathType Container) (
    "Missing icon artifact root: $artifactRoot")
Require (Test-Path -LiteralPath $collectionRoot -PathType Container) (
    "Missing collection root: $collectionRoot")
Require (Test-Path -LiteralPath $suitePackagesRoot -PathType Container) (
    "Missing role-icon suite packages: $suitePackagesRoot")
Require (-not (Test-Path -LiteralPath $packagesRoot)) (
    "$packagesRoot already exists; role-icon package evidence is immutable.")
Require (-not (Test-Path -LiteralPath $previousRoot)) (
    "$previousRoot already exists; previous-package evidence is immutable.")
New-Item -ItemType Directory -Path $packagesRoot, $previousRoot | Out-Null

$oldLeaves = @(
    'Chazman-RunicSentinelClient-1.0.0.zip'
    'Chazman-RunicSentinelServer-1.0.0.zip'
    'Chazman-RunicModClientSuite-1.0.1.zip'
    'Chazman-RunicModServerSuite-1.0.1.zip'
)
foreach ($leaf in $oldLeaves) {
    $source = Join-Path $collectionRoot $leaf
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Missing previous package: $source"
    Copy-Item -LiteralPath $source -Destination (Join-Path $previousRoot $leaf)
}

$results = @()
foreach ($name in @('RunicSentinelClient', 'RunicSentinelServer')) {
    $moduleRoot = Join-Path $repoRoot $name
    $manifest = Get-Content -LiteralPath (Join-Path $moduleRoot 'manifest.json') -Raw |
        ConvertFrom-Json
    $version = [string]$manifest.version_number
    Require ([string]$manifest.name -ceq $name) "$name manifest identity drifted."
    $sources = [ordered]@{
        "$name.dll" = (Join-Path $moduleRoot "bin\Release\netstandard2.1\$name.dll")
        'manifest.json' = (Join-Path $moduleRoot 'manifest.json')
        'README.md' = (Join-Path $moduleRoot 'README.md')
        'icon.png' = (Join-Path $moduleRoot 'icon.png')
        'CHANGELOG.md' = (Join-Path $moduleRoot 'CHANGELOG.md')
        "$name.cfg.example" = (Join-Path $moduleRoot "$name.cfg.example")
    }
    foreach ($source in $sources.Values) {
        Require (Test-Path -LiteralPath $source -PathType Leaf) (
            "$name package source is missing: $source")
    }
    $leaf = "Chazman-$name-$version.zip"
    $first = Join-Path $packagesRoot ('.' + $leaf + '.first')
    $second = Join-Path $packagesRoot ('.' + $leaf + '.second')
    New-DeterministicZip $sources $first
    New-DeterministicZip $sources $second
    $firstHash = Get-Sha256Hex $first
    $secondHash = Get-Sha256Hex $second
    Require ($firstHash -ceq $secondHash) "$name did not package reproducibly."
    Assert-ExactZip $first $sources
    Assert-ExactZip $second $sources
    $final = Join-Path $packagesRoot $leaf
    Move-Item -LiteralPath $first -Destination $final
    Remove-Item -LiteralPath $second -Force
    $results += [pscustomobject]@{
        Name = $name
        Version = $version
        ZipSha256 = Get-Sha256Hex $final
        IconSha256 = Get-Sha256Hex (Join-Path $moduleRoot 'icon.png')
        DllSha256 = Get-Sha256Hex $sources["$name.dll"]
        Path = $final
    }
}

$suiteExpected = [ordered]@{
    'RunicModClientSuite' = '1.0.1'
    'RunicModServerSuite' = '1.0.1'
}
foreach ($name in $suiteExpected.Keys) {
    $version = [string]$suiteExpected[$name]
    $leaf = "Chazman-$name-$version.zip"
    $source = Join-Path $suitePackagesRoot $leaf
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Missing suite package: $source"
    $destination = Join-Path $packagesRoot $leaf
    Copy-Item -LiteralPath $source -Destination $destination
    $results += [pscustomobject]@{
        Name = $name
        Version = $version
        ZipSha256 = Get-Sha256Hex $destination
        IconSha256 = Get-Sha256Hex (Join-Path (Join-Path $repoRoot $name) 'icon.png')
        DllSha256 = $null
        Path = $destination
    }
}

$results | Sort-Object Name | Format-Table -AutoSize
