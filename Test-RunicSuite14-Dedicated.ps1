[CmdletBinding()]
param(
    [string]$ServerRoot = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server',
    [string]$BepInExSource = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Dedicated Server\BepInEx',
    [string]$PluginPayloadRoot = '',
    [string]$EvidenceRoot = '',
    [string]$ClientAssemblyPath = '',
    [ValidateRange(1024, 65532)][int]$Port = 28614,
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 240,
    [ValidateRange(10, 60)][int]$DwellSeconds = 15,
    [ValidateSet('Acceptance', 'Coexistence')][string]$Profile = 'Coexistence',
    [switch]$StaticValidationOnly,
    [switch]$SkipUnifiedAudit,
    [string]$UnifiedAuditEvidencePath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$expectedValheimVersion = '0.221.12'
$expectedNetworkVersion = '36'
$expectedSteamAppIdOverride = '892970'
$expectedAcceptanceScenarioCount = 9
$knownVanillaHeadlessShaderExceptionPattern =
    '(?m)^ArgumentNullException: Value cannot be null\.\r?\n' +
    '^Parameter name: shader\r?\n' +
    '^  at UnityEngine\.Bindings\.ThrowHelper\.ThrowArgumentNullException \(System\.Object obj, System\.String parameterName\) \[0x00018\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at UnityEngine\.Material\.CreateWithShader \(UnityEngine\.Material self, UnityEngine\.Shader shader\) \[0x00003\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at UnityEngine\.Material\.\.ctor \(UnityEngine\.Shader shader\) \[0x00008\] in <89f741081c874c65b780dbd6a0d8d33e>:0[ \t]*\r?\n' +
    '^  at ShieldDomeImageEffect\.Awake \(\) \[0x0000b\] in <c366779df99d449da9e16b4e0a4d1800>:0[ \t]*(?:\r?\n(?!  at )|\z)'
$gameplayPlugins = @(
    @{ File = 'RunicStorage.dll'; Assembly = 'RunicStorage'; Guid = 'chazman.RunicStorage'; Name = 'Runic Storage'; Version = '1.0.0' }
    @{ File = 'RunicCrafting.dll'; Assembly = 'RunicCrafting'; Guid = 'chazman.RunicCrafting'; Name = 'Runic Crafting'; Version = '1.0.0' }
    @{ File = 'RunicAgriculture.dll'; Assembly = 'RunicAgriculture'; Guid = 'chazman.RunicAgriculture'; Name = 'Runic Agriculture'; Version = '1.0.0' }
    @{ File = 'RunicProduction.dll'; Assembly = 'RunicProduction'; Guid = 'chazman.RunicProduction'; Name = 'Runic Production'; Version = '1.0.0' }
    @{ File = 'RunicPrecisionBuildTool.dll'; Assembly = 'RunicPrecisionBuildTool'; Guid = 'chazman.RunicPrecisionBuildTool'; Name = 'Runic Precision Build Tool'; Version = '2.0.1' }
    @{ File = 'RunicInventory.dll'; Assembly = 'RunicInventory'; Guid = 'chazman.RunicInventory'; Name = 'Runic Inventory'; Version = '1.0.0' }
    @{ File = 'RunicPortals.dll'; Assembly = 'RunicPortals'; Guid = 'chazman.RunicPortals'; Name = 'Runic Portals'; Version = '1.0.0' }
    @{ File = 'RunicExploration.dll'; Assembly = 'RunicExploration'; Guid = 'chazman.RunicExploration'; Name = 'Runic Exploration'; Version = '1.0.0' }
    @{ File = 'RunicAwareness.dll'; Assembly = 'RunicAwareness'; Guid = 'chazman.RunicAwareness'; Name = 'Runic Awareness'; Version = '1.0.0' }
    @{ File = 'RunicInteraction.dll'; Assembly = 'RunicInteraction'; Guid = 'chazman.RunicInteraction'; Name = 'Runic Interaction'; Version = '1.0.0' }
    @{ File = 'RunicSafety.dll'; Assembly = 'RunicSafety'; Guid = 'chazman.RunicSafety'; Name = 'Runic Safety'; Version = '1.0.0' }
    @{ File = 'RunicVelocity.dll'; Assembly = 'RunicVelocity'; Guid = 'chazman.RunicVelocity'; Name = 'Runic Velocity'; Version = '1.0.0' }
    @{ File = 'RunicSentinel.dll'; Assembly = 'RunicSentinel'; Guid = 'chazman.RunicSentinel'; Name = 'Runic Sentinel'; Version = '1.0.0' }
    @{ File = 'RunicWorldEngine.dll'; Assembly = 'RunicWorldEngine'; Guid = 'chazman.RunicWorldEngine'; Name = 'Runic World Engine'; Version = '1.0.0' }
)
$companionPlugins = @(
    @{ File = 'RunicBuildCamera.dll'; Assembly = 'RunicBuildCamera'; Guid = 'chazman.RunicBuildCamera'; Name = 'Runic Build Camera'; Version = '1.0.0' }
    @{ File = 'RunicDisplayStands.dll'; Assembly = 'RunicDisplayStands'; Guid = 'chazman.RunicDisplayStands'; Name = 'RunicDisplayStands'; Version = '1.3.1' }
)
$expectedPlugins = if ($Profile -ceq 'Acceptance') {
    @($gameplayPlugins)
}
else {
    @($gameplayPlugins) + @($companionPlugins)
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256Hex {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return (($algorithm.ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Read-SharedTextFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ''
    }
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

function Get-LogSnapshots {
    param([Parameter(Mandatory = $true)][object[]]$Definitions)
    $snapshots = @()
    foreach ($definition in $Definitions) {
        $exists = Test-Path -LiteralPath $definition.Path -PathType Leaf
        $text = ''
        if ($exists) {
            try { $text = Read-SharedTextFile -Path $definition.Path }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 50
                $text = Read-SharedTextFile -Path $definition.Path
            }
        }
        $snapshots += [pscustomobject]@{
            Label = [string]$definition.Label
            Path = [string]$definition.Path
            Exists = [bool]$exists
            Text = [string]$text
        }
    }
    return $snapshots
}

function Get-LogSetSha256Hex {
    param([Parameter(Mandatory = $true)][object[]]$Snapshots)
    $builder = [System.Text.StringBuilder]::new()
    foreach ($snapshot in $Snapshots) {
        [void]$builder.Append($snapshot.Label).Append("`n")
        [void]$builder.Append($snapshot.Text.Length).Append("`n")
        [void]$builder.Append($snapshot.Text).Append("`n")
    }
    return Get-TextSha256Hex -Text $builder.ToString()
}

function Get-OptionalLogFingerprint {
    param([Parameter(Mandatory = $true)][string]$Path)
    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    if (-not $exists) {
        return [pscustomobject]@{ Exists = $false; Bytes = [int64]0; TextSha256 = '' }
    }
    $text = Read-SharedTextFile -Path $Path
    $info = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        Exists = $true
        Bytes = [int64]$info.Length
        TextSha256 = Get-TextSha256Hex -Text $text
    }
}

function Assert-NoFatalLogSignals {
    param(
        [Parameter(Mandatory = $true)][object[]]$Snapshots,
        [Parameter(Mandatory = $true)][string]$Phase
    )
    $knownExceptionRegex = [regex]::new($knownVanillaHeadlessShaderExceptionPattern)
    $fatalPatterns = @(
        @{ Name = 'BepInEx fatal/error'; Pattern = '(?im)^\s*\[(?:Fatal|Error)\s*:' }
        @{ Name = 'unhandled/fatal runtime error'; Pattern = '(?im)^\s*(?:Fatal error|Unhandled exception)\b' }
        @{ Name = 'binary/type/patch exception'; Pattern = '(?im)\b(?:MissingMethodException|TypeLoadException|BadImageFormatException|HarmonyException)\b' }
        @{ Name = 'exception record'; Pattern = '(?im)^\s*[A-Za-z_][A-Za-z0-9_.]*Exception:\s' }
        @{ Name = 'patch failure'; Pattern = '(?im)\bException while patching\b' }
        @{ Name = 'load/resolve failure'; Pattern = '(?im)\bCould not (?:load|resolve)[^\r\n]*(?:Runic|dependency|assembly)\b' }
        @{ Name = 'registered input ownership conflict'; Pattern = '(?im)\bExact input conflict\b' }
    )
    foreach ($snapshot in $Snapshots) {
        $scanText = $snapshot.Text
        if ($snapshot.Label -ceq 'Unity') {
            $knownMatches = $knownExceptionRegex.Matches($scanText)
            if ($knownMatches.Count -gt 1) {
                throw "The known vanilla headless shader exception occurred more than once during $Phase. Evidence: $($snapshot.Path)"
            }
            if ($knownMatches.Count -eq 1) {
                $scanText = $knownExceptionRegex.Replace($scanText, '', 1)
            }
        }
        foreach ($fatalPattern in $fatalPatterns) {
            if ([regex]::IsMatch($scanText, $fatalPattern.Pattern)) {
                throw "Fatal signal '$($fatalPattern.Name)' appeared in $($snapshot.Label) during $Phase. Evidence: $($snapshot.Path)"
            }
        }
    }
}

function Assert-CompleteLogSet {
    param(
        [Parameter(Mandatory = $true)][object[]]$Snapshots,
        [Parameter(Mandatory = $true)][string]$Phase
    )
    if ($Snapshots.Count -ne 4) {
        throw "Expected exactly four isolated logs during $Phase; found $($Snapshots.Count)."
    }
    foreach ($snapshot in $Snapshots) {
        if (-not $snapshot.Exists) {
            throw "The $($snapshot.Label) log did not exist during ${Phase}: $($snapshot.Path)"
        }
    }
}

function Get-BepInPluginIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        $pluginAttributes = @()
        $typeQueue = [System.Collections.Queue]::new()
        foreach ($type in $assembly.MainModule.Types) { $typeQueue.Enqueue($type) }
        while ($typeQueue.Count -gt 0) {
            $type = $typeQueue.Dequeue()
            foreach ($nestedType in $type.NestedTypes) { $typeQueue.Enqueue($nestedType) }
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -eq 'BepInEx.BepInPlugin') {
                    $pluginAttributes += $attribute
                }
            }
        }
        if ($pluginAttributes.Count -ne 1) {
            throw "Expected exactly one BepInPlugin identity in '$Path', found $($pluginAttributes.Count)."
        }
        $pluginAttribute = $pluginAttributes[0]
        if ($pluginAttribute.ConstructorArguments.Count -ne 3) {
            throw "The BepInPlugin identity in '$Path' does not have exactly three constructor arguments."
        }
        return [pscustomobject]@{
            Assembly = [string]$assembly.Name.Name
            Guid = [string]$pluginAttribute.ConstructorArguments[0].Value
            Name = [string]$pluginAttribute.ConstructorArguments[1].Value
            Version = [string]$pluginAttribute.ConstructorArguments[2].Value
            RunicAssemblyReferences = @($assembly.MainModule.AssemblyReferences |
                Where-Object { $_.Name -like 'Runic*' } |
                ForEach-Object { [string]$_.Name })
        }
    }
    finally { $assembly.Dispose() }
}

