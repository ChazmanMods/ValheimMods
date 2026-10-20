$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$baseline=Join-Path $workspace 'Rollback/ArcaneWaters-0.6.1_WaterGardens-1.1.3/Chazman-ArcaneWaters-0.6.1/plugins/ArcaneWaterWorks/ArcaneWaterWorks.dll'
$donor=Join-Path $workspace 'ArcaneWaterWorks/bin/Release/netstandard2.1/ArcaneWaterWorks.dll'
& dotnet build (Join-Path $workspace 'ArcaneWaterWorks/ArcaneWaterWorks.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Icon helper build failed'}
& dotnet run --project (Join-Path $PSScriptRoot 'Build/ClassicPatch.csproj') -c Release -- $baseline $donor (Join-Path $PSScriptRoot 'bin/ArcaneWaterWorks.dll') (Join-Path $PSScriptRoot 'Assets/BuildIcons')
if($LASTEXITCODE -ne 0){throw 'Icon embedding or preservation checks failed'}
& dotnet run --project (Join-Path $PSScriptRoot 'Tests/Classic.Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Regression checks failed'}
