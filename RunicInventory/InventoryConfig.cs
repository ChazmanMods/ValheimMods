using BepInEx.Configuration;
using RunicInventory.Core;
using UnityEngine;

namespace RunicInventory
{
    internal static class InventoryConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> ShowInventoryStatus { get; private set; }
        internal static ConfigEntry<bool> ShowRoleLabels { get; private set; }
        internal static ConfigEntry<bool> ShowPickupPreview { get; private set; }
        internal static ConfigEntry<bool> CompactQuiverLayout { get; private set; }
        internal static ConfigEntry<string> FilteredPickupItems { get; private set; }
        internal static ConfigEntry<string> SortRows { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> Quick1 { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> Quick2 { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> Quick3 { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> Sort { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ToggleLock { get; private set; }
        internal static ConfigEntry<bool> ControllerEnabled { get; private set; }
        internal static ConfigEntry<string> ControllerModifier { get; private set; }
        internal static ConfigEntry<string> ControllerQuick1 { get; private set; }
        internal static ConfigEntry<string> ControllerQuick2 { get; private set; }
        internal static ConfigEntry<string> ControllerQuick3 { get; private set; }
        internal static ConfigEntry<string> ControllerSort { get; private set; }
        internal static ConfigEntry<string> ControllerToggleLock { get; private set; }
        internal static ConfigEntry<bool> VerboseDiagnostics { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                new ConfigDescription("Automatically add the equipment and quick-use row. Disable is unavailable until there is room in normal inventory for every extra-row item.",
                    null, new Integration.ConfigurationManagerAttributes()));
            ShowInventoryStatus = config.Bind("UI", "ShowInventoryStatus", false,
                "Show the bounded topology/authority panel while the player inventory is open.");
            ShowRoleLabels = config.Bind("UI", "ShowRoleLabels", true,
                "Label and subtly outline the eight native bottom-row equipment and quick slots while inventory is open.");
            ShowPickupPreview = config.Bind("UI", "ShowPickupPreview", true,
                "Append a bounded local capacity/weight preview to nearby world-item hover text.");
            CompactQuiverLayout = config.Bind("UI", "CompactQuiverLayout", true,
                "Collapse unused display space between Better Archery's quiver and Runic equipment row. Visual only; no inventory slots or items are moved.");
            FilteredPickupItems = config.Bind("Pickup Filter", "Items", string.Empty,
                "Exact comma/semicolon/newline-separated prefab IDs or shared-name tokens to refuse before pickup mutation; maximum 128 safe entries. Quest items always bypass the filter.");
            SortRows = config.Bind("Sort", "Rows", "1,2",
                "Zero-based general rows eligible for regional sort. Row 0 and the bottom special row are always rejected. Empty means every proven general row.");
            Quick1 = config.Bind("Keyboard", "UseQuickSlot1", new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt),
                "Manually use quick slot 1 for the owning local player, including a dedicated-server client.");
            Quick2 = config.Bind("Keyboard", "UseQuickSlot2", new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt),
                "Manually use quick slot 2 for the owning local player, including a dedicated-server client.");
            Quick3 = config.Bind("Keyboard", "UseQuickSlot3", new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt),
                "Manually use quick slot 3 for the owning local player, including a dedicated-server client.");
            Sort = config.Bind("Keyboard", "SortSelectedRows", new KeyboardShortcut(KeyCode.I, KeyCode.LeftAlt),
                "Sort only configured safe general rows while the inventory is open.");
            ToggleLock = config.Bind("Keyboard", "ToggleFocusedSlotLock", new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt),
                "Optional keyboard fallback for slot locking. The primary gesture is Left Alt + right-click on a player slot.");

            ControllerEnabled = config.Bind("Controller", "Enabled", true,
                "Enable raw, effective-path-validated controller chords.");
            ControllerModifier = config.Bind("Controller", "ModifierAction", ControllerBindingPolicy.ModifierAction,
                "Existing Valheim Joy* action held as the modifier.");
            ControllerQuick1 = config.Bind("Controller", "UseQuickSlot1Action", ControllerBindingPolicy.Quick1Action,
                "Existing gamepad action pressed with ModifierAction to use quick slot 1. The exact untouched legacy 1.0.0 controller set is read as JoyMap without rewriting the file.");
            ControllerQuick2 = config.Bind("Controller", "UseQuickSlot2Action", ControllerBindingPolicy.Quick2Action,
                "Existing gamepad action pressed with ModifierAction to use quick slot 2.");
            ControllerQuick3 = config.Bind("Controller", "UseQuickSlot3Action", ControllerBindingPolicy.Quick3Action,
                "Existing gamepad action pressed with ModifierAction to use quick slot 3.");
            ControllerSort = config.Bind("Controller", "SortSelectedRowsAction", ControllerBindingPolicy.SortAction,
                "Existing gamepad action pressed with ModifierAction to sort while inventory is open.");
            ControllerToggleLock = config.Bind("Controller", "ToggleFocusedSlotLockAction", ControllerBindingPolicy.ToggleLockAction,
                "Existing gamepad action pressed with ModifierAction to toggle the focused slot lock.");
            VerboseDiagnostics = config.Bind("Diagnostics", "Verbose", false,
                "Log successful bounded protection decisions and control routes. Indeterminate protection decisions always log once per reason. Inventory contents and player metadata are never logged.");
        }
    }
}
