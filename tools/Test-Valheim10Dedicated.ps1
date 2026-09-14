[CmdletBinding()]
param(
    [string]$ServerRoot = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server',
    [string]$ClientRoot = 'E:\SteamLibrary\steamapps\common\Valheim',
    [string]$BepInExArchive = 'E:\Valheim Mods\ChazmanModsRepo\artifacts\Valheim1.0\Migration-20260909\Downloads\denikson-BepInExPack_Valheim-5.4.2350.zip',
    [string]$PluginDirectory = '',
    [ValidateRange(0, 100)][int]$ExpectedPluginCount = 0,
    [string]$ExpectedPluginCatalogSha256 = '',
    [string]$EvidenceBase = '',
    [ValidateRange(1024, 65532)][int]$Port = 28630,
    [ValidateRange(60, 600)][int]$TimeoutSeconds = 300,
    [ValidateRange(10, 120)][int]$DwellSeconds = 20
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# This harness intentionally pins one audited release environment. A later Valheim or
# BepInEx update must fail closed until these values are deliberately re-audited.
$baseline = [ordered]@{
    valheim_release = '1.0'
    valheim_runtime_version = '1.0.7'
    valheim_network_version = '39'
    client_app_id = '892970'
    client_build_id = '25185596'
    client_executable_sha256 = '3ECC2EEA5CE2ACFCAA6868AB7D8252B9A07910EA440405E62817890F4EF6BD50'
    client_assembly_sha256 = 'A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC'
    client_unityplayer_sha256 = '4D161E15D8CCDB32EB73262E7A3E0A66F8C175B50A38E22AE5B0E8FB9AEA98F3'
    server_install_app_id = '896660'
    server_runtime_app_id = '892970'
    server_build_id = '25185644'
    server_executable_sha256 = 'E01757027E08D35C5FC926ADFEEC164344B73EADB4D4C61196945642B787E4FD'
    server_assembly_sha256 = '9DF99B0011B4CA0A448E6D935C77368B4E3B98EEE7B0AC8D1B43B34E267471B2'
    server_assembly_utils_sha256 = 'F85F21C2F115EF8DB4118083005E6D36C818854D090A1F867FD7A7484EA1D7E2'
    server_splatform_sha256 = '8953C24A2F2AF7AE257CC64FB36DDE9D102C871C29B4CDD97D0C3C001DE5BD5A'
    server_unityplayer_sha256 = '6690B00B008D089F93DB3F66FAF8F7CA0DEF780625CC12D3E2E10C88A96D78C3'
    server_steam_appid_file_sha256 = '63BB3FB68EA680E69E80A23DE08A09B29969BF80D0DE63DBD769EF046751DEB9'
    bepinex_pack_version = '5.4.2350'
    bepinex_file_version = '5.4.23.5'
    bepinex_archive_sha256 = '37A91C000B4E88F2ED7A4BD7D812239852D2E36CBF0FF0A9F5FAACFBA46B105F'
    doorstop_proxy_sha256 = '93406D0A02E7C164B89828CBFE3B289930A112D2ECA50BD4A52E72ECE169E6A8'
    doorstop_config_sha256 = '4D5C6DFA0F771C6A5B1B0C559ACA0BD0ECE7D08B08FFF894708DC3B73CE73CFC'
    doorstop_version_sha256 = '84FA42545C0993EDF8A5559D2A0772260F85F4A7ED29EC75E6590BE64ED98067'
    bepinex_sha256 = 'F09821B2A7B990C6F50C5EF23229635303CE675374B70EDC6F5A9B960CB818E3'
    preloader_sha256 = 'FB21BF85C462BC5376435ECCFED23F07A36BAA94F770FF35CD567E17D2997C8D'
    harmony_sha256 = '1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031'
    bepinex_harmony_sha256 = '2F0270073E307095CE980F578BFC9489F7AC98D360B9D41F77370A84C33EDD7A'
    mono_cecil_sha256 = '7AE470288FFF4A402899C254D0A76CEFEF55877F5C54F96E83C797CC5BB6E2F6'
    isolation_guard_guid = '000.runic.smoke.dedicated-isolation-guard'
    isolation_guard_name = '000 Runic Dedicated Smoke Isolation Guard'
    isolation_guard_version = '1.0.0'
    isolation_guard_source_sha256 = '39C54B5C1396F8C001F59DA23ECCABCDF57D513F902C9E5EBB507A5C8F8D15DC'
    isolation_guard_project_sha256 = '708BE4AD43C22157232BD5C9C73D1F4890373DF28B21A6126E39847E05A936CA'
    isolation_guard_dll_sha256 = '9287296E5C225A2C19E8B97A662219DF5983188C466E060D339E5DD553FCC2F9'
}

# Unity rewrites exactly these four session bookkeeping values during a normal
# headless process lifetime. They are not game configuration or save data. The
# runtime registry gate below permits data changes only for these exact root-key
# Binary values; names matching a broader pattern are deliberately not allowed.
$allowedVolatilePlayerPrefsValues = @(
    [pscustomobject][ordered]@{ key = ''; name = 'unity_connect.mega_session_id_h1802243016'; kind = 'Binary' }
    [pscustomobject][ordered]@{ key = ''; name = 'unity_connect.session_id_h4145606137'; kind = 'Binary' }
    [pscustomobject][ordered]@{ key = ''; name = 'unity.player_session_count_h922449978'; kind = 'Binary' }
    [pscustomobject][ordered]@{ key = ''; name = 'unity.player_sessionid_h1351336811'; kind = 'Binary' }
)
$expectedStablePlayerPrefsValueCount = 147

$scanRules = @(
    [ordered]@{ id = 'bepinex-error'; pattern = '(?im)^\s*\[(?:Fatal|Error)\s*:(?![ \t]*Unity Log\])[^\r\n]*' }
    [ordered]@{ id = 'unity-error'; pattern = '(?im)^\s*\[Error\s*:\s*Unity Log\][^\r\n]*' }
    [ordered]@{ id = 'unhandled-runtime'; pattern = '(?im)^\s*(?:Fatal error|Unhandled exception)\b[^\r\n]*' }
    [ordered]@{ id = 'managed-exception'; pattern = '(?im)^\s*(?:(?:System|Mono|UnityEngine|HarmonyLib)\.)?[A-Za-z_][A-Za-z0-9_.]*(?:Exception|Error):[^\r\n]*' }
    [ordered]@{ id = 'any-exception-token'; pattern = '(?im)^.*\b(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*Exception\b[^\r\n]*' }
    [ordered]@{ id = 'binary-or-type-exception'; pattern = '(?im)\b(?:BadImageFormatException|FileLoadException|FileNotFoundException|MethodAccessException|MissingFieldException|MissingMethodException|ReflectionTypeLoadException|TargetInvocationException|TypeInitializationException|TypeLoadException)\b[^\r\n]*' }
    [ordered]@{ id = 'harmony-patch-failure'; pattern = '(?im)\b(?:Exception while patching|HarmonyException|No target method specified|Undefined target method)\b[^\r\n]*' }
    [ordered]@{ id = 'harmony-diagnostic-failure'; pattern = '(?im)^.*\bHarmony(?:X|Lib)?\b[^\r\n]*(?:error|exception|fail(?:ed|ure)|could not|missing (?:field|method|type))[^\r\n]*' }
    [ordered]@{ id = 'assembly-load-failure'; pattern = '(?im)\b(?:Could not load file or assembly|Could not resolve assembly|Error loading plugin|Failed to load (?:assembly|plugin))\b[^\r\n]*' }
    [ordered]@{ id = 'loader-warning-failure'; pattern = '(?im)^\s*\[Warning\s*:\s*(?:BepInEx|HarmonyX?)[^\r\n]*(?:could not find|exception|failed)[^\r\n]*' }
    [ordered]@{ id = 'doorstop-bootstrap-failure'; pattern = '(?im)^\s*(?:Doorstop disabled!?|Error invoking code!|Could not find target assembly!|Failed to [^\r\n]*hook)[^\r\n]*' }
    [ordered]@{ id = 'steam-runtime-failure'; pattern = '(?im)^.*(?:Invalid APPID|Steam is not initialized|Awake of network backend failed)[^\r\n]*' }
    [ordered]@{ id = 'isolation-guard-failure'; pattern = '(?im)^.*\[RUNIC_SMOKE_ISOLATION_FAILED\][^\r\n]*' }
)

# These are the complete, exact classes of [Error : Unity Log] noise observed in
# the clean loader-only Unity 6000 headless baseline. Every other Unity error is a
# failure; exception/type/Harmony rules below still apply even to an allowed line.
$baselineNoiseAllowlist = @(
    '^\[Error\s*:\s*Unity Log\]\s*AsyncResourceUpload failed\.$'
    '^\[Error\s*:\s*Unity Log\]\s*This custom render path shader needs to have at least 1 passes\.$'
    '^\[Error\s*:\s*Unity Log\]\s*Could not find material Hidden/(?:VideoDecode|VideoComposite)\. Make sure the Video shaders are included in your build, in the Built-in Shader Settings section of the Graphics Settings\.$'
    '^\[Error\s*:\s*Unity Log\]\s*Could not find video decode shader pass (?:Default|Flip_NV12_To_RGB1|Flip_NV12_To_RGBA|Flip_RGBA_To_RGBA|Flip_RGBASplit_To_RGBA|YCbCr_To_RGB1|YCbCrA_To_RGBA|YCbCrA_To_RGBAFull) in shader <not found>$'
    '^\[Error\s*:\s*Unity Log\]\s*\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2}: Failed to play intro cinematic$'
)

function Require {
    param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
    if (-not $Condition) { throw $Message }
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
    finally { $algorithm.Dispose() }
}

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
            $fullPath.TrimEnd('\', '/'),
            $pathRoot.TrimEnd('\', '/'),
            [StringComparison]::OrdinalIgnoreCase)) {
        return $pathRoot
    }
    return $fullPath.TrimEnd('\', '/')
}

function Test-IsSameOrChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Parent
    )
    $candidatePath = (Get-NormalizedPath -Path $Candidate).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $parentPath = (Get-NormalizedPath -Path $Parent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $candidatePath.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePointInExistingPathChain {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $cursor = Get-NormalizedPath -Path $Path
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            Require (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "$Label path chain contains a reparse point: $cursor"
        }
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { break }
        $parentPath = Get-NormalizedPath -Path $parent.FullName
        if ([string]::Equals($parentPath, $cursor, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = $parentPath
    }
}

function Get-RelativeChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Child
    )
    $rootPath = (Get-NormalizedPath -Path $Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $childPath = [System.IO.Path]::GetFullPath($Child)
    Require ($childPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) "Path is outside catalog root: $childPath"
    return $childPath.Substring($rootPath.Length).Replace('\', '/')
}

function Get-DirectoryFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [bool]$HashFileContents = $true
    )
    $fullPath = Get-NormalizedPath -Path $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [pscustomobject][ordered]@{
            path = $fullPath
            exists = $false
            directory_count = 0
            file_count = 0
            total_bytes = [int64]0
            fingerprint_sha256 = Get-TextSha256Hex -Text 'missing'
            content_hashes_included = $HashFileContents
        }
    }

    $item = Get-Item -LiteralPath $fullPath -Force
    if (-not $item.PSIsContainer) {
        $hash = if ($HashFileContents) { Get-FileSha256Hex -Path $fullPath } else { '(metadata-only)' }
        $line = 'F|' + $item.Name + '|' + [string]$item.Length + '|' + [string]$item.LastWriteTimeUtc.Ticks + '|' + [string]$item.Attributes + '|' + $hash
        return [pscustomobject][ordered]@{
            path = $fullPath
            exists = $true
            directory_count = 0
            file_count = 1
            total_bytes = [int64]$item.Length
            fingerprint_sha256 = Get-TextSha256Hex -Text $line
            content_hashes_included = $HashFileContents
        }
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $directories = @(Get-ChildItem -LiteralPath $fullPath -Directory -Recurse -Force | Sort-Object FullName)
    $files = @(Get-ChildItem -LiteralPath $fullPath -File -Recurse -Force | Sort-Object FullName)
    foreach ($directory in $directories) {
        $relative = Get-RelativeChildPath -Root $fullPath -Child $directory.FullName
        $lines.Add('D|' + $relative + '|' + [string]$directory.LastWriteTimeUtc.Ticks + '|' + [string]$directory.Attributes)
    }
    $totalBytes = [int64]0
    foreach ($file in $files) {
        $relative = Get-RelativeChildPath -Root $fullPath -Child $file.FullName
        $totalBytes += [int64]$file.Length
        $hash = if ($HashFileContents) { Get-FileSha256Hex -Path $file.FullName } else { '(metadata-only)' }
        $lines.Add('F|' + $relative + '|' + [string]$file.Length + '|' + [string]$file.LastWriteTimeUtc.Ticks + '|' + [string]$file.Attributes + '|' + $hash)
    }
    return [pscustomobject][ordered]@{
        path = $fullPath
        exists = $true
        directory_count = $directories.Count
        file_count = $files.Count
        total_bytes = $totalBytes
        fingerprint_sha256 = Get-TextSha256Hex -Text ([string]::Join("`n", $lines))
        content_hashes_included = $HashFileContents
    }
}

