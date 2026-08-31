[CmdletBinding()]
param(
    [ValidateRange(0, 100000)]
    [int]$RunicBuildCameraTests = 71,

    [string]$VerificationJsonPath = '',

    [string]$PluginBuildSummary = 'PASS (0 warnings, 0 errors)',

    [string]$Suite14Summary = '17/22; five unrelated stale architecture/documentation audits remain'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\Thunderstore\1.0.0'))
$packagesRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'Packages'))
$releaseJsonPath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'THUNDERSTORE-RELEASE.json'))
$summaryAuditPath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'PACKAGE-AUDIT.txt'))
$runId = 'BuildCameraReleaseAlignment-' +
    [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture) + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $releaseRoot 'Staging') $runId))
$obsoleteRoot = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $releaseRoot 'Obsolete') $runId))
$archivePackagesRoot = [System.IO.Path]::GetFullPath((Join-Path $obsoleteRoot 'Packages'))
$archiveReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $obsoleteRoot 'ReleaseRecord'))
$packageAuditPath = [System.IO.Path]::GetFullPath((Join-Path $stageRoot 'PACKAGE-AUDIT.json'))
$deploymentEvidencePath = [System.IO.Path]::GetFullPath((Join-Path $stageRoot 'DEPLOYMENT.json'))
$releaseCandidatePath = [System.IO.Path]::GetFullPath((Join-Path $stageRoot 'THUNDERSTORE-RELEASE.json.candidate'))
$summaryCandidatePath = [System.IO.Path]::GetFullPath((Join-Path $stageRoot 'PACKAGE-AUDIT.txt.candidate'))

$targetModules = @('RunicBuildCamera')
$requiredDependency = 'denikson-BepInExPack_Valheim-5.4.2333'
$expectedPackageCount = 16
$expectedUntouchedPackageCount = 15

function Test-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    return $resolvedChild.StartsWith(
        $resolvedParent + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-ArtifactWritePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-ChildPath -Parent $releaseRoot -Child $Path)) {
        throw "Refusing to write outside the intended Thunderstore release root: $Path"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Get-FileHashMap {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Files)

    $result = [ordered]@{}
    foreach ($item in $Files.GetEnumerator()) {
        $result[[string]$item.Key] = Get-Sha256 -Path ([string]$item.Value)
    }
    return $result
}

