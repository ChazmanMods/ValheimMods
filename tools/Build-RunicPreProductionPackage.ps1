[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('RunicWorldEngine','RunicClock','RunicBuildCamera','RunicCrafting','RunicStorage','RunicProduction','RunicAgriculture','RunicSentinel','RunicSentinelServer','RunicPortals','RunicInteraction','RunicInventory','RunicSafety','RunicPrecisionBuildTool','RunicDisplayStands')][string]$Module)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modRoot = if ($Module -eq 'RunicDisplayStands') { [IO.Path]::GetFullPath((Join-Path $repo '../StandaloneItemStands')) } else { Join-Path $repo $Module }
$destination = 'E:\Valheim Mods\PreProduction'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $modRoot 'manifest.json') | ConvertFrom-Json
if ($manifest.name -cne $Module) { throw 'Wrong package identity.' }
$dll = Join-Path $modRoot "bin\Release\netstandard2.1\$Module.dll"
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
if ($version -cne $manifest.version_number) { throw 'DLL/manifest version mismatch.' }
$iconPath = Join-Path $modRoot $(if ($Module -eq 'RunicPrecisionBuildTool') { 'media/icon.png' } else { 'icon.png' })
$icon = [IO.File]::ReadAllBytes($iconPath)
if ($icon.Length -lt 24 -or [BitConverter]::ToString($icon[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or
    [BitConverter]::ToString($icon[16..23]) -ne '00-00-01-00-00-00-01-00') { throw 'Icon must be a 256x256 PNG.' }
$files = [ordered]@{}
foreach ($name in @('manifest.json','README.md','CHANGELOG.md','icon.png',"$Module.cfg.example")) {
    $path = if ($name -eq 'icon.png') { $iconPath } else { Join-Path $modRoot $name }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $name" }
    $files[$name] = $path
}
$files["$Module.dll"] = $dll
$null = New-Item -ItemType Directory -Path $destination -Force
$zipPath = Join-Path $destination "Chazman-$Module-$version.zip"
if (Test-Path -LiteralPath $zipPath) { throw 'Do not overwrite an existing candidate.' }
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files.GetEnumerator()) {
            $entry = $archive.CreateEntry($file.Key, [IO.Compression.CompressionLevel]::Optimal)
            $output = $entry.Open()
            $inputFile = [IO.File]::OpenRead($file.Value)
            try { $inputFile.CopyTo($output) } finally { $inputFile.Dispose(); $output.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$checkStream = [IO.File]::OpenRead($zipPath)
$check = [IO.Compression.ZipArchive]::new($checkStream, [IO.Compression.ZipArchiveMode]::Read)
try {
    if ($check.Entries.Count -ne $files.Count) { throw 'Wrong ZIP root entries.' }
    foreach ($file in $files.GetEnumerator()) {
        $entry = $check.GetEntry($file.Key)
        if ($null -eq $entry) { throw "Missing ZIP entry $($file.Key)" }
        $inputEntry = $entry.Open()
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($hash.ComputeHash($inputEntry)).Replace('-','') }
        finally { $inputEntry.Dispose(); $hash.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $file.Value -Algorithm SHA256).Hash) { throw 'ZIP content mismatch.' }
    }
} finally { $check.Dispose(); $checkStream.Dispose() }
[pscustomobject]@{Package=$zipPath;Version=$version;Sha256=(Get-FileHash -LiteralPath $zipPath).Hash;DllSha256=(Get-FileHash -LiteralPath $dll).Hash}