function Get-RegistryTreeFingerprint {
    param([Parameter(Mandatory = $true)][string]$CurrentUserSubKey)
    $rootKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($CurrentUserSubKey, $false)
    if ($null -eq $rootKey) {
        return [pscustomobject][ordered]@{
            path = 'HKCU:\' + $CurrentUserSubKey
            exists = $false
            key_count = 0
            value_count = 0
            fingerprint_sha256 = Get-TextSha256Hex -Text 'missing'
            key_catalog = @()
            value_catalog = @()
        }
    }
    $rootKey.Dispose()

    $lines = [System.Collections.Generic.List[string]]::new()
    $state = [pscustomobject]@{
        key_count = 0
        keys = [System.Collections.Generic.List[string]]::new()
        values = [System.Collections.Generic.List[object]]::new()
    }
    $visit = $null
    $visit = {
        param([string]$RelativePath)
        $registryPath = if ([string]::IsNullOrEmpty($RelativePath)) {
            $CurrentUserSubKey
        }
        else { $CurrentUserSubKey + '\' + $RelativePath }
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
        Require ($null -ne $key) "Protected registry key disappeared while fingerprinting: HKCU:\$registryPath"
        try {
            $state.key_count++
            $state.keys.Add($RelativePath)
            $lines.Add('K|' + $RelativePath)
            foreach ($valueName in @($key.GetValueNames() | Sort-Object)) {
                $kind = $key.GetValueKind($valueName)
                $value = $key.GetValue(
                    $valueName,
                    $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $stableValue = switch ($kind) {
                    ([Microsoft.Win32.RegistryValueKind]::Binary) {
                        'binary|' + [Convert]::ToBase64String([byte[]]$value)
                        break
                    }
                    ([Microsoft.Win32.RegistryValueKind]::MultiString) {
                        'multi|' + [string]::Join("`0", [string[]]$value)
                        break
                    }
                    default {
                        ([string]$kind) + '|' + [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture)
                        break
                    }
                }
                $valueHash = Get-TextSha256Hex -Text $stableValue
                $lines.Add('V|' + $RelativePath + '|' + $valueName + '|' + [string]$kind + '|' + $valueHash)
                $state.values.Add([pscustomobject][ordered]@{
                    key = $RelativePath
                    name = $valueName
                    kind = [string]$kind
                    data_sha256 = $valueHash
                })
            }
            foreach ($subKeyName in @($key.GetSubKeyNames() | Sort-Object)) {
                $child = if ([string]::IsNullOrEmpty($RelativePath)) { $subKeyName } else { $RelativePath + '\' + $subKeyName }
                & $visit $child
            }
        }
        finally { $key.Dispose() }
    }

    & $visit ''
    $keys = @($state.keys)
    $values = @($state.values)
    return [pscustomobject][ordered]@{
        path = 'HKCU:\' + $CurrentUserSubKey
        exists = $true
        key_count = $state.key_count
        value_count = $values.Count
        fingerprint_sha256 = Get-TextSha256Hex -Text ([string]::Join("`n", $lines))
        key_catalog = $keys
        value_catalog = $values
    }
}

function Get-RegistryValueIdentity {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Key,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Name
    )
    return $Key + [char]0 + $Name
}

function Get-StablePlayerPrefsRegistryCatalog {
    param(
        [Parameter(Mandatory = $true)]$Snapshot,
        [Parameter(Mandatory = $true)][object[]]$AllowedVolatileValues
    )
    $allowedIdentities = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $AllowedVolatileValues) {
        $identity = Get-RegistryValueIdentity -Key ([string]$definition.key) -Name ([string]$definition.name)
        [void]$allowedIdentities.Add($identity)
    }

    $catalog = @()
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($keyPath in @($Snapshot.key_catalog)) {
        $lines.Add('K|' + [string]$keyPath)
    }
    foreach ($entry in @($Snapshot.value_catalog)) {
        $identity = Get-RegistryValueIdentity -Key ([string]$entry.key) -Name ([string]$entry.name)
        if ($allowedIdentities.Contains($identity)) { continue }
        $catalog += $entry
        $lines.Add('V|' + [string]$entry.key + '|' + [string]$entry.name + '|' + [string]$entry.kind + '|' + [string]$entry.data_sha256)
    }
    return [pscustomobject][ordered]@{
        key_count = [int]$Snapshot.key_count
        value_count = $catalog.Count
        fingerprint_sha256 = Get-TextSha256Hex -Text ([string]::Join("`n", $lines))
        value_catalog = $catalog
    }
}

function Compare-PlayerPrefsRegistrySnapshots {
    param(
        [Parameter(Mandatory = $true)]$Before,
        [Parameter(Mandatory = $true)]$After,
        [Parameter(Mandatory = $true)][object[]]$AllowedVolatileValues,
        [Parameter(Mandatory = $true)][int]$ExpectedStableValueCount
    )
    $issues = [System.Collections.Generic.List[string]]::new()
    $beforeByIdentity = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $afterByIdentity = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $allowedByIdentity = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)

    foreach ($definition in $AllowedVolatileValues) {
        $identity = Get-RegistryValueIdentity -Key ([string]$definition.key) -Name ([string]$definition.name)
        if ($allowedByIdentity.ContainsKey($identity)) {
            $issues.Add('Duplicate volatile allowlist identity: ' + [string]$definition.name)
        }
        else { $allowedByIdentity.Add($identity, $definition) }
    }
    foreach ($entry in @($Before.value_catalog)) {
        $identity = Get-RegistryValueIdentity -Key ([string]$entry.key) -Name ([string]$entry.name)
        if ($beforeByIdentity.ContainsKey($identity)) { $issues.Add('Duplicate before value identity: ' + [string]$entry.name) }
        else { $beforeByIdentity.Add($identity, $entry) }
    }
    foreach ($entry in @($After.value_catalog)) {
        $identity = Get-RegistryValueIdentity -Key ([string]$entry.key) -Name ([string]$entry.name)
        if ($afterByIdentity.ContainsKey($identity)) { $issues.Add('Duplicate after value identity: ' + [string]$entry.name) }
        else { $afterByIdentity.Add($identity, $entry) }
    }

    if (-not [bool]$Before.exists) { $issues.Add('The before PlayerPrefs registry key did not exist.') }
    if (-not [bool]$After.exists) { $issues.Add('The after PlayerPrefs registry key did not exist.') }
    if ([int]$Before.key_count -ne [int]$After.key_count) {
        $issues.Add('Registry key count changed from ' + [string]$Before.key_count + ' to ' + [string]$After.key_count + '.')
    }
    if ([int]$Before.value_count -ne [int]$After.value_count) {
        $issues.Add('Registry value count changed from ' + [string]$Before.value_count + ' to ' + [string]$After.value_count + '.')
    }

    $beforeKeyLines = @($Before.key_catalog | ForEach-Object { 'K|' + [string]$_ })
    $afterKeyLines = @($After.key_catalog | ForEach-Object { 'K|' + [string]$_ })
    $beforeKeyCatalogSha256 = Get-TextSha256Hex -Text ([string]::Join("`n", $beforeKeyLines))
    $afterKeyCatalogSha256 = Get-TextSha256Hex -Text ([string]::Join("`n", $afterKeyLines))
    if ($beforeKeyCatalogSha256 -cne $afterKeyCatalogSha256) {
        $issues.Add('Registry key identity catalog changed.')
    }

    $removedValueIdentities = @()
    $addedValueIdentities = @()
    $kindChanges = @()
    $unallowlistedDataChanges = @()
    foreach ($identity in $beforeByIdentity.Keys) {
        if (-not $afterByIdentity.ContainsKey($identity)) {
            $entry = $beforeByIdentity[$identity]
            $removedValueIdentities += [pscustomobject][ordered]@{ key = [string]$entry.key; name = [string]$entry.name }
            continue
        }
        $beforeEntry = $beforeByIdentity[$identity]
        $afterEntry = $afterByIdentity[$identity]
        if ([string]$beforeEntry.kind -cne [string]$afterEntry.kind) {
            $kindChanges += [pscustomobject][ordered]@{
                key = [string]$beforeEntry.key
                name = [string]$beforeEntry.name
                before_kind = [string]$beforeEntry.kind
                after_kind = [string]$afterEntry.kind
            }
        }
        if ([string]$beforeEntry.data_sha256 -cne [string]$afterEntry.data_sha256 -and -not $allowedByIdentity.ContainsKey($identity)) {
            $unallowlistedDataChanges += [pscustomobject][ordered]@{
                key = [string]$beforeEntry.key
                name = [string]$beforeEntry.name
                before_data_sha256 = [string]$beforeEntry.data_sha256
                after_data_sha256 = [string]$afterEntry.data_sha256
            }
        }
    }
    foreach ($identity in $afterByIdentity.Keys) {
        if (-not $beforeByIdentity.ContainsKey($identity)) {
            $entry = $afterByIdentity[$identity]
            $addedValueIdentities += [pscustomobject][ordered]@{ key = [string]$entry.key; name = [string]$entry.name }
        }
    }
    if ($removedValueIdentities.Count -ne 0) { $issues.Add('One or more registry values were removed.') }
    if ($addedValueIdentities.Count -ne 0) { $issues.Add('One or more registry values were added.') }
    if ($kindChanges.Count -ne 0) { $issues.Add('One or more registry value kinds changed.') }
    if ($unallowlistedDataChanges.Count -ne 0) { $issues.Add('One or more nonallowlisted registry values changed data.') }

    $volatileDeltas = @()
    foreach ($definition in $AllowedVolatileValues) {
        $identity = Get-RegistryValueIdentity -Key ([string]$definition.key) -Name ([string]$definition.name)
        $beforeEntry = if ($beforeByIdentity.ContainsKey($identity)) { $beforeByIdentity[$identity] } else { $null }
        $afterEntry = if ($afterByIdentity.ContainsKey($identity)) { $afterByIdentity[$identity] } else { $null }
        if ($null -eq $beforeEntry -or $null -eq $afterEntry) {
            $issues.Add('Required volatile registry value is missing: ' + [string]$definition.name)
        }
        else {
            if ([string]$beforeEntry.kind -cne [string]$definition.kind -or [string]$afterEntry.kind -cne [string]$definition.kind) {
                $issues.Add('Required volatile registry value is not Binary in both snapshots: ' + [string]$definition.name)
            }
        }
        $volatileDeltas += [pscustomobject][ordered]@{
            key = [string]$definition.key
            name = [string]$definition.name
            required_kind = [string]$definition.kind
            before_kind = if ($null -ne $beforeEntry) { [string]$beforeEntry.kind } else { '' }
            after_kind = if ($null -ne $afterEntry) { [string]$afterEntry.kind } else { '' }
            before_data_sha256 = if ($null -ne $beforeEntry) { [string]$beforeEntry.data_sha256 } else { '' }
            after_data_sha256 = if ($null -ne $afterEntry) { [string]$afterEntry.data_sha256 } else { '' }
            changed = ($null -ne $beforeEntry -and $null -ne $afterEntry -and [string]$beforeEntry.data_sha256 -cne [string]$afterEntry.data_sha256)
        }
    }

    $stableBefore = Get-StablePlayerPrefsRegistryCatalog -Snapshot $Before -AllowedVolatileValues $AllowedVolatileValues
    $stableAfter = Get-StablePlayerPrefsRegistryCatalog -Snapshot $After -AllowedVolatileValues $AllowedVolatileValues
    if ($stableBefore.value_count -ne $ExpectedStableValueCount) {
        $issues.Add('Before stable registry catalog has ' + [string]$stableBefore.value_count + ' values; expected exactly ' + [string]$ExpectedStableValueCount + '.')
    }
    if ($stableAfter.value_count -ne $ExpectedStableValueCount) {
        $issues.Add('After stable registry catalog has ' + [string]$stableAfter.value_count + ' values; expected exactly ' + [string]$ExpectedStableValueCount + '.')
    }
    if ([string]$stableBefore.fingerprint_sha256 -cne [string]$stableAfter.fingerprint_sha256) {
        $issues.Add('Stable registry catalog fingerprint changed.')
    }

    return [pscustomobject][ordered]@{
        passed = ($issues.Count -eq 0)
        policy = 'Only the four exact root-key Unity session values listed here may change data, and all four must remain Binary.'
        full_before_fingerprint_sha256 = [string]$Before.fingerprint_sha256
        full_after_fingerprint_sha256 = [string]$After.fingerprint_sha256
        full_fingerprint_unchanged = ([string]$Before.fingerprint_sha256 -ceq [string]$After.fingerprint_sha256)
        before_key_count = [int]$Before.key_count
        after_key_count = [int]$After.key_count
        before_value_count = [int]$Before.value_count
        after_value_count = [int]$After.value_count
        before_key_catalog_sha256 = $beforeKeyCatalogSha256
        after_key_catalog_sha256 = $afterKeyCatalogSha256
        removed_value_identities = $removedValueIdentities
        added_value_identities = $addedValueIdentities
        kind_changes = $kindChanges
        unallowlisted_data_changes = $unallowlistedDataChanges
        expected_stable_value_count = $ExpectedStableValueCount
        stable_before = $stableBefore
        stable_after = $stableAfter
        volatile_value_deltas = $volatileDeltas
        issues = @($issues)
    }
}

function Get-ProtectedPathSnapshots {
    param([Parameter(Mandatory = $true)][object[]]$Definitions)
    $snapshots = @()
    foreach ($definition in $Definitions) {
        $snapshots += [pscustomobject][ordered]@{
            label = [string]$definition.label
            fingerprint = Get-DirectoryFingerprint -Path ([string]$definition.path) -HashFileContents ([bool]$definition.hash)
        }
    }
    return $snapshots
}

function Compare-ProtectedPathSnapshots {
    param(
        [Parameter(Mandatory = $true)][object[]]$Before,
        [Parameter(Mandatory = $true)][object[]]$After
    )
    $checks = @()
    foreach ($beforeEntry in $Before) {
        $afterEntry = $After | Where-Object { $_.label -ceq $beforeEntry.label } | Select-Object -First 1
        $unchanged = $null -ne $afterEntry -and
            $beforeEntry.fingerprint.fingerprint_sha256 -ceq $afterEntry.fingerprint.fingerprint_sha256
        $checks += [pscustomobject][ordered]@{
            label = $beforeEntry.label
            path = $beforeEntry.fingerprint.path
            before_sha256 = $beforeEntry.fingerprint.fingerprint_sha256
            after_sha256 = if ($null -ne $afterEntry) { $afterEntry.fingerprint.fingerprint_sha256 } else { '' }
            unchanged = $unchanged
        }
    }
    return $checks
}

function Write-ImmutableJsonEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value,
        [ValidateRange(2, 30)][int]$Depth = 12
    )
    $jsonPath = Join-Path $Directory $Name
    $hashPath = $jsonPath + '.sha256'
    Require (-not (Test-Path -LiteralPath $jsonPath)) "Immutable evidence already exists: $jsonPath"
    Require (-not (Test-Path -LiteralPath $hashPath)) "Immutable evidence hash already exists: $hashPath"
    $pendingPath = $jsonPath + '.' + [Guid]::NewGuid().ToString('N') + '.pending'
    [System.IO.File]::WriteAllText(
        $pendingPath,
        ($Value | ConvertTo-Json -Depth $Depth),
        [System.Text.UTF8Encoding]::new($false))
    $hash = Get-FileSha256Hex -Path $pendingPath
    Move-Item -LiteralPath $pendingPath -Destination $jsonPath
    [System.IO.File]::WriteAllText(
        $hashPath,
        ($hash + '  ' + $Name + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
    return [pscustomobject][ordered]@{
        path = Get-NormalizedPath -Path $jsonPath
        sha256 = $hash
        hash_path = Get-NormalizedPath -Path $hashPath
    }
}

function Get-FileCatalog {
    param([Parameter(Mandatory = $true)][string]$Root)
    $fullRoot = Get-NormalizedPath -Path $Root
    $catalog = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $fullRoot -File -Recurse -Force | Sort-Object FullName)) {
        $catalog += [pscustomobject][ordered]@{
            path = Get-RelativeChildPath -Root $fullRoot -Child $file.FullName
            bytes = [int64]$file.Length
            sha256 = Get-FileSha256Hex -Path $file.FullName
        }
    }
    return $catalog
}

function Get-FileCatalogSha256 {
    param([Parameter(Mandatory = $true)][object[]]$Catalog)
    $lines = @($Catalog | ForEach-Object { $_.path + '|' + [string]$_.bytes + '|' + $_.sha256 })
    return Get-TextSha256Hex -Text ([string]::Join("`n", $lines))
}

function Read-SharedTextFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-LogSnapshots {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Definitions)
    $snapshots = @()
    foreach ($definition in $Definitions) {
        $exists = Test-Path -LiteralPath $definition.path -PathType Leaf
        $text = ''
        if ($exists) {
            try { $text = Read-SharedTextFile -Path $definition.path }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 50
                $text = Read-SharedTextFile -Path $definition.path
            }
        }
        $snapshots += [pscustomobject][ordered]@{
            label = [string]$definition.label
            path = [string]$definition.path
            exists = [bool]$exists
            text = [string]$text
        }
    }
    return $snapshots
}

function Get-CompletedLogSnapshots {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Snapshots)
    $completed = @()
    foreach ($snapshot in $Snapshots) {
        $text = [string]$snapshot.text
        if ($text.Length -gt 0 -and -not $text.EndsWith("`n", [StringComparison]::Ordinal)) {
            $lastLineFeed = $text.LastIndexOf("`n", [StringComparison]::Ordinal)
            $text = if ($lastLineFeed -ge 0) { $text.Substring(0, $lastLineFeed + 1) } else { '' }
        }
        $completed += [pscustomobject][ordered]@{
            label = $snapshot.label
            path = $snapshot.path
            exists = $snapshot.exists
            text = $text
        }
    }
    return $completed
}

function Find-LogFailures {
    param([Parameter(Mandatory = $true)][object[]]$Snapshots)
    $findings = @()
    $seen = @{}
    foreach ($snapshot in $Snapshots) {
        foreach ($rule in $scanRules) {
            foreach ($match in [regex]::Matches($snapshot.text, $rule.pattern)) {
                $excerpt = [regex]::Replace($match.Value, '\s+', ' ').Trim()
                if ($excerpt.Length -gt 300) { $excerpt = $excerpt.Substring(0, 300) }
                if ($rule.id -ceq 'unity-error') {
                    $allowed = $false
                    foreach ($allowedPattern in $baselineNoiseAllowlist) {
                        if ([regex]::IsMatch($excerpt, $allowedPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                            $allowed = $true
                            break
                        }
                    }
                    if ($allowed) { continue }
                }
                $key = $snapshot.label + '|' + $rule.id + '|' + $excerpt
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $findings += [pscustomobject][ordered]@{
                        log = $snapshot.label
                        rule = $rule.id
                        excerpt = $excerpt
                    }
                }
            }
        }
    }
    return $findings
}

