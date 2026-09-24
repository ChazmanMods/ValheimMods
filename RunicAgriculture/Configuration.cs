using System;
using BepInEx.Configuration;
using RunicAgriculture.Core;
using UnityEngine;

namespace RunicAgriculture
{
    internal static class AgricultureConfig
    {
        internal const int HardMaximumPreview = PatternRequest.AbsoluteMaximumPoints;
        internal const int HardMaximumHarvest = 25;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<PlantPattern> Pattern { get; private set; }
        internal static ConfigEntry<AgricultureAlignment> Alignment { get; private set; }
        internal static ConfigEntry<bool> SnapToExistingRows { get; private set; }
        internal static ConfigEntry<int> Rows { get; private set; }
        internal static ConfigEntry<int> Columns { get; private set; }
        internal static ConfigEntry<float> Spacing { get; private set; }
        internal static ConfigEntry<float> LegacyCircleRadius { get; private set; }
        internal static ConfigEntry<bool> MirrorShape { get; private set; }
        internal static ConfigEntry<float> TrapezoidLeftPinch { get; private set; }
        internal static ConfigEntry<float> TrapezoidRightPinch { get; private set; }
        internal static ConfigEntry<float> NearbySeedChestRange { get; private set; }
        internal static ConfigEntry<InvalidPositionPolicy> InvalidPolicy { get; private set; }
        internal static ConfigEntry<ResourceShortfallPolicy> ResourcePolicy { get; private set; }
        internal static ConfigEntry<float> HarvestRadius { get; private set; }
        internal static ConfigEntry<int> MaximumHarvest { get; private set; }
        internal static ConfigEntry<bool> OfferReplantPreview { get; private set; }
        internal static ConfigEntry<bool> ShowHoverStatus { get; private set; }
        internal static ConfigEntry<bool> ShowContextualControls { get; private set; }
        internal static ConfigEntry<float> ControlBarScale { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfirmPattern { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> CyclePattern { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> AreaHarvest { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfirmReplant { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> IncreaseRows { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> DecreaseRows { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> IncreaseColumns { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> DecreaseColumns { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ToggleShapeSide { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> DecreaseLeftPinch { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> IncreaseLeftPinch { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> DecreaseRightPinch { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> IncreaseRightPinch { get; private set; }
        internal static ConfigEntry<bool> ControllerEnabled { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerModifier { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerConfirm { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerCycle { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerAreaHarvest { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerPreviousEditorField { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerNextEditorField { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerDecreaseEditorValue { get; private set; }
        internal static ConfigEntry<ValheimControllerAction> ControllerIncreaseEditorValue { get; private set; }
        internal static ConfigEntry<bool> LegacyCircleRadiusMigrationApplied { get; private set; }
        internal static ConfigEntry<bool> GridAndCompactHudMigrationApplied { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                global::Runic.Localization.RunicText.Get("text_062f2399ad55"));
            Pattern = config.Bind(
                "Planting Pattern",
                "Pattern",
                PlantingGridPolicy.DefaultPattern,
                global::Runic.Localization.RunicText.Get("text_3380a0ba3316"));
            Alignment = config.Bind(
                "Planting Pattern",
                "Alignment",
                AgricultureAlignment.PlayerHeading,
                global::Runic.Localization.RunicText.Get("text_634d03807cc5"));
            SnapToExistingRows = config.Bind("Planting Pattern", "SnapToExistingRows", true,
                global::Runic.Localization.RunicText.Get("text_c6a150fcd483"));
            Rows = config.Bind(
                "Planting Pattern",
                "Rows",
                5,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_5f8df80ea007"),
                    new AcceptableValueRange<int>(1, PatternRequest.AbsoluteMaximumDimension)));
            Columns = config.Bind(
                "Planting Pattern",
                "ColumnsOrPoints",
                5,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_a5e75c336fe7"),
                    new AcceptableValueRange<int>(1, PatternRequest.AbsoluteMaximumDimension)));
            Spacing = config.Bind(
                "Planting Pattern",
                "SpacingMeters",
                1.5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_f0a386206418"),
                    new AcceptableValueRange<float>(0.5f, 6f)));
            LegacyCircleRadius = config.Bind(
                "Planting Pattern",
                "CircleRadiusMeters",
                3f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_f61a244eba13"),
                    new AcceptableValueRange<float>(0.5f, 12f)));
            LegacyCircleRadiusMigrationApplied = config.Bind(
                "Migrations",
                "LegacyCircleRadiusMappedToRowsAndColumns",
                false,
                global::Runic.Localization.RunicText.Get("text_44ec06e5158e"));
            if (!LegacyCircleRadiusMigrationApplied.Value)
            {
                bool saveOnConfigSet = config.SaveOnConfigSet;
                try
                {
                    // Rows, columns, and the completion marker are one persisted migration state.
                    // Suppressing per-entry saves prevents a process cut from committing only one
                    // dimension and then treating that half-migrated value as a custom override.
                    config.SaveOnConfigSet = false;
                    if (ShouldMapLegacyCircleRadius(false, Rows.Value, Columns.Value))
                    {
                        int dimension = LegacyCircleDimension(
                            LegacyCircleRadius.Value,
                            Spacing.Value);
                        if (Rows.Value != dimension) Rows.Value = dimension;
                        if (Columns.Value != dimension) Columns.Value = dimension;
                    }
                    LegacyCircleRadiusMigrationApplied.Value = true;
                    config.Save();
                }
                finally
                {
                    config.SaveOnConfigSet = saveOnConfigSet;
                }
            }
            MirrorShape = config.Bind(
                "Planting Pattern",
                "MirrorShape",
                false,
                global::Runic.Localization.RunicText.Get("text_d2b9b602bf21"));
            TrapezoidLeftPinch = config.Bind(
                "Planting Pattern",
                "TrapezoidLeftPinch",
                0.5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_02a606cd059b"),
                    new AcceptableValueRange<float>(0f, 1f)));
            TrapezoidRightPinch = config.Bind(
                "Planting Pattern",
                "TrapezoidRightPinch",
                0.5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_a869d4816893"),
                    new AcceptableValueRange<float>(0f, 1f)));
            NearbySeedChestRange = config.Bind(
                "Planting Resources",
                "NearbyChestRangeMeters",
                30f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_aa00bdc07966"),
                    new AcceptableValueRange<float>(1f, 30f)));
            InvalidPolicy = config.Bind(
                "Confirmation",
                "InvalidPositionPolicy",
                InvalidPositionPolicy.SkipInvalid,
                global::Runic.Localization.RunicText.Get("text_ea163e54008a"));
            ResourcePolicy = config.Bind(
                "Confirmation",
                "ResourceShortfallPolicy",
                ResourceShortfallPolicy.TruncatePredictably,
                global::Runic.Localization.RunicText.Get("text_c6ffae0f23ea"));
            HarvestRadius = config.Bind(
                "Harvest",
                "RadiusMeters",
                4f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_044c4dd6edbf"),
                    new AcceptableValueRange<float>(1f, 8f)));
            MaximumHarvest = config.Bind(
                "Harvest",
                "MaximumPlants",
                HardMaximumHarvest,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_7317af1f94d6"),
                    new AcceptableValueRange<int>(1, HardMaximumHarvest)));
            OfferReplantPreview = config.Bind(
                "Harvest",
                "OfferConfirmedReplant",
                true,
                global::Runic.Localization.RunicText.Get("text_6cb55563a879"));
            ShowHoverStatus = config.Bind(
                "Status",
                "ShowBeeAndCropStatus",
                true,
                global::Runic.Localization.RunicText.Get("text_fd8b538263fd"));
            ShowContextualControls = config.Bind(
                "Status",
                "ShowContextualControls",
                true,
                global::Runic.Localization.RunicText.Get("text_feaaacf65721"));
            ControlBarScale = config.Bind(
                "Status",
                "ControlBarScale",
                0.9f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_dbf41efd8026"),
                    new AcceptableValueRange<float>(0.75f, 1.75f)));
            GridAndCompactHudMigrationApplied = config.Bind(
                "Migrations",
                "GridAndCompactBottomHudApplied",
                false,
                global::Runic.Localization.RunicText.Get("text_d678d2a41521"));
            ApplyGridAndCompactHudMigration(config);
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                global::Runic.Localization.RunicText.Get("text_922bc33074a7"));
            ConfirmPattern = config.Bind(
                "Controls",
                "ConfirmPattern",
                new KeyboardShortcut(KeyCode.Mouse0),
                global::Runic.Localization.RunicText.Get("text_bddae62a4ebb"));
            if (ConfirmPattern.Value.MainKey != KeyCode.Mouse0 ||
                System.Linq.Enumerable.Any(ConfirmPattern.Value.Modifiers))
                ConfirmPattern.Value = new KeyboardShortcut(KeyCode.Mouse0);
            CyclePattern = config.Bind(
                "Controls",
                "CyclePattern",
                new KeyboardShortcut(KeyCode.O, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_944abf2e8819"));
            AreaHarvest = config.Bind(
                "Controls",
                "AreaHarvestModifierInteract",
                new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_f9d7a147a398"));
            ConfirmReplant = config.Bind(
                "Controls",
                "ConfirmReplant",
                new KeyboardShortcut(KeyCode.T, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_9bb76ce57f98"));
            IncreaseRows = config.Bind(
                "Pattern Editing Controls",
                "IncreaseRows",
                new KeyboardShortcut(KeyCode.UpArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_c5d12526d008"));
            DecreaseRows = config.Bind(
                "Pattern Editing Controls",
                "DecreaseRows",
                new KeyboardShortcut(KeyCode.DownArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_f4a62507405f"));
            IncreaseColumns = config.Bind(
                "Pattern Editing Controls",
                "IncreaseColumns",
                new KeyboardShortcut(KeyCode.RightArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_1dc2185a3a67"));
            DecreaseColumns = config.Bind(
                "Pattern Editing Controls",
                "DecreaseColumns",
                new KeyboardShortcut(KeyCode.LeftArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_642dfed9cf66"));
            ToggleShapeSide = config.Bind(
                "Pattern Editing Controls",
                "ToggleShapeSide",
                new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_acb7b0125402"));
            DecreaseLeftPinch = config.Bind(
                "Pattern Editing Controls",
                "DecreaseLeftTrapezoidPinch",
                new KeyboardShortcut(KeyCode.LeftBracket, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_1bf04d54db6f"));
            IncreaseLeftPinch = config.Bind(
                "Pattern Editing Controls",
                "IncreaseLeftTrapezoidPinch",
                new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_af299d6064e1"));
            DecreaseRightPinch = config.Bind(
                "Pattern Editing Controls",
                "DecreaseRightTrapezoidPinch",
                new KeyboardShortcut(KeyCode.Semicolon, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_891c491e9a0a"));
            IncreaseRightPinch = config.Bind(
                "Pattern Editing Controls",
                "IncreaseRightTrapezoidPinch",
                new KeyboardShortcut(KeyCode.Quote, KeyCode.LeftAlt, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_1a9700c0ea7b"));
            ControllerEnabled = config.Bind(
                "Controller Controls",
                "Enabled",
                true,
                global::Runic.Localization.RunicText.Get("text_dfc9d3f71a47"));
            ControllerModifier = config.Bind(
                "Controller Controls",
                "ModifierAction",
                ValheimControllerAction.JoyAltKeys,
                global::Runic.Localization.RunicText.Get("text_dc4902cecbf6"));
            ControllerConfirm = config.Bind(
                "Controller Controls",
                "ConfirmAction",
                ValheimControllerAction.JoyPlace,
                global::Runic.Localization.RunicText.Get("text_5de612a71616"));
            ControllerCycle = config.Bind(
                "Controller Controls",
                "CyclePatternAction",
                ValheimControllerAction.JoyPrevSnap,
                global::Runic.Localization.RunicText.Get("text_d04b45d703bc"));
            ControllerAreaHarvest = config.Bind(
                "Controller Controls",
                "AreaHarvestAction",
                ValheimControllerAction.JoyUse,
                global::Runic.Localization.RunicText.Get("text_087da402a5f0"));
            ControllerPreviousEditorField = config.Bind(
                "Controller Pattern Editor",
                "PreviousEditorFieldAction",
                ValheimControllerAction.JoyDPadUp,
                global::Runic.Localization.RunicText.Get("text_eb40b0bfea5b"));
            ControllerNextEditorField = config.Bind(
                "Controller Pattern Editor",
                "NextEditorFieldAction",
                ValheimControllerAction.JoyDPadDown,
                global::Runic.Localization.RunicText.Get("text_734db65456d0"));
            ControllerDecreaseEditorValue = config.Bind(
                "Controller Pattern Editor",
                "DecreaseEditorValueAction",
                ValheimControllerAction.JoyDPadLeft,
                global::Runic.Localization.RunicText.Get("text_7c8c16085237"));
            ControllerIncreaseEditorValue = config.Bind(
                "Controller Pattern Editor",
                "IncreaseEditorValueAction",
                ValheimControllerAction.JoyDPadRight,
                global::Runic.Localization.RunicText.Get("text_0b2485e84dfa"));
        }

        internal static AgricultureControllerBindings CurrentControllerBindings() =>
            new AgricultureControllerBindings(
                ControllerModifier.Value,
                ControllerConfirm.Value,
                ControllerCycle.Value,
                ControllerAreaHarvest.Value,
                ControllerPreviousEditorField.Value,
                ControllerNextEditorField.Value,
                ControllerDecreaseEditorValue.Value,
                ControllerIncreaseEditorValue.Value);

        internal static int LegacyCircleDimension(float radius, float spacing)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (float.IsNaN(spacing) || float.IsInfinity(spacing) || spacing <= 0f)
                throw new ArgumentOutOfRangeException(nameof(spacing));
            int halfSpan = (int)Math.Round(
                radius / spacing,
                MidpointRounding.AwayFromZero);
            return Math.Max(1, Math.Min(49, halfSpan * 2 + 1));
        }

        internal static bool ShouldMapLegacyCircleRadius(
            bool migrationApplied,
            int rows,
            int columns) =>
            !migrationApplied && rows == 5 && columns == 5;

        private static void ApplyGridAndCompactHudMigration(ConfigFile config)
        {
            if (GridAndCompactHudMigrationApplied.Value) return;
            bool saveOnConfigSet = config.SaveOnConfigSet;
            try
            {
                config.SaveOnConfigSet = false;
                Pattern.Value = PlantingGridPolicy.MigrateToDefaultGrid(false, Pattern.Value);
                if (Math.Abs(ControlBarScale.Value - 1f) < 0.0001f)
                    ControlBarScale.Value = 0.9f;
                GridAndCompactHudMigrationApplied.Value = true;
                config.Save();
            }
            finally
            {
                config.SaveOnConfigSet = saveOnConfigSet;
            }
        }

    }
}