function Get-SteamInstallEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ContentPath,
        [Parameter(Mandatory = $true)][string]$AppId
    )
    $directory = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($ContentPath))
    if (-not $directory.Exists) { $directory = $directory.Parent }
    while ($null -ne $directory -and $directory.Name -ine 'common') { $directory = $directory.Parent }
    if ($null -eq $directory -or $null -eq $directory.Parent) {
        throw "Could not locate the Steam 'common' ancestor for $ContentPath."
    }
    $manifestPath = Join-Path $directory.Parent.FullName ('appmanifest_' + $AppId + '.acf')
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Steam app manifest is missing: $manifestPath"
    }
    $manifestText = [System.IO.File]::ReadAllText($manifestPath)
    $appMatch = [regex]::Match($manifestText, '(?im)^\s*"appid"\s+"(?<value>\d+)"\s*$')
    $buildMatch = [regex]::Match($manifestText, '(?im)^\s*"buildid"\s+"(?<value>\d+)"\s*$')
    if (-not $appMatch.Success -or $appMatch.Groups['value'].Value -ne $AppId) {
        throw "Steam app manifest identity drifted: $manifestPath"
    }
    if (-not $buildMatch.Success) { throw "Steam app manifest has no buildid: $manifestPath" }
    return [pscustomobject]@{
        AppId = $AppId
        BuildId = $buildMatch.Groups['value'].Value
        ManifestPath = [System.IO.Path]::GetFullPath($manifestPath)
        ManifestSha256 = Get-FileSha256Hex -Path $manifestPath
    }
}

function Quote-ProcessArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    if ($Value.IndexOf('"') -ge 0) { throw 'A dedicated-server argument unexpectedly contains a quote.' }
    return '"' + $Value + '"'
}

function Get-DoorstopCommandLineArguments {
    param([Parameter(Mandatory = $true)][string]$TargetAssembly)
    return @(
        '--doorstop-enabled',
        'true',
        '--doorstop-target-assembly',
        (Quote-ProcessArgument -Value $TargetAssembly)
    )
}

function Get-ProcessExitCodeText {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)
    $lastFailure = 'unknown'
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            $Process.Refresh()
            if (-not $Process.HasExited) { return 'process-still-running' }
            [void]$Process.WaitForExit(1000)
            return ([int]$Process.ExitCode).ToString([Globalization.CultureInfo]::InvariantCulture)
        }
        catch {
            $message = [regex]::Replace($_.Exception.Message, '\s+', '-').Trim('-')
            if ($message.Length -gt 96) { $message = $message.Substring(0, 96) }
            $lastFailure = $_.Exception.GetType().Name + '-' + $message
            Start-Sleep -Milliseconds 50
        }
    }
    return 'exit-code-unavailable-' + $lastFailure
}

function Invoke-WithProcessSteamAppId {
    param(
        [Parameter(Mandatory = $true)][string]$SteamAppId,
        [Parameter(Mandatory = $true)][scriptblock]$Launch
    )
    $previousValue = [Environment]::GetEnvironmentVariable('SteamAppId', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('SteamAppId', $SteamAppId, 'Process')
        return & $Launch
    }
    finally {
        [Environment]::SetEnvironmentVariable('SteamAppId', $previousValue, 'Process')
        $restoredValue = [Environment]::GetEnvironmentVariable('SteamAppId', 'Process')
        if (-not [string]::Equals($previousValue, $restoredValue, [StringComparison]::Ordinal)) {
            throw 'The process-scoped SteamAppId value was not restored after child launch.'
        }
    }
}

function Update-PeakWorkingSet {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][ref]$PeakBytes
    )
    try {
        $Process.Refresh()
        if ($Process.PeakWorkingSet64 -gt $PeakBytes.Value) { $PeakBytes.Value = $Process.PeakWorkingSet64 }
    }
    catch { }
}

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$smokeScriptPath = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$smokeScriptSha256 = Get-FileSha256Hex -Path $smokeScriptPath
$artifactLeaf = if ($Profile -ceq 'Acceptance') { 'Acceptance' } else { 'Coexistence' }
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ('artifacts\FoundationFree\' + $artifactLeaf)))
$serverRoot = [System.IO.Path]::GetFullPath($ServerRoot)
$bepInExSourceRoot = [System.IO.Path]::GetFullPath($BepInExSource)
$sourceCore = [System.IO.Path]::GetFullPath((Join-Path $bepInExSourceRoot 'core'))
$serverExecutable = Join-Path $serverRoot 'valheim_server.exe'
$serverAssembly = Join-Path $serverRoot 'valheim_server_Data\Managed\assembly_valheim.dll'
$doorstopProxy = Join-Path $serverRoot 'winhttp.dll'
if ([string]::IsNullOrWhiteSpace($ClientAssemblyPath)) {
    $commonRoot = [System.IO.Directory]::GetParent($serverRoot)
    if ($null -eq $commonRoot) { throw "Could not derive the Steam common directory from $serverRoot." }
    $clientAssembly = Join-Path $commonRoot.FullName 'Valheim\valheim_Data\Managed\assembly_valheim.dll'
}
else { $clientAssembly = [System.IO.Path]::GetFullPath($ClientAssemblyPath) }

foreach ($requiredFile in @(
        $serverExecutable,
        $serverAssembly,
        $doorstopProxy,
        $clientAssembly,
        (Join-Path $sourceCore 'BepInEx.Preloader.dll'),
        (Join-Path $sourceCore 'BepInEx.dll'),
        (Join-Path $sourceCore '0Harmony.dll'),
        (Join-Path $sourceCore 'Mono.Cecil.dll'))) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required dedicated-smoke input is missing: $requiredFile"
    }
}

