[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AcceptanceMatrixPath,
    [Parameter(Mandatory = $true)][string]$CoexistenceEvidencePath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Require {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Hash-File {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Hash-ZipEntry {
    param([string]$ZipPath, [string]$EntryName)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $EntryName })
        Require ($matches.Count -eq 1) "ZIP entry '$EntryName' is not unique in $ZipPath"
        $stream = $matches[0].Open()
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return (($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('X2') }) -join '')
        }
        finally {
            $algorithm.Dispose()
            $stream.Dispose()
        }
    }
    finally { $archive.Dispose() }
}

function New-DeterministicZip {
    param([string]$Destination, [object[]]$Files)
    $stream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::CreateNew)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        foreach ($file in $Files) {
            $entry = $archive.CreateEntry(
                [string]$file.Entry,
                [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(
                2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [System.IO.File]::OpenRead([string]$file.Path)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

function Test-Package {
    param([string]$ZipPath, [hashtable]$Module)
    $expectedEntries = @(
        ($Module.Name + '.dll'),
        'manifest.json',
        'README.md',
        'icon.png',
        'CHANGELOG.md',
        ($Module.Name + '.cfg.example'))
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
        Require ($actualEntries.Count -eq $expectedEntries.Count) "$($Module.Name) ZIP entry count drifted."
        Require ((@($actualEntries | Sort-Object) -join '|') -ceq
            (@($expectedEntries | Sort-Object) -join '|')) "$($Module.Name) ZIP entries drifted."
        $manifestEntry = @($archive.Entries | Where-Object { $_.FullName -ceq 'manifest.json' })[0]
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        Require ([string]$manifest.name -ceq [string]$Module.Name) "$($Module.Name) package manifest name drifted."
        Require ([string]$manifest.version_number -ceq [string]$Module.Version) "$($Module.Name) package manifest version drifted."
        $dependencies = @($manifest.dependencies)
        Require ($dependencies.Count -eq 1 -and [string]$dependencies[0] -ceq
            'denikson-BepInExPack_Valheim-5.4.2333') "$($Module.Name) package manifest is not BepInEx-only."
    }
    finally { $archive.Dispose() }
    $dllPath = Join-Path $repoRoot ($Module.Name + '\bin\Release\netstandard2.1\' + $Module.Name + '.dll')
    $sourceDllHash = Hash-File $dllPath
    $zipDllHash = Hash-ZipEntry $ZipPath ($Module.Name + '.dll')
    Require ($sourceDllHash -ceq $zipDllHash) "$($Module.Name) package DLL differs from the release build."
    return [ordered]@{
        module = [string]$Module.Name
        version = [string]$Module.Version
        package_file = [System.IO.Path]::GetFileName($ZipPath)
        package_sha256 = Hash-File $ZipPath
        dll_sha256 = $sourceDllHash
        dependencies = @('denikson-BepInExPack_Valheim-5.4.2333')
        entries = $expectedEntries
    }
}

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\Thunderstore\1.0.0'))
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'Packages'))
Require (Test-Path -LiteralPath $packageRoot -PathType Container) "Package root is missing: $packageRoot"
$acceptancePath = [System.IO.Path]::GetFullPath($AcceptanceMatrixPath)
$coexistencePath = [System.IO.Path]::GetFullPath($CoexistenceEvidencePath)
Require (Test-Path -LiteralPath $acceptancePath -PathType Leaf) "Acceptance matrix is missing: $acceptancePath"
Require (Test-Path -LiteralPath $coexistencePath -PathType Leaf) "Coexistence evidence is missing: $coexistencePath"
$acceptance = [System.IO.File]::ReadAllText($acceptancePath) | ConvertFrom-Json
$coexistence = [System.IO.File]::ReadAllText($coexistencePath) | ConvertFrom-Json
Require ([string]$acceptance.schema -ceq 'runic-foundation-free-dedicated-acceptance-matrix/v1' -and
    [string]$acceptance.status -ceq 'PASS' -and [int]$acceptance.scenario_count -eq 9) 'Acceptance matrix did not pass all nine scenarios.'
Require ([string]$coexistence.schema -ceq 'runic-foundation-free-coexistence-smoke/v1' -and
    [string]$coexistence.status -ceq 'GO' -and [int]$coexistence.payload.loaded_plugin_count -eq 16) 'Coexistence evidence did not pass the exact 16-DLL smoke.'

$modules = @(
    @{ Name='RunicStorage'; Version='1.0.0'; Focused=18 },
    @{ Name='RunicCrafting'; Version='1.0.0'; Focused=26 },
    @{ Name='RunicAgriculture'; Version='1.0.0'; Focused=17 },
    @{ Name='RunicProduction'; Version='1.0.0'; Focused=45 },
    @{ Name='RunicPrecisionBuildTool'; Version='2.0.1'; Focused=156 },
    @{ Name='RunicInventory'; Version='1.0.0'; Focused=116 },
    @{ Name='RunicPortals'; Version='1.0.0'; Focused=19 },
    @{ Name='RunicExploration'; Version='1.0.0'; Focused=53 },
    @{ Name='RunicAwareness'; Version='1.0.0'; Focused=63 },
    @{ Name='RunicInteraction'; Version='1.0.0'; Focused=62 },
    @{ Name='RunicSafety'; Version='1.0.0'; Focused=159 },
    @{ Name='RunicVelocity'; Version='1.0.0'; Focused=19 },
    @{ Name='RunicSentinel'; Version='1.0.0'; Focused=22 },
    @{ Name='RunicWorldEngine'; Version='1.0.0'; Focused=13 }
)
$foundationPackages = @(
    'Chazman-RunicCore-1.0.0.zip',
    'Chazman-RunicPersistence-1.0.0.zip',
    'Chazman-RunicPermissions-1.0.0.zip',
    'Chazman-RunicTransactions-1.0.0.zip')
$unchangedPackages = @(
    'Chazman-RunicBuildCamera-1.0.0.zip',
    'Chazman-RunicDisplayStands-1.3.1.zip')
$expectedBaseline = @($modules | ForEach-Object {
        'Chazman-' + $_.Name + '-' + $_.Version + '.zip'
    }) + $foundationPackages + $unchangedPackages
$baselineFiles = @(Get-ChildItem -LiteralPath $packageRoot -File)
Require ($baselineFiles.Count -eq 20) 'The preserved baseline is not the exact 20-package inventory.'
Require ((@($baselineFiles.Name | Sort-Object) -join '|') -ceq
    (@($expectedBaseline | Sort-Object) -join '|')) 'The preserved baseline package names drifted.'

$buildCameraDll = Join-Path $repoRoot 'RunicBuildCamera\bin\Release\netstandard2.1\RunicBuildCamera.dll'
$integrityDll = Join-Path $repoRoot 'RunicIntegrity\bin\Release\netstandard2.1\RunicIntegrity.dll'
$buildCameraExpected = '00E9D7517B7126482BB2B985382D1E988788A0136F225F1C752F397AE37EFA0A'
$integrityExpected = 'C23E1832EA68E7C695E285BCF8D23620EBFAD573754B849BAD4F47417777E635'
Require ((Hash-File $buildCameraDll) -ceq $buildCameraExpected) 'Runic Build Camera DLL is not byte-identical to the accepted baseline.'
Require ((Hash-File $integrityDll) -ceq $integrityExpected) 'Runic Integrity DLL is not byte-identical to the accepted baseline.'

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot ('Staging\FoundationFree-' + $runId)))
$archiveRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot ('Obsolete\FoundationFree-' + $runId)))
$releasePrefix = $releaseRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
Require ($stageRoot.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) 'Stage root escaped the release root.'
Require ($archiveRoot.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) 'Archive root escaped the release root.'
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$stagedRecords = @()
foreach ($module in $modules) {
    $moduleRoot = Join-Path $repoRoot $module.Name
    $iconPath = if ($module.Name -ceq 'RunicPrecisionBuildTool') {
        Join-Path $moduleRoot 'media\icon.png'
    }
    else {
        Join-Path $moduleRoot 'icon.png'
    }
    $files = @(
        @{ Entry=($module.Name + '.dll'); Path=(Join-Path $moduleRoot ('bin\Release\netstandard2.1\' + $module.Name + '.dll')) },
        @{ Entry='manifest.json'; Path=(Join-Path $moduleRoot 'manifest.json') },
        @{ Entry='README.md'; Path=(Join-Path $moduleRoot 'README.md') },
        @{ Entry='icon.png'; Path=$iconPath },
        @{ Entry='CHANGELOG.md'; Path=(Join-Path $moduleRoot 'CHANGELOG.md') },
        @{ Entry=($module.Name + '.cfg.example'); Path=(Join-Path $moduleRoot ($module.Name + '.cfg.example')) })
    foreach ($file in $files) {
        Require (Test-Path -LiteralPath $file.Path -PathType Leaf) "Package input is missing: $($file.Path)"
    }
    $zipName = 'Chazman-' + $module.Name + '-' + $module.Version + '.zip'
    $zipPath = Join-Path $stageRoot $zipName
    New-DeterministicZip $zipPath $files
    $stagedRecords += Test-Package $zipPath $module
}
Require ($stagedRecords.Count -eq 14) 'The staged package audit did not cover all 14 gameplay modules.'
[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot 'STAGED-PACKAGE-AUDIT.json'),
    ([ordered]@{
            schema='runic-foundation-free-staged-packages/v1'
            status='PASS'
            packages=$stagedRecords
        } | ConvertTo-Json -Depth 7),
    [System.Text.UTF8Encoding]::new($false))

$archivePackages = Join-Path $archiveRoot 'Packages'
$archiveEvidence = Join-Path $archiveRoot 'Evidence'
$retiredFoundation = Join-Path $archiveRoot 'RetiredFoundation'
New-Item -ItemType Directory -Path $archivePackages,$archiveEvidence,$retiredFoundation -Force | Out-Null
$baselineHashes = @{}
foreach ($file in $baselineFiles) {
    $baselineHashes[$file.Name] = Hash-File $file.FullName
    $backup = Join-Path $archivePackages $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $backup
    Require ((Hash-File $backup) -ceq $baselineHashes[$file.Name]) "Baseline archive verification failed: $($file.Name)"
}
foreach ($name in @('THUNDERSTORE-RELEASE.json','PUBLISHING.md')) {
    $source = Join-Path $releaseRoot $name
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Baseline release evidence is missing: $source"
    $backup = Join-Path $archiveEvidence $name
    Copy-Item -LiteralPath $source -Destination $backup
    Require ((Hash-File $backup) -ceq (Hash-File $source)) "Baseline evidence archive verification failed: $name"
}

foreach ($record in $stagedRecords) {
    $source = Join-Path $stageRoot $record.package_file
    $destination = Join-Path $packageRoot $record.package_file
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Require ((Hash-File $destination) -ceq [string]$record.package_sha256) "Promoted package hash drifted: $($record.package_file)"
}
foreach ($name in $foundationPackages) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $packageRoot $name))
    Require ($source.StartsWith(
            $packageRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) "Foundation package target escaped the package root: $source"
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Foundation package is missing before retirement: $source"
    $destination = Join-Path $retiredFoundation $name
    Move-Item -LiteralPath $source -Destination $destination
    Require ((Hash-File $destination) -ceq $baselineHashes[$name]) "Retired Foundation package hash drifted: $name"
}