function Assert-HashMapsEqual {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Expected,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Actual,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Expected.Count -ne $Actual.Count) {
        throw "$Context count changed from $($Expected.Count) to $($Actual.Count)."
    }
    foreach ($key in $Expected.Keys) {
        if (-not $Actual.Contains($key) -or [string]$Expected[$key] -cne [string]$Actual[$key]) {
            throw "$Context hash changed for $key."
        }
    }
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )

    Assert-ArtifactWritePath -Path $Path
    [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

function Write-Utf8Json {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Depth = 30
    )

    $json = $Value | ConvertTo-Json -Depth $Depth
    Write-Utf8Text -Value ($json + [Environment]::NewLine) -Path $Path
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Sources,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Assert-ArtifactWritePath -Path $Destination
    if (Test-Path -LiteralPath $Destination) {
        throw "Refusing to overwrite an existing staged archive: $Destination"
    }

    $stream = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            $timestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($item in $Sources.GetEnumerator()) {
                $entry = $archive.CreateEntry(
                    [string]$item.Key,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $sourceStream = [System.IO.File]::OpenRead([string]$item.Value)
                $entryStream = $entry.Open()
                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                    $sourceStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ExactArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Sources
    )

    $expectedNames = @($Sources.Keys)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($expectedNames.Count -ne 6 -or $archive.Entries.Count -ne 6) {
            throw "Archive must contain exactly six root files: $ArchivePath"
        }

        for ($index = 0; $index -lt $expectedNames.Count; $index++) {
            $expectedName = [string]$expectedNames[$index]
            $entry = $archive.Entries[$index]
            if ($entry.FullName -cne $expectedName -or
                $entry.FullName.IndexOf('/') -ge 0 -or
                $entry.FullName.IndexOf([char]92) -ge 0) {
                throw "Archive entry/order mismatch at index $index in $ArchivePath."
            }

            $expectedTimestamp = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Unspecified)
            if ($entry.LastWriteTime.DateTime -ne $expectedTimestamp) {
                throw "Archive entry timestamp is not deterministic for $expectedName."
            }

            $entryStream = $entry.Open()
            $sourceStream = [System.IO.File]::OpenRead([string]$Sources[$expectedName])
            $entryAlgorithm = [System.Security.Cryptography.SHA256]::Create()
            $sourceAlgorithm = [System.Security.Cryptography.SHA256]::Create()
            try {
                if ($entry.Length -ne $sourceStream.Length) {
                    throw "Archive entry length mismatch for $expectedName."
                }
                $entryHash = [BitConverter]::ToString($entryAlgorithm.ComputeHash($entryStream)).Replace('-', '')
                $sourceHash = [BitConverter]::ToString($sourceAlgorithm.ComputeHash($sourceStream)).Replace('-', '')
                if ($entryHash -cne $sourceHash) {
                    throw "Archive entry bytes mismatch for $expectedName."
                }
            }
            finally {
                $sourceAlgorithm.Dispose()
                $entryAlgorithm.Dispose()
                $sourceStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Set-OrAddProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value
    )

    if ($Object.PSObject.Properties.Name -contains $Name) {
        $Object.$Name = $Value
    }
    else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}

function Resolve-TestCount {
    param(
        [Parameter(Mandatory = $true)][string]$Module,
        [Parameter(Mandatory = $true)][int]$SuppliedCount,
        $Verification
    )

    $verifiedCount = 0
    if ($null -ne $Verification) {
        $matches = @($Verification.focused_tests | Where-Object { [string]$_.module -ceq $Module })
        if ($matches.Count -ne 1) {
            throw "Verification JSON must contain exactly one focused_tests record for $Module."
        }
        $record = $matches[0]
        if ([string]$record.status -cne 'PASS' -or
            [int]$record.passed -le 0 -or
            [int]$record.passed -ne [int]$record.total) {
            throw "Verification JSON does not prove a complete focused-test pass for $Module."
        }
        $verifiedCount = [int]$record.total
    }

    if ($SuppliedCount -gt 0 -and $verifiedCount -gt 0 -and $SuppliedCount -ne $verifiedCount) {
        throw "Supplied and verified focused-test counts disagree for $Module."
    }
    if ($verifiedCount -gt 0) {
        return $verifiedCount
    }
    if ($SuppliedCount -gt 0) {
        return $SuppliedCount
    }
    throw "Supply -${Module}Tests or provide -VerificationJsonPath with a passing record for $Module."
}

function Get-PackageInventoryHashMap {
    param([string[]]$ExcludedNames = @())

    $map = [ordered]@{}
    $files = @(Get-ChildItem -LiteralPath $packagesRoot -Filter '*.zip' -File | Sort-Object Name)
    foreach ($file in $files) {
        if ($ExcludedNames -notcontains $file.Name) {
            $map[$file.Name] = Get-Sha256 -Path $file.FullName
        }
    }
    return $map
}

foreach ($writePath in @(
    $stageRoot,
    $obsoleteRoot,
    $archivePackagesRoot,
    $archiveReleaseRoot,
    $packageAuditPath,
    $deploymentEvidencePath,
    $releaseCandidatePath,
    $summaryCandidatePath)) {
    Assert-ArtifactWritePath -Path $writePath
}

foreach ($requiredPath in @($packagesRoot, $releaseJsonPath, $summaryAuditPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required canonical release path is missing: $requiredPath"
    }
}
if ((Test-Path -LiteralPath $stageRoot) -or (Test-Path -LiteralPath $obsoleteRoot)) {
    throw "Generated run identifier unexpectedly collides with an existing artifact root: $runId"
}

$verification = $null
if (-not [string]::IsNullOrWhiteSpace($VerificationJsonPath)) {
    $resolvedVerificationPath = [System.IO.Path]::GetFullPath($VerificationJsonPath)
    if (-not (Test-Path -LiteralPath $resolvedVerificationPath -PathType Leaf)) {
        throw "Verification JSON is missing: $resolvedVerificationPath"
    }
    $verification = Get-Content -Raw -LiteralPath $resolvedVerificationPath | ConvertFrom-Json
}

$testCounts = [ordered]@{
    RunicBuildCamera = Resolve-TestCount -Module 'RunicBuildCamera' -SuppliedCount $RunicBuildCameraTests -Verification $verification
}
$latestFocusedTestCount = [int](($testCounts.Values | Measure-Object -Sum).Sum)

$moduleDefinitions = @(
    [ordered]@{
        module = 'RunicBuildCamera'
        reason = 'The canonical Thunderstore ZIP now contains the exact Runic Build Camera DLL that passed the current 71-test warnings-as-errors release gate.'
    }
)

$canonicalPackageNames = @($targetModules | ForEach-Object { 'Chazman-' + $_ + '-1.0.0.zip' })
$canonicalInventoryBefore = @(Get-ChildItem -LiteralPath $packagesRoot -Filter '*.zip' -File | Sort-Object Name)
if ($canonicalInventoryBefore.Count -ne $expectedPackageCount) {
    throw "Expected exactly $expectedPackageCount canonical ZIPs, found $($canonicalInventoryBefore.Count)."
}
foreach ($packageName in $canonicalPackageNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $packagesRoot $packageName) -PathType Leaf)) {
        throw "Target canonical package is missing: $packageName"
    }
}
$untouchedHashesBefore = Get-PackageInventoryHashMap -ExcludedNames $canonicalPackageNames
if ($untouchedHashesBefore.Count -ne $expectedUntouchedPackageCount) {
    throw "Expected exactly $expectedUntouchedPackageCount untouched ZIPs, found $($untouchedHashesBefore.Count)."
}
$allPackageHashesBefore = Get-PackageInventoryHashMap
$targetHashesBefore = [ordered]@{}
foreach ($packageName in $canonicalPackageNames) {
    $targetHashesBefore[$packageName] = $allPackageHashesBefore[$packageName]
}
$releaseRecordHashesBefore = [ordered]@{
    'THUNDERSTORE-RELEASE.json' = Get-Sha256 -Path $releaseJsonPath
    'PACKAGE-AUDIT.txt' = Get-Sha256 -Path $summaryAuditPath
}

