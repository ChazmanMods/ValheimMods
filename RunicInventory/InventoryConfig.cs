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
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_8d4aa0761bf8"),
                    null, new Integration.ConfigurationManagerAttributes()));
            ShowInventoryStatus = config.Bind("UI", "ShowInventoryStatus", false,
                global::Runic.Localization.RunicText.Get("text_3c4bb869b3e0"));
            ShowRoleLabels = config.Bind("UI", "ShowRoleLabels", true,
                global::Runic.Localization.RunicText.Get("text_10dc0db42044"));
            ShowPickupPreview = config.Bind("UI", "ShowPickupPreview", true,
                global::Runic.Localization.RunicText.Get("text_9aeb1688941e"));
            CompactQuiverLayout = config.Bind("UI", "CompactQuiverLayout", true,
                global::Runic.Localization.RunicText.Get("text_90beaaeae893"));
            FilteredPickupItems = config.Bind("Pickup Filter", "Items", string.Empty,
                global::Runic.Localization.RunicText.Get("text_c2e4efc127e7"));
            SortRows = config.Bind("Sort", "Rows", "1,2",
                global::Runic.Localization.RunicText.Get("text_cfd403f31628"));
            Quick1 = config.Bind("Keyboard", "UseQuickSlot1", new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_c19bc8ec2e08"));
            Quick2 = config.Bind("Keyboard", "UseQuickSlot2", new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_4a213600b47c"));
            Quick3 = config.Bind("Keyboard", "UseQuickSlot3", new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_dfce180bc2ee"));
            Sort = config.Bind("Keyboard", "SortSelectedRows", new KeyboardShortcut(KeyCode.I, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_6c6419e7f4d1"));
            ToggleLock = config.Bind("Keyboard", "ToggleFocusedSlotLock", new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_31d0dc2a5d57"));

            ControllerEnabled = config.Bind("Controller", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_d760c9e83479"));
            ControllerModifier = config.Bind("Controller", "ModifierAction", ControllerBindingPolicy.ModifierAction,
                global::Runic.Localization.RunicText.Get("text_0040e54c0301"));
            ControllerQuick1 = config.Bind("Controller", "UseQuickSlot1Action", ControllerBindingPolicy.Quick1Action,
                global::Runic.Localization.RunicText.Get("text_21358e79d50b"));
            ControllerQuick2 = config.Bind("Controller", "UseQuickSlot2Action", ControllerBindingPolicy.Quick2Action,
                global::Runic.Localization.RunicText.Get("text_78efbbf396ce"));
            ControllerQuick3 = config.Bind("Controller", "UseQuickSlot3Action", ControllerBindingPolicy.Quick3Action,
                global::Runic.Localization.RunicText.Get("text_d88ee291d981"));
            ControllerSort = config.Bind("Controller", "SortSelectedRowsAction", ControllerBindingPolicy.SortAction,
                global::Runic.Localization.RunicText.Get("text_454da9489f48"));
            ControllerToggleLock = config.Bind("Controller", "ToggleFocusedSlotLockAction", ControllerBindingPolicy.ToggleLockAction,
                global::Runic.Localization.RunicText.Get("text_cece443edf13"));
            VerboseDiagnostics = config.Bind("Diagnostics", "Verbose", false,
                global::Runic.Localization.RunicText.Get("text_00bba4d37a51"));
        }
    }
}
