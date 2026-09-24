using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicStorage.Engine;

internal static class ChestLabelLayout
{
    // uGUI font sizes are pixels. At 100 canvas units per metre a 24-unit line is 24 cm high.
    internal const float UnitsPerMetre = 100f;
    internal static float Width(float faceWidth) => Math.Max(35f, Math.Min(250f, faceWidth * 85f));
    internal static float FontSize(float size) => 24f * size;
    internal static string AutomaticText(ChestRules rules, Func<string, string> itemName)
    {
        var names = new List<string>();
        foreach (string id in rules.Categories)
            names.Add(rules.CustomGroups.Find(g => g.Id == id)?.Name ?? ChestRuleGroups.Display(id));
        foreach (string id in rules.Items) names.Add(itemName(id));
        // A biome-only filter is still a useful chest designation.
        if (rules.OnlyBiome.Length > 0) {
            string biome = ChestRuleGroups.Display(rules.OnlyBiome);
            if (!names.Contains(biome)) names.Insert(0, biome);
        }
        if (names.Count == 0 && rules.Remember)
            foreach (string id in rules.Memory) names.Add(itemName(id));
        if (names.Count == 0) return global::Runic.Localization.RunicText.Get("text_a69c4dece144");
        return string.Join(" / ", names.Take(3)) + (names.Count > 3 ? " / …" : "");
    }
}
