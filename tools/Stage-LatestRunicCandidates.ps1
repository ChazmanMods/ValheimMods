[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -Path 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll'
$collectionRoot = [IO.Path]::GetFullPath('E:\Valheim Mods')
$latest = [IO.Path]::GetFullPath('E:\Valheim Mods\Latest Runic Mods')
$candidates = [IO.Path]::GetFullPath('E:\Valheim Mods\PreProduction')
if ((Split-Path $latest -Parent) -ne $collectionRoot -or (Split-Path $latest -Leaf) -ne 'Latest Runic Mods') { throw 'Unexpected collection target.' }
$expected = @('RunicAgriculture','RunicAwareness','RunicBuildCamera','RunicCharacterVault','RunicCrafting','RunicDisplayStands','RunicExploration','RunicInteraction','RunicInventory','RunicModClientSuite','RunicModServerSuite','RunicModSuite','RunicPortals','RunicPrecisionBuildTool','RunicProduction','RunicSafety','RunicSentinel','RunicSentinelClient','RunicSentinelServer','RunicStorage','RunicVelocity','RunicWorldEngine')
$all = foreach ($root in @($latest, $candidates)) {
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Filter '*.zip') {
        if ($file.Name -notmatch '^Chazman-(?<mod>Runic\w+)-(?<ver>\d+\.\d+\.\d+)\.zip$') { throw "Unexpected package: $($file.Name)" }
        if ($Matches.mod -notin $expected) { throw "Unknown module: $($Matches.mod)" }
        [pscustomobject]@{ Name=$Matches.mod; Version=[version]$Matches.ver; Path=$file.FullName; File=$file.Name; Hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
}
$selected = foreach ($name in $expected) {
    $versions = @($all | Where-Object Name -eq $name | Sort-Object Version -Descending)
    if (!$versions.Count) { throw "Missing $name" }
    $newest = @($versions | Where-Object Version -eq $versions[0].Version)
    if (@($newest.Hash | Select-Object -Unique).Count -ne 1) { throw "Conflicting bytes for $name $($versions[0].Version)" }
    $newest[0]
}
$manifestMap = @{}
foreach ($package in $selected) {
    $stream = [IO.File]::OpenRead($package.Path)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        if (@($zip.Entries.FullName | Select-Object -Unique).Count -ne $zip.Entries.Count) { throw 'Duplicate ZIP entries.' }
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(^[/\\]|(^|[/\\])\.\.([/\\]|$)|:)') { throw "Unsafe ZIP path in $($package.File)" }
        }
        foreach ($required in @('manifest.json','README.md','icon.png')) {
            if (!$zip.GetEntry($required)) { throw "Missing $required in $($package.File)" }
        }
        $reader = [IO.StreamReader]::new($zip.GetEntry('manifest.json').Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.name -cne $package.Name -or $manifest.version_number -cne $package.Version.ToString()) { throw 'Manifest/filename mismatch.' }
        $manifestMap[$package.Name] = $manifest
        $iconStream = $zip.GetEntry('icon.png').Open()
        $memory = [IO.MemoryStream]::new()
        try { $iconStream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $iconStream.Dispose(); $memory.Dispose() }
        if ($bytes.Length -lt 24 -or [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or [BitConverter]::ToString($bytes[16..23]) -ne '00-00-01-00-00-00-01-00') { throw "Invalid icon: $($package.File)" }
        if ($package.Name -notlike 'RunicMod*Suite') {
            $dlls = @($zip.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -ceq ($package.Name + '.dll') })
            if ($dlls.Count -ne 1) { throw "Expected one main DLL: $($package.File)" }
            $dllStream = $dlls[0].Open()
            $dllMemory = [IO.MemoryStream]::new()
            try {
                $dllStream.CopyTo($dllMemory)
                $dllMemory.Position = 0
                $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dllMemory)
                try {
                    if ($assembly.Name.Version.ToString(3) -cne $manifest.version_number) { throw "DLL/manifest mismatch: $($package.File)" }
                } finally { $assembly.Dispose() }
            } finally { $dllStream.Dispose(); $dllMemory.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
foreach ($manifest in $manifestMap.Values) {
    foreach ($dependency in $manifest.dependencies) {
        if ($dependency -match '^Chazman-(?<mod>Runic\w+)-(?<ver>\d+\.\d+\.\d+)$') {
            if (!$manifestMap.ContainsKey($Matches.mod) -or [version]$manifestMap[$Matches.mod].version_number -lt [version]$Matches.ver) { throw "Missing required dependency version: $dependency" }
            if ($manifest.name -like 'RunicMod*Suite' -and $manifestMap[$Matches.mod].version_number -cne $Matches.ver) { throw "Suite does not pin latest selected package: $dependency" }
        }
    }
}
$suffix = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$staging = [IO.Path]::GetFullPath((Join-Path $collectionRoot ('.runic-latest-candidates-' + $suffix)))
$archive = [IO.Path]::GetFullPath((Join-Path $collectionRoot ('Latest Runic Mods.archive-' + $suffix)))
if ((Split-Path $archive -Parent) -ne $collectionRoot -or (Split-Path $staging -Parent) -ne $collectionRoot -or (Test-Path -LiteralPath $archive)) { throw 'Unsafe staging/archive path.' }
$null = New-Item -ItemType Directory -Path $staging
foreach ($package in $selected) {
    $target = Join-Path $staging $package.File
    [IO.File]::Copy($package.Path, $target, $false)
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $package.Hash) { throw 'Staged hash mismatch.' }
}
if (@(Get-ChildItem -LiteralPath $staging -Filter '*.zip' -File).Count -ne 22) { throw 'Incomplete collection.' }
# Recoverable rename: archive the entire old collection, including its PreviousVersions folder.
Move-Item -LiteralPath $latest -Destination $archive
try { Move-Item -LiteralPath $staging -Destination $latest }
catch {
    if (!(Test-Path -LiteralPath $latest)) { Move-Item -LiteralPath $archive -Destination $latest }
    throw
}
$selected | Select-Object Name,Version,Hash | Format-Table -AutoSize
Write-Output "ARCHIVE=$archive"
Write-Output "LATEST=$latest"
Write-Output "VALIDATED=22 packages; one version per package; suite pins match selected latest versions"