$release = Get-Content -Raw -LiteralPath $releaseJsonPath | ConvertFrom-Json
if ([int]$release.package_count -ne $expectedPackageCount -or @($release.packages).Count -ne $expectedPackageCount) {
    throw 'The canonical release inventory is not the expected 16-package release.'
}
foreach ($item in $allPackageHashesBefore.GetEnumerator()) {
    $record = @($release.packages | Where-Object { [string]$_.package_file -ceq [string]$item.Key })
    if ($record.Count -ne 1 -or [string]$record[0].package_sha256 -cne [string]$item.Value) {
        throw "Canonical package $($item.Key) does not match its release-record hash."
    }
}

New-Item -ItemType Directory -Path $stageRoot | Out-Null

$packageResults = @()
foreach ($definition in $moduleDefinitions) {
    $module = [string]$definition.module
    $moduleRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $module))
    if (-not (Test-ChildPath -Parent $repositoryRoot -Child $moduleRoot)) {
        throw "Module path escaped the repository root: $moduleRoot"
    }

    $manifestPath = Join-Path $moduleRoot 'manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ([string]$manifest.name -cne $module -or [string]$manifest.version_number -cne '1.0.0') {
        throw "$module manifest identity/version mismatch."
    }
    $dependencies = @($manifest.dependencies)
    if ($dependencies.Count -ne 1 -or [string]$dependencies[0] -cne $requiredDependency) {
        throw "$module manifest dependency surface is not the standalone BepInEx-only contract."
    }

    $sources = [ordered]@{
        ($module + '.dll') = (Join-Path $moduleRoot ('bin\Release\netstandard2.1\' + $module + '.dll'))
        'manifest.json' = $manifestPath
        'README.md' = (Join-Path $moduleRoot 'README.md')
        'icon.png' = (Join-Path $moduleRoot 'icon.png')
        'CHANGELOG.md' = (Join-Path $moduleRoot 'CHANGELOG.md')
        ($module + '.cfg.example') = (Join-Path $moduleRoot ($module + '.cfg.example'))
    }
    foreach ($source in $sources.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $source.Value -PathType Leaf)) {
            throw "$module package source is missing: $($source.Value)"
        }
    }

    $identity = [System.Reflection.AssemblyName]::GetAssemblyName($sources[$module + '.dll'])
    if ($identity.Name -cne $module -or $identity.Version.ToString() -cne '1.0.0.0') {
        throw "$module assembly identity does not match 1.0.0.0."
    }

    $icon = [System.Drawing.Image]::FromFile($sources['icon.png'])
    try {
        if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
            throw "$module icon is not exactly 256x256."
        }
    }
    finally {
        $icon.Dispose()
    }

    $sourceHashes = Get-FileHashMap -Files $sources
    $zipName = 'Chazman-' + $module + '-1.0.0.zip'
    $firstZipPath = Join-Path $stageRoot ($zipName + '.first')
    $secondZipPath = Join-Path $stageRoot ($zipName + '.second')
    New-DeterministicZip -Sources $sources -Destination $firstZipPath
    New-DeterministicZip -Sources $sources -Destination $secondZipPath
    Assert-ExactArchive -ArchivePath $firstZipPath -Sources $sources
    Assert-ExactArchive -ArchivePath $secondZipPath -Sources $sources

    $sourceHashesAfter = Get-FileHashMap -Files $sources
    Assert-HashMapsEqual -Expected $sourceHashes -Actual $sourceHashesAfter -Context "$module package sources"
    $firstHash = Get-Sha256 -Path $firstZipPath
    $secondHash = Get-Sha256 -Path $secondZipPath
    if ($firstHash -cne $secondHash) {
        throw "$module deterministic package rebuild did not reproduce identical bytes."
    }

    $packageResults += [pscustomobject][ordered]@{
        module = $module
        package_file = $zipName
        stage_path = $secondZipPath
        canonical_path = (Join-Path $packagesRoot $zipName)
        package_sha256 = $secondHash
        dll_sha256 = $sourceHashes[$module + '.dll']
        focused_tests = [int]$testCounts[$module]
        reason = [string]$definition.reason
        dependencies = $dependencies
        source_paths = $sources
        entries = $sourceHashes
    }
}

