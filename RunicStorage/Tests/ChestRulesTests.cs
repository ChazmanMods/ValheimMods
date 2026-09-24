using System;
using System.Linq;
using RunicStorage.Engine;
using RunicStorage.Runtime;

internal static class ChestRulesTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Chest rule regression failed."); }
    internal static void MemorySurvivesEmptyAndReload()
    {
        var rules = new ChestRules { Remember = true };
        rules.Learn(new[] { "Wood", "FineWood", "Wood" }); rules.Learn(Array.Empty<string>());
        Check(rules.Memory.Count == 2);
        Check(ChestRules.TryDecode(rules.Encode(), out var loaded));
        Check(loaded.Priority("Wood", 0, 0, 0, false) == 3);
        loaded.Memory.Remove("Wood");
        Check(loaded.Priority("Wood", 0, 0, 0, false) == int.MaxValue);
        loaded.Remember = false;
        Check(loaded.Priority("FineWood", 0, 0, 0, false) == int.MaxValue);
        Check(loaded.Priority("FineWood", 0, 0, 0, true) == 4);
        Check(rules.Memory.Count == 2);
    }
    internal static void ExplicitRulesOverrideContents()
    {
        var rules = new ChestRules { Remember = true }; rules.Learn(new[] { "Wood" }); rules.Items.Add("FineWood");
        Check(rules.Priority("FineWood", 0, 0, 0, false) == 0);
        Check(rules.Priority("Wood", 0, 0, 0, true) == int.MaxValue);
        Check(rules.Priority("FineWoodBow", 0, 0, 0, true) == int.MaxValue);
        rules.Items.Clear(); Check(rules.Priority("Wood", 0, 0, 0, false) == 3);
        rules.Categories.Add("Food"); Check(rules.Priority("Wood", 0, 0, 0, true) == int.MaxValue);
    }
    internal static void FoodBoundariesAndPriority()
    {
        var rules = new ChestRules(); rules.Categories.Add("Health Foods");
        Check(rules.Priority("meal", 30, 10, 0, false) == 1);
        Check(rules.Priority("meal", 10, 30, 0, false) == int.MaxValue);
        Check(rules.Priority("meal", 20, 20, 0, false) == int.MaxValue);
        rules.Categories[0] = "Stamina Foods";
        Check(rules.Priority("meal", 10, 30, 0, false) == 1);
        Check(rules.Priority("meal", 20, 20, 0, false) == int.MaxValue);
        rules.Categories[0] = "Balanced Foods"; Check(rules.Priority("meal", 20, 20, 0, false) == 1);
        Check(rules.Priority("stone", 0, 0, 0, false) == int.MaxValue);
        rules.Categories[0] = "Eitr Foods"; Check(rules.Priority("meal", 20, 20, 40, false) == 1);
        rules.Categories[0] = "Food"; Check(rules.Priority("meal", 20, 20, 0, false) == 2);
        rules.Items.Add("meal"); Check(rules.Priority("meal", 20, 20, 0, false) == 0);
    }
    internal static void LabelAndRulesRoundTrip()
    {
        for (int side = 0; side < 5; side++)
        {
            var rules = new ChestRules { Remember = true, ShowLabel = true, Label = "Fine wood", Side = side,
                Color = "#AA00BBCC", Size = 1.4f, Horizontal = -.5f, Vertical = .25f };
            rules.Items.Add("FineWood"); rules.Categories.Add("Health Foods"); rules.Learn(new[] { "Wood" });
            Check(ChestRules.TryDecode(rules.Encode(), out var copy));
            Check(copy.Encode() == rules.Encode() && copy.Side == side && copy.ShowLabel && copy.Memory.Single() == "Wood");
        }
    }
    internal static void RejectMalformedAndBoundMemory()
    {
        var rules = new ChestRules { Remember = true }; rules.Learn(Enumerable.Range(0, 1000).Select(i => "item" + i));
        Check(rules.Memory.Count == ChestRules.Limit);
        string encoded = rules.Encode();
        Check(!ChestRules.TryDecode(new string('A', 65537), out _));
        Check(!ChestRules.TryDecode("not base64", out _));
        Check(!ChestRules.TryDecode(encoded.Substring(0, encoded.Length - 4), out _));
        var bytes = Convert.FromBase64String(encoded); bytes[0] = 6;
        Check(!ChestRules.TryDecode(Convert.ToBase64String(bytes), out _));
        rules.Size = float.NaN;
        bool failed = false; try { rules.Encode(); } catch { failed = true; } Check(failed);
        rules.Size = 1; rules.Color = "red</color>";
        failed = false; try { rules.Encode(); } catch { failed = true; } Check(failed);
    }
    internal static void EmptyChestRuleUsesGuardedTransfer()
    {
        var rules = new ChestRules { Remember = true }; rules.Learn(new[] { "1" });
        var source = new Inventory("bag", null, 8, 4); var target = new Inventory("chest", null, 1, 1);
        var wood = new ItemDrop.ItemData { Prefab = 1, m_stack = 15 }; wood.m_customData["creator"] = "retained";
        source.GetAllItems().Add(wood);
        Check(rules.Priority("1", 0, 0, 0, false) == 3);
        var player = new Player { Inventory = source }; Player.m_localPlayer = player;
        Check(StorageMutationLease.TryBegin(player, out var lease));
        using (lease) Check(ValheimContainerService.MoveUpTo(source, target, wood, 15, player, lease, null, new Container { Inventory = target }) == 15);
        Check(source.GetAllItems().Count == 0 && target.GetAllItems().Single().m_customData["creator"] == "retained");
    }
}
