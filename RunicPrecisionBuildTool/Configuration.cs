using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace QuietBuildRotation
{
    internal static class PluginConfig
    {
        private const int DefaultRotationIncrements = 16;
        private const int DefaultFineRotationIncrements = 360;
        private const int CurrentWheelBindingSchemaVersion = 1;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> RequirePrecisionMode { get; private set; }
        internal static ConfigEntry<int> WheelBindingSchemaVersion { get; private set; }

        // A wheel chord is the complete set of Runic selector keys held while the wheel is moved.
        // Roll may additionally include the matching pitch chord and wins by explicit priority.
        // Unconfigured Ctrl keys are transparent. YawWheelChord=None means the
        // unmodified wheel, not a disabled binding.
        internal static ConfigEntry<KeyboardShortcut> YawWheelChord { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> PitchWheelChord { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> RollWheelChord { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineYawWheelChord { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FinePitchWheelChord { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineRollWheelChord { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> MatchRotation { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchPitch { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchRoll { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchYaw { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchPositionX { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchPositionY { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchPositionZ { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchPosition { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchTransform { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MatchSnapSide { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> RepeatTransform { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetPitch { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetRoll { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetYaw { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetSway { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetHeave { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ResetSurge { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> PrecisionModeToggle { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> RotationFrameToggle { get; private set; }
        internal static ConfigEntry<PlacementReferenceFrame> RotationReferenceFrame { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> AxisGuides { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> Reset { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> SearchNext { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ToggleFavorite { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> NextFavorite { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> NextRecent { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> UndoLastPlacement { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> AreaRepair { get; private set; }
        internal static ConfigEntry<string> BuildSearchQuery { get; private set; }
        internal static ConfigEntry<string> FavoritePieceIds { get; private set; }
        internal static ConfigEntry<string> RecentPieceIds { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> MoveUp { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MoveDown { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MoveLeft { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MoveRight { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MoveForward { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> MoveBackward { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveUp { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveDown { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveLeft { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveRight { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveForward { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> FineMoveBackward { get; private set; }

        internal static ConfigEntry<int> RotationIncrementsPerCircle { get; private set; }
        internal static ConfigEntry<int> FineRotationIncrementsPerCircle { get; private set; }
        internal static ConfigEntry<float> SideStepMeters { get; private set; }
        internal static ConfigEntry<float> UpDownStepMeters { get; private set; }
        internal static ConfigEntry<float> ForwardBackStepMeters { get; private set; }
        internal static ConfigEntry<float> FineSideStepMeters { get; private set; }
        internal static ConfigEntry<float> FineUpDownStepMeters { get; private set; }
        internal static ConfigEntry<float> FineForwardBackStepMeters { get; private set; }
        internal static ConfigEntry<PlacementReferenceFrame> ReferenceFrame { get; private set; }
        internal static ConfigEntry<float> AreaRepairRadiusMeters { get; private set; }
        internal static ConfigEntry<int> AreaRepairMaximumPieces { get; private set; }

        internal static ConfigEntry<bool> ShowPlacementAngles { get; private set; }
        internal static ConfigEntry<bool> ShowTargetAngles { get; private set; }
        internal static ConfigEntry<bool> ShowPositionReadout { get; private set; }
        internal static ConfigEntry<int> ReadoutFontSize { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static float RotationStepDegrees =>
            DegreesPerIncrement(RotationIncrementsPerCircle?.Value ?? DefaultRotationIncrements);

        internal static float FineRotationStepDegrees =>
            DegreesPerIncrement(
                FineRotationIncrementsPerCircle?.Value ?? DefaultFineRotationIncrements);

        internal static event Action Changed;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                global::Runic.Localization.RunicText.Get("text_686cd5312a0d"));
            RequirePrecisionMode = config.Bind(
                "General",
                "RequirePrecisionMode",
                true,
                global::Runic.Localization.RunicText.Get("text_593e5f623381"));
            WheelBindingSchemaVersion = config.Bind(
                "Compatibility",
                "WheelBindingSchemaVersion",
                0,
                global::Runic.Localization.RunicText.Get("text_381ef2b5ee71"));

            YawWheelChord = BindWheelChord(
                config,
                "YawWheelChord",
                new KeyboardShortcut(KeyCode.None),
                global::Runic.Localization.RunicText.Get("text_3c8d78f45760"));
            PitchWheelChord = BindWheelChord(
                config,
                "PitchWheelChord",
                new KeyboardShortcut(KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_341839140f70"));
            RollWheelChord = BindWheelChord(
                config,
                "RollWheelChord",
                new KeyboardShortcut(KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_88403d015643"));
            FineYawWheelChord = BindWheelChord(
                config,
                "FineYawWheelChord",
                new KeyboardShortcut(KeyCode.V),
                global::Runic.Localization.RunicText.Get("text_08e4af7bc38f"));
            FinePitchWheelChord = BindWheelChord(
                config,
                "FinePitchWheelChord",
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt),
                global::Runic.Localization.RunicText.Get("text_337d41b9d1a7"));
            FineRollWheelChord = BindWheelChord(
                config,
                "FineRollWheelChord",
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_d20c1bddc354"));

            MatchRotation = config.Bind(
                "Controls - Actions",
                "MatchRotation",
                new KeyboardShortcut(KeyCode.Keypad0),
                global::Runic.Localization.RunicText.Get("text_b0c791cfce88"));
            MatchPitch = config.Bind(
                "Controls - Actions",
                "MatchPitch",
                new KeyboardShortcut(KeyCode.Keypad1),
                global::Runic.Localization.RunicText.Get("text_3b7629265310"));
            MatchRoll = config.Bind(
                "Controls - Actions",
                "MatchRoll",
                new KeyboardShortcut(KeyCode.Keypad2),
                global::Runic.Localization.RunicText.Get("text_e19640ed7d6d"));
            MatchYaw = config.Bind(
                "Controls - Actions",
                "MatchYaw",
                new KeyboardShortcut(KeyCode.Keypad3),
                global::Runic.Localization.RunicText.Get("text_e3b0031d47ac"));
            MatchPositionX = config.Bind(
                "Controls - Actions", "MatchPositionX",
                new KeyboardShortcut(KeyCode.Keypad4),
                global::Runic.Localization.RunicText.Get("text_969a1ec228f6"));
            MatchPositionY = config.Bind(
                "Controls - Actions", "MatchPositionY",
                new KeyboardShortcut(KeyCode.Keypad5),
                global::Runic.Localization.RunicText.Get("text_49d70f25d41a"));
            MatchPositionZ = config.Bind(
                "Controls - Actions", "MatchPositionZ",
                new KeyboardShortcut(KeyCode.Keypad6),
                global::Runic.Localization.RunicText.Get("text_152ae390a3fc"));
            MatchPosition = config.Bind(
                "Controls - Actions", "MatchPosition",
                new KeyboardShortcut(KeyCode.Keypad7),
                global::Runic.Localization.RunicText.Get("text_20fc45da7f7e"));
            MatchTransform = config.Bind(
                "Controls - Actions", "MatchTransform",
                new KeyboardShortcut(KeyCode.Keypad8),
                global::Runic.Localization.RunicText.Get("text_7a7414cf3c26"));
            MatchSnapSide = config.Bind(
                "Controls - Actions", "MatchSnapSide",
                new KeyboardShortcut(KeyCode.Keypad9),
                global::Runic.Localization.RunicText.Get("text_a6ecca97892b"));
            RepeatTransform = config.Bind(
                "Controls - Actions", "RepeatTransform",
                new KeyboardShortcut(KeyCode.KeypadPeriod),
                global::Runic.Localization.RunicText.Get("text_f992e7fdf7fe"));
            ResetPitch = config.Bind(
                "Controls - Axis Reset", "ResetPitch",
                new KeyboardShortcut(KeyCode.Keypad1, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_4e236f0f08c8"));
            ResetRoll = config.Bind(
                "Controls - Axis Reset", "ResetRoll",
                new KeyboardShortcut(KeyCode.Keypad2, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_bf503ab4dd93"));
            ResetYaw = config.Bind(
                "Controls - Axis Reset", "ResetYaw",
                new KeyboardShortcut(KeyCode.Keypad3, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_ddcc6b33d257"));
            ResetSway = config.Bind(
                "Controls - Axis Reset", "ResetSway",
                new KeyboardShortcut(KeyCode.Keypad4, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_73c1e6b04b3c"));
            ResetHeave = config.Bind(
                "Controls - Axis Reset", "ResetHeave",
                new KeyboardShortcut(KeyCode.Keypad5, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_77d5a3a865e7"));
            ResetSurge = config.Bind(
                "Controls - Axis Reset", "ResetSurge",
                new KeyboardShortcut(KeyCode.Keypad6, KeyCode.LeftShift),
                global::Runic.Localization.RunicText.Get("text_baa2395294a3"));
            PrecisionModeToggle = config.Bind(
                "Controls - Actions", "PrecisionModeToggle",
                new KeyboardShortcut(KeyCode.P),
                global::Runic.Localization.RunicText.Get("text_b2e2c85df348"));
            RotationFrameToggle = config.Bind(
                "Controls - Actions", "RotationFrameToggle",
                new KeyboardShortcut(KeyCode.F4),
                global::Runic.Localization.RunicText.Get("text_8c4fbf900f6b"));
            RotationReferenceFrame = config.Bind(
                "Rotation", "ReferenceFrame", PlacementReferenceFrame.Local,
                global::Runic.Localization.RunicText.Get("text_566ea73df41d"));
            AxisGuides = config.Bind(
                "Controls - Actions",
                "AxisGuides",
                new KeyboardShortcut(KeyCode.G),
                global::Runic.Localization.RunicText.Get("text_7a79ac6ac181") +
                global::Runic.Localization.RunicText.Get("text_c0850b137c0b") +
                global::Runic.Localization.RunicText.Get("text_45f0113b3c17") +
                global::Runic.Localization.RunicText.Get("text_ef8dce7b206c") +
                global::Runic.Localization.RunicText.Get("text_29f8f8461c85"));
            Reset = config.Bind(
                "Controls - Actions",
                "Reset",
                new KeyboardShortcut(KeyCode.F10),
                global::Runic.Localization.RunicText.Get("text_4789fbc5e2c9"));

            SearchNext = config.Bind(
                "Controls - Build Catalog", "SearchNext",
                new KeyboardShortcut(KeyCode.F6),
                global::Runic.Localization.RunicText.Get("text_df9b528dff32"));
            ToggleFavorite = config.Bind(
                "Controls - Build Catalog", "ToggleFavorite",
                new KeyboardShortcut(KeyCode.F7),
                global::Runic.Localization.RunicText.Get("text_e92b76fbb4ea"));
            NextFavorite = config.Bind(
                "Controls - Build Catalog", "NextFavorite",
                new KeyboardShortcut(KeyCode.F8),
                global::Runic.Localization.RunicText.Get("text_ea93c881a191"));
            NextRecent = config.Bind(
                "Controls - Build Catalog", "NextRecent",
                new KeyboardShortcut(KeyCode.F9),
                global::Runic.Localization.RunicText.Get("text_afbad8b2b885"));
            UndoLastPlacement = config.Bind(
                "Controls - Safe Utilities", "UndoLastPlacement",
                new KeyboardShortcut(KeyCode.F11),
                global::Runic.Localization.RunicText.Get("text_aa13d0d1ab9d"));
            AreaRepair = config.Bind(
                "Controls - Safe Utilities", "AreaRepair",
                new KeyboardShortcut(KeyCode.F12),
                global::Runic.Localization.RunicText.Get("text_97a95c90cc89"));
            BuildSearchQuery = config.Bind(
                "Build Catalog", "SearchQuery", string.Empty,
                global::Runic.Localization.RunicText.Get("text_7185394aecf0"));
            FavoritePieceIds = config.Bind(
                "Build Catalog", "Favorites", string.Empty,
                global::Runic.Localization.RunicText.Get("text_2de194be734f"));
            RecentPieceIds = config.Bind(
                "Build Catalog", "Recents", string.Empty,
                global::Runic.Localization.RunicText.Get("text_b4c407c0d3a6"));

            MoveUp = BindMovementChord(
                config, "MoveUp", KeyCode.UpArrow,
                global::Runic.Localization.RunicText.Get("text_05a32a590ece"));
            MoveDown = BindMovementChord(
                config, "MoveDown", KeyCode.DownArrow,
                global::Runic.Localization.RunicText.Get("text_b44845e538bb"));
            MoveLeft = BindMovementChord(
                config, "MoveLeft", KeyCode.LeftArrow,
                global::Runic.Localization.RunicText.Get("text_53132963ef68"));
            MoveRight = BindMovementChord(
                config, "MoveRight", KeyCode.RightArrow,
                global::Runic.Localization.RunicText.Get("text_03c4defb3b50"));
            MoveForward = BindMovementChord(
                config, "MoveForward", KeyCode.PageUp,
                global::Runic.Localization.RunicText.Get("text_9f36706d3557"));
            MoveBackward = BindMovementChord(
                config, "MoveBackward", KeyCode.PageDown,
                global::Runic.Localization.RunicText.Get("text_04fe0535d549"));

            FineMoveUp = BindFineMovementChord(
                config, "FineMoveUp", KeyCode.UpArrow,
                global::Runic.Localization.RunicText.Get("text_408ce21b3d2d"));
            FineMoveDown = BindFineMovementChord(
                config, "FineMoveDown", KeyCode.DownArrow,
                global::Runic.Localization.RunicText.Get("text_f2d35e591aa4"));
            FineMoveLeft = BindFineMovementChord(
                config, "FineMoveLeft", KeyCode.LeftArrow,
                global::Runic.Localization.RunicText.Get("text_5f8cc456cfd8"));
            FineMoveRight = BindFineMovementChord(
                config, "FineMoveRight", KeyCode.RightArrow,
                global::Runic.Localization.RunicText.Get("text_fd981ecea312"));
            FineMoveForward = BindFineMovementChord(
                config, "FineMoveForward", KeyCode.PageUp,
                global::Runic.Localization.RunicText.Get("text_20a0616161e9"));
            FineMoveBackward = BindFineMovementChord(
                config, "FineMoveBackward", KeyCode.PageDown,
                global::Runic.Localization.RunicText.Get("text_5ae378e3f395"));

            RotationIncrementsPerCircle = config.Bind(
                "Rotation",
                "IncrementsPerCircle",
                DefaultRotationIncrements,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_012a2e304cdf") +
                    global::Runic.Localization.RunicText.Get("text_58da38ee7127"),
                    new AcceptableValueRange<int>(1, 36000)));
            FineRotationIncrementsPerCircle = config.Bind(
                "Rotation",
                "FineIncrementsPerCircle",
                DefaultFineRotationIncrements,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_1552507550a5") +
                    global::Runic.Localization.RunicText.Get("text_4458b65b04b0"),
                    new AcceptableValueRange<int>(1, 36000)));

            SideStepMeters = BindDistance(
                config, "SideStepMeters", 0.25f,
                global::Runic.Localization.RunicText.Get("text_b3ea4b3126b5"));
            UpDownStepMeters = BindDistance(
                config, "UpDownStepMeters", 0.25f,
                global::Runic.Localization.RunicText.Get("text_c999ce6ebd94"));
            ForwardBackStepMeters = BindDistance(
                config, "ForwardBackStepMeters", 0.25f,
                global::Runic.Localization.RunicText.Get("text_ca0fd42ff029"));
            FineSideStepMeters = BindDistance(
                config, "FineSideStepMeters", 0.05f,
                global::Runic.Localization.RunicText.Get("text_148262651e13"));
            FineUpDownStepMeters = BindDistance(
                config, "FineUpDownStepMeters", 0.05f,
                global::Runic.Localization.RunicText.Get("text_17e2c2afb665"));
            FineForwardBackStepMeters = BindDistance(
                config, "FineForwardBackStepMeters", 0.05f,
                global::Runic.Localization.RunicText.Get("text_78bdc0d3b916"));
            ReferenceFrame = config.Bind(
                "Movement",
                "ReferenceFrame",
                PlacementReferenceFrame.World,
                global::Runic.Localization.RunicText.Get("text_e2b5387ec726"));
            AreaRepairRadiusMeters = config.Bind(
                "Safe Utilities",
                "AreaRepairRadiusMeters",
                5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_4e6ad63a795e"),
                    new AcceptableValueRange<float>(1f, 12f)));
            AreaRepairMaximumPieces = config.Bind(
                "Safe Utilities",
                "AreaRepairMaximumPieces",
                16,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_ac0258a29263"),
                    new AcceptableValueRange<int>(1, 64)));

            ShowPlacementAngles = config.Bind(
                "UI",
                "ShowPlacementAngles",
                true,
                global::Runic.Localization.RunicText.Get("text_39d4c5088ada"));
            ShowTargetAngles = config.Bind(
                "UI",
                "ShowTargetAngles",
                true,
                global::Runic.Localization.RunicText.Get("text_14296ead0976"));
            ShowPositionReadout = config.Bind(
                "UI",
                "ShowPositionReadout",
                true,
                global::Runic.Localization.RunicText.Get("text_cffb02a1d883"));
            ReadoutFontSize = config.Bind(
                "UI",
                "ReadoutFontSize",
                36,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_fc34718ecbba"),
                    new AcceptableValueRange<int>(22, 54)));
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                global::Runic.Localization.RunicText.Get("text_df78c8084db7"));

            MigrateLegacyWheelBindings();
            HookChanges();
        }

        internal static float DegreesPerIncrement(int incrementsPerCircle) =>
            incrementsPerCircle > 0 ? 360f / incrementsPerCircle : 0f;

        internal static string FindExactChordConflicts()
        {
            List<string> conflicts = new List<string>();

            List<ResolvedGesture> wheelGestures = new List<ResolvedGesture>
            {
                Resolve("YawWheelChord", YawWheelChord)
            };
            AddIfConfigured(wheelGestures, "PitchWheelChord", PitchWheelChord);
            AddIfConfigured(wheelGestures, "RollWheelChord", RollWheelChord);
            AddIfConfigured(wheelGestures, "FineYawWheelChord", FineYawWheelChord);
            AddIfConfigured(wheelGestures, "FinePitchWheelChord", FinePitchWheelChord);
            AddIfConfigured(wheelGestures, "FineRollWheelChord", FineRollWheelChord);
            AddDuplicateGestures(wheelGestures, conflicts);

            List<ResolvedGesture> actions = new List<ResolvedGesture>();
            AddIfConfigured(actions, "MatchRotation", MatchRotation);
            AddIfConfigured(actions, "MatchPitch", MatchPitch);
            AddIfConfigured(actions, "MatchRoll", MatchRoll);
            AddIfConfigured(actions, "MatchYaw", MatchYaw);
            AddIfConfigured(actions, "MatchPositionX", MatchPositionX);
            AddIfConfigured(actions, "MatchPositionY", MatchPositionY);
            AddIfConfigured(actions, "MatchPositionZ", MatchPositionZ);
            AddIfConfigured(actions, "MatchPosition", MatchPosition);
            AddIfConfigured(actions, "MatchTransform", MatchTransform);
            AddIfConfigured(actions, "MatchSnapSide", MatchSnapSide);
            AddIfConfigured(actions, "RepeatTransform", RepeatTransform);
            AddIfConfigured(actions, "ResetPitch", ResetPitch);
            AddIfConfigured(actions, "ResetRoll", ResetRoll);
            AddIfConfigured(actions, "ResetYaw", ResetYaw);
            AddIfConfigured(actions, "ResetSway", ResetSway);
            AddIfConfigured(actions, "ResetHeave", ResetHeave);
            AddIfConfigured(actions, "ResetSurge", ResetSurge);
            AddIfConfigured(actions, "PrecisionModeToggle", PrecisionModeToggle);
            AddIfConfigured(actions, "RotationFrameToggle", RotationFrameToggle);
            AddIfConfigured(actions, "Reset", Reset);
            AddIfConfigured(actions, "SearchNext", SearchNext);
            AddIfConfigured(actions, "ToggleFavorite", ToggleFavorite);
            AddIfConfigured(actions, "NextFavorite", NextFavorite);
            AddIfConfigured(actions, "NextRecent", NextRecent);
            AddIfConfigured(actions, "UndoLastPlacement", UndoLastPlacement);
            AddIfConfigured(actions, "AreaRepair", AreaRepair);
            AddIfConfigured(actions, "MoveUp", MoveUp);
            AddIfConfigured(actions, "MoveDown", MoveDown);
            AddIfConfigured(actions, "MoveLeft", MoveLeft);
            AddIfConfigured(actions, "MoveRight", MoveRight);
            AddIfConfigured(actions, "MoveForward", MoveForward);
            AddIfConfigured(actions, "MoveBackward", MoveBackward);
            AddIfConfigured(actions, "FineMoveUp", FineMoveUp);
            AddIfConfigured(actions, "FineMoveDown", FineMoveDown);
            AddIfConfigured(actions, "FineMoveLeft", FineMoveLeft);
            AddIfConfigured(actions, "FineMoveRight", FineMoveRight);
            AddIfConfigured(actions, "FineMoveForward", FineMoveForward);
            AddIfConfigured(actions, "FineMoveBackward", FineMoveBackward);
            AddDuplicateGestures(actions, conflicts);

            return conflicts.Count == 0 ? null : string.Join(", ", conflicts);
        }

        internal static KeyboardShortcut MigrateLegacyRollShortcut(
            KeyboardShortcut shortcut,
            bool fine,
            int schemaVersion)
        {
            if (schemaVersion >= CurrentWheelBindingSchemaVersion)
                return shortcut;

            KeyboardShortcut legacy = fine
                ? new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt, KeyCode.LeftControl)
                : new KeyboardShortcut(KeyCode.LeftControl, KeyCode.LeftAlt);
            if (!ShortcutHasExactKeys(shortcut, legacy))
                return shortcut;

            return fine
                ? new KeyboardShortcut(KeyCode.V, KeyCode.LeftShift)
                : new KeyboardShortcut(KeyCode.LeftShift);
        }

        internal static bool ShortcutHasExactKeys(
            KeyboardShortcut left,
            KeyboardShortcut right)
        {
            HashSet<KeyCode> leftKeys = new HashSet<KeyCode>();
            HashSet<KeyCode> rightKeys = new HashSet<KeyCode>();
            AddShortcutKeys(leftKeys, left);
            AddShortcutKeys(rightKeys, right);
            return leftKeys.SetEquals(rightKeys);
        }

        private static void MigrateLegacyWheelBindings()
        {
            if (WheelBindingSchemaVersion == null ||
                WheelBindingSchemaVersion.Value >= CurrentWheelBindingSchemaVersion)
                return;

            int previousVersion = WheelBindingSchemaVersion.Value;
            RollWheelChord.Value = MigrateLegacyRollShortcut(
                RollWheelChord.Value, false, previousVersion);
            FineRollWheelChord.Value = MigrateLegacyRollShortcut(
                FineRollWheelChord.Value, true, previousVersion);

            // Write the marker last. If configuration saving is interrupted, the exact-key
            // migration is safe to run again on the next startup.
            WheelBindingSchemaVersion.Value = CurrentWheelBindingSchemaVersion;
        }

        private static ConfigEntry<KeyboardShortcut> BindWheelChord(
            ConfigFile config,
            string key,
            KeyboardShortcut defaultValue,
            string description) =>
            config.Bind("Controls - Rotation Wheel", key, defaultValue, description);

        private static ConfigEntry<KeyboardShortcut> BindMovementChord(
            ConfigFile config,
            string key,
            KeyCode directionKey,
            string description) =>
            config.Bind(
                "Controls - Movement",
                key,
                new KeyboardShortcut(directionKey, KeyCode.LeftAlt),
                description + global::Runic.Localization.RunicText.Get("text_1dfd2bc98927"));

        private static ConfigEntry<KeyboardShortcut> BindFineMovementChord(
            ConfigFile config,
            string key,
            KeyCode directionKey,
            string description) =>
            config.Bind(
                "Controls - Movement Fine",
                key,
                new KeyboardShortcut(directionKey, KeyCode.LeftAlt, KeyCode.V),
                description + global::Runic.Localization.RunicText.Get("text_1dfd2bc98927"));

        private static ConfigEntry<float> BindDistance(
            ConfigFile config,
            string key,
            float defaultValue,
            string description) =>
            config.Bind(
                "Movement",
                key,
                defaultValue,
                new ConfigDescription(
                    description,
                    new AcceptableValueRange<float>(0.001f, 10f)));

        private static void HookChanges()
        {
            Enabled.SettingChanged += OnSettingChanged;
            RequirePrecisionMode.SettingChanged += OnSettingChanged;
            YawWheelChord.SettingChanged += OnSettingChanged;
            PitchWheelChord.SettingChanged += OnSettingChanged;
            RollWheelChord.SettingChanged += OnSettingChanged;
            FineYawWheelChord.SettingChanged += OnSettingChanged;
            FinePitchWheelChord.SettingChanged += OnSettingChanged;
            FineRollWheelChord.SettingChanged += OnSettingChanged;
            MatchRotation.SettingChanged += OnSettingChanged;
            MatchPitch.SettingChanged += OnSettingChanged;
            MatchRoll.SettingChanged += OnSettingChanged;
            MatchYaw.SettingChanged += OnSettingChanged;
            MatchPositionX.SettingChanged += OnSettingChanged;
            MatchPositionY.SettingChanged += OnSettingChanged;
            MatchPositionZ.SettingChanged += OnSettingChanged;
            MatchPosition.SettingChanged += OnSettingChanged;
            MatchTransform.SettingChanged += OnSettingChanged;
            MatchSnapSide.SettingChanged += OnSettingChanged;
            RepeatTransform.SettingChanged += OnSettingChanged;
            ResetPitch.SettingChanged += OnSettingChanged;
            ResetRoll.SettingChanged += OnSettingChanged;
            ResetYaw.SettingChanged += OnSettingChanged;
            ResetSway.SettingChanged += OnSettingChanged;
            ResetHeave.SettingChanged += OnSettingChanged;
            ResetSurge.SettingChanged += OnSettingChanged;
            PrecisionModeToggle.SettingChanged += OnSettingChanged;
            AxisGuides.SettingChanged += OnSettingChanged;
            RotationFrameToggle.SettingChanged += OnSettingChanged;
            RotationReferenceFrame.SettingChanged += OnSettingChanged;
            Reset.SettingChanged += OnSettingChanged;
            SearchNext.SettingChanged += OnSettingChanged;
            ToggleFavorite.SettingChanged += OnSettingChanged;
            NextFavorite.SettingChanged += OnSettingChanged;
            NextRecent.SettingChanged += OnSettingChanged;
            UndoLastPlacement.SettingChanged += OnSettingChanged;
            AreaRepair.SettingChanged += OnSettingChanged;
            BuildSearchQuery.SettingChanged += OnSettingChanged;
            FavoritePieceIds.SettingChanged += OnSettingChanged;
            RecentPieceIds.SettingChanged += OnSettingChanged;
            MoveUp.SettingChanged += OnSettingChanged;
            MoveDown.SettingChanged += OnSettingChanged;
            MoveLeft.SettingChanged += OnSettingChanged;
            MoveRight.SettingChanged += OnSettingChanged;
            MoveForward.SettingChanged += OnSettingChanged;
            MoveBackward.SettingChanged += OnSettingChanged;
            FineMoveUp.SettingChanged += OnSettingChanged;
            FineMoveDown.SettingChanged += OnSettingChanged;
            FineMoveLeft.SettingChanged += OnSettingChanged;
            FineMoveRight.SettingChanged += OnSettingChanged;
            FineMoveForward.SettingChanged += OnSettingChanged;
            FineMoveBackward.SettingChanged += OnSettingChanged;
            RotationIncrementsPerCircle.SettingChanged += OnSettingChanged;
            FineRotationIncrementsPerCircle.SettingChanged += OnSettingChanged;
            SideStepMeters.SettingChanged += OnSettingChanged;
            UpDownStepMeters.SettingChanged += OnSettingChanged;
            ForwardBackStepMeters.SettingChanged += OnSettingChanged;
            FineSideStepMeters.SettingChanged += OnSettingChanged;
            FineUpDownStepMeters.SettingChanged += OnSettingChanged;
            FineForwardBackStepMeters.SettingChanged += OnSettingChanged;
            ReferenceFrame.SettingChanged += OnSettingChanged;
            AreaRepairRadiusMeters.SettingChanged += OnSettingChanged;
            AreaRepairMaximumPieces.SettingChanged += OnSettingChanged;
            ShowPlacementAngles.SettingChanged += OnSettingChanged;
            ShowTargetAngles.SettingChanged += OnSettingChanged;
            ShowPositionReadout.SettingChanged += OnSettingChanged;
            ReadoutFontSize.SettingChanged += OnSettingChanged;
            VerboseLogging.SettingChanged += OnSettingChanged;
        }

        private static void OnSettingChanged(object sender, EventArgs args) => Changed?.Invoke();

        private static bool IsConfigured(ConfigEntry<KeyboardShortcut> entry) =>
            entry != null && entry.Value.MainKey != KeyCode.None;

        private static void AddIfConfigured(
            List<ResolvedGesture> gestures,
            string name,
            ConfigEntry<KeyboardShortcut> entry)
        {
            if (IsConfigured(entry))
                gestures.Add(Resolve(name, entry));
        }

        private static ResolvedGesture Resolve(
            string name,
            ConfigEntry<KeyboardShortcut> entry)
        {
            HashSet<KeyCode> keys = new HashSet<KeyCode>();
            if (entry != null)
                AddShortcutKeys(keys, entry.Value);
            return new ResolvedGesture(name, keys);
        }

        private static void AddShortcutKeys(HashSet<KeyCode> keys, KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey != KeyCode.None)
                keys.Add(shortcut.MainKey);
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier != KeyCode.None)
                    keys.Add(modifier);
            }
        }

        private static void AddDuplicateGestures(
            List<ResolvedGesture> gestures,
            List<string> conflicts)
        {
            for (int left = 0; left < gestures.Count; left++)
            {
                for (int right = left + 1; right < gestures.Count; right++)
                {
                    if (gestures[left].Keys.SetEquals(gestures[right].Keys))
                        conflicts.Add(gestures[left].Name + " and " + gestures[right].Name);
                }
            }
        }

        private sealed class ResolvedGesture
        {
            internal ResolvedGesture(string name, HashSet<KeyCode> keys)
            {
                Name = name;
                Keys = keys;
            }

            internal string Name { get; }

            internal HashSet<KeyCode> Keys { get; }
        }
    }
}
