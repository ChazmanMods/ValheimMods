using System;
using BepInEx.Configuration;
using RunicInteraction.Core;

namespace RunicInteraction
{
    internal static class InteractionConfig
    {
        internal static event Action<ConfigDefinition> Changed;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> HoldToRepeat { get; private set; }
        internal static ConfigEntry<bool> TransferGestures { get; private set; }
        internal static ConfigEntry<bool> DragTransfer { get; private set; }
        internal static ConfigEntry<bool> AutoCloseDoors { get; private set; }
        internal static ConfigEntry<bool> EquipmentRestore { get; private set; }
        internal static ConfigEntry<bool> MenuMemory { get; private set; }
        internal static ConfigEntry<bool> TextEntryPolish { get; private set; }
        internal static ConfigEntry<bool> PickupFilters { get; private set; }

        internal static ConfigEntry<float> DoorDelaySeconds { get; private set; }
        internal static ConfigEntry<float> DoorRecentUseSeconds { get; private set; }
        internal static ConfigEntry<float> DoorObstructionRadius { get; private set; }

        internal static ConfigEntry<int> PortalTextLimit { get; private set; }
        internal static ConfigEntry<int> SignTextLimit { get; private set; }
        internal static ConfigEntry<int> TameTextLimit { get; private set; }
        internal static ConfigEntry<float> TextCommitRange { get; private set; }

        internal static ConfigEntry<string> FilteredPickupItems { get; private set; }
        internal static ConfigEntry<bool> FilterQuestItems { get; private set; }
        internal static ConfigEntry<string> PickupBypassControllerModifier { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = Bind(config, "General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_969a2758a6e8"));
            HoldToRepeat = Bind(config, "Features", "HoldToRepeat", true,
                global::Runic.Localization.RunicText.Get("text_1c5b71cb56c5"));
            TransferGestures = Bind(config, "Features", "TransferGestures", true,
                global::Runic.Localization.RunicText.Get("text_d349ca13adb4"));
            DragTransfer = Bind(config, "Features", "DragTransfer", false,
                global::Runic.Localization.RunicText.Get("text_1caa13a20179"));
            AutoCloseDoors = Bind(config, "Features", "AutoCloseDoors", false,
                global::Runic.Localization.RunicText.Get("text_15ff80582fc6"));
            EquipmentRestore = Bind(config, "Features", "EquipmentRestore", true,
                global::Runic.Localization.RunicText.Get("text_da01cc78d2db"));
            MenuMemory = Bind(config, "Features", "MenuMemory", true,
                global::Runic.Localization.RunicText.Get("text_004aab945c86"));
            TextEntryPolish = Bind(config, "Features", "TextEntryPolish", true,
                global::Runic.Localization.RunicText.Get("text_57eb2c40e975"));
            PickupFilters = Bind(config, "Features", "PickupFilters", true,
                global::Runic.Localization.RunicText.Get("text_c59edfb36499"));

            DoorDelaySeconds = Bind(config, "Door Auto-Close", "DelaySeconds", 4f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_c31d1b556390"), new AcceptableValueRange<float>(1f, 60f)));
            DoorRecentUseSeconds = Bind(config, "Door Auto-Close", "RecentUseSafetySeconds", 1.5f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_01d5f8ff074b"), new AcceptableValueRange<float>(0.5f, 10f)));
            DoorObstructionRadius = Bind(config, "Door Auto-Close", "ObstructionRadiusMeters", 0.9f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_621cf88ff9fd"),
                    new AcceptableValueRange<float>(0.35f, 2f)));

            PortalTextLimit = Bind(config, "Text Entry", "PortalCharacterLimit", 10,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_80bdb7a5e105"), new AcceptableValueRange<int>(1, 10)));
            SignTextLimit = Bind(config, "Text Entry", "SignCharacterLimit", 50,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_efb7940f6a02"),
                    new AcceptableValueRange<int>(1, 200)));
            TameTextLimit = Bind(config, "Text Entry", "TameCharacterLimit", 10,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_1d1774e82937"), new AcceptableValueRange<int>(1, 10)));
            TextCommitRange = Bind(config, "Text Entry", "CommitRangeMeters", 6f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_23091f7a28ae"),
                    new AcceptableValueRange<float>(2f, 15f)));

            FilteredPickupItems = Bind(config, "Pickup Filter", "Items", string.Empty,
                global::Runic.Localization.RunicText.Get("text_70899a5a16d4"));
            FilterQuestItems = Bind(config, "Pickup Filter", "AllowQuestItemFiltering", false,
                global::Runic.Localization.RunicText.Get("text_79f444c62fdd"));
            PickupBypassControllerModifier = Bind(
                config,
                "Controller",
                "PickupBypassModifierAction",
                InteractionInputBindings.DefaultPickupBypassControllerModifier,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_53992ef4d815"),
                    new AcceptableValueList<string>(
                        InteractionInputBindings.CreateControllerModifierOptions())));
            VerboseLogging = Bind(config, "Diagnostics", "VerboseLogging", false,
                global::Runic.Localization.RunicText.Get("text_e4ef5138e21c"));

            config.SettingChanged += (_, arguments) =>
                Changed?.Invoke(arguments?.ChangedSetting?.Definition);
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile file,
            string section,
            string key,
            T value,
            string description) =>
            file.Bind(section, key, value, description);

        private static ConfigEntry<T> Bind<T>(
            ConfigFile file,
            string section,
            string key,
            T value,
            ConfigDescription description) =>
            file.Bind(section, key, value, description);
    }
}