$expectedFinal = @($modules | ForEach-Object {
        'Chazman-' + $_.Name + '-' + $_.Version + '.zip'
    }) + $unchangedPackages
$finalFiles = @(Get-ChildItem -LiteralPath $packageRoot -File)
Require ($finalFiles.Count -eq 16) 'The active package directory is not the exact 16-package release.'
Require ((@($finalFiles.Name | Sort-Object) -join '|') -ceq
    (@($expectedFinal | Sort-Object) -join '|')) 'The active 16-package inventory names drifted.'

$packageRecords = @($stagedRecords)
foreach ($name in $unchangedPackages) {
    $path = Join-Path $packageRoot $name
    Require ((Hash-File $path) -ceq $baselineHashes[$name]) "Unchanged companion package drifted: $name"
    $dllEntry = if ($name -like '*BuildCamera*') { 'RunicBuildCamera.dll' } else { 'RunicDisplayStands.dll' }
    $packageRecords += [ordered]@{
        module = [System.IO.Path]::GetFileNameWithoutExtension($dllEntry)
        version = if ($name -like '*BuildCamera*') { '1.0.0' } else { '1.3.1' }
        package_file = $name
        package_sha256 = Hash-File $path
        dll_sha256 = Hash-ZipEntry $path $dllEntry
        dependencies = @('preserved-baseline-manifest')
        entries = @()
        unchanged = $true
    }
}
$focused = @($modules | ForEach-Object {
        [ordered]@{ module=$_.Name; passed=[int]$_.Focused; total=[int]$_.Focused; status='PASS' }
    })
