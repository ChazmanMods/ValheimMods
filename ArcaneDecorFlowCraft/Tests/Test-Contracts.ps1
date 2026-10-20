$ErrorActionPreference='Stop'
Add-Type -Path 'E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/core/Mono.Cecil.dll'
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed')
$resolver.AddSearchDirectory('E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/core')
$resolver.AddSearchDirectory('E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/plugins/ValheimModding-Jotunn')
$p=[Mono.Cecil.ReaderParameters]::new();$p.AssemblyResolver=$resolver
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/ArcaneDecorFlowCraft.dll'),$p)
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll',$p)
try {
$allowed='^(netstandard|mscorlib|System.*|UnityEngine.*|Unity.TextMeshPro|assembly_.*|BepInEx|0Harmony|Jotunn)$'
foreach($reference in $mod.MainModule.AssemblyReferences){if($reference.Name -notmatch $allowed){throw "Unexpected external assembly dependency: $($reference.Name)"}}
$plugin=$mod.MainModule.Types | Where-Object FullName -eq 'ArcaneDecorFlowCraft.Plugin'
$dependencies=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' })
if($dependencies.Count -ne 1 -or $dependencies[0].ConstructorArguments[0].Value -ne 'com.jotunn.jotunn'){throw 'Expected Jotunn as the only BepInDependency'}
$resources=@($mod.MainModule.Resources.Name)
foreach($key in @('pipe','elbow','intake','powered','dragon')){
 foreach($extension in @('meshbin','jpg','png')){if($resources -notcontains "ArcaneDecorFlowCraft.Plumbing.$key.$extension"){throw "Missing plumbing resource: $key.$extension"}}
}
foreach($key in @('pipe4','powered2','pipe_narrow','pipe4_narrow','elbow_narrow','intake_narrow','powered_narrow','powered2_narrow','dragon_narrow','powered_glow','powered4','powered4_narrow','elbow_long','elbow_long_narrow')){if($resources -notcontains "ArcaneDecorFlowCraft.Plumbing.$key.png"){throw "Missing variant icon: $key"}}
if($resources -match '\.Pools\.|HotSprings|BuildIcons'){throw 'Natural assets leaked into FlowCraft'}
"PASS: independent FlowCraft dependencies and all plumbing assets."
$count=0
foreach($r in $mod.MainModule.GetMemberReferences()) {
 if($r.DeclaringType.Scope.Name -match '^(assembly_valheim|assembly_utils|assembly_guiutils|UnityEngine.*|Jotunn)$') {
   if(!$r.Resolve()){throw "Unresolved member: $($r.FullName)"};$count++
 }
}
foreach($spec in @(@('Player','UpdatePlacementGhost'),@('Floating','GetWaterLevel'),@('Floating','IsUnderWater'),@('WaterVolume','UpdateFloaters'),@('Player','TryPlacePiece'),@('Player','PlacePiece'),@('Player','AddKnownPiece'),@('Player','UpdateAvailablePiecesList'),@('WaterVolume','GetWaterSurface'),@('Heightmap','ApplyModifiers'),@('GameCamera','UpdateMouseCapture'),@('Player','TakeInput'),@('PlayerController','TakeInput'),@('Player','SetControls'))) {
 $type=$game.MainModule.Types | Where-Object Name -eq $spec[0]
 if(!($type.Methods | Where-Object Name -eq $spec[1])){throw "Missing patch target $spec"}
}
$heightmap=$game.MainModule.Types | Where-Object Name -eq 'Heightmap'
foreach($field in @('m_heights','m_paintMask')){if(!($heightmap.Fields | Where-Object Name -eq $field)){throw "Missing Heightmap field $field"}}
$water=$game.MainModule.Types | Where-Object Name -eq 'WaterVolume'
if(!($water.Fields | Where-Object Name -eq 'm_inWater')){throw 'Missing WaterVolume floater field'}
$player=$game.MainModule.Types | Where-Object Name -eq 'Player'
foreach($field in @('m_placementGhost','m_placementStatus')){if(!($player.Fields | Where-Object Name -eq $field)){throw "Missing Player field $field"}}
"PASS: $count current game/Unity/Jotunn member references and explicit patch targets/fields resolved."
} finally {$mod.Dispose();$game.Dispose();$resolver.Dispose()}


