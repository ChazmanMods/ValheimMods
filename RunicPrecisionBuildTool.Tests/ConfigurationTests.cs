using BepInEx.Configuration;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class ConfigurationTests
    {
        internal static void Register()
        {
            TestRunner.Run(
                "Legacy default roll chords migrate once to layered Shift defaults",
                LegacyDefaultsMigrate);
            TestRunner.Run(
                "Wheel binding migration preserves every custom chord",
                CustomBindingsArePreserved);
            TestRunner.Run(
                "Wheel binding migration is exact-key and idempotent",
                MigrationIsExactAndIdempotent);
        }

        private static void LegacyDefaultsMigrate()
        {
            KeyboardShortcut roll = PluginConfig.MigrateLegacyRollShortcut(
                new KeyboardShortcut(KeyCode.LeftControl, KeyCode.LeftAlt),
                false,
                0);
            KeyboardShortcut fineRoll = PluginConfig.MigrateLegacyRollShortcut(
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftAlt, KeyCode.LeftControl),
                true,
                0);

            AssertKeys(roll, new KeyboardShortcut(KeyCode.LeftShift));
            AssertKeys(
                fineRoll,
                new KeyboardShortcut(KeyCode.V, KeyCode.LeftShift));
        }

        private static void CustomBindingsArePreserved()
        {
            KeyboardShortcut customRoll =
                new KeyboardShortcut(KeyCode.Q, KeyCode.RightControl);
            KeyboardShortcut customFine =
                new KeyboardShortcut(KeyCode.E, KeyCode.V, KeyCode.RightShift);

            AssertKeys(
                customRoll,
                PluginConfig.MigrateLegacyRollShortcut(customRoll, false, 0));
            AssertKeys(
                customFine,
                PluginConfig.MigrateLegacyRollShortcut(customFine, true, 0));

            KeyboardShortcut legacyAfterMarker =
                new KeyboardShortcut(KeyCode.LeftControl, KeyCode.LeftAlt);
            AssertKeys(
                legacyAfterMarker,
                PluginConfig.MigrateLegacyRollShortcut(
                    legacyAfterMarker, false, 1));
        }

        private static void MigrationIsExactAndIdempotent()
        {
            // KeyboardShortcut distinguishes a main key from modifiers, but wheel selection and
            // migration both operate on the exact held-key set. A reordered legacy set migrates.
            KeyboardShortcut reorderedLegacy =
                new KeyboardShortcut(KeyCode.LeftAlt, KeyCode.LeftControl);
            KeyboardShortcut migrated = PluginConfig.MigrateLegacyRollShortcut(
                reorderedLegacy, false, 0);
            AssertKeys(migrated, new KeyboardShortcut(KeyCode.LeftShift));

            KeyboardShortcut repeated = PluginConfig.MigrateLegacyRollShortcut(
                migrated, false, 0);
            AssertKeys(repeated, migrated);

            KeyboardShortcut legacyPlusExtra = new KeyboardShortcut(
                KeyCode.LeftControl,
                KeyCode.LeftAlt,
                KeyCode.RightShift);
            AssertKeys(
                legacyPlusExtra,
                PluginConfig.MigrateLegacyRollShortcut(
                    legacyPlusExtra, false, 0));
        }

        private static void AssertKeys(
            KeyboardShortcut expected,
            KeyboardShortcut actual) =>
            TestAssert.True(
                PluginConfig.ShortcutHasExactKeys(expected, actual),
                "Expected " + expected + " but received " + actual + ".");
    }
}