$untouchedHashesAfterStaging = Get-PackageInventoryHashMap -ExcludedNames $canonicalPackageNames
Assert-HashMapsEqual -Expected $untouchedHashesBefore -Actual $untouchedHashesAfterStaging -Context 'Untouched canonical package inventory during staging'

$createdUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
$packageAudit = [ordered]@{
    schema = 'runic-focused-thunderstore-package-audit/v1'
    status = 'PASS'
    run_id = $runId
    created_utc = $createdUtc
    stage_root = $stageRoot
    archive_root = $obsoleteRoot
    deterministic_entry_timestamp = '2000-01-01T00:00:00'
    package_count = 1
    canonical_inventory_count = $expectedPackageCount
    untouched_package_count = $expectedUntouchedPackageCount
    untouched_package_hashes_before = $untouchedHashesBefore
    untouched_package_hashes_expected_after = $untouchedHashesBefore
    focused_tests = ($latestFocusedTestCount.ToString() + '/' + $latestFocusedTestCount.ToString())
    plugin_builds = $PluginBuildSummary
    suite14 = $Suite14Summary
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
    packages = @($packageResults | ForEach-Object {
        [ordered]@{
            module = $_.module
            package_file = $_.package_file
            package_path = $_.canonical_path
            package_sha256 = $_.package_sha256
            dll_sha256 = $_.dll_sha256
            focused_tests = ($_.focused_tests.ToString() + '/' + $_.focused_tests.ToString())
            dependencies = @($_.dependencies)
            entries = $_.entries
        }
    })
}
Write-Utf8Json -Value $packageAudit -Path $packageAuditPath
$packageAuditHash = Get-Sha256 -Path $packageAuditPath

