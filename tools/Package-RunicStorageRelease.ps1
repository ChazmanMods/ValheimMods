[CmdletBinding()]
param([string]$OutputDirectory = '')

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$module = Join-Path $repo 'RunicStorage'
if (!$OutputDirectory) { $OutputDirectory = Join-Path (Split-Path -Parent $repo) 'Latest Runic Mods' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$manifest = Get-Content -Raw -LiteralPath (Join-Path $module 'manifest.json') | ConvertFrom-Json
$version = [string]$manifest.version_number
if ($manifest.name -cne 'RunicStorage' -or $version -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.description.Length -gt 250 -or !$manifest.description.Length -or
    @($manifest.dependencies).Count -ne 1 -or
    $manifest.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2350') { throw 'Invalid manifest.' }
$sources = [ordered]@{
    'manifest.json' = Join-Path $module 'manifest.json'
    'README.md' = Join-Path $module 'README.md'
    'CHANGELOG.md' = Join-Path $module 'CHANGELOG.md'
    'icon.png' = Join-Path $module 'icon.png'
    'RunicStorage.dll' = Join-Path $module 'bin/Release/netstandard2.1/RunicStorage.dll'
    'RunicStorage.cfg.example' = Join-Path $module 'RunicStorage.cfg.example'
}
$hashes = [ordered]@{}
foreach ($entry in $sources.GetEnumerator()) {
    $hashes[$entry.Key] = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash
}
$identity = [Reflection.AssemblyName]::GetAssemblyName($sources['RunicStorage.dll'])
if ($identity.Name -cne $manifest.name -or $identity.Version.ToString() -cne "$version.0") { throw 'DLL version mismatch.' }
if ((Get-Content -Raw -LiteralPath (Join-Path $module 'Plugin.cs')) -notmatch
    ('const string Version = "' + [regex]::Escape($version) + '"')) { throw 'Plugin version mismatch.' }
$icon = [Drawing.Image]::FromFile($sources['icon.png'])
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Icon must be a 256x256 PNG.'
    }
} finally { $icon.Dispose() }
$auditDir = Join-Path $repo "artifacts/Thunderstore/RunicStorage/$version"
New-Item -ItemType Directory -Path $auditDir, $OutputDirectory -Force | Out-Null
$packageName = "Chazman-RunicStorage-$version.zip"
$staged = Join-Path $auditDir ($packageName + '.' + [Guid]::NewGuid().ToString('N') + '.staged')
$archive = [IO.Compression.ZipFile]::Open($staged, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($source in $sources.GetEnumerator()) {
        $entry = $archive.CreateEntry($source.Key, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($source.Value)
        $output = $entry.Open()
        try { $input.CopyTo($output) }
        finally { $output.Dispose(); $input.Dispose() }
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($staged)
try {
    if ($archive.Entries.Count -ne $sources.Count) { throw 'Unexpected ZIP contents.' }
    foreach ($entry in $archive.Entries) {
        if (!$sources.Contains($entry.FullName)) { throw 'Unexpected ZIP entry.' }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -cne $hashes[$entry.FullName]) { throw "ZIP hash mismatch: $($entry.FullName)" }
        if ((Get-FileHash -LiteralPath $sources[$entry.FullName]).Hash -cne $actual) { throw 'Source changed during packaging.' }
    }
} finally { $archive.Dispose() }
$destination = Join-Path $OutputDirectory $packageName
if (Test-Path -LiteralPath $destination) {
    Copy-Item -LiteralPath $destination -Destination (Join-Path $auditDir ($packageName + '.previous-' + [Guid]::NewGuid().ToString('N')))
}
Move-Item -LiteralPath $staged -Destination $destination -Force
$report = [ordered]@{
    package = $destination; version = $version; package_validation = 'PASS'
    sha256 = (Get-FileHash -LiteralPath $destination).Hash
    files = $hashes; dependencies = @($manifest.dependencies)
    icon = '256x256 PNG'; published = $false
    format_reference = 'https://wiki.thunderstore.io/mods/creating-a-package'
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $auditDir 'PACKAGE-AUDIT.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
