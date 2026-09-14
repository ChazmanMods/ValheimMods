[CmdletBinding()]
param(
    [string]$ValheimInstall = 'E:\SteamLibrary\steamapps\common\Valheim',
    [string]$BepInExProfile = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $BepInExProfile 'core/Mono.Cecil.dll')
$managed = Join-Path $ValheimInstall 'valheim_Data/Managed'
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory((Join-Path $BepInExProfile 'core'))
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'assembly_valheim.dll'), $parameters)
$utils = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'assembly_utils.dll'), $parameters)
$types = @($game.MainModule.Types) + @($utils.MainModule.Types)
$dll = Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/RunicSigns.dll'
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($dll), $parameters)
try {
    $contracts = @(
        @('Sign','Awake',0), @('Sign','SetText',1), @('TextInput','RequestText',3),
        @('GameCamera','UpdateMouseCapture',0), @('Player','TakeInput',0),
        @('PlayerController','TakeInput',1), @('Player','SetControls',12),
        @('ZInput','GetKeyDown',2), @('ZInput','GetButtonDown',1),
        @('ZInput','GetButton',1), @('ZInput','GetButtonUp',1),
        @('PrivateArea','IsPermitted',1)
    )
    foreach ($contract in $contracts) {
        $type = $types | Where-Object Name -eq $contract[0]
        $method = @($type.Methods | Where-Object { $_.Name -eq $contract[1] -and $_.Parameters.Count -eq $contract[2] })
        if ($method.Count -ne 1) { throw "Missing or ambiguous installed method: $($contract -join ':')" }
    }
    $areas = ($game.MainModule.Types | Where-Object Name -eq 'PrivateArea').Fields | Where-Object Name -eq 'm_allAreas'
    if (!$areas -or $areas.FieldType.FullName -ne 'System.Collections.Generic.List`1<PrivateArea>') { throw 'Ward collection contract changed.' }
    $request = ($game.MainModule.Types | Where-Object Name -eq 'TextInput').Methods | Where-Object Name -eq 'RequestText'
    if ($request.Parameters[0].Name -ne 'sign' -or $request.Parameters[0].ParameterType.FullName -ne 'TextReceiver') { throw 'Editor interception signature changed.' }
    $resolved = 0
    foreach ($reference in $mod.MainModule.GetMemberReferences()) {
        if ($reference.DeclaringType.Scope.Name -notmatch '^(assembly_valheim|assembly_utils|assembly_guiutils|gui_framework|UnityEngine.*|Unity.TextMeshPro)$') { continue }
        if ($null -eq $reference.Resolve()) { throw "Unresolved game member: $($reference.FullName)" }
        $resolved++
    }
    $unexpected = @($mod.MainModule.AssemblyReferences | Where-Object { $_.Name -match '^(BetterSigns|RunicStorage|Jotunn)$' })
    if ($unexpected.Count -gt 0) { throw 'Unexpected mod dependency.' }
    if (@($mod.MainModule.Types | Where-Object FullName -eq 'Runic.Shared.ModalCameraScopePatch').Count -ne 0) {
        throw 'RunicSigns must not share the RunicStorage Harmony __state declaring type name.'
    }
    if (@($mod.MainModule.Types | Where-Object FullName -eq 'RunicSigns.Input.ModalCameraScopePatch').Count -ne 1) {
        throw 'Missing uniquely named RunicSigns camera input scope.'
    }
    $hash = (Get-FileHash -LiteralPath (Join-Path $managed 'assembly_valheim.dll') -Algorithm SHA256).Hash
    Write-Output "PASS: $($contracts.Count) patch/reflection contracts, ward collection, named editor argument and $resolved game/Unity member references."
    Write-Output "PASS: no BetterSigns, RunicStorage or Jotunn assembly dependency."
    Write-Output "Installed assembly_valheim SHA256: $hash"
} finally { $mod.Dispose(); $utils.Dispose(); $game.Dispose(); $resolver.Dispose() }