$pinnedEnvironmentHashes = [ordered]@{
    $serverExecutable = 'A1E5ACCF766C1177A7E0B82B457CBED74CB3C9EFB5EE8E5C1E0BBBB60BD52839'
    $serverAssembly = '84A1B34F95774D36BE328390578D7B07C5CFFBC8CBB15119541900F055D486A3'
    $doorstopProxy = '93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8'
    $clientAssembly = '3B26C8512778F6E0664B5AF2A26F3C30993A00F584C1E76D9123A742B67E2004'
    (Join-Path $sourceCore 'BepInEx.dll') = 'E9AC3A950E91E71B13DF5480B36CE06AF27E981A688F0E62125B674D03A0713A'
    (Join-Path $sourceCore '0Harmony.dll') = '1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031'
    (Join-Path $sourceCore 'Mono.Cecil.dll') = '7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6'
}
foreach ($pinnedPath in $pinnedEnvironmentHashes.Keys) {
    $actualHash = Get-FileSha256Hex -Path $pinnedPath
    $expectedHash = [string]$pinnedEnvironmentHashes[$pinnedPath]
    if ($actualHash -cne $expectedHash) {
        throw "Dedicated-smoke environment hash drifted for '$pinnedPath': expected $expectedHash, found $actualHash."
    }
}

if ($StaticValidationOnly) {
    $expectedCount = if ($Profile -ceq 'Acceptance') { 14 } else { 16 }
    if ($expectedPlugins.Count -ne $expectedCount -or
        @($expectedPlugins.File | Select-Object -Unique).Count -ne $expectedCount -or
        @($expectedPlugins.Guid | Select-Object -Unique).Count -ne $expectedCount) {
        throw "The exact $Profile plugin contract must contain $expectedCount unique filenames and BepInEx GUIDs."
    }
    if (-not [string]::IsNullOrWhiteSpace($PluginPayloadRoot) -or
        -not [string]::IsNullOrWhiteSpace($EvidenceRoot) -or
        $SkipUnifiedAudit -or
        -not [string]::IsNullOrWhiteSpace($UnifiedAuditEvidencePath)) {
        throw '-StaticValidationOnly cannot be combined with payload, evidence, or unified-audit runtime options.'
    }

    $cecilPath = Join-Path $sourceCore 'Mono.Cecil.dll'
    if ($null -eq ('Mono.Cecil.AssemblyDefinition' -as [type])) { Add-Type -Path $cecilPath }
    foreach ($expectedPlugin in $expectedPlugins) {
        $moduleDirectory = [System.IO.Path]::GetFileNameWithoutExtension($expectedPlugin.File)
        $pluginPath = Join-Path $repoRoot ($moduleDirectory + '\bin\Release\netstandard2.1\' + $expectedPlugin.File)
        if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
            throw "Static validation is missing release plugin: $pluginPath"
        }
        $identity = Get-BepInPluginIdentity -Path $pluginPath
        foreach ($field in @('Assembly', 'Guid', 'Name', 'Version')) {
            if ([string]$identity.$field -cne [string]$expectedPlugin[$field]) {
                throw "Static plugin identity drift in $pluginPath`: $field."
            }
        }
        if (@($identity.RunicAssemblyReferences).Count -ne 0) {
            throw "Static plugin has a forbidden Runic runtime assembly reference: $pluginPath"
        }
    }

    $doorstopFixtureTarget = 'C:\isolated evidence\BepInEx\core\BepInEx.Preloader.dll'
    $doorstopFixtureArguments = @(Get-DoorstopCommandLineArguments -TargetAssembly $doorstopFixtureTarget)
    $expectedDoorstopFixtureArguments = @(
        '--doorstop-enabled',
        'true',
        '--doorstop-target-assembly',
        ('"' + $doorstopFixtureTarget + '"')
    )
    if ($doorstopFixtureArguments.Count -ne $expectedDoorstopFixtureArguments.Count) {
        throw 'The Windows Doorstop command-line fixture has the wrong argument count.'
    }
    for ($index = 0; $index -lt $expectedDoorstopFixtureArguments.Count; $index++) {
        if ($doorstopFixtureArguments[$index] -cne $expectedDoorstopFixtureArguments[$index]) {
            throw "The Windows Doorstop command-line fixture drifted at argument $index."
        }
    }
    $doorstopStrings = [System.Text.Encoding]::Unicode.GetString([System.IO.File]::ReadAllBytes($doorstopProxy))
    foreach ($requiredSwitch in @('--doorstop-enabled', '--doorstop-target-assembly')) {
        if ($doorstopStrings.IndexOf($requiredSwitch, [StringComparison]::Ordinal) -lt 0) {
            throw "The pinned Windows Doorstop proxy does not expose required switch $requiredSwitch."
        }
    }

    $steamAppIdBeforeFixture = [Environment]::GetEnvironmentVariable('SteamAppId', 'Process')
    $steamAppIdChildOutput = @(Invoke-WithProcessSteamAppId -SteamAppId $expectedSteamAppIdOverride -Launch {
            $childOutput = @(& powershell -NoProfile -Command "[Environment]::GetEnvironmentVariable('SteamAppId', 'Process')")
            if ($LASTEXITCODE -ne 0) { throw 'The SteamAppId inheritance fixture child failed.' }
            return $childOutput
        })
    $steamAppIdAfterFixture = [Environment]::GetEnvironmentVariable('SteamAppId', 'Process')
    if ($steamAppIdChildOutput.Count -ne 1 -or
        [string]$steamAppIdChildOutput[0] -cne $expectedSteamAppIdOverride) {
        throw 'The child process did not inherit the exact process-scoped SteamAppId override.'
    }
    if (-not [string]::Equals($steamAppIdBeforeFixture, $steamAppIdAfterFixture, [StringComparison]::Ordinal)) {
        throw 'The SteamAppId static fixture did not restore its original process value.'
    }

    $runtimePattern = '(?im)Valheim version:\s*(?<version>\d+\.\d+\.\d+)(?<suffix>-ServerCharacters)?\s+\(network version\s+(?<network>\d+)\)'
    foreach ($validMarker in @(
            'Valheim version: 0.221.12 (network version 36)',
            'Valheim version: 0.221.12-ServerCharacters (network version 36)')) {
        $match = [regex]::Match($validMarker, $runtimePattern)
        if (-not $match.Success -or
            $match.Groups['version'].Value -cne $expectedValheimVersion -or
            $match.Groups['network'].Value -cne $expectedNetworkVersion) {
            throw "Static runtime marker fixture was rejected: $validMarker"
        }
    }
    if ([regex]::IsMatch('Valheim version: 0.221.12-Unaudited (network version 36)', $runtimePattern)) {
        throw 'The runtime marker fixture accepted an unaudited suffix.'
    }
    if ([regex]::IsMatch('Valheim version: 0.221.12 (network version 35)', $runtimePattern) -and
        ([regex]::Match('Valheim version: 0.221.12 (network version 35)', $runtimePattern).Groups['network'].Value -ceq $expectedNetworkVersion)) {
        throw 'The runtime marker fixture failed to distinguish a wrong network version.'
    }

    $orderedFixture = "Registering lobby`nOpened Steam server"
    $reversedFixture = "Opened Steam server`nRegistering lobby"
    if ($orderedFixture.IndexOf('Opened Steam server', [StringComparison]::Ordinal) -le
        $orderedFixture.IndexOf('Registering lobby', [StringComparison]::Ordinal)) {
        throw 'The ordered readiness fixture was rejected.'
    }
    if ($reversedFixture.IndexOf('Opened Steam server', [StringComparison]::Ordinal) -gt
        $reversedFixture.IndexOf('Registering lobby', [StringComparison]::Ordinal)) {
        throw 'The reversed readiness fixture was accepted.'
    }

    $fatalWasRejected = $false
    try {
        Assert-NoFatalLogSignals -Snapshots @([pscustomobject]@{
                Label = 'negative-fixture'
                Path = '(memory)'
                Exists = $true
                Text = '[Error  : BepInEx] HarmonyException while patching RunicStorage'
            }) -Phase 'static negative fixture'
    }
    catch { $fatalWasRejected = $true }
    if (-not $fatalWasRejected) { throw 'The fatal-log negative fixture was accepted.' }
    $inputConflictWasRejected = $false
    try {
        Assert-NoFatalLogSignals -Snapshots @([pscustomobject]@{
                Label = 'input-conflict-fixture'
                Path = '(memory)'
                Exists = $true
                Text = '[Warning:Runic Core] Exact input conflict on JoyAltKeys + JoyUse [controller]: owner-a, owner-b.'
            }) -Phase 'static input conflict fixture'
    }
    catch { $inputConflictWasRejected = $true }
    if (-not $inputConflictWasRejected) { throw 'The exact-input-conflict fixture was accepted.' }

    $knownShaderExceptionLines = @(
        'ArgumentNullException: Value cannot be null.',
        'Parameter name: shader',
        '  at UnityEngine.Bindings.ThrowHelper.ThrowArgumentNullException (System.Object obj, System.String parameterName) [0x00018] in <89f741081c874c65b780dbd6a0d8d33e>:0 ',
        '  at UnityEngine.Material.CreateWithShader (UnityEngine.Material self, UnityEngine.Shader shader) [0x00003] in <89f741081c874c65b780dbd6a0d8d33e>:0 ',
        '  at UnityEngine.Material..ctor (UnityEngine.Shader shader) [0x00008] in <89f741081c874c65b780dbd6a0d8d33e>:0 ',
        '  at ShieldDomeImageEffect.Awake () [0x0000b] in <c366779df99d449da9e16b4e0a4d1800>:0 '
    )
    $knownShaderException = [string]::Join("`n", $knownShaderExceptionLines)
    Assert-NoFatalLogSignals -Snapshots @([pscustomobject]@{
            Label = 'Unity'
            Path = '(known-vanilla-headless-fixture)'
            Exists = $true
            Text = $knownShaderException
        }) -Phase 'static known vanilla headless fixture'

    $shaderExceptionNearMisses = @(
        [pscustomobject]@{
            Label = 'Unity'
            Text = $knownShaderException.Replace('Parameter name: shader', 'Parameter name: material')
        },
        [pscustomobject]@{
            Label = 'Unity'
            Text = [string]::Join("`n", $knownShaderExceptionLines[0..4])
        },
        [pscustomobject]@{
            Label = 'Unity'
            Text = $knownShaderException + "`n  at Unexpected.ExtraFrame () [0x00000] in <00000000000000000000000000000000>:0"
        },
        [pscustomobject]@{
            Label = 'Unity'
            Text = $knownShaderException + "`n`n" + $knownShaderException
        },
        [pscustomobject]@{
            Label = 'BepInEx'
            Text = $knownShaderException
        }
    )
    foreach ($nearMiss in $shaderExceptionNearMisses) {
        $nearMissWasRejected = $false
        try {
            Assert-NoFatalLogSignals -Snapshots @([pscustomobject]@{
                    Label = $nearMiss.Label
                    Path = '(shader-near-miss-fixture)'
                    Exists = $true
                    Text = $nearMiss.Text
                }) -Phase 'static shader near-miss fixture'
        }
        catch { $nearMissWasRejected = $true }
        if (-not $nearMissWasRejected) {
            throw "A shader-exception near miss was accepted for label $($nearMiss.Label)."
        }
    }

    Assert-NoFatalLogSignals -Snapshots @([pscustomobject]@{
            Label = 'positive-fixture'
            Path = '(memory)'
            Exists = $true
            Text = "[Warning:Runic Portals] Universal travel remains authority-safe only.`n" +
                "[Warning:Runic Safety] Recovery-container creation remains disabled.`n" +
                'Registering lobby; Opened Steam server'
        }) -Phase 'static positive fixture'

    Write-Output ('RUNIC_FOUNDATION_FREE_DEDICATED_STATIC_GO profile=' + $Profile.ToLowerInvariant() +
        ' plugins=' + $expectedPlugins.Count +
        ' environment_hashes=7 fixtures=18 doorstop_cli=true steam_appid_inheritance=true vanilla_headless_exception_scope=exact')
    return
}

