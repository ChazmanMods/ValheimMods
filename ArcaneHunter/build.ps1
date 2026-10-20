param([string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'ArcaneHunter.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests/ArcaneHunter.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
$hunterManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$hunterStage = Join-Path $PSScriptRoot ('artifacts/package-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$hunterPlugins = Join-Path $hunterStage 'plugins/ArcaneHunter'
New-Item -ItemType Directory -Force -Path $hunterPlugins | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/ArcaneHunter.dll') -Destination $hunterPlugins
foreach ($hunterFile in @('manifest.json', 'README.md', 'PLAYER-GUIDE.md', 'CHANGELOG.md', 'TESTING.md', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $hunterFile) -Destination $hunterStage
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$hunterZip = Join-Path $OutputDirectory ('Chazman-ArcaneHunter-' + $hunterManifest.version_number + '.zip')
Compress-Archive -Path (Join-Path $hunterStage '*') -DestinationPath $hunterZip -Force
$hunterHash = (Get-FileHash -LiteralPath $hunterZip -Algorithm SHA256).Hash
[IO.File]::WriteAllText($hunterZip + '.sha256', $hunterHash + '  ' + [IO.Path]::GetFileName($hunterZip) + [Environment]::NewLine)
Write-Output ('Packaged: ' + $hunterZip)
Write-Output ('SHA256: ' + $hunterHash)
