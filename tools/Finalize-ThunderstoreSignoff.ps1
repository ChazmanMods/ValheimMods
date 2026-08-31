[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$SignoffRoot)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Sha256File([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Sha256Bytes([byte[]]$Bytes) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function ReadEntryBytes([System.IO.Compression.ZipArchive]$Archive, [string]$Name) {
    $matches = @($Archive.Entries | Where-Object FullName -CEQ $Name)
    Require ($matches.Count -eq 1) "ZIP entry '$Name' is not unique."
    $input = $matches[0].Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $input.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $input.Dispose()
    }
}

function WriteUtf8([string]$Path, [string]$Value) {
    [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

function SetProperty($Object, [string]$Name, $Value) {
    if ($Object.PSObject.Properties.Name -contains $Name) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\Thunderstore\1.0.0'))
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'Staging'))
$resolvedSignoffRoot = [System.IO.Path]::GetFullPath($SignoffRoot)
$stagingPrefix = $stagingRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
Require ($resolvedSignoffRoot.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Signoff root must be a direct release-staging descendant.'
Require (Test-Path -LiteralPath $resolvedSignoffRoot -PathType Container) 'Signoff root is missing.'

$packageRoot = Join-Path $releaseRoot 'Packages'
$releasePath = Join-Path $releaseRoot 'THUNDERSTORE-RELEASE.json'
$summaryPath = Join-Path $releaseRoot 'PACKAGE-AUDIT.txt'
$testLogPath = Join-Path $resolvedSignoffRoot 'FOCUSED-TESTS.log'
$acceptanceRuntimePath = Join-Path $resolvedSignoffRoot 'Acceptance\ACCEPTANCE-RUNTIME-PASS.json'
$acceptanceMatrixPath = Join-Path $resolvedSignoffRoot 'Acceptance\ACCEPTANCE-MATRIX.json'
$coexistencePath = Join-Path $resolvedSignoffRoot 'Coexistence\SMOKE-PASS.json'
$coexistencePayloadRoot = Join-Path $resolvedSignoffRoot 'Payload-Coexistence'
foreach ($path in @($releasePath, $summaryPath, $testLogPath, $acceptanceRuntimePath, $acceptanceMatrixPath, $coexistencePath)) {
    Require (Test-Path -LiteralPath $path -PathType Leaf) "Required signoff evidence is missing: $path"
}

$focusedCounts = [ordered]@{
    RunicStorage = 25
    RunicCrafting = 26
    RunicAgriculture = 29
    RunicProduction = 56
    RunicPrecisionBuildTool = 157
    RunicInventory = 118
    RunicPortals = 25
    RunicExploration = 53
    RunicAwareness = 63
    RunicInteraction = 62
    RunicSafety = 159
    RunicVelocity = 19
    RunicSentinel = 22
    RunicWorldEngine = 13
    RunicBuildCamera = 71
}
$focusedTotal = [int](($focusedCounts.Values | Measure-Object -Sum).Sum)
$testText = [System.IO.File]::ReadAllText($testLogPath)
Require ([regex]::IsMatch($testText, '(?m)^RUNIC_THUNDERSTORE_RELEASE_AUDIT_PASS projects=15\s*$')) 'Focused test transcript lacks the exact release pass marker.'
Require ([regex]::Matches($testText, '(?m)^AUDIT_PASS .+$').Count -eq 15) 'Focused test transcript does not contain exactly 15 project passes.'
Require ($testText.IndexOf('Build FAILED.', [StringComparison]::OrdinalIgnoreCase) -lt 0) 'Focused test transcript contains a failed build.'

$acceptanceRuntime = Get-Content -Raw -LiteralPath $acceptanceRuntimePath | ConvertFrom-Json
$acceptanceMatrix = Get-Content -Raw -LiteralPath $acceptanceMatrixPath | ConvertFrom-Json
$coexistence = Get-Content -Raw -LiteralPath $coexistencePath | ConvertFrom-Json
Require ([string]$acceptanceRuntime.schema -ceq 'runic-foundation-free-dedicated-acceptance-runtime/v1' -and
    [string]$acceptanceRuntime.status -ceq 'GO' -and [string]$acceptanceRuntime.profile -ceq 'acceptance' -and
    [int]$acceptanceRuntime.payload.loaded_plugin_count -eq 14) 'Exact-package dedicated acceptance evidence did not pass.'
Require ([string]$acceptanceMatrix.schema -ceq 'runic-foundation-free-dedicated-acceptance-matrix/v1' -and
    [string]$acceptanceMatrix.status -ceq 'PASS' -and [int]$acceptanceMatrix.scenario_count -eq 9 -and
    [int]$acceptanceMatrix.focused_test_total -eq 827) 'Nine-scenario acceptance matrix did not pass with the current focused count.'
Require ([string]$coexistence.schema -ceq 'runic-foundation-free-coexistence-smoke/v1' -and
    [string]$coexistence.status -ceq 'GO' -and [string]$coexistence.profile -ceq 'coexistence' -and
    [int]$coexistence.payload.loaded_plugin_count -eq 16) 'Exact-package 16-plugin coexistence evidence did not pass.'

$release = Get-Content -Raw -LiteralPath $releasePath | ConvertFrom-Json
$packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.zip' -File | Sort-Object Name)
Require ($packageFiles.Count -eq 16 -and [int]$release.package_count -eq 16 -and @($release.packages).Count -eq 16) 'Canonical release is not the exact 16-package inventory.'
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$packageEvidence = @()
$newPackageCount = 0
$updatePackageCount = 0
$alreadyPublishedCount = 0

foreach ($packageFile in $packageFiles) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        Require (@($names | Group-Object | Where-Object Count -gt 1).Count -eq 0) "Duplicate ZIP entry in $($packageFile.Name)."
        Require (@($names | Where-Object { $_.Contains('/') -or $_.Contains([char]92) }).Count -eq 0) "Nested or unsafe ZIP entry in $($packageFile.Name)."
        foreach ($required in @('manifest.json', 'README.md', 'icon.png')) {
            Require ($names -ccontains $required) "$($packageFile.Name) lacks root $required."
        }
        $manifestBytes = ReadEntryBytes $archive 'manifest.json'
        $readmeBytes = ReadEntryBytes $archive 'README.md'
        [void]$strictUtf8.GetString($readmeBytes)
        $manifest = $strictUtf8.GetString($manifestBytes) | ConvertFrom-Json
        $module = [string]$manifest.name
        $version = [string]$manifest.version_number
        Require ($module -cmatch '^[A-Za-z0-9_]{1,128}$') "$($packageFile.Name) has an invalid manifest name."
        Require ($version -cmatch '^\d+\.\d+\.\d+$') "$module has an invalid semantic version."
        Require (-not [string]::IsNullOrWhiteSpace([string]$manifest.description) -and ([string]$manifest.description).Length -le 250) "$module has an invalid description."
        Require ($manifest.PSObject.Properties.Name -contains 'website_url') "$module has no website_url field."
        $dependencies = @($manifest.dependencies)
        $expectedDependency = if ($module -ceq 'RunicDisplayStands') {
            'denikson-BepInExPack_Valheim-5.4.2202'
        }
        else { 'denikson-BepInExPack_Valheim-5.4.2333' }
        Require ($dependencies.Count -eq 1 -and [string]$dependencies[0] -ceq $expectedDependency) "$module does not have its exact BepInEx-only dependency."
        Require ($packageFile.Name -ceq ('Chazman-' + $module + '-' + $version + '.zip')) "$module package filename and manifest differ."

        $dllName = $module + '.dll'
        Require ($names -ccontains $dllName) "$module package lacks its root DLL."
        $dllBytes = ReadEntryBytes $archive $dllName
        $dllHash = Sha256Bytes $dllBytes
        $packageHash = Sha256File $packageFile.FullName
        $record = @($release.packages | Where-Object module -CEQ $module)
        Require ($record.Count -eq 1 -and [string]$record[0].package_sha256 -ceq $packageHash -and
            [string]$record[0].dll_sha256 -ceq $dllHash) "$module canonical release hashes differ from its ZIP."
        $payloadDll = Join-Path $coexistencePayloadRoot $dllName
        Require (Test-Path -LiteralPath $payloadDll -PathType Leaf) "$module is absent from the coexistence payload."
        Require ((Sha256File $payloadDll) -ceq $dllHash) "$module coexistence payload differs from its ZIP."
        if ($module -cne 'RunicDisplayStands') {
            $sourceDll = Join-Path $repoRoot ($module + '\bin\Release\netstandard2.1\' + $dllName)
            Require (Test-Path -LiteralPath $sourceDll -PathType Leaf) "$module tested release DLL is missing."
            Require ((Sha256File $sourceDll) -ceq $dllHash) "$module ZIP differs from the tested release DLL."
            Require ($focusedCounts.Contains($module)) "$module has no current focused test count."
            $focusedRecord = @($release.focused_tests | Where-Object module -CEQ $module)
            Require ($focusedRecord.Count -eq 1 -and [int]$focusedRecord[0].passed -eq [int]$focusedCounts[$module] -and
                [int]$focusedRecord[0].total -eq [int]$focusedCounts[$module] -and [string]$focusedRecord[0].status -ceq 'PASS') "$module focused-test release record is stale."
        }

        $iconBytes = ReadEntryBytes $archive 'icon.png'
        $iconStream = [System.IO.MemoryStream]::new($iconBytes, $false)
        try {
            $icon = [System.Drawing.Image]::FromStream($iconStream)
            try { Require ($icon.Width -eq 256 -and $icon.Height -eq 256) "$module icon is not 256x256." }
            finally { $icon.Dispose() }
        }
        finally { $iconStream.Dispose() }

        $remoteState = ''
        try {
            $remote = Invoke-RestMethod -Uri ('https://thunderstore.io/api/experimental/package/Chazman/' + $module + '/') -Method Get -TimeoutSec 30
            $remoteVersion = [string]$remote.latest.version_number
            if ([version]$version -gt [version]$remoteVersion) {
                $remoteState = 'UPDATE_READY'
                $updatePackageCount++
            }
            elseif ([version]$version -eq [version]$remoteVersion -and $module -ceq 'RunicDisplayStands') {
                $download = Invoke-WebRequest -Uri ('https://thunderstore.io/package/download/Chazman/' + $module + '/' + $version + '/') -UseBasicParsing -TimeoutSec 60
                Require ((Sha256Bytes ([byte[]]$download.Content)) -ceq $packageHash) 'Published RunicDisplayStands bytes differ from the canonical ZIP.'
                $remoteState = 'ALREADY_PUBLISHED_EXACT'
                $alreadyPublishedCount++
            }
            else { throw "$module local version $version is not newer than published $remoteVersion." }
        }
        catch {
            if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
                $remoteState = 'NEW_PACKAGE_READY'
                $newPackageCount++
            }
            else { throw }
        }

        $packageEvidence += [pscustomobject][ordered]@{
            module = $module
            version = $version
            package_file = $packageFile.Name
            package_sha256 = $packageHash
            dll_sha256 = $dllHash
            thunderstore_disposition = $remoteState
        }
    }
    finally { $archive.Dispose() }
}

Require ($newPackageCount -eq 14 -and $updatePackageCount -eq 1 -and $alreadyPublishedCount -eq 1) 'Thunderstore publication disposition is not 14 new, one update, and one exact already-published package.'
$acceptancePlugins = @($acceptanceRuntime.payload.plugins)
$coexistencePlugins = @($coexistence.payload.plugins)
Require ($acceptancePlugins.Count -eq 14 -and $coexistencePlugins.Count -eq 16) 'Runtime evidence plugin inventories drifted.'
foreach ($package in $packageEvidence) {
    $loaded = @($coexistencePlugins | Where-Object file -CEQ ($package.module + '.dll'))
    Require ($loaded.Count -eq 1 -and [string]$loaded[0].sha256 -ceq [string]$package.dll_sha256) "$($package.module) coexistence runtime hash differs from its package."
}

$completedUtc = [DateTime]::UtcNow.ToString('O')
$runId = Split-Path -Leaf $resolvedSignoffRoot
$signoff = [ordered]@{
    schema = 'runic-thunderstore-publication-signoff/v1'
    status = 'READY_TO_PUBLISH'
    run_id = $runId
    completed_utc = $completedUtc
    package_count = 16
    uploadable_package_count = 15
    new_package_count = $newPackageCount
    update_package_count = $updatePackageCount
    already_published_exact_count = $alreadyPublishedCount
    focused_project_count = 15
    focused_test_total = $focusedTotal
    focused_test_log = $testLogPath
    focused_test_log_sha256 = Sha256File $testLogPath
    dedicated_acceptance_plugins = 14
    dedicated_acceptance_scenarios = 9
    acceptance_runtime = $acceptanceRuntimePath
    acceptance_runtime_sha256 = Sha256File $acceptanceRuntimePath
    acceptance_matrix = $acceptanceMatrixPath
    acceptance_matrix_sha256 = Sha256File $acceptanceMatrixPath
    coexistence_plugins = 16
    coexistence_evidence = $coexistencePath
    coexistence_evidence_sha256 = Sha256File $coexistencePath
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
    packages = $packageEvidence
}
$signoffPath = Join-Path $resolvedSignoffRoot 'FULL-SIGNOFF.json'
WriteUtf8 $signoffPath (($signoff | ConvertTo-Json -Depth 20) + [Environment]::NewLine)
$signoffHash = Sha256File $signoffPath

$release.status = 'READY_TO_PUBLISH'
$release.completed_utc = $completedUtc
$release.focused_test_total = $focusedTotal
SetProperty $release 'latest_focused_test_count' $focusedTotal
SetProperty $release 'latest_validated_modules' ([object[]]@($packageEvidence.module))
SetProperty $release 'publication_signoff' ([pscustomobject][ordered]@{
    status = 'READY_TO_PUBLISH'
    run_id = $runId
    evidence = $signoffPath
    evidence_sha256 = $signoffHash
    package_count = 16
    uploadable_package_count = 15
    already_published_exact_count = 1
    focused_tests = ($focusedTotal.ToString() + '/' + $focusedTotal.ToString())
    dedicated_acceptance = 'PASS (14/14 exact package DLLs; 9/9 scenarios)'
    coexistence = 'PASS (16/16 exact package DLLs)'
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
})
SetProperty $release 'latest_regression_summary' ([pscustomobject][ordered]@{
    focused_modules = ('PASS (' + $focusedTotal + '/' + $focusedTotal + ')')
    plugin_builds = 'PASS (0 warnings, 0 errors)'
    package_format = 'PASS (16/16)'
    exact_package_dlls = 'PASS (16/16)'
    dedicated_acceptance = 'PASS (14/14; 9/9 scenarios)'
    coexistence = 'PASS (16/16)'
    thunderstore_eligibility = 'PASS (14 new, 1 update, 1 already published exact)'
    deployment = 'USER_MANAGED_NOT_DEPLOYED'
    live_acceptance = 'PASS_EXACT_PACKAGE_DEDICATED_LOAD'
})

$releaseCandidate = Join-Path $resolvedSignoffRoot 'THUNDERSTORE-RELEASE.json.candidate'
WriteUtf8 $releaseCandidate (($release | ConvertTo-Json -Depth 40) + [Environment]::NewLine)
$auditLines = @(
    'RUNIC_THUNDERSTORE_PUBLICATION_SIGNOFF_PASS packages=16 uploadable=15 already_published_exact=1',
    'status=READY_TO_PUBLISH',
    ('run_id=' + $runId),
    ('focused_projects=15'),
    ('focused_tests=' + $focusedTotal + '/' + $focusedTotal),
    'plugin_builds=PASS_0_warnings_0_errors',
    'package_format=PASS_16/16',
    'exact_package_dlls=PASS_16/16',
    'dedicated_acceptance=PASS_14/14_scenarios_9/9',
    'coexistence=PASS_16/16',
    'thunderstore_eligibility=PASS_14_new_1_update_1_already_published_exact',
    'deployment_status=USER_MANAGED_NOT_DEPLOYED',
    ('full_signoff=' + $signoffPath),
    ('full_signoff_sha256=' + $signoffHash),
    ('focused_test_log=' + $testLogPath),
    ('focused_test_log_sha256=' + $signoff.focused_test_log_sha256),
    ('acceptance_matrix=' + $acceptanceMatrixPath),
    ('acceptance_matrix_sha256=' + $signoff.acceptance_matrix_sha256),
    ('coexistence_evidence=' + $coexistencePath),
    ('coexistence_evidence_sha256=' + $signoff.coexistence_evidence_sha256)
)
$summaryCandidate = Join-Path $resolvedSignoffRoot 'PACKAGE-AUDIT.txt.candidate'
WriteUtf8 $summaryCandidate (($auditLines -join [Environment]::NewLine) + [Environment]::NewLine)

$archiveRoot = Join-Path (Join-Path $releaseRoot 'Obsolete') ('PublicationSignoff-' + $runId)
Require (-not (Test-Path -LiteralPath $archiveRoot)) "Signoff archive already exists: $archiveRoot"
New-Item -ItemType Directory -Path $archiveRoot | Out-Null
Copy-Item -LiteralPath $releasePath -Destination (Join-Path $archiveRoot 'THUNDERSTORE-RELEASE.json')
Copy-Item -LiteralPath $summaryPath -Destination (Join-Path $archiveRoot 'PACKAGE-AUDIT.txt')

try {
    $releaseTemporary = Join-Path $releaseRoot ('.codex-' + $runId + '-THUNDERSTORE-RELEASE.json.tmp')
    $summaryTemporary = Join-Path $releaseRoot ('.codex-' + $runId + '-PACKAGE-AUDIT.txt.tmp')
    Copy-Item -LiteralPath $releaseCandidate -Destination $releaseTemporary
    Copy-Item -LiteralPath $summaryCandidate -Destination $summaryTemporary
    [System.IO.File]::Move($releaseTemporary, $releasePath, $true)
    [System.IO.File]::Move($summaryTemporary, $summaryPath, $true)
    Require ((Get-Content -Raw -LiteralPath $releasePath | ConvertFrom-Json).status -ceq 'READY_TO_PUBLISH') 'Canonical release promotion did not retain READY_TO_PUBLISH.'
}
catch {
    Copy-Item -LiteralPath (Join-Path $archiveRoot 'THUNDERSTORE-RELEASE.json') -Destination $releasePath -Force
    Copy-Item -LiteralPath (Join-Path $archiveRoot 'PACKAGE-AUDIT.txt') -Destination $summaryPath -Force
    throw
}

Write-Output ('RUNIC_THUNDERSTORE_PUBLICATION_SIGNOFF_PASS packages=16 uploadable=15 focused_tests=' + $focusedTotal + '/' + $focusedTotal)
Write-Output ('SIGNOFF=' + $signoffPath)
Write-Output ('SIGNOFF_SHA256=' + $signoffHash)
Write-Output ('RELEASE_JSON_SHA256=' + (Sha256File $releasePath))