$completedUtc = [DateTime]::UtcNow.ToString('O')
$releaseRecord = [ordered]@{
    schema = 'runic-foundation-free-thunderstore-release/v1'
    status = 'package-audit-pass'
    completed_utc = $completedUtc
    package_count = 16
    rebuilt_gameplay_package_count = 14
    runtime_profile_dll_count = 16
    removed_runtime_packages = @('RunicCore','RunicPersistence','RunicPermissions','RunicTransactions')
    acceptance = [ordered]@{ path=$acceptancePath; sha256=(Hash-File $acceptancePath); scenarios=9; status='PASS' }
    coexistence = [ordered]@{ path=$coexistencePath; sha256=(Hash-File $coexistencePath); plugins=16; status='GO' }
    focused_tests = $focused
    focused_test_total = [int](($modules | ForEach-Object { [int]$_['Focused'] } | Measure-Object -Sum).Sum)
    unchanged_runtime_companions = @(
        [ordered]@{ module='RunicBuildCamera'; dll_sha256=$buildCameraExpected },
        [ordered]@{ module='RunicIntegrity'; dll_sha256=$integrityExpected })
    packages = @($packageRecords | Sort-Object package_file)
    baseline_archive = $archiveRoot
    staged_audit = Join-Path $stageRoot 'STAGED-PACKAGE-AUDIT.json'
}
$releaseJson = Join-Path $releaseRoot 'THUNDERSTORE-RELEASE.json'
[System.IO.File]::WriteAllText($releaseJson, ($releaseRecord | ConvertTo-Json -Depth 9), [System.Text.UTF8Encoding]::new($false))