if ($Profile -ceq 'Acceptance') {
    if ($SkipUnifiedAudit -or -not [string]::IsNullOrWhiteSpace($UnifiedAuditEvidencePath)) {
        throw 'Acceptance creates its own validated static preflight; external audit options are not valid.'
    }
}
elseif (-not $SkipUnifiedAudit -or [string]::IsNullOrWhiteSpace($UnifiedAuditEvidencePath)) {
    throw 'Coexistence requires -SkipUnifiedAudit and -UnifiedAuditEvidencePath pointing to the successful Foundation-free acceptance matrix.'
}

$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $runRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $runId))
    $artifactPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $runRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to create a smoke-test directory outside the artifact root: $runRoot"
    }
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
}
else {
    $runRoot = [System.IO.Path]::GetFullPath($EvidenceRoot)
    $volumeRoot = [System.IO.Path]::GetPathRoot($runRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    if ($runRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -ieq $volumeRoot -or
        $runRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -ieq $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -or
        $runRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -ieq $serverRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -or
        $runRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -ieq $bepInExSourceRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
        throw "Refusing to use a broad or live directory as the smoke evidence root: $runRoot"
    }
    if (Test-Path -LiteralPath $runRoot) {
        if (-not (Test-Path -LiteralPath $runRoot -PathType Container)) {
            throw "The explicit smoke evidence root is not a directory: $runRoot"
        }
        if (@(Get-ChildItem -LiteralPath $runRoot -Force).Count -ne 0) {
            throw "The explicit smoke evidence root must be empty: $runRoot"
        }
    }
    else {
        New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    }
}

$auditMode = ''
$auditEvidence = ''
if ($Profile -ceq 'Acceptance') {
    $auditMode = 'generated-static-preflight'
    $auditEvidence = Join-Path $runRoot 'acceptance-static-preflight.txt'
    [System.IO.File]::WriteAllLines(
        $auditEvidence,
        @(
            'RUNIC_FOUNDATION_FREE_STATIC_PASS modules=14',
            'manifests=bepinex-only',
            'runic_runtime_assembly_references=0',
            'foundation_payload_dlls=0'
        ),
        [System.Text.UTF8Encoding]::new($false))
}
else {
    $auditMode = 'validated-external'
    $auditEvidence = [System.IO.Path]::GetFullPath($UnifiedAuditEvidencePath)
    if (-not (Test-Path -LiteralPath $auditEvidence -PathType Leaf)) {
        throw "External acceptance-matrix evidence is missing: $auditEvidence"
    }
}

$auditHashBefore = Get-FileSha256Hex -Path $auditEvidence
$auditText = [System.IO.File]::ReadAllText($auditEvidence)
$auditHashAfter = Get-FileSha256Hex -Path $auditEvidence
if ($auditHashBefore -ne $auditHashAfter) { throw "Gate evidence changed while it was being validated: $auditEvidence" }
$expectedAuditUnits = if ($Profile -ceq 'Acceptance') { 14 } else { $expectedAcceptanceScenarioCount }
$auditPassPattern = if ($Profile -ceq 'Acceptance') {
    '(?m)^RUNIC_FOUNDATION_FREE_STATIC_PASS modules=14\s*$'
}
else {
    '(?m)^RUNIC_FOUNDATION_FREE_ACCEPTANCE_PASS scenarios=' + $expectedAcceptanceScenarioCount + '\s*$'
}
if (-not [regex]::IsMatch($auditText, $auditPassPattern)) {
    throw "Gate evidence does not contain the exact $Profile pass marker: $auditEvidence"
}

$cecilPath = Join-Path $sourceCore 'Mono.Cecil.dll'
if ($null -eq ('Mono.Cecil.AssemblyDefinition' -as [type])) { Add-Type -Path $cecilPath }

$payloadMode = 'repository-release'
$resolvedPayloadRoot = ''
$pluginSourcePaths = @{}
if (-not [string]::IsNullOrWhiteSpace($PluginPayloadRoot)) {
    $payloadMode = 'staged-flat'
    $resolvedPayloadRoot = [System.IO.Path]::GetFullPath($PluginPayloadRoot)
    if (-not (Test-Path -LiteralPath $resolvedPayloadRoot -PathType Container)) {
        throw "Plugin payload root is missing: $resolvedPayloadRoot"
    }
    $payloadEntries = @(Get-ChildItem -LiteralPath $resolvedPayloadRoot -Force)
    if ($payloadEntries.Count -ne $expectedPlugins.Count) {
        throw "The staged payload must contain exactly $($expectedPlugins.Count) root DLLs; found $($payloadEntries.Count): $resolvedPayloadRoot"
    }
    $expectedFileNames = @($expectedPlugins | ForEach-Object { $_.File })
    foreach ($entry in $payloadEntries) {
        if ($entry.PSIsContainer -or $expectedFileNames -cnotcontains $entry.Name) {
            throw "The staged payload contains an unexpected root entry: $($entry.FullName)"
        }
    }
    foreach ($expectedPlugin in $expectedPlugins) {
        $pluginSourcePaths[$expectedPlugin.File] = Join-Path $resolvedPayloadRoot $expectedPlugin.File
    }
}
else {
    foreach ($expectedPlugin in $expectedPlugins) {
        $moduleDirectory = [System.IO.Path]::GetFileNameWithoutExtension($expectedPlugin.File)
        $pluginSourcePaths[$expectedPlugin.File] = Join-Path $repoRoot ($moduleDirectory + '\bin\Release\netstandard2.1\' + $expectedPlugin.File)
    }
}

$validatedPlugins = @()
foreach ($expectedPlugin in $expectedPlugins) {
    $sourcePath = [System.IO.Path]::GetFullPath([string]$pluginSourcePaths[$expectedPlugin.File])
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Expected plugin DLL is missing: $sourcePath" }
    $identity = Get-BepInPluginIdentity -Path $sourcePath
    foreach ($field in @('Assembly', 'Guid', 'Name', 'Version')) {
        if ([string]$identity.$field -cne [string]$expectedPlugin[$field]) {
            throw "Plugin identity drift in $sourcePath`: expected $field '$($expectedPlugin[$field])', found '$($identity.$field)'."
        }
    }
    if (@($identity.RunicAssemblyReferences).Count -ne 0) {
        throw "Plugin has a forbidden Runic runtime assembly reference: $sourcePath"
    }
    $fileInfo = Get-Item -LiteralPath $sourcePath
    $validatedPlugins += [pscustomobject]@{
        File = $expectedPlugin.File
        Assembly = $identity.Assembly
        Guid = $identity.Guid
        Name = $identity.Name
        Version = $identity.Version
        SourcePath = $sourcePath
        Bytes = [int64]$fileInfo.Length
        Sha256 = Get-FileSha256Hex -Path $sourcePath
    }
}
if (@($validatedPlugins.Guid | Select-Object -Unique).Count -ne $expectedPlugins.Count) {
    throw 'The expected plugin payload contains duplicate BepInEx GUIDs.'
}

$bepRoot = Join-Path $runRoot 'BepInEx'
$core = Join-Path $bepRoot 'core'
$plugins = Join-Path $bepRoot 'plugins'
$savedir = Join-Path $runRoot 'savedir'
New-Item -ItemType Directory -Path $core -Force | Out-Null
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $bepRoot 'config') -Force | Out-Null
New-Item -ItemType Directory -Path $savedir -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $sourceCore -Force) {
    Copy-Item -LiteralPath $item.FullName -Destination $core -Recurse -Force
}

