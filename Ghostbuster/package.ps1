$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -cne 'Ghostbuster' -or $manifest.name -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Invalid package name.' }
if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected Major.Minor.Patch version.' }
if ([string]::IsNullOrWhiteSpace($manifest.description) -or $manifest.description.Length -gt 250) { throw 'Description must contain 1-250 characters.' }
if ($manifest.website_url -ne '' -and $manifest.website_url -notmatch '^https?://') { throw 'Website must be empty or an HTTP(S) URL.' }
if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2350') { throw 'Unexpected dependency list.' }
$dllPath = Join-Path $projectRoot 'bin\Release\netstandard2.1\Ghostbuster.dll'
if (!(Test-Path -LiteralPath $dllPath)) { throw 'Build the Release DLL first.' }
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($dllPath).Version
if ($assemblyVersion.ToString(3) -ne $manifest.version_number) { throw 'DLL and manifest versions differ.' }
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
foreach ($document in @('manifest.json', 'README.md', 'CHANGELOG.md')) {
    $content = $strictUtf8.GetString([IO.File]::ReadAllBytes((Join-Path $projectRoot $document)))
    if ([string]::IsNullOrWhiteSpace($content)) { throw "Empty document: $document" }
}
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $projectRoot 'icon.png'))
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw 'icon.png must be 256x256.' }
    if ($icon.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'icon.png must be a real PNG.' }
} finally { $icon.Dispose() }
$stage = Join-Path $projectRoot ('dist\stage-' + [guid]::NewGuid().ToString('N'))
$pluginDirectory = Join-Path $stage 'BepInEx\plugins\Ghostbuster'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
Copy-Item -LiteralPath $dllPath -Destination $pluginDirectory
foreach ($name in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $stage
}
$archive = Join-Path $projectRoot ("dist\Chazman-Ghostbuster-$($manifest.version_number).zip")
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expected = @('BepInEx/plugins/Ghostbuster/Ghostbuster.dll','CHANGELOG.md','README.md','icon.png','manifest.json')
    $actual = @($zip.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { $_.FullName })
    if (Compare-Object ($expected | Sort-Object) ($actual | Sort-Object) -CaseSensitive) { throw 'ZIP contains unexpected files or paths.' }
    $stream = $zip.GetEntry('BepInEx/plugins/Ghostbuster/Ghostbuster.dll').Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $packedHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
        if ($packedHash -ne (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash) { throw 'ZIP DLL differs from built DLL.' }
    } finally { $sha.Dispose(); $stream.Dispose() }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding ascii
Write-Output $archive
Write-Output "SHA256 $hash"
Write-Output 'PASS: manifest, DLL version, UTF-8 documents, PNG, ZIP layout and DLL hash validated.'
