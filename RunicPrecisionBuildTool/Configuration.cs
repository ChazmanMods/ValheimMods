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
                "Master client-side enable for Runic placement manipulation and orientation hints.");
            RequirePrecisionMode = config.Bind(
                "General",
                "RequirePrecisionMode",
                true,
                "When true (recommended), Runic transforms and utilities activate only after the explicit PrecisionModeToggle while building.");
            WheelBindingSchemaVersion = config.Bind(
                "Compatibility",
                "WheelBindingSchemaVersion",
                0,
                "Internal one-time wheel-binding migration marker. Leave this value unchanged.");

            YawWheelChord = BindWheelChord(
                config,
                "YawWheelChord",
                new KeyboardShortcut(KeyCode.None),
                "Selector chord held while scrolling for normal yaw. None means wheel alone. Keys absent from every rotation selector are transparent.");
            PitchWheelChord = BindWheelChord(
                config,
                "PitchWheelChord",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Selector chord held while scrolling for normal pitch. Keys absent from every rotation selector are transparent.");
            RollWheelChord = BindWheelChord(
                config,
                "RollWheelChord",
                new KeyboardShortcut(KeyCode.LeftShift),
                "Complete selector chord held while scrolling for normal roll. The configured pitch chord may also be held; roll wins.");
            FineYawWheelChord = BindWheelChord(
                config,
                "FineYawWheelChord",
                new KeyboardShortcut(KeyCode.V),
                "Selector chord held while scrolling for fine yaw. Keys absent from every rotation selector are transparent. None disables this chord.");
            FinePitchWheelChord = BindWheelChord(
                config,
                "FinePitchWheelChord",
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt),
                "Selector chord held while scrolling for fine pitch. Keys absent from every rotation selector are transparent. None disables this chord.");
            FineRollWheelChord = BindWheelChord(
                config,
                "FineRollWheelChord",
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftShift),
                "Complete selector chord held while scrolling for fine roll. The configured fine-pitch chord may also be held; roll wins. None disables this chord.");

            MatchRotation = config.Bind(
                "Controls - Actions",
                "MatchRotation",
                new KeyboardShortcut(KeyCode.Keypad0),
                "Press this complete key combination to copy the aimed-at build piece's exact rotation.");
            MatchPitch = config.Bind(
                "Controls - Actions",
                "MatchPitch",
                new KeyboardShortcut(KeyCode.Keypad1),
                "Press this complete key combination to copy only the aimed-at build piece's pitch.");
            MatchRoll = config.Bind(
                "Controls - Actions",
                "MatchRoll",
                new KeyboardShortcut(KeyCode.Keypad2),
                "Press this complete key combination to copy only the aimed-at build piece's roll.");
            MatchYaw = config.Bind(
                "Controls - Actions",
                "MatchYaw",
                new KeyboardShortcut(KeyCode.Keypad3),
                "Press this complete key combination to copy only the aimed-at build piece's yaw.");
            MatchPositionX = config.Bind(
                "Controls - Actions", "MatchPositionX",
                new KeyboardShortcut(KeyCode.Keypad4),
                "Copy only the aimed piece's world X position and hold it through vanilla validation.");
            MatchPositionY = config.Bind(
                "Controls - Actions", "MatchPositionY",
                new KeyboardShortcut(KeyCode.Keypad5),
                "Copy only the aimed piece's world Y position and hold it through vanilla validation.");
            MatchPositionZ = config.Bind(
                "Controls - Actions", "MatchPositionZ",
                new KeyboardShortcut(KeyCode.Keypad6),
                "Copy only the aimed piece's world Z position and hold it through vanilla validation.");
            MatchPosition = config.Bind(
                "Controls - Actions", "MatchPosition",
                new KeyboardShortcut(KeyCode.Keypad7),
                "Copy the aimed piece's complete world position without copying rotation or scale.");
            MatchTransform = config.Bind(
                "Controls - Actions", "MatchTransform",
                new KeyboardShortcut(KeyCode.Keypad8),
                "Copy the aimed piece's complete position and rotation without copying ownership, damage, or scale.");
            MatchSnapSide = config.Bind(
                "Controls - Actions", "MatchSnapSide",
                new KeyboardShortcut(KeyCode.Keypad9),
                "Lock the currently validated vanilla source/target snap pair with opposed normals and matching tangent.");
            RepeatTransform = config.Bind(
                "Controls - Actions", "RepeatTransform",
                new KeyboardShortcut(KeyCode.KeypadPeriod),
                "Apply the bounded relative position/rotation pattern from the two most recent committed placements.");
            ResetPitch = config.Bind(
                "Controls - Axis Reset", "ResetPitch",
                new KeyboardShortcut(KeyCode.Keypad1, KeyCode.LeftShift),
                "Reset only pitch to the current vanilla placement base.");
            ResetRoll = config.Bind(
                "Controls - Axis Reset", "ResetRoll",
                new KeyboardShortcut(KeyCode.Keypad2, KeyCode.LeftShift),
                "Reset only roll to the current vanilla placement base.");
            ResetYaw = config.Bind(
                "Controls - Axis Reset", "ResetYaw",
                new KeyboardShortcut(KeyCode.Keypad3, KeyCode.LeftShift),
                "Reset only yaw to the current vanilla placement base.");
            ResetSway = config.Bind(
                "Controls - Axis Reset", "ResetSway",
                new KeyboardShortcut(KeyCode.Keypad4, KeyCode.LeftShift),
                "Reset only sway/X movement and its X position match.");
            ResetHeave = config.Bind(
                "Controls - Axis Reset", "ResetHeave",
                new KeyboardShortcut(KeyCode.Keypad5, KeyCode.LeftShift),
                "Reset only heave/Y movement and its Y position match.");
            ResetSurge = config.Bind(
                "Controls - Axis Reset", "ResetSurge",
                new KeyboardShortcut(KeyCode.Keypad6, KeyCode.LeftShift),
                "Reset only surge/Z movement and its Z position match.");
            PrecisionModeToggle = config.Bind(
                "Controls - Actions", "PrecisionModeToggle",
                new KeyboardShortcut(KeyCode.P),
                "Explicitly toggle precision mode for the current hammer placement session.");
            AxisGuides = config.Bind(
                "Controls - Actions",
                "AxisGuides",
                new KeyboardShortcut(KeyCode.G),
                "Hold this complete key combination to show the placement's world-axis guides. " +
                "Every listed key is required, while other rotation controls may be held at the " +
                "same time. The guides hide immediately when the chord is released or placement " +
                "input is gated. Valheim's overlapping G radial action is suppressed only while " +
                "an eligible placement actively owns this guide chord.");
            Reset = config.Bind(
                "Controls - Actions",
                "Reset",
                new KeyboardShortcut(KeyCode.F10),
                "Press this complete key combination to clear the Runic rotation and translation.");

            SearchNext = config.Bind(
                "Controls - Build Catalog", "SearchNext",
                new KeyboardShortcut(KeyCode.F6),
                "Select the next currently unlocked piece matching BuildSearchQuery; an empty query cycles every unlocked piece.");
            ToggleFavorite = config.Bind(
                "Controls - Build Catalog", "ToggleFavorite",
                new KeyboardShortcut(KeyCode.F7),
                "Add or remove the currently selected unlocked piece from the bounded favorites list.");
            NextFavorite = config.Bind(
                "Controls - Build Catalog", "NextFavorite",
                new KeyboardShortcut(KeyCode.F8),
                "Select the next currently unlocked favorite.");
            NextRecent = config.Bind(
                "Controls - Build Catalog", "NextRecent",
                new KeyboardShortcut(KeyCode.F9),
                "Select the next currently unlocked recent selection.");
            UndoLastPlacement = config.Bind(
                "Controls - Safe Utilities", "UndoLastPlacement",
                new KeyboardShortcut(KeyCode.F11),
                "Undo the most recent unchanged, full-health, inert, structurally independent placement after fresh native local-owner checks.");
            AreaRepair = config.Bind(
                "Controls - Safe Utilities", "AreaRepair",
                new KeyboardShortcut(KeyCode.F12),
                "Repair a bounded nearby set after fresh native local-owner checks with proportional hammer durability.");
            BuildSearchQuery = config.Bind(
                "Build Catalog", "SearchQuery", string.Empty,
                "Optional case-insensitive search text, bounded to 64 characters. Empty cycles every PieceTable entry Valheim currently exposes as unlocked.");
            FavoritePieceIds = config.Bind(
                "Build Catalog", "Favorites", string.Empty,
                "Internal bounded list of at most 64 safe prefab identifiers. Invalid or unavailable identifiers are ignored.");
            RecentPieceIds = config.Bind(
                "Build Catalog", "Recents", string.Empty,
                "Internal bounded list of at most 16 safe prefab identifiers. Invalid or unavailable identifiers are ignored.");

            MoveUp = BindMovementChord(
                config, "MoveUp", KeyCode.UpArrow,
                "Move upward by UpDownStepMeters.");
            MoveDown = BindMovementChord(
                config, "MoveDown", KeyCode.DownArrow,
                "Move downward by UpDownStepMeters.");
            MoveLeft = BindMovementChord(
                config, "MoveLeft", KeyCode.LeftArrow,
                "Move left by SideStepMeters.");
            MoveRight = BindMovementChord(
                config, "MoveRight", KeyCode.RightArrow,
                "Move right by SideStepMeters.");
            MoveForward = BindMovementChord(
                config, "MoveForward", KeyCode.PageUp,
                "Move forward by ForwardBackStepMeters.");
            MoveBackward = BindMovementChord(
                config, "MoveBackward", KeyCode.PageDown,
                "Move backward by ForwardBackStepMeters.");

            FineMoveUp = BindFineMovementChord(
                config, "FineMoveUp", KeyCode.UpArrow,
                "Move upward by FineUpDownStepMeters.");
            FineMoveDown = BindFineMovementChord(
                config, "FineMoveDown", KeyCode.DownArrow,
                "Move downward by FineUpDownStepMeters.");
            FineMoveLeft = BindFineMovementChord(
                config, "FineMoveLeft", KeyCode.LeftArrow,
                "Move left by FineSideStepMeters.");
            FineMoveRight = BindFineMovementChord(
                config, "FineMoveRight", KeyCode.RightArrow,
                "Move right by FineSideStepMeters.");
            FineMoveForward = BindFineMovementChord(
                config, "FineMoveForward", KeyCode.PageUp,
                "Move forward by FineForwardBackStepMeters.");
            FineMoveBackward = BindFineMovementChord(
                config, "FineMoveBackward", KeyCode.PageDown,
                "Move backward by FineForwardBackStepMeters.");

            RotationIncrementsPerCircle = config.Bind(
                "Rotation",
                "IncrementsPerCircle",
                DefaultRotationIncrements,
                new ConfigDescription(
                    "Number of equal normal increments in one full 360 degree turn. " +
                    "Examples: 8 = 45 degrees, 16 = 22.5 degrees, 32 = 11.25 degrees.",
                    new AcceptableValueRange<int>(1, 36000)));
            FineRotationIncrementsPerCircle = config.Bind(
                "Rotation",
                "FineIncrementsPerCircle",
                DefaultFineRotationIncrements,
                new ConfigDescription(
                    "Number of equal fine increments in one full 360 degree turn. " +
                    "The default 360 equals 1 degree per wheel increment.",
                    new AcceptableValueRange<int>(1, 36000)));

            SideStepMeters = BindDistance(
                config, "SideStepMeters", 0.25f,
                "Normal left/right distance per key press in meters.");
            UpDownStepMeters = BindDistance(
                config, "UpDownStepMeters", 0.25f,
                "Normal up/down distance per key press in meters.");
            ForwardBackStepMeters = BindDistance(
                config, "ForwardBackStepMeters", 0.25f,
                "Normal forward/back distance per key press in meters.");
            FineSideStepMeters = BindDistance(
                config, "FineSideStepMeters", 0.05f,
                "Fine left/right distance per key press in meters.");
            FineUpDownStepMeters = BindDistance(
                config, "FineUpDownStepMeters", 0.05f,
                "Fine up/down distance per key press in meters.");
            FineForwardBackStepMeters = BindDistance(
                config, "FineForwardBackStepMeters", 0.05f,
                "Fine forward/back distance per key press in meters.");
            ReferenceFrame = config.Bind(
                "Movement",
                "ReferenceFrame",
                PlacementReferenceFrame.World,
                "World keeps movement on fixed global axes; Local rotates each new movement increment by the pending piece orientation.");
            AreaRepairRadiusMeters = config.Bind(
                "Safe Utilities",
                "AreaRepairRadiusMeters",
                5f,
                new ConfigDescription(
                    "Bounded area-repair radius around the authenticated local or dedicated player.",
                    new AcceptableValueRange<float>(1f, 12f)));
            AreaRepairMaximumPieces = config.Bind(
                "Safe Utilities",
                "AreaRepairMaximumPieces",
                16,
                new ConfigDescription(
                    "Maximum successful repairs per explicit action. Collider discovery is independently capped at 256.",
                    new AcceptableValueRange<int>(1, 64)));

            ShowPlacementAngles = config.Bind(
                "UI",
                "ShowPlacementAngles",
                true,
                "Show the Precision line in the selected-piece panel with effective Pitch/Roll/Yaw and the next wheel increment.");
            ShowTargetAngles = config.Bind(
                "UI",
                "ShowTargetAngles",
                true,
                "Show the conditional Target line in the selected-piece panel and Match Orientation hint.");
            ShowPositionReadout = config.Bind(
                "UI",
                "ShowPositionReadout",
                true,
                "Include bounded world position, local movement delta, reference frame, target distance, and active snap state in the selected-piece rows.");
            ReadoutFontSize = config.Bind(
                "UI",
                "ReadoutFontSize",
                36,
                new ConfigDescription(
                    "Maximum font size for the bounded selected-piece Precision and Target readouts.",
                    new AcceptableValueRange<int>(22, 54)));
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Enable additional state-transition diagnostics. Per-frame logging is never emitted.");

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
                description + " This is a complete action chord, not a shared modifier.");

        private static ConfigEntry<KeyboardShortcut> BindFineMovementChord(
            ConfigFile config,
            string key,
            KeyCode directionKey,
            string description) =>
            config.Bind(
                "Controls - Movement Fine",
                key,
                new KeyboardShortcut(directionKey, KeyCode.LeftAlt, KeyCode.V),
                description + " This is a complete action chord, not a shared modifier.");

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