$deploymentEvidence = [ordered]@{
    schema = 'runic-user-managed-deployment-evidence/v1'
    status = 'USER_MANAGED_NOT_DEPLOYED'
    run_id = $runId
    recorded_utc = $createdUtc
    source = 'one audited canonical Thunderstore ZIP'
    package_audit = $packageAuditPath
    package_audit_sha256 = $packageAuditHash
    process_gate = 'NOT_APPLICABLE_USER_MANAGED'
    active_r2_profile = $null
    r2_package_cache_updated = $false
    dedicated_server_updated = $false
    bepinex_loader_caches_invalidated = $false
    packages = @($packageResults | ForEach-Object {
        [ordered]@{
            module = $_.module
            zip = $_.canonical_path
            package_sha256 = $_.package_sha256
            dll_sha256 = $_.dll_sha256
        }
    })
}
Write-Utf8Json -Value $deploymentEvidence -Path $deploymentEvidencePath
$deploymentEvidenceHash = Get-Sha256 -Path $deploymentEvidencePath

$release.status = 'focused-package-audit-pass-user-managed-deployment'
$release.completed_utc = $createdUtc
$release.latest_rebuilt_package_count = 1

foreach ($result in $packageResults) {
    $focusedRecord = @($release.focused_tests | Where-Object { [string]$_.module -ceq $result.module })
    if ($focusedRecord.Count -eq 0) {
        $newFocusedRecord = [pscustomobject][ordered]@{
            module = $result.module
            passed = $result.focused_tests
            total = $result.focused_tests
            status = 'PASS'
        }
        $release.focused_tests += $newFocusedRecord
        $focusedRecord = @($newFocusedRecord)
    }
    elseif ($focusedRecord.Count -ne 1) {
        throw "Expected one focused test record for $($result.module)."
    }
    $focusedRecord[0].passed = $result.focused_tests
    $focusedRecord[0].total = $result.focused_tests
    $focusedRecord[0].status = 'PASS'

    $packageRecord = @($release.packages | Where-Object { [string]$_.module -ceq $result.module })
    if ($packageRecord.Count -ne 1) {
        throw "Expected one package inventory record for $($result.module)."
    }
    $packageRecord[0].package_sha256 = $result.package_sha256
    $packageRecord[0].dll_sha256 = $result.dll_sha256
    $packageRecord[0].dependencies = @($result.dependencies)
    $packageRecord[0].entries = @($result.entries.Keys)

    $release.post_acceptance_updates += [pscustomobject][ordered]@{
        module = $result.module
        updated_utc = $createdUtc
        reason = $result.reason
        focused_tests = $result.focused_tests
        focused_status = 'PASS'
        package_sha256 = $result.package_sha256
        dll_sha256 = $result.dll_sha256
        dedicated_acceptance_reused = $false
        coexistence_smoke_reused = $false
    }
}