function Assert-HealthyLogs {
    param(
        [Parameter(Mandatory = $true)][object[]]$Snapshots,
        [Parameter(Mandatory = $true)][string]$Phase
    )
    $findings = @(Find-LogFailures -Snapshots $Snapshots)
    if ($findings.Count -gt 0) {
        $first = $findings[0]
        throw "Log scan failed during $Phase ($($first.log)/$($first.rule)): $($first.excerpt)"
    }
}

function Assert-IsolationGuardRuntime {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory = $true)][string]$IsolatedSaveRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedManagedRoot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$PayloadIdentities
    )
    $loadMatches = [regex]::Matches($Text, '(?m)\bLoading \[(?<name>[^\]\r\n]+) (?<version>\d+\.\d+\.\d+(?:\.[^\]\r\n]+)?)\]')
    $expectedGuardLoadText = 'Loading [' + $baseline.isolation_guard_name + ' ' + $baseline.isolation_guard_version + ']'
    $guardLoadPattern = '(?m)\b' + [regex]::Escape($expectedGuardLoadText)
    $guardLoadCount = [regex]::Matches($Text, $guardLoadPattern).Count
    Require ($guardLoadCount -eq 1) 'Expected exactly one BepInEx load record for the harness isolation guard.'
    Require ($loadMatches.Count -eq ($PayloadIdentities.Count + 1)) "BepInEx loaded $($loadMatches.Count) total plugin identities; expected $($PayloadIdentities.Count) payload identities plus one isolation guard."

    $guardMarker = '[RUNIC_SMOKE_ISOLATION_READY] mode=dedicated-server local_save_root=' + $IsolatedSaveRoot +
        ' managed_root=' + $ExpectedManagedRoot +
        ' platform_manager_instance=null distribution_platform=null' +
        ' steam_platform_assembly=absent steam_platform_type=absent cloud_storage_supported=false dont_save_anything=true'
    $guardMarkerCount = [regex]::Matches(
        $Text,
        [regex]::Escape($guardMarker),
        [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
    Require ($guardMarkerCount -eq 1) 'The exact successful isolation-guard marker was not observed exactly once.'
    $guardLoadIndex = $Text.IndexOf($expectedGuardLoadText, [StringComparison]::OrdinalIgnoreCase)
    $guardReadyIndex = $Text.IndexOf($guardMarker, [StringComparison]::OrdinalIgnoreCase)
    Require ($guardLoadIndex -ge 0 -and $guardReadyIndex -gt $guardLoadIndex) 'The isolation guard did not complete after its BepInEx load record.'

    $payloadOrder = @()
    foreach ($identity in $PayloadIdentities) {
        $payloadLoadText = 'Loading [' + $identity.name + ' ' + $identity.version + ']'
        $payloadLoadPattern = '(?m)\b' + [regex]::Escape($payloadLoadText)
        $payloadLoadCount = [regex]::Matches($Text, $payloadLoadPattern).Count
        Require ($payloadLoadCount -eq 1) "Expected exactly one load record for $($identity.guid) $($identity.version)."
        $payloadLoadIndex = $Text.IndexOf($payloadLoadText, [StringComparison]::OrdinalIgnoreCase)
        Require ($payloadLoadIndex -gt $guardReadyIndex) "Payload plugin loaded before isolation was established: $($identity.guid)"
        $payloadOrder += [pscustomobject][ordered]@{
            guid = $identity.guid
            name = $identity.name
            version = $identity.version
            load_index = $payloadLoadIndex
        }
    }

    return [pscustomobject][ordered]@{
        guard_load_text = $expectedGuardLoadText
        guard_load_count = $guardLoadCount
        guard_load_index = $guardLoadIndex
        ready_marker = $guardMarker
        ready_marker_count = $guardMarkerCount
        ready_marker_index = $guardReadyIndex
        payload_load_count = $PayloadIdentities.Count
        total_load_count = $loadMatches.Count
        ready_before_every_payload_load = $true
        payload_loads = $payloadOrder
    }
}

function Get-SteamInstallEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ContentRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedAppId,
        [Parameter(Mandatory = $true)][string]$ExpectedBuildId
    )
    $root = Get-NormalizedPath -Path $ContentRoot
    $directory = [System.IO.DirectoryInfo]::new($root)
    while ($null -ne $directory -and $directory.Name -ine 'common') { $directory = $directory.Parent }
    Require ($null -ne $directory -and $null -ne $directory.Parent) "Could not locate the Steam 'common' ancestor for $root."
    $manifestPath = Join-Path $directory.Parent.FullName ('appmanifest_' + $ExpectedAppId + '.acf')
    Require (Test-Path -LiteralPath $manifestPath -PathType Leaf) "Steam app manifest is missing: $manifestPath"
    $text = [System.IO.File]::ReadAllText($manifestPath)
    $values = [ordered]@{}
    foreach ($key in @('appid', 'buildid', 'TargetBuildID', 'StateFlags', 'installdir')) {
        $match = [regex]::Match($text, '(?im)^\s*"' + [regex]::Escape($key) + '"\s+"(?<value>[^"]*)"\s*$')
        Require $match.Success "Steam app manifest is missing '$key': $manifestPath"
        $values[$key] = $match.Groups['value'].Value
    }
    Require ([string]$values.appid -ceq $ExpectedAppId) "Steam app id drifted in $manifestPath."
    Require ([string]$values.buildid -ceq $ExpectedBuildId) "Steam build id drifted in $manifestPath; expected $ExpectedBuildId, found $($values.buildid)."
    Require ([string]$values.TargetBuildID -ceq $ExpectedBuildId) "Steam target build is not fully installed in $manifestPath."
    Require ([string]$values.StateFlags -ceq '4') "Steam app is not in the fully-installed state in $manifestPath (StateFlags=$($values.StateFlags))."
    $expectedRoot = Get-NormalizedPath -Path (Join-Path $directory.FullName ([string]$values.installdir))
    Require ($root -ceq $expectedRoot) "Content root does not match Steam manifest installdir: expected $expectedRoot, found $root."
    return [pscustomobject][ordered]@{
        app_id = $ExpectedAppId
        build_id = [string]$values.buildid
        target_build_id = [string]$values.TargetBuildID
        state_flags = [string]$values.StateFlags
        manifest_path = Get-NormalizedPath -Path $manifestPath
        manifest_sha256 = Get-FileSha256Hex -Path $manifestPath
        content_root = $root
    }
}

function Get-BepInPluginIdentities {
    param([Parameter(Mandatory = $true)][string]$Path)
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        $identities = @()
        $queue = [System.Collections.Queue]::new()
        foreach ($type in $assembly.MainModule.Types) { $queue.Enqueue($type) }
        while ($queue.Count -gt 0) {
            $type = $queue.Dequeue()
            foreach ($nested in $type.NestedTypes) { $queue.Enqueue($nested) }
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -ne 'BepInEx.BepInPlugin') { continue }
                Require ($attribute.ConstructorArguments.Count -eq 3) "BepInPlugin attribute has an unexpected shape in $Path."
                $identities += [pscustomobject][ordered]@{
                    assembly = [string]$assembly.Name.Name
                    guid = [string]$attribute.ConstructorArguments[0].Value
                    name = [string]$attribute.ConstructorArguments[1].Value
                    version = [string]$attribute.ConstructorArguments[2].Value
                }
            }
        }
        return $identities
    }
    finally { $assembly.Dispose() }
}

function Get-DedicatedSaveIsolationContract {
    param(
        [Parameter(Mandatory = $true)][string]$ManagedRoot,
        [Parameter(Mandatory = $true)][string]$ExpectedAssemblyUtilsSha256,
        [Parameter(Mandatory = $true)][string]$ExpectedSplatformSha256
    )
    $root = Get-NormalizedPath -Path $ManagedRoot
    Require (Test-Path -LiteralPath $root -PathType Container) "Dedicated Managed directory is missing: $root"
    Assert-NoReparsePointInExistingPathChain -Path $root -Label 'dedicated Managed directory'

    $steamPlatformAssembly = Join-Path $root 'Splatform.Steam.dll'
    Require (-not (Test-Path -LiteralPath $steamPlatformAssembly)) "Dedicated isolation requires Splatform.Steam.dll to be absent: $steamPlatformAssembly"
    $assemblyUtils = Join-Path $root 'assembly_utils.dll'
    $splatform = Join-Path $root 'Splatform.dll'
    $assemblyUtilsHash = Assert-Hash -Path $assemblyUtils -Expected $ExpectedAssemblyUtilsSha256 -Label 'dedicated assembly_utils'
    $splatformHash = Assert-Hash -Path $splatform -Expected $ExpectedSplatformSha256 -Label 'dedicated Splatform'

    $utilsAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyUtils)
    try {
        $fileHelpers = @($utilsAssembly.MainModule.Types | Where-Object { $_.FullName -ceq 'FileHelpers' })
        Require ($fileHelpers.Count -eq 1) 'Dedicated assembly_utils must contain exactly one FileHelpers type.'
        $cloudStorageGetters = @($fileHelpers[0].Methods | Where-Object {
                $_.Name -ceq 'get_CloudStorageSupported' -and $_.IsStatic -and $_.ReturnType.FullName -ceq 'System.Boolean'
            })
        Require ($cloudStorageGetters.Count -eq 1) 'Dedicated FileHelpers.CloudStorageSupported getter shape drifted.'
        Require $cloudStorageGetters[0].HasBody 'Dedicated FileHelpers.CloudStorageSupported getter has no managed body.'
        $cloudStorageOpcodes = @($cloudStorageGetters[0].Body.Instructions | ForEach-Object { [string]$_.OpCode.Code })
        Require ($cloudStorageOpcodes.Count -eq 2 -and
            $cloudStorageOpcodes[0] -ceq 'Ldc_I4_0' -and
            $cloudStorageOpcodes[1] -ceq 'Ret') 'Dedicated FileHelpers.CloudStorageSupported is no longer the exact constant-false implementation.'
    }
    finally { $utilsAssembly.Dispose() }

    $managedDlls = @(Get-ChildItem -LiteralPath $root -Filter '*.dll' -File | Sort-Object Name)
    Require ($managedDlls.Count -gt 0) "Dedicated Managed directory contains no DLLs: $root"
    $platformImplementations = @()
    $steamAssemblyReferences = @()
    foreach ($managedDll in $managedDlls) {
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($managedDll.FullName)
        try {
            foreach ($reference in $assembly.MainModule.AssemblyReferences) {
                if ($reference.Name -ceq 'Splatform.Steam') {
                    $steamAssemblyReferences += $managedDll.Name + ' -> ' + $reference.FullName
                }
            }
            $queue = [System.Collections.Queue]::new()
            foreach ($type in $assembly.MainModule.Types) { $queue.Enqueue($type) }
            while ($queue.Count -gt 0) {
                $type = $queue.Dequeue()
                foreach ($nested in $type.NestedTypes) { $queue.Enqueue($nested) }
                foreach ($interface in $type.Interfaces) {
                    if ($interface.InterfaceType.FullName -in @('Splatform.IDistributionPlatform', 'Splatform.ISaveDataProvider')) {
                        $platformImplementations += $managedDll.Name + '!' + $type.FullName + ' -> ' + $interface.InterfaceType.FullName
                    }
                }
            }
        }
        finally { $assembly.Dispose() }
    }
    Require ($steamAssemblyReferences.Count -eq 0) "Dedicated Managed assemblies unexpectedly reference Splatform.Steam: $([string]::Join('; ', $steamAssemblyReferences))"
    Require ($platformImplementations.Count -eq 0) "Dedicated Managed assemblies unexpectedly implement a platform/save provider: $([string]::Join('; ', $platformImplementations))"

    return [pscustomobject][ordered]@{
        managed_root = $root
        managed_dll_count = $managedDlls.Count
        splatform_steam_path = Get-NormalizedPath -Path $steamPlatformAssembly
        splatform_steam_present = $false
        splatform_steam_reference_count = $steamAssemblyReferences.Count
        distribution_or_save_provider_implementation_count = $platformImplementations.Count
        assembly_utils_path = Get-NormalizedPath -Path $assemblyUtils
        assembly_utils_sha256 = $assemblyUtilsHash
        splatform_path = Get-NormalizedPath -Path $splatform
        splatform_sha256 = $splatformHash
        cloud_storage_supported_getter_opcodes = $cloudStorageOpcodes
        cloud_storage_supported_constant = $false
    }
}

function Quote-ProcessArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    Require ($Value.IndexOf('"') -lt 0) 'A process argument unexpectedly contains a quote.'
    return '"' + $Value + '"'
}

