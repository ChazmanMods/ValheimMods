[CmdletBinding()]
param([string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts/RunicDeathPenalty'))
$ErrorActionPreference = 'Stop'
$rdpModule = Join-Path $PSScriptRoot 'RunicDeathPenalty'
dotnet build (Join-Path $rdpModule 'RunicDeathPenalty.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE) { throw 'RunicDeathPenalty build failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'RunicDeathPenalty.Tests') -c Release
if ($LASTEXITCODE) { throw 'RunicDeathPenalty tests failed.' }
$rdpManifest = Get-Content (Join-Path $rdpModule 'manifest.json') -Raw | ConvertFrom-Json
$rdpVersion = $rdpManifest.version_number
$rdpRelease = Join-Path $OutputRoot $rdpVersion
$rdpStage = Join-Path $rdpRelease ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $rdpStage | Out-Null
$rdpDll = Join-Path $rdpModule 'bin/Release/netstandard2.1/RunicDeathPenalty.dll'
Copy-Item -LiteralPath $rdpDll -Destination $rdpStage
foreach ($rdpName in @('README.md','CHANGELOG.md','TESTING.md','manifest.json','RunicDeathPenalty.cfg.example')) {
    Copy-Item -LiteralPath (Join-Path $rdpModule $rdpName) -Destination $rdpStage
}
Copy-Item -LiteralPath (Join-Path $rdpModule 'icon-deathpenalty.png') -Destination (Join-Path $rdpStage 'icon.png')
$rdpZip = Join-Path $rdpRelease ("Chazman-RunicDeathPenalty-$rdpVersion.zip")
Compress-Archive -Path (Join-Path $rdpStage '*') -DestinationPath $rdpZip -Force
Copy-Item -LiteralPath $rdpDll -Destination (Join-Path $rdpRelease 'RunicDeathPenalty.dll') -Force
$rdpHashes = @($rdpZip, (Join-Path $rdpRelease 'RunicDeathPenalty.dll')) | Get-FileHash -Algorithm SHA256
$rdpHashes | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath (Join-Path $rdpRelease 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Package: $rdpZip"
$rdpHashes | Format-Table Hash,Path -AutoSize
