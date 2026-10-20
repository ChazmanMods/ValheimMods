[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Add-Type -Path 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll'
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory('E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed')
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$expected = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../atlas.png'))
try {
    foreach ($name in @('RunicSigns','RunicStorage')) {
        $path = Join-Path $root "$name/bin/Release/netstandard2.1/$name.dll"
        $mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $parameters)
        try {
            $atlas = $mod.MainModule.Resources | Where-Object Name -eq 'Runic.Emoji.atlas.png'
            $license = $mod.MainModule.Resources | Where-Object Name -eq 'Runic.Emoji.LICENSE.txt'
            if (!$atlas -or !$license) { throw "$name missing embedded atlas/license" }
            if ([Convert]::ToBase64String($atlas.GetResourceData()) -ne [Convert]::ToBase64String($expected)) { throw "$name atlas differs from source" }
            $resolved = 0
            foreach ($reference in $mod.MainModule.GetMemberReferences()) {
                if ($reference.DeclaringType.Scope.Name -notmatch '^(UnityEngine.*|Unity.TextMeshPro)$') { continue }
                if ($null -eq $reference.Resolve()) { throw "Unresolved Unity member: $($reference.FullName)" }
                $resolved++
            }
            if ($mod.MainModule.AssemblyReferences | Where-Object { $_.Name -eq 'RunicSigns' -or $_.Name -eq 'RunicStorage' }) { throw 'Cross-mod runtime dependency' }
            Write-Output "PASS $name embedded atlas matches source, license included, $resolved Unity/TMP members resolve, no cross-mod dependency."
        } finally { $mod.Dispose() }
    }
} finally { $resolver.Dispose() }
