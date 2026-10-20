param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$log=Join-Path $projectRoot ('evidence\probe-build-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
& dotnet build (Join-Path $PSScriptRoot 'NativeAccessProbe\NativeAccessProbe.csproj') -c $Configuration --nologo 2>&1 | Tee-Object -FilePath $log
if($LASTEXITCODE -ne 0) { throw "Probe build failed; see $log" }
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('NativeAccessProbe\bin\'+$Configuration+'\netstandard2.1\RunicCrafting3.NativeAccessProbe.dll')) | Select-Object Path,Hash
