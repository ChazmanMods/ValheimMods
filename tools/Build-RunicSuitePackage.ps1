[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('RunicModSuite','RunicModClientSuite','RunicModServerSuite')][string]$Module,
    [string]$DependencyDirectory = 'E:\Valheim Mods\Latest Runic Mods',
    [string]$Destination = 'E:\Valheim Mods\PreProduction'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$suiteRoot = Join-Path (Split-Path $PSScriptRoot -Parent) $Module
$suiteManifest = Get-Content -Raw -LiteralPath (Join-Path $suiteRoot 'manifest.json') | ConvertFrom-Json
if ($suiteManifest.name -cne $Module -or $suiteManifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid suite identity/version.' }
if ($suiteManifest.description.Length -gt 250) { throw 'Description too long.' }
if (@($suiteManifest.dependencies | Select-Object -Unique).Count -ne $suiteManifest.dependencies.Count) { throw 'Duplicate dependencies.' }
foreach ($suiteDependency in $suiteManifest.dependencies) {
    if ($suiteDependency -notmatch '^Chazman-(?<name>\w+)-(?<version>\d+\.\d+\.\d+)$') { continue }
    $dependencyName = $Matches.name; $dependencyVersion = $Matches.version
    $dependencyPath = Join-Path $DependencyDirectory ($suiteDependency + '.zip')
    $dependencyZip = [IO.Compression.ZipFile]::OpenRead($dependencyPath)
    try {
        $dependencyReader = [IO.StreamReader]::new($dependencyZip.GetEntry('manifest.json').Open())
        try { $dependencyManifest = $dependencyReader.ReadToEnd() | ConvertFrom-Json } finally { $dependencyReader.Dispose() }
        if ($dependencyManifest.name -cne $dependencyName -or $dependencyManifest.version_number -cne $dependencyVersion) { throw "Dependency mismatch: $suiteDependency" }
    } finally { $dependencyZip.Dispose() }
}
if ($Module -eq 'RunicModServerSuite' -and @($suiteManifest.dependencies | Where-Object { $_ -match '^Chazman-Runic(Clock|Crafting)-' }).Count -gt 0) { throw 'Client-only clock/crafting found in server suite.' }
$suiteIcon = [IO.File]::ReadAllBytes((Join-Path $suiteRoot 'icon.png'))
if ($suiteIcon.Length -lt 24 -or [BitConverter]::ToString($suiteIcon[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or [BitConverter]::ToString($suiteIcon[16..23]) -ne '00-00-01-00-00-00-01-00') { throw 'Icon must be a 256x256 PNG.' }
$suiteFiles = @('manifest.json','README.md','CHANGELOG.md','icon.png')
foreach ($suiteFile in $suiteFiles) { if (-not (Test-Path -LiteralPath (Join-Path $suiteRoot $suiteFile) -PathType Leaf)) { throw "Missing $suiteFile" } }
$suiteZipPath = Join-Path $Destination "Chazman-$Module-$($suiteManifest.version_number).zip"
if (Test-Path -LiteralPath $suiteZipPath) { throw 'Do not overwrite an existing package.' }
$null = New-Item -ItemType Directory -Path $Destination -Force
$suiteZip = [IO.Compression.ZipFile]::Open($suiteZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($suiteFile in $suiteFiles) { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($suiteZip, (Join-Path $suiteRoot $suiteFile), $suiteFile, [IO.Compression.CompressionLevel]::Optimal) }
} finally { $suiteZip.Dispose() }
$suiteZip = [IO.Compression.ZipFile]::OpenRead($suiteZipPath)
try {
    if ($suiteZip.Entries.Count -ne 4) { throw 'Dependency-only suite must contain four files.' }
    foreach ($suiteFile in $suiteFiles) {
        $suiteStream = $suiteZip.GetEntry($suiteFile).Open(); $suiteHasher = [Security.Cryptography.SHA256]::Create()
        try { $suiteHash = [BitConverter]::ToString($suiteHasher.ComputeHash($suiteStream)).Replace('-','') }
        finally { $suiteStream.Dispose(); $suiteHasher.Dispose() }
        if ($suiteHash -cne (Get-FileHash -LiteralPath (Join-Path $suiteRoot $suiteFile)).Hash) { throw "ZIP mismatch: $suiteFile" }
    }
} finally { $suiteZip.Dispose() }
[pscustomobject]@{Package=$suiteZipPath;Version=$suiteManifest.version_number;Dependencies=$suiteManifest.dependencies.Count;Sha256=(Get-FileHash -LiteralPath $suiteZipPath).Hash}
