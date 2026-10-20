$ErrorActionPreference='Stop'
$runtime=Join-Path $PSScriptRoot 'server-runtime'
$env:IVY_NETWORK='server'
$env:SteamAppId='892970'
$arguments='-batchmode -nographics -name "Ivy Validation" -port 2476 -world IvyValidation -password ivytest123 -public 0 -savedir "'+(Join-Path $runtime 'saves')+'" --doorstop-enable true --doorstop-target-assembly "'+(Join-Path $runtime 'BepInEx/core/BepInEx.Preloader.dll')+'" -logFile "'+(Join-Path $runtime 'unity.log')+'"'
$p=Start-Process 'E:/SteamLibrary/steamapps/common/Valheim dedicated server/valheim_server.exe' -ArgumentList $arguments -WorkingDirectory 'E:/SteamLibrary/steamapps/common/Valheim dedicated server' -WindowStyle Hidden -PassThru
$p.PriorityClass='BelowNormal';Write-Output "Isolated server PID $($p.Id)"
try {if(!$p.WaitForExit(280000)){Write-Output 'Ending disposable server test.'}}finally{if(!$p.HasExited){Stop-Process -Id $p.Id -Force}}

