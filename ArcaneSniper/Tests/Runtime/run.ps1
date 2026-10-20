$ErrorActionPreference = 'Stop'
$sniperProject = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sniperRuntime = Join-Path $sniperProject 'artifacts/runtime'
$sniperCore = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Solo\BepInEx\core'
dotnet build (Join-Path $PSScriptRoot 'Runtime.csproj') -c Release --nologo -p:MSBuildWarningsAsMessages=MSB3277
if ($LASTEXITCODE -ne 0) { throw 'Runtime harness build failed' }
foreach ($sniperDirectory in @('BepInEx/core','BepInEx/plugins','BepInEx/config','saves')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $sniperRuntime $sniperDirectory) | Out-Null
}
foreach ($sniperLog in @('unity.log','BepInEx/LogOutput.log')) {
    $sniperLogPath = Join-Path $sniperRuntime $sniperLog
    if (Test-Path -LiteralPath $sniperLogPath) { Copy-Item -LiteralPath $sniperLogPath -Destination ($sniperLogPath + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.previous') }
}
Copy-Item -Path (Join-Path $sniperCore '*') -Destination (Join-Path $sniperRuntime 'BepInEx/core') -Force
Copy-Item -LiteralPath (Join-Path $sniperProject 'bin/Release/netstandard2.1/ArcaneSniper.dll'),(Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/ArcaneSniper.RuntimeChecks.dll') -Destination (Join-Path $sniperRuntime 'BepInEx/plugins') -Force
$sniperArguments = @('--doorstop-enabled','true','--doorstop-target-assembly',('"' + (Join-Path $sniperRuntime 'BepInEx/core/BepInEx.Preloader.dll') + '"'),'-batchmode','-nographics','-savedir',('"' + (Join-Path $sniperRuntime 'saves') + '"'),'-logFile',('"' + (Join-Path $sniperRuntime 'unity.log') + '"'))
$sniperProcess = Start-Process -FilePath 'E:\SteamLibrary\steamapps\common\Valheim\valheim.exe' -ArgumentList $sniperArguments -WorkingDirectory $sniperRuntime -WindowStyle Hidden -PassThru
$sniperProcess.Id | Set-Content (Join-Path $sniperRuntime 'pid.txt')
Write-Output "Isolated runtime PID: $($sniperProcess.Id)"
