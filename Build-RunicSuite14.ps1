[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\RunicSuite14'))
$modules = @(
    @{ Name = 'RunicStorage'; Directory = 'RunicStorage'; Icon = 'icon.png' }
    @{ Name = 'RunicCrafting'; Directory = 'RunicCrafting'; Icon = 'icon.png' }
    @{ Name = 'RunicAgriculture'; Directory = 'RunicAgriculture'; Icon = 'icon.png' }
    @{ Name = 'RunicProduction'; Directory = 'RunicProduction'; Icon = 'icon.png' }
    @{ Name = 'RunicPrecisionBuildTool'; Directory = 'RunicPrecisionBuildTool'; Icon = 'media\icon.png' }
    @{ Name = 'RunicInventory'; Directory = 'RunicInventory'; Icon = 'icon.png' }
    @{ Name = 'RunicPortals'; Directory = 'RunicPortals'; Icon = 'icon.png' }
    @{ Name = 'RunicExploration'; Directory = 'RunicExploration'; Icon = 'icon.png' }
    @{ Name = 'RunicAwareness'; Directory = 'RunicAwareness'; Icon = 'icon.png' }
    @{ Name = 'RunicInteraction'; Directory = 'RunicInteraction'; Icon = 'icon.png' }
    @{ Name = 'RunicSafety'; Directory = 'RunicSafety'; Icon = 'icon.png' }
    @{ Name = 'RunicVelocity'; Directory = 'RunicVelocity'; Icon = 'icon.png' }
    @{ Name = 'RunicSentinel'; Directory = 'RunicSentinel'; Icon = 'icon.png' }
    @{ Name = 'RunicWorldEngine'; Directory = 'RunicWorldEngine'; Icon = 'icon.png' }
)
$compatibilityCompanions = @(
    @{ Name = 'RunicBuildCamera'; Directory = 'RunicBuildCamera' }
    @{ Name = 'RunicIntegrity'; Directory = 'RunicIntegrity' }
)
$expectedAuditCounts = [ordered]@{
    'RunicCore\Tests\RunicCore.Tests.csproj' = 29
    'RunicPermissions\Tests\RunicPermissions.Tests.csproj' = 44
    'RunicTransactions\Tests\RunicTransactions.Tests.csproj' = 39
    'RunicPersistence.Tests\RunicPersistence.Tests.csproj' = 61
    'RunicFoundation.Tests\RunicFoundation.Tests.csproj' = 7
    'RunicStorage\Tests\RunicStorage.Tests.csproj' = 38
    'RunicCrafting\Tests\RunicCrafting.Tests.csproj' = 27
    'RunicAgriculture\Tests\RunicAgriculture.Tests.csproj' = 49
    'RunicProduction\Tests\RunicProduction.Tests.csproj' = 150
    'RunicPrecisionBuildTool.Tests\RunicPrecisionBuildTool.Tests.csproj' = 144
    'RunicInventory.Tests\RunicInventory.Tests.csproj' = 120
    'RunicPortals.Tests\RunicPortals.Tests.csproj' = 226
    'RunicExploration.Tests\RunicExploration.Tests.csproj' = 52
    'RunicAwareness.Tests\RunicAwareness.Tests.csproj' = 68
    'RunicInteraction.Tests\RunicInteraction.Tests.csproj' = 70
    'RunicSafety.Tests\RunicSafety.Tests.csproj' = 197
    'RunicVelocity.Tests\RunicVelocity.Tests.csproj' = 19
    'RunicSentinel.Tests\RunicSentinel.Tests.csproj' = 29
    'RunicWorldEngine.Tests\RunicWorldEngine.Tests.csproj' = 15
    'RunicGameplayWave1.Tests\RunicGameplayWave1.Tests.csproj' = 21
    'RunicBuildCamera.Tests\RunicBuildCamera.Tests.csproj' = 71
    'RunicIntegrity.Tests\RunicIntegrity.Tests.csproj' = 6
    'RunicSuite14.Tests\RunicSuite14.Tests.csproj' = 22
}

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-StreamSha256Hex {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($algorithm.ComputeHash($Stream) | ForEach-Object { $_.ToString('X2') }) -join '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-FileSha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return Get-StreamSha256Hex -Stream $stream
    }
    finally {
        $stream.Dispose()
    }
}

function Test-DirectChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )
    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)
    return [string]::Equals(
        (Split-Path -Parent $resolvedChild),
        $resolvedParent,
        [StringComparison]::OrdinalIgnoreCase)
}

function Get-CollisionSafeObsoletePath {
    param(
        [Parameter(Mandatory = $true)][string]$ObsoleteRoot,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$SourceHash
    )
    $extension = [System.IO.Path]::GetExtension($SourcePath)
    $sourceLeaf = [System.IO.Path]::GetFileNameWithoutExtension($SourcePath)
    if ([string]::IsNullOrWhiteSpace($sourceLeaf) -or
        [string]::IsNullOrWhiteSpace($extension) -or
        $SourceHash -notmatch '^[0-9A-F]{64}$') {
        throw "Cannot construct a safe obsolete-artifact identity for $SourcePath."
    }
    $timestamp = [DateTime]::UtcNow.ToString(
        'yyyyMMddTHHmmssfffZ',
        [Globalization.CultureInfo]::InvariantCulture)
    for ($attempt = 0; $attempt -lt 32; $attempt++) {
        $nonce = [Guid]::NewGuid().ToString('N').Substring(0, 12)
        $leaf = $sourceLeaf + '.superseded-' + $timestamp + '-' +
            $SourceHash.Substring(0, 12) + '-' + $nonce + $extension
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $ObsoleteRoot $leaf))
        if (-not (Test-DirectChildPath -Parent $ObsoleteRoot -Child $candidate)) {
            throw "Refusing to archive an artifact outside the Obsolete directory: $candidate"
        }
        if (-not (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    throw "Could not allocate a collision-safe obsolete-artifact name for $SourcePath."
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )
    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-SourceTreeEvidence {
    param([Parameter(Mandatory = $true)][string]$Root)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force) {
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Source snapshot contains a reparse-point file: $($file.FullName)"
        }
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Source snapshot escaped the repository root: $fullPath"
        }
        $relative = $fullPath.Substring($prefix.Length).Replace('\', '/')
        $parts = @($relative.Split('/'))
        $excluded = $false
        foreach ($part in $parts) {
            if (@('.git', '.vs', 'artifacts', 'bin', 'obj', 'TestResults') -ccontains $part) {
                $excluded = $true
                break
            }
        }
        if ($excluded) { continue }
        $records.Add($relative + "`0" + (Get-FileSha256Hex -Path $fullPath))
    }
    $recordArray = $records.ToArray()
    [Array]::Sort($recordArray, [StringComparer]::Ordinal)
    $memory = [System.IO.MemoryStream]::new()
    try {
        foreach ($record in $recordArray) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($record + "`n")
            $memory.Write($bytes, 0, $bytes.Length)
        }
        $memory.Position = 0
        $digest = Get-StreamSha256Hex -Stream $memory
    }
    finally { $memory.Dispose() }
    return [pscustomobject][ordered]@{
        algorithm = 'SHA256(relative-path NUL file-SHA256 LF; ordinal path order)'
        excluded_path_components = @('.git', '.vs', 'artifacts', 'bin', 'obj', 'TestResults')
        file_count = $recordArray.Count
        digest = $digest
    }
}

function Export-ValidatedZipEntry {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$ExpectedHash
    )
    if (Test-Path -LiteralPath $Destination) {
        throw "Refusing to overwrite a staged payload entry: $Destination"
    }
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $EntryName })
        if ($matches.Count -ne 1) {
            throw "$(Split-Path -Leaf $ZipPath) must contain exactly one $EntryName."
        }
        $source = $matches[0].Open()
        try {
            $destinationStream = [System.IO.File]::Open(
                $Destination,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            try { $source.CopyTo($destinationStream) }
            finally { $destinationStream.Dispose() }
        }
        finally { $source.Dispose() }
    }
    finally { $archive.Dispose() }
    $actualHash = Get-FileSha256Hex -Path $Destination
    if ($actualHash -cne $ExpectedHash) {
        throw "Extracted payload $EntryName differs from its validated ZIP entry."
    }
}

function New-DeterministicTreeZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Identity
    )
    $root = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $root + [System.IO.Path]::DirectorySeparatorChar
    $entryMap = @{}
    $entryNames = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Evidence tree contains a reparse-point file: $($file.FullName)"
        }
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Evidence tree escaped its root: $fullPath"
        }
        $entryName = $fullPath.Substring($prefix.Length).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($entryName) -or
            $entryName.StartsWith('/', [StringComparison]::Ordinal) -or
            $entryName.IndexOf('../', [StringComparison]::Ordinal) -ge 0) {
            throw "Unsafe evidence ZIP entry: $entryName"
        }
        $entryMap[$entryName] = $fullPath
        $entryNames.Add($entryName)
    }
    if ($entryNames.Count -eq 0) {
        throw "Evidence directory is empty: $root"
    }
    $entries = $entryNames.ToArray()
    [Array]::Sort($entries, [StringComparer]::Ordinal)
    $identityParts = [System.Collections.Generic.List[string]]::new()
    foreach ($entryName in $entries) {
        $identityParts.Add($entryName + '=' + (Get-FileSha256Hex -Path $entryMap[$entryName]))
    }
    $timestamp = Get-DeterministicZipTime -PackageIdentity (
        $Identity + '|' + [string]::Join('|', $identityParts))
    $stream = [System.IO.File]::Open(
        $ZipPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($entryName in $entries) {
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $source = [System.IO.File]::OpenRead($entryMap[$entryName])
                try {
                    $destination = $entry.Open()
                    try { $source.CopyTo($destination) }
                    finally { $destination.Dispose() }
                }
                finally { $source.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    $verification = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $actualEntries = @($verification.Entries | ForEach-Object { $_.FullName })
        if ($actualEntries.Count -ne $entries.Count) {
            throw "Evidence ZIP entry count drifted."
        }
        foreach ($entryName in $entries) {
            $matches = @($verification.Entries | Where-Object { $_.FullName -ceq $entryName })
            if ($matches.Count -ne 1) {
                throw "Evidence ZIP must contain exactly one $entryName."
            }
            $entryStream = $matches[0].Open()
            try { $zipHash = Get-StreamSha256Hex -Stream $entryStream }
            finally { $entryStream.Dispose() }
            if ($zipHash -cne (Get-FileSha256Hex -Path $entryMap[$entryName])) {
                throw "Evidence ZIP entry $entryName differs from its source."
            }
        }
    }
    finally { $verification.Dispose() }
}

function Assert-AuditTranscript {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ExpectedCounts
    )
    $observed = @{}
    $passedProjects = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $current = $null
    $persistenceSummarySeen = $false
    foreach ($rawLine in $Lines) {
        $line = ([string]$rawLine).Trim()
        if ($line -match '^AUDIT_START (?<project>.+)$') {
            if ($null -ne $current) {
                throw "Nested audit transcript section at $line."
            }
            $current = [string]$Matches.project
            if (-not $ExpectedCounts.Contains($current) -or $observed.ContainsKey($current)) {
                throw "Unexpected or duplicate audit project in transcript: $current"
            }
            $observed[$current] = 0
            continue
        }
        if ($line -match '^PASS\s+' -and $null -ne $current) {
            # Windows PowerShell can mojibake the Unicode dash in Persistence's
            # one known summary. Exclude only that exact project/count/suffix;
            # every other PASS line remains part of the strict count.
            if ($current -ceq 'RunicPersistence.Tests\RunicPersistence.Tests.csproj' -and
                $line -match '^PASS\s+[^A-Za-z0-9]*(?<passed>\d+)/(?<total>\d+)\s+Runic Persistence foundation tests$') {
                if ($persistenceSummarySeen) {
                    throw 'Runic Persistence emitted a duplicate focused-test summary.'
                }
                if ([int]$Matches.passed -ne [int]$ExpectedCounts[$current] -or
                    [int]$Matches.total -ne [int]$ExpectedCounts[$current]) {
                    throw "Runic Persistence summary drifted; expected $($ExpectedCounts[$current])/$($ExpectedCounts[$current]), observed $($Matches.passed)/$($Matches.total)."
                }
                $persistenceSummarySeen = $true
                continue
            }
            $observed[$current] = [int]$observed[$current] + 1
            continue
        }
        if ($line -match '^AUDIT_PASS (?<project>.+)$') {
            $project = [string]$Matches.project
            if ($null -eq $current -or $project -cne $current) {
                throw "Mismatched audit completion marker: $line"
            }
            if ([int]$observed[$project] -ne [int]$ExpectedCounts[$project]) {
                throw "$project passed-count drifted; expected $($ExpectedCounts[$project]), observed $($observed[$project])."
            }
            if ($project -ceq 'RunicPersistence.Tests\RunicPersistence.Tests.csproj' -and
                -not $persistenceSummarySeen) {
                throw 'Runic Persistence transcript is missing its exact focused-test summary.'
            }
            if (-not $passedProjects.Add($project)) {
                throw "Duplicate audit completion marker: $project"
            }
            $current = $null
        }
    }
    if ($null -ne $current) {
        throw "Audit transcript ended inside $current."
    }
    if ($passedProjects.Count -ne $ExpectedCounts.Count) {
        throw "Audit transcript covered $($passedProjects.Count) projects; expected $($ExpectedCounts.Count)."
    }
    $expectedFinal = 'RUNIC_SUITE_14_AUDIT_PASS projects=' + $ExpectedCounts.Count
    if (-not @($Lines | ForEach-Object { ([string]$_).Trim() } |
            Where-Object { $_ -ceq $expectedFinal })) {
        throw "Audit transcript is missing its exact final pass marker: $expectedFinal"
    }
}