function Invoke-WithIsolatedLaunchEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$SteamAppId,
        [Parameter(Mandatory = $true)][string]$TargetAssembly,
        [Parameter(Mandatory = $true)][string]$CoreSearchPath,
        [Parameter(Mandatory = $true)][string]$SaveRoot,
        [Parameter(Mandatory = $true)][string]$LiveValheimDataRoot,
        [Parameter(Mandatory = $true)][string]$ProcessMode,
        [Parameter(Mandatory = $true)][string]$ExpectedManagedRoot,
        [Parameter(Mandatory = $true)][string]$ProcessProfileRoot,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Evidence,
        [Parameter(Mandatory = $true)][scriptblock]$Launch
    )
    $names = @(
        'SteamAppId',
        'DOORSTOP_DISABLE',
        'DOORSTOP_INITIALIZED',
        'DOORSTOP_ENABLED',
        'DOORSTOP_TARGET_ASSEMBLY',
        'DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE',
        'RUNIC_SMOKE_SAVEDIR',
        'RUNIC_SMOKE_LIVE_VALHEIM_DATA',
        'RUNIC_SMOKE_PROCESS_MODE',
        'RUNIC_SMOKE_EXPECTED_MANAGED_ROOT',
        'USERPROFILE',
        'APPDATA',
        'LOCALAPPDATA',
        'TEMP',
        'TMP'
    )
    $prior = @{}
    foreach ($name in $names) { $prior[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        [Environment]::SetEnvironmentVariable('SteamAppId', $SteamAppId, 'Process')
        # On the PowerShell/.NET runtime used by this harness, passing $null to
        # SetEnvironmentVariable leaves a present-but-empty variable. Doorstop tests
        # presence, not truthiness, so remove both recursion guards through Env:.
        Remove-Item -LiteralPath 'Env:DOORSTOP_DISABLE' -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath 'Env:DOORSTOP_INITIALIZED' -ErrorAction SilentlyContinue
        # Doorstop 4 accepts these values through both its Windows CLI and environment.
        # Supplying both keeps the command line independently auditable while avoiding
        # inheritance or argument-parsing ambiguity in Unity 6.
        [Environment]::SetEnvironmentVariable('DOORSTOP_ENABLED', '1', 'Process')
        [Environment]::SetEnvironmentVariable('DOORSTOP_TARGET_ASSEMBLY', $TargetAssembly, 'Process')
        [Environment]::SetEnvironmentVariable('DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE', $CoreSearchPath, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_SAVEDIR', $SaveRoot, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_LIVE_VALHEIM_DATA', $LiveValheimDataRoot, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_PROCESS_MODE', $ProcessMode, 'Process')
        [Environment]::SetEnvironmentVariable('RUNIC_SMOKE_EXPECTED_MANAGED_ROOT', $ExpectedManagedRoot, 'Process')
        [Environment]::SetEnvironmentVariable('USERPROFILE', $ProcessProfileRoot, 'Process')
        [Environment]::SetEnvironmentVariable('APPDATA', (Join-Path $ProcessProfileRoot 'AppData\Roaming'), 'Process')
        [Environment]::SetEnvironmentVariable('LOCALAPPDATA', (Join-Path $ProcessProfileRoot 'AppData\Local'), 'Process')
        [Environment]::SetEnvironmentVariable('TEMP', (Join-Path $ProcessProfileRoot 'AppData\Local\Temp'), 'Process')
        [Environment]::SetEnvironmentVariable('TMP', (Join-Path $ProcessProfileRoot 'AppData\Local\Temp'), 'Process')
        Require ($null -eq [Environment]::GetEnvironmentVariable('DOORSTOP_DISABLE', 'Process')) 'DOORSTOP_DISABLE remained present immediately before launch.'
        Require ($null -eq [Environment]::GetEnvironmentVariable('DOORSTOP_INITIALIZED', 'Process')) 'DOORSTOP_INITIALIZED remained present immediately before launch.'
        foreach ($name in $names) {
            $value = [Environment]::GetEnvironmentVariable($name, 'Process')
            $Evidence[$name] = [ordered]@{
                present = $null -ne $value
                value = if ($name -in @('DOORSTOP_TARGET_ASSEMBLY', 'DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE')) { $value } else { [string]$value }
            }
        }
        return & $Launch
    }
    finally {
        foreach ($name in $names) {
            if ($null -eq $prior[$name]) {
                Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($name, [string]$prior[$name], 'Process')
            }
        }
        foreach ($name in $names) {
            $restored = [Environment]::GetEnvironmentVariable($name, 'Process')
            $presenceRestored = ($null -eq $prior[$name]) -eq ($null -eq $restored)
            $valueRestored = [string]::Equals([string]$prior[$name], [string]$restored, [StringComparison]::Ordinal)
            Require ($presenceRestored -and $valueRestored) "Process environment variable '$name' was not restored."
        }
    }
}

function Assert-TestPortsAvailable {
    param([Parameter(Mandatory = $true)][int]$BasePort)
    foreach ($candidate in @($BasePort, $BasePort + 1, $BasePort + 2)) {
        $udp = $null
        try {
            $udp = [System.Net.Sockets.UdpClient]::new($candidate)
        }
        catch { throw "Required isolated UDP port $candidate is unavailable. Choose another -Port value." }
        finally { if ($null -ne $udp) { $udp.Dispose() } }
    }
}

function Assert-NoSteamDesktopProcesses {
    $blocked = @(Get-Process -Name @('steam', 'steamwebhelper', 'GameOverlayUI') -ErrorAction SilentlyContinue)
    if ($blocked.Count -gt 0) {
        $description = [string]::Join(', ', @($blocked | Sort-Object ProcessName, Id | ForEach-Object {
                    $_.ProcessName + ':' + $_.Id
                }))
        throw "Fail-closed isolation: close the Steam desktop client before smoke testing. Found $description. Steam must be closed because its AutoCloud process scans and uploads the live LocalLow Valheim profile for runtime AppID 892970."
    }
}

function Assert-ExclusiveSmokeHostState {
    Assert-NoSteamDesktopProcesses
    $blocked = @(Get-Process -Name @('valheim', 'valheim_server') -ErrorAction SilentlyContinue)
    if ($blocked.Count -gt 0) {
        $description = [string]::Join(', ', @($blocked | Sort-Object ProcessName, Id | ForEach-Object {
                    $_.ProcessName + ':' + $_.Id
                }))
        throw "Fail-closed isolation: close Valheim and every dedicated server before smoke testing. Found $description."
    }
}

function Get-SteamDesktopRoot {
    $property = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop
    Require (-not [string]::IsNullOrWhiteSpace([string]$property.SteamPath)) 'SteamPath is missing from the current-user Steam registry key.'
    $path = Get-NormalizedPath -Path ([string]$property.SteamPath)
    Require (Test-Path -LiteralPath $path -PathType Container) "Steam desktop root is missing: $path"
    return $path
}

function Stop-TrackedProcess {
    param([AllowNull()][System.Diagnostics.Process]$Process)
    if ($null -eq $Process) { return $false }
    try {
        $Process.Refresh()
        if ($Process.HasExited) { return $false }
        # Kill the exact Process object returned by Start-Process. Never enumerate or
        # terminate any other Valheim process.
        $Process.Kill()
        Require ($Process.WaitForExit(30000)) 'The tracked isolated server did not exit within 30 seconds.'
        return $true
    }
    finally { }
}

function Assert-Hash {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "$Label is missing: $Path"
    $actual = Get-FileSha256Hex -Path $Path
    Require ($actual -ceq $Expected) "$Label hash drifted; expected $Expected, found ${actual}: $Path"
    return $actual
}

$scriptPath = Get-NormalizedPath -Path $MyInvocation.MyCommand.Path
$scriptHashAtStart = Get-FileSha256Hex -Path $scriptPath
$repoRoot = Get-NormalizedPath -Path (Join-Path $PSScriptRoot '..')
$serverRootPath = Get-NormalizedPath -Path $ServerRoot
$clientRootPath = Get-NormalizedPath -Path $ClientRoot
$archivePath = Get-NormalizedPath -Path $BepInExArchive
$serverExecutable = Join-Path $serverRootPath 'valheim_server.exe'
$serverManagedRoot = Join-Path $serverRootPath 'valheim_server_Data\Managed'
$serverAssembly = Join-Path $serverManagedRoot 'assembly_valheim.dll'
$serverAssemblyUtils = Join-Path $serverManagedRoot 'assembly_utils.dll'
$serverSplatform = Join-Path $serverManagedRoot 'Splatform.dll'
$serverSplatformSteam = Join-Path $serverManagedRoot 'Splatform.Steam.dll'
$serverUnityPlayer = Join-Path $serverRootPath 'UnityPlayer.dll'
$serverSteamAppIdFile = Join-Path $serverRootPath 'steam_appid.txt'
$serverStartScript = Join-Path $serverRootPath 'start_headless_server.bat'
$clientExecutable = Join-Path $clientRootPath 'valheim.exe'
$clientAssembly = Join-Path $clientRootPath 'valheim_Data\Managed\assembly_valheim.dll'
$clientUnityPlayer = Join-Path $clientRootPath 'UnityPlayer.dll'
$isolationGuardProject = Get-NormalizedPath -Path (Join-Path $PSScriptRoot 'Valheim10DedicatedIsolationGuard\Valheim10DedicatedIsolationGuard.csproj')
$isolationGuardSourceRoot = Split-Path -Parent $isolationGuardProject
$isolationGuardSource = Join-Path $isolationGuardSourceRoot 'Plugin.cs'

Require (Test-Path -LiteralPath $serverRootPath -PathType Container) "Dedicated-server root is missing: $serverRootPath"
Require (Test-Path -LiteralPath $clientRootPath -PathType Container) "Valheim client root is missing: $clientRootPath"
Assert-NoReparsePointInExistingPathChain -Path $serverRootPath -Label 'dedicated-server root'
Assert-NoReparsePointInExistingPathChain -Path $clientRootPath -Label 'Valheim client root'
Assert-NoReparsePointInExistingPathChain -Path $archivePath -Label 'BepInEx archive'
Assert-Hash -Path $serverExecutable -Expected $baseline.server_executable_sha256 -Label 'Valheim 1.0 server executable' | Out-Null
Assert-Hash -Path $serverAssembly -Expected $baseline.server_assembly_sha256 -Label 'Valheim 1.0 server assembly' | Out-Null
Assert-Hash -Path $serverAssemblyUtils -Expected $baseline.server_assembly_utils_sha256 -Label 'Valheim 1.0 server assembly_utils' | Out-Null
Assert-Hash -Path $serverSplatform -Expected $baseline.server_splatform_sha256 -Label 'Valheim 1.0 server Splatform' | Out-Null
Require (-not (Test-Path -LiteralPath $serverSplatformSteam)) "Dedicated isolation requires the installed server Splatform.Steam.dll to be absent: $serverSplatformSteam"
Assert-Hash -Path $serverUnityPlayer -Expected $baseline.server_unityplayer_sha256 -Label 'Valheim 1.0 server UnityPlayer' | Out-Null
Assert-Hash -Path $serverSteamAppIdFile -Expected $baseline.server_steam_appid_file_sha256 -Label 'Valheim dedicated-server steam_appid file' | Out-Null
Require (Test-Path -LiteralPath $serverStartScript -PathType Leaf) "Valheim dedicated-server start script is missing: $serverStartScript"
$serverStartScriptText = [System.IO.File]::ReadAllText($serverStartScript)
Require ([regex]::IsMatch($serverStartScriptText, '(?im)^\s*set\s+SteamAppId\s*=\s*' + [regex]::Escape($baseline.server_runtime_app_id) + '\s*$')) "The shipped dedicated-server start script no longer selects runtime SteamAppId $($baseline.server_runtime_app_id)."
Assert-Hash -Path $clientExecutable -Expected $baseline.client_executable_sha256 -Label 'Valheim 1.0 client executable' | Out-Null
Assert-Hash -Path $clientAssembly -Expected $baseline.client_assembly_sha256 -Label 'Valheim 1.0 client assembly' | Out-Null
Assert-Hash -Path $clientUnityPlayer -Expected $baseline.client_unityplayer_sha256 -Label 'Valheim 1.0 client UnityPlayer' | Out-Null
Assert-Hash -Path $archivePath -Expected $baseline.bepinex_archive_sha256 -Label 'BepInExPack Valheim 5.4.2350 archive' | Out-Null
Require (Test-Path -LiteralPath $isolationGuardProject -PathType Leaf) "Harness isolation-guard project is missing: $isolationGuardProject"
Assert-Hash -Path $isolationGuardSource -Expected $baseline.isolation_guard_source_sha256 -Label 'harness isolation-guard source' | Out-Null
Assert-Hash -Path $isolationGuardProject -Expected $baseline.isolation_guard_project_sha256 -Label 'harness isolation-guard project' | Out-Null
Assert-ExclusiveSmokeHostState

$serverSteam = Get-SteamInstallEvidence -ContentRoot $serverRootPath -ExpectedAppId $baseline.server_install_app_id -ExpectedBuildId $baseline.server_build_id
$clientSteam = Get-SteamInstallEvidence -ContentRoot $clientRootPath -ExpectedAppId $baseline.client_app_id -ExpectedBuildId $baseline.client_build_id
$steamDesktopRoot = Get-SteamDesktopRoot
$steamUserdataRoot = Get-NormalizedPath -Path (Join-Path $steamDesktopRoot 'userdata')
Require (Test-Path -LiteralPath $steamUserdataRoot -PathType Container) "Steam userdata root is missing: $steamUserdataRoot"
Assert-NoReparsePointInExistingPathChain -Path $steamDesktopRoot -Label 'Steam desktop root'
Assert-NoReparsePointInExistingPathChain -Path $steamUserdataRoot -Label 'Steam userdata root'
$steamUserRoots = @(Get-ChildItem -LiteralPath $steamUserdataRoot -Directory -Force | Where-Object { $_.Name -match '^\d+$' } | Sort-Object Name)
Require ($steamUserRoots.Count -gt 0) "No numeric Steam user roots were found beneath $steamUserdataRoot."
Assert-TestPortsAvailable -BasePort $Port

$mode = if ([string]::IsNullOrWhiteSpace($PluginDirectory)) { 'loader-only' } else { 'plugins' }
$pluginSourceRoot = ''
if ($mode -ceq 'plugins') {
    $pluginSourceRoot = Get-NormalizedPath -Path $PluginDirectory
    Require (Test-Path -LiteralPath $pluginSourceRoot -PathType Container) "Explicit plugin DLL directory is missing: $pluginSourceRoot"
    Assert-NoReparsePointInExistingPathChain -Path $pluginSourceRoot -Label 'explicit plugin source'
    Require ($ExpectedPluginCount -gt 0) '-ExpectedPluginCount must be explicit and greater than zero in plugin mode.'
    Require ([regex]::IsMatch($ExpectedPluginCatalogSha256, '^[0-9A-Fa-f]{64}$')) '-ExpectedPluginCatalogSha256 must explicitly pin the flat plugin payload in plugin mode.'
}
else {
    Require ([string]::IsNullOrWhiteSpace($ExpectedPluginCatalogSha256)) '-ExpectedPluginCatalogSha256 is only valid in plugin mode.'
}

if ([string]::IsNullOrWhiteSpace($EvidenceBase)) {
    $evidenceBasePath = Get-NormalizedPath -Path (Join-Path $repoRoot 'artifacts\Valheim1.0\DedicatedSmoke')
}
else { $evidenceBasePath = Get-NormalizedPath -Path $EvidenceBase }
Assert-NoReparsePointInExistingPathChain -Path $evidenceBasePath -Label 'evidence base'

foreach ($protectedRoot in @($serverRootPath, $clientRootPath)) {
    Require (-not (Test-IsSameOrChildPath -Candidate $evidenceBasePath -Parent $protectedRoot)) "Evidence base may not be inside a live game installation: $evidenceBasePath"
}
$defaultValheimData = Get-NormalizedPath -Path (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\IronGate\Valheim')
Assert-NoReparsePointInExistingPathChain -Path $defaultValheimData -Label 'live Valheim data'
Require (-not (Test-IsSameOrChildPath -Candidate $evidenceBasePath -Parent $defaultValheimData)) "Evidence base may not be inside the live Valheim data directory: $evidenceBasePath"
Require (-not (Test-IsSameOrChildPath -Candidate $evidenceBasePath -Parent $steamUserdataRoot)) "Evidence base may not be inside Steam userdata: $evidenceBasePath"

New-Item -ItemType Directory -Path $evidenceBasePath -Force | Out-Null
Assert-NoReparsePointInExistingPathChain -Path $evidenceBasePath -Label 'created evidence base'
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + $mode + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $evidenceBasePath $runId
Require (-not (Test-Path -LiteralPath $runRoot)) "Timestamped evidence root already exists and will not be overwritten: $runRoot"
New-Item -ItemType Directory -Path $runRoot | Out-Null
Assert-NoReparsePointInExistingPathChain -Path $runRoot -Label 'created evidence run root'

$process = $null
$processId = $null
$processStartedUtc = ''
$processWasStoppedByHarness = $false
$failureMessage = ''
$protectedBefore = @()
$protectedPreLaunch = @()
$protectedAfter = @()
$protectedDefinitions = @()
$pluginSourceBefore = $null
$isolationGuardSourceBefore = $null
$launchEnvironmentEvidence = [ordered]@{}
$protectedBeforeEvidence = $null
$protectedPreLaunchEvidence = $null
$protectedAfterEvidence = $null
$isolationChecks = @()
$preLaunchIsolationChecks = @()
$pluginSourceAfter = $null
$isolationGuardSourceAfter = $null
$logDefinitions = @()
$runtimeFileCopies = @()
$runtimeDirectoryCopies = @()
$pluginSourceCatalog = @()
$pluginSourceCatalogSha256 = ''
$stagedRuntimeFileChecks = @()
$stagedRuntimeDirectoryChecks = @()
$stagedPluginChecks = @()
$coreCatalogAfter = @()
$coreCatalogAfterSha256 = ''
$serverStage = ''
$liveValheimRegistrySubKey = 'Software\IronGate\Valheim'
$liveRegistryBefore = $null
$liveRegistryPreLaunch = $null
$liveRegistryAfter = $null
$liveRegistryBaselineContract = $null
$liveRegistryRuntimeComparison = $null
$dedicatedSourceContractBefore = $null
$dedicatedStagedContractBefore = $null
$dedicatedSourceContractPreLaunch = $null
$dedicatedStagedContractPreLaunch = $null
$dedicatedSourceContractAfter = $null
$dedicatedStagedContractAfter = $null

try {
    $protectedDefinitions = @(
        [ordered]@{ label = 'server-install-full'; path = $serverRootPath; hash = $true }
        [ordered]@{ label = 'client-install-full'; path = $clientRootPath; hash = $true }
        [ordered]@{ label = 'server-live-native-config'; path = (Join-Path $serverRootPath 'config\config.vdf'); hash = $true }
        [ordered]@{ label = 'server-live-connection-log-port-28630'; path = (Join-Path $serverRootPath 'logs\connection_log_28630.txt'); hash = $true }
        [ordered]@{ label = ('server-live-connection-log-test-port-' + $Port); path = (Join-Path $serverRootPath ('logs\connection_log_' + $Port + '.txt')); hash = $true }
        [ordered]@{ label = 'server-steam-app-manifest'; path = $serverSteam.manifest_path; hash = $true }
        [ordered]@{ label = 'client-steam-app-manifest'; path = $clientSteam.manifest_path; hash = $true }
        [ordered]@{ label = 'default-valheim-data-full'; path = $defaultValheimData; hash = $true }
        [ordered]@{ label = 'default-valheim-worlds-legacy'; path = (Join-Path $defaultValheimData 'worlds'); hash = $true }
        [ordered]@{ label = 'default-valheim-worlds-local'; path = (Join-Path $defaultValheimData 'worlds_local'); hash = $true }
        [ordered]@{ label = 'steam-cloud-log'; path = (Join-Path $steamDesktopRoot 'logs\cloud_log.txt'); hash = $true }
        [ordered]@{ label = 'steam-content-log'; path = (Join-Path $steamDesktopRoot 'logs\content_log.txt'); hash = $true }
    )
    foreach ($steamUserRoot in $steamUserRoots) {
        $protectedDefinitions += [ordered]@{
            label = 'steam-userdata-' + $steamUserRoot.Name + '-app-' + $baseline.client_app_id
            path = Join-Path $steamUserRoot.FullName $baseline.client_app_id
            hash = $true
        }
        $protectedDefinitions += [ordered]@{
            label = 'steam-userdata-' + $steamUserRoot.Name + '-localconfig'
            path = Join-Path $steamUserRoot.FullName 'config\localconfig.vdf'
            hash = $true
        }
    }
    $protectedBefore = @(Get-ProtectedPathSnapshots -Definitions $protectedDefinitions)
    $liveRegistryBefore = Get-RegistryTreeFingerprint -CurrentUserSubKey $liveValheimRegistrySubKey
    $liveRegistryBaselineContract = Compare-PlayerPrefsRegistrySnapshots `
        -Before $liveRegistryBefore `
        -After $liveRegistryBefore `
        -AllowedVolatileValues $allowedVolatilePlayerPrefsValues `
        -ExpectedStableValueCount $expectedStablePlayerPrefsValueCount
    if ($mode -ceq 'plugins') {
        $pluginSourceBefore = Get-DirectoryFingerprint -Path $pluginSourceRoot -HashFileContents $true
    }
    $isolationGuardSourceBefore = Get-DirectoryFingerprint -Path $isolationGuardSourceRoot -HashFileContents $true
    $protectedBeforeEvidence = Write-ImmutableJsonEvidence -Directory $runRoot -Name 'PROTECTED-BEFORE.json' -Value ([ordered]@{
            schema = 'runic-valheim10-protected-snapshot/v1'
            phase = 'before-staging'
            captured_utc = [DateTime]::UtcNow.ToString('O')
            protected_paths = $protectedBefore
            live_valheim_registry = $liveRegistryBefore
            live_valheim_registry_baseline_contract = $liveRegistryBaselineContract
            plugin_source = $pluginSourceBefore
            isolation_guard_source = $isolationGuardSourceBefore
        })
    Require ([bool]$liveRegistryBaselineContract.passed) ('Live Valheim PlayerPrefs registry does not match the pinned ' + [string]$expectedStablePlayerPrefsValueCount + '-stable-plus-four-volatile baseline: ' + [string]::Join(' ', @($liveRegistryBaselineContract.issues)))

    $extractRoot = Join-Path $runRoot 'loader-source'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot
    $packRoot = Join-Path $extractRoot 'BepInExPack_Valheim'
    $serverStage = Join-Path $runRoot 'server-stage'
    New-Item -ItemType Directory -Path $serverStage | Out-Null
    foreach ($runtimeFileName in @(
            'valheim_server.exe',
            'UnityPlayer.dll',
            'UnityCrashHandler64.exe',
            'steam_appid.txt',
            'steamclient.dll',
            'steamclient64.dll',
            'steamwebrtc.dll',
            'steamwebrtc64.dll',
            'tier0_s.dll',
            'tier0_s64.dll',
            'vstdlib_s.dll',
            'vstdlib_s64.dll')) {
        $sourceRuntimeFile = Join-Path $serverRootPath $runtimeFileName
        Require (Test-Path -LiteralPath $sourceRuntimeFile -PathType Leaf) "Required server runtime file is missing: $sourceRuntimeFile"
        Require ((((Get-Item -LiteralPath $sourceRuntimeFile -Force).Attributes) -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Runtime source file may not be a reparse point: $sourceRuntimeFile"
        $isolatedRuntimeFile = Join-Path $serverStage $runtimeFileName
        $sourceRuntimeFileHash = Get-FileSha256Hex -Path $sourceRuntimeFile
        Copy-Item -LiteralPath $sourceRuntimeFile -Destination $isolatedRuntimeFile
        Require ((((Get-Item -LiteralPath $isolatedRuntimeFile -Force).Attributes) -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Isolated runtime file unexpectedly became a reparse point: $isolatedRuntimeFile"
        Require ($sourceRuntimeFileHash -ceq (Get-FileSha256Hex -Path $isolatedRuntimeFile)) "Isolated server runtime copy drifted: $runtimeFileName"
        $runtimeFileCopies += [pscustomobject][ordered]@{
            name = $runtimeFileName
            source_path = Get-NormalizedPath -Path $sourceRuntimeFile
            isolated_path = Get-NormalizedPath -Path $isolatedRuntimeFile
            bytes = [int64](Get-Item -LiteralPath $sourceRuntimeFile).Length
            sha256 = $sourceRuntimeFileHash
        }
    }
    foreach ($runtimeDirectoryName in @(
            'D3D12',
            'MonoBleedingEdge',
            'Valheim_BurstDebugInformation_DoNotShip',
            'valheim_server_Data')) {
        $runtimeTarget = Join-Path $serverRootPath $runtimeDirectoryName
        Require (Test-Path -LiteralPath $runtimeTarget -PathType Container) "Required server runtime directory is missing: $runtimeTarget"
        $sourceRuntimeItem = Get-Item -LiteralPath $runtimeTarget -Force
        Require (($sourceRuntimeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Runtime source directory may not be a reparse point: $runtimeTarget"
        $sourceReparseEntries = @(Get-ChildItem -LiteralPath $runtimeTarget -Recurse -Force | Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            })
        Require ($sourceReparseEntries.Count -eq 0) "Runtime source directory contains a reparse point and cannot be safely isolated: $runtimeTarget"
        $sourceCatalog = @(Get-FileCatalog -Root $runtimeTarget)
        $sourceCatalogHash = Get-FileCatalogSha256 -Catalog $sourceCatalog
        Copy-Item -LiteralPath $runtimeTarget -Destination $serverStage -Recurse
        $runtimeCopy = Join-Path $serverStage $runtimeDirectoryName
        Require (Test-Path -LiteralPath $runtimeCopy -PathType Container) "Isolated runtime directory copy is missing: $runtimeCopy"
        $copiedRuntimeItem = Get-Item -LiteralPath $runtimeCopy -Force
        Require (($copiedRuntimeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Isolated runtime copy unexpectedly became a reparse point: $runtimeCopy"
        $copiedReparseEntries = @(Get-ChildItem -LiteralPath $runtimeCopy -Recurse -Force | Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            })
        Require ($copiedReparseEntries.Count -eq 0) "Isolated runtime copy contains a reparse point: $runtimeCopy"
        $copiedCatalog = @(Get-FileCatalog -Root $runtimeCopy)
        $copiedCatalogHash = Get-FileCatalogSha256 -Catalog $copiedCatalog
        Require ($sourceCatalog.Count -eq $copiedCatalog.Count -and $sourceCatalogHash -ceq $copiedCatalogHash) "Isolated runtime directory copy failed exact content verification: $runtimeDirectoryName"
        $runtimeDirectoryCopies += [pscustomobject][ordered]@{
            name = $runtimeDirectoryName
            source_path = Get-NormalizedPath -Path $runtimeTarget
            isolated_path = Get-NormalizedPath -Path $runtimeCopy
            file_count = $sourceCatalog.Count
            total_bytes = [int64](($sourceCatalog | Measure-Object -Property bytes -Sum).Sum)
            catalog_sha256 = $sourceCatalogHash
        }
    }

    $packBepInEx = Join-Path $packRoot 'BepInEx'
    Require (Test-Path -LiteralPath $packBepInEx -PathType Container) "Downloaded BepInEx archive has an unexpected layout: $packBepInEx"
    Copy-Item -LiteralPath $packBepInEx -Destination $serverStage -Recurse
    foreach ($loaderRootFile in @('.doorstop_version', 'doorstop_config.ini')) {
        Copy-Item -LiteralPath (Join-Path $packRoot $loaderRootFile) -Destination (Join-Path $serverStage $loaderRootFile)
    }

    $bepRoot = Join-Path $serverStage 'BepInEx'
    $coreRoot = Join-Path $bepRoot 'core'
    $isolatedPlugins = Join-Path $bepRoot 'plugins'
    $isolatedConfig = Join-Path $bepRoot 'config'
    $isolatedSavedir = Join-Path $runRoot 'savedir'
    $processProfileRoot = Join-Path $runRoot 'process-profile'
    $processTempRoot = Join-Path $processProfileRoot 'AppData\Local\Temp'
    foreach ($directory in @($packRoot, $serverStage, $bepRoot, $coreRoot)) {
        Require (Test-Path -LiteralPath $directory -PathType Container) "Downloaded BepInEx archive has an unexpected layout: $directory"
    }
    foreach ($directory in @(
            $isolatedPlugins,
            $isolatedConfig,
            $isolatedSavedir,
            $processTempRoot,
            (Join-Path $processProfileRoot 'AppData\Roaming'))) {
        if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    }
    foreach ($isolatedRoot in @($isolatedSavedir, $processProfileRoot, $processTempRoot)) {
        Assert-NoReparsePointInExistingPathChain -Path $isolatedRoot -Label 'isolated runtime root'
        Require (Test-IsSameOrChildPath -Candidate $isolatedRoot -Parent $runRoot) "Isolated process root escaped the immutable run root: $isolatedRoot"
        Require (-not (Test-IsSameOrChildPath -Candidate $isolatedRoot -Parent $defaultValheimData)) "Isolated process root overlaps live Valheim data: $isolatedRoot"
    }

    $packWinHttp = Join-Path $packRoot 'winhttp.dll'
    $packDoorstopConfig = Join-Path $packRoot 'doorstop_config.ini'
    $packDoorstopVersion = Join-Path $packRoot '.doorstop_version'
    $isolatedWinHttpProxy = Join-Path $serverStage 'winhttp.dll'
    $isolatedDoorstopConfig = Join-Path $serverStage 'doorstop_config.ini'
    $isolatedDoorstopVersion = Join-Path $serverStage '.doorstop_version'
    Assert-Hash -Path $packWinHttp -Expected $baseline.doorstop_proxy_sha256 -Label 'Downloaded pack Doorstop proxy' | Out-Null
    Assert-Hash -Path $packDoorstopConfig -Expected $baseline.doorstop_config_sha256 -Label 'Downloaded pack Doorstop configuration' | Out-Null
    Assert-Hash -Path $packDoorstopVersion -Expected $baseline.doorstop_version_sha256 -Label 'Downloaded pack Doorstop version marker' | Out-Null
    Assert-Hash -Path $isolatedDoorstopConfig -Expected $baseline.doorstop_config_sha256 -Label 'Isolated Doorstop configuration' | Out-Null
    Assert-Hash -Path $isolatedDoorstopVersion -Expected $baseline.doorstop_version_sha256 -Label 'Isolated Doorstop version marker' | Out-Null
    # Stage the exact release proxy from the exact-hashed BepInExPack. The dedicated
    # AppID is supplied explicitly at launch so Steam does not relaunch the server and
    # inherit Doorstop's recursion guards into what should be the primary process.
    Copy-Item -LiteralPath $packWinHttp -Destination $isolatedWinHttpProxy
    Assert-Hash -Path $isolatedWinHttpProxy -Expected $baseline.doorstop_proxy_sha256 -Label 'isolated release Doorstop WINHTTP proxy' | Out-Null

    $criticalCore = [ordered]@{
        'BepInEx.dll' = $baseline.bepinex_sha256
        'BepInEx.Preloader.dll' = $baseline.preloader_sha256
        '0Harmony.dll' = $baseline.harmony_sha256
        'BepInEx.Harmony.dll' = $baseline.bepinex_harmony_sha256
        'Mono.Cecil.dll' = $baseline.mono_cecil_sha256
    }
    foreach ($name in $criticalCore.Keys) {
        Assert-Hash -Path (Join-Path $coreRoot $name) -Expected ([string]$criticalCore[$name]) -Label "BepInEx core $name" | Out-Null
    }
    $observedBepInExVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $coreRoot 'BepInEx.dll')).FileVersion
    Require ($observedBepInExVersion -ceq $baseline.bepinex_file_version) "BepInEx file version drifted; expected $($baseline.bepinex_file_version), found $observedBepInExVersion."
    $doorstopStrings = [System.Text.Encoding]::Unicode.GetString([System.IO.File]::ReadAllBytes($isolatedWinHttpProxy))
    foreach ($requiredSwitch in @('--doorstop-enabled', '--doorstop-target-assembly')) {
        Require ($doorstopStrings.IndexOf($requiredSwitch, [StringComparison]::Ordinal) -ge 0) "The pinned Doorstop proxy does not expose required switch $requiredSwitch."
    }

    $coreCatalog = @(Get-FileCatalog -Root $coreRoot)
    Require ($coreCatalog.Count -gt 0) 'The isolated BepInEx core catalog is empty.'
    $coreCatalogSha256 = Get-FileCatalogSha256 -Catalog $coreCatalog
    $cecilPath = Join-Path $coreRoot 'Mono.Cecil.dll'
    if ($null -eq ('Mono.Cecil.AssemblyDefinition' -as [type])) { Add-Type -Path $cecilPath }

    $stagedServerManagedRoot = Join-Path $serverStage 'valheim_server_Data\Managed'
    $dedicatedSourceContractBefore = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $serverManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
    $dedicatedStagedContractBefore = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $stagedServerManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256

    # Build and stage the harness-only early isolation guard outside every release
    # payload. Dedicated mode establishes the Utils override before platform
    # initialization, blocks saving, and proves the server exposes no Steam/cloud
    # save provider. The guard's default graphical-client branch remains separate.
    $guardBuildRoot = Join-Path $runRoot 'isolation-guard-build'
    $guardObjectRoot = Join-Path $guardBuildRoot 'obj'
    $guardOutputRoot = Join-Path $guardBuildRoot 'out'
    New-Item -ItemType Directory -Path $guardObjectRoot, $guardOutputRoot | Out-Null
    $guardBuildLog = Join-Path $guardBuildRoot 'dotnet-build.log'
    $dotnetCommand = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $guardBuildArguments = @(
        'build',
        $isolationGuardProject,
        '--configuration', 'Release',
        '--nologo',
        '--verbosity', 'minimal',
        ('-p:GameManagedPath=' + $serverManagedRoot),
        ('-p:BepInExCorePath=' + $coreRoot),
        ('-p:BaseIntermediateOutputPath=' + $guardObjectRoot + '\'),
        ('-p:MSBuildProjectExtensionsPath=' + $guardObjectRoot + '\'),
        ('-p:OutputPath=' + $guardOutputRoot + '\')
    )
    $guardBuildText = (& $dotnetCommand @guardBuildArguments 2>&1 | Out-String)
    $guardBuildExitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllText($guardBuildLog, $guardBuildText, [System.Text.UTF8Encoding]::new($false))
    Require ($guardBuildExitCode -eq 0) "Harness isolation guard failed to build (exit $guardBuildExitCode); see $guardBuildLog"
    $builtGuard = Join-Path $guardOutputRoot 'Valheim10DedicatedIsolationGuard.dll'
    Require (Test-Path -LiteralPath $builtGuard -PathType Leaf) "Harness isolation guard output is missing: $builtGuard"
    $guardIdentities = @(Get-BepInPluginIdentities -Path $builtGuard)
    Require ($guardIdentities.Count -eq 1) 'Harness isolation guard must expose exactly one BepInPlugin identity.'
    $guardIdentity = $guardIdentities[0]
    Require ($guardIdentity.guid -ceq $baseline.isolation_guard_guid) "Harness isolation guard GUID drifted: $($guardIdentity.guid)"
    Require ($guardIdentity.name -ceq $baseline.isolation_guard_name) "Harness isolation guard name drifted: $($guardIdentity.name)"
    Require ($guardIdentity.version -ceq $baseline.isolation_guard_version) "Harness isolation guard version drifted: $($guardIdentity.version)"
    $isolatedGuard = Join-Path $isolatedPlugins '000-Valheim10DedicatedIsolationGuard.dll'
    Copy-Item -LiteralPath $builtGuard -Destination $isolatedGuard
    Require ((((Get-Item -LiteralPath $builtGuard -Force).Attributes) -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Built isolation guard may not be a reparse point.'
    Require ((((Get-Item -LiteralPath $isolatedGuard -Force).Attributes) -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Staged isolation guard may not be a reparse point.'
    $guardHash = Get-FileSha256Hex -Path $builtGuard
    Require ($guardHash -ceq $baseline.isolation_guard_dll_sha256) "Harness isolation guard deterministic DLL hash drifted; expected $($baseline.isolation_guard_dll_sha256), found $guardHash."
    Require ((Get-FileSha256Hex -Path $isolatedGuard) -ceq $guardHash) 'Harness isolation guard copy hash mismatch.'

    $pluginFiles = @()
    $pluginIdentities = @()
    if ($mode -ceq 'loader-only') {
        Require ($ExpectedPluginCount -eq 0) '-ExpectedPluginCount must be zero in loader-only mode.'
        $loaderOnlyEntries = @(Get-ChildItem -LiteralPath $isolatedPlugins -Force)
        Require ($loaderOnlyEntries.Count -eq 1 -and $loaderOnlyEntries[0].FullName -ceq $isolatedGuard) 'Loader-only mode must contain only the single harness isolation guard.'
    }
    else {
        $sourceEntries = @(Get-ChildItem -LiteralPath $pluginSourceRoot -Force)
        Require ($sourceEntries.Count -gt 0) "Explicit plugin directory is empty: $pluginSourceRoot"
        foreach ($entry in $sourceEntries) {
            Require (-not $entry.PSIsContainer -and $entry.Extension -ieq '.dll') "Plugin directory must be a flat DLL-only payload; unexpected entry: $($entry.FullName)"
            Require (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Plugin source DLL may not be a reparse point: $($entry.FullName)"
            $identities = @(Get-BepInPluginIdentities -Path $entry.FullName)
            Require ($identities.Count -gt 0) "DLL contains no BepInPlugin identity and is not an explicit plugin payload: $($entry.FullName)"
            $destination = Join-Path $isolatedPlugins $entry.Name
            Copy-Item -LiteralPath $entry.FullName -Destination $destination
            Require ((((Get-Item -LiteralPath $destination -Force).Attributes) -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Staged plugin DLL may not be a reparse point: $destination"
            $sourceHash = Get-FileSha256Hex -Path $entry.FullName
            Require ((Get-FileSha256Hex -Path $destination) -ceq $sourceHash) "Plugin copy hash mismatch: $($entry.Name)"
            $pluginFiles += [pscustomobject][ordered]@{
                file = $entry.Name
                bytes = [int64]$entry.Length
                sha256 = $sourceHash
                source_path = $entry.FullName
                isolated_path = $destination
            }
            $pluginIdentities += $identities
        }
        $duplicateGuids = @($pluginIdentities | Group-Object guid | Where-Object { $_.Count -gt 1 })
        Require ($duplicateGuids.Count -eq 0) 'Explicit plugin payload contains duplicate BepInPlugin GUIDs.'
        Require (@($pluginIdentities | Where-Object { $_.guid -ceq $baseline.isolation_guard_guid }).Count -eq 0) 'Explicit plugin payload collides with the harness isolation-guard GUID.'
        Require ($pluginFiles.Count -eq $ExpectedPluginCount) "Explicit plugin payload has $($pluginFiles.Count) DLLs; expected $ExpectedPluginCount."
        Require ($pluginIdentities.Count -eq $ExpectedPluginCount) "Explicit plugin payload has $($pluginIdentities.Count) BepInPlugin identities; expected $ExpectedPluginCount."
        $pluginSourceCatalog = @(Get-FileCatalog -Root $pluginSourceRoot)
        $pluginSourceCatalogSha256 = Get-FileCatalogSha256 -Catalog $pluginSourceCatalog
        Require ($pluginSourceCatalogSha256 -ceq $ExpectedPluginCatalogSha256.ToUpperInvariant()) "Explicit plugin payload catalog drifted; expected $($ExpectedPluginCatalogSha256.ToUpperInvariant()), found $pluginSourceCatalogSha256."
    }

    $isolatedPluginEntries = @(Get-ChildItem -LiteralPath $isolatedPlugins -Force)
    Require ($isolatedPluginEntries.Count -eq ($pluginFiles.Count + 1)) 'Isolated plugin directory differs from the explicit source payload plus the single harness guard.'

    $stdoutLog = Join-Path $runRoot 'server-stdout.log'
    $stderrLog = Join-Path $runRoot 'server-stderr.log'
    $unityLog = Join-Path $runRoot 'unity-player.log'
    $bepInExLog = Join-Path $bepRoot 'LogOutput.log'
    $logDefinitions = @(
        [pscustomobject]@{ label = 'BepInEx'; path = $bepInExLog }
        [pscustomobject]@{ label = 'stdout'; path = $stdoutLog }
        [pscustomobject]@{ label = 'stderr'; path = $stderrLog }
        [pscustomobject]@{ label = 'Unity'; path = $unityLog }
    )

    $worldName = 'RunicValheim10Smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $isolatedServerExecutable = Join-Path $serverStage 'valheim_server.exe'
    Assert-Hash -Path $isolatedServerExecutable -Expected $baseline.server_executable_sha256 -Label 'isolated server executable' | Out-Null
    $targetAssembly = Join-Path $coreRoot 'BepInEx.Preloader.dll'
    $arguments = @(
        '--doorstop-enabled', 'true',
        '--doorstop-target-assembly', (Quote-ProcessArgument -Value $targetAssembly),
        '--doorstop-mono-dll-search-path-override', (Quote-ProcessArgument -Value $coreRoot),
        '-nographics',
        '-batchmode',
        '-name', (Quote-ProcessArgument -Value 'Runic Valheim 1.0 Isolated Smoke'),
        '-port', $Port.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-world', (Quote-ProcessArgument -Value $worldName),
        '-password', (Quote-ProcessArgument -Value 'RunicSmokeOnly'),
        '-savedir', (Quote-ProcessArgument -Value $isolatedSavedir),
        '-backups', '0',
        '-public', '0',
        '-logFile', (Quote-ProcessArgument -Value $unityLog)
    )

    # Seal a second snapshot immediately before launch. This catches any staging or
    # guard-build escape before Valheim is allowed to execute.
    $protectedPreLaunch = @(Get-ProtectedPathSnapshots -Definitions $protectedDefinitions)
    $preLaunchIsolationChecks = @(Compare-ProtectedPathSnapshots -Before $protectedBefore -After $protectedPreLaunch)
    $liveRegistryPreLaunch = Get-RegistryTreeFingerprint -CurrentUserSubKey $liveValheimRegistrySubKey
    $pluginSourcePreLaunch = if ($mode -ceq 'plugins') {
        Get-DirectoryFingerprint -Path $pluginSourceRoot -HashFileContents $true
    }
    else { $null }
    $isolationGuardSourcePreLaunch = Get-DirectoryFingerprint -Path $isolationGuardSourceRoot -HashFileContents $true
    $dedicatedSourceContractPreLaunch = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $serverManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
    $dedicatedStagedContractPreLaunch = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $stagedServerManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
    $protectedPreLaunchEvidence = Write-ImmutableJsonEvidence -Directory $runRoot -Name 'PROTECTED-PRELAUNCH.json' -Value ([ordered]@{
            schema = 'runic-valheim10-protected-snapshot/v1'
            phase = 'prelaunch'
            captured_utc = [DateTime]::UtcNow.ToString('O')
            protected_paths = $protectedPreLaunch
            comparison_to_before = $preLaunchIsolationChecks
            live_valheim_registry = $liveRegistryPreLaunch
            live_valheim_registry_unchanged = ($liveRegistryBefore.fingerprint_sha256 -ceq $liveRegistryPreLaunch.fingerprint_sha256)
            plugin_source = $pluginSourcePreLaunch
            isolation_guard_source = $isolationGuardSourcePreLaunch
            dedicated_save_isolation_source_contract = $dedicatedSourceContractPreLaunch
            dedicated_save_isolation_staged_contract = $dedicatedStagedContractPreLaunch
        })
    foreach ($check in $preLaunchIsolationChecks) {
        Require $check.unchanged "Protected live path changed during staging: $($check.path)"
    }
    Require ($liveRegistryBefore.fingerprint_sha256 -ceq $liveRegistryPreLaunch.fingerprint_sha256) "Live Valheim PlayerPrefs registry changed during staging: $($liveRegistryBefore.path)"
    if ($mode -ceq 'plugins') {
        Require ($pluginSourceBefore.fingerprint_sha256 -ceq $pluginSourcePreLaunch.fingerprint_sha256) "Explicit plugin source directory changed during staging: $pluginSourceRoot"
    }
    Require ($isolationGuardSourceBefore.fingerprint_sha256 -ceq $isolationGuardSourcePreLaunch.fingerprint_sha256) "Harness isolation guard source changed during staging: $isolationGuardSourceRoot"
    Assert-ExclusiveSmokeHostState

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $chainloaderSeconds = -1.0
    $registeringLobbySeconds = -1.0
    $openedServerSeconds = -1.0
    $readyLog = ''
    $dwellElapsedSeconds = -1.0
    $peakWorkingSetBytes = [int64]0
    $lastSteamProcessCheckSeconds = -1.0
    $isolationGuardGateSeconds = -1.0
    $startupGuardRuntimeEvidence = $null
    $processStartedUtc = [DateTime]::UtcNow.ToString('O')
    try {
        $process = Invoke-WithIsolatedLaunchEnvironment -SteamAppId $baseline.server_runtime_app_id -TargetAssembly $targetAssembly -CoreSearchPath $coreRoot -SaveRoot $isolatedSavedir -LiveValheimDataRoot $defaultValheimData -ProcessMode 'dedicated-server' -ExpectedManagedRoot $stagedServerManagedRoot -ProcessProfileRoot $processProfileRoot -Evidence $launchEnvironmentEvidence -Launch {
            Start-Process -FilePath $isolatedServerExecutable `
                -ArgumentList $arguments `
                -WorkingDirectory $serverStage `
                -RedirectStandardOutput $stdoutLog `
                -RedirectStandardError $stderrLog `
                -WindowStyle Hidden `
                -PassThru
        }
        $processId = $process.Id
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $ready = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) { throw "The isolated server exited before readiness (exit code $($process.ExitCode))." }
            if (($timer.Elapsed.TotalSeconds - $lastSteamProcessCheckSeconds) -ge 1.0) {
                Assert-NoSteamDesktopProcesses
                $lastSteamProcessCheckSeconds = $timer.Elapsed.TotalSeconds
            }
            try {
                if ($process.PeakWorkingSet64 -gt $peakWorkingSetBytes) { $peakWorkingSetBytes = $process.PeakWorkingSet64 }
            }
            catch { }
            $snapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
            $completedSnapshots = @(Get-CompletedLogSnapshots -Snapshots $snapshots)
            Assert-HealthyLogs -Snapshots $completedSnapshots -Phase 'startup monitoring'
            $bepSnapshot = $completedSnapshots | Where-Object { $_.label -ceq 'BepInEx' } | Select-Object -First 1
            if ($null -ne $bepSnapshot -and $chainloaderSeconds -lt 0 -and
                $bepSnapshot.text.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $startupGuardRuntimeEvidence = Assert-IsolationGuardRuntime -Text $bepSnapshot.text -IsolatedSaveRoot $isolatedSavedir -ExpectedManagedRoot $stagedServerManagedRoot -PayloadIdentities $pluginIdentities
                $isolationGuardGateSeconds = $timer.Elapsed.TotalSeconds
                $chainloaderSeconds = $timer.Elapsed.TotalSeconds
            }
            foreach ($snapshot in $completedSnapshots) {
                $registerIndex = $snapshot.text.IndexOf('Registering lobby', [StringComparison]::OrdinalIgnoreCase)
                $openedIndex = $snapshot.text.IndexOf('Opened Steam server', [StringComparison]::OrdinalIgnoreCase)
                if ($registerIndex -ge 0 -and $registeringLobbySeconds -lt 0) { $registeringLobbySeconds = $timer.Elapsed.TotalSeconds }
                if ($registerIndex -ge 0 -and $openedIndex -gt $registerIndex) {
                    if ($openedServerSeconds -lt 0) {
                        $openedServerSeconds = $timer.Elapsed.TotalSeconds
                        $readyLog = $snapshot.label
                    }
                    if ($chainloaderSeconds -ge 0) { $ready = $true }
                }
            }
            if ($ready) {
                Assert-HealthyLogs -Snapshots $completedSnapshots -Phase 'readiness gate'
                break
            }
            Start-Sleep -Milliseconds 250
        }
        Require $ready "The isolated server did not reach ordered 'Registering lobby' -> 'Opened Steam server' readiness within $TimeoutSeconds seconds."

        $dwellStart = $timer.Elapsed.TotalSeconds
        while (($timer.Elapsed.TotalSeconds - $dwellStart) -lt $DwellSeconds) {
            $process.Refresh()
            if ($process.HasExited) { throw "The isolated server exited during the stability dwell (exit code $($process.ExitCode))." }
            if (($timer.Elapsed.TotalSeconds - $lastSteamProcessCheckSeconds) -ge 1.0) {
                Assert-NoSteamDesktopProcesses
                $lastSteamProcessCheckSeconds = $timer.Elapsed.TotalSeconds
            }
            try {
                if ($process.PeakWorkingSet64 -gt $peakWorkingSetBytes) { $peakWorkingSetBytes = $process.PeakWorkingSet64 }
            }
            catch { }
            Start-Sleep -Milliseconds 250
        }
        $dwellElapsedSeconds = $timer.Elapsed.TotalSeconds - $dwellStart
        $postDwellSnapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
        $completedPostDwellSnapshots = @(Get-CompletedLogSnapshots -Snapshots $postDwellSnapshots)
        Assert-HealthyLogs -Snapshots $completedPostDwellSnapshots -Phase 'post-dwell gate'
        Require (-not $process.HasExited) 'The isolated server was not alive after the stability dwell.'
    }
    finally {
        if ($null -ne $process) {
            $processWasStoppedByHarness = Stop-TrackedProcess -Process $process
        }
    }
    Require ($null -ne $processId -and $processWasStoppedByHarness) 'The harness did not stop its exact tracked server process after the completed dwell.'
    $process.Refresh()
    Require $process.HasExited 'The exact tracked server process remained alive after cleanup.'
    Assert-ExclusiveSmokeHostState
    # Seal post-run state before interpreting logs. Even a later verification
    # failure therefore retains the exact live-path state observed after the
    # tracked process stopped.
    $protectedAfter = @(Get-ProtectedPathSnapshots -Definitions $protectedDefinitions)
    $isolationChecks = @(Compare-ProtectedPathSnapshots -Before $protectedBefore -After $protectedAfter)
    $liveRegistryAfter = Get-RegistryTreeFingerprint -CurrentUserSubKey $liveValheimRegistrySubKey
    $liveRegistryRuntimeComparison = Compare-PlayerPrefsRegistrySnapshots `
        -Before $liveRegistryBefore `
        -After $liveRegistryAfter `
        -AllowedVolatileValues $allowedVolatilePlayerPrefsValues `
        -ExpectedStableValueCount $expectedStablePlayerPrefsValueCount
    $pluginSourceAfter = if ($mode -ceq 'plugins') {
        Get-DirectoryFingerprint -Path $pluginSourceRoot -HashFileContents $true
    }
    else { $null }
    $isolationGuardSourceAfter = Get-DirectoryFingerprint -Path $isolationGuardSourceRoot -HashFileContents $true
    $dedicatedSourceContractAfter = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $serverManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
    $dedicatedStagedContractAfter = Get-DedicatedSaveIsolationContract `
        -ManagedRoot $stagedServerManagedRoot `
        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
    $protectedAfterEvidence = Write-ImmutableJsonEvidence -Directory $runRoot -Name 'PROTECTED-AFTER.json' -Value ([ordered]@{
            schema = 'runic-valheim10-protected-snapshot/v1'
            phase = 'after-process-stop'
            captured_utc = [DateTime]::UtcNow.ToString('O')
            protected_paths = $protectedAfter
            comparison_to_before = $isolationChecks
            live_valheim_registry = $liveRegistryAfter
            live_valheim_registry_before_sha256 = $liveRegistryBefore.fingerprint_sha256
            live_valheim_registry_full_fingerprint_unchanged = ($liveRegistryBefore.fingerprint_sha256 -ceq $liveRegistryAfter.fingerprint_sha256)
            live_valheim_registry_runtime_comparison = $liveRegistryRuntimeComparison
            plugin_source = $pluginSourceAfter
            isolation_guard_source = $isolationGuardSourceAfter
            dedicated_save_isolation_source_contract = $dedicatedSourceContractAfter
            dedicated_save_isolation_staged_contract = $dedicatedStagedContractAfter
            process_id = $processId
            stopped_only_tracked_process = $processWasStoppedByHarness
            runtime_directories_were_disposable_copies = $true
        })
    foreach ($check in $isolationChecks) {
        Require $check.unchanged "Protected live path changed during isolated smoke: $($check.path)"
    }
    Require ([bool]$liveRegistryRuntimeComparison.passed) ('Live Valheim PlayerPrefs registry changed outside the exact four permitted Unity session values: ' + [string]::Join(' ', @($liveRegistryRuntimeComparison.issues)))
    if ($mode -ceq 'plugins') {
        Require ($pluginSourceBefore.fingerprint_sha256 -ceq $pluginSourceAfter.fingerprint_sha256) "Explicit plugin source directory changed during smoke: $pluginSourceRoot"
    }
    Require ($isolationGuardSourceBefore.fingerprint_sha256 -ceq $isolationGuardSourceAfter.fingerprint_sha256) "Harness isolation guard source changed during smoke: $isolationGuardSourceRoot"

    # Prove the bytes that actually executed remained identical to the verified
    # staged inputs for the full lifetime of the process.
    foreach ($runtimeFile in $runtimeFileCopies) {
        Require (Test-Path -LiteralPath $runtimeFile.isolated_path -PathType Leaf) "Executed staged runtime file disappeared: $($runtimeFile.isolated_path)"
        $afterHash = Get-FileSha256Hex -Path $runtimeFile.isolated_path
        Require ($afterHash -ceq $runtimeFile.sha256) "Executed staged runtime file changed during smoke: $($runtimeFile.name)"
        $stagedRuntimeFileChecks += [pscustomobject][ordered]@{
            name = $runtimeFile.name
            path = $runtimeFile.isolated_path
            before_sha256 = $runtimeFile.sha256
            after_sha256 = $afterHash
            unchanged = $true
        }
    }
    foreach ($runtimeDirectory in $runtimeDirectoryCopies) {
        $afterCatalog = @(Get-FileCatalog -Root $runtimeDirectory.isolated_path)
        $afterCatalogHash = Get-FileCatalogSha256 -Catalog $afterCatalog
        Require ($afterCatalog.Count -eq $runtimeDirectory.file_count -and $afterCatalogHash -ceq $runtimeDirectory.catalog_sha256) "Executed staged runtime directory changed during smoke: $($runtimeDirectory.name)"
        $stagedRuntimeDirectoryChecks += [pscustomobject][ordered]@{
            name = $runtimeDirectory.name
            path = $runtimeDirectory.isolated_path
            file_count = $afterCatalog.Count
            before_catalog_sha256 = $runtimeDirectory.catalog_sha256
            after_catalog_sha256 = $afterCatalogHash
            unchanged = $true
        }
    }
    $coreCatalogAfter = @(Get-FileCatalog -Root $coreRoot)
    $coreCatalogAfterSha256 = Get-FileCatalogSha256 -Catalog $coreCatalogAfter
    Require ($coreCatalogAfter.Count -eq $coreCatalog.Count -and $coreCatalogAfterSha256 -ceq $coreCatalogSha256) 'Executed staged BepInEx core changed during smoke.'
    Require ((Get-FileSha256Hex -Path $builtGuard) -ceq $guardHash) 'Built isolation-guard DLL changed during smoke.'
    Require ((Get-FileSha256Hex -Path $isolatedGuard) -ceq $guardHash) 'Executed isolation-guard DLL changed during smoke.'
    foreach ($pluginFile in $pluginFiles) {
        $afterHash = Get-FileSha256Hex -Path $pluginFile.isolated_path
        Require ($afterHash -ceq $pluginFile.sha256) "Executed staged plugin changed during smoke: $($pluginFile.file)"
        $stagedPluginChecks += [pscustomobject][ordered]@{
            file = $pluginFile.file
            path = $pluginFile.isolated_path
            before_sha256 = $pluginFile.sha256
            after_sha256 = $afterHash
            unchanged = $true
        }
    }
    Assert-Hash -Path $isolatedWinHttpProxy -Expected $baseline.doorstop_proxy_sha256 -Label 'post-run isolated Doorstop WINHTTP proxy' | Out-Null
    Assert-Hash -Path $isolatedDoorstopConfig -Expected $baseline.doorstop_config_sha256 -Label 'post-run isolated Doorstop configuration' | Out-Null
    Assert-Hash -Path $isolatedDoorstopVersion -Expected $baseline.doorstop_version_sha256 -Label 'post-run isolated Doorstop version marker' | Out-Null

    $finalSnapshots = @(Get-LogSnapshots -Definitions $logDefinitions)
    $doorstopLogFiles = @(Get-ChildItem -LiteralPath $serverStage -Filter 'doorstop_*.log' -File)
    foreach ($doorstopLogFile in $doorstopLogFiles) {
        $finalSnapshots += @(Get-LogSnapshots -Definitions @(
                [pscustomobject]@{ label = ('Doorstop-' + $doorstopLogFile.BaseName); path = $doorstopLogFile.FullName }
            ))
    }
    foreach ($snapshot in $finalSnapshots) {
        Require $snapshot.exists "Required isolated log was not created: $($snapshot.path)"
    }
    Assert-HealthyLogs -Snapshots $finalSnapshots -Phase 'final stopped-process rescan'
    $bepText = [string](($finalSnapshots | Where-Object { $_.label -ceq 'BepInEx' } | Select-Object -First 1).text)
    Require ($bepText.IndexOf('BepInEx ' + $baseline.bepinex_file_version + ' - valheim', [StringComparison]::OrdinalIgnoreCase) -ge 0) "Exact BepInEx $($baseline.bepinex_file_version) runtime marker was not observed."
    Require ($bepText.IndexOf('Chainloader startup complete', [StringComparison]::OrdinalIgnoreCase) -ge 0) 'BepInEx chainloader completion was not retained in the final log.'

    $finalGuardRuntimeEvidence = Assert-IsolationGuardRuntime -Text $bepText -IsolatedSaveRoot $isolatedSavedir -ExpectedManagedRoot $stagedServerManagedRoot -PayloadIdentities $pluginIdentities
    Require ($null -ne $startupGuardRuntimeEvidence) 'The isolation guard was not verified at the startup chainloader gate.'
    Require ($startupGuardRuntimeEvidence.ready_marker -ceq $finalGuardRuntimeEvidence.ready_marker -and
        $startupGuardRuntimeEvidence.total_load_count -eq $finalGuardRuntimeEvidence.total_load_count) 'Final isolation-guard evidence differs from the startup gate.'
    $guardMarker = $finalGuardRuntimeEvidence.ready_marker

    $allLogText = [string]::Join("`n", @($finalSnapshots | ForEach-Object { $_.text }))
    Require ($allLogText.IndexOf('Using steam APPID:' + $baseline.server_runtime_app_id, [StringComparison]::OrdinalIgnoreCase) -ge 0) "Exact runtime Steam AppID $($baseline.server_runtime_app_id) was not observed."
    $savedirMarker = 'Setting -savedir to: ' + $isolatedSavedir
    Require ($allLogText.IndexOf($savedirMarker, [StringComparison]::OrdinalIgnoreCase) -ge 0) 'Valheim did not acknowledge the exact isolated -savedir path.'
    $forbiddenLivePaths = @($serverRootPath, $clientRootPath, $defaultValheimData, $steamUserdataRoot)
    foreach ($steamUserRoot in $steamUserRoots) {
        $forbiddenLivePaths += Join-Path $steamUserRoot.FullName $baseline.client_app_id
    }
    foreach ($forbiddenLivePath in @($forbiddenLivePaths | Select-Object -Unique)) {
        Require ($allLogText.IndexOf($forbiddenLivePath, [StringComparison]::OrdinalIgnoreCase) -lt 0) "A protected live path appeared in isolated runtime logs: $forbiddenLivePath"
    }
    $runtimeMarkers = @([regex]::Matches($allLogText, '(?im)^.*Valheim version:\s*[^\r\n]+$') | ForEach-Object { $_.Value.Trim() } | Select-Object -Unique)
    Require ($runtimeMarkers.Count -gt 0) 'No Valheim runtime version marker was observed.'
    $exactRuntimeMatches = [regex]::Matches(
        $allLogText,
        '(?im)Valheim version:\s*(?<version>\d+\.\d+\.\d+)\s+\(network version\s+(?<network>\d+)\)')
    Require ($exactRuntimeMatches.Count -gt 0) 'No exact Valheim runtime/network marker was observed.'
    $observedRuntimeVersions = @($exactRuntimeMatches | ForEach-Object { $_.Groups['version'].Value } | Select-Object -Unique)
    $observedNetworkVersions = @($exactRuntimeMatches | ForEach-Object { $_.Groups['network'].Value } | Select-Object -Unique)
    Require ($observedRuntimeVersions.Count -eq 1 -and $observedRuntimeVersions[0] -ceq $baseline.valheim_runtime_version) "Valheim runtime version drifted; expected $($baseline.valheim_runtime_version), found $([string]::Join(',', $observedRuntimeVersions))."
    Require ($observedNetworkVersions.Count -eq 1 -and $observedNetworkVersions[0] -ceq $baseline.valheim_network_version) "Valheim network version drifted; expected $($baseline.valheim_network_version), found $([string]::Join(',', $observedNetworkVersions))."
    Require ($chainloaderSeconds -ge 0 -and $registeringLobbySeconds -ge 0 -and $openedServerSeconds -ge 0) 'Final readiness timing evidence is incomplete.'
    Require ($dwellElapsedSeconds -ge $DwellSeconds) 'The monotonic stability dwell completed too early.'
    $isolatedSteamConfig = Join-Path $serverStage 'config\config.vdf'
    $isolatedSteamLog = Join-Path $serverStage ('logs\connection_log_' + $Port + '.txt')
    Require (Test-Path -LiteralPath $isolatedSteamConfig -PathType Leaf) "Steam native config was not contained in the evidence root: $isolatedSteamConfig"
    Require (Test-Path -LiteralPath $isolatedSteamLog -PathType Leaf) "Steam connection log was not contained in the evidence root: $isolatedSteamLog"

    Assert-Hash -Path $serverExecutable -Expected $baseline.server_executable_sha256 -Label 'post-run server executable' | Out-Null
    Assert-Hash -Path $serverAssembly -Expected $baseline.server_assembly_sha256 -Label 'post-run server assembly' | Out-Null
    Assert-Hash -Path $serverAssemblyUtils -Expected $baseline.server_assembly_utils_sha256 -Label 'post-run server assembly_utils' | Out-Null
    Assert-Hash -Path $serverSplatform -Expected $baseline.server_splatform_sha256 -Label 'post-run server Splatform' | Out-Null
    Require (-not (Test-Path -LiteralPath $serverSplatformSteam)) 'Installed dedicated-server Splatform.Steam.dll appeared during smoke.'
    Assert-Hash -Path $serverUnityPlayer -Expected $baseline.server_unityplayer_sha256 -Label 'post-run server UnityPlayer' | Out-Null
    Assert-Hash -Path $serverSteamAppIdFile -Expected $baseline.server_steam_appid_file_sha256 -Label 'post-run server steam_appid file' | Out-Null
    $serverStartScriptAfterText = [System.IO.File]::ReadAllText($serverStartScript)
    Require ([regex]::IsMatch($serverStartScriptAfterText, '(?im)^\s*set\s+SteamAppId\s*=\s*' + [regex]::Escape($baseline.server_runtime_app_id) + '\s*$')) 'The dedicated-server runtime AppID source changed during smoke.'
    Assert-Hash -Path $clientExecutable -Expected $baseline.client_executable_sha256 -Label 'post-run client executable' | Out-Null
    Assert-Hash -Path $clientAssembly -Expected $baseline.client_assembly_sha256 -Label 'post-run client assembly' | Out-Null
    Assert-Hash -Path $clientUnityPlayer -Expected $baseline.client_unityplayer_sha256 -Label 'post-run client UnityPlayer' | Out-Null
    Assert-Hash -Path $archivePath -Expected $baseline.bepinex_archive_sha256 -Label 'post-run BepInEx archive' | Out-Null
    Require ((Get-FileSha256Hex -Path $scriptPath) -ceq $scriptHashAtStart) 'The smoke harness changed while it was running.'

    $logEvidence = @()
    foreach ($snapshot in $finalSnapshots) {
        $info = Get-Item -LiteralPath $snapshot.path
        $logEvidence += [pscustomobject][ordered]@{
            label = $snapshot.label
            path = $snapshot.path
            bytes = [int64]$info.Length
            sha256 = Get-FileSha256Hex -Path $snapshot.path
        }
    }
    $completedUtc = [DateTime]::UtcNow.ToString('O')
    $evidence = [ordered]@{
        schema = 'runic-valheim10-dedicated-smoke/v1'
        status = 'GO'
        run_id = $runId
        mode = $mode
        started_utc = $processStartedUtc
        completed_utc = $completedUtc
        baseline = $baseline
        harness = [ordered]@{
            path = $scriptPath
            sha256 = $scriptHashAtStart
        }
        steam = [ordered]@{
            server = $serverSteam
            client = $clientSteam
            server_runtime_app_id = $baseline.server_runtime_app_id
            server_runtime_app_id_source = $serverStartScript
            server_steam_appid_file = [ordered]@{ path = $serverSteamAppIdFile; sha256 = $baseline.server_steam_appid_file_sha256 }
        }
        binaries = [ordered]@{
            server_executable = [ordered]@{ path = $serverExecutable; sha256 = $baseline.server_executable_sha256 }
            server_assembly = [ordered]@{ path = $serverAssembly; sha256 = $baseline.server_assembly_sha256 }
            server_assembly_utils = [ordered]@{ path = $serverAssemblyUtils; sha256 = $baseline.server_assembly_utils_sha256 }
            server_splatform = [ordered]@{ path = $serverSplatform; sha256 = $baseline.server_splatform_sha256 }
            server_unityplayer = [ordered]@{ path = $serverUnityPlayer; sha256 = $baseline.server_unityplayer_sha256 }
            client_executable = [ordered]@{ path = $clientExecutable; sha256 = $baseline.client_executable_sha256 }
            client_assembly = [ordered]@{ path = $clientAssembly; sha256 = $baseline.client_assembly_sha256 }
            client_unityplayer = [ordered]@{ path = $clientUnityPlayer; sha256 = $baseline.client_unityplayer_sha256 }
            staged_runtime_root_files = $runtimeFileCopies
            staged_runtime_root_file_after_checks = $stagedRuntimeFileChecks
        }
        loader = [ordered]@{
            archive_path = $archivePath
            archive_sha256 = $baseline.bepinex_archive_sha256
            extracted_pack_root = $packRoot
            isolated_server_root = $serverStage
            bepinex_file_version = $observedBepInExVersion
            core_catalog_sha256 = $coreCatalogSha256
            core_catalog_after_sha256 = $coreCatalogAfterSha256
            core_unchanged_during_smoke = $true
            core_files = $coreCatalog
            doorstop_proxy_source_sha256 = Get-FileSha256Hex -Path $packWinHttp
            doorstop_runtime_proxy_path = $isolatedWinHttpProxy
            doorstop_runtime_proxy_sha256 = Get-FileSha256Hex -Path $isolatedWinHttpProxy
            doorstop_version_path = $isolatedDoorstopVersion
            doorstop_version_sha256 = Get-FileSha256Hex -Path $isolatedDoorstopVersion
            bootstrap_proxy = 'winhttp.dll (exact BepInExPack 5.4.2350 release bytes)'
            doorstop_target_assembly = $targetAssembly
            override_mode = 'windows-command-line'
            launch_environment = $launchEnvironmentEvidence
        }
        plugins = [ordered]@{
            source_directory = $pluginSourceRoot
            expected_count = $ExpectedPluginCount
            expected_catalog_sha256 = $ExpectedPluginCatalogSha256.ToUpperInvariant()
            observed_catalog_sha256 = $pluginSourceCatalogSha256
            dll_count = $pluginFiles.Count
            identity_count = $pluginIdentities.Count
            files = $pluginFiles
            identities = $pluginIdentities
            staged_after_checks = $stagedPluginChecks
            runtime_payload_load_count = $pluginIdentities.Count
            runtime_total_load_count_including_guard = $finalGuardRuntimeEvidence.total_load_count
        }
        harness_instrumentation = [ordered]@{
            isolation_guard_project = $isolationGuardProject
            isolation_guard_source_root = $isolationGuardSourceRoot
            source_before = $isolationGuardSourceBefore
            source_after = $isolationGuardSourceAfter
            build_log = [ordered]@{
                path = $guardBuildLog
                sha256 = Get-FileSha256Hex -Path $guardBuildLog
            }
            built_dll = [ordered]@{
                path = $builtGuard
                sha256 = $guardHash
            }
            staged_dll = [ordered]@{
                path = $isolatedGuard
                sha256 = Get-FileSha256Hex -Path $isolatedGuard
            }
            identity = $guardIdentity
            runtime_load_count = $finalGuardRuntimeEvidence.guard_load_count
            ready_marker = $guardMarker
            ready_marker_count = $finalGuardRuntimeEvidence.ready_marker_count
            ready_before_every_payload_load = $true
            startup_gate_seconds = $isolationGuardGateSeconds
            startup_gate = $startupGuardRuntimeEvidence
            final_rescan = $finalGuardRuntimeEvidence
            early_platform_precondition = 'guard verified PlatformManager.DistributionPlatform was null before installing save isolation'
            save_override = 'Utils.SetSaveDataPath plus exact static m_saveDataOverride verification'
            session_flag = 'SaveSystemSessionFlags.DontSaveAnything set and verified'
            process_mode = 'dedicated-server'
            dedicated_save_isolation = [ordered]@{
                source_before = $dedicatedSourceContractBefore
                staged_before = $dedicatedStagedContractBefore
                source_prelaunch = $dedicatedSourceContractPreLaunch
                staged_prelaunch = $dedicatedStagedContractPreLaunch
                source_after = $dedicatedSourceContractAfter
                staged_after = $dedicatedStagedContractAfter
                runtime_contract = 'Splatform.Steam assembly/type absent and FileHelpers.CloudStorageSupported false'
            }
            steam_save_provider = 'Unavailable by the exact dedicated-server contract; no graphical Steam save-provider implementation is present'
        }
        runtime = [ordered]@{
            process_id = $processId
            stop_scope = 'exact Process object returned by Start-Process'
            stopped_by_harness = $processWasStoppedByHarness
            chainloader_seconds = $chainloaderSeconds
            registering_lobby_seconds = $registeringLobbySeconds
            opened_steam_server_seconds = $openedServerSeconds
            readiness_sequence = @('Registering lobby', 'Opened Steam server')
            readiness_log = $readyLog
            dwell_requested_seconds = $DwellSeconds
            dwell_elapsed_seconds = $dwellElapsedSeconds
            peak_working_set_bytes = $peakWorkingSetBytes
            valheim_runtime_markers = $runtimeMarkers
            observed_runtime_versions = $observedRuntimeVersions
            observed_network_versions = $observedNetworkVersions
        }
        isolation = [ordered]@{
            isolated_savedir = $isolatedSavedir
            savedir_acknowledgement = $savedirMarker
            isolated_bepinex_root = $bepRoot
            isolated_process_profile = $processProfileRoot
            live_paths_unchanged = $true
            protected_path_checks = $isolationChecks
            protected_before_evidence = $protectedBeforeEvidence
            protected_prelaunch_evidence = $protectedPreLaunchEvidence
            protected_after_evidence = $protectedAfterEvidence
            live_valheim_registry_before = $liveRegistryBefore
            live_valheim_registry_prelaunch = $liveRegistryPreLaunch
            live_valheim_registry_after = $liveRegistryAfter
            live_valheim_registry_full_fingerprint_unchanged = [bool]$liveRegistryRuntimeComparison.full_fingerprint_unchanged
            live_valheim_registry_stable_catalog_unchanged = $true
            live_valheim_registry_runtime_comparison = $liveRegistryRuntimeComparison
            explicit_plugin_source_before = $pluginSourceBefore
            explicit_plugin_source_after = $pluginSourceAfter
            steam_desktop_required_closed = $true
            steam_desktop_checked_before_staging_before_launch_and_during_runtime = $true
            forbidden_live_paths_absent_from_logs = @($forbiddenLivePaths | Select-Object -Unique)
            live_server_install_used_as_verified_copy_source_only = $serverRootPath
            process_working_directory = $serverStage
            runtime_directory_copies = $runtimeDirectoryCopies
            runtime_directory_after_checks = $stagedRuntimeDirectoryChecks
            runtime_junctions_created = $false
            isolated_steam_config = $isolatedSteamConfig
            isolated_steam_connection_log = $isolatedSteamLog
            public_server = $false
            test_port = $Port
            disposable_world = $worldName
        }
        log_scan = [ordered]@{
            rules = @($scanRules | ForEach-Object { $_.id })
            baseline_noise_allowlist = $baselineNoiseAllowlist
            failure_count = 0
            readiness_scan_passed = $true
            post_dwell_scan_passed = $true
            stopped_process_rescan_passed = $true
        }
        logs = $logEvidence
    }

    $pendingJson = Join-Path $runRoot '.RESULT.json.pending'
    $resultJson = Join-Path $runRoot 'RESULT.json'
    [System.IO.File]::WriteAllText($pendingJson, ($evidence | ConvertTo-Json -Depth 12), [System.Text.UTF8Encoding]::new($false))
    $resultHash = Get-FileSha256Hex -Path $pendingJson
    Move-Item -LiteralPath $pendingJson -Destination $resultJson
    $hashPath = Join-Path $runRoot 'RESULT.sha256'
    [System.IO.File]::WriteAllText($hashPath, ($resultHash + '  RESULT.json' + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
    Write-Output ('RUNIC_VALHEIM10_DEDICATED_SMOKE_GO mode=' + $mode +
        ' plugins=' + $pluginIdentities.Count +
        ' server_build=' + $serverSteam.build_id +
        ' client_build=' + $clientSteam.build_id +
        ' evidence="' + $resultJson + '" sha256=' + $resultHash)
}
catch {
    $failureMessage = $_.Exception.Message
    try {
        if ($null -ne $process -and -not $processWasStoppedByHarness) {
            $processWasStoppedByHarness = Stop-TrackedProcess -Process $process
        }
    }
    catch { $failureMessage += ' | Cleanup: ' + $_.Exception.Message }

    $failureProtectedEvidence = $null
    $failureIsolationChecks = @()
    $failureRegistryAfter = $null
    $failureRegistryComparison = $null
    $failureDedicatedSourceContract = $null
    $failureDedicatedStagedContract = $null
    try {
        if ($null -ne ('Mono.Cecil.AssemblyDefinition' -as [type])) {
            if (Test-Path -LiteralPath $serverManagedRoot -PathType Container) {
                $failureDedicatedSourceContract = Get-DedicatedSaveIsolationContract `
                    -ManagedRoot $serverManagedRoot `
                    -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
                    -ExpectedSplatformSha256 $baseline.server_splatform_sha256
            }
            if (-not [string]::IsNullOrWhiteSpace($serverStage)) {
                $failureStagedManagedRoot = Join-Path $serverStage 'valheim_server_Data\Managed'
                if (Test-Path -LiteralPath $failureStagedManagedRoot -PathType Container) {
                    $failureDedicatedStagedContract = Get-DedicatedSaveIsolationContract `
                        -ManagedRoot $failureStagedManagedRoot `
                        -ExpectedAssemblyUtilsSha256 $baseline.server_assembly_utils_sha256 `
                        -ExpectedSplatformSha256 $baseline.server_splatform_sha256
                }
            }
        }
    }
    catch { $failureMessage += ' | Dedicated save-isolation contract: ' + $_.Exception.Message }
    try {
        if ($protectedBefore.Count -gt 0 -and $protectedDefinitions.Count -gt 0) {
            $failureProtectedAfter = @(Get-ProtectedPathSnapshots -Definitions $protectedDefinitions)
            $failureIsolationChecks = @(Compare-ProtectedPathSnapshots -Before $protectedBefore -After $failureProtectedAfter)
            $failureRegistryAfter = Get-RegistryTreeFingerprint -CurrentUserSubKey $liveValheimRegistrySubKey
            if ($null -ne $liveRegistryBefore) {
                $failureRegistryComparison = Compare-PlayerPrefsRegistrySnapshots `
                    -Before $liveRegistryBefore `
                    -After $failureRegistryAfter `
                    -AllowedVolatileValues $allowedVolatilePlayerPrefsValues `
                    -ExpectedStableValueCount $expectedStablePlayerPrefsValueCount
                if (-not [bool]$failureRegistryComparison.passed) {
                    $failureMessage += ' | Live Valheim PlayerPrefs registry changed outside the exact four permitted Unity session values: ' + [string]::Join(' ', @($failureRegistryComparison.issues))
                }
            }
            $failurePluginSourceAfter = if ($mode -ceq 'plugins') {
                Get-DirectoryFingerprint -Path $pluginSourceRoot -HashFileContents $true
            }
            else { $null }
            $failureGuardSourceAfter = Get-DirectoryFingerprint -Path $isolationGuardSourceRoot -HashFileContents $true
            $failureProtectedEvidence = Write-ImmutableJsonEvidence -Directory $runRoot -Name 'PROTECTED-AFTER-FAILURE.json' -Value ([ordered]@{
                    schema = 'runic-valheim10-protected-snapshot/v1'
                    phase = 'after-failure-cleanup'
                    captured_utc = [DateTime]::UtcNow.ToString('O')
                    protected_paths = $failureProtectedAfter
                    comparison_to_before = $failureIsolationChecks
                    live_valheim_registry = $failureRegistryAfter
                    live_valheim_registry_before_sha256 = if ($null -ne $liveRegistryBefore) { $liveRegistryBefore.fingerprint_sha256 } else { '' }
                    live_valheim_registry_full_fingerprint_unchanged = ($null -ne $liveRegistryBefore -and $liveRegistryBefore.fingerprint_sha256 -ceq $failureRegistryAfter.fingerprint_sha256)
                    live_valheim_registry_runtime_comparison = $failureRegistryComparison
                    plugin_source = $failurePluginSourceAfter
                    isolation_guard_source = $failureGuardSourceAfter
                    dedicated_save_isolation_source_contract = $failureDedicatedSourceContract
                    dedicated_save_isolation_staged_contract = $failureDedicatedStagedContract
                    process_id = $processId
                    stopped_only_tracked_process = $processWasStoppedByHarness
                })
        }
    }
    catch { $failureMessage += ' | Failure-state fingerprint: ' + $_.Exception.Message }

    $availableLogEvidence = @()
    try {
        $failureLogDefinitions = @($logDefinitions)
        if (-not [string]::IsNullOrWhiteSpace($serverStage) -and (Test-Path -LiteralPath $serverStage -PathType Container)) {
            foreach ($doorstopLogFile in @(Get-ChildItem -LiteralPath $serverStage -Filter 'doorstop_*.log' -File -ErrorAction SilentlyContinue)) {
                $failureLogDefinitions += [pscustomobject]@{
                    label = 'Doorstop-' + $doorstopLogFile.BaseName
                    path = $doorstopLogFile.FullName
                }
            }
        }
        if ($failureLogDefinitions.Count -gt 0) {
            foreach ($snapshot in @(Get-LogSnapshots -Definitions $failureLogDefinitions)) {
                if (-not $snapshot.exists) { continue }
                $logInfo = Get-Item -LiteralPath $snapshot.path
                $availableLogEvidence += [pscustomobject][ordered]@{
                    label = $snapshot.label
                    path = $snapshot.path
                    bytes = [int64]$logInfo.Length
                    sha256 = Get-FileSha256Hex -Path $snapshot.path
                }
            }
        }
    }
    catch { $failureMessage += ' | Failure-log catalog: ' + $_.Exception.Message }
    try {
        $failure = [ordered]@{
            schema = 'runic-valheim10-dedicated-smoke-failure/v1'
            status = 'NO-GO'
            run_id = $runId
            mode = $mode
            failed_utc = [DateTime]::UtcNow.ToString('O')
            message = $failureMessage
            process_id = $processId
            stopped_only_tracked_process = $processWasStoppedByHarness
            evidence_root = $runRoot
            harness_path = $scriptPath
            harness_sha256 = $scriptHashAtStart
            protected_before_evidence = $protectedBeforeEvidence
            protected_prelaunch_evidence = $protectedPreLaunchEvidence
            protected_after_evidence = $protectedAfterEvidence
            protected_after_failure_evidence = $failureProtectedEvidence
            protected_path_checks_after_failure = $failureIsolationChecks
            live_valheim_registry_before = $liveRegistryBefore
            live_valheim_registry_after_failure = $failureRegistryAfter
            live_valheim_registry_runtime_comparison_after_failure = $failureRegistryComparison
            dedicated_save_isolation_source_contract_after_failure = $failureDedicatedSourceContract
            dedicated_save_isolation_staged_contract_after_failure = $failureDedicatedStagedContract
            logs = $availableLogEvidence
        }
        [void](Write-ImmutableJsonEvidence -Directory $runRoot -Name 'FAILURE.json' -Value $failure -Depth 12)
    }
    catch { }
    throw
}
