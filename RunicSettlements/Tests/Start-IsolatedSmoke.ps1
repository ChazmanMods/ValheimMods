param([string]$ServerRoot = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server', [string]$WorldName = 'RunicSettlementsSmoke', [string]$WorldSnapshot = '', [string]$NetworkSnapshot = '', [int]$Port = 24986, [switch]$StartupOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime = Join-Path $projectRoot ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runtime | Out-Null
foreach ($folder in @('valheim_server_Data', 'MonoBleedingEdge')) {
    New-Item -ItemType Junction -Path (Join-Path $runtime $folder) -Target (Join-Path $ServerRoot $folder) | Out-Null
}
foreach ($name in @('valheim_server.exe','UnityPlayer.dll','winhttp.dll','steamclient64.dll','steamwebrtc64.dll','tier0_s64.dll','vstdlib_s64.dll','steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $ServerRoot $name) -Destination $runtime
}
Copy-Item -LiteralPath (Join-Path $ServerRoot 'doorstop_config.ini') -Destination $runtime
# Valheim networking uses the game App ID, not the dedicated-server download App ID.
'892970' | Set-Content -LiteralPath (Join-Path $runtime 'steam_appid.txt') -Encoding ascii
New-Item -ItemType Directory -Path (Join-Path $runtime 'BepInEx\plugins'), (Join-Path $runtime 'BepInEx\config'), (Join-Path $runtime 'saves') | Out-Null
Copy-Item -LiteralPath (Join-Path $ServerRoot 'BepInEx\core') -Destination (Join-Path $runtime 'BepInEx') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\netstandard2.1\RunicSettlements.dll') -Destination (Join-Path $runtime 'BepInEx\plugins')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Smoke\bin\Release\netstandard2.1\RunicSettlements.Smoke.dll') -Destination (Join-Path $runtime 'BepInEx\plugins')
if ($WorldSnapshot) {
    New-Item -ItemType Directory -Force -Path (Join-Path $runtime 'saves\worlds_local'), (Join-Path $runtime 'BepInEx\config\RunicSettlements') | Out-Null
    Copy-Item -LiteralPath $WorldSnapshot -Destination (Join-Path $runtime 'saves\worlds_local') -Recurse
    # No road snapshot: exercise fresh settlement planning.
    'Unassisted spawn reproduction' | Set-Content (Join-Path $runtime 'repro.flag')
}
@'
[Sites]
Diameter = 120
'@ | Set-Content -LiteralPath (Join-Path $runtime 'BepInEx\config\com.chazman.runicsettlements.cfg')
@'
[Logging.Console]
Enabled = false
'@ | Set-Content -LiteralPath (Join-Path $runtime 'BepInEx\config\BepInEx.cfg')
if ($NetworkSnapshot) {
    New-Item -ItemType Directory -Force -Path (Join-Path $runtime 'BepInEx\config\RunicSettlements') | Out-Null
    Copy-Item -LiteralPath $NetworkSnapshot -Destination (Join-Path $runtime 'BepInEx\config\RunicSettlements') -Recurse
}
if ($StartupOnly) { 'Existing populated world startup regression' | Set-Content -LiteralPath (Join-Path $runtime 'startup-only.flag') }
$previousSmoke = $env:RUNIC_SETTLEMENTS_ISOLATED_SMOKE
try {
    $env:RUNIC_SETTLEMENTS_ISOLATED_SMOKE = '1'
    $arguments = '-nographics -batchmode -name "RunicSettlements isolated smoke" -port ' + $Port + ' -world "' + $WorldName + '" -password "isolated-smoke-only" -public 0 -savedir "' + (Join-Path $runtime 'saves') + '" -logFile "' + (Join-Path $runtime 'unity.log') + '"'
    $process = Start-Process -FilePath (Join-Path $runtime 'valheim_server.exe') -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Hidden -PassThru
    @{ ProcessId = $process.Id; Runtime = $runtime; Port = $Port } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\last-smoke.json')
} finally { $env:RUNIC_SETTLEMENTS_ISOLATED_SMOKE = $previousSmoke }


