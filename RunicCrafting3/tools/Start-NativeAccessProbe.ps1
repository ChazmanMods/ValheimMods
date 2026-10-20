param(
    [string]$GameInstall = 'E:\SteamLibrary\steamapps\common\Valheim',
    [string]$BepCore = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Valheim 1.0\BepInEx\core',
    [string]$LoaderFixture = 'E:\Valheim Mods\ReleasePreparation\RunicStorageRules-2.1.3-20261001\Clients\A',
    [switch]$ManualOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$probeRoot = Join-Path $projectRoot ('fixtures\native-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
foreach ($name in @('valheim.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe')) {
    Copy-Item -LiteralPath (Join-Path $GameInstall $name) -Destination (Join-Path $probeRoot $name)
}
foreach ($name in @('valheim_Data', 'MonoBleedingEdge')) {
    New-Item -ItemType Junction -Path (Join-Path $probeRoot $name) -Target (Join-Path $GameInstall $name) | Out-Null
}
foreach ($name in @('winhttp.dll', 'doorstop_config.ini', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $LoaderFixture $name) -Destination (Join-Path $probeRoot $name)
}
New-Item -ItemType Directory -Path (Join-Path $probeRoot 'BepInEx\plugins') -Force | Out-Null
Copy-Item -LiteralPath $BepCore -Destination (Join-Path $probeRoot 'BepInEx\core') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeAccessProbe\bin\Release\netstandard2.1\RunicCrafting3.NativeAccessProbe.dll') -Destination (Join-Path $probeRoot 'BepInEx\plugins')
if($ManualOnly) { [IO.File]::WriteAllText((Join-Path $probeRoot 'manual-only.txt'),'native outcome diagnostic only') }
$probeProcess = Start-Process -FilePath (Join-Path $probeRoot 'valheim.exe') -WorkingDirectory $probeRoot -ArgumentList @('-screen-width','960','-screen-height','540','-screen-fullscreen','0','-logFile',('"'+(Join-Path $probeRoot 'unity.log')+'"')) -WindowStyle Hidden -PassThru
[pscustomobject]@{root=$probeRoot;pid=$probeProcess.Id;evidenceClass='native installed handlers; one process; simulated peer IDs'} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $projectRoot 'evidence\native-probe-run.json')