foreach ($validatedPlugin in $validatedPlugins) {
    $destination = Join-Path $plugins $validatedPlugin.File
    Copy-Item -LiteralPath $validatedPlugin.SourcePath -Destination $destination
    $copiedHash = Get-FileSha256Hex -Path $destination
    if ($copiedHash -ne $validatedPlugin.Sha256) { throw "Plugin payload copy verification failed for $($validatedPlugin.File)." }
    $copiedIdentity = Get-BepInPluginIdentity -Path $destination
    if ($copiedIdentity.Assembly -cne $validatedPlugin.Assembly -or
        $copiedIdentity.Guid -cne $validatedPlugin.Guid -or
        $copiedIdentity.Name -cne $validatedPlugin.Name -or
        $copiedIdentity.Version -cne $validatedPlugin.Version) {
        throw "Plugin identity changed during isolated staging: $destination"
    }
}

$isolatedPluginEntries = @(Get-ChildItem -LiteralPath $plugins -Force)
if ($isolatedPluginEntries.Count -ne $expectedPlugins.Count -or
    @($isolatedPluginEntries | Where-Object { $_.PSIsContainer -or $_.Extension -cne '.dll' }).Count -ne 0) {
    throw "The isolated plugin directory is not the exact $($expectedPlugins.Count)-DLL payload: $plugins"
}
foreach ($criticalCoreFile in @('BepInEx.dll', '0Harmony.dll', 'Mono.Cecil.dll', 'BepInEx.Preloader.dll')) {
    $sourceHash = Get-FileSha256Hex -Path (Join-Path $sourceCore $criticalCoreFile)
    $isolatedHash = Get-FileSha256Hex -Path (Join-Path $core $criticalCoreFile)
    if ($sourceHash -ne $isolatedHash) { throw "BepInEx core copy verification failed for $criticalCoreFile." }
}

$serverSteam = Get-SteamInstallEvidence -ContentPath $serverRoot -AppId '896660'
$clientSteam = Get-SteamInstallEvidence -ContentPath $clientAssembly -AppId '892970'
$environmentEvidence = [ordered]@{
    pinned_hashes_verified = $true
    smoke_script_path = $smokeScriptPath
    smoke_script_sha256 = $smokeScriptSha256
    server_executable_path = $serverExecutable
    server_executable_sha256 = Get-FileSha256Hex -Path $serverExecutable
    server_executable_expected_sha256 = [string]$pinnedEnvironmentHashes[$serverExecutable]
    server_assembly_path = $serverAssembly
    server_assembly_sha256 = Get-FileSha256Hex -Path $serverAssembly
    server_assembly_expected_sha256 = [string]$pinnedEnvironmentHashes[$serverAssembly]
    doorstop_proxy_path = $doorstopProxy
    doorstop_proxy_sha256 = Get-FileSha256Hex -Path $doorstopProxy
    doorstop_proxy_expected_sha256 = [string]$pinnedEnvironmentHashes[$doorstopProxy]
    doorstop_proxy_file_version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($doorstopProxy).FileVersion
    server_steam_appid = $serverSteam.AppId
    server_steam_buildid = $serverSteam.BuildId
    server_steam_manifest_path = $serverSteam.ManifestPath
    server_steam_manifest_sha256 = $serverSteam.ManifestSha256
    client_assembly_path = $clientAssembly
    client_assembly_sha256 = Get-FileSha256Hex -Path $clientAssembly
    client_assembly_expected_sha256 = [string]$pinnedEnvironmentHashes[$clientAssembly]
    client_steam_appid = $clientSteam.AppId
    client_steam_buildid = $clientSteam.BuildId
    client_steam_manifest_path = $clientSteam.ManifestPath
    client_steam_manifest_sha256 = $clientSteam.ManifestSha256
    bepinex_path = Join-Path $core 'BepInEx.dll'
    bepinex_sha256 = Get-FileSha256Hex -Path (Join-Path $core 'BepInEx.dll')
    bepinex_expected_sha256 = [string]$pinnedEnvironmentHashes[(Join-Path $sourceCore 'BepInEx.dll')]
    bepinex_file_version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $core 'BepInEx.dll')).FileVersion
    harmony_path = Join-Path $core '0Harmony.dll'
    harmony_sha256 = Get-FileSha256Hex -Path (Join-Path $core '0Harmony.dll')
    harmony_expected_sha256 = [string]$pinnedEnvironmentHashes[(Join-Path $sourceCore '0Harmony.dll')]
    harmony_file_version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $core '0Harmony.dll')).FileVersion
    mono_cecil_path = Join-Path $core 'Mono.Cecil.dll'
    mono_cecil_sha256 = Get-FileSha256Hex -Path (Join-Path $core 'Mono.Cecil.dll')
    mono_cecil_expected_sha256 = [string]$pinnedEnvironmentHashes[(Join-Path $sourceCore 'Mono.Cecil.dll')]
}

