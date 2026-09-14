param([string]$Stage = 'E:/Valheim Mods/.runic-death-smoke')
$ErrorActionPreference = 'Stop'
$Stage = [IO.Path]::GetFullPath($Stage)
if (-not $Stage.Contains('.runic-death-smoke')) { throw 'Expected isolated smoke directory.' }
if (Test-Path "$Stage/pid.txt") {
    $rdpPriorId = [int](Get-Content "$Stage/pid.txt")
    $rdpPrior = Get-Process -Id $rdpPriorId -ErrorAction SilentlyContinue
    if ($rdpPrior -and $rdpPrior.Path -eq (Join-Path $Stage 'valheim_server.exe')) { throw 'The previous isolated smoke is still running.' }
}
$rdpRepo = Split-Path $PSScriptRoot -Parent
dotnet build "$PSScriptRoot/RunicDeathPenalty.Smoke.csproj" -c Release --nologo -v quiet
if ($LASTEXITCODE) { throw 'Build failed.' }
Copy-Item -LiteralPath "$rdpRepo/RunicDeathPenalty/bin/Release/netstandard2.1/RunicDeathPenalty.dll","$PSScriptRoot/bin/Release/netstandard2.1/RunicDeathPenalty.Smoke.dll" -Destination "$Stage/BepInEx/plugins" -Force
$env:RDP_SMOKE_SAVEDIR = "$Stage/saves"
try {
    $rdpProcess = Start-Process -FilePath "$Stage/valheim_server.exe" -WorkingDirectory $Stage -ArgumentList "-batchmode -nographics -name RDP-Isolated-Test -port 28770 -world RDP-Smoke-Only -password RdpSmoke123 -public 0 -backups 0 -savedir `"$Stage/saves`" -logFile `"$Stage/unity.log`"" -WindowStyle Hidden -PassThru
    $rdpProcess.Id | Set-Content -LiteralPath "$Stage/pid.txt"
    Write-Output "Smoke PID: $($rdpProcess.Id)"
} finally { Remove-Item Env:RDP_SMOKE_SAVEDIR }
