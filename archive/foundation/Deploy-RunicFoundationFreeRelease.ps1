[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ClientPluginRoot,
    [Parameter(Mandatory = $true)][string]$ServerPluginRoot,
    [string]$ReleaseRoot = (Join-Path $PSScriptRoot 'artifacts\Thunderstore\1.0.0')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Require {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-ContainedPath {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = [System.IO.Path]::GetFullPath($Root).
        TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Require ($full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) `
        "Path escaped its intended root: $full"
    return $full
}

function Require-ProcessGate {
    $running = @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -match '^(valheim|valheim_server)(\.exe)?$'
        })
    Require ($running.Count -eq 0) `
        'Valheim or the dedicated server is running; deployment was not performed.'
}

$modules = @(
    @{ Name='RunicStorage'; Version='1.0.0' },
    @{ Name='RunicCrafting'; Version='1.0.0' },
    @{ Name='RunicAgriculture'; Version='1.0.0' },
    @{ Name='RunicProduction'; Version='1.0.0' },
    @{ Name='RunicPrecisionBuildTool'; Version='2.0.1' },
    @{ Name='RunicInventory'; Version='1.0.0' },
    @{ Name='RunicPortals'; Version='1.0.0' },
    @{ Name='RunicExploration'; Version='1.0.0' },
    @{ Name='RunicAwareness'; Version='1.0.0' },
    @{ Name='RunicInteraction'; Version='1.0.0' },
    @{ Name='RunicSafety'; Version='1.0.0' },
    @{ Name='RunicVelocity'; Version='1.0.0' },
    @{ Name='RunicSentinel'; Version='1.0.0' },
    @{ Name='RunicWorldEngine'; Version='1.0.0' }
)
$foundationNames = @(
    'RunicCore',
    'RunicPersistence',
    'RunicPermissions',
    'RunicTransactions'
)

Require-ProcessGate
$releaseRootPath = [System.IO.Path]::GetFullPath($ReleaseRoot)
$packageRoot = Join-Path $releaseRootPath 'Packages'
$releaseJson = Join-Path $releaseRootPath 'THUNDERSTORE-RELEASE.json'
Require (Test-Path -LiteralPath $packageRoot -PathType Container) `
    "Package directory is missing: $packageRoot"
Require (Test-Path -LiteralPath $releaseJson -PathType Leaf) `
    "Release record is missing: $releaseJson"
$release = [System.IO.File]::ReadAllText($releaseJson) | ConvertFrom-Json
Require ([string]$release.status -ceq 'package-audit-pass' -and
    [int]$release.package_count -eq 16 -and
    [int]$release.rebuilt_gameplay_package_count -eq 14) `
    'The selected release is not the audited Foundation-free package set.'

$targets = @(
    [pscustomobject]@{
        Name = 'ClientDefault'
        Root = [System.IO.Path]::GetFullPath($ClientPluginRoot)
        Versioned = $false
        StageRoot = $null
        BackupRoot = $null
    },
    [pscustomobject]@{
        Name = 'ServerDirect'
        Root = [System.IO.Path]::GetFullPath($ServerPluginRoot)
        Versioned = $true
        StageRoot = $null
        BackupRoot = $null
    }
)
foreach ($target in $targets) {
    Require (Test-Path -LiteralPath $target.Root -PathType Container) `
        "Plugin directory is missing: $($target.Root)"
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$packageRecords = @{}
foreach ($module in $modules) {
    $matches = @($release.packages | Where-Object {
            [string]$_.module -ceq [string]$module.Name
        })
    Require ($matches.Count -eq 1) `
        "Release record is missing $($module.Name)."
    $packageRecords[[string]$module.Name] = $matches[0]
}

foreach ($target in $targets) {
    $bepInExRoot = Split-Path -Parent $target.Root
    $target.StageRoot = Join-Path $bepInExRoot ('.codex-runic-stage\' + $runId)
    $target.BackupRoot = Join-Path $bepInExRoot ('.codex-backups\FoundationFree-' + $runId)
    New-Item -ItemType Directory -Path $target.StageRoot,$target.BackupRoot -Force | Out-Null

    foreach ($module in $modules) {
        $name = [string]$module.Name
        $version = [string]$module.Version
        $installName = if ($target.Versioned) {
            'Chazman-' + $name + '-' + $version
        }
        else {
            'Chazman-' + $name
        }
        $zipPath = Join-Path $packageRoot ('Chazman-' + $name + '-' + $version + '.zip')
        Require (Test-Path -LiteralPath $zipPath -PathType Leaf) `
            "Package is missing: $zipPath"
        Require ((Get-Sha256 $zipPath) -ceq
            [string]$packageRecords[$name].package_sha256) `
            "Package hash drifted: $name"

        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
            $expectedEntries = @(
                ($name + '.dll'),
                'manifest.json',
                'README.md',
                'icon.png',
                'CHANGELOG.md',
                ($name + '.cfg.example')
            )
            Require ($actualEntries.Count -eq 6 -and
                (@($actualEntries | Sort-Object) -join '|') -ceq
                (@($expectedEntries | Sort-Object) -join '|')) `
                "Package entries drifted: $name"
        }
        finally { $archive.Dispose() }

        $stagedPath = Get-ContainedPath `
            (Join-Path $target.StageRoot $installName) $target.StageRoot
        Expand-Archive -LiteralPath $zipPath -DestinationPath $stagedPath
        $stagedDll = Join-Path $stagedPath ($name + '.dll')
        Require ((Get-Sha256 $stagedDll) -ceq
            [string]$packageRecords[$name].dll_sha256) `
            "Staged DLL hash drifted: $name"
        $manifest = [System.IO.File]::ReadAllText(
            (Join-Path $stagedPath 'manifest.json')) | ConvertFrom-Json
        $dependencies = @($manifest.dependencies)
        Require ($dependencies.Count -eq 1 -and
            [string]$dependencies[0] -ceq
            'denikson-BepInExPack_Valheim-5.4.2333') `
            "Staged manifest is not BepInEx-only: $name"
    }
}

Require-ProcessGate
$movedOld = New-Object System.Collections.ArrayList
$movedNew = New-Object System.Collections.ArrayList
try {
    $managedNames = @($modules | ForEach-Object { $_.Name }) + $foundationNames
    foreach ($target in $targets) {
        $existing = @(Get-ChildItem -LiteralPath $target.Root -Directory | Where-Object {
                $directoryName = $_.Name
                $isManaged = $false
                foreach ($managedName in $managedNames) {
                    $pattern = '^Chazman-' +
                        [regex]::Escape([string]$managedName) +
                        '(?:-[0-9].*)?$'
                    if ($directoryName -match $pattern) {
                        $isManaged = $true
                        break
                    }
                }
                $isManaged
            })
        foreach ($directory in $existing) {
            $original = Get-ContainedPath $directory.FullName $target.Root
            $backup = Get-ContainedPath `
                (Join-Path $target.BackupRoot $directory.Name) $target.BackupRoot
            Require (-not (Test-Path -LiteralPath $backup)) `
                "Backup collision: $backup"
            Move-Item -LiteralPath $original -Destination $backup
            [void]$movedOld.Add([pscustomobject]@{
                    Original = $original
                    Backup = $backup
                })
        }
    }

    foreach ($target in $targets) {
        foreach ($module in $modules) {
            $name = [string]$module.Name
            $installName = if ($target.Versioned) {
                'Chazman-' + $name + '-' + [string]$module.Version
            }
            else {
                'Chazman-' + $name
            }
            $staged = Get-ContainedPath `
                (Join-Path $target.StageRoot $installName) $target.StageRoot
            $active = Get-ContainedPath `
                (Join-Path $target.Root $installName) $target.Root
            Require (-not (Test-Path -LiteralPath $active)) `
                "Install target is not clear: $active"
            Move-Item -LiteralPath $staged -Destination $active
            [void]$movedNew.Add([pscustomobject]@{
                    Stage = $staged
                    Active = $active
                })
        }
    }
}
catch {
    foreach ($item in @($movedNew) | Select-Object -Reverse) {
        if ((Test-Path -LiteralPath $item.Active) -and
            -not (Test-Path -LiteralPath $item.Stage)) {
            Move-Item -LiteralPath $item.Active -Destination $item.Stage
        }
    }
    foreach ($item in @($movedOld) | Select-Object -Reverse) {
        if ((Test-Path -LiteralPath $item.Backup) -and
            -not (Test-Path -LiteralPath $item.Original)) {
            Move-Item -LiteralPath $item.Backup -Destination $item.Original
        }
    }
    throw
}

