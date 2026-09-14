[CmdletBinding()]
param(
    [string]$ClientRoot = 'E:\SteamLibrary\steamapps\common\Valheim',
    [string]$BepInExArchive = 'E:\Valheim Mods\ChazmanModsRepo\artifacts\Valheim1.0\Migration-20260909\Downloads\denikson-BepInExPack_Valheim-5.4.2350.zip',
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$PluginDirectory,
    [string]$EvidenceBase = '',
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 300,
    [ValidateRange(10, 120)][int]$DwellSeconds = 20,
    [ValidateRange(640, 1920)][int]$WindowWidth = 960,
    [ValidateRange(360, 1080)][int]$WindowHeight = 540
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# This is deliberately a visible, graphical smoke. The isolated Valheim window and
# the BepInEx console supplied by the pinned pack may briefly take focus. The harness
# never sends input, never enters a world, and terminates only its Start-Process object.
# A harness-only, source-pinned guard establishes the isolated save root and disables
# all process save writes before platform initialization. A changed Valheim/BepInEx/
# Doorstop/guard byte or Steam build fails closed before launch.
$baseline = [ordered]@{
    valheim_release = '1.0'
    valheim_runtime_version = '1.0.7'
    valheim_network_version = '39'
    client_app_id = '892970'
    client_build_id = '25185596'
    client_executable_sha256 = '3ECC2EEA5CE2ACFCAA6868AB7D8252B9A07910EA440405E62817890F4EF6BD50'
    client_assembly_sha256 = 'A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC'
    client_unityplayer_sha256 = '4D161E15D8CCDB32EB73262E7A3E0A66F8C175B50A38E22AE5B0E8FB9AEA98F3'
    bepinex_pack_version = '5.4.2350'
    bepinex_file_version = '5.4.23.5'
    bepinex_archive_sha256 = '37A91C000B4E88F2ED7A4BD7D812239852D2E36CBF0FF0A9F5FAACFBA46B105F'
    packaged_proxy_sha256 = '93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8'
    doorstop_config_sha256 = '4D5C6DFA0F771C6A5B1B0C559ACA0BD0ECE7D08B08FFF894708DC3B73CE73CFC'
    isolation_guard_guid = '000.runic.smoke.isolation-guard'
    isolation_guard_name = '000 Runic Smoke Isolation Guard'
    isolation_guard_version = '1.0.0'
    isolation_guard_source_sha256 = '771DD52F079467134C5A4933852FF912BCD11CB41BC47293C54CCB839DE83C27'
    isolation_guard_project_sha256 = '4E9122ACA38D5DD53A1104CB2CB36DC386ED3A1B562FB4AAE86E31D1D053BA15'
    isolation_guard_assembly_sha256 = 'C169241F08FA784ACDBC968356FB9126A4A2FB9A111413C95BB8944016FC463C'
    core_sha256 = [ordered]@{
        '0Harmony.dll' = '1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031'
        '0Harmony.xml' = 'D1F02FC3ADA3A13DA307DE421225BFE56EBE24064370980979391C4BE021672F'
        '0Harmony20.dll' = '5708451A91EAECEE71A49B1881FB081EE22142E11DE04E8575F4F6832FA16D0E'
        'BepInEx.dll' = 'F09821B2A7B990C6F50C5EF23229635303CE675374B70EDC6F5A9B960CB818E3'
        'BepInEx.Harmony.dll' = '2F0270073E307095CE980F578BFC9489F7AC98D360B9D41F77370A84C33EDD7A'
        'BepInEx.Harmony.xml' = 'A04FEDF08F7C81F5D01ABA6F2840A7FFCE50B79BBD24587D8DBE69AB73971D29'
        'BepInEx.Preloader.dll' = 'FB21BF85C462BC5376435ECCFED23F07A36BAA94F770FF35CD567E17D2997C8D'
        'BepInEx.Preloader.xml' = 'AB433415CB8F006F683C0C193D140B92F0244568E6E443D53A5C4C18A632DC98'
        'BepInEx.xml' = '65B9D6E40C8645E04CF1CF6342C2812F52766F2D0FC1B5736B03F17FB23DF11E'
        'HarmonyXInterop.dll' = 'DAC1B52655F03BA00773EB63894BFD0DA4D4DF6D47041A64CFD4B1DA757371BA'
        'Mono.Cecil.dll' = '7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6'
        'Mono.Cecil.Mdb.dll' = '5896D1898F616701FFF18F3B2C71E6B844D2390EF9F41E1C5FCCCE8CB27C698E'
        'Mono.Cecil.Pdb.dll' = '174DB44A067F58561510AF746F3CAEB032037762C57A31C8D9EE32DB25174984'
        'Mono.Cecil.Rocks.dll' = '54AC539FB5DDC8B44C0E9ACD0FCB7324F89D1A072EDF8EBC1B06DD691E3D3927'
        'MonoMod.RuntimeDetour.dll' = '40E49BB314391CD7BDDC2644F8553EEBA92C194B940836B103DF16955C464E0C'
        'MonoMod.RuntimeDetour.xml' = '54887808960D156550B37D602D08847607AA9E908D039F2765FB0B5E79394AA4'
        'MonoMod.Utils.dll' = '9D1495F147AC93C4F81F84538C1A326E8F8A6AEFC78D6289D798F3CE1162C5E9'
        'MonoMod.Utils.xml' = '0577B362023A3432D6E8D7934C5EDDC3E08FDBB19E191AF083E341562C5EDE38'
    }
}

$scanRules = @(
    [ordered]@{ id = 'bepinex-error'; pattern = '(?im)^\s*\[(?:Fatal|Error)\s*:[^\r\n]*' }
    [ordered]@{ id = 'unhandled-runtime'; pattern = '(?im)^\s*(?:Fatal error|Unhandled exception)\b[^\r\n]*' }
    [ordered]@{ id = 'binary-or-type-exception'; pattern = '(?im)\b(?:BadImageFormatException|FileLoadException|FileNotFoundException|MethodAccessException|MissingFieldException|MissingMethodException|ReflectionTypeLoadException|TargetInvocationException|TypeInitializationException|TypeLoadException)\b[^\r\n]*' }
    [ordered]@{ id = 'harmony-patch-failure'; pattern = '(?im)\b(?:Exception while patching|HarmonyException|No target method specified|Undefined target method)\b[^\r\n]*' }
    [ordered]@{ id = 'assembly-load-failure'; pattern = '(?im)\b(?:Could not load file or assembly|Could not resolve assembly|Error loading plugin|Failed to load (?:assembly|plugin))\b[^\r\n]*' }
    [ordered]@{ id = 'loader-warning-failure'; pattern = '(?im)^\s*\[Warning\s*:\s*(?:BepInEx|HarmonyX?)[^\r\n]*(?:could not find|exception|failed)[^\r\n]*' }
    [ordered]@{ id = 'doorstop-bootstrap-failure'; pattern = '(?im)^\s*(?:Doorstop disabled!?|Error invoking code!|Could not find target assembly!|Failed to [^\r\n]*hook)[^\r\n]*' }
)

function Require {
    param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-IsSameOrChildPath {
    param([string]$Candidate, [string]$Parent)
    $candidatePath = (Get-NormalizedPath $Candidate) + [IO.Path]::DirectorySeparatorChar
    $parentPath = (Get-NormalizedPath $Parent) + [IO.Path]::DirectorySeparatorChar
    return $candidatePath.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativeChildPath {
    param([string]$Root, [string]$Child)
    $rootPath = (Get-NormalizedPath $Root) + [IO.Path]::DirectorySeparatorChar
    $childPath = [IO.Path]::GetFullPath($Child)
    Require ($childPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) "Path is outside catalog root: $childPath"
    return $childPath.Substring($rootPath.Length).Replace('\', '/')
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256Hex {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally { $sha.Dispose() }
}

function Get-DirectoryFingerprint {
    param([Parameter(Mandatory = $true)][string]$Path, [bool]$HashFileContents = $true)
    $fullPath = Get-NormalizedPath $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [pscustomobject][ordered]@{ path = $fullPath; exists = $false; directory_count = 0; file_count = 0; total_bytes = [int64]0; fingerprint_sha256 = Get-TextSha256Hex 'missing'; content_hashes_included = $HashFileContents }
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (-not $item.PSIsContainer) {
        $contentHash = if ($HashFileContents) { Get-FileSha256Hex $fullPath } else { '(metadata-only)' }
        $line = 'F|' + $item.Name + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks + '|' + $contentHash
        return [pscustomobject][ordered]@{ path = $fullPath; exists = $true; directory_count = 0; file_count = 1; total_bytes = [int64]$item.Length; fingerprint_sha256 = Get-TextSha256Hex $line; content_hashes_included = $HashFileContents }
    }
    $lines = [Collections.Generic.List[string]]::new()
    $directories = @(Get-ChildItem -LiteralPath $fullPath -Directory -Recurse -Force | Sort-Object FullName)
    $files = @(Get-ChildItem -LiteralPath $fullPath -File -Recurse -Force | Sort-Object FullName)
    foreach ($directory in $directories) {
        $relative = Get-RelativeChildPath $fullPath $directory.FullName
        $lines.Add('D|' + $relative + '|' + $directory.LastWriteTimeUtc.Ticks)
    }
    $totalBytes = [int64]0
    foreach ($file in $files) {
        $relative = Get-RelativeChildPath $fullPath $file.FullName
        $totalBytes += [int64]$file.Length
        $contentHash = if ($HashFileContents) { Get-FileSha256Hex $file.FullName } else { '(metadata-only)' }
        $lines.Add('F|' + $relative + '|' + $file.Length + '|' + $file.LastWriteTimeUtc.Ticks + '|' + $contentHash)
    }
    return [pscustomobject][ordered]@{
        path = $fullPath; exists = $true; directory_count = $directories.Count; file_count = $files.Count
        total_bytes = $totalBytes; fingerprint_sha256 = Get-TextSha256Hex ([string]::Join("`n", $lines)); content_hashes_included = $HashFileContents
    }
}

function Get-FileCatalog {
    param([Parameter(Mandatory = $true)][string]$Root)
    $fullRoot = Get-NormalizedPath $Root
    $result = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $fullRoot -File -Recurse -Force | Sort-Object FullName)) {
        $result += [pscustomobject][ordered]@{ path = Get-RelativeChildPath $fullRoot $file.FullName; bytes = [int64]$file.Length; sha256 = Get-FileSha256Hex $file.FullName }
    }
    return $result
}

function Get-CatalogSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Catalog)
    return Get-TextSha256Hex ([string]::Join("`n", @($Catalog | ForEach-Object { $_.path + '|' + $_.bytes + '|' + $_.sha256 })))
}

function Save-FileSnapshotEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $sourcePath = Get-NormalizedPath $Source
    $destinationPath = Get-NormalizedPath $Destination
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        return [pscustomobject][ordered]@{
            label = $Label; live_path = $sourcePath; exists = $false; bytes = [int64]0
            sha256 = Get-TextSha256Hex 'missing'; last_write_utc = $null; creation_utc = $null
            attributes = $null; evidence_copy = $null
        }
    }
    Require (-not (Test-Path -LiteralPath $destinationPath)) "Snapshot evidence path already exists: $destinationPath"
    $destinationParent = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) { New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null }
    $bytes = $null; $afterRead = $null; $capturedStableBoundary = $false
    for ($attempt = 1; $attempt -le 5 -and -not $capturedStableBoundary; $attempt++) {
        $captureBefore = Get-Item -LiteralPath $sourcePath -Force
        Require ([int64]$captureBefore.Length -le [int]::MaxValue) "Volatile Steam metadata is unexpectedly large: $sourcePath"
        $candidateBytes = [byte[]]::new([int]$captureBefore.Length)
        $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
        $stream = [IO.File]::Open($sourcePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        try {
            $totalRead = 0
            while ($totalRead -lt $candidateBytes.Length) {
                $read = $stream.Read($candidateBytes, $totalRead, $candidateBytes.Length - $totalRead)
                if ($read -eq 0) { break }
                $totalRead += $read
            }
        }
        finally { $stream.Dispose() }
        $captureAfter = Get-Item -LiteralPath $sourcePath -Force
        if ($totalRead -eq $candidateBytes.Length -and
            $captureBefore.Length -eq $captureAfter.Length -and
            $captureBefore.LastWriteTimeUtc.Ticks -eq $captureAfter.LastWriteTimeUtc.Ticks) {
            $bytes = $candidateBytes; $afterRead = $captureAfter; $capturedStableBoundary = $true
        }
        else { Start-Sleep -Milliseconds 250 }
    }
    Require $capturedStableBoundary "Volatile Steam metadata changed during every shared-read snapshot attempt: $sourcePath"
    [IO.File]::WriteAllBytes($destinationPath, $bytes)
    $snapshotHash = Get-FileSha256Hex $destinationPath
    Require ([int64]$bytes.Length -eq [int64]$afterRead.Length) "Volatile Steam metadata snapshot length does not match its stable source: $sourcePath"
    return [pscustomobject][ordered]@{
        label = $Label; live_path = $sourcePath; exists = $true; bytes = [int64]$afterRead.Length
        sha256 = $snapshotHash; last_write_utc = $afterRead.LastWriteTimeUtc.ToString('O')
        creation_utc = $afterRead.CreationTimeUtc.ToString('O'); attributes = [string]$afterRead.Attributes
        evidence_copy = $destinationPath
    }
}

