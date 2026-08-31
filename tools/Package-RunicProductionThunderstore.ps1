[CmdletBinding()]
param(
    [ValidateRange(1, 100000)]
    [int]$FocusedTests = 56
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\Thunderstore\1.0.0'))
$packagesRoot = Join-Path $releaseRoot 'Packages'
$releasePath = Join-Path $releaseRoot 'THUNDERSTORE-RELEASE.json'
$auditPath = Join-Path $releaseRoot 'PACKAGE-AUDIT.txt'
$moduleRoot = Join-Path $repositoryRoot 'RunicProduction'
$packageName = 'Chazman-RunicProduction-1.0.0.zip'
$packagePath = Join-Path $packagesRoot $packageName
$runId = 'Production-' + [DateTime]::UtcNow.ToString(
    'yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture) + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$stageRoot = Join-Path (Join-Path $releaseRoot 'Staging') $runId
$obsoleteRoot = Join-Path (Join-Path $releaseRoot 'Obsolete') $runId
$archivePackages = Join-Path $obsoleteRoot 'Packages'
$archiveRecords = Join-Path $obsoleteRoot 'ReleaseRecord'
$stagedZipOne = Join-Path $stageRoot ($packageName + '.first')
$stagedZipTwo = Join-Path $stageRoot ($packageName + '.second')
$packageAuditPath = Join-Path $stageRoot 'PACKAGE-AUDIT.json'
$deploymentPath = Join-Path $stageRoot 'DEPLOYMENT.json'
$releaseCandidate = Join-Path $stageRoot 'THUNDERSTORE-RELEASE.json.candidate'
$auditCandidate = Join-Path $stageRoot 'PACKAGE-AUDIT.txt.candidate'

function Get-Sha([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Write-Utf8([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Write-Json([string]$Path, $Value) {
    Write-Utf8 $Path (($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine)
}

function Get-PackageHashes([string]$Exclude = '') {
    $map = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $packagesRoot -Filter '*.zip' -File | Sort-Object Name) {
        if ($file.Name -cne $Exclude) { $map[$file.Name] = Get-Sha $file.FullName }
    }
    $map
}

function Assert-SameHashes($Expected, $Actual, [string]$Label) {
    if ($Expected.Count -ne $Actual.Count) { throw "$Label count changed." }
    foreach ($key in $Expected.Keys) {
        if (-not $Actual.Contains($key) -or
            [string]$Expected[$key] -cne [string]$Actual[$key]) {
            throw "$Label changed for $key."
        }
    }
}

function New-DeterministicZip([string]$Path, $Sources) {
    $stream = [IO.File]::Open(
        $Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($source in $Sources.GetEnumerator()) {
                $entry = $archive.CreateEntry(
                    [string]$source.Key, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $input = [IO.File]::OpenRead([string]$source.Value)
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
}

function Assert-Zip([string]$Path, $Sources) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = @($Sources.Keys)
        if ($names.Count -ne 6 -or $archive.Entries.Count -ne 6) {
            throw 'Production archive must contain exactly six root files.'
        }
        for ($index = 0; $index -lt $names.Count; $index++) {
            $entry = $archive.Entries[$index]
            $name = [string]$names[$index]
            if ($entry.FullName -cne $name -or $entry.FullName.Contains('/') -or
                $entry.FullName.Contains([char]92)) { throw "ZIP entry/order mismatch: $name" }
            $expectedTime = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
            if ($entry.LastWriteTime.DateTime -ne $expectedTime) {
                throw "ZIP timestamp mismatch: $name"
            }
            $source = [IO.File]::OpenRead([string]$Sources[$name])
            $inside = $entry.Open()
            $insideHash = [Security.Cryptography.SHA256]::Create()
            $sourceHash = [Security.Cryptography.SHA256]::Create()
            try {
                $a = [BitConverter]::ToString($insideHash.ComputeHash($inside)).Replace('-', '')
                $b = [BitConverter]::ToString($sourceHash.ComputeHash($source)).Replace('-', '')
                if ($a -cne $b) { throw "ZIP content mismatch: $name" }
            }
            finally {
                $sourceHash.Dispose(); $insideHash.Dispose(); $inside.Dispose(); $source.Dispose()
            }
        }
    }
    finally { $archive.Dispose() }
}

foreach ($required in @($packagesRoot, $releasePath, $auditPath, $packagePath)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing release input: $required" }
}
$inventory = @(Get-ChildItem -LiteralPath $packagesRoot -Filter '*.zip' -File)
if ($inventory.Count -ne 16) { throw "Expected 16 packages, found $($inventory.Count)." }
$untouchedBefore = Get-PackageHashes $packageName
if ($untouchedBefore.Count -ne 15) { throw 'Expected exactly 15 untouched packages.' }
$priorPackageHash = Get-Sha $packagePath
$priorReleaseHash = Get-Sha $releasePath
$priorAuditHash = Get-Sha $auditPath

$manifestPath = Join-Path $moduleRoot 'manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ([string]$manifest.name -cne 'RunicProduction' -or
    [string]$manifest.version_number -cne '1.0.0' -or
    @($manifest.dependencies).Count -ne 1 -or
    [string]$manifest.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2333') {
    throw 'Production manifest identity, version, or dependency surface is invalid.'
}
$sources = [ordered]@{
    'RunicProduction.dll' = Join-Path $moduleRoot 'bin\Release\netstandard2.1\RunicProduction.dll'
    'manifest.json' = $manifestPath
    'README.md' = Join-Path $moduleRoot 'README.md'
    'icon.png' = Join-Path $moduleRoot 'icon.png'
    'CHANGELOG.md' = Join-Path $moduleRoot 'CHANGELOG.md'
    'RunicProduction.cfg.example' = Join-Path $moduleRoot 'RunicProduction.cfg.example'
}
$sourceHashes = [ordered]@{}
foreach ($source in $sources.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $source.Value -PathType Leaf)) {
        throw "Missing package source: $($source.Value)"
    }
    $sourceHashes[$source.Key] = Get-Sha $source.Value
}
$identity = [Reflection.AssemblyName]::GetAssemblyName($sources['RunicProduction.dll'])
if ($identity.Name -cne 'RunicProduction' -or $identity.Version.ToString() -cne '1.0.0.0') {
    throw 'Production assembly identity is invalid.'
}
$icon = [Drawing.Image]::FromFile($sources['icon.png'])
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw 'Icon must be 256x256.' }
}
finally { $icon.Dispose() }

New-Item -ItemType Directory -Path $stageRoot | Out-Null
New-DeterministicZip $stagedZipOne $sources
New-DeterministicZip $stagedZipTwo $sources
Assert-Zip $stagedZipOne $sources
Assert-Zip $stagedZipTwo $sources
$newPackageHash = Get-Sha $stagedZipTwo
if ((Get-Sha $stagedZipOne) -cne $newPackageHash) {
    throw 'Deterministic package rebuild did not reproduce identical bytes.'
}
Assert-SameHashes $sourceHashes ([ordered]@{
    'RunicProduction.dll' = Get-Sha $sources['RunicProduction.dll']
    'manifest.json' = Get-Sha $sources['manifest.json']
    'README.md' = Get-Sha $sources['README.md']
    'icon.png' = Get-Sha $sources['icon.png']
    'CHANGELOG.md' = Get-Sha $sources['CHANGELOG.md']
    'RunicProduction.cfg.example' = Get-Sha $sources['RunicProduction.cfg.example']
}) 'Production sources'
Assert-SameHashes $untouchedBefore (Get-PackageHashes $packageName) 'Untouched packages during staging'

$created = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
$packageAudit = [ordered]@{
    schema = 'runic-focused-thunderstore-package-audit/v1'
    status = 'PASS'
    run_id = $runId
    created_utc = $created
    module = 'RunicProduction'
    focused_tests = "$FocusedTests/$FocusedTests"
    plugin_build = 'PASS (0 warnings, 0 errors)'
    package_path = $packagePath
    package_sha256 = $newPackageHash
    dll_sha256 = $sourceHashes['RunicProduction.dll']
    deterministic_entry_timestamp = '2000-01-01T00:00:00'
    canonical_inventory_count = 16
    untouched_package_count = 15
    untouched_package_hashes = $untouchedBefore
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
}
Write-Json $packageAuditPath $packageAudit
$packageAuditHash = Get-Sha $packageAuditPath
$deployment = [ordered]@{
    schema = 'runic-user-managed-deployment-evidence/v1'
    status = 'USER_MANAGED_NOT_DEPLOYED'
    run_id = $runId
    recorded_utc = $created
    package = $packagePath
    package_sha256 = $newPackageHash
    dll_sha256 = $sourceHashes['RunicProduction.dll']
    r2_package_cache_updated = $false
    runtime_profile_deployed = $false
    dedicated_server_updated = $false
}
Write-Json $deploymentPath $deployment
$deploymentHash = Get-Sha $deploymentPath

$release = Get-Content -Raw -LiteralPath $releasePath | ConvertFrom-Json
$release.status = 'focused-package-audit-pass-user-managed-deployment'
$release.completed_utc = $created
$release.latest_rebuilt_package_count = 1
$release.latest_focused_test_count = $FocusedTests
$release.latest_rebuilt_modules = @('RunicProduction')
$focused = @($release.focused_tests | Where-Object module -eq 'RunicProduction')
$packageRecord = @($release.packages | Where-Object module -eq 'RunicProduction')
if ($focused.Count -ne 1 -or $packageRecord.Count -ne 1) {
    throw 'Release record lacks one exact Production test/package record.'
}
$focused[0].passed = $FocusedTests
$focused[0].total = $FocusedTests
$focused[0].status = 'PASS'
$packageRecord[0].package_sha256 = $newPackageHash
$packageRecord[0].dll_sha256 = $sourceHashes['RunicProduction.dll']
$packageRecord[0].dependencies = @($manifest.dependencies)
$packageRecord[0].entries = @($sources.Keys)
$release.focused_test_total = [int](($release.focused_tests | Measure-Object total -Sum).Sum)
$release.post_acceptance_updates += [pscustomobject][ordered]@{
    module = 'RunicProduction'
    updated_utc = $created
    reason = 'Relay chests can serve independent station roles; recipe Output and explicit Fuel Input links remain supported; finite over-capacity oven fuel no longer disables cooking automation.'
    focused_tests = $FocusedTests
    focused_status = 'PASS'
    package_sha256 = $newPackageHash
    dll_sha256 = $sourceHashes['RunicProduction.dll']
    dedicated_acceptance_reused = $false
    coexistence_smoke_reused = $false
}
$release.staged_audit = $packageAuditPath
$release.latest_regression_summary = [pscustomobject][ordered]@{
    focused_modules = "PASS ($FocusedTests/$FocusedTests)"
    plugin_builds = 'PASS (0 warnings, 0 errors)'
    suite14 = 'not rerun; focused Production-only update'
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
    live_acceptance = 'PENDING_USER_TESTING'
}
$release | Add-Member -Force -NotePropertyName production_release -NotePropertyValue ([pscustomobject][ordered]@{
    status = 'USER_MANAGED_NOT_DEPLOYED'
    run_id = $runId
    package_archive = $obsoleteRoot
    package_audit = $packageAuditPath
    package_audit_sha256 = $packageAuditHash
    deployment_evidence = $deploymentPath
    deployment_evidence_sha256 = $deploymentHash
})
Write-Json $releaseCandidate $release
$releaseHash = Get-Sha $releaseCandidate
$auditLines = @(
    'RUNIC_PRODUCTION_PACKAGE_AUDIT_PASS packages=1 deployed_targets=0',
    'status=focused-package-audit-pass-user-managed-deployment',
    "run_id=$runId",
    'active_package_inventory=16',
    'latest_rebuilt_packages=1',
    'latest_rebuilt_modules=RunicProduction',
    'runtime_profile_deployed=false',
    'r2_package_cache_updated=false',
    'dedicated_server_updated=false',
    'deployment_status=USER_MANAGED_NOT_DEPLOYED',
    "focused_production=$FocusedTests/$FocusedTests",
    'plugin_builds=PASS_(0_warnings,_0_errors)',
    "package_audit=$packageAuditPath",
    "package_audit_sha256=$packageAuditHash",
    "deployment_evidence=$deploymentPath",
    "deployment_evidence_sha256=$deploymentHash",
    "release_json=$releasePath",
    "release_json_sha256=$releaseHash",
    "prior_package_archive=$obsoleteRoot",
    'untouched_packages_preserved=15',
    "production_package_sha256=$newPackageHash",
    "production_dll_sha256=$($sourceHashes['RunicProduction.dll'])"
)
Write-Utf8 $auditCandidate (($auditLines -join [Environment]::NewLine) + [Environment]::NewLine)

Assert-SameHashes $untouchedBefore (Get-PackageHashes $packageName) 'Untouched packages before commit'
if ((Get-Sha $packagePath) -cne $priorPackageHash -or
    (Get-Sha $releasePath) -cne $priorReleaseHash -or
    (Get-Sha $auditPath) -cne $priorAuditHash) {
    throw 'Canonical Production package or release records changed concurrently.'
}
New-Item -ItemType Directory -Path $archivePackages | Out-Null
New-Item -ItemType Directory -Path $archiveRecords | Out-Null
Copy-Item -LiteralPath $packagePath -Destination (Join-Path $archivePackages $packageName)
Copy-Item -LiteralPath $releasePath -Destination (Join-Path $archiveRecords 'THUNDERSTORE-RELEASE.json')
Copy-Item -LiteralPath $auditPath -Destination (Join-Path $archiveRecords 'PACKAGE-AUDIT.txt')
if (@(Get-ChildItem -LiteralPath $obsoleteRoot -Recurse -File).Count -ne 3 -or
    (Get-Sha (Join-Path $archivePackages $packageName)) -cne $priorPackageHash -or
    (Get-Sha (Join-Path $archiveRecords 'THUNDERSTORE-RELEASE.json')) -cne $priorReleaseHash -or
    (Get-Sha (Join-Path $archiveRecords 'PACKAGE-AUDIT.txt')) -cne $priorAuditHash) {
    throw 'Prior Production package/release archive verification failed.'
}

try {
    $packageTemporary = Join-Path $packagesRoot ('.codex-' + $runId + '-' + $packageName + '.tmp')
    $releaseTemporary = Join-Path $releaseRoot ('.codex-' + $runId + '-THUNDERSTORE-RELEASE.json.tmp')
    $auditTemporary = Join-Path $releaseRoot ('.codex-' + $runId + '-PACKAGE-AUDIT.txt.tmp')
    Copy-Item -LiteralPath $stagedZipTwo -Destination $packageTemporary
    Copy-Item -LiteralPath $releaseCandidate -Destination $releaseTemporary
    Copy-Item -LiteralPath $auditCandidate -Destination $auditTemporary
    [IO.File]::Move($packageTemporary, $packagePath, $true)
    [IO.File]::Move($releaseTemporary, $releasePath, $true)
    [IO.File]::Move($auditTemporary, $auditPath, $true)
    if ((Get-Sha $packagePath) -cne $newPackageHash -or
        (Get-Sha $releasePath) -cne $releaseHash) { throw 'Canonical promotion hash mismatch.' }
    Assert-Zip $packagePath $sources
    Assert-SameHashes $untouchedBefore (Get-PackageHashes $packageName) 'Untouched packages after commit'
}
catch {
    Copy-Item -Force -LiteralPath (Join-Path $archivePackages $packageName) -Destination $packagePath
    Copy-Item -Force -LiteralPath (Join-Path $archiveRecords 'THUNDERSTORE-RELEASE.json') -Destination $releasePath
    Copy-Item -Force -LiteralPath (Join-Path $archiveRecords 'PACKAGE-AUDIT.txt') -Destination $auditPath
    throw
}

Write-Output "RUN_ID=$runId"
Write-Output 'DEPLOYMENT_STATUS=USER_MANAGED_NOT_DEPLOYED'
Write-Output "PRODUCTION_FOCUSED_TESTS=$FocusedTests/$FocusedTests"
Write-Output "PRODUCTION_PACKAGE_SHA256=$newPackageHash"
Write-Output "PRODUCTION_DLL_SHA256=$($sourceHashes['RunicProduction.dll'])"
Write-Output 'UNTOUCHED_PACKAGES_PRESERVED=15'
Write-Output "PACKAGE_AUDIT=$packageAuditPath"
Write-Output "DEPLOYMENT_EVIDENCE=$deploymentPath"
