param([string]$WorldName = 'RunicPathsSmoke')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\last-smoke.json') | ConvertFrom-Json
$runtime = [IO.Path]::GetFullPath($run.Runtime)
$port = if ($run.Port) { $run.Port } else { 24976 }
$allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $runtime.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke runtime is outside the isolated artifact directory' }
$process = Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue
if ($process) {
    if ($process.Path -ne (Join-Path $runtime 'valheim_server.exe')) { throw 'Process identity changed' }
    Stop-Process -Id $process.Id
    $process.WaitForExit()
}
dotnet build (Join-Path $PSScriptRoot 'Smoke\RunicPaths.Smoke.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\netstandard2.1\RunicPaths.dll') -Destination (Join-Path $runtime 'BepInEx\plugins')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Smoke\bin\Release\netstandard2.1\RunicPaths.Smoke.dll') -Destination (Join-Path $runtime 'BepInEx\plugins')
$previousSmoke = $env:RUNIC_PATHS_ISOLATED_SMOKE
try {
    $env:RUNIC_PATHS_ISOLATED_SMOKE = '1'
    $log = 'unity-reload-' + (Get-Date -Format 'HHmmss') + '.log'
    $arguments = '-nographics -batchmode -name "RunicPaths isolated smoke" -port ' + $port + ' -world "' + $WorldName + '" -password "isolated-smoke-only" -public 0 -savedir "' + (Join-Path $runtime 'saves') + '" -logFile "' + (Join-Path $runtime $log) + '"'
    $process = Start-Process -FilePath (Join-Path $runtime 'valheim_server.exe') -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Hidden -PassThru
    @{ ProcessId = $process.Id; Runtime = $runtime; Port = $port } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\last-smoke.json')
} finally { $env:RUNIC_PATHS_ISOLATED_SMOKE = $previousSmoke }