function Assert-Hash {
    param([string]$Path, [string]$Expected, [string]$Label)
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "$Label is missing: $Path"
    $actual = Get-FileSha256Hex $Path
    Require ($actual -ceq $Expected) "$Label hash drifted; expected $Expected, found ${actual}: $Path"
    return $actual
}

function Get-SteamInstallEvidence {
    param([string]$ContentRoot, [string]$ExpectedAppId, [string]$ExpectedBuildId)
    $root = Get-NormalizedPath $ContentRoot
    $directory = [IO.DirectoryInfo]::new($root)
    while ($null -ne $directory -and $directory.Name -ine 'common') { $directory = $directory.Parent }
    Require ($null -ne $directory -and $null -ne $directory.Parent) "Could not locate Steam's common ancestor for $root."
    $manifestPath = Join-Path $directory.Parent.FullName ('appmanifest_' + $ExpectedAppId + '.acf')
    Require (Test-Path -LiteralPath $manifestPath -PathType Leaf) "Steam app manifest is missing: $manifestPath"
    $text = [IO.File]::ReadAllText($manifestPath)
    $values = [ordered]@{}
    foreach ($key in @('appid', 'buildid', 'TargetBuildID', 'StateFlags', 'installdir', 'LauncherPath')) {
        $match = [regex]::Match($text, '(?im)^\s*"' + [regex]::Escape($key) + '"\s+"(?<value>[^"]*)"\s*$')
        Require $match.Success "Steam app manifest is missing '$key': $manifestPath"
        $values[$key] = $match.Groups['value'].Value
    }
    Require ([string]$values.appid -ceq $ExpectedAppId) "Steam app id drifted in $manifestPath."
    Require ([string]$values.buildid -ceq $ExpectedBuildId) "Steam build id drifted; expected $ExpectedBuildId, found $($values.buildid)."
    Require ([string]$values.TargetBuildID -ceq $ExpectedBuildId) "Steam target build is not fully installed."
    Require ([string]$values.StateFlags -ceq '4') "Steam app is not fully installed (StateFlags=$($values.StateFlags))."
    $expectedRoot = Get-NormalizedPath (Join-Path $directory.FullName ([string]$values.installdir))
    Require ($root -ceq $expectedRoot) "Content root does not match Steam manifest installdir: $expectedRoot"
    return [pscustomobject][ordered]@{
        app_id = $ExpectedAppId; build_id = [string]$values.buildid; target_build_id = [string]$values.TargetBuildID
        state_flags = [string]$values.StateFlags; manifest_path = Get-NormalizedPath $manifestPath
        manifest_sha256 = Get-FileSha256Hex $manifestPath; launcher_path = [string]$values.LauncherPath; content_root = $root
    }
}

function Get-ValheimSteamCloudDirectories {
    param([Parameter(Mandatory = $true)][string]$SteamLauncherPath)
    $steamRoot = Split-Path -Parent (Get-NormalizedPath $SteamLauncherPath)
    $userDataRoot = Join-Path $steamRoot 'userdata'
    if (-not (Test-Path -LiteralPath $userDataRoot -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $userDataRoot -Directory -Force | ForEach-Object {
            $candidate = Join-Path $_.FullName '892970'
            if (Test-Path -LiteralPath $candidate -PathType Container) { Get-NormalizedPath $candidate }
        } | Sort-Object -Unique)
}

function Get-RunningSteamProcessEvidence {
    param([Parameter(Mandatory = $true)][string]$ExpectedExecutable)
    $expected = Get-NormalizedPath $ExpectedExecutable
    $matches = @()
    foreach ($candidate in @(Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) {
        try {
            $image = Get-NormalizedPath $candidate.MainModule.FileName
            if ($image -ceq $expected) {
                $matches += [pscustomobject][ordered]@{
                    process_id = $candidate.Id
                    image = $image
                    start_time_utc = $candidate.StartTime.ToUniversalTime().ToString('O')
                }
            }
        }
        catch { }
    }
    Require ($matches.Count -gt 0) "The exact Steam client is not running: $expected"
    return $matches
}

function Add-RegistryFingerprintLines {
    param([Microsoft.Win32.RegistryKey]$Key, [string]$RelativePath, [Collections.Generic.List[string]]$Lines, [Collections.Generic.List[string]]$Excluded)
    foreach ($name in @($Key.GetValueNames() | Sort-Object)) {
        $qualified = if ($RelativePath.Length -eq 0) { $name } else { $RelativePath + '\' + $name }
        if ($name -match '^(?:unity\.player_session(?:_count|id)|unity_connect\.(?:mega_session_id|session_id))_h\d+$') {
            $Excluded.Add($qualified)
            continue
        }
        $kind = $Key.GetValueKind($name)
        $value = $Key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($value -is [byte[]]) { $encoded = [Convert]::ToBase64String($value) }
        elseif ($value -is [string[]]) { $encoded = [string]::Join('|', @($value | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) })) }
        else { $encoded = [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture) }
        $Lines.Add('V|' + $qualified + '|' + $kind + '|' + $encoded)
    }
    foreach ($subName in @($Key.GetSubKeyNames() | Sort-Object)) {
        $subKey = $Key.OpenSubKey($subName, $false)
        try {
            $relative = if ($RelativePath.Length -eq 0) { $subName } else { $RelativePath + '\' + $subName }
            $Lines.Add('K|' + $relative)
            Add-RegistryFingerprintLines $subKey $relative $Lines $Excluded
        }
        finally { if ($null -ne $subKey) { $subKey.Dispose() } }
    }
}

function Get-StableValheimRegistryFingerprint {
    $subKeyPath = 'Software\IronGate\Valheim'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKeyPath, $false)
    if ($null -eq $key) {
        return [pscustomobject][ordered]@{ path = 'HKCU:\' + $subKeyPath; exists = $false; fingerprint_sha256 = Get-TextSha256Hex 'missing'; excluded_volatile_values = @() }
    }
    $lines = [Collections.Generic.List[string]]::new()
    $excluded = [Collections.Generic.List[string]]::new()
    try { Add-RegistryFingerprintLines $key '' $lines $excluded }
    finally { $key.Dispose() }
    return [pscustomobject][ordered]@{
        path = 'HKCU:\' + $subKeyPath; exists = $true; fingerprint_sha256 = Get-TextSha256Hex ([string]::Join("`n", $lines))
        stable_entry_count = $lines.Count; excluded_volatile_values = @($excluded)
    }
}

function Get-BepInPluginIdentities {
    param([Parameter(Mandatory = $true)][string]$Path)
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        $identities = @()
        $queue = [Collections.Queue]::new()
        foreach ($type in $assembly.MainModule.Types) { $queue.Enqueue($type) }
        while ($queue.Count -gt 0) {
            $type = $queue.Dequeue()
            foreach ($nested in $type.NestedTypes) { $queue.Enqueue($nested) }
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -ne 'BepInEx.BepInPlugin') { continue }
                Require ($attribute.ConstructorArguments.Count -eq 3) "BepInPlugin attribute has an unexpected shape: $Path"
                $identities += [pscustomobject][ordered]@{
                    assembly = [string]$assembly.Name.Name; guid = [string]$attribute.ConstructorArguments[0].Value
                    name = [string]$attribute.ConstructorArguments[1].Value; version = [string]$attribute.ConstructorArguments[2].Value
                }
            }
        }
        return $identities
    }
    finally { $assembly.Dispose() }
}

