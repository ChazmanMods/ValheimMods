[CmdletBinding()]
param(
    [string]$Destination = 'E:\Valheim Mods\Latest Runic Mods',
    [string]$ValheimInstall = $env:VALHEIM_INSTALL,
    [string]$BepInExProfile = $env:BEPINEX_PROFILE
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$fixedZipTimestamp = New-Object System.DateTimeOffset(
    2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$expectedLoaderDependency = 'denikson-BepInExPack_Valheim-5.4.2350'
$expectedBepInExFileVersion = '5.4.23.5'
$expectedValheimAssemblySha256 =
    '27A766A8D23A7BD8B6A54FB9AD0452A96C305FB3629B39C40527C09A1C393A84'
$namespace = 'Chazman'

function Require {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    Require (-not [string]::IsNullOrWhiteSpace($Path)) 'A required path is empty.'
    return [System.IO.Path]::GetFullPath($Path.Trim().Trim('"'))
}

function Test-PathEquals {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    return [string]::Equals(
        (Get-NormalizedFullPath $Left).TrimEnd('\', '/'),
        (Get-NormalizedFullPath $Right).TrimEnd('\', '/'),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsStrictChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $candidateFull = (Get-NormalizedFullPath $Candidate).TrimEnd('\', '/')
    $parentFull = (Get-NormalizedFullPath $Parent).TrimEnd('\', '/')
    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith(
        $prefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-OrdinaryDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Require (Test-Path -LiteralPath $Path -PathType Container) (
        "$Label directory does not exist: $Path")
    $item = Get-Item -LiteralPath $Path -Force
    Require (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) (
        "$Label must not be a junction or symbolic link: $Path")
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-StreamSha256Hex {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($algorithm.ComputeHash($Stream) | ForEach-Object {
            $_.ToString('X2')
        }) -join '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Write-NewUtf8Text {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    Require (-not (Test-Path -LiteralPath $Path)) (
        "Refusing to overwrite immutable release evidence: $Path")
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Get-OrdinalSortedStrings {
    param([Parameter(Mandatory = $true)][object[]]$Values)

    [string[]]$result = @($Values | ForEach-Object { [string]$_ })
    [Array]::Sort($result, [System.StringComparer]::Ordinal)
    return ,$result
}

function Assert-ExactSequence {
    param(
        [Parameter(Mandatory = $true)][object[]]$Actual,
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Require ($Actual.Count -eq $Expected.Count) (
        "$Label count is $($Actual.Count); expected $($Expected.Count).")
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Require ([string]::Equals(
            [string]$Actual[$index],
            [string]$Expected[$index],
            [System.StringComparison]::Ordinal)) (
            "$Label differs at index ${index}: '$($Actual[$index])' != '$($Expected[$index])'.")
    }
}

function Assert-UniqueStrings {
    param(
        [Parameter(Mandatory = $true)][object[]]$Values,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $seen = @{}
    foreach ($valueObject in $Values) {
        $value = [string]$valueObject
        $key = $value.ToUpperInvariant()
        Require (-not $seen.ContainsKey($key)) "$Label contains a duplicate: $value"
        $seen[$key] = $true
    }
}

function Assert-Png256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Require (Test-Path -LiteralPath $Path -PathType Leaf) "$Label is missing: $Path"
    [byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
    [byte[]]$signature = @(137, 80, 78, 71, 13, 10, 26, 10)
    Require ($bytes.Length -ge 24) "$Label is too short to be a PNG."
    for ($index = 0; $index -lt $signature.Length; $index++) {
        Require ($bytes[$index] -eq $signature[$index]) "$Label has an invalid PNG signature."
    }
    Require ([System.Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ceq 'IHDR') (
        "$Label has no IHDR chunk in the expected PNG location.")
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor
        ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor
        ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    Require ($width -eq 256 -and $height -eq 256) (
        "$Label is ${width}x${height}; Thunderstore requires 256x256.")
}

function Read-Manifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedName,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Missing manifest: $Path"
    try {
        $manifest = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Invalid JSON in ${Path}: $($_.Exception.Message)"
    }

    foreach ($property in @(
        'name', 'version_number', 'website_url', 'description', 'dependencies')) {
        Require ($manifest.PSObject.Properties.Name -contains $property) (
            "$ExpectedName manifest is missing '$property'.")
    }
    Require ([string]$manifest.name -ceq $ExpectedName) (
        "$ExpectedName manifest identity is '$($manifest.name)'.")
    Require ([string]$manifest.version_number -ceq $ExpectedVersion) (
        "$ExpectedName manifest version is '$($manifest.version_number)'; expected '$ExpectedVersion'.")
    Require ([string]$manifest.version_number -match '^\d+\.\d+\.\d+$') (
        "$ExpectedName manifest version is not a three-part semantic version.")
    $description = [string]$manifest.description
    Require (-not [string]::IsNullOrWhiteSpace($description)) (
        "$ExpectedName manifest description is empty.")
    Require ($description.Length -le 250) (
        "$ExpectedName manifest description exceeds Thunderstore's 250-character limit.")
    Require (-not [string]::IsNullOrWhiteSpace([string]$manifest.website_url)) (
        "$ExpectedName manifest website_url is empty.")

    [string[]]$dependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
    Assert-UniqueStrings $dependencies "$ExpectedName dependencies"
    foreach ($dependency in $dependencies) {
        Require ($dependency -match '^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$') (
            "$ExpectedName has an invalid Thunderstore dependency reference: $dependency")
    }

    return [pscustomobject]@{
        Object = $manifest
        Dependencies = $dependencies
        Sha256 = Get-Sha256Hex $Path
    }
}

function Get-AssemblyStringAttribute {
    param(
        [Parameter(Mandatory = $true)]$AssemblyDefinition,
        [Parameter(Mandatory = $true)][string]$AttributeTypeName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $matches = @($AssemblyDefinition.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -ceq $AttributeTypeName
    })
    Require ($matches.Count -eq 1) (
        "$Label must contain exactly one $AttributeTypeName attribute; found $($matches.Count).")
    Require ($matches[0].ConstructorArguments.Count -eq 1) (
        "$Label has an unexpected $AttributeTypeName constructor.")
    return [string]$matches[0].ConstructorArguments[0].Value
}

function Get-DllReleaseMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedName,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    Require (Test-Path -LiteralPath $Path -PathType Leaf) (
        "$ExpectedName build did not produce its expected DLL: $Path")
    $definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        $assemblyName = [string]$definition.Name.Name
        $assemblyVersion = [string]$definition.Name.Version
        Require ($assemblyName -ceq $ExpectedName) (
            "$ExpectedName DLL assembly identity is '$assemblyName'.")
        Require ($assemblyVersion -ceq ($ExpectedVersion + '.0')) (
            "$ExpectedName assembly version is '$assemblyVersion'; expected '$ExpectedVersion.0'.")

        $fileVersion = Get-AssemblyStringAttribute `
            -AssemblyDefinition $definition `
            -AttributeTypeName 'System.Reflection.AssemblyFileVersionAttribute' `
            -Label $ExpectedName
        $informationalVersion = Get-AssemblyStringAttribute `
            -AssemblyDefinition $definition `
            -AttributeTypeName 'System.Reflection.AssemblyInformationalVersionAttribute' `
            -Label $ExpectedName
        Require ($fileVersion -ceq ($ExpectedVersion + '.0')) (
            "$ExpectedName file version is '$fileVersion'; expected '$ExpectedVersion.0'.")
        Require ($informationalVersion -ceq $ExpectedVersion) (
            "$ExpectedName informational version is '$informationalVersion'; expected '$ExpectedVersion'.")

        $pluginAttributes = @()
        $pendingTypes = New-Object System.Collections.Queue
        foreach ($type in $definition.MainModule.Types) {
            $pendingTypes.Enqueue($type)
        }
        while ($pendingTypes.Count -gt 0) {
            $type = $pendingTypes.Dequeue()
            foreach ($nestedType in $type.NestedTypes) {
                $pendingTypes.Enqueue($nestedType)
            }
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -ceq 'BepInEx.BepInPlugin') {
                    $pluginAttributes += [pscustomobject]@{
                        Type = [string]$type.FullName
                        Attribute = $attribute
                    }
                }
            }
        }
        Require ($pluginAttributes.Count -eq 1) (
            "$ExpectedName DLL must contain exactly one BepInPlugin attribute; found $($pluginAttributes.Count).")
        $pluginAttribute = $pluginAttributes[0].Attribute
        Require ($pluginAttribute.ConstructorArguments.Count -eq 3) (
            "$ExpectedName BepInPlugin attribute has an unexpected constructor.")
        $pluginGuid = [string]$pluginAttribute.ConstructorArguments[0].Value
        $pluginName = [string]$pluginAttribute.ConstructorArguments[1].Value
        $pluginVersion = [string]$pluginAttribute.ConstructorArguments[2].Value
        Require (-not [string]::IsNullOrWhiteSpace($pluginGuid)) (
            "$ExpectedName BepInPlugin GUID is empty.")
        Require (-not [string]::IsNullOrWhiteSpace($pluginName)) (
            "$ExpectedName BepInPlugin display name is empty.")
        Require ($pluginVersion -ceq $ExpectedVersion) (
            "$ExpectedName BepInPlugin version is '$pluginVersion'; expected '$ExpectedVersion'.")

        return [pscustomobject]@{
            AssemblyName = $assemblyName
            AssemblyVersion = $assemblyVersion
            FileVersion = $fileVersion
            InformationalVersion = $informationalVersion
            PluginType = [string]$pluginAttributes[0].Type
            PluginGuid = $pluginGuid
            PluginName = $pluginName
            PluginVersion = $pluginVersion
            Sha256 = Get-Sha256Hex $Path
        }
    }
    finally {
        $definition.Dispose()
    }
}

function New-SourceEntry {
    param(
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return [pscustomobject]@{
        EntryName = $EntryName
        Path = Get-NormalizedFullPath $Path
    }
}

function Assert-SourceEntries {
    param(
        [Parameter(Mandatory = $true)][object[]]$Sources,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Require ($Sources.Count -gt 0) "$Label has no package sources."
    $names = @($Sources | ForEach-Object { [string]$_.EntryName })
    Assert-UniqueStrings $names "$Label ZIP entries"
    foreach ($source in $Sources) {
        $entryName = [string]$source.EntryName
        Require (-not [string]::IsNullOrWhiteSpace($entryName)) (
            "$Label has an empty ZIP entry name.")
        Require ($entryName.IndexOfAny([char[]]@('/', '\')) -lt 0) (
            "$Label contains a non-root ZIP entry: $entryName")
        Require (-not $entryName.Contains(':') -and $entryName -cne '.' -and $entryName -cne '..') (
            "$Label contains an unsafe ZIP entry: $entryName")
        Require (Test-Path -LiteralPath $source.Path -PathType Leaf) (
            "$Label package source is missing: $($source.Path)")
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][object[]]$Sources,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Assert-SourceEntries $Sources (Split-Path -Leaf $Destination)
    Require (-not (Test-Path -LiteralPath $Destination)) (
        "Deterministic ZIP creation never overwrites: $Destination")

    $sourceByName = @{}
    foreach ($source in $Sources) {
        $sourceByName[[string]$source.EntryName] = [string]$source.Path
    }
    [string[]]$entryNames = Get-OrdinalSortedStrings @($sourceByName.Keys)

    $output = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive(
            $output,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($entryName in $entryNames) {
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedZipTimestamp
                $input = [System.IO.File]::OpenRead($sourceByName[$entryName])
                $entryOutput = $entry.Open()
                try {
                    $input.CopyTo($entryOutput)
                }
                finally {
                    $entryOutput.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $output.Dispose()
    }
}

function Assert-ZipMatchesSources {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object[]]$Sources,
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][bool]$ExpectDll
    )

    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Missing ZIP: $Path"
    Assert-SourceEntries $Sources (Split-Path -Leaf $Path)
    $sourceByName = @{}
    foreach ($source in $Sources) {
        $sourceByName[[string]$source.EntryName] = [string]$source.Path
    }
    [string[]]$expectedNames = Get-OrdinalSortedStrings @($sourceByName.Keys)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries)
        [string[]]$actualNames = @($entries | ForEach-Object { [string]$_.FullName })
        Assert-ExactSequence $actualNames $expectedNames "$(Split-Path -Leaf $Path) entries"
        Assert-UniqueStrings $actualNames "$(Split-Path -Leaf $Path) entries"

        foreach ($entry in $entries) {
            $entryName = [string]$entry.FullName
            Require ($entryName.IndexOfAny([char[]]@('/', '\')) -lt 0) (
                "$(Split-Path -Leaf $Path) contains a non-root entry: $entryName")
            Require (-not $entryName.Contains(':') -and
                $entryName -cne '.' -and $entryName -cne '..') (
                "$(Split-Path -Leaf $Path) contains an unsafe entry: $entryName")
            Require ($entry.LastWriteTime.Year -eq 2000 -and
                $entry.LastWriteTime.Month -eq 1 -and
                $entry.LastWriteTime.Day -eq 1 -and
                $entry.LastWriteTime.Hour -eq 0 -and
                $entry.LastWriteTime.Minute -eq 0 -and
                $entry.LastWriteTime.Second -eq 0) (
                "$(Split-Path -Leaf $Path) has a non-deterministic timestamp: $entryName")
            $entryInput = $entry.Open()
            try {
                $entryHash = Get-StreamSha256Hex $entryInput
            }
            finally {
                $entryInput.Dispose()
            }
            Require ($entryHash -ceq (Get-Sha256Hex $sourceByName[$entryName])) (
                "$(Split-Path -Leaf $Path) entry bytes differ from source: $entryName")
        }

        foreach ($requiredEntry in @('manifest.json', 'README.md', 'icon.png', 'CHANGELOG.md')) {
            Require ($actualNames -ccontains $requiredEntry) (
                "$(Split-Path -Leaf $Path) is missing root $requiredEntry.")
        }
        [string[]]$dllEntries = @($actualNames | Where-Object { $_ -match '(?i)\.dll$' })
        if ($ExpectDll) {
            Require ($dllEntries.Count -eq 1 -and
                $dllEntries[0] -ceq ($PackageName + '.dll')) (
                "$(Split-Path -Leaf $Path) must contain only $PackageName.dll as its DLL.")
        }
        else {
            Require ($dllEntries.Count -eq 0) (
                "Dependency-only suite $(Split-Path -Leaf $Path) unexpectedly contains a DLL.")
        }

        $manifestEntry = $archive.GetEntry('manifest.json')
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open(), $utf8NoBom, $true)
        try {
            $zippedManifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }
        Require ([string]$zippedManifest.name -ceq $PackageName) (
            "$(Split-Path -Leaf $Path) manifest identity changed in the ZIP.")
        Require ([string]$zippedManifest.version_number -ceq $PackageVersion) (
            "$(Split-Path -Leaf $Path) manifest version changed in the ZIP.")
    }
    finally {
        $archive.Dispose()
    }
}

function New-VerifiedPackageZip {
    param(
        [Parameter(Mandatory = $true)][object[]]$Sources,
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][bool]$ExpectDll,
        [Parameter(Mandatory = $true)][string]$PackagesRoot
    )

    $leaf = "$namespace-$PackageName-$PackageVersion.zip"
    $first = Join-Path $PackagesRoot ('.' + $leaf + '.first')
    $second = Join-Path $PackagesRoot ('.' + $leaf + '.second')
    $final = Join-Path $PackagesRoot $leaf
    foreach ($path in @($first, $second, $final)) {
        Require (-not (Test-Path -LiteralPath $path)) "Package output already exists: $path"
    }

    New-DeterministicZip $Sources $first
    New-DeterministicZip $Sources $second
    $firstHash = Get-Sha256Hex $first
    $secondHash = Get-Sha256Hex $second
    Require ($firstHash -ceq $secondHash) (
        "$PackageName ZIP output was not byte-for-byte reproducible across two builds.")
    Assert-ZipMatchesSources $first $Sources $PackageName $PackageVersion $ExpectDll
    Assert-ZipMatchesSources $second $Sources $PackageName $PackageVersion $ExpectDll
    Move-Item -LiteralPath $first -Destination $final
    Remove-Item -LiteralPath $second -Force

    return [pscustomobject]@{
        File = $leaf
        Path = $final
        Sha256 = Get-Sha256Hex $final
        DeterministicProbeSha256 = $secondHash
        EntryCount = $Sources.Count
        Entries = @($Sources | Sort-Object EntryName | ForEach-Object {
            [ordered]@{
                name = [string]$_.EntryName
                source = [string]$_.Path
                source_sha256 = Get-Sha256Hex ([string]$_.Path)
            }
        })
    }
}

function Invoke-ReleaseBuild {
    param(
        [Parameter(Mandatory = $true)]$Spec,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$ValheimRoot,
        [Parameter(Mandatory = $true)][string]$BepInExRoot
    )

    Require (Test-Path -LiteralPath $Spec.ProjectPath -PathType Leaf) (
        "$($Spec.Name) project is missing: $($Spec.ProjectPath)")
    $arguments = @(
        'build', [string]$Spec.ProjectPath,
        '--configuration', 'Release',
        '--nologo',
        '--verbosity', 'minimal',
        "-p:VALHEIM_INSTALL=$ValheimRoot",
        "-p:BEPINEX_PROFILE=$BepInExRoot"
    )
    Write-Host "Building $($Spec.Name) $($Spec.Version)..."
    $nativeOutput = @(& dotnet @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    [string[]]$lines = @($nativeOutput | ForEach-Object { [string]$_ })
    [System.IO.File]::WriteAllLines($LogPath, $lines, $utf8NoBom)
    foreach ($line in $lines) {
        Write-Host $line
    }
    Require ($exitCode -eq 0) (
        "$($Spec.Name) Release build failed with exit code $exitCode. See $LogPath")
    return [pscustomobject]@{
        exit_code = $exitCode
        log = $LogPath
        log_sha256 = Get-Sha256Hex $LogPath
    }
}

$repoRoot = Get-NormalizedFullPath (Join-Path $PSScriptRoot '..')
$workspaceRoot = Get-NormalizedFullPath (Split-Path -Parent $repoRoot)
$displayRoot = Get-NormalizedFullPath (Join-Path $workspaceRoot 'StandaloneItemStands')
$expectedDestination = Get-NormalizedFullPath (
    (Join-Path $workspaceRoot 'Latest Runic Mods'))
$destinationRoot = Get-NormalizedFullPath $Destination
$valheimRoot = Get-NormalizedFullPath $ValheimInstall
$bepInExRoot = Get-NormalizedFullPath $BepInExProfile

Assert-OrdinaryDirectory $repoRoot 'Repository root'
Assert-OrdinaryDirectory $workspaceRoot 'Workspace root'
Assert-OrdinaryDirectory $displayRoot 'RunicDisplayStands source root'
Assert-OrdinaryDirectory $valheimRoot 'Valheim installation'
Assert-OrdinaryDirectory $bepInExRoot 'BepInEx profile'
Require (Test-PathEquals $destinationRoot $expectedDestination) (
    "This release builder only replaces the requested destination: $expectedDestination")
Require (Test-IsStrictChildPath $destinationRoot $workspaceRoot) (
    "Destination escaped the workspace root: $destinationRoot")

$valheimExe = Join-Path $valheimRoot 'valheim.exe'
$valheimAssembly = Join-Path $valheimRoot 'valheim_Data\Managed\assembly_valheim.dll'
$bepInExDll = Join-Path $bepInExRoot 'core\BepInEx.dll'
$harmonyDll = Join-Path $bepInExRoot 'core\0Harmony.dll'
$cecilDll = Join-Path $bepInExRoot 'core\Mono.Cecil.dll'
foreach ($requiredFile in @($valheimExe, $valheimAssembly, $bepInExDll, $harmonyDll, $cecilDll)) {
    Require (Test-Path -LiteralPath $requiredFile -PathType Leaf) (
        "Required Valheim 1.0 build input is missing: $requiredFile")
}
$bepInExVersion = [string](Get-Item -LiteralPath $bepInExDll).VersionInfo.FileVersion
Require ($bepInExVersion -ceq $expectedBepInExFileVersion) (
    "BEPINEX_PROFILE contains BepInEx $bepInExVersion; expected $expectedBepInExFileVersion from $expectedLoaderDependency.")
$valheimAssemblySha256 = Get-Sha256Hex $valheimAssembly
Require ($valheimAssemblySha256 -ceq $expectedValheimAssemblySha256) (
    "VALHEIM_INSTALL is not the audited Valheim 1.0 client build. assembly_valheim.dll is $valheimAssemblySha256; expected $expectedValheimAssemblySha256.")
Add-Type -LiteralPath $cecilDll
Require ($null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)) (
    'The dotnet CLI is required to build the release packages.')
$dotnetVersion = [string](& dotnet --version)
Require ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($dotnetVersion)) (
    'Could not determine the dotnet SDK version.')

$expectedVersions = [ordered]@{
    RunicAgriculture = '1.0.3'
    RunicAwareness = '1.0.2'
    RunicBuildCamera = '1.0.3'
    RunicCharacterVault = '1.0.2'
    RunicClock = '1.0.1'
    RunicCrafting = '1.0.8'
    RunicDisplayStands = '1.3.4'
    RunicExploration = '1.0.2'
    RunicInteraction = '1.0.4'
    RunicInventory = '1.1.1'
    RunicPortals = '1.2.4'
    RunicPrecisionBuildTool = '2.0.4'
    RunicProduction = '1.0.5'
    RunicSafety = '1.0.4'
    RunicSentinel = '1.3.2'
    RunicSentinelClient = '1.0.1'
    RunicSentinelServer = '1.0.2'
    RunicStorage = '1.0.6'
    RunicVelocity = '1.0.2'
    RunicWorldEngine = '1.1.2'
    RunicModSuite = '1.2.28'
    RunicModClientSuite = '1.0.18'
    RunicModServerSuite = '1.0.13'
}

$canonicalNames = @(
    'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCharacterVault', 'RunicClock', 'RunicCrafting',
    'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
    'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
    'RunicSentinel', 'RunicStorage', 'RunicVelocity', 'RunicWorldEngine'
)
$dllPackageNames = @(
    'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCharacterVault', 'RunicClock', 'RunicCrafting',
    'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
    'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
    'RunicSentinel', 'RunicSentinelClient', 'RunicSentinelServer', 'RunicStorage',
    'RunicVelocity', 'RunicWorldEngine'
)
$suiteNames = @('RunicModSuite', 'RunicModClientSuite', 'RunicModServerSuite')
Assert-UniqueStrings $canonicalNames 'canonical package identities'
Assert-UniqueStrings $dllPackageNames 'DLL package identities'
Require ($canonicalNames.Count -eq 18) 'The canonical release set must contain exactly 18 mods.'
Require ($dllPackageNames.Count -eq 20) 'The DLL release set must contain exactly 20 packages.'

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$artifactRoot = Get-NormalizedFullPath (
    (Join-Path $repoRoot "artifacts\Valheim1.0\RunicRelease-$runId"))
$artifactPackagesRoot = Join-Path $artifactRoot 'Packages'
$buildLogsRoot = Join-Path $artifactRoot 'BuildLogs'
$stageRoot = Get-NormalizedFullPath (
    (Join-Path $workspaceRoot ('.Latest Runic Mods.stage-' + $runId)))
Require (Test-IsStrictChildPath $artifactRoot $repoRoot) 'Artifact root escaped the repository.'
Require (Test-IsStrictChildPath $stageRoot $workspaceRoot) 'Staging root escaped the workspace.'
foreach ($newDirectory in @($artifactRoot, $artifactPackagesRoot, $buildLogsRoot, $stageRoot)) {
    Require (-not (Test-Path -LiteralPath $newDirectory)) (
        "Immutable release path already exists: $newDirectory")
}
New-Item -ItemType Directory -Path $artifactPackagesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $buildLogsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$specs = @()
foreach ($name in $dllPackageNames) {
    $moduleRoot = if ($name -ceq 'RunicDisplayStands') {
        $displayRoot
    }
    else {
        Get-NormalizedFullPath (Join-Path $repoRoot $name)
    }
    Require ((Test-IsStrictChildPath $moduleRoot $repoRoot) -or
        (Test-PathEquals $moduleRoot $displayRoot)) (
        "$name source root is outside its approved location: $moduleRoot")
    Assert-OrdinaryDirectory $moduleRoot "$name source root"
    $iconPath = if ($name -ceq 'RunicPrecisionBuildTool') {
        Join-Path $moduleRoot 'media\icon.png'
    }
    else {
        Join-Path $moduleRoot 'icon.png'
    }
    $specs += [pscustomobject]@{
        Name = $name
        Version = [string]$expectedVersions[$name]
        Root = $moduleRoot
        ProjectPath = Join-Path $moduleRoot ($name + '.csproj')
        DllPath = Join-Path $moduleRoot "bin\Release\netstandard2.1\$name.dll"
        ManifestPath = Join-Path $moduleRoot 'manifest.json'
        IconPath = $iconPath
    }
}

$buildResults = @{}
foreach ($spec in $specs) {
    $logPath = Join-Path $buildLogsRoot ($spec.Name + '.log')
    $buildResults[$spec.Name] = Invoke-ReleaseBuild `
        -Spec $spec `
        -LogPath $logPath `
        -ValheimRoot $valheimRoot `
        -BepInExRoot $bepInExRoot
}

$packageMetadata = @{}
foreach ($spec in $specs) {
    $manifestResult = Read-Manifest `
        -Path $spec.ManifestPath `
        -ExpectedName $spec.Name `
        -ExpectedVersion $spec.Version
    $loaderDependencies = @($manifestResult.Dependencies | Where-Object {
        $_ -match '^denikson-BepInExPack_Valheim-'
    })
    Require ($loaderDependencies.Count -eq 1 -and
        $loaderDependencies[0] -ceq $expectedLoaderDependency) (
        "$($spec.Name) must depend on exactly $expectedLoaderDependency.")
    Assert-Png256 $spec.IconPath "$($spec.Name) icon"
    $dllMetadata = Get-DllReleaseMetadata `
        -Path $spec.DllPath `
        -ExpectedName $spec.Name `
        -ExpectedVersion $spec.Version
    $packageMetadata[$spec.Name] = [pscustomobject]@{
        Name = $spec.Name
        Version = $spec.Version
        Root = $spec.Root
        Manifest = $manifestResult
        Dll = $dllMetadata
        Spec = $spec
    }
}

foreach ($metadata in $packageMetadata.Values) {
    foreach ($dependency in $metadata.Manifest.Dependencies) {
        if ($dependency -notmatch '^Chazman-(?<package>[A-Za-z0-9_]+)-(?<version>\d+\.\d+\.\d+)$') {
            continue
        }
        $dependencyName = [string]$Matches.package
        $dependencyVersion = [string]$Matches.version
        Require ($packageMetadata.ContainsKey($dependencyName)) (
            "$($metadata.Name) references a local package absent from this release: $dependencyName")
        Require ([string]$packageMetadata[$dependencyName].Version -ceq $dependencyVersion) (
            "$($metadata.Name) pins $dependencyName $dependencyVersion; this release contains $($packageMetadata[$dependencyName].Version).")
        Require ($dependencyName -cne $metadata.Name) (
            "$($metadata.Name) contains a self-dependency.")
    }
}

$packageRecords = @()
foreach ($spec in $specs) {
    $metadata = $packageMetadata[$spec.Name]
    $sources = @(
        (New-SourceEntry ($spec.Name + '.dll') $spec.DllPath),
        (New-SourceEntry 'manifest.json' $spec.ManifestPath),
        (New-SourceEntry 'README.md' (Join-Path $spec.Root 'README.md')),
        (New-SourceEntry 'icon.png' $spec.IconPath),
        (New-SourceEntry 'CHANGELOG.md' (Join-Path $spec.Root 'CHANGELOG.md'))
    )
    foreach ($configPath in @(Get-ChildItem -LiteralPath $spec.Root -File -Filter '*.cfg.example' |
        ForEach-Object { $_.FullName })) {
        $sources += New-SourceEntry (Split-Path -Leaf $configPath) $configPath
    }
    foreach ($optionalDocName in @('INSTRUCTIONS.md', 'TESTING.md', 'LICENSE', 'LICENSE.md', 'NOTICE', 'NOTICE.md')) {
        $optionalDocPath = Join-Path $spec.Root $optionalDocName
        if (Test-Path -LiteralPath $optionalDocPath -PathType Leaf) {
            $sources += New-SourceEntry $optionalDocName $optionalDocPath
        }
    }
    $zipResult = New-VerifiedPackageZip `
        -Sources $sources `
        -PackageName $spec.Name `
        -PackageVersion $spec.Version `
        -ExpectDll $true `
        -PackagesRoot $artifactPackagesRoot
    $packageRecords += [pscustomobject]@{
        Name = $spec.Name
        Version = $spec.Version
        Kind = if ($canonicalNames -ccontains $spec.Name) { 'canonical-mod' } else { 'sentinel-role-variant' }
        Dependencies = @($metadata.Manifest.Dependencies)
        ManifestSha256 = $metadata.Manifest.Sha256
        Dll = $metadata.Dll
        Build = $buildResults[$spec.Name]
        Zip = $zipResult
    }
}

$expectedSuiteDependencies = [ordered]@{
    RunicModSuite = @(
        'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCharacterVault', 'RunicClock', 'RunicCrafting',
        'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
        'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
        'RunicSentinel', 'RunicStorage', 'RunicVelocity', 'RunicWorldEngine'
    )
    RunicModClientSuite = @(
        'RunicAgriculture', 'RunicAwareness', 'RunicBuildCamera', 'RunicCharacterVault', 'RunicClock', 'RunicCrafting',
        'RunicDisplayStands', 'RunicExploration', 'RunicInteraction', 'RunicInventory',
        'RunicPortals', 'RunicPrecisionBuildTool', 'RunicProduction', 'RunicSafety',
        'RunicSentinelClient', 'RunicStorage', 'RunicVelocity', 'RunicWorldEngine'
    )
    RunicModServerSuite = @(
        'RunicCharacterVault', 'RunicDisplayStands', 'RunicPortals', 'RunicProduction', 'RunicSafety',
        'RunicSentinelServer', 'RunicVelocity', 'RunicWorldEngine'
    )
}

foreach ($suiteName in $suiteNames) {
    $suiteRoot = Get-NormalizedFullPath (Join-Path $repoRoot $suiteName)
    Require (Test-IsStrictChildPath $suiteRoot $repoRoot) (
        "$suiteName source root escaped the repository.")
    Assert-OrdinaryDirectory $suiteRoot "$suiteName source root"
    $suiteVersion = [string]$expectedVersions[$suiteName]
    $manifestPath = Join-Path $suiteRoot 'manifest.json'
    $manifestResult = Read-Manifest $manifestPath $suiteName $suiteVersion
    [string[]]$expectedDependencies = @(
        $expectedSuiteDependencies[$suiteName] | ForEach-Object {
            "$namespace-$_-$($expectedVersions[$_])"
        })
    if ($suiteName -cne 'RunicModServerSuite') {
        $expectedDependencies += 'shudnal-ConfigurationManager-1.1.17'
    }
    Assert-ExactSequence `
        @($manifestResult.Dependencies) `
        @($expectedDependencies) `
        "$suiteName dependency pins"
    Assert-Png256 (Join-Path $suiteRoot 'icon.png') "$suiteName icon"

    $sources = @(
        (New-SourceEntry 'manifest.json' $manifestPath),
        (New-SourceEntry 'README.md' (Join-Path $suiteRoot 'README.md')),
        (New-SourceEntry 'icon.png' (Join-Path $suiteRoot 'icon.png')),
        (New-SourceEntry 'CHANGELOG.md' (Join-Path $suiteRoot 'CHANGELOG.md'))
    )
    foreach ($optionalDocName in @('INSTRUCTIONS.md', 'LICENSE', 'LICENSE.md', 'NOTICE', 'NOTICE.md')) {
        $optionalDocPath = Join-Path $suiteRoot $optionalDocName
        if (Test-Path -LiteralPath $optionalDocPath -PathType Leaf) {
            $sources += New-SourceEntry $optionalDocName $optionalDocPath
        }
    }
    $zipResult = New-VerifiedPackageZip `
        -Sources $sources `
        -PackageName $suiteName `
        -PackageVersion $suiteVersion `
        -ExpectDll $false `
        -PackagesRoot $artifactPackagesRoot
    $packageRecords += [pscustomobject]@{
        Name = $suiteName
        Version = $suiteVersion
        Kind = 'dependency-only-suite'
        Dependencies = @($manifestResult.Dependencies)
        ManifestSha256 = $manifestResult.Sha256
        Dll = $null
        Build = $null
        Zip = $zipResult
    }
}

$expectedIdentities = Get-OrdinalSortedStrings @($dllPackageNames + $suiteNames)
$actualIdentities = Get-OrdinalSortedStrings @($packageRecords | ForEach-Object { $_.Name })
Assert-ExactSequence $actualIdentities $expectedIdentities 'release package identities'
Require ($packageRecords.Count -eq 23) 'The final release must contain exactly 23 unique ZIPs.'
Assert-UniqueStrings @($packageRecords | ForEach-Object { $_.Name }) 'release package identities'

$artifactZipFiles = @(Get-ChildItem -LiteralPath $artifactPackagesRoot -File -Filter '*.zip')
Require ($artifactZipFiles.Count -eq 23) (
    "Artifact package directory contains $($artifactZipFiles.Count) ZIPs; expected 23.")
foreach ($record in $packageRecords) {
    $destinationZip = Join-Path $stageRoot $record.Zip.File
    Copy-Item -LiteralPath $record.Zip.Path -Destination $destinationZip
    Require ((Get-Sha256Hex $destinationZip) -ceq $record.Zip.Sha256) (
        "$($record.Zip.File) changed while being copied to final staging.")
}

$checksumLines = @($packageRecords | Sort-Object { $_.Zip.File } | ForEach-Object {
    $_.Zip.Sha256 + '  ' + $_.Zip.File
})
$checksumsText = ($checksumLines -join "`n") + "`n"
$artifactChecksums = Join-Path $artifactRoot 'SHA256SUMS.txt'
Write-NewUtf8Text $artifactChecksums $checksumsText
Write-NewUtf8Text (Join-Path $stageRoot 'SHA256SUMS.txt') $checksumsText

$versionLines = @($packageRecords | Sort-Object Name | ForEach-Object {
    "| $($_.Name) | $($_.Version) | $($_.Kind) |"
})
$readmeLines = @(
    '# Latest Runic Mods — Valheim 1.0',
    '',
    "This validated release contains all 18 canonical Runic mods, both Sentinel role variants, and all three suite packages. Every DLL was rebuilt against the installed Valheim 1.0 assemblies and BepInEx 5.4.23.5. No package was uploaded by this script.",
    '',
    'Choose exactly one suite:',
    '',
    "- **RunicModSuite $($expectedVersions.RunicModSuite)**: all 18 canonical mods, including full RunicSentinel and RunicCharacterVault. Best for listen hosts and full admin installs.",
    "- **RunicModClientSuite $($expectedVersions.RunicModClientSuite)**: the player-facing mods plus RunicSentinelClient and RunicCharacterVault. Give this to players joining a dedicated server.",
    "- **RunicModServerSuite $($expectedVersions.RunicModServerSuite)**: the eight server-relevant packages, including RunicSentinelServer and RunicCharacterVault. Install this on the dedicated server.",
    '',
    'Do not install RunicSentinel and RunicSentinelServer together. They are alternative authority implementations. The client suite uses RunicSentinelClient instead.',
    '',
    '| Package | Version | Type |',
    '|---|---:|---|'
) + $versionLines + @(
    '',
    'Use `SHA256SUMS.txt` to verify ZIP bytes. `COLLECTION-EVIDENCE.json` records build inputs, plugin and assembly versions, dependency pins, deterministic packaging proof, and source hashes.',
    ''
)
$readmeText = $readmeLines -join "`n"
$artifactReadme = Join-Path $artifactRoot 'README.md'
Write-NewUtf8Text $artifactReadme $readmeText
Write-NewUtf8Text (Join-Path $stageRoot 'README.md') $readmeText

$archivePath = $null
if (Test-Path -LiteralPath $destinationRoot) {
    Require (Test-Path -LiteralPath $destinationRoot -PathType Container) (
        "Existing destination is not a directory: $destinationRoot")
    $destinationItem = Get-Item -LiteralPath $destinationRoot -Force
    Require (($destinationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) (
        "Existing destination is a junction or symbolic link and will not be moved: $destinationRoot")
    $archivePath = Get-NormalizedFullPath (
        (Join-Path $workspaceRoot ("Latest Runic Mods.archive-$runId")))
    Require (Test-IsStrictChildPath $archivePath $workspaceRoot) (
        'Destination archive path escaped the workspace.')
    Require (-not (Test-Path -LiteralPath $archivePath)) (
        "Destination archive path already exists: $archivePath")
}

$evidencePackages = @($packageRecords | Sort-Object Name | ForEach-Object {
    $dllEvidence = $null
    if ($null -ne $_.Dll) {
        $dllEvidence = [ordered]@{
            sha256 = $_.Dll.Sha256
            assembly_name = $_.Dll.AssemblyName
            assembly_version = $_.Dll.AssemblyVersion
            file_version = $_.Dll.FileVersion
            informational_version = $_.Dll.InformationalVersion
            plugin_type = $_.Dll.PluginType
            plugin_guid = $_.Dll.PluginGuid
            plugin_name = $_.Dll.PluginName
            plugin_version = $_.Dll.PluginVersion
        }
    }
    $buildEvidence = $null
    if ($null -ne $_.Build) {
        $buildEvidence = [ordered]@{
            exit_code = $_.Build.exit_code
            log = $_.Build.log
            log_sha256 = $_.Build.log_sha256
        }
    }
    [ordered]@{
        name = $_.Name
        version = $_.Version
        kind = $_.Kind
        file = $_.Zip.File
        zip_sha256 = $_.Zip.Sha256
        deterministic_second_build_sha256 = $_.Zip.DeterministicProbeSha256
        deterministic_match = ($_.Zip.Sha256 -ceq $_.Zip.DeterministicProbeSha256)
        manifest_sha256 = $_.ManifestSha256
        dependencies = @($_.Dependencies)
        zip_entry_count = $_.Zip.EntryCount
        zip_entries = @($_.Zip.Entries)
        dll = $dllEvidence
        build = $buildEvidence
    }
})
$evidence = [ordered]@{
    schema = 'runic-valheim-1.0-release/v1'
    status = 'PASS'
    created_utc = [DateTime]::UtcNow.ToString('o')
    run_id = $runId
    release_builder = $MyInvocation.MyCommand.Path
    release_builder_sha256 = Get-Sha256Hex $MyInvocation.MyCommand.Path
    repository_root = $repoRoot
    display_stands_source_root = $displayRoot
    artifact_root = $artifactRoot
    destination = $destinationRoot
    previous_destination_archive = $archivePath
    thunderstore_upload_performed = $false
    counts = [ordered]@{
        canonical_mods = 18
        dll_packages = 20
        dependency_only_suites = 3
        unique_zip_packages = 23
    }
    build_environment = [ordered]@{
        dotnet_sdk = $dotnetVersion.Trim()
        valheim_install = $valheimRoot
        valheim_exe_file_version = [string](Get-Item -LiteralPath $valheimExe).VersionInfo.FileVersion
        assembly_valheim_sha256 = $valheimAssemblySha256
        expected_assembly_valheim_sha256 = $expectedValheimAssemblySha256
        bepinex_profile = $bepInExRoot
        bepinex_file_version = $bepInExVersion
        bepinex_sha256 = Get-Sha256Hex $bepInExDll
        harmony_sha256 = Get-Sha256Hex $harmonyDll
        required_loader_dependency = $expectedLoaderDependency
    }
    suite_membership = [ordered]@{
        full = @($expectedSuiteDependencies.RunicModSuite)
        client = @($expectedSuiteDependencies.RunicModClientSuite)
        server = @($expectedSuiteDependencies.RunicModServerSuite)
    }
    validations = @(
        'all 20 DLL projects built successfully in Release configuration'
        'all 18 canonical identities and both Sentinel role variants are present'
        'manifest, assembly, file, informational, and BepInPlugin versions match the pinned release map'
        'every DLL package pins denikson-BepInExPack_Valheim-5.4.2350 exactly once'
        'all local Chazman dependency versions resolve to packages in this collection'
        'all three dependency-only suites have exact role membership and exact version pins'
        'all icons are valid 256x256 PNG files'
        'all ZIP entries are safe root entries and match their source bytes'
        'every ZIP reproduced byte-for-byte across two independent archive creations'
        'the final collection contains exactly 23 ZIP packages'
    )
    packages = $evidencePackages
}
$evidenceText = ($evidence | ConvertTo-Json -Depth 12) + "`n"
$artifactEvidence = Join-Path $artifactRoot 'COLLECTION-EVIDENCE.json'
Write-NewUtf8Text $artifactEvidence $evidenceText
Write-NewUtf8Text (Join-Path $stageRoot 'COLLECTION-EVIDENCE.json') $evidenceText

$stageZipFiles = @(Get-ChildItem -LiteralPath $stageRoot -File -Filter '*.zip')
Require ($stageZipFiles.Count -eq 23) (
    "Final stage contains $($stageZipFiles.Count) ZIPs; expected 23.")
$stageTopLevelFiles = @(Get-ChildItem -LiteralPath $stageRoot -File)
Require ($stageTopLevelFiles.Count -eq 25) (
    "Final stage contains $($stageTopLevelFiles.Count) files; expected 25.")
foreach ($record in $packageRecords) {
    $stagedZip = Join-Path $stageRoot $record.Zip.File
    Require ((Get-Sha256Hex $stagedZip) -ceq $record.Zip.Sha256) (
        "Final-stage checksum mismatch: $($record.Zip.File)")
}

$destinationWasArchived = $false
try {
    if ($null -ne $archivePath) {
        Write-Host "Archiving the existing collection to $archivePath"
        Move-Item -LiteralPath $destinationRoot -Destination $archivePath
        $destinationWasArchived = $true
    }
    Require (-not (Test-Path -LiteralPath $destinationRoot)) (
        "Destination appeared during the atomic replacement: $destinationRoot")
    Write-Host "Promoting validated staging directory to $destinationRoot"
    Move-Item -LiteralPath $stageRoot -Destination $destinationRoot
}
catch {
    $swapError = $_
    if ($destinationWasArchived -and
        -not (Test-Path -LiteralPath $destinationRoot) -and
        (Test-Path -LiteralPath $archivePath -PathType Container)) {
        try {
            Move-Item -LiteralPath $archivePath -Destination $destinationRoot
            $destinationWasArchived = $false
        }
        catch {
            throw "Release promotion failed and automatic restoration also failed. Promotion: $($swapError.Exception.Message) Restoration: $($_.Exception.Message) Archived collection: $archivePath"
        }
    }
    throw "Release promotion failed; the previous destination was restored when possible. $($swapError.Exception.Message)"
}

Require (Test-Path -LiteralPath $destinationRoot -PathType Container) (
    'The validated collection was not promoted to the requested destination.')
foreach ($record in $packageRecords) {
    $finalZip = Join-Path $destinationRoot $record.Zip.File
    Require ((Get-Sha256Hex $finalZip) -ceq $record.Zip.Sha256) (
        "Post-promotion checksum mismatch: $($record.Zip.File)")
}
Require (@(Get-ChildItem -LiteralPath $destinationRoot -File -Filter '*.zip').Count -eq 23) (
    'Promoted destination does not contain exactly 23 ZIPs.')

[pscustomobject]@{
    Status = 'PASS'
    Destination = $destinationRoot
    PreviousDestinationArchive = $archivePath
    ArtifactRoot = $artifactRoot
    CanonicalMods = 18
    DllPackages = 20
    DependencyOnlySuites = 3
    UniqueZips = 23
    BepInEx = $bepInExVersion
    ThunderstoreUploadPerformed = $false
} | Format-List
