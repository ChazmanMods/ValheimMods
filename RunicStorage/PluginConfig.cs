using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage
{
    internal static class PluginConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> ShowReadyMessage { get; private set; }
        internal static ConfigEntry<float> RangeMeters { get; private set; }
        internal static ConfigEntry<int> MaximumCandidates { get; private set; }
        internal static ConfigEntry<bool> ProtectHotbar { get; private set; }
        internal static ConfigEntry<bool> ShowContentsOnHover { get; private set; }
        internal static ConfigEntry<int> HoverMaximumItemKinds { get; private set; }
        internal static ConfigEntry<int> HoverItemsPerLine { get; private set; }
        internal static ConfigEntry<int> HoverMaximumCharacters { get; private set; }
        internal static ConfigEntry<int> HoverMaximumStacksExamined { get; private set; }
        internal static ConfigEntry<int> HoverMaximumSnapshotCharacters { get; private set; }
        internal static ConfigEntry<float> HoverRefreshIntervalSeconds { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> QuickStackKey { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> RestockKey { get; private set; }
        internal static ConfigEntry<string> RestockTargets { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> SortOpenedContainerKey { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> StoreAllOpenedContainerKey { get; private set; }
        internal static ConfigEntry<string> LockedContainerSlots { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConsolidateKey { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> SearchKey { get; private set; }
        internal static ConfigEntry<string> SearchItem { get; private set; }
        internal static ConfigEntry<int> SearchMenuFontSize { get; private set; }
        internal static ConfigEntry<StorageSearchMenuFontColor> SearchMenuFontColor { get; private set; }
        internal static ConfigEntry<bool> ControllerShortcuts { get; private set; }
        internal static ConfigEntry<string> ControllerModifier { get; private set; }
        internal static ConfigEntry<string> ControllerQuickStack { get; private set; }
        internal static ConfigEntry<string> ControllerRestock { get; private set; }
        internal static ConfigEntry<string> ControllerSort { get; private set; }
        internal static ConfigEntry<string> ControllerConsolidate { get; private set; }
        internal static ConfigEntry<string> ControllerSearch { get; private set; }
        internal static ConfigEntry<bool> DebugTransfers { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Enable Runic Storage gameplay actions.");
            ShowReadyMessage = config.Bind("General", "ShowReadyMessage", true,
                "Show a one-time in-world control reminder after the local player loads.");
            RangeMeters = config.Bind("Discovery", "RangeMeters", 20f,
                new ConfigDescription("Nearby storage radius. Hard limited to 50 meters.", new AcceptableValueRange<float>(1f, 50f)));
            MaximumCandidates = config.Bind("Discovery", "MaximumCandidates", 64,
                new ConfigDescription("Maximum cached containers examined per action.", new AcceptableValueRange<int>(1, 256)));
            ProtectHotbar = config.Bind("Safety", "ProtectHotbar", true,
                "Never quick-stack, store-all, or consolidate items in the top-row hotbar.");
            ShowContentsOnHover = config.Bind("Hover", "ShowContents", true,
                "List a closed authorized container's synchronized contents in its hover text. Private, busy, unsynchronized, or Runic-reserved containers retain vanilla text only.");
            HoverMaximumItemKinds = config.Bind("Hover", "MaximumItemKinds", 8,
                new ConfigDescription("Maximum distinct item kinds shown in one hover summary.", new AcceptableValueRange<int>(1, 24)));
            HoverItemsPerLine = config.Bind("Hover", "ItemsPerLine", 3,
                new ConfigDescription("Maximum item kinds placed on each summary line.", new AcceptableValueRange<int>(1, 4)));
            HoverMaximumCharacters = config.Bind("Hover", "MaximumCharacters", 320,
                new ConfigDescription("Hard character ceiling for the complete generated contents suffix, including formatting tags.", new AcceptableValueRange<int>(64, 1024)));
            HoverMaximumStacksExamined = config.Bind("Hover", "MaximumStacksExamined", 256,
                new ConfigDescription("Maximum inventory stacks examined when rebuilding one changed summary. Additional stacks are reported as unscanned.", new AcceptableValueRange<int>(16, 1024)));
            HoverMaximumSnapshotCharacters = config.Bind("Hover", "MaximumSnapshotCharacters", 262144,
                new ConfigDescription("Maximum persisted Base64 inventory characters accepted for an exact hover snapshot. Oversized third-party containers retain vanilla text.", new AcceptableValueRange<int>(16384, 1048576)));
            HoverRefreshIntervalSeconds = config.Bind("Hover", "FailedRefreshRetrySeconds", 0.5f,
                new ConfigDescription("Retry delay after a container cannot prove an exact synchronized inventory snapshot. Stable summaries remain cached by exact persisted item-payload evidence.", new AcceptableValueRange<float>(0.1f, 5f)));
            QuickStackKey = config.Bind("Keys", "QuickStack", new KeyboardShortcut(KeyCode.Q, KeyCode.LeftAlt),
                "Deposit eligible carried stacks into authorized nearby containers already holding that item.");
            RestockKey = config.Bind("Keys", "Restock", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt),
                "Restock configured carried targets from authorized nearby storage.");
            RestockTargets = config.Bind("Restock", "Targets", "Wood=50,Stone=50",
                "Comma-separated prefab/name targets, for example Wood=50,Stone=50.");
            SortOpenedContainerKey = config.Bind("Keys", "SortOpenedContainer", new KeyboardShortcut(KeyCode.S, KeyCode.LeftAlt),
                "Sort the currently opened authorized container by category, name, quality, then weight.");
            StoreAllOpenedContainerKey = config.Bind("Keys", "StoreAllOpenedContainer", new KeyboardShortcut(KeyCode.A, KeyCode.LeftAlt),
                "Store every eligible carried item in the currently opened authorized locally owned container.");
            LockedContainerSlots = config.Bind("Sort", "LockedSlots", string.Empty,
                "Semicolon-separated zero-based slots to leave fixed, for example 0,0;1,0.");
            ConsolidateKey = config.Bind("Keys", "ConsolidateCarriedStacks", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt),
                "Safely consolidate compatible carried stacks while respecting protected slots and equipment.");
            SearchKey = config.Bind("Keys", "Search", new KeyboardShortcut(KeyCode.F, KeyCode.LeftAlt),
                "Open a selectable list of item kinds in authorized nearby containers and highlight every matching chest.");
            SearchItem = config.Bind("Search", "SearchItem", "Wood",
                "Legacy setting retained for configuration compatibility; Alt+F now opens the complete nearby-item list.");
            SearchMenuFontSize = config.Bind("Search", "MenuFontSize", 14,
                new ConfigDescription(
                    "Font size for every title, label, text field, and button in the Alt+F nearby-item window.",
                    new AcceptableValueRange<int>(10, 32)));
            bool hadPersistedSearchMenuFontColor = TryReadAndRemoveLegacySearchMenuFontColor(
                config,
                out StorageSearchMenuFontColor persistedSearchMenuFontColor);
            SearchMenuFontColor = config.Bind(
                "Search",
                "MenuFontColor",
                StorageSearchMenuAppearance.DefaultFontColor,
                "Named font color for the complete Alt+F nearby-item window. Configuration Manager presents the available colors as a dropdown.");
            if (hadPersistedSearchMenuFontColor)
                SearchMenuFontColor.Value = persistedSearchMenuFontColor;
            ControllerShortcuts = config.Bind("Controller", "Enabled", true,
                "Enable controller chords resolved through Valheim's current ZInput action map.");
            ControllerModifier = config.Bind("Controller", "ModifierAction", "JoyAltKeys",
                "Valheim ZInput action held as the controller modifier. Change only to an existing action name.");
            ControllerQuickStack = config.Bind("Controller", "QuickStackAction", "JoyDPadDown",
                "Valheim ZInput action pressed with ModifierAction to Quick Stack.");
            ControllerRestock = config.Bind("Controller", "RestockAction", "JoyDPadUp",
                "Valheim ZInput action pressed with ModifierAction to Restock.");
            ControllerSort = config.Bind("Controller", "SortOpenedContainerAction", "JoyRStick",
                "Valheim ZInput action pressed with ModifierAction to sort the opened container.");
            ControllerConsolidate = config.Bind("Controller", "ConsolidateAction", "JoyDPadLeft",
                "Valheim ZInput action pressed with ModifierAction to consolidate carried stacks.");
            ControllerSearch = config.Bind("Controller", "SearchAction", "JoyDPadRight",
                "Valheim ZInput action pressed with ModifierAction to search nearby storage.");
            DebugTransfers = config.Bind("Diagnostics", "DebugTransfers", false,
                "Log detected controls, routing decisions, discovery counts, action summaries, transfer legs, and stable no-op/denial codes.");
        }

        private static bool TryReadAndRemoveLegacySearchMenuFontColor(
            ConfigFile config,
            out StorageSearchMenuFontColor migrated)
        {
            var definition = new ConfigDefinition("Search", "MenuFontColor");
            bool hadPersistedValue = HasOrphanedValue(config, definition);
            ConfigEntry<string> legacyEntry = config.Bind(
                definition,
                StorageSearchMenuAppearance.DefaultFontColor.ToString(),
                new ConfigDescription("Legacy Alt+F menu color migration entry."));
            migrated =
                StorageSearchMenuAppearance.NormalizeFontColorConfigValue(legacyEntry.Value);

            // ConfigFile.Remove is the public BepInEx path for rebinding a definition with a new
            // type. The replacement enum entry is bound immediately by the caller, before
            // Configuration Manager can enumerate the file.
            if (!config.Remove(definition))
                throw new InvalidOperationException(
                    "Runic Storage could not replace its legacy MenuFontColor setting.");
            return hadPersistedValue;
        }

        private static bool HasOrphanedValue(ConfigFile config, ConfigDefinition definition)
        {
            // BepInEx 5 intentionally keeps its pre-Bind value table internal. Read it without
            // mutating it so a fresh configuration can be distinguished from a persisted value;
            // this keeps the permanent enum's DefaultValue (and Config Manager Reset) canonical.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", flags);
            object table = property?.GetValue(config, null);
            if (table == null)
            {
                FieldInfo field = typeof(ConfigFile).GetField(
                    "<OrphanedEntries>k__BackingField",
                    flags);
                table = field?.GetValue(config);
            }
            return table is IDictionary<ConfigDefinition, string> orphaned &&
                   orphaned.ContainsKey(definition);
        }
    }
}