function Assert-IsolationGuardContract {
    param([Parameter(Mandatory = $true)][string]$Path)
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        $types = [Collections.Generic.List[object]]::new()
        $queue = [Collections.Queue]::new()
        foreach ($type in $assembly.MainModule.Types) { $queue.Enqueue($type) }
        while ($queue.Count -gt 0) {
            $type = $queue.Dequeue()
            $types.Add($type)
            foreach ($nested in $type.NestedTypes) { $queue.Enqueue($nested) }
        }
        $pluginType = @($types | Where-Object FullName -ceq 'RunicSmoke.ClientIsolation.Plugin')
        Require ($pluginType.Count -eq 1) 'Isolation guard plugin type contract drifted.'
        $awake = @($pluginType[0].Methods | Where-Object Name -ceq 'Awake')
        $prefix = @($pluginType[0].Methods | Where-Object Name -ceq 'DisableSteamSaveProvider')
        Require ($awake.Count -eq 1 -and $awake[0].HasBody) 'Isolation guard Awake contract is missing.'
        Require ($prefix.Count -eq 1 -and $prefix[0].HasBody) 'Isolation guard save-provider prefix is missing.'
        Require ($prefix[0].IsStatic -and $prefix[0].ReturnType.FullName -ceq 'System.Boolean') 'Isolation guard prefix return contract drifted.'
        Require ($prefix[0].Parameters.Count -eq 1 -and $prefix[0].Parameters[0].ParameterType.IsByReference) 'Isolation guard prefix parameter contract drifted.'
        Require ($prefix[0].Parameters[0].ParameterType.GetElementType().FullName -ceq 'Splatform.ISaveDataProvider') 'Isolation guard prefix no longer nulls ISaveDataProvider.'

        $instructions = @($awake[0].Body.Instructions)
        $methodCalls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { [string]$_.Operand.FullName })
        $strings = @($instructions | Where-Object { $_.Operand -is [string] } | ForEach-Object { [string]$_.Operand })
        $requiredCalls = @(
            'PlatformManager::get_DistributionPlatform',
            'Utils::SetSaveDataPath',
            'HarmonyLib.AccessTools::Field',
            'System.Reflection.FieldInfo::get_IsStatic',
            'System.Reflection.FieldInfo::get_FieldType',
            'System.Reflection.FieldInfo::GetValue',
            'SaveSystem::SetSessionFlags',
            'SaveSystem::HasSessionFlag',
            'HarmonyLib.Harmony::Patch',
            'HarmonyLib.Harmony::GetPatchInfo',
            'System.Environment::FailFast'
        )
        foreach ($requiredCall in $requiredCalls) {
            Require (@($methodCalls | Where-Object { $_.IndexOf($requiredCall, [StringComparison]::Ordinal) -ge 0 }).Count -gt 0) "Isolation guard is missing required call: $requiredCall"
        }
        foreach ($requiredString in @(
                'RUNIC_SMOKE_SAVEDIR',
                'RUNIC_SMOKE_LIVE_VALHEIM_DATA',
                'Splatform.Steam.SteamPlatform, Splatform.Steam',
                '[RUNIC_SMOKE_ISOLATION_READY]',
                '[RUNIC_SMOKE_ISOLATION_FAILED]')) {
            Require (@($strings | Where-Object { $_.IndexOf($requiredString, [StringComparison]::Ordinal) -ge 0 }).Count -gt 0) "Isolation guard is missing required marker: $requiredString"
        }
        $catchHandlers = @($awake[0].Body.ExceptionHandlers | Where-Object {
                $_.HandlerType -eq [Mono.Cecil.Cil.ExceptionHandlerType]::Catch -and
                $null -ne $_.CatchType -and $_.CatchType.FullName -ceq 'System.Exception'
            })
        Require ($catchHandlers.Count -eq 1) 'Isolation guard does not have its single fail-closed exception boundary.'
        $failFastCalls = @($instructions | Where-Object {
                $_.Operand -is [Mono.Cecil.MethodReference] -and
                ([string]$_.Operand.FullName).IndexOf('System.Environment::FailFast', [StringComparison]::Ordinal) -ge 0
            })
        Require ($failFastCalls.Count -eq 1 -and $failFastCalls[0].Offset -ge $catchHandlers[0].HandlerStart.Offset) 'Isolation guard FailFast is not in its exception handler.'
        return [pscustomobject][ordered]@{
            assembly_path = Get-NormalizedPath $Path
            assembly_sha256 = Get-FileSha256Hex $Path
            plugin_type = $pluginType[0].FullName
            required_call_contracts = $requiredCalls
            required_marker_contracts = @('RUNIC_SMOKE_SAVEDIR', 'RUNIC_SMOKE_LIVE_VALHEIM_DATA', 'SteamPlatform.SaveDataProvider=null', 'DontSaveAnything', 'READY/FAILED')
            fail_closed_exception_handler = $true
            fail_fast_call_in_handler = $true
            save_provider_prefix_signature = 'static bool DisableSteamSaveProvider(ref Splatform.ISaveDataProvider)'
        }
    }
    finally { $assembly.Dispose() }
}

function Read-SharedTextFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-SharedFileBoundaryEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = Get-NormalizedPath $Path
    Require (Test-Path -LiteralPath $fullPath -PathType Leaf) "Required shared log is missing: $fullPath"
    $info = Get-Item -LiteralPath $fullPath -Force
    return [pscustomobject][ordered]@{
        path = $fullPath; length = [int64]$info.Length
        last_write_utc = $info.LastWriteTimeUtc.ToString('O')
    }
}

function Save-AppendedSharedLogEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Int64]$BeforeLength,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $fullPath = Get-NormalizedPath $Path
    $destinationPath = Get-NormalizedPath $Destination
    # Steam begins AutoCloud asynchronously when the app exits. Give it a
    # bounded opportunity to start, then require a stable boundary before copy.
    Start-Sleep -Milliseconds 3000
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $previousLength = [int64]-1; $previousTicks = [int64]-1; $stableSamples = 0; $stableInfo = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        Require (Test-Path -LiteralPath $fullPath -PathType Leaf) "Steam cloud log disappeared: $fullPath"
        $current = Get-Item -LiteralPath $fullPath -Force
        Require ([int64]$current.Length -ge $BeforeLength) "Steam cloud log was truncated or rotated during the run: $fullPath"
        if ([int64]$current.Length -eq $previousLength -and $current.LastWriteTimeUtc.Ticks -eq $previousTicks) { $stableSamples++ }
        else { $stableSamples = 0 }
        $previousLength = [int64]$current.Length; $previousTicks = $current.LastWriteTimeUtc.Ticks; $stableInfo = $current
        if ($stableSamples -ge 8) { break }
        Start-Sleep -Milliseconds 250
    }
    Require ($stableSamples -ge 8 -and $null -ne $stableInfo) "Steam cloud log did not reach a stable post-exit boundary: $fullPath"
    $allBytes = $null; $afterRead = $null; $capturedStableBoundary = $false
    for ($attempt = 1; $attempt -le 5 -and -not $capturedStableBoundary; $attempt++) {
        $captureBefore = Get-Item -LiteralPath $fullPath -Force
        Require ([int64]$captureBefore.Length -ge $BeforeLength -and [int64]$captureBefore.Length -le [int]::MaxValue) 'Steam cloud log length is invalid at capture.'
        $candidateBytes = [byte[]]::new([int]$captureBefore.Length)
        $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
        $stream = [IO.File]::Open($fullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        try {
            $totalRead = 0
            while ($totalRead -lt $candidateBytes.Length) {
                $read = $stream.Read($candidateBytes, $totalRead, $candidateBytes.Length - $totalRead)
                if ($read -eq 0) { break }
                $totalRead += $read
            }
        }
        finally { $stream.Dispose() }
        $captureAfter = Get-Item -LiteralPath $fullPath -Force
        if ($totalRead -eq $candidateBytes.Length -and
            $captureBefore.Length -eq $captureAfter.Length -and
            $captureBefore.LastWriteTimeUtc.Ticks -eq $captureAfter.LastWriteTimeUtc.Ticks) {
            $allBytes = $candidateBytes; $afterRead = $captureAfter; $capturedStableBoundary = $true
        }
        else { Start-Sleep -Milliseconds 250 }
    }
    Require $capturedStableBoundary 'Steam cloud log changed during every shared-read capture attempt.'
    $sliceLength = [int64]$allBytes.Length - $BeforeLength
    Require ($sliceLength -ge 0 -and $sliceLength -le [int]::MaxValue) 'Steam cloud log slice length is invalid.'
    $slice = [byte[]]::new([int]$sliceLength)
    if ($sliceLength -gt 0) { [Buffer]::BlockCopy($allBytes, [int]$BeforeLength, $slice, 0, [int]$sliceLength) }
    $destinationParent = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) { New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null }
    Require (-not (Test-Path -LiteralPath $destinationPath)) "Cloud-log evidence already exists: $destinationPath"
    [IO.File]::WriteAllBytes($destinationPath, $slice)
    return [pscustomobject][ordered]@{
        source_path = $fullPath; before_offset = $BeforeLength; after_length = [int64]$allBytes.Length
        appended_length = $sliceLength; appended_slice_path = $destinationPath
        appended_slice_sha256 = Get-FileSha256Hex $destinationPath
        source_last_write_utc_after = $afterRead.LastWriteTimeUtc.ToString('O')
    }
}