$environmentResults = foreach ($target in $targets) {
    $matched = 0
    foreach ($module in $modules) {
        $name = [string]$module.Name
        $installName = if ($target.Versioned) {
            'Chazman-' + $name + '-' + [string]$module.Version
        }
        else {
            'Chazman-' + $name
        }
        $dllPath = Join-Path (Join-Path $target.Root $installName) ($name + '.dll')
        Require ((Get-Sha256 $dllPath) -ceq
            [string]$packageRecords[$name].dll_sha256) `
            "Installed DLL does not match the package: $dllPath"
        $matched++
    }
    $foundationDlls = @(Get-ChildItem -LiteralPath $target.Root -Recurse -File |
        Where-Object {
            $_.Name -in @(
                'RunicCore.dll',
                'RunicPersistence.dll',
                'RunicPermissions.dll',
                'RunicTransactions.dll')
        })
    Require ($foundationDlls.Count -eq 0) `
        "Retired Foundation DLLs remain active in $($target.Name)."
    [ordered]@{
        environment = $target.Name
        plugin_root = $target.Root
        gameplay_dlls = $matched
        hashes_match_release = $true
        retired_foundation_dlls = 0
        backup = $target.BackupRoot
    }
}

[ordered]@{
    status = 'DEPLOYED'
    source = 'audited Thunderstore ZIPs'
    release_record = $releaseJson
    environments = $environmentResults
} | ConvertTo-Json -Depth 5
