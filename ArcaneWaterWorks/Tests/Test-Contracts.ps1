$ErrorActionPreference='Stop'
Add-Type -Path 'E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/core/Mono.Cecil.dll'
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed')
$resolver.AddSearchDirectory('E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/core')
$resolver.AddSearchDirectory('E:/Valheim Mods/ArcaneWaterWorks/Tests/runtime/BepInEx/plugins/ValheimModding-Jotunn')
$p=[Mono.Cecil.ReaderParameters]::new();$p.AssemblyResolver=$resolver
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/ArcaneNaturalWaters.dll'),$p)
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll',$p)
try {
$allowed='^(netstandard|mscorlib|System.*|UnityEngine.*|Unity.TextMeshPro|assembly_.*|BepInEx|0Harmony|Jotunn)$'
foreach($reference in $mod.MainModule.AssemblyReferences){if($reference.Name -notmatch $allowed){throw "Unexpected external assembly dependency: $($reference.Name)"}}
$plugin=$mod.MainModule.Types | Where-Object FullName -eq 'ArcaneWaterWorks.Plugin'
$dependencies=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' })
if($dependencies.Count -ne 1 -or $dependencies[0].ConstructorArguments[0].Value -ne 'com.jotunn.jotunn'){throw 'Expected Jotunn as the only BepInDependency'}
$resources=@($mod.MainModule.Resources.Name)
if($resources -notcontains 'ArcaneWaterWorks.RiverFlow'){throw 'Missing flowing river shader bundle'}
foreach($asset in @('Models.rock_001.meshbin','Models.rock_001.jpg','Models.rock_001.png','Cascade','Audio.waterfall_001.wav','Audio.waterfall_002.wav')){
 if($resources -notcontains "ArcaneNaturalWaters.Pools.$asset"){throw "Missing standalone pool resource: $asset"}
}
1..7 | ForEach-Object {if($resources -notcontains ('ArcaneNaturalWaters.Pools.Audio.night_ambience_{0:000}.wav' -f $_)){throw "Missing ambience $_"}}
"PASS: standalone assembly dependencies, Jotunn plugin requirement and pool resources."
if($resources -match 'Plumbing' -or $mod.MainModule.Types.FullName -match '\.Plumbing\.') {throw 'Natural mod still contains plumbing'}
"PASS: natural mod contains no plumbing types or resources."
foreach($key in @('river-source','river-ambience','hot-spring-round','hot-spring-oval','hot-spring-kidney','hot-spring-wild','hoe-start','hoe-trench','hoe-deepen','hoe-shallow','hoe-smooth','hoe-unsmooth','hoe-undo')){
 if($resources -notcontains "ArcaneWaterWorks.BuildIcons.$key.png"){throw "Missing build icon: $key"}
}
"PASS: all 13 distinct river, pool and hoe action icons are embedded."
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