function Get-LogSnapshots {
    param([Parameter(Mandatory = $true)][object[]]$Definitions)
    $result = @()
    foreach ($definition in $Definitions) {
        $exists = Test-Path -LiteralPath $definition.path -PathType Leaf
        $text = if ($exists) { Read-SharedTextFile $definition.path } else { '' }
        $result += [pscustomobject][ordered]@{ label = [string]$definition.label; path = [string]$definition.path; exists = [bool]$exists; text = [string]$text }
    }
    return $result
}

function Find-LogFailures {
    param([Parameter(Mandatory = $true)][object[]]$Snapshots)
    $findings = @(); $seen = @{}
    foreach ($snapshot in $Snapshots) {
        foreach ($rule in $scanRules) {
            foreach ($match in [regex]::Matches($snapshot.text, $rule.pattern)) {
                $excerpt = [regex]::Replace($match.Value, '\s+', ' ').Trim()
                if ($excerpt.Length -gt 300) { $excerpt = $excerpt.Substring(0, 300) }
                $key = $snapshot.label + '|' + $rule.id + '|' + $excerpt
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $findings += [pscustomobject][ordered]@{ log = $snapshot.label; rule = $rule.id; excerpt = $excerpt }
                }
            }
        }
    }
    return $findings
}

function Assert-HealthyLogs {
    param([object[]]$Snapshots, [string]$Phase)
    $findings = @(Find-LogFailures $Snapshots)
    if ($findings.Count -gt 0) {
        $first = $findings[0]
        throw "Log scan failed during $Phase ($($first.log)/$($first.rule)): $($first.excerpt)"
    }
}

function Quote-ProcessArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    Require ($Value.IndexOf('"') -lt 0) 'A process argument unexpectedly contains a quote.'
    return '"' + $Value + '"'
}

