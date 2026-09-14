[CmdletBinding()]
param(
    [string]$ServerRoot = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server',
    [string]$BepInExSource = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Dedicated Server\BepInEx',
    [ValidateRange(1024, 65532)][int]$Port = 28643,
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 240,
    [ValidateRange(3, 60)][int]$DwellSeconds = 5,
    [switch]$SkipBuild
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function OptionalFingerprint([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{ Exists = $false; Bytes = [int64]0; Sha256 = '' }
    }
    $file = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        Exists = $true
        Bytes = [int64]$file.Length
        Sha256 = Sha256 $Path
    }
}

function ReadShared([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        $share)
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function QuoteArgument([string]$Value) {
    Require ($Value.IndexOf('"') -lt 0) 'A process argument contains an unexpected quote.'
    return '"' + $Value + '"'
}

function InvokeCleanBuild([string]$Project) {
    $output = @(& dotnet build $Project -c Release --no-restore 2>&1)
    $code = $LASTEXITCODE
    $text = [string]::Join("`n", @($output | ForEach-Object { [string]$_ }))
    Write-Output $text
    Require ($code -eq 0) "Release build failed: $Project"
    Require ($text.IndexOf('0 Warning(s)', [StringComparison]::Ordinal) -ge 0 -and
        $text.IndexOf('0 Error(s)', [StringComparison]::Ordinal) -ge 0) "Release build was not clean: $Project"
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$serverRoot = [System.IO.Path]::GetFullPath($ServerRoot)
$bepInExSource = [System.IO.Path]::GetFullPath($BepInExSource)
$serverExecutable = Join-Path $serverRoot 'valheim_server.exe'
$sourceCore = Join-Path $bepInExSource 'core'
$harnessProject = Join-Path $PSScriptRoot 'RunicInventory.ReleaseHarness\RunicInventory.ReleaseHarness.csproj'
$harnessDll = Join-Path $PSScriptRoot 'RunicInventory.ReleaseHarness\bin\Release\netstandard2.1\RunicInventory.ReleaseHarness.dll'
$inventoryDll = Join-Path $repoRoot 'RunicInventory\bin\Release\netstandard2.1\RunicInventory.dll'
$safetyDll = Join-Path $repoRoot 'RunicSafety\bin\Release\netstandard2.1\RunicSafety.dll'
$interactionDll = Join-Path $repoRoot 'RunicInteraction\bin\Release\netstandard2.1\RunicInteraction.dll'
$storageDll = Join-Path $repoRoot 'RunicStorage\bin\Release\netstandard2.1\RunicStorage.dll'

foreach ($required in @(
        $serverExecutable,
        (Join-Path $serverRoot 'winhttp.dll'),
        (Join-Path $sourceCore 'BepInEx.Preloader.dll'),
        (Join-Path $sourceCore 'BepInEx.dll'),
        (Join-Path $sourceCore '0Harmony.dll'))) {
    Require (Test-Path -LiteralPath $required -PathType Leaf) "Required runtime input is missing: $required"
}

if (-not $SkipBuild) {
    InvokeCleanBuild (Join-Path $repoRoot 'RunicInventory\RunicInventory.csproj')
    InvokeCleanBuild (Join-Path $repoRoot 'RunicSafety\RunicSafety.csproj')
    InvokeCleanBuild (Join-Path $repoRoot 'RunicInteraction\RunicInteraction.csproj')
    InvokeCleanBuild (Join-Path $repoRoot 'RunicStorage\RunicStorage.csproj')
    InvokeCleanBuild $harnessProject
}
foreach ($required in @($inventoryDll, $safetyDll, $interactionDll, $storageDll, $harnessDll)) {
    Require (Test-Path -LiteralPath $required -PathType Leaf) "Required tested DLL is missing: $required"
}

$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $repoRoot ('artifacts\RunicInventory\LiveAcceptance-1.0.2\' + $runId)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\RunicInventory'))
$resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
$artifactPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
Require ($resolvedRunRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Unsafe evidence root.'
Require (-not (Test-Path -LiteralPath $resolvedRunRoot)) 'Evidence root already exists.'

$bepRoot = Join-Path $resolvedRunRoot 'BepInEx'
$core = Join-Path $bepRoot 'core'
$plugins = Join-Path $bepRoot 'plugins'
$savedir = Join-Path $resolvedRunRoot 'savedir'
New-Item -ItemType Directory -Path $core, $plugins, (Join-Path $bepRoot 'config'), $savedir | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $sourceCore -Force) {
    Copy-Item -LiteralPath $entry.FullName -Destination $core -Recurse -Force
}
$payload = [ordered]@{
    'RunicInventory.dll' = $inventoryDll
    'RunicSafety.dll' = $safetyDll
    'RunicInteraction.dll' = $interactionDll
    'RunicStorage.dll' = $storageDll
    'RunicInventory.ReleaseHarness.dll' = $harnessDll
}
foreach ($entry in $payload.GetEnumerator()) {
    $destination = Join-Path $plugins $entry.Key
    Copy-Item -LiteralPath $entry.Value -Destination $destination
    Require ((Sha256 $destination) -ceq (Sha256 $entry.Value)) "Staged DLL hash mismatch: $($entry.Key)"
}
Require (@(Get-ChildItem -LiteralPath $plugins -File).Count -eq $payload.Count) 'The isolated plugin inventory is not exact.'

$resultPath = Join-Path $resolvedRunRoot 'HARNESS-RESULT.txt'
$stdoutPath = Join-Path $resolvedRunRoot 'server-stdout.log'
$stderrPath = Join-Path $resolvedRunRoot 'server-stderr.log'
$unityPath = Join-Path $resolvedRunRoot 'unity-player.log'
$bepLogPath = Join-Path $bepRoot 'LogOutput.log'
$targetAssembly = Join-Path $core 'BepInEx.Preloader.dll'
$worldName = 'RunicInventoryRelease-' + $runId.Substring(0, 15)
$sourceProfileLog = Join-Path $bepInExSource 'LogOutput.log'
$liveServerLog = Join-Path $serverRoot 'BepInEx\LogOutput.log'
$sourceProfileLogBefore = OptionalFingerprint $sourceProfileLog
$liveServerLogBefore = OptionalFingerprint $liveServerLog
$arguments = @(
    '--doorstop-enabled', 'true',
    '--doorstop-target-assembly', (QuoteArgument $targetAssembly),
    '-nographics', '-batchmode',
    '-name', (QuoteArgument 'Runic Inventory Release Harness'),
    '-port', $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-world', (QuoteArgument $worldName),
    '-password', (QuoteArgument 'RunicReleasePass'),
    '-savedir', (QuoteArgument $savedir),
    '-public', '0',
    '-logFile', (QuoteArgument $unityPath)
)

$previousAppId = [Environment]::GetEnvironmentVariable('SteamAppId', 'Process')
$previousResult = [Environment]::GetEnvironmentVariable(
    'RUNIC_INVENTORY_RELEASE_HARNESS_RESULT', 'Process')
$process = $null
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$ready = $false
$harnessComplete = $false
try {
    [Environment]::SetEnvironmentVariable('SteamAppId', '892970', 'Process')
    [Environment]::SetEnvironmentVariable(
        'RUNIC_INVENTORY_RELEASE_HARNESS_RESULT', $resultPath, 'Process')
    $process = Start-Process `
        -FilePath $serverExecutable `
        -ArgumentList $arguments `
        -WorkingDirectory $serverRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    Write-Output "RUN_ROOT=$resolvedRunRoot"
    Write-Output "PID=$($process.Id)"

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Isolated server exited before acceptance completed: $resolvedRunRoot" }
        $resultText = ReadShared $resultPath
        $combined = (ReadShared $bepLogPath) + "`n" + (ReadShared $stdoutPath) + "`n" + (ReadShared $unityPath)
        if ($resultText.IndexOf('status=FAIL', [StringComparison]::Ordinal) -ge 0) {
            throw "The installed runtime harness rejected the candidate: $resolvedRunRoot"
        }
        $harnessComplete = $resultText.IndexOf('status=PASS', [StringComparison]::Ordinal) -ge 0 -or
            $resultText.IndexOf('status=FAIL', [StringComparison]::Ordinal) -ge 0
        $ready = $combined.IndexOf('Opened Steam server', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $combined.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0
        if ($harnessComplete -and $ready) { break }
        Start-Sleep -Milliseconds 250
    }
    Require $harnessComplete "The installed runtime harness timed out: $resolvedRunRoot"
    Require $ready "The isolated server never reached Steam-server readiness: $resolvedRunRoot"
    $dwellStart = $timer.Elapsed.TotalSeconds
    while (($timer.Elapsed.TotalSeconds - $dwellStart) -lt $DwellSeconds) {
        Require (-not $process.HasExited) "The isolated server exited during its stability dwell: $resolvedRunRoot"
        Start-Sleep -Milliseconds 250
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SteamAppId', $previousAppId, 'Process')
    [Environment]::SetEnvironmentVariable(
        'RUNIC_INVENTORY_RELEASE_HARNESS_RESULT', $previousResult, 'Process')
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            [void]$process.WaitForExit(30000)
        }
        $process.Dispose()
    }
}
Require ([string]::Equals(
        $previousAppId,
        [Environment]::GetEnvironmentVariable('SteamAppId', 'Process'),
        [StringComparison]::Ordinal)) 'SteamAppId was not restored after the isolated launch.'
Require ([string]::Equals(
        $previousResult,
        [Environment]::GetEnvironmentVariable('RUNIC_INVENTORY_RELEASE_HARNESS_RESULT', 'Process'),
        [StringComparison]::Ordinal)) 'The harness result environment variable was not restored.'

$resultText = ReadShared $resultPath
$bepText = ReadShared $bepLogPath
$stdoutText = ReadShared $stdoutPath
$stderrText = ReadShared $stderrPath
$unityText = ReadShared $unityPath
$sourceProfileLogAfter = OptionalFingerprint $sourceProfileLog
$liveServerLogAfter = OptionalFingerprint $liveServerLog
Require ($sourceProfileLogBefore.Exists -eq $sourceProfileLogAfter.Exists -and
    $sourceProfileLogBefore.Bytes -eq $sourceProfileLogAfter.Bytes -and
    $sourceProfileLogBefore.Sha256 -ceq $sourceProfileLogAfter.Sha256) 'The source BepInEx profile log changed; isolation failed.'
Require ($liveServerLogBefore.Exists -eq $liveServerLogAfter.Exists -and
    $liveServerLogBefore.Bytes -eq $liveServerLogAfter.Bytes -and
    $liveServerLogBefore.Sha256 -ceq $liveServerLogAfter.Sha256) 'The live dedicated-server BepInEx log changed; isolation failed.'
Require ([regex]::IsMatch($resultText, '(?m)^status=PASS\s*$')) "Harness did not pass: $resolvedRunRoot"
Require ([regex]::IsMatch($resultText, '(?m)^scenario_count=40\s*$')) 'Harness scenario count drifted.'
Require ([regex]::Matches($resultText, '(?m)^scenario=').Count -eq 40) 'Harness did not record exactly 40 scenarios.'
Require ([regex]::IsMatch($resultText, '(?m)^marker=RUNIC_INVENTORY_RELEASE_HARNESS_PASS\s*$')) 'Harness pass marker is missing.'
Require ([regex]::Matches($bepText, '(?m)\bLoading \[[^\r\n]+\]').Count -eq $payload.Count) 'BepInEx loaded an unexpected plugin count.'
foreach ($load in @(
        'Runic Inventory 1.0.2',
        'Runic Safety 1.0.0',
        'Runic Interaction 1.0.0',
        'Runic Storage 1.0.1',
        'Runic Inventory Release Harness 1.0.0')) {
    Require ([regex]::Matches($bepText, '(?m)\bLoading \[' + [regex]::Escape($load) + '\]').Count -eq 1) "Missing exact load record: $load"
}
Require ($bepText.IndexOf('RUNIC_INVENTORY_RELEASE_HARNESS_FAIL', [StringComparison]::Ordinal) -lt 0) 'Harness failure marker is present.'
Require (-not [regex]::IsMatch($bepText, '(?im)^\s*\[(?:Fatal|Error)\s*:')) 'BepInEx log contains an error or fatal record.'
Require (-not [regex]::IsMatch($stderrText, '(?im)\b(?:exception|fatal|error)\b')) 'Server stderr contains a fatal signal.'
$knownHeadlessShaderException =
    '(?m)^ArgumentNullException: Value cannot be null\.\r?\n' +
    '^Parameter name: shader\r?\n' +
    '^  at UnityEngine\.Bindings\.ThrowHelper\.ThrowArgumentNullException \(System\.Object obj, System\.String parameterName\) \[0x00018\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at UnityEngine\.Material\.CreateWithShader \(UnityEngine\.Material self, UnityEngine\.Shader shader\) \[0x00003\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at UnityEngine\.Material\.\.ctor \(UnityEngine\.Shader shader\) \[0x00008\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at ShieldDomeImageEffect\.Awake \(\) \[0x0000b\] in <c366779df99d449da9e16b4e0a4d1800>:0[ \t]*(?:\r?\n(?!  at )|\z)'
$shaderRegex = [regex]::new($knownHeadlessShaderException)
$shaderMatches = $shaderRegex.Matches($unityText)
Require ($shaderMatches.Count -le 1) 'The known vanilla headless shader exception occurred more than once.'
$scannableUnity = $shaderRegex.Replace($unityText, '', 1)
Require (-not [regex]::IsMatch(
        $scannableUnity,
        '(?im)^\s*(?:Fatal error|Unhandled exception|[A-Za-z_][A-Za-z0-9_.]*Exception:)')) 'Unity log contains an unexpected exception or fatal error.'
Require ($bepText.IndexOf('protected-item/deny-providerunavailable', [StringComparison]::Ordinal) -ge 0) 'Fail-closed Safety diagnostic was not observed.'
Require ($bepText.IndexOf('protected-item/allow-none', [StringComparison]::Ordinal) -ge 0) 'Safety allow decision was not observed.'
$versionMatches = [regex]::Matches(
    $unityText + "`n" + $stdoutText,
    '(?im)Valheim version:\s*(?<version>\d+\.\d+\.\d+)(?:-ServerCharacters)?\s+\(network version\s+(?<network>\d+)\)')
Require ($versionMatches.Count -ge 1) 'No Valheim runtime version marker was observed.'
Require (@($versionMatches | ForEach-Object { $_.Groups['version'].Value } | Select-Object -Unique).Count -eq 1 -and
    $versionMatches[0].Groups['version'].Value -ceq '0.221.12') 'Valheim version drifted.'
Require (@($versionMatches | ForEach-Object { $_.Groups['network'].Value } | Select-Object -Unique).Count -eq 1 -and
    $versionMatches[0].Groups['network'].Value -ceq '36') 'Valheim network version drifted.'

$testedInventoryDll = Join-Path $resolvedRunRoot 'RunicInventory.dll'
Copy-Item -LiteralPath (Join-Path $plugins 'RunicInventory.dll') -Destination $testedInventoryDll
Require ((Sha256 $testedInventoryDll) -ceq (Sha256 $inventoryDll)) 'The preserved live-tested DLL differs from the source DLL.'

$scenarioNames = @([regex]::Matches($resultText, '(?m)^scenario=(?<name>[^\r\n]+)\s*$') |
    ForEach-Object { $_.Groups['name'].Value })
$pluginEvidence = @()
foreach ($entry in $payload.GetEnumerator()) {
    $path = Join-Path $plugins $entry.Key
    $pluginEvidence += [ordered]@{
        file = $entry.Key
        sha256 = Sha256 $path
        source_path = $entry.Value
        isolated_path = $path
    }
}
$completedUtc = [DateTime]::UtcNow.ToString('O')
$evidence = [ordered]@{
    schema = 'runic-inventory-installed-runtime-acceptance/v1'
    status = 'PASS'
    module = 'RunicInventory'
    version = '1.0.2'
    completed_utc = $completedUtc
    acceptance_kind = 'isolated installed Valheim/BepInEx runtime decision matrix'
    natural_network_sessions = $false
    runtime_matrix = @(
        'true dedicated/no local player',
        'remote non-owner compatibility',
        'solo/listen/remote owning-local authoritative model',
        'existing-character migration fallback',
        'transient load and genuine fault fail-closed states')
    exercised_consumers = @(
        'RunicSafety cooking authorization',
        'RunicInteraction transfer-policy authorization',
        'RunicStorage item-protection capture')
    scenario_count = $scenarioNames.Count
    scenarios = $scenarioNames
    inventory_dll = $testedInventoryDll
    inventory_dll_sha256 = Sha256 $testedInventoryDll
    plugin_payload = $pluginEvidence
    runtime = [ordered]@{
        valheim_version = '0.221.12'
        network_version = '36'
        chainloader_complete = $true
        steam_server_ready = $true
        dwell_seconds = $DwellSeconds
        port = $Port
        world = $worldName
    }
    logs = [ordered]@{
        bepinex = $bepLogPath
        bepinex_sha256 = Sha256 $bepLogPath
        stdout = $stdoutPath
        stdout_sha256 = Sha256 $stdoutPath
        stderr = $stderrPath
        stderr_sha256 = Sha256 $stderrPath
        unity = $unityPath
        unity_sha256 = Sha256 $unityPath
        harness_result = $resultPath
        harness_result_sha256 = Sha256 $resultPath
    }
    isolation = [ordered]@{
        run_root = $resolvedRunRoot
        savedir = $savedir
        source_profile_untouched = $true
        source_profile_log_path = $sourceProfileLog
        source_profile_log_sha256 = $sourceProfileLogAfter.Sha256
        live_server_profile_untouched = $true
        live_server_log_path = $liveServerLog
        live_server_log_sha256 = $liveServerLogAfter.Sha256
        process_environment_restored = $true
    }
}
$evidencePath = Join-Path $resolvedRunRoot 'PASS.json'
[System.IO.File]::WriteAllText(
    $evidencePath,
    ($evidence | ConvertTo-Json -Depth 10) + "`n",
    [System.Text.UTF8Encoding]::new($false))
Write-Output "RUNIC_INVENTORY_RELEASE_ACCEPTANCE_PASS scenarios=$($scenarioNames.Count)"
Write-Output "EVIDENCE=$evidencePath"
Write-Output "LIVE_DLL=$testedInventoryDll"
Write-Output "DLL_SHA256=$(Sha256 $testedInventoryDll)"
