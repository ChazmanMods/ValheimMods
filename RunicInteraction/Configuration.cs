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
                "Master switch. When false every patch yields to vanilla behavior.");
            HoldToRepeat = Bind(config, "Features", "HoldToRepeat", true,
                "Let vanilla fuel/input interactions repeat at Valheim's installed 0.2 second hold cadence.");
            TransferGestures = Bind(config, "Features", "TransferGestures", true,
                "Add Alt-click full-stack transfer while retaining vanilla Ctrl-click and controller transfer.");
            DragTransfer = Bind(config, "Features", "DragTransfer", false,
                "Reserved fail-closed gate. Valheim 0.221.12 has no authority-safe drag-sweep transaction boundary.");
            AutoCloseDoors = Bind(config, "Features", "AutoCloseDoors", false,
                "Session-only delayed close for doors opened by the native local player. Off by default.");
            EquipmentRestore = Bind(config, "Features", "EquipmentRestore", true,
                "Restore the previously selected legal weapon/shield after a temporary Tool is put away.");
            MenuMemory = Bind(config, "Features", "MenuMemory", true,
                "Remember crafting selection and active group per station/mode for the current game session.");
            TextEntryPolish = Bind(config, "Features", "TextEntryPolish", true,
                "Validate portal, sign, and tame text immediately before vanilla commit.");
            PickupFilters = Bind(config, "Features", "PickupFilters", true,
                "Decline configured world drops before ownership or inventory mutation. Alt+Use bypasses the filter.");

            DoorDelaySeconds = Bind(config, "Door Auto-Close", "DelaySeconds", 4f,
                new ConfigDescription("Delay after the last door use.", new AcceptableValueRange<float>(1f, 60f)));
            DoorRecentUseSeconds = Bind(config, "Door Auto-Close", "RecentUseSafetySeconds", 1.5f,
                new ConfigDescription("Minimum quiet time before closing.", new AcceptableValueRange<float>(0.5f, 10f)));
            DoorObstructionRadius = Bind(config, "Door Auto-Close", "ObstructionRadiusMeters", 0.9f,
                new ConfigDescription("Non-alloc character/movable-body check around the doorway.",
                    new AcceptableValueRange<float>(0.35f, 2f)));

            PortalTextLimit = Bind(config, "Text Entry", "PortalCharacterLimit", 10,
                new ConfigDescription("Cannot exceed vanilla's portal limit.", new AcceptableValueRange<int>(1, 10)));
            SignTextLimit = Bind(config, "Text Entry", "SignCharacterLimit", 50,
                new ConfigDescription("Upper bound; a sign's smaller prefab limit still wins.",
                    new AcceptableValueRange<int>(1, 200)));
            TameTextLimit = Bind(config, "Text Entry", "TameCharacterLimit", 10,
                new ConfigDescription("Cannot exceed vanilla's tame-name limit.", new AcceptableValueRange<int>(1, 10)));
            TextCommitRange = Bind(config, "Text Entry", "CommitRangeMeters", 6f,
                new ConfigDescription("Maximum portal/sign commit distance; tame naming keeps its vanilla 15 m bound.",
                    new AcceptableValueRange<float>(2f, 15f)));

            FilteredPickupItems = Bind(config, "Pickup Filter", "Items", string.Empty,
                "Comma/semicolon/newline-separated prefab IDs or localized item tokens, maximum 128 exact entries.");
            FilterQuestItems = Bind(config, "Pickup Filter", "AllowQuestItemFiltering", false,
                "Dangerous option. False guarantees quest items bypass pickup filters.");
            PickupBypassControllerModifier = Bind(
                config,
                "Controller",
                "PickupBypassModifierAction",
                InteractionInputBindings.DefaultPickupBypassControllerModifier,
                new ConfigDescription(
                    "Generic Valheim controller action held while JoyUse intentionally bypasses a pickup filter.",
                    new AcceptableValueList<string>(
                        InteractionInputBindings.CreateControllerModifierOptions())));
            VerboseLogging = Bind(config, "Diagnostics", "VerboseLogging", false,
                "Log bounded feature decisions without logging inventory contents or text-entry content.");

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
