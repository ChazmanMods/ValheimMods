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
                global::Runic.Localization.RunicText.Get("text_ec3a59079845"));

            ShowFoodTimers = Panel(config, "FoodTimers", true,
                global::Runic.Localization.RunicText.Get("text_a4bb65e67fea"));
            ShowEffectTimers = Panel(config, "EffectTimers", true,
                global::Runic.Localization.RunicText.Get("text_8c10f579fab2"));
            ShowComfort = Panel(config, "ComfortBreakdown", true,
                global::Runic.Localization.RunicText.Get("text_0a62886999a3"));
            ShowItemComparison = Panel(config, "ItemComparison", true,
                global::Runic.Localization.RunicText.Get("text_d8c348bb2521"));
            ShowProduction = Panel(config, "ProductionContext", true,
                global::Runic.Localization.RunicText.Get("text_c15b51f64b09"));
            ShowAgriculture = Panel(config, "AgricultureContext", true,
                global::Runic.Localization.RunicText.Get("text_f0ca691afa6f"));
            ShowBuilding = Panel(config, "BuildingContext", true,
                global::Runic.Localization.RunicText.Get("text_dc6303811cd6"));
            ShowTamedAnimals = Panel(config, "TamedAnimalContext", true,
                global::Runic.Localization.RunicText.Get("text_cbe9e30cc686"));

            TimerPrecision = config.Bind(
                "Display",
                "TimerPrecision",
                AwarenessTimerPrecision.WholeSecond,
                global::Runic.Localization.RunicText.Get("text_8286cca9c032"));
            RefreshInterval = config.Bind(
                "Display",
                "RefreshIntervalSeconds",
                0.35f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_352dbacb99be"),
                    new AcceptableValueRange<float>(0.2f, 2f)));
            MaximumEffectRows = config.Bind(
                "Display",
                "MaximumEffectRows",
                5,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_e5d72a41f73c"),
                    new AcceptableValueRange<int>(1, 8)));
            MaximumComfortRows = config.Bind(
                "Display",
                "MaximumComfortWinnerRows",
                6,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_65e1027cf017"),
                    new AcceptableValueRange<int>(1, 8)));
            MaximumContextLines = config.Bind(
                "Display",
                "MaximumContextLines",
                5,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_943e318572fe"),
                    new AcceptableValueRange<int>(1, 6)));
            UiScale = config.Bind(
                "Display",
                "UiScale",
                1f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_bddd88ea038a"),
                    new AcceptableValueRange<float>(0.75f, 2f)));
            ControllerScaleMultiplier = config.Bind(
                "Display",
                "ControllerScaleMultiplier",
                1.15f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_256227e437ea"),
                    new AcceptableValueRange<float>(1f, 1.5f)));
            Anchor = config.Bind(
                "Display",
                "Anchor",
                AwarenessOverlayAnchor.MiddleLeft,
                global::Runic.Localization.RunicText.Get("text_161e710f61d3"));
            MiddleLeftAnchorMigrationApplied = config.Bind(
                "Migrations",
                "DefaultAnchorMovedToMiddleLeft",
                false,
                global::Runic.Localization.RunicText.Get("text_65bb41d59a9b"));
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
                global::Runic.Localization.RunicText.Get("text_4a49a608f002"));
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