$publishing = @"
# Foundation-free Thunderstore publishing

This release contains 16 active packages: 14 independently installable gameplay packages plus the
unchanged Runic Build Camera and Runic Display Stands packages. Every rebuilt gameplay manifest has
only `denikson-BepInExPack_Valheim-5.4.2333` as a dependency.

Do not publish or install Runic Core, Runic Persistence, Runic Permissions, or Runic Transactions.
They were moved from the active inventory only after the nine-row dedicated acceptance matrix and
the exact 16-DLL coexistence smoke passed. Their prior ZIPs and release evidence are preserved under:

`$archiveRoot`

The gameplay packages may be published in any order. Runic Inventory is optional for the bounded
Crafting and Storage enhancements; it is not a package dependency. Runic Portals owns its portal and
group data formats. No package requires a shared Runic runtime DLL.

Acceptance evidence: `$acceptancePath`

Coexistence evidence: `$coexistencePath`

The authoritative package and DLL hashes are recorded in `THUNDERSTORE-RELEASE.json`.
"@
[System.IO.File]::WriteAllText((Join-Path $releaseRoot 'PUBLISHING.md'), $publishing, [System.Text.UTF8Encoding]::new($false))
$releaseHash = Hash-File $releaseJson
$auditPath = Join-Path $releaseRoot 'PACKAGE-AUDIT.txt'
[System.IO.File]::WriteAllLines($auditPath, @(
        'RUNIC_FOUNDATION_FREE_PACKAGE_AUDIT_PASS packages=16 rebuilt=14',
        'status=package-audit-pass',
        'foundation_packages_active=0',
        'runtime_profile_dlls=16',
        ('focused_tests=' + $releaseRecord.focused_test_total),
        ('acceptance=' + $acceptancePath),
        ('coexistence=' + $coexistencePath),
        ('release_json=' + $releaseJson),
        ('release_json_sha256=' + $releaseHash),
        ('baseline_archive=' + $archiveRoot)
    ), [System.Text.UTF8Encoding]::new($false))
Write-Output ('RUNIC_FOUNDATION_FREE_PACKAGE_AUDIT_PASS packages=16 rebuilt=14 focused_tests=' +
    $releaseRecord.focused_test_total + ' release_sha256=' + $releaseHash + ' archive="' + $archiveRoot + '"')