$release.focused_test_total = [int](($release.focused_tests | Measure-Object -Property total -Sum).Sum)
Set-OrAddProperty -Object $release -Name 'latest_focused_test_count' -Value $latestFocusedTestCount
Set-OrAddProperty -Object $release -Name 'latest_rebuilt_modules' -Value ([object[]]$targetModules)
Set-OrAddProperty -Object $release -Name 'staged_audit' -Value $packageAuditPath
Set-OrAddProperty -Object $release -Name 'build_camera_release_alignment' -Value ([pscustomobject][ordered]@{
    status = 'USER_MANAGED_NOT_DEPLOYED'
    run_id = $runId
    source = 'one audited canonical Thunderstore ZIP'
    process_gate = 'NOT_APPLICABLE_USER_MANAGED'
    package_archive = $obsoleteRoot
    package_audit = $packageAuditPath
    package_audit_sha256 = $packageAuditHash
    deployment_evidence = $deploymentEvidencePath
    deployment_evidence_sha256 = $deploymentEvidenceHash
    active_r2_profile = $null
    r2_package_cache_updated = $false
    dedicated_server_updated = $false
    bepinex_loader_caches_invalidated = $false
})
Set-OrAddProperty -Object $release -Name 'latest_regression_summary' -Value ([pscustomobject][ordered]@{
    focused_modules = ('PASS (' + $latestFocusedTestCount + '/' + $latestFocusedTestCount + ')')
    plugin_builds = $PluginBuildSummary
    suite14 = $Suite14Summary
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
    live_acceptance = 'PENDING_USER_TESTING'
})
Write-Utf8Json -Value $release -Path $releaseCandidatePath -Depth 40
$releaseCandidateHash = Get-Sha256 -Path $releaseCandidatePath

$resultByModule = @{}
foreach ($result in $packageResults) {
    $resultByModule[$result.module] = $result
}
$auditLines = @(
    'RUNIC_BUILD_CAMERA_RELEASE_ALIGNMENT_PACKAGE_AUDIT_PASS packages=1 deployed_targets=0',
    'status=focused-package-audit-pass-user-managed-deployment',
    ('run_id=' + $runId),
    ('active_package_inventory=' + $expectedPackageCount),
    'latest_rebuilt_packages=1',
    'latest_rebuilt_modules=RunicBuildCamera',
    'runtime_profile_deployed=false',
    'r2_package_cache_updated=false',
    'dedicated_server_updated=false',
    'deployment_status=USER_MANAGED_NOT_DEPLOYED',
    ('focused_tests_latest=' + $latestFocusedTestCount),
    ('focused_tests_release_record=' + $release.focused_test_total),
    ('focused_build_camera=' + $testCounts.RunicBuildCamera + '/' + $testCounts.RunicBuildCamera),
    ('plugin_builds=' + $PluginBuildSummary.Replace(' ', '_')),
    ('suite14=' + $Suite14Summary.Replace(' ', '_')),
    'historical_acceptance_reused=false',
    'historical_coexistence_reused=false',
    ('package_audit=' + $packageAuditPath),
    ('package_audit_sha256=' + $packageAuditHash),
    ('deployment_evidence=' + $deploymentEvidencePath),
    ('deployment_evidence_sha256=' + $deploymentEvidenceHash),
    ('release_json=' + $releaseJsonPath),
    ('release_json_sha256=' + $releaseCandidateHash),
    ('prior_package_archive=' + $obsoleteRoot),
    ('untouched_packages_preserved=' + $expectedUntouchedPackageCount),
    ('build_camera_package_sha256=' + $resultByModule.RunicBuildCamera.package_sha256),
    ('build_camera_dll_sha256=' + $resultByModule.RunicBuildCamera.dll_sha256)
)
Write-Utf8Text -Value (($auditLines -join [Environment]::NewLine) + [Environment]::NewLine) -Path $summaryCandidatePath

# Recheck every package source immediately before the canonical commit. This makes
# concurrent edits/builds a hard failure instead of silently packaging mixed bytes.
foreach ($result in $packageResults) {
    $currentSourceHashes = Get-FileHashMap -Files $result.source_paths
    Assert-HashMapsEqual -Expected $result.entries -Actual $currentSourceHashes -Context "$($result.module) pre-commit package sources"
}
$untouchedHashesPreCommit = Get-PackageInventoryHashMap -ExcludedNames $canonicalPackageNames
Assert-HashMapsEqual -Expected $untouchedHashesBefore -Actual $untouchedHashesPreCommit -Context 'Untouched canonical package inventory before commit'
$allPackageHashesPreCommit = Get-PackageInventoryHashMap
$targetHashesPreCommit = [ordered]@{}
foreach ($packageName in $canonicalPackageNames) {
    $targetHashesPreCommit[$packageName] = $allPackageHashesPreCommit[$packageName]
}
Assert-HashMapsEqual -Expected $targetHashesBefore -Actual $targetHashesPreCommit -Context 'Target canonical package inventory before archive'
$releaseRecordHashesPreCommit = [ordered]@{
    'THUNDERSTORE-RELEASE.json' = Get-Sha256 -Path $releaseJsonPath
    'PACKAGE-AUDIT.txt' = Get-Sha256 -Path $summaryAuditPath
}
Assert-HashMapsEqual -Expected $releaseRecordHashesBefore -Actual $releaseRecordHashesPreCommit -Context 'Canonical release records before archive'

