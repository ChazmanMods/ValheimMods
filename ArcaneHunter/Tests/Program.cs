using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcaneHunter;
using ArcaneHunter.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

int passed = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAIL: " + label);
    passed++; System.Console.WriteLine("PASS: " + label);
}
void Reject(Action action, string label)
{
    bool threw = false; try { action(); } catch { threw = true; }
    Check(threw, label);
}
var rule = new SpawnRule { Chance = 3, Frequency = 2, Population = 1.5f };
Check(HuntingRules.Chance(5, rule) == 15, "Serpent 3x chance example");
Check(HuntingRules.Interval(1000, rule) == 500, "2x frequency halves interval");
Check(HuntingRules.Population(3, rule) == 5, "Population rounds up");
Check(HuntingRules.Population(0, rule) == 0, "Unlimited population sentinel survives");
Check(HuntingRules.Chance(80, rule) == 100, "Chance saturates at 100");
Check(HuntingRules.Population(100, new SpawnRule { Population = 10 }) == 200, "Finite cap bounded");
Check(HuntingRules.Interval(0.1f, rule) == 1, "No zero/tiny attempt interval");
Check(HuntingRules.Clamp(float.NaN, 0, 20) == 1, "NaN does not poison spawn values");
var errors = new List<string>();
var parsed = HuntingRules.ParseOverrides("Serpent:3:2:1; ModDeer:0:1:2;bad:NaN:1:1;bad2:1:0:1;bad3:1:1:99;incomplete;Serpent:4:1:1", errors.Add);
Check(parsed.Count == 2 && errors.Count == 4, "Invalid custom rows isolated and reported");
Check(parsed["serpent"].Chance == 4, "Custom names case insensitive and last duplicate wins");
Check(parsed["ModDeer"].Chance == 0, "Custom creature disable supported");
Check(HuntingRules.Bearing(0, 10) == "N" && HuntingRules.Bearing(10, 0) == "E" && HuntingRules.Bearing(0, -10) == "S" && HuntingRules.Bearing(-10, 0) == "W", "Cardinal compass bearings");
Check(HuntingRules.Bearing(-10, 10) == "NW", "Diagonal compass bearing");

var original = new SpawnSystem.SpawnData { m_prefab = new SpawnSystem.Prefab { name = "Serpent" } };
var other = new SpawnSystem.SpawnData { m_prefab = new SpawnSystem.Prefab { name = "Deer" } };
var list = new List<SpawnSystem.SpawnData> { original, other };
List<SpawnSystem.SpawnData> Apply(List<SpawnSystem.SpawnData> input, bool raid = false)
{
    object[] arguments = { input, raid };
    typeof(SpawnPatches).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
    return (List<SpawnSystem.SpawnData>)arguments[0];
}
Plugin.Settings.Rules["Serpent"] = rule;
var changed = Apply(list);
Check(changed[0].m_spawnChance == 15 && changed[0].m_spawnInterval == 500, "Production prefix applies selected rule");
Check(original.m_spawnChance == 5 && original.m_spawnInterval == 1000, "Shared baseline stays untouched");
Check(changed.Count == list.Count && ReferenceEquals(changed[1], other), "Other entries and cooldown indexes preserved");
Check(Apply(list)[0].m_spawnChance == 15, "Repeated calls do not compound multiplier");
Check(ReferenceEquals(Apply(list, true), list), "Raid list untouched");
Plugin.Settings.Enabled.Value = false;
Check(ReferenceEquals(Apply(list), list), "Master disable restores baseline immediately");
Plugin.Settings.Enabled.Value = true;
Plugin.Settings.Rules["Serpent"] = new SpawnRule();
Check(ReferenceEquals(Apply(list), list), "Default config is exact no-op");
Plugin.Settings.Rules["Serpent"] = new SpawnRule { Chance = 0, AnyTime = true, AnyWeather = true };
changed = Apply(list);
Check(!changed[0].m_enabled && changed.Count == 2, "Chance zero disables without deleting entry");
Check(changed[0].m_spawnAtDay && changed[0].m_spawnAtNight && changed[0].m_requiredEnvironments.Count == 0, "Opt-in time and weather override");
Check(!original.m_spawnAtDay && original.m_requiredEnvironments.Count == 1, "Clone weather list is isolated");