function Invoke-WithIsolatedLaunchEnvironment {
    param([string]$TargetAssembly, [string]$CoreSearchPath, [string]$SaveRoot, [string]$LiveDataRoot, [scriptblock]$Launch)
    $names = @(
        'SteamAppId', 'DOORSTOP_DISABLE', 'DOORSTOP_INITIALIZED', 'DOORSTOP_ENABLED',
        'DOORSTOP_TARGET_ASSEMBLY', 'DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE',
        'RUNIC_SMOKE_SAVEDIR', 'RUNIC_SMOKE_LIVE_VALHEIM_DATA'
    )
    $prior = @{}
    foreach ($name in $names) {
        $prior[$name] = [pscustomobject]@{
            present = Test-Path -LiteralPath ('Env:\' + $name)
            value = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
    }
    try {
        [Environment]::SetEnvironmentVariable('SteamAppId', '892970', 'Process')
        # Doorstop checks variable presence, so a present empty value is still a
        # guard. Delete both guards from the environment inherited by the child.
        Remove-Item -LiteralPath 'Env:\DOORSTOP_DISABLE' -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath 'Env:\DOORSTOP_INITIALIZED' -ErrorAction SilentlyContinue
        [Environment]::SetEnvironmentVariable('DOORSTOP_ENABLED', '1', 'Process')
        [Environment]::SetEnvironmentVariable('DOORSTOP_TARGET_ASSEMBLY', $TargetAssembly, 'Process')
        [Environment]::SetEnvironmentVariable('DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE', $CoreSearchPath, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_SAVEDIR', $SaveRoot, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_LIVE_VALHEIM_DATA', $LiveDataRoot, 'Process')
        return & $Launch
    }
    finally {
        foreach ($name in $names) {
            if ($prior[$name].present) { Set-Item -LiteralPath ('Env:\' + $name) -Value ([string]$prior[$name].value) }
            else { Remove-Item -LiteralPath ('Env:\' + $name) -ErrorAction SilentlyContinue }
        }
        foreach ($name in $names) {
            $present = Test-Path -LiteralPath ('Env:\' + $name)
            $value = [Environment]::GetEnvironmentVariable($name, 'Process')
            Require ($present -eq [bool]$prior[$name].present -and [string]::Equals([string]$prior[$name].value, [string]$value, [StringComparison]::Ordinal)) "Process environment '$name' was not restored."
        }
    }
}

function Stop-TrackedProcess {
    param([AllowNull()][Diagnostics.Process]$Process, [string]$ExpectedExecutable)
    if ($null -eq $Process) { return $false }
    $Process.Refresh()
    if ($Process.HasExited) { return $false }
    $actualExecutable = Get-NormalizedPath $Process.MainModule.FileName
    Require ($actualExecutable -ceq (Get-NormalizedPath $ExpectedExecutable)) "Tracked process image changed unexpectedly: $actualExecutable"
    # Never enumerate or terminate by name. Kill exactly the object returned by Start-Process.
    $Process.Kill()
    Require ($Process.WaitForExit(30000)) 'The tracked isolated client did not exit within 30 seconds.'
    return $true
}

function Remove-CreatedJunctions {
    param([object[]]$Junctions, [string]$AllowedRoot)
    foreach ($junction in @($Junctions | Sort-Object path -Descending)) {
        $path = Get-NormalizedPath $junction.path
        Require (Test-IsSameOrChildPath $path $AllowedRoot) "Refusing to remove a junction outside the isolated run root: $path"
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            Require ($item.LinkType -ceq 'Junction') "Refusing to remove a non-junction path: $path"
            Remove-Item -LiteralPath $path -Force
            Require (-not (Test-Path -LiteralPath $path)) "Created junction was not removed: $path"
        }
    }
}

$scriptPath = Get-NormalizedPath $MyInvocation.MyCommand.Path
$scriptHashAtStart = Get-FileSha256Hex $scriptPath
$repoRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '..')
$clientRootPath = Get-NormalizedPath $ClientRoot
$archivePath = Get-NormalizedPath $BepInExArchive
$pluginSourceRoot = Get-NormalizedPath $PluginDirectory
$guardProject = Get-NormalizedPath (Join-Path $PSScriptRoot 'Valheim10ClientIsolationGuard\Valheim10ClientIsolationGuard.csproj')
$guardSource = Get-NormalizedPath (Join-Path $PSScriptRoot 'Valheim10ClientIsolationGuard\Plugin.cs')
$clientExecutable = Join-Path $clientRootPath 'valheim.exe'
$clientAssembly = Join-Path $clientRootPath 'valheim_Data\Managed\assembly_valheim.dll'
$clientUnityPlayer = Join-Path $clientRootPath 'UnityPlayer.dll'

Require (Test-Path -LiteralPath $clientRootPath -PathType Container) "Valheim client root is missing: $clientRootPath"
Require (Test-Path -LiteralPath $pluginSourceRoot -PathType Container) "Explicit plugin DLL directory is missing: $pluginSourceRoot"
Assert-Hash $clientExecutable $baseline.client_executable_sha256 'Valheim 1.0 client executable' | Out-Null
Assert-Hash $clientAssembly $baseline.client_assembly_sha256 'Valheim 1.0 client assembly' | Out-Null
Assert-Hash $clientUnityPlayer $baseline.client_unityplayer_sha256 'Valheim 1.0 client UnityPlayer' | Out-Null
Assert-Hash $archivePath $baseline.bepinex_archive_sha256 'BepInExPack Valheim 5.4.2350 archive' | Out-Null
Assert-Hash $guardProject $baseline.isolation_guard_project_sha256 'isolation guard project' | Out-Null
Assert-Hash $guardSource $baseline.isolation_guard_source_sha256 'isolation guard source' | Out-Null
$clientSteam = Get-SteamInstallEvidence $clientRootPath $baseline.client_app_id $baseline.client_build_id
$steamRootPath = Get-NormalizedPath (Split-Path -Parent $clientSteam.launcher_path)
$steamCloudLogPath = Join-Path $steamRootPath 'logs\cloud_log.txt'

$defaultValheimData = Get-NormalizedPath (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\IronGate\Valheim')
Require (-not (Test-IsSameOrChildPath $pluginSourceRoot $clientRootPath)) 'Plugin input may not be the live Valheim installation or one of its children.'
Require (-not (Test-IsSameOrChildPath $pluginSourceRoot $defaultValheimData)) 'Plugin input may not be the live Valheim data directory or one of its children.'
if ([string]::IsNullOrWhiteSpace($EvidenceBase)) { $evidenceBasePath = Get-NormalizedPath (Join-Path $repoRoot 'artifacts\Valheim1.0\ClientSmoke') }
else { $evidenceBasePath = Get-NormalizedPath $EvidenceBase }
Require (-not (Test-IsSameOrChildPath $evidenceBasePath $clientRootPath)) "Evidence base may not be inside the live client: $evidenceBasePath"
Require (-not (Test-IsSameOrChildPath $evidenceBasePath $defaultValheimData)) "Evidence base may not be inside live Valheim data: $evidenceBasePath"
$existingClients = @(Get-Process -Name 'valheim' -ErrorAction SilentlyContinue)
Require ($existingClients.Count -eq 0) 'A Valheim client is already running. Close it before using the isolated smoke harness.'

New-Item -ItemType Directory -Path $evidenceBasePath -Force | Out-Null
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-plugins-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $evidenceBasePath $runId
Require (-not (Test-Path -LiteralPath $runRoot)) "Evidence root already exists and will not be overwritten: $runRoot"
New-Item -ItemType Directory -Path $runRoot | Out-Null

$process = $null; $processId = $null; $processWasStopped = $false; $createdJunctions = @()
$protectedDefinitions = @(); $protectedBefore = @(); $pluginSourceBefore = $null; $stableRegistryBefore = $null
$volatileSteamMetadataBefore = @(); $volatileSteamMetadataAfter = @(); $volatileSteamMetadataDiffs = @(); $volatileSteamMetadataDiffPath = $null
$steamCloudLogBefore = $null; $steamCloudLogEvidence = $null; $steamCloudForbiddenFindings = @()
try {
    $protectedDefinitions += [ordered]@{ label = 'live-client-install'; path = $clientRootPath; hash = $true }
    $protectedDefinitions += [ordered]@{ label = 'live-valheim-data'; path = $defaultValheimData; hash = $true }
    $steamCloudRoots = @(Get-ValheimSteamCloudDirectories $clientSteam.launcher_path)
    foreach ($cloudPath in $steamCloudRoots) {
        $userId = Split-Path (Split-Path $cloudPath -Parent) -Leaf
        # `remote` is the actual local Steam Cloud save payload. Steam's external
        # client rewrites app-level remotecache.vdf on every clean app exit even
        # when its bytes and every save are unchanged, so cache metadata is
        # captured separately and never treated as a Runic/game write.
        $protectedDefinitions += [ordered]@{ label = 'live-steam-cloud-remote-' + $userId; path = (Join-Path $cloudPath 'remote'); hash = $true }
        $volatileSteamMetadataBefore += Save-FileSnapshotEvidence `
            (Join-Path $cloudPath 'remotecache.vdf') `
            (Join-Path $runRoot ('volatile-steam-metadata\before\' + $userId + '-remotecache.vdf')) `
            ('steam-remotecache-' + $userId)
    }
    foreach ($definition in $protectedDefinitions) {
        $protectedBefore += [pscustomobject][ordered]@{ label = $definition.label; fingerprint = Get-DirectoryFingerprint $definition.path $definition.hash }
    }
    $stableRegistryBefore = Get-StableValheimRegistryFingerprint
    $pluginSourceBefore = Get-DirectoryFingerprint $pluginSourceRoot $true

    $extractRoot = Join-Path $runRoot 'loader-source'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot
    $packRoot = Join-Path $extractRoot 'BepInExPack_Valheim'
    $clientStage = Join-Path $runRoot 'client-stage'
    New-Item -ItemType Directory -Path $clientStage | Out-Null
    foreach ($runtimeFileName in @('valheim.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt')) {
        $source = Join-Path $clientRootPath $runtimeFileName
        Require (Test-Path -LiteralPath $source -PathType Leaf) "Required client runtime file is missing: $source"
        $destination = Join-Path $clientStage $runtimeFileName
        Copy-Item -LiteralPath $source -Destination $destination
        Require ((Get-FileSha256Hex $source) -ceq (Get-FileSha256Hex $destination)) "Client runtime copy drifted: $runtimeFileName"
    }
    foreach ($runtimeDirectoryName in @('D3D12', 'MonoBleedingEdge', 'Valheim_BurstDebugInformation_DoNotShip', 'valheim_Data')) {
        $target = Join-Path $clientRootPath $runtimeDirectoryName
        Require (Test-Path -LiteralPath $target -PathType Container) "Required client runtime directory is missing: $target"
        $link = Join-Path $clientStage $runtimeDirectoryName
        $created = New-Item -ItemType Junction -Path $link -Target $target
        Require ($created.LinkType -ceq 'Junction') "Failed to create isolated runtime junction: $link"
        $createdJunctions += [pscustomobject][ordered]@{ path = Get-NormalizedPath $link; target = Get-NormalizedPath $target }
    }

    $packBepInEx = Join-Path $packRoot 'BepInEx'
    Require (Test-Path -LiteralPath $packBepInEx -PathType Container) "Unexpected BepInEx archive layout: $packBepInEx"
    Copy-Item -LiteralPath $packBepInEx -Destination $clientStage -Recurse
    foreach ($loaderFile in @('.doorstop_version', 'doorstop_config.ini')) {
        Copy-Item -LiteralPath (Join-Path $packRoot $loaderFile) -Destination (Join-Path $clientStage $loaderFile)
    }
    $bepRoot = Join-Path $clientStage 'BepInEx'; $coreRoot = Join-Path $bepRoot 'core'
    $isolatedPlugins = Join-Path $bepRoot 'plugins'; $isolatedConfig = Join-Path $bepRoot 'config'; $isolatedCache = Join-Path $bepRoot 'cache'
    $isolatedSavedir = Join-Path $runRoot 'savedir'
    foreach ($directory in @($isolatedPlugins, $isolatedConfig, $isolatedCache, $isolatedSavedir)) {
        if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    }

    $packProxy = Join-Path $packRoot 'winhttp.dll'
    $packDoorstopConfig = Join-Path $packRoot 'doorstop_config.ini'
    Assert-Hash $packProxy $baseline.packaged_proxy_sha256 'packaged optimized Doorstop proxy' | Out-Null
    Assert-Hash $packDoorstopConfig $baseline.doorstop_config_sha256 'packaged Doorstop configuration' | Out-Null
    Assert-Hash (Join-Path $clientStage 'doorstop_config.ini') $baseline.doorstop_config_sha256 'isolated Doorstop configuration' | Out-Null
    $isolatedPackProxy = Join-Path $clientStage 'winhttp.dll'
    Copy-Item -LiteralPath $packProxy -Destination $isolatedPackProxy
    Assert-Hash $isolatedPackProxy $baseline.packaged_proxy_sha256 'isolated BepInExPack WINHTTP proxy' | Out-Null
    Require (-not (Test-Path -LiteralPath (Join-Path $clientStage 'version.dll'))) 'No unverified VERSION proxy alias may be staged.'

    $actualCoreFiles = @(Get-ChildItem -LiteralPath $coreRoot -File -Force | Sort-Object Name)
    Require ($actualCoreFiles.Count -eq $baseline.core_sha256.Count) 'Pinned BepInEx core file count drifted.'
    foreach ($coreFile in $actualCoreFiles) {
        Require $baseline.core_sha256.Contains($coreFile.Name) "Unexpected BepInEx core file: $($coreFile.Name)"
        Assert-Hash $coreFile.FullName ([string]$baseline.core_sha256[$coreFile.Name]) "BepInEx core $($coreFile.Name)" | Out-Null
    }
    foreach ($name in $baseline.core_sha256.Keys) { Require (Test-Path -LiteralPath (Join-Path $coreRoot $name) -PathType Leaf) "Pinned BepInEx core file is missing: $name" }
    $observedBepInExVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $coreRoot 'BepInEx.dll')).FileVersion
    Require ($observedBepInExVersion -ceq $baseline.bepinex_file_version) "BepInEx version drifted: $observedBepInExVersion"
    $coreCatalog = @(Get-FileCatalog $coreRoot); $coreCatalogSha256 = Get-CatalogSha256 $coreCatalog
    if ($null -eq ('Mono.Cecil.AssemblyDefinition' -as [type])) { Add-Type -Path (Join-Path $coreRoot 'Mono.Cecil.dll') }

    # Build the harness-only guard entirely beneath this immutable evidence root.
    # No guard output is written to the repository, live installation, or payload.
    $guardBuildRoot = Join-Path $runRoot 'guard-build'
    $guardBaseOutput = (Get-NormalizedPath (Join-Path $guardBuildRoot 'bin')) + [IO.Path]::DirectorySeparatorChar
    $guardBaseIntermediate = (Get-NormalizedPath (Join-Path $guardBuildRoot 'obj')) + [IO.Path]::DirectorySeparatorChar
    $guardBuildLog = Join-Path $runRoot 'guard-build.log'
    $guardBuildOutput = @(& dotnet build $guardProject --configuration Release --nologo `
            "-p:BaseOutputPath=$guardBaseOutput" `
            "-p:BaseIntermediateOutputPath=$guardBaseIntermediate" `
            "-p:GameManagedPath=$(Join-Path $clientRootPath 'valheim_Data\Managed')" `
            "-p:BepInExCorePath=$coreRoot" 2>&1)
    $guardBuildExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText($guardBuildLog, ([string]::Join([Environment]::NewLine, @($guardBuildOutput | ForEach-Object { [string]$_ })) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Require ($guardBuildExitCode -eq 0) "Isolation guard build failed with exit code $guardBuildExitCode; see $guardBuildLog"
    $guardAssembly = Join-Path $guardBaseOutput 'Release\netstandard2.1\Valheim10ClientIsolationGuard.dll'
    Assert-Hash $guardAssembly $baseline.isolation_guard_assembly_sha256 'built isolation guard' | Out-Null
    $guardIdentities = @(Get-BepInPluginIdentities $guardAssembly)
    Require ($guardIdentities.Count -eq 1) 'Isolation guard must expose exactly one BepInPlugin identity.'
    Require ($guardIdentities[0].guid -ceq $baseline.isolation_guard_guid) 'Isolation guard GUID drifted.'
    Require ($guardIdentities[0].name -ceq $baseline.isolation_guard_name) 'Isolation guard display name drifted.'
    Require ($guardIdentities[0].version -ceq $baseline.isolation_guard_version) 'Isolation guard version drifted.'
    $guardContractProof = Assert-IsolationGuardContract $guardAssembly

    $pluginFiles = @(); $pluginIdentities = @()
    $sourceEntries = @(Get-ChildItem -LiteralPath $pluginSourceRoot -Force)
    Require ($sourceEntries.Count -gt 0) "Explicit plugin directory is empty: $pluginSourceRoot"
    foreach ($entry in $sourceEntries) {
        Require (-not $entry.PSIsContainer -and $entry.Extension -ieq '.dll') "Plugin directory must be flat and DLL-only: $($entry.FullName)"
        Require (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Plugin input may not be a reparse point: $($entry.FullName)"
        $identities = @(Get-BepInPluginIdentities $entry.FullName)
        Require ($identities.Count -gt 0) "DLL contains no BepInPlugin identity: $($entry.FullName)"
        $destination = Join-Path $isolatedPlugins $entry.Name
        Copy-Item -LiteralPath $entry.FullName -Destination $destination
        $sourceHash = Get-FileSha256Hex $entry.FullName
        Require ((Get-FileSha256Hex $destination) -ceq $sourceHash) "Plugin copy hash mismatch: $($entry.Name)"
        $pluginFiles += [pscustomobject][ordered]@{ file = $entry.Name; bytes = [int64]$entry.Length; sha256 = $sourceHash; source_path = $entry.FullName; isolated_path = $destination }
        $pluginIdentities += $identities
    }
    Require (@($pluginIdentities | Group-Object guid | Where-Object Count -gt 1).Count -eq 0) 'Plugin payload contains duplicate BepInPlugin GUIDs.'
    Require (@($pluginIdentities | Where-Object guid -ceq $baseline.isolation_guard_guid).Count -eq 0) 'Runic payload illegally contains the harness-only isolation guard identity.'
    $isolatedGuardAssembly = Join-Path $isolatedPlugins '000-Valheim10ClientIsolationGuard.dll'
    Require (-not (Test-Path -LiteralPath $isolatedGuardAssembly)) 'Reserved isolation guard plugin filename is occupied.'
    Copy-Item -LiteralPath $guardAssembly -Destination $isolatedGuardAssembly
    Assert-Hash $isolatedGuardAssembly $baseline.isolation_guard_assembly_sha256 'isolated guard copy' | Out-Null
    Require (@(Get-ChildItem -LiteralPath $isolatedPlugins -Force).Count -eq ($pluginFiles.Count + 1)) 'Isolated plugin set differs from the Runic payload plus harness-only guard.'

    $stdoutLog = Join-Path $runRoot 'client-stdout.log'; $stderrLog = Join-Path $runRoot 'client-stderr.log'
    $unityLog = Join-Path $runRoot 'unity-player.log'; $bepInExLog = Join-Path $bepRoot 'LogOutput.log'
    $logDefinitions = @(
        [pscustomobject]@{ label = 'BepInEx'; path = $bepInExLog }
        [pscustomobject]@{ label = 'stdout'; path = $stdoutLog }
        [pscustomobject]@{ label = 'stderr'; path = $stderrLog }
        [pscustomobject]@{ label = 'Unity'; path = $unityLog }
    )
    $isolatedExecutable = Join-Path $clientStage 'valheim.exe'
    Assert-Hash $isolatedExecutable $baseline.client_executable_sha256 'isolated Valheim client executable' | Out-Null
    $targetAssembly = Join-Path $coreRoot 'BepInEx.Preloader.dll'
    $arguments = @(
        '--doorstop-enabled', 'true', '--doorstop-target-assembly', (Quote-ProcessArgument $targetAssembly),
        '--doorstop-mono-dll-search-path-override', (Quote-ProcessArgument $coreRoot),
        '-logFile', (Quote-ProcessArgument $unityLog),
        '-screen-fullscreen', '0', '-screen-width', $WindowWidth.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-screen-height', $WindowHeight.ToString([Globalization.CultureInfo]::InvariantCulture)
    )

    $timer = [Diagnostics.Stopwatch]::StartNew(); $chainloaderSeconds = -1.0; $menuSeconds = -1.0; $windowSeconds = -1.0
    $dwellElapsedSeconds = -1.0; $peakWorkingSetBytes = [int64]0; $readyLog = ''; $processStartedUtc = [DateTime]::UtcNow.ToString('O')
    $steamProcessesAtLaunch = @(); $steamProcessesAfterStop = @(); $lastSteamCheckSeconds = -1.0
    try {
        # This is intentionally adjacent to Start-Process. Steam must remain open
        # for the entire graphical client smoke, but the harness never manages it.
        $steamProcessesAtLaunch = @(Get-RunningSteamProcessEvidence $clientSteam.launcher_path)
        $steamCloudLogBefore = Get-SharedFileBoundaryEvidence $steamCloudLogPath
        $process = Invoke-WithIsolatedLaunchEnvironment $targetAssembly $coreRoot $isolatedSavedir $defaultValheimData {
            Start-Process -FilePath $isolatedExecutable -ArgumentList $arguments -WorkingDirectory $clientStage `
                -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -WindowStyle Normal -PassThru
        }
        $processId = $process.Id
        $process.Refresh()
        Require ((Get-NormalizedPath $process.MainModule.FileName) -ceq (Get-NormalizedPath $isolatedExecutable)) 'Start-Process did not return the staged Valheim client process.'
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds); $ready = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) { throw "The isolated client exited before main-menu readiness (exit code $($process.ExitCode))." }
            if ($lastSteamCheckSeconds -lt 0 -or ($timer.Elapsed.TotalSeconds - $lastSteamCheckSeconds) -ge 1.0) {
                [void](Get-RunningSteamProcessEvidence $clientSteam.launcher_path)
                $lastSteamCheckSeconds = $timer.Elapsed.TotalSeconds
            }
            try { if ($process.PeakWorkingSet64 -gt $peakWorkingSetBytes) { $peakWorkingSetBytes = $process.PeakWorkingSet64 } } catch { }
            $snapshots = @(Get-LogSnapshots $logDefinitions)
            $bepText = [string](($snapshots | Where-Object label -ceq 'BepInEx' | Select-Object -First 1).text)
            if ($chainloaderSeconds -lt 0 -and $bepText.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) { $chainloaderSeconds = $timer.Elapsed.TotalSeconds }
            foreach ($snapshot in $snapshots) {
                $steamIndex = $snapshot.text.IndexOf('Steam initialized, persona:', [StringComparison]::OrdinalIgnoreCase)
                $versionIndex = $snapshot.text.IndexOf('Valheim version: ' + $baseline.valheim_runtime_version + ' (network version ' + $baseline.valheim_network_version + ')', [StringComparison]::OrdinalIgnoreCase)
                $renderIndex = $snapshot.text.IndexOf('Render threading mode:', [StringComparison]::OrdinalIgnoreCase)
                if ($steamIndex -ge 0 -and $versionIndex -gt $steamIndex -and $renderIndex -gt $versionIndex -and $menuSeconds -lt 0) {
                    $menuSeconds = $timer.Elapsed.TotalSeconds; $readyLog = $snapshot.label
                }
            }
            try {
                if ($process.MainWindowHandle -ne 0 -and $process.Responding -and $windowSeconds -lt 0) { $windowSeconds = $timer.Elapsed.TotalSeconds }
            }
            catch { }
            if ($chainloaderSeconds -ge 0 -and $menuSeconds -ge 0 -and $windowSeconds -ge 0) {
                Assert-HealthyLogs $snapshots 'main-menu readiness gate'; $ready = $true; break
            }
            Start-Sleep -Milliseconds 250
        }
        Require $ready "The client did not reach chainloader + FejdStartup menu + responsive-window readiness within $TimeoutSeconds seconds."
        $dwellStart = $timer.Elapsed.TotalSeconds
        while (($timer.Elapsed.TotalSeconds - $dwellStart) -lt $DwellSeconds) {
            $process.Refresh()
            if ($process.HasExited) { throw "The isolated client exited during dwell (exit code $($process.ExitCode))." }
            if (($timer.Elapsed.TotalSeconds - $lastSteamCheckSeconds) -ge 1.0) {
                [void](Get-RunningSteamProcessEvidence $clientSteam.launcher_path)
                $lastSteamCheckSeconds = $timer.Elapsed.TotalSeconds
            }
            try { if ($process.PeakWorkingSet64 -gt $peakWorkingSetBytes) { $peakWorkingSetBytes = $process.PeakWorkingSet64 } } catch { }
            Start-Sleep -Milliseconds 250
        }
        $dwellElapsedSeconds = $timer.Elapsed.TotalSeconds - $dwellStart
        Assert-HealthyLogs @(Get-LogSnapshots $logDefinitions) 'post-dwell gate'
        Require (-not $process.HasExited) 'The isolated client was not alive after dwell.'
    }
    finally {
        if ($null -ne $process) { $processWasStopped = Stop-TrackedProcess $process $isolatedExecutable }
    }
    Require $processWasStopped 'The harness did not stop its exact tracked client after the successful dwell.'
    $steamProcessesAfterStop = @(Get-RunningSteamProcessEvidence $clientSteam.launcher_path)
    $steamCloudLogEvidence = Save-AppendedSharedLogEvidence $steamCloudLogPath $steamCloudLogBefore.length (Join-Path $runRoot 'steam-cloud-log\appended-slice.txt')
    $steamCloudSliceText = Read-SharedTextFile $steamCloudLogEvidence.appended_slice_path
    $steamAppLines = [regex]::Matches($steamCloudSliceText, '(?im)^\[[^\r\n]*\]\s+\[AppID 892970\][^\r\n]*')
    Require ($steamAppLines.Count -gt 0) 'The captured Steam cloud-log slice contains no AppID 892970 audit records.'
    Require ($steamCloudSliceText.IndexOf('[AppID 892970] Starting sync (up,AC Exit,)', [StringComparison]::OrdinalIgnoreCase) -ge 0) 'Steam AutoCloud exit scan start was not captured.'
    Require ($steamCloudSliceText.IndexOf('[AppID 892970] AutoCloud complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) 'Steam AutoCloud completion was not captured.'
    $steamCloudForbiddenPattern = '(?im)^\[[^\r\n]*\]\s+\[AppID 892970\][^\r\n]*(?:Need to (?:upload|forget)|Upload batch initiated|(?:Upload|Forget)\s+OK|HTTP upload[^\r\n]*\bsuccess\b)'
    foreach ($match in [regex]::Matches($steamCloudSliceText, $steamCloudForbiddenPattern)) {
        $steamCloudForbiddenFindings += [pscustomobject][ordered]@{ excerpt = ([regex]::Replace($match.Value, '\s+', ' ').Trim()) }
    }
    if ($steamCloudForbiddenFindings.Count -gt 0) { throw "Steam cloud log reported an upload/forget action: $($steamCloudForbiddenFindings[0].excerpt)" }
    Remove-CreatedJunctions $createdJunctions $runRoot

    $finalSnapshots = @(Get-LogSnapshots $logDefinitions)
    foreach ($snapshot in $finalSnapshots) { Require $snapshot.exists "Required isolated log was not created: $($snapshot.path)" }
    Assert-HealthyLogs $finalSnapshots 'final stopped-process rescan'
    $bepText = [string](($finalSnapshots | Where-Object label -ceq 'BepInEx' | Select-Object -First 1).text)
    Require ($bepText.IndexOf('BepInEx ' + $baseline.bepinex_file_version + ' - valheim', [StringComparison]::OrdinalIgnoreCase) -ge 0) 'Exact BepInEx runtime marker was not observed.'
    Require ($bepText.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) 'Chainloader completion was not retained.'
    $guardReadyPattern = '(?m)\[RUNIC_SMOKE_ISOLATION_READY\] local_save_root=' + [regex]::Escape((Get-NormalizedPath $isolatedSavedir)) + ' steam_save_provider=null dont_save_anything=true'
    $guardReadyMatches = [regex]::Matches($bepText, $guardReadyPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    Require ($guardReadyMatches.Count -eq 1) 'The harness isolation guard did not prove its exact save root/provider/session contract once.'
    Require ([regex]::Matches($bepText, '(?m)\[RUNIC_SMOKE_ISOLATION_FAILED\]').Count -eq 0) 'The harness isolation guard reported failure.'
    $loadMatches = [regex]::Matches($bepText, '(?m)\bLoading \[(?<name>[^\]\r\n]+) (?<version>\d+\.\d+\.\d+(?:\.[^\]\r\n]+)?)\]')
    Require ($loadMatches.Count -eq ($pluginIdentities.Count + 1)) "BepInEx loaded $($loadMatches.Count) identities; expected $($pluginIdentities.Count) Runic identities plus one harness-only guard."
    Require ($loadMatches[0].Groups['name'].Value -ceq $baseline.isolation_guard_name -and $loadMatches[0].Groups['version'].Value -ceq $baseline.isolation_guard_version) 'The harness isolation guard was not the first BepInEx plugin load record.'
    $firstRunicLoadIndex = [int]::MaxValue
    foreach ($identity in $pluginIdentities) {
        $pattern = '(?m)\bLoading \[' + [regex]::Escape($identity.name) + ' ' + [regex]::Escape($identity.version) + '\]'
        $identityLoadMatches = [regex]::Matches($bepText, $pattern)
        Require ($identityLoadMatches.Count -eq 1) "Expected exactly one load record for $($identity.guid) $($identity.version)."
        if ($identityLoadMatches[0].Index -lt $firstRunicLoadIndex) { $firstRunicLoadIndex = $identityLoadMatches[0].Index }
    }
    $guardLoadPattern = '(?m)\bLoading \[' + [regex]::Escape($baseline.isolation_guard_name) + ' ' + [regex]::Escape($baseline.isolation_guard_version) + '\]'
    Require ([regex]::Matches($bepText, $guardLoadPattern).Count -eq 1) 'Expected exactly one load record for the harness-only isolation guard.'
    Require ($guardReadyMatches[0].Index -lt $firstRunicLoadIndex) 'The isolation guard did not reach READY before the first Runic plugin load/Awake.'
    $allLogText = [string]::Join("`n", @($finalSnapshots | ForEach-Object text))
    $runtimeMatches = [regex]::Matches($allLogText, '(?im)Valheim version:\s*(?<version>\d+\.\d+\.\d+)\s+\(network version\s+(?<network>\d+)\)')
    Require ($runtimeMatches.Count -gt 0) 'No exact Valheim runtime/network marker was observed.'
    $runtimeVersions = @($runtimeMatches | ForEach-Object { $_.Groups['version'].Value } | Select-Object -Unique)
    $networkVersions = @($runtimeMatches | ForEach-Object { $_.Groups['network'].Value } | Select-Object -Unique)
    Require ($runtimeVersions.Count -eq 1 -and $runtimeVersions[0] -ceq $baseline.valheim_runtime_version) 'Valheim runtime version drifted.'
    Require ($networkVersions.Count -eq 1 -and $networkVersions[0] -ceq $baseline.valheim_network_version) 'Valheim network version drifted.'
    Require ($allLogText.IndexOf($defaultValheimData, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'A runtime log referenced the live Valheim data root.'
    Require ($allLogText.IndexOf($defaultValheimData.Replace('\', '/'), [StringComparison]::OrdinalIgnoreCase) -lt 0) 'A runtime log referenced the slash-normalized live Valheim data root.'
    Require ($chainloaderSeconds -ge 0 -and $menuSeconds -ge 0 -and $windowSeconds -ge 0) 'Readiness timing evidence is incomplete.'
    Require ($dwellElapsedSeconds -ge $DwellSeconds) 'The monotonic dwell completed too early.'

    foreach ($cloudPath in $steamCloudRoots) {
        $userId = Split-Path (Split-Path $cloudPath -Parent) -Leaf
        $volatileSteamMetadataAfter += Save-FileSnapshotEvidence `
            (Join-Path $cloudPath 'remotecache.vdf') `
            (Join-Path $runRoot ('volatile-steam-metadata\after\' + $userId + '-remotecache.vdf')) `
            ('steam-remotecache-' + $userId)
    }
    foreach ($beforeCache in $volatileSteamMetadataBefore) {
        $afterCache = $volatileSteamMetadataAfter | Where-Object label -ceq $beforeCache.label | Select-Object -First 1
        Require ($null -ne $afterCache) "Missing post-run Steam metadata snapshot for $($beforeCache.label)."
        $bytesIdentical = $beforeCache.exists -eq $afterCache.exists -and ((-not $beforeCache.exists) -or $beforeCache.sha256 -ceq $afterCache.sha256)
        $metadataIdentical = $beforeCache.exists -eq $afterCache.exists -and ((-not $beforeCache.exists) -or (
                $beforeCache.bytes -eq $afterCache.bytes -and
                $beforeCache.last_write_utc -ceq $afterCache.last_write_utc -and
                $beforeCache.creation_utc -ceq $afterCache.creation_utc -and
                $beforeCache.attributes -ceq $afterCache.attributes))
        $volatileSteamMetadataDiffs += [pscustomobject][ordered]@{
            label = $beforeCache.label; classification = 'external Steam app-cache metadata; excluded from immutable save-payload gate'
            before = $beforeCache; after = $afterCache; bytes_identical = $bytesIdentical
            metadata_identical = $metadataIdentical; content_changed = -not $bytesIdentical; metadata_changed = -not $metadataIdentical
        }
    }
    $volatileSteamMetadataDiffPath = Join-Path $runRoot 'volatile-steam-metadata\DIFF.json'
    [IO.File]::WriteAllText($volatileSteamMetadataDiffPath, ($volatileSteamMetadataDiffs | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

    $protectedAfter = @(); $isolationChecks = @()
    foreach ($definition in $protectedDefinitions) { $protectedAfter += [pscustomobject][ordered]@{ label = $definition.label; fingerprint = Get-DirectoryFingerprint $definition.path $definition.hash } }
    foreach ($before in $protectedBefore) {
        $after = $protectedAfter | Where-Object label -ceq $before.label | Select-Object -First 1
        $unchanged = $null -ne $after -and $before.fingerprint.fingerprint_sha256 -ceq $after.fingerprint.fingerprint_sha256
        $isolationChecks += [pscustomobject][ordered]@{ label = $before.label; path = $before.fingerprint.path; before_sha256 = $before.fingerprint.fingerprint_sha256; after_sha256 = if ($null -ne $after) { $after.fingerprint.fingerprint_sha256 } else { '' }; unchanged = $unchanged }
        Require $unchanged "Protected live filesystem content changed: $($before.fingerprint.path)"
    }
    $stableRegistryAfter = Get-StableValheimRegistryFingerprint
    Require ($stableRegistryBefore.fingerprint_sha256 -ceq $stableRegistryAfter.fingerprint_sha256) 'Stable Valheim registry configuration changed during the smoke.'
    $pluginSourceAfter = Get-DirectoryFingerprint $pluginSourceRoot $true
    Require ($pluginSourceBefore.fingerprint_sha256 -ceq $pluginSourceAfter.fingerprint_sha256) 'Plugin source directory changed during the smoke.'
    $clientSteamAfter = Get-SteamInstallEvidence $clientRootPath $baseline.client_app_id $baseline.client_build_id
    Assert-Hash $clientExecutable $baseline.client_executable_sha256 'post-run client executable' | Out-Null
    Assert-Hash $clientAssembly $baseline.client_assembly_sha256 'post-run client assembly' | Out-Null
    Assert-Hash $clientUnityPlayer $baseline.client_unityplayer_sha256 'post-run client UnityPlayer' | Out-Null
    Assert-Hash $archivePath $baseline.bepinex_archive_sha256 'post-run BepInEx archive' | Out-Null
    Assert-Hash $guardProject $baseline.isolation_guard_project_sha256 'post-run isolation guard project' | Out-Null
    Assert-Hash $guardSource $baseline.isolation_guard_source_sha256 'post-run isolation guard source' | Out-Null
    Assert-Hash $isolatedGuardAssembly $baseline.isolation_guard_assembly_sha256 'post-run isolated guard copy' | Out-Null
    Require ((Get-FileSha256Hex $scriptPath) -ceq $scriptHashAtStart) 'The harness changed while it was running.'

    $logEvidence = @()
    foreach ($snapshot in $finalSnapshots) {
        $info = Get-Item -LiteralPath $snapshot.path
        $logEvidence += [pscustomobject][ordered]@{ label = $snapshot.label; path = $snapshot.path; bytes = [int64]$info.Length; sha256 = Get-FileSha256Hex $snapshot.path }
    }
    $guardBuildLogInfo = Get-Item -LiteralPath $guardBuildLog
    $logEvidence += [pscustomobject][ordered]@{ label = 'guard-build'; path = $guardBuildLog; bytes = [int64]$guardBuildLogInfo.Length; sha256 = Get-FileSha256Hex $guardBuildLog }
    $savedirCatalog = @(Get-FileCatalog $isolatedSavedir)
    $evidence = [ordered]@{
        schema = 'runic-valheim10-client-smoke/v2'; status = 'GO'; run_id = $runId; started_utc = $processStartedUtc; completed_utc = [DateTime]::UtcNow.ToString('O')
        baseline = $baseline; harness = [ordered]@{ path = $scriptPath; sha256 = $scriptHashAtStart }
        steam = [ordered]@{
            before = $clientSteam; after = $clientSteamAfter
            cloud_log = [ordered]@{
                before_boundary = $steamCloudLogBefore; appended_slice = $steamCloudLogEvidence
                app_id_892970_record_count = $steamAppLines.Count; forbidden_upload_or_forget_count = $steamCloudForbiddenFindings.Count
                required_exit_scan_start_and_completion = $true
            }
        }
        loader = [ordered]@{
            bepinex_archive = $archivePath; bepinex_archive_sha256 = $baseline.bepinex_archive_sha256
            isolated_client_root = $clientStage; bepinex_file_version = $observedBepInExVersion; core_catalog_sha256 = $coreCatalogSha256; core_files = $coreCatalog
            bootstrap_proxy = 'winhttp.dll (exact BepInExPack Valheim 5.4.2350 bytes)'; proxy_sha256 = $baseline.packaged_proxy_sha256
            version_alias_absent = $true; target_assembly = $targetAssembly; override_mode = 'command-line and restored process environment'
        }
        plugins = [ordered]@{
            source_directory = $pluginSourceRoot; dll_count = $pluginFiles.Count; identity_count = $pluginIdentities.Count
            files = $pluginFiles; identities = $pluginIdentities; runic_runtime_load_count = $pluginIdentities.Count
            total_runtime_load_count_including_guard = $loadMatches.Count
        }
        isolation_guard = [ordered]@{
            harness_only = $true; excluded_from_runic_payload_and_identity_counts = $true
            source_path = $guardSource; project_path = $guardProject; built_assembly = $guardAssembly; isolated_assembly = $isolatedGuardAssembly
            identity = $guardIdentities[0]; source_sha256 = $baseline.isolation_guard_source_sha256
            project_sha256 = $baseline.isolation_guard_project_sha256; assembly_sha256 = $baseline.isolation_guard_assembly_sha256
            build_exit_code = $guardBuildExitCode; build_log = $guardBuildLog; focused_il_contract_proof = $guardContractProof
            runtime_ready_marker_count = 1; runtime_failed_marker_count = 0
            first_bepinex_plugin_load = $true; ready_before_first_runic_plugin_load_and_awake = $true
            supported_process_scope = 'Valheim client and dedicated server when BepInEx plugin Awake occurs before PlatformManager initialization; this run exercises graphical client.'
        }
        runtime = [ordered]@{
            process_id = $processId; process_image = $isolatedExecutable; stop_scope = 'exact Process object returned by Start-Process'; stopped_by_harness = $processWasStopped
            graphical = $true; batchmode = $false; nographics = $false; window_width = $WindowWidth; window_height = $WindowHeight
            readiness_sequence = @('Chainloader startup complete', 'Steam initialized, persona:', 'Valheim version 1.0.7 / network 39', 'Render threading mode:', 'responsive main window')
            readiness_log = $readyLog; chainloader_seconds = $chainloaderSeconds; menu_seconds = $menuSeconds; window_seconds = $windowSeconds
            dwell_requested_seconds = $DwellSeconds; dwell_elapsed_seconds = $dwellElapsedSeconds; peak_working_set_bytes = $peakWorkingSetBytes
            observed_runtime_versions = $runtimeVersions; observed_network_versions = $networkVersions
            steam_required_open_for_entire_run = $true; steam_processes_at_launch = $steamProcessesAtLaunch; steam_processes_after_stop = $steamProcessesAfterStop
        }
        isolation = [ordered]@{
            isolated_savedir = $isolatedSavedir; save_root_established_by = 'harness-only guard before PlatformManager initialization'; savedir_catalog_sha256 = Get-CatalogSha256 $savedirCatalog; savedir_files = $savedirCatalog
            isolated_bepinex_root = $bepRoot; process_working_directory = $clientStage; live_filesystem_paths_byte_identical = $true
            protected_path_checks = $isolationChecks; stable_registry_config_unchanged = $true; registry_before = $stableRegistryBefore; registry_after = $stableRegistryAfter
            steam_remote_save_payloads_immutable = $true
            volatile_external_steam_metadata = [ordered]@{
                policy = 'remotecache.vdf is external Steam app-cache metadata, not save payload; preserve both snapshots and report byte/metadata deltas without restoring it'
                before = $volatileSteamMetadataBefore; after = $volatileSteamMetadataAfter; diffs = $volatileSteamMetadataDiffs
                diff_evidence_path = $volatileSteamMetadataDiffPath
            }
            volatile_registry_session_values_excluded = $stableRegistryBefore.excluded_volatile_values
            junction_contract = 'runtime targets are read only; every target byte is covered by the live-client-install pre/post fingerprint'
            runtime_junctions_removed_after_stop = $true; runtime_junctions = $createdJunctions
        }
        ui_behavior = 'A normal 960x540-style Valheim window and the pinned pack BepInEx console may appear and briefly take focus; no input is sent.'
        log_scan = [ordered]@{ rules = @($scanRules | ForEach-Object id); failure_count = 0; readiness_passed = $true; post_dwell_passed = $true; final_rescan_passed = $true }
        logs = $logEvidence
    }
    $pending = Join-Path $runRoot '.RESULT.json.pending'; $result = Join-Path $runRoot 'RESULT.json'
    [IO.File]::WriteAllText($pending, ($evidence | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
    $resultHash = Get-FileSha256Hex $pending
    Move-Item -LiteralPath $pending -Destination $result
    [IO.File]::WriteAllText((Join-Path $runRoot 'RESULT.sha256'), ($resultHash + '  RESULT.json' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Write-Output ('RUNIC_VALHEIM10_CLIENT_SMOKE_GO plugins=' + $pluginIdentities.Count + ' build=' + $clientSteam.build_id + ' evidence="' + $result + '" sha256=' + $resultHash)
}
catch {
    $message = $_.Exception.Message
    try { if ($null -ne $process -and -not $processWasStopped) { $processWasStopped = Stop-TrackedProcess $process (Join-Path $runRoot 'client-stage\valheim.exe') } } catch { $message += ' | Stop cleanup: ' + $_.Exception.Message }
    try { if ($createdJunctions.Count -gt 0) { Remove-CreatedJunctions $createdJunctions $runRoot } } catch { $message += ' | Junction cleanup: ' + $_.Exception.Message }
    $failureIsolation = @()
    try {
        foreach ($before in $protectedBefore) {
            $after = Get-DirectoryFingerprint $before.fingerprint.path $before.fingerprint.content_hashes_included
            $failureIsolation += [pscustomobject][ordered]@{ label = $before.label; path = $before.fingerprint.path; before_sha256 = $before.fingerprint.fingerprint_sha256; after_sha256 = $after.fingerprint_sha256; unchanged = $before.fingerprint.fingerprint_sha256 -ceq $after.fingerprint_sha256 }
        }
    }
    catch { $message += ' | Isolation audit: ' + $_.Exception.Message }
    try {
        $failure = [ordered]@{
            schema = 'runic-valheim10-client-smoke-failure/v1'; status = 'NO-GO'; run_id = $runId; failed_utc = [DateTime]::UtcNow.ToString('O')
            message = $message; process_id = $processId; stopped_only_tracked_process = $processWasStopped; evidence_root = $runRoot
            harness_path = $scriptPath; harness_sha256 = $scriptHashAtStart; protected_path_checks = $failureIsolation
            volatile_external_steam_metadata = [ordered]@{
                before = $volatileSteamMetadataBefore; after = $volatileSteamMetadataAfter; diffs = $volatileSteamMetadataDiffs; diff_evidence_path = $volatileSteamMetadataDiffPath
            }
            steam_cloud_log = [ordered]@{ before_boundary = $steamCloudLogBefore; appended_slice = $steamCloudLogEvidence; forbidden_upload_or_forget_findings = $steamCloudForbiddenFindings }
            stable_registry_before = $stableRegistryBefore
            stable_registry_after = try { Get-StableValheimRegistryFingerprint } catch { $null }
        }
        [IO.File]::WriteAllText((Join-Path $runRoot 'FAILURE.json'), ($failure | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    }
    catch { }
    throw
}
