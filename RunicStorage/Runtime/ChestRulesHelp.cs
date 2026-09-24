namespace RunicStorage.Runtime;

internal static class ChestRulesHelp
{
    internal static string For(string label) => Help(StableLabel(label));

    private static string StableLabel(string label)
    {
        foreach (var candidate in Labels)
            if (global::Runic.Localization.RunicText.TranslateEnglish(candidate) == label) return candidate;
        return label;
    }
    private static readonly string[] Labels = new[] { "Quick Stack Rules", "Priority", "Remember", "Clear remembered items", "Clear rules", "Always accept", "Groups", "Never accept", "Accepted", "Remembered", "Only from biome", "Custom groups", "Search items or rules", "Previous", "Next", "Exterior", "Automatic label (or enter your own)", "Color name", "Text color", "Background", "Front", "Back", "Left", "Right", "Top", "Smaller", "Larger", "\u2190", "\u2192", "\u2193", "\u2191", "Reset position", "Group name", "Save group", "Delete", "New group", "Cancel", "Save" };
    private static string Help(string label) => label switch {
        "Quick Stack Rules" => global::Runic.Localization.RunicText.Get("text_e055b32114f6"),
        "Priority" => global::Runic.Localization.RunicText.Get("text_0fcc8279330e"),
        "Remember" => global::Runic.Localization.RunicText.Get("text_94e39e87d5f6"),
        "Clear remembered items" => global::Runic.Localization.RunicText.Get("text_50f372c6ffae"),
        "Clear rules" => global::Runic.Localization.RunicText.Get("text_17a7ad4d4f64"),
        "Always accept" => global::Runic.Localization.RunicText.Get("text_9e15300824f1"),
        "Groups" => global::Runic.Localization.RunicText.Get("text_0f307afdb59b"),
        "Never accept" => global::Runic.Localization.RunicText.Get("text_8d2dd48c9c44"),
        "Accepted" => global::Runic.Localization.RunicText.Get("text_ff366cfba8cd"),
        "Remembered" => global::Runic.Localization.RunicText.Get("text_d550bf02555a"),
        "Only from biome" => global::Runic.Localization.RunicText.Get("text_080704a90681"),
        "Custom groups" => global::Runic.Localization.RunicText.Get("text_e180d091de97"),
        "Search items or rules" => global::Runic.Localization.RunicText.Get("text_0e7a1d983170"),
        "Previous" => global::Runic.Localization.RunicText.Get("text_ccec4e53e333"),
        "Next" => global::Runic.Localization.RunicText.Get("text_3f7e27b36a7c"),
        "Exterior" => global::Runic.Localization.RunicText.Get("text_a61a376e1ed4"),
        "Automatic label (or enter your own)" => global::Runic.Localization.RunicText.Get("text_2f543c6ec797"),
        "Color name" => global::Runic.Localization.RunicText.Get("text_4382861d3b00"),
        "Text color" => global::Runic.Localization.RunicText.Get("text_b2e30326430f"),
        "Background" => global::Runic.Localization.RunicText.Get("text_6d10b8eeb3df"),
        "Front" or "Back" or "Left" or "Right" or "Top" => global::Runic.Localization.RunicText.Get("text_ed0e2368c9a5") + label.ToLowerInvariant() + global::Runic.Localization.RunicText.Get("text_f235077e694a"),
        "Smaller" => global::Runic.Localization.RunicText.Get("text_b68fdb013ad7"),
        "Larger" => global::Runic.Localization.RunicText.Get("text_76f6ff21293e"),
        "←" => global::Runic.Localization.RunicText.Get("text_6bf8211256e9"),
        "→" => global::Runic.Localization.RunicText.Get("text_c747a7bb1853"),
        "↓" => global::Runic.Localization.RunicText.Get("text_ffe302f8f00a"),
        "↑" => global::Runic.Localization.RunicText.Get("text_7e52cd495ea3"),
        "Reset position" => global::Runic.Localization.RunicText.Get("text_c590c8f79bd5"),
        "Group name" => global::Runic.Localization.RunicText.Get("text_2da3173bd76b"),
        "Save group" => global::Runic.Localization.RunicText.Get("text_1e2d0a8e7420"),
        "Delete" => global::Runic.Localization.RunicText.Get("text_bdff781a82c2"),
        "New group" => global::Runic.Localization.RunicText.Get("text_28a7bce167b2"),
        "Cancel" => global::Runic.Localization.RunicText.Get("text_b614a82f4ccd"),
        "Save" => global::Runic.Localization.RunicText.Get("text_eb7af15d2e76"),
        _ => label.StartsWith("Items (") ? global::Runic.Localization.RunicText.Get("text_e0bb20ab35ed") : global::Runic.Localization.RunicText.Get("text_1f7133c21072") + label + "."
    };
}