var journal = new JournalData();
Check(journal.Observe("Deer", "Meadows") && journal.XP == 5, "Discovery grants renown once");
Check(!journal.Observe("Deer", "Meadows") && journal.XP == 5 && journal.Get("Deer").Biomes.Count == 1, "Repeated observations cannot farm renown");
journal.PrepareBoard("world1:1", "Deer");
Check(journal.RecordKill("kill1", "Deer", 1, true, true, 45, "Meadows", true) == 150, "Clean farshot awards both oaths once");
int xp = journal.XP;
Check(journal.RecordKill("kill1", "Deer", 1, true, true, 45, "Meadows", true) == 0 && journal.XP == xp, "Duplicate receipt awards nothing");
journal.RecordKill("kill2", "Deer", 1, false, false, 99, "Meadows", true);
Check(journal.Get("Deer").LongestShot == 45, "Melee distance cannot set ranged record");
Check(journal.RecordKill("kill3", "Deer", 1, false, false, 1, "Meadows", true) == 50, "Third kill completes provisioning oath");
Check(journal.Board.All(c => c.Paid), "All three oaths independently complete");
Check(journal.UnlockSeals().Contains("Keeper of Three Oaths"), "Three-oath seal earned");
Check(journal.UnlockSeals().Count == 0, "Seals unlock once");
journal.PrepareBoard("world1:1", "Serpent");
Check(journal.Board.All(c => c.Prefab == "Deer"), "Same-day biome change cannot reroll board");
journal.PrepareBoard("world1:2", "Serpent");
Check(journal.Board.All(c => c.Prefab == "Serpent" && c.Progress == 0), "Next day creates fresh board");
Check(journal.Seals.Contains("Keeper of Three Oaths"), "Daily seal persists after board rollover");
journal.Focus = "Serpent";
string json = journal.Serialize();
var restored = JournalData.Deserialize(json);
Check(restored.XP == journal.XP && restored.Focus == "Serpent" && restored.Get("Deer").Kills == 3, "Save roundtrip preserves progression and focus");
Check(restored.Seals.Contains("Keeper of Three Oaths") && restored.Receipts.Contains("kill1"), "Save roundtrip preserves seals and deduplication");
Check(restored.RecordKill("kill1", "Deer", 1, false, false, 0, "Meadows", true) == 0, "Duplicate receipt rejected after reload");
Reject(() => JournalData.Deserialize("{broken"), "Corrupt save is rejected for caller to preserve");
Reject(() => JournalData.Deserialize(json.Replace("\"Version\":1", "\"Version\":2")), "Future save schema rejected without overwrite");
var noCommissions = new JournalData();
noCommissions.PrepareBoard("world:1", "Boar");
noCommissions.RecordKill("a", "Boar", 3, true, true, 80, "Meadows", false);
Check(noCommissions.Board.All(c => c.Progress == 0), "Disabled commissions cannot progress");
Check(noCommissions.UnlockSeals().Contains("Crown of the Wild"), "Two-star seal threshold");
Check(noCommissions.Seals.Contains("Beyond the Bowstring"), "80m seal threshold");
for (int i = 0; i < 300; i++) noCommissions.RecordKill("bounded" + i, "Boar", 1, false, false, 0, "Meadows", false);
Check(noCommissions.Receipts.Count == 256, "Receipt history bounded");
Check(HuntingRules.Rank(99) == "Wayfarer" && HuntingRules.Rank(100) == "Pathfinder" && HuntingRules.Rank(5000) == "Mythic Huntsman", "Title thresholds");