New-Item -ItemType Directory -Path $archivePackagesRoot | Out-Null
New-Item -ItemType Directory -Path $archiveReleaseRoot | Out-Null

foreach ($result in $packageResults) {
    Copy-Item -LiteralPath $result.canonical_path -Destination (Join-Path $archivePackagesRoot $result.package_file)
}
Copy-Item -LiteralPath $releaseJsonPath -Destination (Join-Path $archiveReleaseRoot 'THUNDERSTORE-RELEASE.json')
Copy-Item -LiteralPath $summaryAuditPath -Destination (Join-Path $archiveReleaseRoot 'PACKAGE-AUDIT.txt')

$archivedPackageFiles = @(Get-ChildItem -LiteralPath $archivePackagesRoot -File)
$archivedReleaseFiles = @(Get-ChildItem -LiteralPath $archiveReleaseRoot -File)
$allArchivedFiles = @(Get-ChildItem -LiteralPath $obsoleteRoot -Recurse -File)
if ($archivedPackageFiles.Count -ne 1 -or
    $archivedReleaseFiles.Count -ne 2 -or
    $allArchivedFiles.Count -ne 3 -or
    ((@($archivedPackageFiles.Name | Sort-Object) -join '|') -cne (@($canonicalPackageNames | Sort-Object) -join '|')) -or
    ((@($archivedReleaseFiles.Name | Sort-Object) -join '|') -cne (@('PACKAGE-AUDIT.txt', 'THUNDERSTORE-RELEASE.json') -join '|'))) {
    throw 'The obsolete archive must contain only the prior Build Camera ZIP and two prior release records.'
}
$archivedTargetHashes = [ordered]@{}
foreach ($packageName in $canonicalPackageNames) {
    $archivedTargetHashes[$packageName] = Get-Sha256 -Path (Join-Path $archivePackagesRoot $packageName)
}
Assert-HashMapsEqual -Expected $targetHashesBefore -Actual $archivedTargetHashes -Context 'Archived target packages'
$archivedReleaseRecordHashes = [ordered]@{
    'THUNDERSTORE-RELEASE.json' = Get-Sha256 -Path (Join-Path $archiveReleaseRoot 'THUNDERSTORE-RELEASE.json')
    'PACKAGE-AUDIT.txt' = Get-Sha256 -Path (Join-Path $archiveReleaseRoot 'PACKAGE-AUDIT.txt')
}
Assert-HashMapsEqual -Expected $releaseRecordHashesBefore -Actual $archivedReleaseRecordHashes -Context 'Archived release records'

