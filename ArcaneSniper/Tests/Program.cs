using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ArcaneSniper;
using Mono.Cecil;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
Check(Math.Abs(ScopeMath.FieldOfView(65, 1) - 65) < .001, "1x preserves normal FOV");
float fourX = ScopeMath.FieldOfView(65, 4);
Check(Math.Abs(Math.Tan(65 * Math.PI / 360) / Math.Tan(fourX * Math.PI / 360) - 4) < .001, "4x uses true optical magnification");
Check(ScopeMath.FieldOfView(65, 20) < fourX && ScopeMath.FieldOfView(65, 40) > 0, "High zoom narrows FOV without becoming zero");
Check(ScopeMath.FieldOfView(65, 0) == ScopeMath.FieldOfView(65, 1), "Sub-1x input clamped");
Check(ScopeMath.FieldOfView(65, 100) == ScopeMath.FieldOfView(65, 40), "Excessive zoom clamped");
Check(WorldGeometry.InSquare(-5, 12, -4, 11, 1) && !WorldGeometry.InSquare(-6, 12, -4, 11, 1), "Camera activity includes exactly one neighboring ring across negative coordinates");
Check(WorldGeometry.ValidFocus(100, 5000, 100, 100, 100, 1609.344f), "Dungeon altitude does not consume horizontal camera tether");
Check(!WorldGeometry.ValidFocus(2000, 5000, 0, 0, 0, 1609.344f), "Dungeon travel cannot bypass horizontal range");
Check(!WorldGeometry.ValidFocus(float.NaN, 5, 0, 0, 0, 1609.344f) && !WorldGeometry.ValidFocus(0, float.PositiveInfinity, 0, 0, 0, 1609.344f), "Malformed network coordinates rejected");
var gate = new AimGate();
Check(gate.Ready(1, 0, 0, .2f, true), "Click on acquired target fires immediately");
Check(!gate.Ready(2, .1f, 0, .2f, true), "Switching targets cannot bypass cooldown");
Check(gate.Ready(2, .3f, 0, .2f, true), "Next target fires after cooldown");
gate = new AimGate();
Check(!gate.Ready(1, 0, 1, .2f, true), "Dwell starts pending");
Check(!gate.Ready(2, .9f, 1, .2f, true), "Switch resets dwell");
Check(!gate.Ready(2, 1.2f, 1, .2f, true), "New target requires full dwell");
Check(gate.Ready(2, 2, 1, .2f, true), "Continuous dwell fires");
gate.Clear();
Check(!gate.Ready(2, 3, 1, .2f, true), "UI interruption resets acquisition");
Check(!gate.Ready(0, 5, 0, .2f, true), "Empty aim never fires");

var resolver = new DefaultAssemblyResolver();
gate = new AimGate();
Check(!gate.Ready(1, 0, 0, .2f, false), "Aiming without a click never fires");
Check(!gate.Ready(1, 5, 0, .2f, false), "Long hover never fires");
Check(gate.Ready(1, 5, 0, .2f, true), "Fresh click fires");
Check(!gate.Ready(1, 6, 0, .2f, false), "Holding button without another down event never repeats");
gate = new AimGate();
Check(!gate.Ready(1, 0, 1, .2f, true), "Early click is discarded");
Check(!gate.Ready(1, 2, 1, .2f, false), "Discarded click cannot fire later");
Check(gate.Ready(1, 2, 1, .2f, true), "Fresh click after steady aim fires");

