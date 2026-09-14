[CmdletBinding()]
param([switch]$UseExistingBuild)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!$UseExistingBuild) {
    dotnet build RunicSigns.csproj -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project Tests/RunicSigns.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    dotnet run --project Tests/Coexistence/Coexistence.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'RunicStorage coexistence regression failed.' }
    & ./Tests/Test-InstalledContracts.ps1
    }
    $manifest = Get-Content -LiteralPath manifest.json -Raw | ConvertFrom-Json
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $stage = Join-Path $dist ('package-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($file in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage
    }
    Copy-Item -LiteralPath 'bin/Release/netstandard2.1/RunicSigns.dll' -Destination $stage
    $zip = Join-Path $dist "Chazman-RunicSigns-$($manifest.version_number).zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    & ./Tests/Test-ReleasePackage.ps1 -Path $zip
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
    Write-Output "Thunderstore release package: $zip"
    Write-Output 'Prepared for manual upload; not published.'
} finally { Pop-Location }
