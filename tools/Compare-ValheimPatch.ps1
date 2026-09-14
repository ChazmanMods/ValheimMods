$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll'
$auditRoot = Join-Path $PSScriptRoot '..\artifacts\Compatibility-1.0.12'
$null = New-Item -ItemType Directory -Path $auditRoot -Force
$oldPath = Join-Path $auditRoot 'assembly_valheim-1.0.7.dll'
if (!(Test-Path -LiteralPath $oldPath)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\RunicInventory.Tests\bin\Release\net8.0\assembly_valheim.dll') -Destination $oldPath }
$newPath = 'E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll'
if ((Get-FileHash $oldPath).Hash -ne 'A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC') { throw 'Old audit binary is not 1.0.7.' }
$old = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($oldPath)
$new = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($newPath)
function AllTypes($types) { foreach ($type in $types) { $type; AllTypes $type.NestedTypes } }
function Body($method) {
    if (!$method.HasBody) { return '' }
    return (($method.Body.Instructions | ForEach-Object { $_.OpCode.ToString() + ' ' + [string]$_.Operand }) -join "`n")
}
$oldMethods = @{}
foreach ($type in (AllTypes $old.MainModule.Types)) { foreach ($method in $type.Methods) { $oldMethods[$method.FullName] = $method } }
$changes = foreach ($type in (AllTypes $new.MainModule.Types)) {
    foreach ($method in $type.Methods) {
        if (!$oldMethods.ContainsKey($method.FullName)) { [pscustomobject]@{ Kind='Added'; Method=$method.FullName }; continue }
        if ((Body $method) -cne (Body $oldMethods[$method.FullName])) { [pscustomobject]@{ Kind='Body'; Method=$method.FullName } }
        $oldMethods.Remove($method.FullName)
    }
}
foreach ($method in $oldMethods.Keys) { $changes += [pscustomobject]@{ Kind='Removed'; Method=$method } }
$old.Dispose(); $new.Dispose()
$changes | ConvertTo-Json -Depth 3 | Out-File (Join-Path $auditRoot 'method-changes.json') -Encoding utf8
$changes | Where-Object { $_.Method -notmatch 'Terminal|AchievementUnlockPopup|::<|/<' } | Format-Table -AutoSize | Out-String -Width 240