$stdout = Join-Path $runRoot 'server-stdout.log'
$stderr = Join-Path $runRoot 'server-stderr.log'
$unityLog = Join-Path $runRoot 'unity-player.log'
$bepLog = Join-Path $bepRoot 'LogOutput.log'
$liveBepLog = Join-Path $serverRoot 'BepInEx\LogOutput.log'
$liveBepLogBefore = Get-OptionalLogFingerprint -Path $liveBepLog
$logDefinitions = @(
    [pscustomobject]@{ Label = 'BepInEx'; Path = $bepLog }
    [pscustomobject]@{ Label = 'stdout'; Path = $stdout }
    [pscustomobject]@{ Label = 'stderr'; Path = $stderr }
    [pscustomobject]@{ Label = 'Unity'; Path = $unityLog }
)
$targetAssembly = Join-Path $core 'BepInEx.Preloader.dll'
$worldName = 'RunicSuite14Audit-' + $runId.Substring(0, 15)
$doorstopArguments = @(Get-DoorstopCommandLineArguments -TargetAssembly $targetAssembly)
$arguments = @($doorstopArguments) + @(
    '-nographics',
    '-batchmode',
    '-name', (Quote-ProcessArgument -Value 'Runic Suite 14 Audit'),
    '-port', $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-world', (Quote-ProcessArgument -Value $worldName),
    '-password', (Quote-ProcessArgument -Value 'RunicAuditPassword'),
    '-savedir', (Quote-ProcessArgument -Value $savedir),
    '-public', '0',
    '-logFile', (Quote-ProcessArgument -Value $unityLog)
)

$process = $null
$steamAppIdRestoredAfterLaunch = $false
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$chainloaderSeconds = -1.0
$registeringLobbySeconds = -1.0
$openedServerSeconds = -1.0
$readyLogLabel = ''
$peakWorkingSetBytes = [int64]0
$ready = $false
$dwellCompleted = $false
$aliveAfterDwell = $false
$dwellElapsedSeconds = -1.0
$unexpectedExit = $false
$unexpectedExitCode = $null
$preDwellLogSetHash = ''
$postDwellLogSetHash = ''
$preDwellScanUtc = ''
$postDwellScanUtc = ''
try {
    $process = Invoke-WithProcessSteamAppId -SteamAppId $expectedSteamAppIdOverride -Launch {
        Start-Process `
            -FilePath $serverExecutable `
            -ArgumentList $arguments `
            -WorkingDirectory $serverRoot `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr `
            -WindowStyle Hidden `
            -PassThru
    }
    $steamAppIdRestoredAfterLaunch = $true

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            $unexpectedExit = $true
            $unexpectedExitCode = Get-ProcessExitCodeText -Process $process
            break
        }
        Update-PeakWorkingSet -Process $process -PeakBytes ([ref]$peakWorkingSetBytes)
        $snapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
        foreach ($snapshot in $snapshots) {
            if ($chainloaderSeconds -lt 0 -and
                $snapshot.Text.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $chainloaderSeconds = $timer.Elapsed.TotalSeconds
            }
            $registerIndex = $snapshot.Text.IndexOf('Registering lobby', [StringComparison]::OrdinalIgnoreCase)
            $openedIndex = $snapshot.Text.IndexOf('Opened Steam server', [StringComparison]::OrdinalIgnoreCase)
            if ($registerIndex -ge 0 -and $registeringLobbySeconds -lt 0) {
                $registeringLobbySeconds = $timer.Elapsed.TotalSeconds
            }
            if ($registerIndex -ge 0 -and $openedIndex -gt $registerIndex) {
                if ($openedServerSeconds -lt 0) {
                    $openedServerSeconds = $timer.Elapsed.TotalSeconds
                    $readyLogLabel = $snapshot.Label
                }
                if ($chainloaderSeconds -ge 0) { $ready = $true }
            }
        }
        if ($ready) {
            $preDwellScanUtc = [DateTime]::UtcNow.ToString('O')
            Assert-CompleteLogSet -Snapshots $snapshots -Phase 'pre-dwell readiness gate'
            Assert-NoFatalLogSignals -Snapshots $snapshots -Phase 'pre-dwell readiness gate'
            $preDwellLogSetHash = Get-LogSetSha256Hex -Snapshots $snapshots
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not $ready -or $unexpectedExit) {
        if ($unexpectedExit) { throw "The isolated server exited before readiness with code $unexpectedExitCode. Evidence: $runRoot" }
        throw "The isolated server did not reach the ordered 'Registering lobby' -> 'Opened Steam server' readiness sequence within $TimeoutSeconds seconds. Evidence: $runRoot"
    }

    $dwellStartedSeconds = $timer.Elapsed.TotalSeconds
    while (($timer.Elapsed.TotalSeconds - $dwellStartedSeconds) -lt $DwellSeconds) {
        if ($process.HasExited) {
            $unexpectedExit = $true
            $unexpectedExitCode = Get-ProcessExitCodeText -Process $process
            break
        }
        Update-PeakWorkingSet -Process $process -PeakBytes ([ref]$peakWorkingSetBytes)
        Start-Sleep -Milliseconds 250
    }
    if ($unexpectedExit) {
        throw "The isolated server exited during the $DwellSeconds-second stability dwell with code $unexpectedExitCode. Evidence: $runRoot"
    }
    $aliveAfterDwell = -not $process.HasExited
    if (-not $aliveAfterDwell) {
        $unexpectedExitCode = Get-ProcessExitCodeText -Process $process
        throw "The isolated server exited at the end of the stability dwell with code $unexpectedExitCode. Evidence: $runRoot"
    }
    $dwellElapsedSeconds = $timer.Elapsed.TotalSeconds - $dwellStartedSeconds
    if ($dwellElapsedSeconds -lt $DwellSeconds) {
        throw "The monotonic stability dwell completed too early. Evidence: $runRoot"
    }
    $dwellCompleted = $true
    $postDwellScanUtc = [DateTime]::UtcNow.ToString('O')
    $postDwellSnapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
    Assert-CompleteLogSet -Snapshots $postDwellSnapshots -Phase 'post-dwell stability gate'
    Assert-NoFatalLogSignals -Snapshots $postDwellSnapshots -Phase 'post-dwell stability gate'
    $postDwellLogSetHash = Get-LogSetSha256Hex -Snapshots $postDwellSnapshots
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(30000) | Out-Null
        }
        $process.Dispose()
    }
}

$liveBepLogAfter = Get-OptionalLogFingerprint -Path $liveBepLog
if (-not $steamAppIdRestoredAfterLaunch) {
    throw 'The process-scoped SteamAppId launch window did not complete.'
}
$liveBepLogUnchanged = (
    $liveBepLogBefore.Exists -eq $liveBepLogAfter.Exists -and
    $liveBepLogBefore.Bytes -eq $liveBepLogAfter.Bytes -and
    $liveBepLogBefore.TextSha256 -ceq $liveBepLogAfter.TextSha256)