function Get-ConfiguredPropertyPath {
    param(
        [Parameter(Mandatory = $true)][string]$EnvironmentName,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )
    $environmentValue = [Environment]::GetEnvironmentVariable($EnvironmentName)
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return [System.IO.Path]::GetFullPath($environmentValue)
    }
    $projectText = Get-Content -LiteralPath $ProjectPath -Raw
    $pattern = '<' + [regex]::Escape($PropertyName) + '[^>]*>(?<path>[^<]+)</' +
        [regex]::Escape($PropertyName) + '>'
    $match = [regex]::Match($projectText, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Could not resolve $PropertyName from $ProjectPath."
    }
    return [System.IO.Path]::GetFullPath($match.Groups['path'].Value.Trim())
}

function Get-EnvironmentEvidence {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
    $clientRoot = Get-ConfiguredPropertyPath `
        -EnvironmentName 'VALHEIM_INSTALL' `
        -ProjectPath (Join-Path $RepositoryRoot 'RunicStorage\RunicStorage.csproj') `
        -PropertyName 'VALHEIM_INSTALL'
    $bepRoot = Get-ConfiguredPropertyPath `
        -EnvironmentName 'BEPINEX_PROFILE' `
        -ProjectPath (Join-Path $RepositoryRoot 'RunicSuite14.Tests\RunicSuite14.Tests.csproj') `
        -PropertyName 'BEPINEX_PROFILE'
    $serverRoot = [Environment]::GetEnvironmentVariable('VALHEIM_DEDICATED_INSTALL')
    if ([string]::IsNullOrWhiteSpace($serverRoot)) {
        $serverRoot = 'E:\SteamLibrary\steamapps\common\Valheim dedicated server'
    }
    $serverRoot = [System.IO.Path]::GetFullPath($serverRoot)
    $specifications = @(
        @{ Label = 'client/assembly_valheim.dll'; Path = (Join-Path $clientRoot 'valheim_Data\Managed\assembly_valheim.dll') }
        @{ Label = 'dedicated/assembly_valheim.dll'; Path = (Join-Path $serverRoot 'valheim_server_Data\Managed\assembly_valheim.dll') }
        @{ Label = 'dedicated/valheim_server.exe'; Path = (Join-Path $serverRoot 'valheim_server.exe') }
        @{ Label = 'audit-bepinex/BepInEx.dll'; Path = (Join-Path $bepRoot 'core\BepInEx.dll') }
        @{ Label = 'audit-bepinex/0Harmony.dll'; Path = (Join-Path $bepRoot 'core\0Harmony.dll') }
        @{ Label = 'audit-bepinex/Mono.Cecil.dll'; Path = (Join-Path $bepRoot 'core\Mono.Cecil.dll') }
    )
    $catalog = @()
    foreach ($specification in $specifications) {
        $path = [System.IO.Path]::GetFullPath([string]$specification.Path)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Audited environment file is missing: $path"
        }
        $info = Get-Item -LiteralPath $path
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        $catalog += [pscustomobject][ordered]@{
            label = [string]$specification.Label
            sha256 = Get-FileSha256Hex -Path $path
            bytes = [int64]$info.Length
            file_version = if ([string]::IsNullOrWhiteSpace($version)) { $null } else { $version }
        }
    }
    return $catalog
}

function Get-DeterministicZipTime {
    param([Parameter(Mandatory = $true)][string]$PackageIdentity)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($PackageIdentity)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = $algorithm.ComputeHash($bytes)
    }
    finally {
        $algorithm.Dispose()
    }
    [uint64]$slots = [System.BitConverter]::ToUInt32($digest, 0)
    $slots %= [uint64]315360000
    return [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero).AddSeconds([double]($slots * 2))
}

