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
        internal static ConfigEntry<string> QuickStackPrefabIds { get; private set; }
        internal static ConfigEntry<string> PullPrefabIds { get; private set; }
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
            QuickStackPrefabIds = config.Bind("Modded Containers", "QuickStackPrefabIds", "piece_drawer",
                global::Runic.Localization.RunicText.Get("text_f9dbe38b22fd"));
            PullPrefabIds = config.Bind("Modded Containers", "PullPrefabIds", "piece_drawer",
                global::Runic.Localization.RunicText.Get("text_980709710050"));
            Enabled = config.Bind("General", "Enabled", true, global::Runic.Localization.RunicText.Get("text_340e175cb779"));
            ShowReadyMessage = config.Bind("General", "ShowReadyMessage", true,
                global::Runic.Localization.RunicText.Get("text_37acb38649e3"));
            RangeMeters = config.Bind("Discovery", "RangeMeters", 20f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_c80e4d62bbc8"), new AcceptableValueRange<float>(1f, 50f)));
            MaximumCandidates = config.Bind("Discovery", "MaximumCandidates", 64,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_81d1e63d0211"), new AcceptableValueRange<int>(1, 256)));
            ProtectHotbar = config.Bind("Safety", "ProtectHotbar", true,
                global::Runic.Localization.RunicText.Get("text_5c5d641c67ad"));
            ShowContentsOnHover = config.Bind("Hover", "ShowContents", true,
                global::Runic.Localization.RunicText.Get("text_9006a780df66"));
            HoverMaximumItemKinds = config.Bind("Hover", "MaximumItemKinds", 8,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_cb0291602bfc"), new AcceptableValueRange<int>(1, 24)));
            HoverItemsPerLine = config.Bind("Hover", "ItemsPerLine", 3,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_bd19c2bd5b7d"), new AcceptableValueRange<int>(1, 4)));
            HoverMaximumCharacters = config.Bind("Hover", "MaximumCharacters", 320,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_28a4a81bb673"), new AcceptableValueRange<int>(64, 1024)));
            HoverMaximumStacksExamined = config.Bind("Hover", "MaximumStacksExamined", 256,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_eeb8535a9c0d"), new AcceptableValueRange<int>(16, 1024)));
            HoverMaximumSnapshotCharacters = config.Bind("Hover", "MaximumSnapshotCharacters", 262144,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_585364fc88e1"), new AcceptableValueRange<int>(16384, 1048576)));
            HoverRefreshIntervalSeconds = config.Bind("Hover", "FailedRefreshRetrySeconds", 0.5f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_703485340375"), new AcceptableValueRange<float>(0.1f, 5f)));
            QuickStackKey = config.Bind("Keys", "QuickStack", new KeyboardShortcut(KeyCode.Q, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_db7d4548125e"));
            RestockKey = config.Bind("Keys", "Restock", new KeyboardShortcut(KeyCode.R, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_b62be2d6c34b"));
            RestockTargets = config.Bind("Restock", "Targets", "Wood=50,Stone=50",
                global::Runic.Localization.RunicText.Get("text_ff38805f2729"));
            SortOpenedContainerKey = config.Bind("Keys", "SortOpenedContainer", new KeyboardShortcut(KeyCode.S, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_c9597fab958a"));
            StoreAllOpenedContainerKey = config.Bind("Keys", "StoreAllOpenedContainer", new KeyboardShortcut(KeyCode.A, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_9432793f9256"));
            LockedContainerSlots = config.Bind("Sort", "LockedSlots", string.Empty,
                global::Runic.Localization.RunicText.Get("text_bdb9a2049fde"));
            ConsolidateKey = config.Bind("Keys", "ConsolidateCarriedStacks", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_9568168aaf98"));
            SearchKey = config.Bind("Keys", "Search", new KeyboardShortcut(KeyCode.F, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_0de747a97dc1"));
            SearchItem = config.Bind("Search", "SearchItem", "Wood",
                global::Runic.Localization.RunicText.Get("text_0b403f3328e2"));
            SearchMenuFontSize = config.Bind("Search", "MenuFontSize", 14,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_1ffa52cdbe74"),
                    new AcceptableValueRange<int>(10, 32)));
            bool hadPersistedSearchMenuFontColor = TryReadAndRemoveLegacySearchMenuFontColor(
                config,
                out StorageSearchMenuFontColor persistedSearchMenuFontColor);
            SearchMenuFontColor = config.Bind(
                "Search",
                "MenuFontColor",
                StorageSearchMenuAppearance.DefaultFontColor,
                global::Runic.Localization.RunicText.Get("text_022702280f9d"));
            if (hadPersistedSearchMenuFontColor)
                SearchMenuFontColor.Value = persistedSearchMenuFontColor;
            ControllerShortcuts = config.Bind("Controller", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_95eba864befa"));
            ControllerModifier = config.Bind("Controller", "ModifierAction", "JoyAltKeys",
                global::Runic.Localization.RunicText.Get("text_26f0831c752c"));
            ControllerQuickStack = config.Bind("Controller", "QuickStackAction", "JoyDPadDown",
                global::Runic.Localization.RunicText.Get("text_90bca5befeac"));
            ControllerRestock = config.Bind("Controller", "RestockAction", "JoyDPadUp",
                global::Runic.Localization.RunicText.Get("text_deebb0d367e0"));
            ControllerSort = config.Bind("Controller", "SortOpenedContainerAction", "JoyRStick",
                global::Runic.Localization.RunicText.Get("text_e633ef105cf6"));
            ControllerConsolidate = config.Bind("Controller", "ConsolidateAction", "JoyDPadLeft",
                global::Runic.Localization.RunicText.Get("text_b27dc2d1856e"));
            ControllerSearch = config.Bind("Controller", "SearchAction", "JoyDPadRight",
                global::Runic.Localization.RunicText.Get("text_9138fc4bdde0"));
            DebugTransfers = config.Bind("Diagnostics", "DebugTransfers", false,
                global::Runic.Localization.RunicText.Get("text_5b7619e46a9a"));
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