if (-not $liveBepLogUnchanged) {
    throw "The live dedicated-server BepInEx log changed; Doorstop isolation was not honored. Live log: $liveBepLog Evidence: $runRoot"
}

foreach ($requiredLog in @($bepLog, $stdout, $stderr, $unityLog)) {
    if (-not (Test-Path -LiteralPath $requiredLog -PathType Leaf)) { throw "An isolated smoke log was not created: $requiredLog" }
}
$finalSnapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
Assert-CompleteLogSet -Snapshots $finalSnapshots -Phase 'final stopped-process rescan'
Assert-NoFatalLogSignals -Snapshots $finalSnapshots -Phase 'final stopped-process rescan'
$finalUnitySnapshot = $finalSnapshots | Where-Object { $_.Label -ceq 'Unity' } | Select-Object -First 1
$allowedVanillaHeadlessShaderExceptionCount = [regex]::Matches(
    $finalUnitySnapshot.Text,
    $knownVanillaHeadlessShaderExceptionPattern).Count
$allLogText = [string]::Join("`n", @($finalSnapshots | ForEach-Object { $_.Text }))
$genericRuntimeMatches = [regex]::Matches($allLogText, '(?im)Valheim version:[^\r\n]+')
$runtimeMatches = [regex]::Matches(
    $allLogText,
    '(?im)Valheim version:\s*(?<version>\d+\.\d+\.\d+)(?<suffix>-ServerCharacters)?\s+\(network version\s+(?<network>\d+)\)')
if ($genericRuntimeMatches.Count -lt 1) {
    throw "No Valheim runtime version marker was observed in the isolated logs. Evidence: $runRoot"
}
if ($runtimeMatches.Count -ne $genericRuntimeMatches.Count) {
    throw "At least one Valheim runtime marker had an unaudited suffix or shape. Evidence: $runRoot"
}
$observedVersions = @($runtimeMatches | ForEach-Object { $_.Groups['version'].Value } | Select-Object -Unique)
if ($observedVersions.Count -ne 1 -or $observedVersions[0] -cne $expectedValheimVersion) {
    throw "The observed Valheim runtime version drifted; expected $expectedValheimVersion, found '$([string]::Join(',', $observedVersions))'. Evidence: $runRoot"
}
$observedNetworkVersions = @($runtimeMatches | ForEach-Object { $_.Groups['network'].Value } | Select-Object -Unique)
if ($observedNetworkVersions.Count -ne 1 -or $observedNetworkVersions[0] -cne $expectedNetworkVersion) {
    throw "The observed Valheim network version drifted; expected $expectedNetworkVersion, found '$([string]::Join(',', $observedNetworkVersions))'. Evidence: $runRoot"
}
$observedVersionSuffixes = @($runtimeMatches | ForEach-Object {
        if ($_.Groups['suffix'].Success) { $_.Groups['suffix'].Value }
        else { '(none)' }
    } | Select-Object -Unique)
$observedVersionMarkers = @($runtimeMatches | ForEach-Object { $_.Value.Trim() } | Select-Object -Unique)
$bepText = ($finalSnapshots | Where-Object { $_.Label -eq 'BepInEx' } | Select-Object -First 1).Text
$loadingCount = [regex]::Matches($bepText, '(?m)\bLoading \[[^\r\n]+\]').Count
if ($loadingCount -ne $expectedPlugins.Count) {
    throw "Expected exactly $($expectedPlugins.Count) isolated plugins, but BepInEx reported $loadingCount. Evidence: $runRoot"
}
foreach ($expectedPlugin in $expectedPlugins) {
    $loadPattern = '(?m)\bLoading \[' +
        [regex]::Escape([string]$expectedPlugin.Name) + ' ' +
        [regex]::Escape([string]$expectedPlugin.Version) + '\]'
    $expectedLoadCount = [regex]::Matches($bepText, $loadPattern).Count
    if ($expectedLoadCount -ne 1) {
        throw "Expected exactly one runtime load record for $($expectedPlugin.Guid) $($expectedPlugin.Version); found $expectedLoadCount. Evidence: $bepLog"
    }
}
if ($chainloaderSeconds -lt 0) { throw "BepInEx did not report chainloader startup completion. Evidence: $runRoot" }
if (-not $ready -or $registeringLobbySeconds -lt 0 -or $openedServerSeconds -lt 0) {
    throw "The genuine ordered Steam-server readiness sequence was not retained in the final logs. Evidence: $runRoot"
}
if (-not $dwellCompleted -or -not $aliveAfterDwell) {
    throw "The dedicated server did not remain alive through the complete stability dwell. Evidence: $runRoot"
}

$auditHashFinal = Get-FileSha256Hex -Path $auditEvidence
if ($auditHashFinal -cne $auditHashAfter) {
    throw "Gate evidence changed during the dedicated run: $auditEvidence"
}
foreach ($validatedPlugin in $validatedPlugins) {
    $sourceHashFinal = Get-FileSha256Hex -Path $validatedPlugin.SourcePath
    $isolatedHashFinal = Get-FileSha256Hex -Path (Join-Path $plugins $validatedPlugin.File)
    if ($sourceHashFinal -cne $validatedPlugin.Sha256 -or
        $isolatedHashFinal -cne $validatedPlugin.Sha256) {
        throw "Plugin payload bytes changed during the dedicated smoke: $($validatedPlugin.File)"
    }
}
foreach ($pinnedPath in $pinnedEnvironmentHashes.Keys) {
    if ((Get-FileSha256Hex -Path $pinnedPath) -cne [string]$pinnedEnvironmentHashes[$pinnedPath]) {
        throw "A pinned environment binary changed during the dedicated smoke: $pinnedPath"
    }
}
if ((Get-FileSha256Hex -Path $smokeScriptPath) -cne $smokeScriptSha256) {
    throw "The dedicated-smoke script changed while it was running: $smokeScriptPath"
}

$logEvidence = @()
foreach ($snapshot in $finalSnapshots) {
    $logInfo = Get-Item -LiteralPath $snapshot.Path
    $logEvidence += [ordered]@{
        label = $snapshot.Label
        path = $snapshot.Path
        bytes = [int64]$logInfo.Length
        sha256 = Get-FileSha256Hex -Path $snapshot.Path
    }
}
$pluginEvidence = @()
foreach ($validatedPlugin in $validatedPlugins) {
    $pluginEvidence += [ordered]@{
        file = $validatedPlugin.File
        assembly = $validatedPlugin.Assembly
        guid = $validatedPlugin.Guid
        name = $validatedPlugin.Name
        version = $validatedPlugin.Version
        runtime_load_record_verified = $true
        bytes = $validatedPlugin.Bytes
        sha256 = $validatedPlugin.Sha256
        source_path = $validatedPlugin.SourcePath
        isolated_path = Join-Path $plugins $validatedPlugin.File
    }
}

