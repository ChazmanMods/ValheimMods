param(
    [string]$GameInstall = 'E:\SteamLibrary\steamapps\common\Valheim',
    [string]$ServerInstall = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server',
    [int]$Port = 2476
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$fixture=Join-Path $projectRoot ('fixtures\mixed-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach($role in @('server','vanilla','crafter')) {
    $roleRoot=Join-Path $fixture $role
    New-Item -ItemType Directory -Path $roleRoot,(Join-Path $roleRoot 'test-saves') -Force | Out-Null
    $install=if($role -eq 'server'){$ServerInstall}else{$GameInstall}
    $exe=if($role -eq 'server'){'valheim_server.exe'}else{'valheim.exe'}
    foreach($name in @($exe,'UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')) {
        Copy-Item -LiteralPath (Join-Path $install $name) -Destination (Join-Path $roleRoot $name)
    }
    $data=if($role -eq 'server'){'valheim_server_Data'}else{'valheim_Data'}
    foreach($name in @($data,'MonoBleedingEdge')) {
        New-Item -ItemType Junction -Path (Join-Path $roleRoot $name) -Target (Join-Path $install $name) | Out-Null
    }
    if($role -eq 'server') {
        # Match the vendor's start_headless_server.bat application ID.
        [IO.File]::WriteAllText((Join-Path $roleRoot 'steam_appid.txt'),'892970')
        foreach($name in @('steamclient64.dll','steamwebrtc64.dll','tier0_s64.dll','vstdlib_s64.dll')) {
            Copy-Item -LiteralPath (Join-Path $install $name) -Destination (Join-Path $roleRoot $name)
        }
    }
    # No winhttp/Doorstop/BepInEx is copied to server or vanilla.
}
$crafter=Join-Path $fixture 'crafter'
$loader='E:\Valheim Mods\ReleasePreparation\RunicStorageRules-2.1.3-20261001\Clients\A'
foreach($name in @('winhttp.dll','doorstop_config.ini')) { Copy-Item -LiteralPath (Join-Path $loader $name) -Destination (Join-Path $crafter $name) }
New-Item -ItemType Directory -Path (Join-Path $crafter 'BepInEx\plugins') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $loader 'BepInEx\core') -Destination (Join-Path $crafter 'BepInEx\core') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeAccessProbe\bin\Release\netstandard2.1\RunicCrafting3.NativeAccessProbe.dll') -Destination (Join-Path $crafter 'BepInEx\plugins')
@('127.0.0.1',[string]$Port,'Runic3Test') | Set-Content (Join-Path $crafter 'network-target.txt')
$server=Join-Path $fixture 'server'
$serverProcess=Start-Process -FilePath (Join-Path $server 'valheim_server.exe') -WorkingDirectory $server -ArgumentList @('-batchmode','-nographics','-name','Runic3-Isolated','-port',[string]$Port,'-world','Runic3MixedNative','-password','Runic3Test','-public','0','-savedir',('"'+(Join-Path $server 'test-saves')+'"'),'-logFile',('"'+(Join-Path $server 'unity.log')+'"')) -WindowStyle Hidden -PassThru
[pscustomobject]@{root=$fixture;serverPid=$serverProcess.Id;port=$Port;password='Runic3Test'} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $projectRoot 'evidence\mixed-fixture.json')
