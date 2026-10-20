param([string]$Valheim='E:/SteamLibrary/steamapps/common/Valheim',[switch]$AllowConcurrentGame,[switch]$Combined)
$ErrorActionPreference='Stop'
if(!$AllowConcurrentGame -and (Get-Process valheim -ErrorAction SilentlyContinue)){throw 'Valheim is running; isolated test will not launch.'}
$runtime=Join-Path $PSScriptRoot $(if($Combined){'combined-runtime'}else{'standalone-runtime'})
$plugins=Join-Path $runtime 'BepInEx/plugins'
# Remove only the retired DLL from this isolated fixture; never touch live profiles.
$retired=Join-Path $plugins 'ArcaneWaterWorks.dll'
if(Test-Path -LiteralPath $retired){Remove-Item -LiteralPath $retired}
New-Item -ItemType Directory -Force $plugins | Out-Null
$bep=Join-Path $runtime 'BepInEx'
if(!(Test-Path (Join-Path $bep 'core'))){Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../ArcaneWaterWorks/Tests/runtime/BepInEx/core') -Destination $bep -Recurse}
if(!(Test-Path (Join-Path $plugins 'ValheimModding-Jotunn'))){Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../ArcaneWaterWorks/Tests/runtime/BepInEx/plugins/ValheimModding-Jotunn') -Destination $plugins -Recurse}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/ArcaneDecorFlowCraft.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Smoke/bin/Release/netstandard2.1/ArcaneDecorFlowCraft.Smoke.dll') -Destination $plugins
if(Get-ChildItem -LiteralPath $plugins -Recurse -Filter '*WaterGardens*'){throw 'Unexpected WaterGardens files in standalone fixture'}
if($Combined){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../../ArcaneWaterWorks/bin/Release/netstandard2.1/ArcaneNaturalWaters.dll") -Destination $plugins}
$env:FC_COMBINED=if($Combined){"1"}else{"0"}
$preloader=Join-Path $bep 'core/BepInEx.Preloader.dll'
$arguments='-batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --doorstop-enable true --doorstop-target-assembly "'+$preloader+'" -logFile "'+(Join-Path $runtime 'unity.log')+'"'
$process=$null
try{
    $process=Start-Process -FilePath (Join-Path $Valheim 'valheim.exe') -ArgumentList $arguments -WorkingDirectory $Valheim -WindowStyle Hidden -PassThru
    $process.PriorityClass='BelowNormal'
    Write-Output "Standalone test process: $($process.Id)"
    if(!$process.WaitForExit(360000)){throw 'Standalone test timed out'}
    if(!(Select-String -LiteralPath (Join-Path $bep 'LogOutput.log') -Pattern 'STANDALONE COMPLETE PASS' -Quiet)){throw 'Standalone runtime test failed; inspect log'}
    Select-String -LiteralPath (Join-Path $bep 'LogOutput.log') -Pattern 'STANDALONE COMPLETE'
}finally{if($process -and !$process.HasExited){Stop-Process -Id $process.Id -Force}}
