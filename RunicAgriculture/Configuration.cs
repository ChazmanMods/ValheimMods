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
                "Enable Runic planting previews, explicit batch actions, and read-only status text.");
            Pattern = config.Bind(
                "Planting Pattern",
                "Pattern",
                PlantingGridPolicy.DefaultPattern,
                "Preview shape: Row, Grid, Circle, Star, RightTriangle, HalfCircle, or Trapezoid. The cycle shortcut changes it live.");
            Alignment = config.Bind(
                "Planting Pattern",
                "Alignment",
                AgricultureAlignment.PlayerHeading,
                "Align to player heading, world axes, or the nearest two matching crops.");
            Rows = config.Bind(
                "Planting Pattern",
                "Rows",
                5,
                new ConfigDescription(
                    "Forward footprint in planting rows for every shape except Row. Change it live with the row controls.",
                    new AcceptableValueRange<int>(1, PatternRequest.AbsoluteMaximumDimension)));
            Columns = config.Bind(
                "Planting Pattern",
                "ColumnsOrPoints",
                5,
                new ConfigDescription(
                    "Side-to-side footprint in planting columns. Change it live with the column controls.",
                    new AcceptableValueRange<int>(1, PatternRequest.AbsoluteMaximumDimension)));
            Spacing = config.Bind(
                "Planting Pattern",
                "SpacingMeters",
                1.5f,
                new ConfigDescription(
                    "Center-to-center spacing for every generated shape.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            LegacyCircleRadius = config.Bind(
                "Planting Pattern",
                "CircleRadiusMeters",
                3f,
                new ConfigDescription(
                    "Legacy migration-only circle radius. Live Circle size now uses Rows and Columns; this value is never reapplied after the migration marker is set.",
                    new AcceptableValueRange<float>(0.5f, 12f)));
            LegacyCircleRadiusMigrationApplied = config.Bind(
                "Migrations",
                "LegacyCircleRadiusMappedToRowsAndColumns",
                false,
                "Internal one-time migration marker. Custom row/column dimensions take precedence over the legacy radius.");
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
                "Switch the side used by RightTriangle and HalfCircle, or mirror an asymmetric Trapezoid.");
            TrapezoidLeftPinch = config.Bind(
                "Planting Pattern",
                "TrapezoidLeftPinch",
                0.5f,
                new ConfigDescription(
                    "How far the unmirrored trapezoid's left front edge tapers inward (0 = straight, 1 = center).",
                    new AcceptableValueRange<float>(0f, 1f)));
            TrapezoidRightPinch = config.Bind(
                "Planting Pattern",
                "TrapezoidRightPinch",
                0.5f,
                new ConfigDescription(
                    "How far the unmirrored trapezoid's right front edge tapers inward (0 = straight, 1 = center).",
                    new AcceptableValueRange<float>(0f, 1f)));
            NearbySeedChestRange = config.Bind(
                "Planting Resources",
                "NearbyChestRangeMeters",
                30f,
                new ConfigDescription(
                    "Player-centered range for eligible nearby chests that may supply planting resources. Personal inventory is always consumed first. Static locally-owned chests only; access and wards are rechecked before mutation.",
                    new AcceptableValueRange<float>(1f, 30f)));
            InvalidPolicy = config.Bind(
                "Confirmation",
                "InvalidPositionPolicy",
                InvalidPositionPolicy.SkipInvalid,
                "Skip amber/gray terrain or spacing failures, or block the entire confirmation before the first plant.");
            ResourcePolicy = config.Bind(
                "Confirmation",
                "ResourceShortfallPolicy",
                ResourceShortfallPolicy.TruncatePredictably,
                "Stop cleanly at the combined personal-and-nearby-chest per-cell resource budget, or block first. A confirmed left-click batch uses one normal stamina/tool-durability action.");
            HarvestRadius = config.Bind(
                "Harvest",
                "RadiusMeters",
                4f,
                new ConfigDescription(
                    "Area-harvest radius for the exact same registered Pickable prefab.",
                    new AcceptableValueRange<float>(1f, 8f)));
            MaximumHarvest = config.Bind(
                "Harvest",
                "MaximumPlants",
                HardMaximumHarvest,
                new ConfigDescription(
                    "Maximum Pickable requests in one area-harvest batch. Hard-capped at 25.",
                    new AcceptableValueRange<int>(1, HardMaximumHarvest)));
            OfferReplantPreview = config.Bind(
                "Harvest",
                "OfferConfirmedReplant",
                true,
                "After bounded area harvest, remember successful positions and offer replant ghosts only for matching crops in authorized planting areas; the cultivator does not need to be equipped and planting is never automatic.");
            ShowHoverStatus = config.Bind(
                "Status",
                "ShowBeeAndCropStatus",
                true,
                "Append concise read-only honey, bee happiness, crop maturity, and growth-failure status.");
            ShowContextualControls = config.Bind(
                "Status",
                "ShowContextualControls",
                true,
                "Replace the vanilla bottom build hints with the Agriculture control bar while a crop preview is active.");
            ControlBarScale = config.Bind(
                "Status",
                "ControlBarScale",
                0.9f,
                new ConfigDescription(
                    "Scale Valheim's compact bottom Agriculture build-hint panel.",
                    new AcceptableValueRange<float>(0.75f, 1.75f)));
            GridAndCompactHudMigrationApplied = config.Bind(
                "Migrations",
                "GridAndCompactBottomHudApplied",
                false,
                "Internal one-time migration marker. Resets the prior shape to the basic Grid and selects the compact native HUD scale once.");
            ApplyGridAndCompactHudMigration(config);
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Log input routing, preview state changes, denials, and action results for troubleshooting.");
            ConfirmPattern = config.Bind(
                "Controls",
                "ConfirmPattern",
                new KeyboardShortcut(KeyCode.Mouse0),
                "Informational binding for ordinary left-click planting; the live Valheim Attack action confirms the displayed pattern.");
            if (ConfirmPattern.Value.MainKey != KeyCode.Mouse0 ||
                System.Linq.Enumerable.Any(ConfirmPattern.Value.Modifiers))
                ConfirmPattern.Value = new KeyboardShortcut(KeyCode.Mouse0);
            CyclePattern = config.Bind(
                "Controls",
                "CyclePattern",
                new KeyboardShortcut(KeyCode.O, KeyCode.LeftAlt),
                "Cycle Row, Grid, Circle, Star, RightTriangle, HalfCircle, and Trapezoid while holding a plant with the cultivator.");
            AreaHarvest = config.Bind(
                "Controls",
                "AreaHarvestModifierInteract",
                new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
                "Modifier-interact an available Pickable to harvest nearby objects of that exact registered prefab.");
            ConfirmReplant = config.Bind(
                "Controls",
                "ConfirmReplant",
                new KeyboardShortcut(KeyCode.T, KeyCode.LeftAlt),
                "Explicitly confirm a pending replant preview while the matching crop is selected.");
            IncreaseRows = config.Bind(
                "Pattern Editing Controls",
                "IncreaseRows",
                new KeyboardShortcut(KeyCode.UpArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Add one forward row to the live planting preview.");
            DecreaseRows = config.Bind(
                "Pattern Editing Controls",
                "DecreaseRows",
                new KeyboardShortcut(KeyCode.DownArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Remove one forward row from the live planting preview.");
            IncreaseColumns = config.Bind(
                "Pattern Editing Controls",
                "IncreaseColumns",
                new KeyboardShortcut(KeyCode.RightArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Add one side-to-side column to the live planting preview.");
            DecreaseColumns = config.Bind(
                "Pattern Editing Controls",
                "DecreaseColumns",
                new KeyboardShortcut(KeyCode.LeftArrow, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Remove one side-to-side column from the live planting preview.");
            ToggleShapeSide = config.Bind(
                "Pattern Editing Controls",
                "ToggleShapeSide",
                new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Switch RightTriangle/HalfCircle sides or mirror an asymmetric Trapezoid.");
            DecreaseLeftPinch = config.Bind(
                "Pattern Editing Controls",
                "DecreaseLeftTrapezoidPinch",
                new KeyboardShortcut(KeyCode.LeftBracket, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Widen the unmirrored trapezoid's left front edge by one step.");
            IncreaseLeftPinch = config.Bind(
                "Pattern Editing Controls",
                "IncreaseLeftTrapezoidPinch",
                new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Pinch the unmirrored trapezoid's left front edge inward by one step.");
            DecreaseRightPinch = config.Bind(
                "Pattern Editing Controls",
                "DecreaseRightTrapezoidPinch",
                new KeyboardShortcut(KeyCode.Semicolon, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Widen the unmirrored trapezoid's right front edge by one step.");
            IncreaseRightPinch = config.Bind(
                "Pattern Editing Controls",
                "IncreaseRightTrapezoidPinch",
                new KeyboardShortcut(KeyCode.Quote, KeyCode.LeftAlt, KeyCode.LeftShift),
                "Pinch the unmirrored trapezoid's right front edge inward by one step.");
            ControllerEnabled = config.Bind(
                "Controller Controls",
                "Enabled",
                true,
                "Enable contextual controller chords through Valheim's ZInput action mappings.");
            ControllerModifier = config.Bind(
                "Controller Controls",
                "ModifierAction",
                ValheimControllerAction.JoyAltKeys,
                "Valheim controller action held as the Runic modifier.");
            ControllerConfirm = config.Bind(
                "Controller Controls",
                "ConfirmAction",
                ValheimControllerAction.JoyPlace,
                "Valheim controller action pressed with the modifier to confirm a visible pattern or matching replant preview.");
            ControllerCycle = config.Bind(
                "Controller Controls",
                "CyclePatternAction",
                ValheimControllerAction.JoyPrevSnap,
                "Valheim controller action pressed with the modifier to cycle the planting pattern.");
            ControllerAreaHarvest = config.Bind(
                "Controller Controls",
                "AreaHarvestAction",
                ValheimControllerAction.JoyUse,
                "Valheim controller action pressed with the modifier while interacting with an available Pickable.");
            ControllerPreviousEditorField = config.Bind(
                "Controller Pattern Editor",
                "PreviousEditorFieldAction",
                ValheimControllerAction.JoyDPadUp,
                "Unmodified Valheim controller action that selects the previous visible pattern setting only during an active crop preview.");
            ControllerNextEditorField = config.Bind(
                "Controller Pattern Editor",
                "NextEditorFieldAction",
                ValheimControllerAction.JoyDPadDown,
                "Unmodified Valheim controller action that selects the next visible pattern setting only during an active crop preview.");
            ControllerDecreaseEditorValue = config.Bind(
                "Controller Pattern Editor",
                "DecreaseEditorValueAction",
                ValheimControllerAction.JoyDPadLeft,
                "Unmodified Valheim controller action that decreases the selected pattern setting only during an active crop preview.");
            ControllerIncreaseEditorValue = config.Bind(
                "Controller Pattern Editor",
                "IncreaseEditorValueAction",
                ValheimControllerAction.JoyDPadRight,
                "Unmodified Valheim controller action that increases the selected pattern setting only during an active crop preview.");
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