// Exercise the actual pickup prefix with a mutable vanilla-style drop registry.
var pickupPrefix = typeof(AutoPickupPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
bool Scan(Player p, bool enabled = true) => (bool)pickupPrefix.Invoke(null, new object[] { p, enabled });
Player ResetPickup()
{
    ItemDrop.All.Clear();
    UnityEngine.Time.time += 1;
    Plugin.Settings.AutoPickup.Value = true;
    Plugin.Settings.Enabled.Value = true;
    return Player.m_localPlayer = new Player();
}
var picker = ResetPickup();
new ItemDrop(499); new ItemDrop(500); new ItemDrop(501);
Check(!Scan(picker) && picker.Inventory.Count == 2 && ItemDrop.All.Count == 1, "500m boundary and removal-safe traversal");
picker = ResetPickup(); new ItemDrop(400);
Check(!Scan(picker, false) && picker.Inventory.Count == 0, "Vanilla auto pickup toggle respected");
Plugin.Settings.AutoPickup.Value = false;
Check(Scan(picker), "Disabled feature restores vanilla");
Plugin.Settings.AutoPickup.Value = true;
Check(Scan(new Player()), "Remote players untouched");
picker = ResetPickup(); new ItemDrop(100) { Piece = true }; new ItemDrop(100) { Tar = true }; new ItemDrop(100) { m_autoPickup = false };
Check(!Scan(picker) && picker.Inventory.Count == 0, "Placed, tar and excluded drops remain");
picker = ResetPickup(); picker.Inventory.Slots = 0; new ItemDrop(100);
Check(!Scan(picker) && picker.Inventory.Count == 0, "Full inventory leaves loot intact");
picker = ResetPickup(); picker.MaxWeight = 1; new ItemDrop(100); new ItemDrop(100);
Check(!Scan(picker) && picker.Inventory.Count == 1, "Weight rechecked for every drop");
picker = ResetPickup(); var remoteDrop = new ItemDrop(100) { Owned = false };
Scan(picker);
Check(remoteDrop.Requested && picker.Inventory.Count == 0, "Requests ownership before transfer");
remoteDrop.Owned = true; UnityEngine.Time.time += .3f; Scan(picker);
Check(picker.Inventory.Count == 1, "Ownership completion collected on next scan");
picker = ResetPickup(); picker.Dead = true; new ItemDrop(100); Scan(picker);
Check(picker.Inventory.Count == 0, "No collection after death");
picker = ResetPickup(); picker.Unique.Add("loot"); new ItemDrop(100); Scan(picker);
Check(picker.Inventory.Count == 0, "Unique quest item excluded");
picker = ResetPickup(); picker.MaxWeight = 2000;
for (int n = 0; n < 600; n++) new ItemDrop(400);
Scan(picker);
Check(picker.Inventory.Count == 256, "Scan work bounded to 256 drops");
Scan(picker);
Check(picker.Inventory.Count == 256, "Scan throttled within same interval");
UnityEngine.Time.time += .3f; Scan(picker); UnityEngine.Time.time += .3f; Scan(picker);
Check(picker.Inventory.Count == 600, "Dense field drains despite swap-removal");

// Verify compiled production API operands and Harmony targets against installed game binaries.
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string game = "E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed";
string bepinex = "C:/Users/Charles Sammons/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Solo/BepInEx";
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(game); resolver.AddSearchDirectory(bepinex + "/core");
resolver.AddSearchDirectory(bepinex + "/plugins/ValheimModding-Jotunn");
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(root, "ArcaneHunter/bin/Release/netstandard2.1/ArcaneHunter.dll"), new ReaderParameters { AssemblyResolver = resolver });
int apiReferences = 0, patches = 0;
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
foreach (var type in Types(assembly.MainModule.Types))
{
    foreach (var method in type.Methods)
    {
        foreach (var attr in method.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        {
            if (attr.ConstructorArguments.Count == 2 && attr.ConstructorArguments[0].Value is TypeReference target && attr.ConstructorArguments[1].Value is string methodName)
            {
                Check(type.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"), "Harmony container discoverable: " + type.Name);
                var targetMethod = target.Resolve().Methods.Single(m => m.Name == methodName);
                Check(targetMethod != null, "Game patch target: " + target.FullName + "." + methodName);
                foreach (var parameter in method.Parameters.Where(p => p.Name.StartsWith("___")))
                    Check(target.Resolve().Fields.Any(f => f.Name == parameter.Name.Substring(3)), "Injected game field: " + parameter.Name);
                patches++;
            }
        }
        if (!method.HasBody) continue;
        foreach (var instruction in method.Body.Instructions)
        {
            var member = instruction.Operand as MemberReference;
            string scope = member?.DeclaringType?.Scope?.Name ?? "";
            if (scope != "assembly_valheim" && scope != "Jotunn" && !scope.StartsWith("UnityEngine")) continue;
            if (member is MethodReference mr) { if (mr.Resolve() == null) throw new Exception("Missing game method " + mr); apiReferences++; }
            else if (member is FieldReference fr) { if (fr.Resolve() == null) throw new Exception("Missing game field " + fr); apiReferences++; }
        }
    }
}
Check(patches == 5, "All five method-level patches discovered (spawn patch uses class target)");
var pickupType = assembly.MainModule.Types.Single(t => t.Name == "AutoPickupPatch");
var pickupAttribute = pickupType.CustomAttributes.Single(a => a.AttributeType.Name == "HarmonyPatch");
var playerType = ((TypeReference)pickupAttribute.ConstructorArguments[0].Value).Resolve();
Check(playerType.Methods.Any(m => m.Name == "AutoPickup") && playerType.Fields.Any(f => f.Name == "m_enableAutoPickup" && f.IsStatic), "Pickup Harmony target and injected static toggle exist");
var itemType = playerType.Module.Types.Single(t => t.Name == "ItemDrop");
Check(itemType.Fields.Any(f => f.Name == "s_instances" && f.IsStatic), "Loaded drop registry reflection contract");
var spawnType = assembly.MainModule.GetType("ArcaneHunter.SpawnPatches");
var spawnAttr = spawnType.CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
var spawnGame = ((TypeReference)spawnAttr.ConstructorArguments[0].Value).Resolve();
var spawnMethod = spawnGame.Methods.Single(m => m.Name == "UpdateSpawnList");
Check(spawnMethod.Parameters.Any(p => p.Name == "spawners") && spawnMethod.Parameters.Any(p => p.Name == "eventSpawners"), "Spawn prefix parameter contract");
Check(apiReferences > 100, $"Resolved {apiReferences} production game/Jotunn/Unity API references");
System.Console.WriteLine($"\n{passed} checks passed. Live Unity/multiplayer playtest is still required.");