Player ResetPickup()
{
    ItemDrop.All.Clear(); Player.SetAutoPickup(true); UnityEngine.Time.time += 1;
    return Player.m_localPlayer = new Player();
}
var collector = new PickupCollector();
var origin = new UnityEngine.Vector3(0,1,0);
var player = ResetPickup();
new ItemDrop(1609); new ItemDrop(1610);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 1 && ItemDrop.All.Count == 1, "One-mile pickup range includes 1609m and excludes 1610m");
player = ResetPickup(); Player.SetAutoPickup(false); new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 0, "Native pickup toggle stops collection");
player = ResetPickup(); new ItemDrop(100) { Piece = true }; new ItemDrop(100) { Tar = true }; new ItemDrop(100) { m_autoPickup = false };
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 0, "Placed, tar and disabled drops remain");
player = ResetPickup(); player.Inventory.Slots = 0; new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 0, "No pickup when inventory full");
player = ResetPickup(); player.MaxWeight = 1; new ItemDrop(100); new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 1, "Weight rechecked per drop");
player = ResetPickup(); var remote = new ItemDrop(100) { Owned = false };
collector.Tick(player, origin, 1609.344f);
Check(remote.Requested && player.Inventory.Count == 0, "Network ownership requested before pickup");
remote.Owned = true; UnityEngine.Time.time += .3f; collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 1, "Ownership completion collects once");
player = ResetPickup(); player.Dead = true; new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 0, "Dead players cannot collect");
player = ResetPickup(); player.Unique.Add("loot"); new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 0, "Unique quest duplicates excluded");
player = ResetPickup(); player.MaxWeight = 2000;
for (int i = 0; i < 600; i++) new ItemDrop(100);
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 256, "Bounded scan batch");
collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 256, "Repeated scan throttled");
UnityEngine.Time.time += .3f; collector.Tick(player, origin, 1609.344f);
UnityEngine.Time.time += .3f; collector.Tick(player, origin, 1609.344f);
Check(player.Inventory.Count == 600, "Swap-removal and dense drops drain without skips");
player = ResetPickup(); new ItemDrop(1000);
collector.Tick(player, new UnityEngine.Vector3(1000,1,0), 10);
Check(player.Inventory.Count == 1, "Pickup supports detached camera origin");
resolver.AddSearchDirectory(@"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed");
resolver.AddSearchDirectory(@"C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Solo\BepInEx\core");
var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(AppContext.BaseDirectory, "../../../../bin/Release/netstandard2.1/ArcaneSniper.dll"), new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts) => ts.SelectMany(t => new[] { t }.Concat(All(t.NestedTypes)));
int patches = 0, refs = 0;
foreach (var t in All(assembly.MainModule.Types))
{
    foreach (var a in t.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch" && a.ConstructorArguments.Count == 2))
    {
        var target = ((TypeReference)a.ConstructorArguments[0].Value).Resolve();
        var method = target.Methods.Single(m => m.Name == (string)a.ConstructorArguments[1].Value);
        Check(method != null, "Harmony target exists");
        foreach (var patch in t.Methods.Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
            foreach (var p in patch.Parameters.Where(p => !p.Name.StartsWith("__")))
                Check(method.Parameters.Any(mp => mp.Name == p.Name && (mp.ParameterType.FullName == p.ParameterType.FullName || p.ParameterType.FullName == "System.Object")), "Harmony parameter contract: " + t.Name + "." + p.Name);
        patches++;
    }
    foreach (var m in t.Methods.Where(m => m.HasBody)) foreach (var i in m.Body.Instructions)
    {
        if (i.Operand is not MemberReference member) continue;
        string scope = member.DeclaringType?.Scope?.Name ?? "";
        if (scope != "assembly_valheim" && !scope.StartsWith("UnityEngine")) continue;
        if (member is MethodReference mr) { Check(mr.Resolve() != null, "Game method resolves"); refs++; }
        if (member is FieldReference fr) { Check(fr.Resolve() != null, "Game field resolves"); refs++; }
    }
}
Check(patches == 16, "Camera, interest, authority, spawning and lifecycle patch targets validated: " + patches);
var gameAssembly = resolver.Resolve(new AssemblyNameReference("assembly_valheim", new Version()));
Check(gameAssembly.MainModule.Types.Single(t => t.Name == "ItemDrop").Fields.Any(f => f.Name == "s_instances" && f.IsStatic), "Loaded drop registry exists");
Check(gameAssembly.MainModule.Types.Single(t => t.Name == "Player").Fields.Any(f => f.Name == "m_enableAutoPickup" && f.IsStatic), "Vanilla pickup toggle exists");
var plugin = assembly.MainModule.Types.Single(t => t.FullName == "ArcaneSniper.Plugin");
Check((float)plugin.Fields.Single(f => f.Name == "OneMile").Constant == 1609.344f, "Mile conversion uses 1609.344 meters");
var aim = plugin.Methods.Single(m => m.Name == "Aim");
Check(aim.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GetMouseButtonDown"), "Production firing uses mouse down edge");
var world = assembly.MainModule.Types.Single(t => t.FullName == "ArcaneSniper.CameraWorld");
var worldTypes = All(new[] { world }).ToArray();
var calls = worldTypes.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToArray();
Check(!calls.Any(m => m.Name == "SetReferencePosition" || m.Name == "SetPublicReferencePosition" || m.Name == "TeleportTo" || m.Name == "Save"), "Streaming never moves player/reference position or forces persistence");
var travel = plugin.Methods.Single(m => m.Name == "TravelDungeon");
Check(!travel.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "TeleportTo") && travel.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_targetPoint"), "Dungeon camera follows linked target without teleporting player");
Check(travel.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "OverlapSphereNonAlloc"), "Dungeon interaction uses bounded local overlap");
var gameSpawn = gameAssembly.MainModule.Types.Single(t => t.Name == "SpawnSystem");
var updateSpawn = gameSpawn.Methods.Single(m => m.Name == "UpdateSpawning").Body.Instructions;
Check(updateSpawn.Any(i => i.Operand is FieldReference f && f.Name == "m_localPlayer") && updateSpawn.Any(i => i.Operand is MethodReference m && m.Name == "op_Equality"), "Installed game has expected dedicated spawn gate");
Check(gameSpawn.Methods.Count(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "FindBaseSpawnPoint")) == 1, "Only known caller consumes custom spawn origins");
foreach (var type in worldTypes)
foreach (var method in type.Methods.Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
foreach (var parameter in method.Parameters.Where(p => p.Name.StartsWith("___")))
{
    var patch = type.CustomAttributes.Single(a => a.AttributeType.Name == "HarmonyPatch");
    var target = ((TypeReference)patch.ConstructorArguments[0].Value).Resolve();
    Check(target.Fields.Any(f => f.Name == parameter.Name.Substring(3)), "Injected game field exists");
}
System.Console.WriteLine($"{passed} checks passed; {refs} game API references and {patches} Harmony targets. Live playtest pending.");
