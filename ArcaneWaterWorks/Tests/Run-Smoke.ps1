param([string]$Valheim='E:/SteamLibrary/steamapps/common/Valheim',[switch]$RenderFrames,[switch]$VisiblePreview)
$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before running the isolated test.'}
$runtime=Join-Path $PSScriptRoot 'runtime'
$preloader=Join-Path $runtime 'BepInEx/core/BepInEx.Preloader.dll'
if(!(Test-Path -LiteralPath $preloader)){throw 'Stage an isolated BepInEx runtime and Jotunn under Tests/runtime first.'}
& dotnet build (Join-Path $PSScriptRoot '../ArcaneWaterWorks.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Mod build failed'}
& dotnet build (Join-Path $PSScriptRoot 'Smoke/Smoke.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Smoke build failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/ArcaneWaterWorks.dll') -Destination (Join-Path $runtime 'BepInEx/plugins')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Smoke/bin/Release/netstandard2.1/ArcaneWaterWorks.Smoke.dll') -Destination (Join-Path $runtime 'BepInEx/plugins')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../RunicLocalization/RunicLilyPads/bin/Release/netstandard2.1/ArcaneDecorWaterGardens.dll') -Destination (Join-Path $runtime 'BepInEx/plugins')
$arguments='-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --doorstop-enable true --doorstop-target-assembly "'+$preloader+'" -logFile "'+(Join-Path $runtime 'unity.log')+'"'
if($RenderFrames -or $VisiblePreview){$arguments=$arguments.Replace('-batchmode ','')}
# A launcher-owned watchdog is essential: Unity can stall before the in-game watchdog runs.
$testProcess=$null
try {
    $windowStyle=if($VisiblePreview){'Normal'}else{'Hidden'}
    $testProcess=Start-Process -FilePath (Join-Path $Valheim 'valheim.exe') -ArgumentList $arguments -WorkingDirectory $Valheim -WindowStyle $windowStyle -PassThru
    $testProcess.PriorityClass='BelowNormal'
    $testProcess.Id | Set-Content (Join-Path $runtime 'process-id.txt')
    Write-Output "Isolated test PID: $($testProcess.Id); external 300-second limit."
    if(!$testProcess.WaitForExit(300000)){throw 'Isolated test exceeded 300 seconds.'}
    if($testProcess.ExitCode -ne 0){throw "Isolated test failed with exit code $($testProcess.ExitCode)."}
    if(!(Select-String -LiteralPath (Join-Path $runtime 'BepInEx/LogOutput.log') -Pattern 'SMOKE COMPLETE PASS' -Quiet)){throw 'Test process exited without a passing result.'}
} finally {
    if($testProcess -and !$testProcess.HasExited){Stop-Process -Id $testProcess.Id -Force}
}
