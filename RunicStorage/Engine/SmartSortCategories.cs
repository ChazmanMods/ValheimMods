using System;
using System.Linq;

namespace RunicStorage.Engine;

internal static class SmartSortCategories
{
    // Specific materials win over broad recipe-ingredient and food memberships.
    private static readonly string[] Order = {
        "resource:wood", "resource:stone", "resource:ores", "resource:bars",
        "resource:seeds", "resource:crops", "resource:animal", "other:trophies",
        "consumable:potions", "food:all", "equipment:tools", "equipment:weapons",
        "equipment:shields", "equipment:armor", "ammo:all", "other:valuables", "other:ingredients"
    };
    internal static string For(ChestItemFacts facts) => Order.FirstOrDefault(id => ChestRuleGroups.Find(id).Matches(facts)) ?? "other:misc";
    internal static string Display(string category) => category == "other:misc" ? "Other Materials" : ChestRuleGroups.Display(category);
    internal static bool HasManualRules(ChestRules rules) => rules.Items.Count != 0 || rules.Categories.Count != 0 ||
        rules.Excluded.Count != 0 || rules.OnlyBiome.Length != 0 || rules.Remember || rules.Preferred || rules.Label.Length != 0;
}
