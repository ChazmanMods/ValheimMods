param([string]$Label)
$ErrorActionPreference='Stop'
$testRoot='E:\Valheim Mods\ArcaneDecorNorseFurnishings\Runtime'
$arguments=@('-batchmode','-logFile',('"'+$testRoot+'\Player-'+$Label+'.log"'),'--doorstop-enable','true','--doorstop-target-assembly',('"'+$testRoot+'\BepInEx\core\BepInEx.Preloader.dll"'))
$process=Start-Process -FilePath 'E:\SteamLibrary\steamapps\common\Valheim\valheim.exe' -ArgumentList $arguments -WorkingDirectory 'E:\SteamLibrary\steamapps\common\Valheim' -WindowStyle Hidden -PassThru
$process.Id | Set-Content ($testRoot+'\process-id.txt')
Write-Output "Isolated render process $($process.Id) started: $Label"
