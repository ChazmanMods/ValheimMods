$ErrorActionPreference='Stop'
Add-Type -Path 'C:/Users/Charles Sammons/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Drak Test/BepInEx/core/Mono.Cecil.dll'
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed')
$resolver.AddSearchDirectory('C:/Users/Charles Sammons/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Drak Test/BepInEx/core')
$resolver.AddSearchDirectory('C:/Users/Charles Sammons/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Drak Test/BepInEx/plugins/ValheimModding-Jotunn')
$p=[Mono.Cecil.ReaderParameters]::new();$p.AssemblyResolver=$resolver
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/Release/netstandard2.1/ArcaneDecorWaterGardens.dll'),$p)
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll',$p)
try {
$count=0
foreach($r in $mod.MainModule.GetMemberReferences()) {
 if($r.DeclaringType.Scope.Name -match '^(assembly_valheim|assembly_utils|UnityEngine.*|Jotunn)$') {
   if(!$r.Resolve()){throw "Unresolved member: $($r.FullName)"};$count++
 }
}
foreach($spec in @(@('Player','UpdatePlacementGhost'),@('Heightmap','ApplyModifiers'),@('GameCamera','UpdateMouseCapture'),@('Player','TakeInput'),@('PlayerController','TakeInput'),@('Player','SetControls'))) {
 $type=$game.MainModule.Types | Where-Object Name -eq $spec[0]
 if(!($type.Methods | Where-Object Name -eq $spec[1])){throw "Missing patch target $spec"}
}
$heightmap=$game.MainModule.Types | Where-Object Name -eq 'Heightmap'
foreach($field in @('m_heights','m_paintMask')){if(!($heightmap.Fields | Where-Object Name -eq $field)){throw "Missing Heightmap field $field"}}
$player=$game.MainModule.Types | Where-Object Name -eq 'Player'
foreach($field in @('m_placementGhost','m_placementStatus')){if(!($player.Fields | Where-Object Name -eq $field)){throw "Missing Player field $field"}}
"PASS: $count current game/Unity/Jotunn member references and explicit patch targets/fields resolved."
} finally {$mod.Dispose();$game.Dispose();$resolver.Dispose()}
