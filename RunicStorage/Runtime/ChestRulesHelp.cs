namespace RunicStorage.Runtime;

internal static class ChestRulesHelp
{
    internal static string For(string label) => label switch {
        "Quick Stack Rules" => "Choose what Runic Quick Stack puts in this chest and customize its exterior label. The separate vanilla Stack button keeps its normal behavior.",
        "Priority" => "Toggle Normal / Preferred. Among chests with equally specific matching rules, Preferred is tried before Normal, then the nearest chest first. An exact-item chest still comes before a broad-group chest. Exclusions always prevent delivery to this chest.",
        "Remember" => "Remember item types stored here so Runic Quick Stack can refill this chest after it is empty. Turning this off preserves the remembered list but stops using and learning it. The number counts item types, not quantities.",
        "Clear remembered items" => "Forget this chest's remembered item types. Does not remove actual items or explicit rules. If Remember contents stays ON, later chest inventory changes can teach these items again.",
        "Clear rules" => "Clear Always accept, Never accept, group selections and the biome filter. Keeps actual items, remembered contents, priority and label settings. Save to apply.",
        "Always accept" => "Select specific items this chest should accept, even when empty. These bypass the biome filter. Never accept still blocks them. Click a checked item to remove its exception.",
        "Groups" => "Select built-in or custom item groups. Multiple selections accept any matching group. Only from biome can narrow these matches. A check marks a selected group.",
        "Never accept" => "Select items Runic Quick Stack must never put here. This overrides Always accept, groups, remembered contents and both priority settings. It does not remove items already here or block manual placement.",
        "Accepted" => "Review your explicit Always accept items and selected groups. Click an entry to remove that rule. This is not a complete list of all items that could match.",
        "Remembered" => "Review remembered item types. Click one to forget it. Memory is a fallback when no Always accept or group rules are set; Never accept and the biome filter still apply.",
        "Only from biome" => "Restrict group and fallback matches to one biome. Any biome removes the restriction. Always accept exceptions bypass this filter; Never accept exclusions do not.",
        "Custom groups" => "Create reusable groups from your own item selections. Save group stores a local template immediately; select that template under Groups and Save the chest to use it.",
        "Search items or rules" => "Filter the current list by display name or internal item/rule ID. Search changes only what is shown; it does not change chest rules.",
        "Previous" => "Show the previous page of the current filtered list.",
        "Next" => "Show the next page of the current filtered list.",
        "Exterior" => "Show or hide the label attached to this chest. Sorting rules continue to work when the label is hidden. Save to apply.",
        "Automatic label (or enter your own)" => "Leave empty to build a label from selected rules or remembered items. Enter your own caption to change only the visible text, not the sorting rules.",
        "Color name" => "Type a color name, then press Enter or leave this field. Supported Unity names are used directly. Familiar other colors use the nearest palette RGB; unknown words use the nearest spelling. The chosen name is shown before Save.",
        "Text color" => "Choose a supported Unity text color. The mod writes its opaque hex color tag automatically. The label background is selected separately.",
        "Background" => "Choose Transparent (chest surface shows through), solid White, or solid Black behind the label text. This does not change text color or chest rules.",
        "Front" or "Back" or "Left" or "Right" or "Top" => "Put the label on the " + label.ToLowerInvariant() + " face, relative to the chest's own rotation. Save to apply.",
        "Smaller" => "Reduce label text size by 0.1, down to 0.3. Text can shrink further automatically to fit its label area.",
        "Larger" => "Increase label text size by 0.1, up to 2.0. Long text still shrinks to fit its label area.",
        "←" => "Move the label left 0.05 metres on the selected chest face (limit 1 metre).",
        "→" => "Move the label right 0.05 metres on the selected chest face (limit 1 metre).",
        "↓" => "Move the label down 0.05 metres on the selected chest face (limit 1 metre).",
        "↑" => "Move the label up 0.05 metres on the selected chest face (limit 1 metre).",
        "Reset position" => "Center the label and restore size 1.0. Keeps your chosen face, text, color and background.",
        "Group name" => "Enter a unique name for this reusable custom group. It needs at least one selected item before Save group.",
        "Save group" => "Save this reusable template immediately to your local group library. Cancel does not undo library saves. Other chests keep their own saved copies.",
        "Delete" => "Delete the selected template from your local group library immediately. Existing chest selections keep their saved copies; Cancel does not undo this deletion.",
        "New group" => "Start a new custom group. Unsaved edits to the current template are discarded. Choose its items and name, then Save group.",
        "Cancel" => "Close without saving this chest's edits. Custom group library saves and deletions already performed are kept.",
        "Save" => "Save rules and label settings to this chest. Access and ownership are checked again; conflicting changes from another edit are not overwritten.",
        _ => label.StartsWith("Items (") ? "Choose the exact items in this custom group. Click items to add or remove them, then Save group." : "Select " + label + "."
    };
}
