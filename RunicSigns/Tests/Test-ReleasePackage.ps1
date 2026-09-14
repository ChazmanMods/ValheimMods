[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Path))
try {
    $expected = @('manifest.json','README.md','CHANGELOG.md','icon.png','RunicSigns.dll')
    $actual = @($zip.Entries | ForEach-Object FullName)
    if ($actual.Count -ne $expected.Count -or @(Compare-Object $expected $actual -CaseSensitive).Count) { throw 'Unexpected ZIP contents or missing root files.' }
    $utf8 = [Text.UTF8Encoding]::new($false,$true)
    function Read-ZipBytes([string]$name) {
        $stream = $zip.GetEntry($name).Open(); $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); return ,$memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
    }
    $manifest = $utf8.GetString((Read-ZipBytes 'manifest.json')) | ConvertFrom-Json
    if ($manifest.name -cne 'RunicSigns' -or $manifest.name.Length -gt 128 -or $manifest.name -notmatch '^[a-zA-Z0-9_]+$') { throw 'Invalid package name.' }
    if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
    if (!$manifest.description -or $manifest.description.Length -gt 250) { throw 'Invalid description.' }
    if ($manifest.website_url -and $manifest.website_url -notmatch '^https?://') { throw 'Invalid website URL.' }
    if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2350') { throw 'Unexpected dependency list.' }
    foreach ($name in @('README.md','CHANGELOG.md')) {
        $markdown = $utf8.GetString((Read-ZipBytes $name))
        if ([string]::IsNullOrWhiteSpace($markdown)) { throw "Empty $name" }
    }
    $iconBytes = Read-ZipBytes 'icon.png'
    if ([BitConverter]::ToString($iconBytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Icon is not PNG.' }
    $iconStream = [IO.MemoryStream]::new($iconBytes,$false)
    $icon = [Drawing.Image]::FromStream($iconStream)
    try { if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw 'Icon must be 256x256.' } } finally { $icon.Dispose(); $iconStream.Dispose() }
    $dllPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/RunicSigns.dll'))
    if ([Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString(3) -ne $manifest.version_number) { throw 'DLL/manifest version mismatch.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $packageDllHash = [BitConverter]::ToString($sha.ComputeHash((Read-ZipBytes 'RunicSigns.dll'))).Replace('-','') } finally { $sha.Dispose() }
    if ($packageDllHash -ne (Get-FileHash -LiteralPath $dllPath).Hash) { throw 'Packaged DLL differs from the release build.' }
    Write-Output "PASS upload package: five root files, UTF-8 docs, manifest/version, dependency, 256x256 PNG and matching DLL. Version $($manifest.version_number)."
} finally { $zip.Dispose() }
