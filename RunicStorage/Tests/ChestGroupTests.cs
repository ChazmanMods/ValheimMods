using System;
using System.IO;
using System.Linq;
using System.Text;
using RunicStorage.Engine;

internal static class ChestGroupTests
{
    private static void Check(bool value, string why = "Group regression") { if (!value) throw new Exception(why); }
    internal static void ExceptionsBiomeAndOrdering()
    {
        var wood = new ChestItemFacts { Id = "FineWood" };
        var narrow = new ChestRules(); narrow.Categories.Add("resource:wood");
        var exact = new ChestRules(); exact.Items.Add("FineWood");
        var broad = new ChestRules { Preferred = true }; broad.Categories.Add("other:ingredients"); wood.Ingredient = true;
        Check(exact.RoutingScore(wood, false) < narrow.RoutingScore(wood, false));
        Check(narrow.RoutingScore(wood, false) < broad.RoutingScore(wood, false));
        var preferred = new ChestRules { Preferred = true }; preferred.Categories.Add("resource:wood");
        Check(preferred.RoutingScore(wood, false) < narrow.RoutingScore(wood, false));
        narrow.OnlyBiome = "biome:swamp"; Check(narrow.Rank(wood, true) == int.MaxValue);
        narrow.Items.Add("FineWood"); Check(narrow.Rank(wood, false) == 0);
        narrow.Excluded.Add("FineWood"); Check(narrow.Rank(wood, true) == int.MaxValue);
        var mixed = new ChestRules(); mixed.Categories.AddRange(new[] { "resource:bars", "resource:ores" });
        Check(mixed.Rank(new ChestItemFacts { Id = "Iron" }, false) == 1);
        Check(mixed.Rank(new ChestItemFacts { Id = "CopperOre" }, false) == 1);
        mixed.OnlyBiome = "biome:swamp";
        Check(mixed.Rank(new ChestItemFacts { Id = "Iron" }, false) == 1);
        Check(mixed.Rank(new ChestItemFacts { Id = "CopperOre" }, false) == int.MaxValue);
        var remembered = new ChestRules { Remember = true, Preferred = true }; remembered.Memory.Add("FineWood");
        Check(broad.RoutingScore(wood, false) < remembered.RoutingScore(wood, false));
        Check(remembered.RoutingScore(wood, false) < new ChestRules().RoutingScore(wood, true));
    }
    internal static void ModdedTypeGroups()
    {
        bool Matches(string group, ChestItemFacts item) => ChestRuleGroups.Find(group).Matches(item);
        Check(Matches("equipment:weapons", new ChestItemFacts { Id = "modded-sword", Type = "OneHandedWeapon" }));
        Check(!Matches("equipment:weapons", new ChestItemFacts { Type = "OneHandedWeapon", Tool = true }));
        Check(Matches("equipment:tools", new ChestItemFacts { Type = "Tool", Tool = true }));
        Check(Matches("equipment:armor", new ChestItemFacts { Type = "Chest" }));
        Check(Matches("equipment:capes", new ChestItemFacts { Type = "Shoulder" }));
        Check(Matches("equipment:shields", new ChestItemFacts { Type = "Shield" }));
        Check(Matches("ammo:arrows", new ChestItemFacts { Type = "Ammo", Ammo = "$ammo_arrows" }));
        Check(!Matches("ammo:arrows", new ChestItemFacts { Type = "Ammo", Ammo = "$ammo_bolts" }));
        Check(Matches("ammo:bolts", new ChestItemFacts { Type = "Ammo", Ammo = "$ammo_bolts" }));
        Check(Matches("ammo:all", new ChestItemFacts { Type = "AmmoNonEquipable" }));
        Check(Matches("consumable:potions", new ChestItemFacts { Potion = true }));
        Check(Matches("other:valuables", new ChestItemFacts { Valuable = true }));
        Check(Matches("other:trophies", new ChestItemFacts { Type = "Trophy" }));
        Check(Matches("other:ingredients", new ChestItemFacts { Ingredient = true }));
    }
    internal static void CustomGroupsAreReusableSnapshots()
    {
        var template = new ChestCustomGroup { Name = "Building Supplies" }; template.Items.AddRange(new[] { "FineWood", "Stone" });
        var chest = new ChestRules { OnlyBiome = "biome:meadows", Preferred = true };
        chest.CustomGroups.Add(template.Copy()); chest.Categories.Add(template.Id); chest.Excluded.Add("Stone");
        template.Name = "Renamed template"; template.Items.Clear(); template.Items.Add("Gold");
        Check(chest.Rank(new ChestItemFacts { Id = "FineWood" }, false) == 2);
        Check(chest.Rank(new ChestItemFacts { Id = "Stone" }, true) == int.MaxValue);
        Check(ChestRules.TryDecode(chest.Encode(), out var loaded));
        Check(loaded.Preferred && loaded.Excluded.Single() == "Stone" && loaded.OnlyBiome == "biome:meadows");
        Check(loaded.CustomGroups.Single().Name == "Building Supplies");
        loaded.Label = "Purple bananas";
        Check(loaded.Rank(new ChestItemFacts { Id = "FineWood" }, false) == 2);
        var library = new ChestRules(); library.CustomGroups.Add(template.Copy());
        Check(ChestRules.TryDecode(library.Encode(), out var libraryCopy));
        Check(libraryCopy.CustomGroups.Single().Id == template.Id);
        Check(libraryCopy.CustomGroups.Single().Items.Single() == "Gold");
    }
    internal static void LegacyFoodRulesMigrateLosslessly()
    {
        using var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            w.Write((byte)1); w.Write(true); w.Write(true); w.Write((byte)4);
            w.Write(1.3f); w.Write(-.25f); w.Write(.5f); w.Write("#FFAABBCC"); w.Write("Legacy caption");
            foreach (string item in new[] { "FineWood", "Health Foods", "Stone" }) { w.Write((byte)1); w.Write(item); }
        }
        Check(ChestRules.TryDecode(Convert.ToBase64String(bytes.ToArray()), out var rules));
        Check(rules.Categories.Single() == "food:health" && rules.Items.Single() == "FineWood" && rules.Memory.Single() == "Stone");
        Check(rules.ShowLabel && rules.Remember && rules.Side == 4 && rules.Label == "Legacy caption" && rules.Color == "#FFAABBCC");
        Check(rules.Horizontal == -.25f && rules.Vertical == .5f && rules.Size == 1.3f);
        Check(!rules.Preferred && rules.Excluded.Count == 0 && rules.OnlyBiome == "");
        Check(ChestRules.TryDecode(rules.Encode(), out var reloaded) && reloaded.Encode() == rules.Encode());
    }
    internal static void CuratedIdsExistInInstalledGame()
    {
        string install = Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ?? @"E:\SteamLibrary\steamapps\common\Valheim";
        var manifest = File.ReadAllLines(Path.Combine(install, "valheim_Data", "StreamingAssets", "SoftRef", "manifest_extended"));
        var ids = manifest.Where(l => l.Contains("Assets/GameElements/Items/") && l.EndsWith(".prefab", StringComparison.Ordinal))
            .Select(l => Path.GetFileNameWithoutExtension(l)).ToHashSet(StringComparer.Ordinal);
        foreach (var group in ChestRuleGroups.Curated)
            foreach (string id in group.Value) Check(ids.Contains(id), group.Key + " has an unknown prefab: " + id);
        Check(ChestRuleGroups.All.Select(g => g.Id).Distinct().Count() == ChestRuleGroups.All.Count);
        Check(ChestRuleGroups.Biomes.Count() == 8);
        Check(ChestRuleGroups.Find("biome:deep-north").Matches(new ChestItemFacts { Id = "GoldOre" }));
        Check(!ChestRuleGroups.Find("biome:mountains").Matches(new ChestItemFacts { Id = "GoldOre" }));
        Check(ChestRuleGroups.Find("biome:meadows").Matches(new ChestItemFacts { Id = "Wood" }) &&
            ChestRuleGroups.Find("biome:black-forest").Matches(new ChestItemFacts { Id = "Wood" }));
    }
}
