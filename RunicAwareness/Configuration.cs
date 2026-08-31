using BepInEx.Configuration;

namespace RunicAwareness
{
    public enum AwarenessTimerPrecision
    {
        WholeSecond = 1,
        FiveSeconds = 5,
        TenSeconds = 10,
        WholeMinute = 60
    }

    public enum AwarenessOverlayAnchor
    {
        TopRight = 0,
        TopLeft = 1,
        BottomRight = 2,
        BottomLeft = 3,
        MiddleLeft = 4
    }

    internal static class AwarenessConfig
    {
        internal const int HardMaximumFoodScan = 8;
        internal const int HardMaximumEffectScan = 32;
        internal const int HardMaximumComfortPieces = 64;
        internal const int HardMaximumPanelLines = 40;
        internal const int HardMaximumPanelCharacters = 4096;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> ShowFoodTimers { get; private set; }
        internal static ConfigEntry<bool> ShowEffectTimers { get; private set; }
        internal static ConfigEntry<bool> ShowComfort { get; private set; }
        internal static ConfigEntry<bool> ShowItemComparison { get; private set; }
        internal static ConfigEntry<bool> ShowProduction { get; private set; }
        internal static ConfigEntry<bool> ShowAgriculture { get; private set; }
        internal static ConfigEntry<bool> ShowBuilding { get; private set; }
        internal static ConfigEntry<bool> ShowTamedAnimals { get; private set; }
        internal static ConfigEntry<AwarenessTimerPrecision> TimerPrecision { get; private set; }
        internal static ConfigEntry<float> RefreshInterval { get; private set; }
        internal static ConfigEntry<int> MaximumEffectRows { get; private set; }
        internal static ConfigEntry<int> MaximumComfortRows { get; private set; }
        internal static ConfigEntry<int> MaximumContextLines { get; private set; }
        internal static ConfigEntry<float> UiScale { get; private set; }
        internal static ConfigEntry<float> ControllerScaleMultiplier { get; private set; }
        internal static ConfigEntry<AwarenessOverlayAnchor> Anchor { get; private set; }
        internal static ConfigEntry<bool> MiddleLeftAnchorMigrationApplied { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable Runic Awareness. This module only observes and renders already-known local state.");

            ShowFoodTimers = Panel(config, "FoodTimers", true,
                "Show remaining time and the local food item that supplied each active food slot.");
            ShowEffectTimers = Panel(config, "EffectTimers", true,
                "Show locally active HUD status effects and their remaining time.");
            ShowComfort = Panel(config, "ComfortBreakdown", true,
                "Show current comfort, Rested time, and bounded category results from vanilla's own comfort pass.");
            ShowItemComparison = Panel(config, "ItemComparison", true,
                "Retain bounded item-comparison capture for compatibility; inventory UI suppression takes precedence, so this capture is not rendered as an Awareness panel while inventory is open.");
            ShowProduction = Panel(config, "ProductionContext", true,
                "Show the currently hovered station's visible state and optional Runic Production status when its runtime contract is available.");
            ShowAgriculture = Panel(config, "AgricultureContext", true,
                "Show bounded text already exposed by the currently hovered crop or beehive.");
            ShowBuilding = Panel(config, "BuildingContext", true,
                "Show the currently selected or hovered build piece's known transform, health, station level, and vanilla support legend.");
            ShowTamedAnimals = Panel(config, "TamedAnimalContext", true,
                "Show bounded text already exposed by the currently hovered tameable animal.");

            TimerPrecision = config.Bind(
                "Display",
                "TimerPrecision",
                AwarenessTimerPrecision.WholeSecond,
                "Round remaining timers upward to whole seconds, five seconds, ten seconds, or whole minutes.");
            RefreshInterval = config.Bind(
                "Display",
                "RefreshIntervalSeconds",
                0.35f,
                new ConfigDescription(
                    "Low-frequency state sampling interval. Text is rebuilt only when a bounded state signature changes.",
                    new AcceptableValueRange<float>(0.2f, 2f)));
            MaximumEffectRows = config.Bind(
                "Display",
                "MaximumEffectRows",
                5,
                new ConfigDescription(
                    "Maximum visible HUD-effect rows. The scan itself is hard-capped at 32 local effects.",
                    new AcceptableValueRange<int>(1, 8)));
            MaximumComfortRows = config.Bind(
                "Display",
                "MaximumComfortWinnerRows",
                6,
                new ConfigDescription(
                    "Maximum named comfort winners. Oversized vanilla comfort sets fail closed to a level-only explanation.",
                    new AcceptableValueRange<int>(1, 8)));
            MaximumContextLines = config.Bind(
                "Display",
                "MaximumContextLines",
                5,
                new ConfigDescription(
                    "Maximum lines copied from already-visible current-hover text.",
                    new AcceptableValueRange<int>(1, 6)));
            UiScale = config.Bind(
                "Display",
                "UiScale",
                1f,
                new ConfigDescription(
                    "Overlay text and panel scale.",
                    new AcceptableValueRange<float>(0.75f, 2f)));
            ControllerScaleMultiplier = config.Bind(
                "Display",
                "ControllerScaleMultiplier",
                1.15f,
                new ConfigDescription(
                    "Additional readability scale while Valheim reports controller navigation as active.",
                    new AcceptableValueRange<float>(1f, 1.5f)));
            Anchor = config.Bind(
                "Display",
                "Anchor",
                AwarenessOverlayAnchor.MiddleLeft,
                "Place the non-interactive overlay at a safe-area anchor. The default is left-middle.");
            MiddleLeftAnchorMigrationApplied = config.Bind(
                "Migrations",
                "DefaultAnchorMovedToMiddleLeft",
                false,
                "Internal one-time migration marker. Once true, later Anchor choices are never rewritten by this migration.");
            if (!MiddleLeftAnchorMigrationApplied.Value)
            {
                AwarenessOverlayAnchor migrated = MigrateLegacyAnchor(Anchor.Value, false);
                if (migrated != Anchor.Value) Anchor.Value = migrated;
                MiddleLeftAnchorMigrationApplied.Value = true;
            }
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Log Awareness configuration refreshes. Never logs hidden world state.");
        }

        internal static AwarenessOverlayAnchor MigrateLegacyAnchor(
            AwarenessOverlayAnchor anchor,
            bool migrationApplied) =>
            !migrationApplied && anchor == AwarenessOverlayAnchor.TopRight
                ? AwarenessOverlayAnchor.MiddleLeft
                : anchor;

        private static ConfigEntry<bool> Panel(
            ConfigFile config,
            string key,
            bool defaultValue,
            string description) =>
            config.Bind("Panels", key, defaultValue, description);
    }
}