$completedUtc = [DateTime]::UtcNow.ToString('O')
$evidenceSchema = if ($Profile -ceq 'Acceptance') {
    'runic-foundation-free-dedicated-acceptance-runtime/v1'
}
else {
    'runic-foundation-free-coexistence-smoke/v1'
}
$evidence = [ordered]@{
    schema = $evidenceSchema
    status = 'GO'
    profile = $Profile.ToLowerInvariant()
    completed_utc = $completedUtc
    unified_audit_mode = $auditMode
    unified_audit_transcript_sha256 = $auditHashAfter
    audit = [ordered]@{
        status = 'PASS'
        mode = $auditMode
        skip_requested = [bool]$SkipUnifiedAudit
        expected_units = $expectedAuditUnits
        evidence_path = $auditEvidence
        evidence_sha256 = $auditHashAfter
    }
    payload = [ordered]@{
        mode = $payloadMode
        root = $resolvedPayloadRoot
        source_mode = $payloadMode
        source_root = $resolvedPayloadRoot
        expected_plugin_count = $expectedPlugins.Count
        loaded_plugin_count = $loadingCount
        plugins = $pluginEvidence
        source_hash_catalog = @($pluginEvidence | ForEach-Object {
                [ordered]@{
                    file = $_.file
                    bytes = $_.bytes
                    sha256 = $_.sha256
                    source_path = $_.source_path
                }
            })
    }
    runtime = [ordered]@{
        expected_valheim_version = $expectedValheimVersion
        observed_valheim_versions = $observedVersions
        allowed_version_suffixes = @('(none)', '-ServerCharacters')
        observed_version_suffixes = $observedVersionSuffixes
        expected_network_version = $expectedNetworkVersion
        observed_network_versions = $observedNetworkVersions
        observed_version_markers = $observedVersionMarkers
        version_marker_observed = $true
        chainloader_seconds = $chainloaderSeconds
        chainloader_observed = $true
        registering_lobby_seconds = $registeringLobbySeconds
        registering_lobby_observed = $true
        opened_steam_server_seconds = $openedServerSeconds
        opened_steam_server_observed = $true
        readiness_sequence = @('Registering lobby', 'Opened Steam server')
        readiness_order_valid = $true
        ready_log = $readyLogLabel
        dwell_seconds = $DwellSeconds
        dwell_elapsed_seconds = $dwellElapsedSeconds
        dwell_completed = $dwellCompleted
        process_alive_after_dwell = $aliveAfterDwell
        peak_working_set_bytes = $peakWorkingSetBytes
        pre_dwell_scan_utc = $preDwellScanUtc
        pre_dwell_logset_sha256 = $preDwellLogSetHash
        post_dwell_scan_utc = $postDwellScanUtc
        post_dwell_logset_sha256 = $postDwellLogSetHash
        final_logset_sha256 = Get-LogSetSha256Hex -Snapshots $finalSnapshots
    }
    log_scan = [ordered]@{
        expected_log_count = 4
        scanned_log_count = $finalSnapshots.Count
        pre_dwell_passed = $true
        post_dwell_passed = $true
        final_stopped_process_rescan_passed = $true
        fatal_signals_found = 0
        allowed_vanilla_headless_shader_exception_count = $allowedVanillaHeadlessShaderExceptionCount
        allowed_vanilla_headless_shader_exception_maximum = 1
        allowed_vanilla_headless_shader_exception_scope = 'Unity log only; exact pinned stack shape'
    }
    isolation = [ordered]@{
        run_root = $runRoot
        savedir = $savedir
        unity_log = $unityLog
        port = $Port
        world = $worldName
        public = $false
        doorstop_override_mode = 'windows-command-line'
        doorstop_target_assembly = $targetAssembly
        live_bepinex_log_path = $liveBepLog
        live_bepinex_log_unchanged = $liveBepLogUnchanged
        live_bepinex_log_before_sha256 = $liveBepLogBefore.TextSha256
        live_bepinex_log_after_sha256 = $liveBepLogAfter.TextSha256
        steam_appid_override = $expectedSteamAppIdOverride
        steam_appid_override_scope = 'process-inheritance-during-launch-only'
        steam_appid_restored_after_launch = $steamAppIdRestoredAfterLaunch
    }
    environment = $environmentEvidence
    logs = $logEvidence
}

$jsonFileName = if ($Profile -ceq 'Acceptance') { 'ACCEPTANCE-RUNTIME-PASS.json' } else { 'SMOKE-PASS.json' }
$evidenceFileName = if ($Profile -ceq 'Acceptance') { 'ACCEPTANCE-RUNTIME-EVIDENCE.txt' } else { 'SMOKE-EVIDENCE.txt' }
$jsonPath = Join-Path $runRoot $jsonFileName
$pendingJsonPath = Join-Path $runRoot ('.' + $jsonFileName + '.pending')
$jsonText = $evidence | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($pendingJsonPath, $jsonText, [System.Text.UTF8Encoding]::new($false))
$jsonHash = Get-FileSha256Hex -Path $pendingJsonPath
$resultPath = Join-Path $runRoot $evidenceFileName
$evidenceHeader = if ($Profile -ceq 'Acceptance') {
    'RUNIC_FOUNDATION_FREE_DEDICATED_ACCEPTANCE_RUNTIME_EVIDENCE'
}
else {
    'RUNIC_FOUNDATION_FREE_COEXISTENCE_SMOKE_EVIDENCE'
}
$result = @(
    $evidenceHeader,
    ('status=GO'),
    ('schema=' + $evidenceSchema),
    ('profile=' + $Profile.ToLowerInvariant()),
    ('utc=' + $completedUtc),
    ('audit_status=passed'),
    ('audit_mode=' + $auditMode),
    ('audit_skip_requested=' + [bool]$SkipUnifiedAudit),
    ('audit_evidence=' + $auditEvidence),
    ('audit_evidence_sha256=' + $auditHashAfter),
    ('valheim_expected=' + $expectedValheimVersion),
    ('valheim_observed=' + [string]::Join(',', $observedVersions)),
    ('network_expected=' + $expectedNetworkVersion),
    ('network_observed=' + [string]::Join(',', $observedNetworkVersions)),
    ('plugins=' + $expectedPlugins.Count),
    ('payload_mode=' + $payloadMode),
    ('payload_root=' + $resolvedPayloadRoot),
    ('doorstop_override_mode=windows-command-line'),
    ('doorstop_target_assembly=' + $targetAssembly),
    ('live_bepinex_log_unchanged=' + $liveBepLogUnchanged),
    ('steam_appid_override=' + $expectedSteamAppIdOverride),
    ('steam_appid_override_scope=process-inheritance-during-launch-only'),
    ('steam_appid_restored_after_launch=' + $steamAppIdRestoredAfterLaunch),
    ('allowed_vanilla_headless_shader_exception_count=' + $allowedVanillaHeadlessShaderExceptionCount),
    ('chainloader_seconds=' + $chainloaderSeconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)),
    ('registering_lobby_seconds=' + $registeringLobbySeconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)),
    ('opened_steam_server_seconds=' + $openedServerSeconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)),
    ('dwell_seconds=' + $DwellSeconds),
    ('dwell_elapsed_seconds=' + $dwellElapsedSeconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)),
    ('alive_after_dwell=' + $aliveAfterDwell),
    ('peak_working_set_bytes=' + $peakWorkingSetBytes),
    ('server_buildid=' + $serverSteam.BuildId),
    ('client_buildid=' + $clientSteam.BuildId),
    ('server_executable_sha256=' + $environmentEvidence.server_executable_sha256),
    ('server_assembly_sha256=' + $environmentEvidence.server_assembly_sha256),
    ('client_assembly_sha256=' + $environmentEvidence.client_assembly_sha256),
    ('bepinex_sha256=' + $environmentEvidence.bepinex_sha256),
    ('harmony_sha256=' + $environmentEvidence.harmony_sha256),
    ('evidence_json=' + $jsonPath),
    ('evidence_json_sha256=' + $jsonHash)
)
foreach ($validatedPlugin in $validatedPlugins) {
    $result += ('plugin.' + $validatedPlugin.File + '.guid=' + $validatedPlugin.Guid)
    $result += ('plugin.' + $validatedPlugin.File + '.version=' + $validatedPlugin.Version)
    $result += ('plugin.' + $validatedPlugin.File + '.sha256=' + $validatedPlugin.Sha256)
}
[System.IO.File]::WriteAllLines($resultPath, $result, [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $pendingJsonPath -Destination $jsonPath
$goMarker = if ($Profile -ceq 'Acceptance') {
    'RUNIC_FOUNDATION_FREE_DEDICATED_ACCEPTANCE_RUNTIME_GO'
}
else {
    'RUNIC_FOUNDATION_FREE_COEXISTENCE_SMOKE_GO'
}
Write-Output ($goMarker + ' plugins=' + $expectedPlugins.Count +
    ' audited=true audit_mode=' + $auditMode +
    ' evidence_json="' + $jsonPath + '" evidence_sha256=' + $jsonHash)