function Assert-ExactRootEntries {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($Actual.Count -ne $Expected.Count) {
        throw "$Label entry count drifted; expected $($Expected.Count), found $($Actual.Count)."
    }
    foreach ($entry in $Actual) {
        if ($entry.IndexOf('/') -ge 0 -or $entry.IndexOf('\') -ge 0) {
            throw "$Label contains a non-root entry: $entry"
        }
        if ($Expected -cnotcontains $entry) {
            throw "$Label contains an unexpected entry: $entry"
        }
    }
    foreach ($entry in $Expected) {
        if ($Actual -cnotcontains $entry) {
            throw "$Label is missing entry: $entry"
        }
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$PackageStage,
        [Parameter(Mandatory = $true)][string[]]$Entries,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Identity
    )
    $timestampParts = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $Entries) {
        $timestampParts.Add($entry + '=' + (Get-FileSha256Hex -Path (Join-Path $PackageStage $entry)))
    }
    $timestamp = Get-DeterministicZipTime -PackageIdentity ($Identity + '|' + [string]::Join('|', $timestampParts))
    $stream = [System.IO.File]::Open(
        $ZipPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($entryName in $Entries) {
                $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $source = [System.IO.File]::OpenRead((Join-Path $PackageStage $entryName))
                try {
                    $destination = $entry.Open()
                    try { $source.CopyTo($destination) }
                    finally { $destination.Dispose() }
                }
                finally { $source.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-Package {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$PackageStage,
        [Parameter(Mandatory = $true)][string[]]$Entries,
        [Parameter(Mandatory = $true)][string]$ModuleName
    )
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $actual = @($archive.Entries | ForEach-Object { $_.FullName })
        Assert-ExactRootEntries -Actual $actual -Expected $Entries -Label (Split-Path -Leaf $ZipPath)
        $dlls = @($archive.Entries | Where-Object {
            [System.IO.Path]::GetExtension($_.FullName) -ieq '.dll'
        })
        if ($dlls.Count -ne 1 -or $dlls[0].FullName -cne ($ModuleName + '.dll')) {
            throw "$(Split-Path -Leaf $ZipPath) must contain exactly its own root DLL."
        }
        foreach ($entryName in $Entries) {
            $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $entryName })
            if ($matches.Count -ne 1) {
                throw "$(Split-Path -Leaf $ZipPath) must contain exactly one $entryName."
            }
            $stream = $matches[0].Open()
            try { $actualHash = Get-StreamSha256Hex -Stream $stream }
            finally { $stream.Dispose() }
            $expectedHash = Get-FileSha256Hex -Path (Join-Path $PackageStage $entryName)
            if ($actualHash -cne $expectedHash) {
                throw "$(Split-Path -Leaf $ZipPath) entry $entryName differs from the validated source."
            }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-FoundationPackagesMatch {
    param([Parameter(Mandatory = $true)][object[]]$Manifests)
    $dependencies = @($Manifests | ForEach-Object { @($_.dependencies) } | Where-Object {
        $_ -match '^Chazman-(RunicCore|RunicPermissions|RunicTransactions|RunicPersistence)-[0-9]+\.[0-9]+\.[0-9]+$'
    } | Sort-Object -Unique)
    $expectedNames = @('RunicCore', 'RunicPermissions', 'RunicTransactions', 'RunicPersistence')
    $actualNames = @($dependencies | ForEach-Object {
        if ($_ -notmatch '^Chazman-(?<name>Runic[^-]+)-') {
            throw "Invalid Foundation dependency identity: $_"
        }
        [string]$Matches.name
    })
    if ($dependencies.Count -ne $expectedNames.Count) {
        throw "Suite manifests must collectively pin exactly four Foundation packages."
    }
    foreach ($expectedName in $expectedNames) {
        if ($actualNames -cnotcontains $expectedName) {
            throw "Suite manifests do not pin required Foundation package $expectedName."
        }
    }
    $validated = @()
    foreach ($dependency in $dependencies) {
        if ($dependency -notmatch '^Chazman-(?<name>Runic[^-]+)-(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') {
            throw "Invalid Foundation dependency identity: $dependency"
        }
        $name = $Matches.name
        $version = $Matches.version
        $zip = Join-Path $repoRoot ('artifacts\RunicFoundation\' + $dependency + '.zip')
        $sourceRoot = Join-Path $repoRoot $name
        $dll = Join-Path $sourceRoot ('bin\Release\netstandard2.1\' + $name + '.dll')
        if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
            throw "Validated Foundation package is missing: $zip"
        }
        if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
            throw "Foundation Release DLL is missing: $dll"
        }
        $sources = [ordered]@{
            ($name + '.dll') = $dll
            'manifest.json' = (Join-Path $sourceRoot 'manifest.json')
            'README.md' = (Join-Path $sourceRoot 'README.md')
            'icon.png' = (Join-Path $sourceRoot 'icon.png')
        }
        foreach ($optionalName in @('CHANGELOG.md', ($name + '.cfg.example'))) {
            $optionalPath = Join-Path $sourceRoot $optionalName
            if (Test-Path -LiteralPath $optionalPath -PathType Leaf) {
                $sources[$optionalName] = $optionalPath
            }
        }
        foreach ($source in $sources.GetEnumerator()) {
            if (-not (Test-Path -LiteralPath $source.Value -PathType Leaf)) {
                throw "$dependency current source is missing: $($source.Value)"
            }
        }
        $currentManifest = Get-Content -LiteralPath $sources['manifest.json'] -Raw | ConvertFrom-Json
        if ([string]$currentManifest.name -cne $name -or
            [string]$currentManifest.version_number -cne $version) {
            throw "$dependency does not match the current Foundation manifest identity."
        }
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try {
            $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
            Assert-ExactRootEntries -Actual $entryNames -Expected @($sources.Keys) -Label $dependency
            $dllEntries = @($archive.Entries | Where-Object {
                [System.IO.Path]::GetExtension($_.FullName) -ieq '.dll'
            })
            if ($dllEntries.Count -ne 1 -or $dllEntries[0].FullName -cne ($name + '.dll')) {
                throw "$dependency must contain exactly one root $name.dll and no other DLL."
            }
            foreach ($source in $sources.GetEnumerator()) {
                $matches = @($archive.Entries | Where-Object { $_.FullName -ceq $source.Key })
                if ($matches.Count -ne 1) {
                    throw "$dependency must contain exactly one $($source.Key)."
                }
                $stream = $matches[0].Open()
                try { $packagedHash = Get-StreamSha256Hex -Stream $stream }
                finally { $stream.Dispose() }
                $sourceHash = Get-FileSha256Hex -Path $source.Value
                if ($packagedHash -cne $sourceHash) {
                    throw "$dependency entry $($source.Key) does not match current Foundation source."
                }
            }
        }
        finally { $archive.Dispose() }
        $validated += [pscustomobject][ordered]@{
            Name = $name
            Version = $version
            Zip = [System.IO.Path]::GetFullPath($zip)
            ZipHash = Get-FileSha256Hex -Path $zip
            DllEntry = $name + '.dll'
            DllHash = Get-FileSha256Hex -Path $dll
        }
    }
    return $validated
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($module in $modules) {
    $project = Join-Path $repoRoot ($module.Directory + '\' + $module.Name + '.csproj')
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Suite project is missing: $project"
    }
    Invoke-Dotnet @('build', $project, '-c', 'Release', '-p:TreatWarningsAsErrors=true')
}
foreach ($companion in $compatibilityCompanions) {
    $project = Join-Path $repoRoot ($companion.Directory + '\' + $companion.Name + '.csproj')
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Compatibility-companion project is missing: $project"
    }
    Invoke-Dotnet @('build', $project, '-c', 'Release', '-p:TreatWarningsAsErrors=true')
}

# Prove that the complete gameplay payload is reproducible across two independent
# compiler invocations. Creating two archives from one staged directory checks the
# ZIP writer, but it cannot detect a nondeterministic build; the two Rebuild passes
# below close that gap before any package source is staged.
$firstRebuildHashes = @{}
foreach ($module in $modules) {
    $project = Join-Path $repoRoot ($module.Directory + '\' + $module.Name + '.csproj')
    Invoke-Dotnet @('build', $project, '-c', 'Release', '-t:Rebuild', '--no-restore', '-p:TreatWarningsAsErrors=true')
    $dll = Join-Path $repoRoot ($module.Directory + '\bin\Release\netstandard2.1\' + $module.Name + '.dll')
    $firstRebuildHashes[[string]$module.Name] = Get-FileSha256Hex -Path $dll
}
$secondRebuildHashes = @{}
foreach ($module in $modules) {
    $project = Join-Path $repoRoot ($module.Directory + '\' + $module.Name + '.csproj')
    Invoke-Dotnet @('build', $project, '-c', 'Release', '-t:Rebuild', '--no-restore', '-p:TreatWarningsAsErrors=true')
    $dll = Join-Path $repoRoot ($module.Directory + '\bin\Release\netstandard2.1\' + $module.Name + '.dll')
    $secondHash = Get-FileSha256Hex -Path $dll
    $firstHash = [string]$firstRebuildHashes[[string]$module.Name]
    if ($secondHash -cne $firstHash) {
        throw "$($module.Name) Release build is not reproducible: $firstHash != $secondHash"
    }
    $secondRebuildHashes[[string]$module.Name] = $secondHash
    Write-Output "REPRODUCIBLE_BUILD $($module.Name) DLL_SHA256=$secondHash"
}

# Build the two always-present compatibility participants immediately before the
# unified audit and freeze their bytes. They are not part of the fourteen-package
# identity, but the dedicated smoke must exercise the exact binaries just audited.
$companionHashes = @{}
foreach ($companion in $compatibilityCompanions) {
    $project = Join-Path $repoRoot ($companion.Directory + '\' + $companion.Name + '.csproj')
    Invoke-Dotnet @('build', $project, '-c', 'Release', '-t:Rebuild', '--no-restore', '-p:TreatWarningsAsErrors=true')
    $dll = Join-Path $repoRoot ($companion.Directory + '\bin\Release\netstandard2.1\' + $companion.Name + '.dll')
    $companionHashes[[string]$companion.Name] = Get-FileSha256Hex -Path $dll
    Write-Output "FROZEN_COMPANION $($companion.Name) DLL_SHA256=$($companionHashes[[string]$companion.Name])"
}

# All staging, including -SkipTests validation, lives under the OS temporary
# directory. The release artifact tree is not created or changed until every
# audit, package, true-ready dedicated smoke, and evidence gate has passed.
$stageParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$stageRoot = [System.IO.Path]::GetFullPath((Join-Path $stageParent (
    'runic-suite14-stage-' + [Guid]::NewGuid().ToString('N'))))
if (-not (Test-DirectChildPath -Parent $stageParent -Child $stageRoot) -or
    -not (Split-Path -Leaf $stageRoot).StartsWith('runic-suite14-stage-', [StringComparison]::Ordinal)) {
    throw "Refusing to create an unsafe suite stage: $stageRoot"
}
New-Item -ItemType Directory -Path $stageRoot | Out-Null

# Audit the exact second-rebuild outputs that are about to be staged. The audit
# runner may build its own test harnesses, so prove afterward that none of those
# builds replaced a gameplay payload before packaging.
try {
    $auditLines = @()
    $auditLogPath = Join-Path $stageRoot 'UNIFIED-AUDIT.log'
    if (-not $SkipTests) {
        $auditArguments = @('-NoProfile', '-File', (Join-Path $repoRoot 'Test-RunicSuite14.ps1'))
        $auditOutput = @(& powershell @auditArguments 2>&1)
        $auditExitCode = $LASTEXITCODE
        $auditLines = @($auditOutput | ForEach-Object { [string]$_ })
        foreach ($auditLine in $auditLines) { Write-Output $auditLine }
        if ($auditExitCode -ne 0) {
            throw "The unified Runic Suite 14 audit failed with exit code $auditExitCode."
        }
        Assert-AuditTranscript -Lines $auditLines -ExpectedCounts $expectedAuditCounts
        [System.IO.File]::WriteAllLines(
            $auditLogPath,
            $auditLines,
            [System.Text.UTF8Encoding]::new($false))
        foreach ($module in $modules) {
            $dll = Join-Path $repoRoot ($module.Directory + '\bin\Release\netstandard2.1\' + $module.Name + '.dll')
            $auditedHash = Get-FileSha256Hex -Path $dll
            $expectedHash = [string]$firstRebuildHashes[[string]$module.Name]
            if ($auditedHash -cne $expectedHash) {
                throw "$($module.Name) changed after its reproducible rebuild and before packaging: $expectedHash != $auditedHash"
            }
            Write-Output "AUDITED_PAYLOAD $($module.Name) DLL_SHA256=$auditedHash"
        }
        foreach ($companion in $compatibilityCompanions) {
            $dll = Join-Path $repoRoot ($companion.Directory + '\bin\Release\netstandard2.1\' + $companion.Name + '.dll')
            $auditedHash = Get-FileSha256Hex -Path $dll
            $expectedHash = [string]$companionHashes[[string]$companion.Name]
            if ($auditedHash -cne $expectedHash) {
                throw "$($companion.Name) changed between its frozen rebuild and unified audit: $expectedHash != $auditedHash"
            }
            Write-Output "AUDITED_COMPANION $($companion.Name) DLL_SHA256=$auditedHash"
        }
    }

    $manifests = @()
    foreach ($module in $modules) {
        $manifestPath = Join-Path $repoRoot ($module.Directory + '\manifest.json')
        $manifests += Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    $foundationPackages = @(Assert-FoundationPackagesMatch -Manifests $manifests)
    if ($foundationPackages.Count -ne 4 -or
        @($foundationPackages | Where-Object {
            $_ -isnot [pscustomobject] -or [string]::IsNullOrWhiteSpace([string]$_.Name)
        }).Count -ne 0) {
        throw "Foundation admission must return four individual validated package records."
    }

    $pending = @()
    foreach ($module in $modules) {
        $name = [string]$module.Name
        $sourceRoot = Join-Path $repoRoot ([string]$module.Directory)
        $manifestPath = Join-Path $sourceRoot 'manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.name -cne $name) {
            throw "$name manifest package identity drifted to $($manifest.name)."
        }
        $version = [string]$manifest.version_number
        if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
            throw "$name manifest has invalid semantic version $version."
        }

        $packageStage = Join-Path $stageRoot $name
        New-Item -ItemType Directory -Path $packageStage | Out-Null
        $sourceDll = Join-Path $sourceRoot ('bin\Release\netstandard2.1\' + $name + '.dll')
        $sources = [ordered]@{
            ($name + '.dll') = $sourceDll
            'manifest.json' = $manifestPath
            'README.md' = (Join-Path $sourceRoot 'README.md')
            'icon.png' = (Join-Path $sourceRoot ([string]$module.Icon))
            'CHANGELOG.md' = (Join-Path $sourceRoot 'CHANGELOG.md')
            ($name + '.cfg.example') = (Join-Path $sourceRoot ($name + '.cfg.example'))
        }
        foreach ($entry in $sources.GetEnumerator()) {
            if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
                throw "$name package source is missing: $($entry.Value)"
            }
            Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $packageStage $entry.Key)
        }
        $entries = @($sources.Keys)
        Assert-ExactRootEntries -Actual @(
            Get-ChildItem -LiteralPath $packageStage -File | ForEach-Object { $_.Name }
        ) -Expected $entries -Label "$name package stage"

        $icon = [System.Drawing.Image]::FromFile((Join-Path $packageStage 'icon.png'))
        try {
            if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
                throw "$name icon must be exactly 256x256 pixels."
            }
        }
        finally { $icon.Dispose() }

        $identity = [System.Reflection.AssemblyName]::GetAssemblyName($sourceDll)
        if ($identity.Name -cne $name -or $identity.Version.ToString() -cne ($version + '.0')) {
            throw "$name assembly identity $($identity.FullName) does not match package version $version."
        }

        $zipName = 'Chazman-' + $name + '-' + $version + '.zip'
        $first = Join-Path $stageRoot ($name + '.first.zip')
        $second = Join-Path $stageRoot $zipName
        New-DeterministicZip -PackageStage $packageStage -Entries $entries -ZipPath $first -Identity $zipName
        New-DeterministicZip -PackageStage $packageStage -Entries $entries -ZipPath $second -Identity $zipName
        Assert-Package -ZipPath $first -PackageStage $packageStage -Entries $entries -ModuleName $name
        Assert-Package -ZipPath $second -PackageStage $packageStage -Entries $entries -ModuleName $name
        $firstHash = Get-FileSha256Hex -Path $first
        $secondHash = Get-FileSha256Hex -Path $second
        if ($firstHash -cne $secondHash) {
            throw "$zipName is not reproducible: $firstHash != $secondHash"
        }
        $dllHash = Get-FileSha256Hex -Path $sourceDll
        $pending += @{
            Name = $name
            Version = $version
            Source = $second
            PackageStage = $packageStage
            DllSource = (Join-Path $packageStage ($name + '.dll'))
            Destination = (Join-Path $artifactRoot $zipName)
            ZipHash = $secondHash
            DllHash = $dllHash
            FirstBuildDllHash = [string]$firstRebuildHashes[$name]
            SecondBuildDllHash = [string]$secondRebuildHashes[$name]
            FirstZipHash = $firstHash
            SecondZipHash = $secondHash
        }
        Write-Output "VALIDATED $zipName ZIP_SHA256=$secondHash DLL_SHA256=$dllHash"
    }

    if ($SkipTests) {
        Write-Output 'Validated all fourteen packages without promotion because -SkipTests was requested.'
    }
    else {
        $parityGatePath = Join-Path $repoRoot 'RUNIC_DEDICATED_PARITY.md'
        if (-not (Test-Path -LiteralPath $parityGatePath -PathType Leaf)) {
            throw "The dedicated-player parity gate is missing: $parityGatePath"
        }
        $parityGateText = Get-Content -LiteralPath $parityGatePath -Raw
        if (-not [regex]::IsMatch(
                $parityGateText,
                '(?m)^Status: \*\*GO - exact 18-module dedicated and remote-player parity verified\*\*$')) {
            throw "Dedicated-player release is blocked until RUNIC_DEDICATED_PARITY.md records exact 18-module GO."
        }
        $parityMatrixMatch = [regex]::Match(
            $parityGateText,
            '(?s)## Required feature matrix\s+(?<matrix>.*?)\s+## Network and compatibility acceptance')
        if (-not $parityMatrixMatch.Success) {
            throw "The dedicated-player parity matrix is missing or malformed."
        }
        $parityMatrixText = $parityMatrixMatch.Groups['matrix'].Value
        if ([regex]::IsMatch(
                $parityMatrixText,
                '\*\*(OPEN|PARTIAL|IN PROGRESS|SOURCE CHECKPOINT|HOLD)[^*]*\*\*')) {
            throw "Dedicated-player release is blocked by an incomplete module row in the parity matrix."
        }
        $parityGoRows = [regex]::Matches($parityMatrixText, '(?m)^\| [^|]+ \|[^\r\n]+\| \*\*GO\*\* \|').Count
        if ($parityGoRows -ne 18) {
            throw "The dedicated-player parity matrix must contain exactly 18 GO module rows; found $parityGoRows."
        }

        $sourceReportPath = Join-Path $repoRoot 'RUNIC_SUITE_14_AUDIT.md'
        if (-not (Test-Path -LiteralPath $sourceReportPath -PathType Leaf)) {
            throw "The source audit report is missing: $sourceReportPath"
        }
        $sourceReportText = Get-Content -LiteralPath $sourceReportPath -Raw
        if ([regex]::IsMatch(
                $sourceReportText,
                '\bpending\b',
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            throw "The source audit report is incomplete; promotion requires no pending fields."
        }
        $sourceFindingRows = [regex]::Matches(
            $sourceReportText,
            '(?m)^\| (P0|HIGH) \|')
        $sourceFindingDetails = [regex]::Matches(
            $sourceReportText,
            '(?m)^\| (?<severity>P0|HIGH) \| (?<finding>[^|]+) \| (?<remediation>[^|]+) \| (?<gate>[^|]+) \| (?<disposition>[^|]+) \|$')
        $sourceClosedRows = [regex]::Matches(
            $sourceReportText,
            '(?m)^\| (P0|HIGH) \|[^\r\n]+\| CLOSED at source gate \|$')
        $sourceArtifactHoldRows = [regex]::Matches(
            $sourceReportText,
            '(?m)^\| (P0|HIGH) \|[^\r\n]+\| IMPLEMENTATION REQUIRES (ARTIFACT|RUNTIME) CLOSURE; release HOLD \|$')
        $sourceReportingHoldRows = [regex]::Matches(
            $sourceReportText,
            '(?m)^\| HIGH \|[^\r\n]+\| CLOSED for reporting; release still HOLD \|$')
        if ($sourceFindingRows.Count -ne 14 -or
            $sourceFindingDetails.Count -ne 14 -or
            $sourceClosedRows.Count -ne 10 -or
            $sourceArtifactHoldRows.Count -ne 3 -or
            $sourceReportingHoldRows.Count -ne 1 -or
            [regex]::Matches(
                $sourceReportText,
                '(?m)^\*\*Source compatibility/security/performance audit: PASS\.\*\*').Count -ne 1 -or
            [regex]::Matches(
                $sourceReportText,
                '(?m)^\*\*Candidate release: HOLD\.\*\*').Count -ne 1) {
            throw "The source audit report's structured PASS/HOLD/finding-disposition schema drifted."
        }

        # Assemble the exact twenty-DLL runtime set without consulting an arbitrary
        # Release directory for any packaged component. Gameplay DLLs come from the
        # validated package stages, and Foundation DLLs are extracted from the exact
        # byte-for-byte validated dependency ZIPs.
        $smokePayloadRoot = Join-Path $stageRoot 'smoke-payload'
        New-Item -ItemType Directory -Path $smokePayloadRoot | Out-Null
        $runtimeDllEvidence = @()
        foreach ($package in $pending) {
            $payloadDll = Join-Path $smokePayloadRoot ($package.Name + '.dll')
            Copy-Item -LiteralPath $package.DllSource -Destination $payloadDll
            $payloadHash = Get-FileSha256Hex -Path $payloadDll
            if ($payloadHash -cne $package.DllHash) {
                throw "Smoke payload differs from staged package DLL for $($package.Name)."
            }
            $runtimeDllEvidence += [pscustomobject][ordered]@{
                name = [string]$package.Name
                role = 'gameplay'
                version = [string]$package.Version
                sha256 = $payloadHash
            }
        }
        foreach ($foundation in $foundationPackages) {
            $payloadDll = Join-Path $smokePayloadRoot ($foundation.Name + '.dll')
            Export-ValidatedZipEntry `
                -ZipPath $foundation.Zip `
                -EntryName $foundation.DllEntry `
                -Destination $payloadDll `
                -ExpectedHash $foundation.DllHash
            $runtimeDllEvidence += [pscustomobject][ordered]@{
                name = [string]$foundation.Name
                role = 'foundation'
                version = [string]$foundation.Version
                sha256 = [string]$foundation.DllHash
            }
        }
        $companionPackageEvidence = @()
        foreach ($companion in $compatibilityCompanions) {
            $name = [string]$companion.Name
            $sourceRoot = Join-Path $repoRoot ([string]$companion.Directory)
            $sourceDll = Join-Path $sourceRoot ('bin\Release\netstandard2.1\' + $name + '.dll')
            $manifest = Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Raw |
                ConvertFrom-Json
            if ([string]$manifest.name -cne $name -or
                [string]$manifest.version_number -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
                throw "$name companion manifest identity/version is invalid."
            }
            $expectedHash = [string]$companionHashes[$name]
            if ((Get-FileSha256Hex -Path $sourceDll) -cne $expectedHash) {
                throw "$name changed after the unified audit."
            }
            $payloadDll = Join-Path $smokePayloadRoot ($name + '.dll')
            if ($name -ceq 'RunicBuildCamera') {
                # Build Camera is deliberately released outside Suite 14. Admit its
                # current independent package and extract the smoke DLL from that ZIP,
                # while proving the package still contains the freshly audited bytes.
                $version = [string]$manifest.version_number
                $zipName = 'Chazman-' + $name + '-' + $version + '.zip'
                $zipPath = Join-Path $repoRoot ('artifacts\RunicBuildCamera\' + $zipName)
                if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
                    throw "The current independent Build Camera package is missing: $zipPath"
                }
                $cameraEntries = @(
                    'RunicBuildCamera.dll',
                    'manifest.json',
                    'README.md',
                    'icon.png',
                    'CHANGELOG.md',
                    'RunicBuildCamera.cfg.example'
                )
                $cameraPackageStage = Join-Path $stageRoot 'RunicBuildCamera-independent-package'
                New-Item -ItemType Directory -Path $cameraPackageStage | Out-Null
                $cameraSources = [ordered]@{
                    'RunicBuildCamera.dll' = $sourceDll
                    'manifest.json' = (Join-Path $sourceRoot 'manifest.json')
                    'README.md' = (Join-Path $sourceRoot 'README.md')
                    'icon.png' = (Join-Path $sourceRoot 'icon.png')
                    'CHANGELOG.md' = (Join-Path $sourceRoot 'CHANGELOG.md')
                    'RunicBuildCamera.cfg.example' = (Join-Path $sourceRoot 'RunicBuildCamera.cfg.example')
                }
                foreach ($cameraSource in $cameraSources.GetEnumerator()) {
                    if (-not (Test-Path -LiteralPath $cameraSource.Value -PathType Leaf)) {
                        throw "Build Camera package source is missing: $($cameraSource.Value)"
                    }
                    Copy-Item -LiteralPath $cameraSource.Value -Destination (
                        Join-Path $cameraPackageStage $cameraSource.Key)
                }
                Assert-Package `
                    -ZipPath $zipPath `
                    -PackageStage $cameraPackageStage `
                    -Entries $cameraEntries `
                    -ModuleName $name
                Export-ValidatedZipEntry `
                    -ZipPath $zipPath `
                    -EntryName ($name + '.dll') `
                    -Destination $payloadDll `
                    -ExpectedHash $expectedHash
                $companionPackageEvidence += [pscustomobject][ordered]@{
                    name = $name
                    version = $version
                    zip_file = $zipName
                    zip_sha256 = Get-FileSha256Hex -Path $zipPath
                    dll_sha256 = $expectedHash
                    release_scope = 'independent-build-camera-package'
                }
            }
            else {
                Copy-Item -LiteralPath $sourceDll -Destination $payloadDll
            }
            if ((Get-FileSha256Hex -Path $payloadDll) -cne $expectedHash) {
                throw "$name smoke-payload copy failed byte verification."
            }
            $runtimeDllEvidence += [pscustomobject][ordered]@{
                name = $name
                role = if ($name -ceq 'RunicIntegrity') {
                    'compatibility-only-unpackaged'
                }
                else {
                    'independent-build-camera-package'
                }
                version = [string]$manifest.version_number
                sha256 = $expectedHash
            }
        }
        $expectedPayloadEntries = @($runtimeDllEvidence | ForEach-Object { $_.name + '.dll' })
        Assert-ExactRootEntries -Actual @(
            Get-ChildItem -LiteralPath $smokePayloadRoot -File | ForEach-Object { $_.Name }
        ) -Expected $expectedPayloadEntries -Label 'dedicated smoke payload'
        if ($runtimeDllEvidence.Count -ne 20 -or
            @($runtimeDllEvidence | Select-Object -ExpandProperty name -Unique).Count -ne 20) {
            throw "Dedicated smoke payload must contain exactly twenty uniquely named participants."
        }

        $smokeEvidenceRoot = Join-Path $stageRoot 'dedicated-smoke-evidence'
        $smokeScript = Join-Path $repoRoot 'Test-RunicSuite14-Dedicated.ps1'
        if (-not (Test-Path -LiteralPath $smokeScript -PathType Leaf)) {
            throw "The hardened dedicated smoke runner is missing: $smokeScript"
        }
        $smokeScriptHash = Get-FileSha256Hex -Path $smokeScript
        if ($smokeScriptHash -cne '91631C2A34B00B2B6E24FBCC7162B5DD8BE8378AE7B28619BF2179F8BAC7C7A3') {
            throw "The hardened dedicated smoke runner changed after its frozen WinPS5/static audit: $smokeScriptHash"
        }
        $smokeArguments = @(
            '-NoProfile',
            '-File', $smokeScript,
            '-PluginPayloadRoot', $smokePayloadRoot,
            '-EvidenceRoot', $smokeEvidenceRoot,
            '-SkipUnifiedAudit',
            '-UnifiedAuditEvidencePath', $auditLogPath,
            '-DwellSeconds', '15'
        )
        $smokeOutput = @(& powershell @smokeArguments 2>&1)
        $smokeExitCode = $LASTEXITCODE
        foreach ($smokeLine in $smokeOutput) { Write-Output ([string]$smokeLine) }
        if ($smokeExitCode -ne 0) {
            throw "The staged-payload dedicated smoke failed with exit code $smokeExitCode."
        }
        $smokeGoMarkers = @($smokeOutput | ForEach-Object { ([string]$_).Trim() } |
            Where-Object {
                $_ -match '^RUNIC_SUITE_14_DEDICATED_SMOKE_GO plugins=20 audited=true audit_mode=validated-external evidence_json="[^"]+" evidence_sha256=[0-9A-F]{64}$'
            })
        if ($smokeGoMarkers.Count -ne 1) {
            throw "Dedicated smoke stdout must contain exactly one authoritative GO marker."
        }
        $smokeGoMatch = [regex]::Match(
            $smokeGoMarkers[0],
            '^RUNIC_SUITE_14_DEDICATED_SMOKE_GO plugins=20 audited=true audit_mode=validated-external evidence_json="(?<path>[^"]+)" evidence_sha256=(?<hash>[0-9A-F]{64})$')
        $smokeEvidenceCandidates = @(Get-ChildItem `
            -LiteralPath $smokeEvidenceRoot `
            -Recurse `
            -File `
            -Filter 'SMOKE-PASS.json')
        if ($smokeEvidenceCandidates.Count -ne 1) {
            throw "Dedicated smoke must produce exactly one SMOKE-PASS.json; found $($smokeEvidenceCandidates.Count)."
        }
        $smokeEvidenceJsonPath = $smokeEvidenceCandidates[0].FullName
        if (-not [string]::Equals(
                [System.IO.Path]::GetFullPath($smokeGoMatch.Groups['path'].Value),
                [System.IO.Path]::GetFullPath($smokeEvidenceJsonPath),
                [StringComparison]::OrdinalIgnoreCase) -or
            $smokeGoMatch.Groups['hash'].Value -cne (Get-FileSha256Hex -Path $smokeEvidenceJsonPath)) {
            throw "Dedicated smoke stdout evidence path/hash differs from SMOKE-PASS.json."
        }
        $smokeEvidenceText = Get-Content -LiteralPath $smokeEvidenceJsonPath -Raw
        $smokeEvidence = $smokeEvidenceText | ConvertFrom-Json
        $observedVersions = @($smokeEvidence.runtime.observed_valheim_versions)
        $observedNetworks = @($smokeEvidence.runtime.observed_network_versions)
        $readinessSequence = @($smokeEvidence.runtime.readiness_sequence)
        $smokePlugins = @($smokeEvidence.payload.plugins)
        $smokeSourceCatalog = @($smokeEvidence.payload.source_hash_catalog)
        $resolvedSmokePayloadRoot = [System.IO.Path]::GetFullPath($smokePayloadRoot)
        if ([string]$smokeEvidence.schema -cne 'runic-suite14-dedicated-smoke/v2' -or
            [string]$smokeEvidence.status -cne 'GO' -or
            [string]$smokeEvidence.unified_audit_mode -cne 'validated-external' -or
            [string]$smokeEvidence.unified_audit_transcript_sha256 -cne (Get-FileSha256Hex -Path $auditLogPath) -or
            [string]$smokeEvidence.audit.status -cne 'PASS' -or
            [string]$smokeEvidence.audit.mode -cne 'validated-external' -or
            $smokeEvidence.audit.skip_requested -ne $true -or
            [int]$smokeEvidence.audit.expected_projects -ne 23 -or
            [string]$smokeEvidence.audit.evidence_sha256 -cne (Get-FileSha256Hex -Path $auditLogPath) -or
            [string]$smokeEvidence.payload.mode -cne 'staged-flat' -or
            [string]$smokeEvidence.payload.source_mode -cne 'staged-flat' -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$smokeEvidence.payload.source_root),
                $resolvedSmokePayloadRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            [int]$smokeEvidence.payload.expected_plugin_count -ne 20 -or
            [int]$smokeEvidence.payload.loaded_plugin_count -ne 20 -or
            $smokePlugins.Count -ne 20 -or
            $smokeSourceCatalog.Count -ne 20 -or
            [string]$smokeEvidence.runtime.expected_valheim_version -cne '0.221.12' -or
            $observedVersions.Count -ne 1 -or [string]$observedVersions[0] -cne '0.221.12' -or
            [string]$smokeEvidence.runtime.expected_network_version -cne '36' -or
            $observedNetworks.Count -ne 1 -or [string]$observedNetworks[0] -cne '36' -or
            $smokeEvidence.runtime.version_marker_observed -ne $true -or
            $smokeEvidence.runtime.chainloader_observed -ne $true -or
            $smokeEvidence.runtime.registering_lobby_observed -ne $true -or
            $smokeEvidence.runtime.opened_steam_server_observed -ne $true -or
            $smokeEvidence.runtime.readiness_order_valid -ne $true -or
            $readinessSequence.Count -ne 2 -or
            [string]$readinessSequence[0] -cne 'Registering lobby' -or
            [string]$readinessSequence[1] -cne 'Opened Steam server' -or
            [int]$smokeEvidence.runtime.dwell_seconds -lt 10 -or
            [int]$smokeEvidence.runtime.dwell_seconds -gt 60 -or
            $smokeEvidence.runtime.dwell_completed -ne $true -or
            $smokeEvidence.runtime.process_alive_after_dwell -ne $true -or
            [int]$smokeEvidence.log_scan.expected_log_count -ne 4 -or
            [int]$smokeEvidence.log_scan.scanned_log_count -ne 4 -or
            $smokeEvidence.log_scan.pre_dwell_passed -ne $true -or
            $smokeEvidence.log_scan.post_dwell_passed -ne $true -or
            $smokeEvidence.log_scan.final_stopped_process_rescan_passed -ne $true -or
            [int]$smokeEvidence.log_scan.fatal_signals_found -ne 0 -or
            $smokeEvidence.environment.pinned_hashes_verified -ne $true) {
            throw "Dedicated smoke evidence is incomplete or does not prove true-ready survival."
        }
        $runtimeHashByFile = @{}
        foreach ($runtimeDll in $runtimeDllEvidence) {
            $runtimeHashByFile[[string]$runtimeDll.name + '.dll'] = [string]$runtimeDll.sha256
        }
        $observedPluginFiles = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($smokePlugin in $smokePlugins) {
            $pluginFile = [string]$smokePlugin.file
            if (-not $runtimeHashByFile.ContainsKey($pluginFile) -or
                -not $observedPluginFiles.Add($pluginFile) -or
                [string]$smokePlugin.sha256 -cne [string]$runtimeHashByFile[$pluginFile] -or
                [string]::IsNullOrWhiteSpace([string]$smokePlugin.assembly) -or
                [string]::IsNullOrWhiteSpace([string]$smokePlugin.guid) -or
                [string]::IsNullOrWhiteSpace([string]$smokePlugin.name) -or
                [string]$smokePlugin.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
                [int64]$smokePlugin.bytes -le 0 -or
                [string]::IsNullOrWhiteSpace([string]$smokePlugin.source_path) -or
                [string]::IsNullOrWhiteSpace([string]$smokePlugin.isolated_path)) {
                throw "Dedicated smoke plugin evidence is incomplete, duplicated, or differs from the staged payload: $pluginFile"
            }
        }
        $observedSourceCatalogFiles = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($sourceCatalogEntry in $smokeSourceCatalog) {
            $catalogFile = [string]$sourceCatalogEntry.file
            $catalogSourcePath = [System.IO.Path]::GetFullPath([string]$sourceCatalogEntry.source_path)
            $expectedSourcePath = [System.IO.Path]::GetFullPath((Join-Path $smokePayloadRoot $catalogFile))
            if (-not $runtimeHashByFile.ContainsKey($catalogFile) -or
                -not $observedSourceCatalogFiles.Add($catalogFile) -or
                [string]$sourceCatalogEntry.sha256 -cne [string]$runtimeHashByFile[$catalogFile] -or
                [int64]$sourceCatalogEntry.bytes -le 0 -or
                -not [string]::Equals(
                    $catalogSourcePath,
                    $expectedSourcePath,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Dedicated smoke source hash catalog differs from the exact staged payload: $catalogFile"
            }
        }
        $environmentPairs = @(
            @('server_executable_sha256', 'server_executable_expected_sha256'),
            @('server_assembly_sha256', 'server_assembly_expected_sha256'),
            @('client_assembly_sha256', 'client_assembly_expected_sha256'),
            @('bepinex_sha256', 'bepinex_expected_sha256'),
            @('harmony_sha256', 'harmony_expected_sha256'),
            @('mono_cecil_sha256', 'mono_cecil_expected_sha256')
        )
        foreach ($environmentPair in $environmentPairs) {
            $actualEnvironmentHash = [string]$smokeEvidence.environment.($environmentPair[0])
            $expectedEnvironmentHash = [string]$smokeEvidence.environment.($environmentPair[1])
            if ($actualEnvironmentHash -notmatch '^[0-9A-F]{64}$' -or
                $actualEnvironmentHash -cne $expectedEnvironmentHash) {
                throw "Dedicated smoke environment hash evidence drifted for $($environmentPair[0])."
            }
        }
        if ([string]::IsNullOrWhiteSpace([string]$smokeEvidence.environment.server_steam_buildid) -or
            [string]::IsNullOrWhiteSpace([string]$smokeEvidence.environment.client_steam_buildid)) {
            throw "Dedicated smoke evidence is missing client/server Steam build IDs."
        }
        $smokeHumanPath = Join-Path $smokeEvidenceRoot 'SMOKE-EVIDENCE.txt'
        if (-not (Test-Path -LiteralPath $smokeHumanPath -PathType Leaf) -or
            -not @(Get-Content -LiteralPath $smokeHumanPath | Where-Object { $_ -ceq 'status=GO' })) {
            throw "Dedicated smoke human evidence is missing its exact GO marker."
        }

        $smokeLogEvidence = @($smokeEvidence.logs)
        $expectedSmokeLogLabels = @('BepInEx', 'stdout', 'stderr', 'Unity')
        $smokeRootPrefix = [System.IO.Path]::GetFullPath($smokeEvidenceRoot).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if ($smokeLogEvidence.Count -ne 4) {
            throw "Dedicated smoke machine evidence must catalog exactly four logs."
        }
        foreach ($expectedSmokeLogLabel in $expectedSmokeLogLabels) {
            $matches = @($smokeLogEvidence | Where-Object {
                [string]$_.label -ceq $expectedSmokeLogLabel
            })
            if ($matches.Count -ne 1) {
                throw "Dedicated smoke evidence must contain exactly one $expectedSmokeLogLabel log."
            }
            $logPath = [System.IO.Path]::GetFullPath([string]$matches[0].path)
            if (-not $logPath.StartsWith($smokeRootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $logPath -PathType Leaf) -or
                ((Get-Item -LiteralPath $logPath).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                [int64](Get-Item -LiteralPath $logPath).Length -ne [int64]$matches[0].bytes -or
                (Get-FileSha256Hex -Path $logPath) -cne [string]$matches[0].sha256) {
                throw "Dedicated smoke $expectedSmokeLogLabel log differs from its machine evidence."
            }
        }

        # Preserve the complete proof set without archiving the disposable BepInEx
        # runtime, copied plugin payload, or generated world. The machine/human
        # evidence and all four independently scanned log streams are sufficient and
        # keep the release evidence bounded.
        $smokeEvidenceArchiveRoot = Join-Path $stageRoot 'dedicated-smoke-evidence-archive'
        New-Item -ItemType Directory -Path $smokeEvidenceArchiveRoot | Out-Null
        Copy-Item -LiteralPath $smokeEvidenceJsonPath -Destination (
            Join-Path $smokeEvidenceArchiveRoot 'SMOKE-PASS.json')
        Copy-Item -LiteralPath $smokeHumanPath -Destination (
            Join-Path $smokeEvidenceArchiveRoot 'SMOKE-EVIDENCE.txt')
        foreach ($smokeLog in $smokeLogEvidence) {
            $archiveLogLeaf = [string]$smokeLog.label + '.log'
            Copy-Item -LiteralPath ([string]$smokeLog.path) -Destination (
                Join-Path $smokeEvidenceArchiveRoot $archiveLogLeaf)
        }
        $smokeArchiveCatalog = [pscustomobject][ordered]@{
            schema = 'runic-suite14-dedicated-smoke-archive/v1'
            machine_evidence = [pscustomobject][ordered]@{
                file = 'SMOKE-PASS.json'
                sha256 = Get-FileSha256Hex -Path $smokeEvidenceJsonPath
            }
            human_evidence = [pscustomobject][ordered]@{
                file = 'SMOKE-EVIDENCE.txt'
                sha256 = Get-FileSha256Hex -Path $smokeHumanPath
            }
            logs = @($smokeLogEvidence | ForEach-Object {
                [pscustomobject][ordered]@{
                    label = [string]$_.label
                    file = [string]$_.label + '.log'
                    bytes = [int64]$_.bytes
                    sha256 = [string]$_.sha256
                }
            })
        }
        Write-Utf8NoBom `
            -Path (Join-Path $smokeEvidenceArchiveRoot 'ARCHIVE-CATALOG.json') `
            -Text (($smokeArchiveCatalog | ConvertTo-Json -Depth 6) + "`n")
        Assert-ExactRootEntries -Actual @(
            Get-ChildItem -LiteralPath $smokeEvidenceArchiveRoot -File |
                ForEach-Object { $_.Name }
        ) -Expected @(
            'SMOKE-PASS.json',
            'SMOKE-EVIDENCE.txt',
            'ARCHIVE-CATALOG.json',
            'BepInEx.log',
            'stdout.log',
            'stderr.log',
            'Unity.log'
        ) -Label 'bounded dedicated smoke evidence archive stage'
        $smokeEvidenceZip = Join-Path $stageRoot 'DEDICATED-SMOKE-EVIDENCE.zip'
        New-DeterministicTreeZip `
            -SourceRoot $smokeEvidenceArchiveRoot `
            -ZipPath $smokeEvidenceZip `
            -Identity 'RunicSuite14 dedicated smoke evidence'
        $smokeEvidenceZipHash = Get-FileSha256Hex -Path $smokeEvidenceZip

        $sourceReportSnapshot = Join-Path $stageRoot 'SOURCE-AUDIT-REPORT.md'
        Copy-Item -LiteralPath $sourceReportPath -Destination $sourceReportSnapshot
        $sourceSnapshot = Get-SourceTreeEvidence -Root $repoRoot
        $environmentEvidence = @(Get-EnvironmentEvidence -RepositoryRoot $repoRoot)
        if ($environmentEvidence.Count -ne 6 -or
            @($environmentEvidence | Where-Object {
                $_ -isnot [pscustomobject] -or [string]$_.sha256 -notmatch '^[0-9A-F]{64}$'
            }).Count -ne 0) {
            throw "Environment evidence must contain six individual pinned-file records."
        }
        $testEvidence = @()
        $totalChecks = 0
        foreach ($entry in $expectedAuditCounts.GetEnumerator()) {
            $totalChecks += [int]$entry.Value
            $testEvidence += [pscustomobject][ordered]@{
                project = ([string]$entry.Key).Replace('\', '/')
                checks = [int]$entry.Value
                result = 'PASS'
            }
        }
        if ($totalChecks -ne 1418) {
            throw "Expected audit-count ledger drifted from 1,418 checks to $totalChecks."
        }
        $packageEvidence = @($pending | ForEach-Object {
            [pscustomobject][ordered]@{
                name = [string]$_.Name
                version = [string]$_.Version
                zip_file = [System.IO.Path]::GetFileName([string]$_.Destination)
                zip_sha256 = [string]$_.ZipHash
                dll_sha256 = [string]$_.DllHash
                reproducible_build = [pscustomobject][ordered]@{
                    first_sha256 = [string]$_.FirstBuildDllHash
                    second_sha256 = [string]$_.SecondBuildDllHash
                    byte_identical = ([string]$_.FirstBuildDllHash -ceq [string]$_.SecondBuildDllHash)
                }
                reproducible_archive = [pscustomobject][ordered]@{
                    first_sha256 = [string]$_.FirstZipHash
                    second_sha256 = [string]$_.SecondZipHash
                    byte_identical = ([string]$_.FirstZipHash -ceq [string]$_.SecondZipHash)
                }
            }
        })
        if (@($packageEvidence | Where-Object {
                -not $_.reproducible_build.byte_identical -or
                -not $_.reproducible_archive.byte_identical
            }).Count -ne 0) {
            throw "Canonical evidence cannot admit a non-reproducible DLL or archive."
        }
        $foundationEvidence = @($foundationPackages | ForEach-Object {
            [pscustomobject][ordered]@{
                name = [string]$_.Name
                version = [string]$_.Version
                zip_file = [System.IO.Path]::GetFileName([string]$_.Zip)
                zip_sha256 = [string]$_.ZipHash
                dll_sha256 = [string]$_.DllHash
            }
        })
        $findingLedger = @(
            [pscustomobject][ordered]@{ id = 'P0-01'; finding = 'early world-load marker could false-green'; remediation = 'require true ready, bounded dwell, live process, and post-dwell rescan'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'P0-02'; finding = 'packages could promote before runtime smoke'; remediation = 'smoke exact staged payload before artifact mutation'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'P0-03'; finding = 'per-module promotion could leave a mixed release'; remediation = 'two-phase verified archive/copy with complete rollback and exact-root verification'; disposition = 'AWAITING_TRANSACTION_COMMIT' }
            [pscustomobject][ordered]@{ id = 'HIGH-04'; finding = 'smoke bytes were not tied to package bytes'; remediation = 'copy gameplay DLLs from validated package stages and extract Foundation DLLs from validated ZIPs'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'HIGH-05'; finding = 'runtime evidence omitted logs/version/survival'; remediation = 'machine-readable evidence covers all logs, exact runtime version, readiness, dwell, and process survival'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'HIGH-06'; finding = 'Foundation ZIP admission was structurally weak'; remediation = 'exact root set, manifest identity, one-DLL rule, and source-byte equality'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'HIGH-07'; finding = 'compatibility companions could be stale or skipped'; remediation = 'mandatory fresh rebuild, unified audit, byte freeze, and dedicated load'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'HIGH-08'; finding = 'zero-warning release condition was advisory'; remediation = 'all release and audit builds use TreatWarningsAsErrors'; disposition = 'REMEDIATED' }
            [pscustomobject][ordered]@{ id = 'HIGH-09'; finding = 'evidence completion did not gate promotion'; remediation = 'structured source report, candidate evidence, and final last-published GO commit marker gate release'; disposition = 'AWAITING_TRANSACTION_COMMIT' }
        )
        $sourceFindingLedger = @()
        for ($sourceFindingIndex = 0; $sourceFindingIndex -lt $sourceFindingDetails.Count; $sourceFindingIndex++) {
            $sourceFinding = $sourceFindingDetails[$sourceFindingIndex]
            $sourceDisposition = $sourceFinding.Groups['disposition'].Value.Trim()
            $findingText = $sourceFinding.Groups['finding'].Value.Trim()
            $candidateDisposition = 'SOURCE_GATE_VERIFIED'
            if ($findingText.StartsWith(
                    'Release publication could archive/promote',
                    [StringComparison]::Ordinal)) {
                $candidateDisposition = 'AWAITING_TRANSACTION_COMMIT'
            }
            elseif ($sourceDisposition.StartsWith(
                    'IMPLEMENTATION REQUIRES',
                    [StringComparison]::Ordinal)) {
                $candidateDisposition = 'STAGED_OR_RUNTIME_ARTIFACT_EVIDENCE_VERIFIED'
            }
            elseif ($sourceDisposition.StartsWith(
                    'CLOSED for reporting',
                    [StringComparison]::Ordinal)) {
                $candidateDisposition = 'REPORTING_CONTRACT_VERIFIED'
            }
            $sourceFindingLedger += [pscustomobject][ordered]@{
                id = 'SOURCE-' + ($sourceFindingIndex + 1).ToString('00', [Globalization.CultureInfo]::InvariantCulture)
                severity = $sourceFinding.Groups['severity'].Value
                finding = $findingText
                remediation = $sourceFinding.Groups['remediation'].Value.Trim()
                regression_gate = $sourceFinding.Groups['gate'].Value.Trim()
                source_disposition = $sourceDisposition
                candidate_disposition = $candidateDisposition
            }
        }
        if ($sourceFindingLedger.Count -ne 14 -or
            @($sourceFindingLedger | Where-Object {
                $_.candidate_disposition -ceq 'AWAITING_TRANSACTION_COMMIT'
            }).Count -ne 1) {
            throw "Source finding ledger must map all fourteen P0/HIGH rows with one transaction closure."
        }
        $zipCatalog = @()
        foreach ($packageItem in $packageEvidence) {
            $zipCatalog += [pscustomobject][ordered]@{
                scope = 'suite14-gameplay'
                file = [string]$packageItem.zip_file
                sha256 = [string]$packageItem.zip_sha256
            }
        }
        foreach ($foundationItem in $foundationEvidence) {
            $zipCatalog += [pscustomobject][ordered]@{
                scope = 'foundation-dependency'
                file = [string]$foundationItem.zip_file
                sha256 = [string]$foundationItem.zip_sha256
            }
        }
        foreach ($companionItem in $companionPackageEvidence) {
            $zipCatalog += [pscustomobject][ordered]@{
                scope = 'independent-build-camera'
                file = [string]$companionItem.zip_file
                sha256 = [string]$companionItem.zip_sha256
            }
        }
        $zipCatalog += [pscustomobject][ordered]@{
            scope = 'dedicated-smoke-evidence'
            file = 'DEDICATED-SMOKE-EVIDENCE.zip'
            sha256 = $smokeEvidenceZipHash
        }
        $evidenceObject = [pscustomobject][ordered]@{
            schema = 'runic-suite14-release-evidence/v1'
            status = 'RELEASE_CANDIDATE'
            game_version = '0.221.12'
            gameplay_module_count = 14
            runtime_participant_count = 20
            source_snapshot = $sourceSnapshot
            source_audit_report = [pscustomobject][ordered]@{
                file = 'SOURCE-AUDIT-REPORT.md'
                sha256 = Get-FileSha256Hex -Path $sourceReportSnapshot
                completeness_gate = 'PASS_WITH_EXPLICIT_ARTIFACT_HOLD'
            }
            environment = $environmentEvidence
            tests = [pscustomobject][ordered]@{
                project_count = $expectedAuditCounts.Count
                total_checks = $totalChecks
                warning_count = 0
                error_count = 0
                transcript_file = 'UNIFIED-AUDIT.log'
                transcript_sha256 = Get-FileSha256Hex -Path $auditLogPath
                projects = $testEvidence
            }
            runtime_dlls = $runtimeDllEvidence
            gameplay_packages = $packageEvidence
            foundation_packages = $foundationEvidence
            independent_compatibility_packages = $companionPackageEvidence
            zip_catalog = $zipCatalog
            dedicated_smoke = [pscustomobject][ordered]@{
                runner_file = 'Test-RunicSuite14-Dedicated.ps1'
                runner_sha256 = $smokeScriptHash
                evidence_archive = 'DEDICATED-SMOKE-EVIDENCE.zip'
                archive_sha256 = $smokeEvidenceZipHash
                machine_evidence_sha256 = Get-FileSha256Hex -Path $smokeEvidenceJsonPath
                result = 'GO'
                audit_mode = [string]$smokeEvidence.audit.mode
                readiness_sequence = @($smokeEvidence.runtime.readiness_sequence)
                dwell_seconds = [int]$smokeEvidence.runtime.dwell_seconds
                process_alive_after_dwell = [bool]$smokeEvidence.runtime.process_alive_after_dwell
                scanned_log_count = [int]$smokeEvidence.log_scan.scanned_log_count
                fatal_signals_found = [int]$smokeEvidence.log_scan.fatal_signals_found
                environment = [pscustomobject][ordered]@{
                    server_steam_buildid = [string]$smokeEvidence.environment.server_steam_buildid
                    client_steam_buildid = [string]$smokeEvidence.environment.client_steam_buildid
                    server_executable_sha256 = [string]$smokeEvidence.environment.server_executable_sha256
                    server_assembly_sha256 = [string]$smokeEvidence.environment.server_assembly_sha256
                    client_assembly_sha256 = [string]$smokeEvidence.environment.client_assembly_sha256
                    bepinex_sha256 = [string]$smokeEvidence.environment.bepinex_sha256
                    harmony_sha256 = [string]$smokeEvidence.environment.harmony_sha256
                    mono_cecil_sha256 = [string]$smokeEvidence.environment.mono_cecil_sha256
                }
            }
            publication_plan = [pscustomobject][ordered]@{
                mode = 'two-phase-all-or-nothing'
                archive_phase = 'archive and SHA256-verify every superseded direct-root artifact before any active copy'
                promotion_phase = 'SHA256-verify temporary direct-root copy then same-directory rename'
                rollback = 'remove only recorded new temp/destination files; move and SHA256-verify every archived original without overwrite'
                exact_active_gameplay_package_count = 14
                exact_active_gameplay_packages = @($pending | ForEach-Object {
                    [System.IO.Path]::GetFileName([string]$_.Destination)
                })
                post_promotion_hash_verification_required = $true
            }
            publication_execution = $null
            source_finding_ledger = $sourceFindingLedger
            finding_remediation_ledger = $findingLedger
            open_release_closures = 1
            unresolved_p0_or_high = 1
        }
        $candidateEvidencePath = Join-Path $stageRoot 'AUDIT-EVIDENCE.candidate.json'
        $evidenceJson = $evidenceObject | ConvertTo-Json -Depth 12
        Write-Utf8NoBom -Path $candidateEvidencePath -Text ($evidenceJson + "`n")
        $evidenceText = Get-Content -LiteralPath $candidateEvidencePath -Raw
        $evidenceRoundTrip = $evidenceText | ConvertFrom-Json
        if ([string]$evidenceRoundTrip.status -cne 'RELEASE_CANDIDATE' -or
            [int]$evidenceRoundTrip.open_release_closures -ne 1 -or
            [int]$evidenceRoundTrip.unresolved_p0_or_high -ne 1 -or
            [regex]::IsMatch(
                $evidenceText,
                '\bpending\b',
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            throw "Canonical candidate evidence did not satisfy the staged/smoke/no-pending gate."
        }

        $releaseItems = @()
        foreach ($package in $pending) {
            $releaseItems += [pscustomobject]@{
                Name = [string]$package.Name
                Kind = 'gameplay-package'
                Source = [string]$package.Source
                Destination = [System.IO.Path]::GetFullPath([string]$package.Destination)
                Hash = [string]$package.ZipHash
            }
        }
        foreach ($metadata in @(
                @{ Name = 'UNIFIED-AUDIT'; Source = $auditLogPath; Leaf = 'UNIFIED-AUDIT.log' }
                @{ Name = 'SOURCE-AUDIT-REPORT'; Source = $sourceReportSnapshot; Leaf = 'SOURCE-AUDIT-REPORT.md' }
                @{ Name = 'DEDICATED-SMOKE-EVIDENCE'; Source = $smokeEvidenceZip; Leaf = 'DEDICATED-SMOKE-EVIDENCE.zip' }
            )) {
            $releaseItems += [pscustomobject]@{
                Name = [string]$metadata.Name
                Kind = 'release-evidence'
                Source = [System.IO.Path]::GetFullPath([string]$metadata.Source)
                Destination = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot ([string]$metadata.Leaf)))
                Hash = Get-FileSha256Hex -Path ([string]$metadata.Source)
            }
        }

        $artifactRootExisted = Test-Path -LiteralPath $artifactRoot -PathType Container
        if ((Test-Path -LiteralPath $artifactRoot) -and -not $artifactRootExisted) {
            throw "Suite artifact root is not a directory: $artifactRoot"
        }
        if ($artifactRootExisted -and
            ((Get-Item -LiteralPath $artifactRoot).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to promote through a reparse-point artifact root: $artifactRoot"
        }
        $obsoleteRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'Obsolete'))
        if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $obsoleteRoot)) {
            throw "Refusing to use an Obsolete directory outside the suite artifact root."
        }
        if ((Test-Path -LiteralPath $obsoleteRoot) -and
            (-not (Test-Path -LiteralPath $obsoleteRoot -PathType Container) -or
             ((Get-Item -LiteralPath $obsoleteRoot).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Suite Obsolete path is not a safe directory: $obsoleteRoot"
        }

        $releaseByDestination = [System.Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($releaseItem in $releaseItems) {
            if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $releaseItem.Destination)) {
                throw "Release destination is not a direct artifact-root child: $($releaseItem.Destination)"
            }
            if ($releaseByDestination.ContainsKey($releaseItem.Destination)) {
                throw "Duplicate release destination: $($releaseItem.Destination)"
            }
            if (-not (Test-Path -LiteralPath $releaseItem.Source -PathType Leaf) -or
                ((Get-Item -LiteralPath $releaseItem.Source).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                (Get-FileSha256Hex -Path $releaseItem.Source) -cne $releaseItem.Hash) {
                throw "Release source failed its final byte/safety check: $($releaseItem.Source)"
            }
            $releaseByDestination.Add($releaseItem.Destination, $releaseItem)
        }
        $finalEvidenceDestination = [System.IO.Path]::GetFullPath((
            Join-Path $artifactRoot 'AUDIT-EVIDENCE.json'))
        if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $finalEvidenceDestination)) {
            throw "Final GO evidence destination is not a direct artifact-root child."
        }

        $supersededByPath = [System.Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        if ($artifactRootExisted) {
            $modulePrefixes = @($modules | ForEach-Object { 'Chazman-' + [string]$_.Name + '-' })
            foreach ($existing in Get-ChildItem -LiteralPath $artifactRoot -File -Filter '*.zip') {
                $isGameplayPackage = $false
                foreach ($prefix in $modulePrefixes) {
                    if ($existing.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                        $isGameplayPackage = $true
                        break
                    }
                }
                if (-not $isGameplayPackage) { continue }
                if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing to archive a reparse-point package: $($existing.FullName)"
                }
                $path = [System.IO.Path]::GetFullPath($existing.FullName)
                $hash = Get-FileSha256Hex -Path $path
                if ($releaseByDestination.ContainsKey($path) -and
                    $hash -ceq [string]$releaseByDestination[$path].Hash) {
                    continue
                }
                $supersededByPath.Add($path, [pscustomobject]@{ Original = $path; Hash = $hash })
            }
        }
        if (Test-Path -LiteralPath $finalEvidenceDestination -PathType Leaf) {
            $existingEvidenceItem = Get-Item -LiteralPath $finalEvidenceDestination
            if (($existingEvidenceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to replace reparse-point final evidence: $finalEvidenceDestination"
            }
            $existingEvidenceHash = Get-FileSha256Hex -Path $finalEvidenceDestination
            if (-not $supersededByPath.ContainsKey($finalEvidenceDestination)) {
                $supersededByPath.Add(
                    $finalEvidenceDestination,
                    [pscustomobject]@{
                        Original = $finalEvidenceDestination
                        Hash = $existingEvidenceHash
                    })
            }
        }
        elseif (Test-Path -LiteralPath $finalEvidenceDestination) {
            throw "A non-file blocks final GO evidence destination: $finalEvidenceDestination"
        }
        foreach ($releaseItem in $releaseItems) {
            if (Test-Path -LiteralPath $releaseItem.Destination -PathType Leaf) {
                $existingItem = Get-Item -LiteralPath $releaseItem.Destination
                if (($existingItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing to replace a reparse-point release artifact: $($releaseItem.Destination)"
                }
                $existingHash = Get-FileSha256Hex -Path $releaseItem.Destination
                if ($existingHash -cne $releaseItem.Hash -and
                    -not $supersededByPath.ContainsKey($releaseItem.Destination)) {
                    $supersededByPath.Add(
                        $releaseItem.Destination,
                        [pscustomobject]@{ Original = $releaseItem.Destination; Hash = $existingHash })
                }
            }
            elseif (Test-Path -LiteralPath $releaseItem.Destination) {
                throw "A non-file blocks release destination $($releaseItem.Destination)."
            }
        }

        $archivePlans = @()
        $archiveTargets = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($superseded in @($supersededByPath.Values | Sort-Object Original)) {
            $archivePath = Get-CollisionSafeObsoletePath `
                -ObsoleteRoot $obsoleteRoot `
                -SourcePath $superseded.Original `
                -SourceHash $superseded.Hash
            if (-not $archiveTargets.Add($archivePath)) {
                throw "Obsolete archive target collision: $archivePath"
            }
            $archivePlans += [pscustomobject]@{
                Original = [string]$superseded.Original
                Archive = $archivePath
                Hash = [string]$superseded.Hash
            }
        }
        $promotionPlans = @()
        foreach ($releaseItem in $releaseItems) {
            $alreadyExact = Test-Path -LiteralPath $releaseItem.Destination -PathType Leaf
            if ($alreadyExact) {
                $alreadyExact = (Get-FileSha256Hex -Path $releaseItem.Destination) -ceq $releaseItem.Hash
            }
            if ($alreadyExact) { continue }
            $tempLeaf = '.promoting-' + [Guid]::NewGuid().ToString('N') + '-' +
                [System.IO.Path]::GetFileName($releaseItem.Destination)
            $tempPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $tempLeaf))
            if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $tempPath) -or
                (Test-Path -LiteralPath $tempPath)) {
                throw "Could not allocate a safe promotion temporary path."
            }
            $promotionPlans += [pscustomobject]@{
                Name = [string]$releaseItem.Name
                Kind = [string]$releaseItem.Kind
                Source = [string]$releaseItem.Source
                Destination = [string]$releaseItem.Destination
                Temp = $tempPath
                Hash = [string]$releaseItem.Hash
                TempCreatedByInvocation = $false
                DestinationCreatedByInvocation = $false
            }
        }

        $archived = @()
        $promoted = @()
        $obsoleteRootExisted = Test-Path -LiteralPath $obsoleteRoot -PathType Container
        try {
            if (-not $artifactRootExisted) {
                New-Item -ItemType Directory -Path $artifactRoot | Out-Null
            }
            if ($archivePlans.Count -gt 0 -and -not $obsoleteRootExisted) {
                New-Item -ItemType Directory -Path $obsoleteRoot | Out-Null
            }

            # Phase 1: archive and verify every superseded package/evidence file.
            # No new active release file is copied until this phase is complete.
            foreach ($archivePlan in $archivePlans) {
                $archived += $archivePlan
                [System.IO.File]::Move($archivePlan.Original, $archivePlan.Archive)
                if ((Test-Path -LiteralPath $archivePlan.Original) -or
                    -not (Test-Path -LiteralPath $archivePlan.Archive -PathType Leaf) -or
                    (Get-FileSha256Hex -Path $archivePlan.Archive) -cne $archivePlan.Hash) {
                    throw "Archive verification failed for $($archivePlan.Original)."
                }
                Write-Output "ARCHIVED $($archivePlan.Original) -> $($archivePlan.Archive) SHA256=$($archivePlan.Hash)"
            }

            # Phase 2: verified temporary copies followed by same-directory atomic
            # renames. Record each plan before copying so partial copies roll back.
            foreach ($promotionPlan in $promotionPlans) {
                $promoted += $promotionPlan
                $promotionSourceStream = [System.IO.File]::OpenRead($promotionPlan.Source)
                try {
                    $promotionTempStream = [System.IO.File]::Open(
                        $promotionPlan.Temp,
                        [System.IO.FileMode]::CreateNew,
                        [System.IO.FileAccess]::Write,
                        [System.IO.FileShare]::None)
                    $promotionPlan.TempCreatedByInvocation = $true
                    try { $promotionSourceStream.CopyTo($promotionTempStream) }
                    finally { $promotionTempStream.Dispose() }
                }
                finally { $promotionSourceStream.Dispose() }
                if ((Get-FileSha256Hex -Path $promotionPlan.Temp) -cne $promotionPlan.Hash) {
                    throw "Temporary promotion hash mismatch for $($promotionPlan.Name)."
                }
                if (Test-Path -LiteralPath $promotionPlan.Destination) {
                    throw "Destination reappeared during promotion: $($promotionPlan.Destination)"
                }
                [System.IO.File]::Move($promotionPlan.Temp, $promotionPlan.Destination)
                $promotionPlan.DestinationCreatedByInvocation = $true
                if ((Get-FileSha256Hex -Path $promotionPlan.Destination) -cne $promotionPlan.Hash) {
                    throw "Final promotion hash mismatch for $($promotionPlan.Name)."
                }
            }

            foreach ($releaseItem in $releaseItems) {
                if (-not (Test-Path -LiteralPath $releaseItem.Destination -PathType Leaf) -or
                    (Get-FileSha256Hex -Path $releaseItem.Destination) -cne $releaseItem.Hash) {
                    throw "Final release verification failed for $($releaseItem.Name)."
                }
            }
            $expectedGameplayRoots = @($pending | ForEach-Object {
                [System.IO.Path]::GetFullPath([string]$_.Destination)
            })
            $actualGameplayRoots = @()
            foreach ($existing in Get-ChildItem -LiteralPath $artifactRoot -File -Filter '*.zip') {
                foreach ($module in $modules) {
                    if ($existing.Name.StartsWith(
                            ('Chazman-' + [string]$module.Name + '-'),
                            [StringComparison]::OrdinalIgnoreCase)) {
                        $actualGameplayRoots += [System.IO.Path]::GetFullPath($existing.FullName)
                        break
                    }
                }
            }
            if ($actualGameplayRoots.Count -ne 14) {
                throw "Final active Suite-14 package set is not exact; found $($actualGameplayRoots.Count)."
            }
            foreach ($expectedRoot in $expectedGameplayRoots) {
                if ($actualGameplayRoots -notcontains $expectedRoot) {
                    throw "Final active Suite-14 package set is missing $expectedRoot."
                }
            }

            # Build the executed transaction ledger from the moves/copies that have
            # actually completed and the exact active hashes just re-read above.
            # AUDIT-EVIDENCE.json is the last-published commit marker: if creating,
            # validating, or atomically renaming it fails, the same catch restores
            # every archived prior artifact and removes verified new bytes.
            $archiveExecution = @($archived | ForEach-Object {
                [pscustomobject][ordered]@{
                    original_file = [System.IO.Path]::GetFileName([string]$_.Original)
                    original_sha256 = [string]$_.Hash
                    obsolete_file = 'Obsolete/' + [System.IO.Path]::GetFileName([string]$_.Archive)
                    move_verified = $true
                }
            })
            $promotionExecution = @($promoted | ForEach-Object {
                [pscustomobject][ordered]@{
                    name = [string]$_.Name
                    kind = [string]$_.Kind
                    destination_file = [System.IO.Path]::GetFileName([string]$_.Destination)
                    sha256 = [string]$_.Hash
                    temporary_copy_verified = $true
                    destination_verified = $true
                }
            })
            $promotedDestinationSet = [System.Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($promotedItem in $promoted) {
                [void]$promotedDestinationSet.Add([string]$promotedItem.Destination)
            }
            $reusedExecution = @($releaseItems | Where-Object {
                -not $promotedDestinationSet.Contains([string]$_.Destination)
            } | ForEach-Object {
                [pscustomobject][ordered]@{
                    name = [string]$_.Name
                    kind = [string]$_.Kind
                    destination_file = [System.IO.Path]::GetFileName([string]$_.Destination)
                    sha256 = [string]$_.Hash
                    preexisting_exact_bytes_verified = $true
                }
            })
            $activePackageExecution = @($pending | ForEach-Object {
                [pscustomobject][ordered]@{
                    name = [string]$_.Name
                    file = [System.IO.Path]::GetFileName([string]$_.Destination)
                    sha256 = Get-FileSha256Hex -Path ([string]$_.Destination)
                }
            })
            $evidenceObject.status = 'GO'
            $evidenceObject.open_release_closures = 0
            $evidenceObject.unresolved_p0_or_high = 0
            foreach ($finding in $evidenceObject.finding_remediation_ledger) {
                if ([string]$finding.id -ceq 'P0-03' -or
                    [string]$finding.id -ceq 'HIGH-09') {
                    $finding.disposition = 'REMEDIATED_AND_TRANSACTION_COMMITTED'
                }
            }
            foreach ($sourceFindingRecord in $evidenceObject.source_finding_ledger) {
                if ([string]$sourceFindingRecord.candidate_disposition -ceq
                    'AWAITING_TRANSACTION_COMMIT') {
                    $sourceFindingRecord.candidate_disposition =
                        'CLOSED_BY_TRANSACTION_COMMIT'
                }
            }
            $evidenceObject.publication_execution = [pscustomobject][ordered]@{
                result = 'COMMITTED'
                archived_count = $archiveExecution.Count
                archived = $archiveExecution
                promoted_count = $promotionExecution.Count
                promoted = $promotionExecution
                reused_exact_count = $reusedExecution.Count
                reused_exact = $reusedExecution
                exact_active_gameplay_package_count = $activePackageExecution.Count
                exact_active_gameplay_packages = $activePackageExecution
                all_release_item_hashes_reverified = $true
                exact_active_set_reverified = $true
                final_commit_marker = 'AUDIT-EVIDENCE.json'
                final_commit_marker_published_last = $true
                rollback_contract = 'on any pre-commit-marker failure, preserve foreign bytes, remove only invocation-created exact expected hashes, and restore only preverified archived hashes without overwrite'
            }
            $finalEvidenceStage = Join-Path $stageRoot 'AUDIT-EVIDENCE.json'
            $finalEvidenceJson = $evidenceObject | ConvertTo-Json -Depth 16
            Write-Utf8NoBom -Path $finalEvidenceStage -Text ($finalEvidenceJson + "`n")
            $finalEvidenceText = Get-Content -LiteralPath $finalEvidenceStage -Raw
            $finalEvidenceRoundTrip = $finalEvidenceText | ConvertFrom-Json
            if ([string]$finalEvidenceRoundTrip.status -cne 'GO' -or
                [int]$finalEvidenceRoundTrip.open_release_closures -ne 0 -or
                [int]$finalEvidenceRoundTrip.unresolved_p0_or_high -ne 0 -or
                [string]$finalEvidenceRoundTrip.publication_execution.result -cne 'COMMITTED' -or
                [int]$finalEvidenceRoundTrip.publication_execution.archived_count -ne $archivePlans.Count -or
                ([int]$finalEvidenceRoundTrip.publication_execution.promoted_count +
                 [int]$finalEvidenceRoundTrip.publication_execution.reused_exact_count) -ne
                    $releaseItems.Count -or
                [int]$finalEvidenceRoundTrip.publication_execution.exact_active_gameplay_package_count -ne 14 -or
                @($finalEvidenceRoundTrip.source_finding_ledger).Count -ne 14 -or
                $finalEvidenceRoundTrip.publication_execution.all_release_item_hashes_reverified -ne $true -or
                $finalEvidenceRoundTrip.publication_execution.exact_active_set_reverified -ne $true -or
                [regex]::IsMatch(
                    $finalEvidenceText,
                    '\bpending\b|AWAITING_TRANSACTION_COMMIT',
                    [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                throw "Final artifact evidence did not satisfy the executed GO/closure/commit-ledger gate."
            }
            $finalEvidenceHash = Get-FileSha256Hex -Path $finalEvidenceStage
            $finalEvidenceTemp = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot (
                '.promoting-' + [Guid]::NewGuid().ToString('N') + '-AUDIT-EVIDENCE.json')))
            if (-not (Test-DirectChildPath -Parent $artifactRoot -Child $finalEvidenceTemp) -or
                (Test-Path -LiteralPath $finalEvidenceTemp) -or
                (Test-Path -LiteralPath $finalEvidenceDestination)) {
                throw "Could not allocate an empty direct-root final evidence commit path."
            }
            $finalEvidencePlan = [pscustomobject]@{
                Name = 'AUDIT-EVIDENCE'
                Kind = 'final-go-commit-marker'
                Source = $finalEvidenceStage
                Destination = $finalEvidenceDestination
                Temp = $finalEvidenceTemp
                Hash = $finalEvidenceHash
                TempCreatedByInvocation = $false
                DestinationCreatedByInvocation = $false
            }
            $promoted += $finalEvidencePlan
            $finalEvidenceSourceStream = [System.IO.File]::OpenRead($finalEvidenceStage)
            try {
                $finalEvidenceTempStream = [System.IO.File]::Open(
                    $finalEvidenceTemp,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None)
                $finalEvidencePlan.TempCreatedByInvocation = $true
                try { $finalEvidenceSourceStream.CopyTo($finalEvidenceTempStream) }
                finally { $finalEvidenceTempStream.Dispose() }
            }
            finally { $finalEvidenceSourceStream.Dispose() }
            if ((Get-FileSha256Hex -Path $finalEvidenceTemp) -cne $finalEvidenceHash) {
                throw "Final GO evidence temporary-copy hash mismatch."
            }
            if (Test-Path -LiteralPath $finalEvidenceDestination) {
                throw "Final GO evidence destination reappeared before commit."
            }
            [System.IO.File]::Move($finalEvidenceTemp, $finalEvidenceDestination)
            $finalEvidencePlan.DestinationCreatedByInvocation = $true
            if (-not (Test-Path -LiteralPath $finalEvidenceDestination -PathType Leaf) -or
                (Get-FileSha256Hex -Path $finalEvidenceDestination) -cne $finalEvidenceHash) {
                throw "Final GO evidence commit-marker hash verification failed."
            }
        }
        catch {
            $promotionFailure = $_
            $rollbackErrors = [System.Collections.Generic.List[string]]::new()
            for ($promotionIndex = $promoted.Count - 1; $promotionIndex -ge 0; $promotionIndex--) {
                $promotionPlan = $promoted[$promotionIndex]
                $createdRecords = @(
                    [pscustomobject]@{
                        Path = [string]$promotionPlan.Temp
                        Created = [bool]$promotionPlan.TempCreatedByInvocation
                    }
                    [pscustomobject]@{
                        Path = [string]$promotionPlan.Destination
                        Created = [bool]$promotionPlan.DestinationCreatedByInvocation
                    }
                )
                foreach ($createdRecord in $createdRecords) {
                    if (-not $createdRecord.Created -or
                        -not (Test-Path -LiteralPath $createdRecord.Path -PathType Leaf)) {
                        continue
                    }
                    try {
                        $rollbackHash = Get-FileSha256Hex -Path $createdRecord.Path
                        if ($rollbackHash -cne [string]$promotionPlan.Hash) {
                            throw "bytes differ from this invocation's expected SHA256; preserved"
                        }
                        Remove-Item -LiteralPath $createdRecord.Path -Force
                        if (Test-Path -LiteralPath $createdRecord.Path) {
                            throw "path still exists after removal"
                        }
                    }
                    catch {
                        $rollbackErrors.Add(
                            "could not safely remove $($createdRecord.Path)`: $($_.Exception.Message)")
                    }
                }
            }
            for ($archiveIndex = $archived.Count - 1; $archiveIndex -ge 0; $archiveIndex--) {
                $archivePlan = $archived[$archiveIndex]
                try {
                    if (Test-Path -LiteralPath $archivePlan.Archive -PathType Leaf) {
                        $archiveRollbackHash = Get-FileSha256Hex -Path $archivePlan.Archive
                        if ($archiveRollbackHash -cne $archivePlan.Hash) {
                            throw "archived bytes differ before restore; preserved in Obsolete"
                        }
                        if (Test-Path -LiteralPath $archivePlan.Original) {
                            throw "restore destination unexpectedly exists"
                        }
                        [System.IO.File]::Move($archivePlan.Archive, $archivePlan.Original)
                    }
                    elseif (-not (Test-Path -LiteralPath $archivePlan.Original -PathType Leaf)) {
                        throw "neither original nor archived copy exists"
                    }
                    if (-not (Test-Path -LiteralPath $archivePlan.Original -PathType Leaf) -or
                        (Get-FileSha256Hex -Path $archivePlan.Original) -cne $archivePlan.Hash) {
                        throw "restored hash differs"
                    }
                }
                catch {
                    $rollbackErrors.Add("could not restore $($archivePlan.Original)`: $($_.Exception.Message)")
                }
            }
            if (-not $obsoleteRootExisted -and
                (Test-Path -LiteralPath $obsoleteRoot -PathType Container) -and
                @(Get-ChildItem -LiteralPath $obsoleteRoot -Force).Count -eq 0) {
                try { Remove-Item -LiteralPath $obsoleteRoot -Force }
                catch { $rollbackErrors.Add("could not remove newly created Obsolete directory: $($_.Exception.Message)") }
            }
            if (-not $artifactRootExisted -and
                (Test-Path -LiteralPath $artifactRoot -PathType Container) -and
                @(Get-ChildItem -LiteralPath $artifactRoot -Force).Count -eq 0) {
                try { Remove-Item -LiteralPath $artifactRoot -Force }
                catch { $rollbackErrors.Add("could not remove newly created artifact root: $($_.Exception.Message)") }
            }
            if ($rollbackErrors.Count -gt 0) {
                throw "Promotion failed and rollback was incomplete. Original failure: $promotionFailure Rollback: $([string]::Join('; ', $rollbackErrors))"
            }
            throw $promotionFailure
        }

        foreach ($package in $pending) {
            Write-Output "PACKAGED $($package.Destination) ZIP_SHA256=$($package.ZipHash) DLL_SHA256=$($package.DllHash)"
        }
        Write-Output "EVIDENCE $finalEvidenceDestination STATUS=GO SHA256=$finalEvidenceHash TRANSACTION=COMMITTED"
    }
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
    $safeParent = Test-DirectChildPath -Parent $stageParent -Child $resolvedStage
    $safeLeaf = (Split-Path -Leaf $resolvedStage).StartsWith(
        'runic-suite14-stage-',
        [StringComparison]::Ordinal)
    if ($safeParent -and $safeLeaf) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SkipTests) {
    Write-Output 'Runic Suite 14 build/package validation completed; tests and promotion were skipped.'
}
else {
    Write-Output 'Runic Suite 14 build, 1,418-check unified diagnostics, two-pass reproducibility, exact-byte true-ready dedicated smoke, canonical evidence, and transactional promotion completed.'
}