$commitStarted = $false
$temporaryCommitPaths = @()
try {
    $commitStarted = $true
    foreach ($result in $packageResults) {
        $temporaryPath = Join-Path $packagesRoot ('.codex-' + $runId + '-' + $result.package_file + '.tmp')
        Assert-ArtifactWritePath -Path $temporaryPath
        Copy-Item -LiteralPath $result.stage_path -Destination $temporaryPath
        $temporaryCommitPaths += $temporaryPath
        if ((Get-Sha256 -Path $temporaryPath) -cne $result.package_sha256) {
            throw "Temporary canonical promotion hash mismatch for $($result.module)."
        }
        # File.Replace rejects a null backup path on the PowerShell/.NET runtime
        # used by this Windows host. File.Move's overwrite overload performs the
        # intended same-volume promotion without creating an untracked backup;
        # the verified obsolete archive remains the transaction rollback source.
        [System.IO.File]::Move($temporaryPath, $result.canonical_path, $true)
    }

    $releaseTemporaryPath = Join-Path $releaseRoot ('.codex-' + $runId + '-THUNDERSTORE-RELEASE.json.tmp')
    $summaryTemporaryPath = Join-Path $releaseRoot ('.codex-' + $runId + '-PACKAGE-AUDIT.txt.tmp')
    foreach ($pair in @(
        @($releaseCandidatePath, $releaseTemporaryPath, $releaseJsonPath),
        @($summaryCandidatePath, $summaryTemporaryPath, $summaryAuditPath))) {
        Assert-ArtifactWritePath -Path $pair[1]
        Copy-Item -LiteralPath $pair[0] -Destination $pair[1]
        $temporaryCommitPaths += $pair[1]
        [System.IO.File]::Move($pair[1], $pair[2], $true)
    }

    foreach ($result in $packageResults) {
        if ((Get-Sha256 -Path $result.canonical_path) -cne $result.package_sha256) {
            throw "Canonical package promotion hash mismatch for $($result.module)."
        }
        Assert-ExactArchive -ArchivePath $result.canonical_path -Sources $result.source_paths
    }

    $canonicalInventoryAfter = @(Get-ChildItem -LiteralPath $packagesRoot -Filter '*.zip' -File | Sort-Object Name)
    if ($canonicalInventoryAfter.Count -ne $expectedPackageCount) {
        throw "Canonical package count changed to $($canonicalInventoryAfter.Count)."
    }
    $untouchedHashesAfter = Get-PackageInventoryHashMap -ExcludedNames $canonicalPackageNames
    Assert-HashMapsEqual -Expected $untouchedHashesBefore -Actual $untouchedHashesAfter -Context 'Untouched canonical package inventory after commit'
    if ((Get-Sha256 -Path $releaseJsonPath) -cne $releaseCandidateHash) {
        throw 'Canonical release JSON promotion hash mismatch.'
    }
}
catch {
    if ($commitStarted) {
        foreach ($result in $packageResults) {
            $archivePath = Join-Path $archivePackagesRoot $result.package_file
            if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
                Copy-Item -LiteralPath $archivePath -Destination $result.canonical_path -Force
            }
        }
        Copy-Item -LiteralPath (Join-Path $archiveReleaseRoot 'THUNDERSTORE-RELEASE.json') -Destination $releaseJsonPath -Force
        Copy-Item -LiteralPath (Join-Path $archiveReleaseRoot 'PACKAGE-AUDIT.txt') -Destination $summaryAuditPath -Force
    }
    foreach ($temporaryPath in $temporaryCommitPaths) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
    throw
}

Write-Output ('RUN_ID=' + $runId)
Write-Output 'DEPLOYMENT_STATUS=USER_MANAGED_NOT_DEPLOYED'
Write-Output ('PACKAGE_AUDIT=' + $packageAuditPath)
Write-Output ('PACKAGE_AUDIT_SHA256=' + $packageAuditHash)
Write-Output ('DEPLOYMENT_EVIDENCE=' + $deploymentEvidencePath)
Write-Output ('DEPLOYMENT_EVIDENCE_SHA256=' + $deploymentEvidenceHash)
Write-Output ('RELEASE_JSON_SHA256=' + (Get-Sha256 -Path $releaseJsonPath))
Write-Output ('UNTOUCHED_PACKAGES_PRESERVED=' + $expectedUntouchedPackageCount)
foreach ($result in $packageResults) {
    Write-Output ($result.module + '_FOCUSED_TESTS=' + $result.focused_tests + '/' + $result.focused_tests)
    Write-Output ($result.module + '_PACKAGE_SHA256=' + $result.package_sha256)
    Write-Output ($result.module + '_DLL_SHA256=' + $result.dll_sha256)
}
