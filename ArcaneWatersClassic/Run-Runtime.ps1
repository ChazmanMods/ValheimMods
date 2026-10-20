param([switch]$Baseline)
$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Close Valheim before running the isolated test.'}
$workspace=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $workspace 'ArcaneWaterWorks/Tests/runtime'
$original=Join-Path $workspace 'Rollback/ArcaneWaters-0.6.1_WaterGardens-1.1.3'
$waterDll=if($Baseline){Join-Path $original 'Chazman-ArcaneWaters-0.6.1/plugins/ArcaneWaterWorks/ArcaneWaterWorks.dll'}else{Join-Path $PSScriptRoot 'bin/ArcaneWaterWorks.dll'}
Copy-Item -LiteralPath $waterDll -Destination (Join-Path $runtime 'BepInEx/plugins/ArcaneWaterWorks.dll')
Copy-Item -LiteralPath (Join-Path $original 'Chazman-ArcaneDecor_WaterGardens-1.1.3/plugins/ArcaneDecorWaterGardens/ArcaneDecorWaterGardens.dll') -Destination (Join-Path $runtime 'BepInEx/plugins/ArcaneDecorWaterGardens.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime/bin/Release/netstandard2.1/ArcaneWaterWorks.Smoke.dll') -Destination (Join-Path $runtime 'BepInEx/plugins/ArcaneWaterWorks.Smoke.dll')
$preloader=Join-Path $runtime 'BepInEx/core/BepInEx.Preloader.dll'
$arguments='-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --doorstop-enable true --doorstop-target-assembly "'+$preloader+'" -logFile "'+(Join-Path $runtime 'unity.log')+'"'
$testProcess=$null
try{
 $testProcess=Start-Process -FilePath 'E:/SteamLibrary/steamapps/common/Valheim/valheim.exe' -ArgumentList $arguments -WorkingDirectory 'E:/SteamLibrary/steamapps/common/Valheim' -WindowStyle Hidden -PassThru
 $testProcess.PriorityClass='BelowNormal'
 Write-Output ('Classic isolated test PID: '+$testProcess.Id)
 if(!$testProcess.WaitForExit(300000)){throw 'Classic isolated test timed out.'}
 if($Baseline){
  if(!(Select-String -LiteralPath (Join-Path $runtime 'BepInEx/LogOutput.log') -Pattern 'CLASSIC FAIL distant trench aim' -Quiet)){throw 'Baseline did not reproduce the expected regression'}
  Write-Output 'Original 0.6.1 regression reproduced.'
 }elseif($testProcess.ExitCode -ne 0 -or !(Select-String -LiteralPath (Join-Path $runtime 'BepInEx/LogOutput.log') -Pattern 'CLASSIC COMPLETE PASS' -Quiet)){throw 'Classic runtime regressions failed.'}
}finally{
 if($testProcess -and !$testProcess.HasExited){Stop-Process -Id $testProcess.Id -Force}
 $label=if($Baseline){'baseline'}else{'fixed'}
 Copy-Item -LiteralPath (Join-Path $runtime 'BepInEx/LogOutput.log') -Destination (Join-Path $PSScriptRoot ('Runtime/'+$label+'.log'))
}
