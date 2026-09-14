[CmdletBinding()]
param(
    [switch]$UseCandidateBuilds,
    [string]$PackageDirectory = 'E:\Valheim Mods\Latest Runic Mods'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll'
Add-Type -AssemblyName System.IO.Compression
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$auditRoot = Join-Path $repoRoot 'artifacts/Compatibility-1.0.12'
$old = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $auditRoot 'assembly_valheim-1.0.7.dll'))
$new = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll')
function Get-AllTypes($types) { foreach ($type in $types) { $type; Get-AllTypes $type.NestedTypes } }
$removed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($type in (Get-AllTypes $old.MainModule.Types)) {
    foreach ($member in @($type.Fields) + @($type.Methods)) { $null = $removed.Add($member.FullName) }
}
foreach ($type in (Get-AllTypes $new.MainModule.Types)) {
    foreach ($member in @($type.Fields) + @($type.Methods)) { $null = $removed.Remove($member.FullName) }
}
$old.Dispose(); $new.Dispose()
$candidates = @('RunicInteraction','RunicInventory','RunicPortals','RunicSafety','RunicProduction','RunicPrecisionBuildTool')
$results = foreach ($zipPath in Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.zip') {
    $stream = [IO.File]::OpenRead($zipPath.FullName)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        foreach ($entry in $zip.Entries | Where-Object { $_.Name -like '*.dll' }) {
            $module = [IO.Path]::GetFileNameWithoutExtension($entry.Name)
            $buffer = [IO.MemoryStream]::new()
            try {
                if ($UseCandidateBuilds -and $candidates -contains $module) {
                    $definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repoRoot "$module/bin/Release/netstandard2.1/$module.dll"))
                } else {
                    $entryStream = $entry.Open()
                    try { $entryStream.CopyTo($buffer) } finally { $entryStream.Dispose() }
                    $buffer.Position = 0
                    $definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($buffer)
                }
                try {
                    $broken = @($definition.MainModule.GetMemberReferences() | Where-Object {
                        $_.DeclaringType.Scope.Name -eq 'assembly_valheim' -and $removed.Contains($_.FullName)
                    } | ForEach-Object FullName)
                    [pscustomobject]@{ Module=$module; Version=$definition.Name.Version.ToString(3); BrokenReferences=$broken }
                } finally { $definition.Dispose() }
            } finally { $buffer.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
$results | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $auditRoot $(if ($UseCandidateBuilds) { 'candidate-member-references.json' } else { 'previous-member-references.json' })) -Encoding utf8
$results | Format-Table Module, Version, @{Name='Removed API references';Expression={$_.BrokenReferences -join '; '}} -AutoSize
if ($UseCandidateBuilds -and @($results | Where-Object { $_.BrokenReferences.Count -gt 0 }).Count -gt 0) { throw 'Candidates still reference removed Valheim API members.' }
