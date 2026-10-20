param([string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'ArcaneSniper.csproj') -c Release --nologo -p:MSBuildWarningsAsMessages=MSB3277
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests/ArcaneSniper.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
$sniperStage = Join-Path $PSScriptRoot ('artifacts/package-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$sniperPlugins = Join-Path $sniperStage 'plugins/ArcaneSniper'
New-Item -ItemType Directory -Force -Path $sniperPlugins | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/ArcaneSniper.dll') -Destination $sniperPlugins
foreach ($sniperFile in @('README.md', 'manifest.json', 'CHANGELOG.md', 'VALIDATION-0.3.0.md', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $sniperFile) -Destination $sniperStage
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sniperZip = Join-Path $OutputDirectory 'Chazman-ArcaneSniper-0.3.0.zip'
Compress-Archive -Path (Join-Path $sniperStage '*') -DestinationPath $sniperZip -Force
python (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/check_icon_backgrounds.py') $sniperZip
if ($LASTEXITCODE -ne 0) { throw 'Packaged icon background check failed' }
@'
import sys, zipfile
from pathlib import Path
assert zipfile.ZipFile(sys.argv[1]).read('icon.png') == Path(sys.argv[2]).read_bytes(), 'Packaged icon differs from approved source'
'@ | python - $sniperZip (Join-Path $PSScriptRoot 'icon.png')
if ($LASTEXITCODE -ne 0) { throw 'Packaged icon byte-match check failed' }
Write-Output ('Packaged: ' + $sniperZip)
