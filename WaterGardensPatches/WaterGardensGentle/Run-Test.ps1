$ErrorActionPreference='Stop'
if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'Valheim is still running; test not started.'}
$runtime='E:/Valheim Mods/WaterGardensPondMotion/runtime'
Copy-Item (Join-Path $PSScriptRoot 'Tests/bin/Release/netstandard2.1/IvySmoke.dll') (Join-Path $runtime 'BepInEx/plugins')
$preloader=Join-Path $runtime 'BepInEx/core/BepInEx.Preloader.dll'
$arguments='-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --doorstop-enable true --doorstop-target-assembly "'+$preloader+'" -logFile "'+(Join-Path $runtime 'unity.log')+'"'
$p=Start-Process 'E:/SteamLibrary/steamapps/common/Valheim/valheim.exe' -ArgumentList $arguments -WorkingDirectory 'E:/SteamLibrary/steamapps/common/Valheim' -WindowStyle Hidden -PassThru
$p.PriorityClass='BelowNormal'
Write-Output "Ivy test PID $($p.Id)"
try {if(!$p.WaitForExit(300000)){throw 'Isolated test exceeded 300 seconds.'};Write-Output "Exit $($p.ExitCode)"}finally{if(!$p.HasExited){Stop-Process -Id $p.Id -Force}}
