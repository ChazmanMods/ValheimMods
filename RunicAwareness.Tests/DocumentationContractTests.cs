using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RunicAwareness.Tests
{
    internal static class DocumentationContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("manifest identity and dependencies are exact", ManifestIsExact);
            TestRunner.Run("documentation states sources and privacy limits", SourcesAndLimitsAreDocumented);
            TestRunner.Run("example configuration covers every panel", ExampleConfigurationIsComplete);
            TestRunner.Run("release notes and testing record are version aligned", ReleaseRecordsAreAligned);
            TestRunner.Run("icon is an exact 256 by 256 PNG", IconIsExact);
        }

        private static void ManifestIsExact()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.PluginFile("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicAwareness", root.GetProperty("name").GetString());
            TestAssert.Equal("1.0.0", root.GetProperty("version_number").GetString());
            string[] dependencies = root.GetProperty("dependencies")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            TestAssert.Equal(1, dependencies.Length);
            TestAssert.True(dependencies.Contains(
                "denikson-BepInExPack_Valheim-5.4.2333", StringComparer.Ordinal));
            TestAssert.False(dependencies.Any(value =>
                value != null && (value.Contains("Production", StringComparison.Ordinal) ||
                                  value.Contains("Storage", StringComparison.Ordinal) ||
                                  value.Contains("Transactions", StringComparison.Ordinal))));
        }

        private static void SourcesAndLimitsAreDocumented()
        {
            string readme = File.ReadAllText(TestPaths.PluginFile("README.md"));
            foreach (string required in new[]
                     {
                         "display-only", "0.221.12", "GetFoods()", "completed vanilla",
                         "0.8 seconds", "5 seconds", "8,192", "10 m", "native hover text",
                         "never claims", "runtime-discovered", "No hidden enemy",
                         "Intentional MVP limits", "does not probe private",
                          "not server authority", "no multiplayer RPC",
                          "no world mutation", "dedicated server", "left-middle",
                          "inventory", "large map", "Runic Portals setup guide"
                     })
                TestAssert.Contains(required, readme);
            TestAssert.Contains("adds no", readme);
            TestAssert.Contains("keybinding", readme);
            TestAssert.Contains("controller", readme);
            TestAssert.Contains("ultrawide", File.ReadAllText(
                TestPaths.PluginFile("TESTING.md")));
        }

        private static void ExampleConfigurationIsComplete()
        {
            string config = File.ReadAllText(TestPaths.PluginFile(
                "RunicAwareness.cfg.example"));
            foreach (string key in new[]
                     {
                         "FoodTimers", "EffectTimers", "ComfortBreakdown", "ItemComparison",
                         "ProductionContext", "AgricultureContext", "BuildingContext",
                         "TamedAnimalContext", "RefreshIntervalSeconds", "UiScale",
                         "ControllerScaleMultiplier", "Anchor"
                     })
                TestAssert.Contains(key + " =", config);
            TestAssert.Contains("Anchor = MiddleLeft", config);
            TestAssert.Contains("DefaultAnchorMovedToMiddleLeft = true", config);
            string configuration = File.ReadAllText(TestPaths.PluginFile("Configuration.cs"));
            TestAssert.Contains(
                "AwarenessOverlayAnchor.MiddleLeft",
                configuration);
            TestAssert.Contains("inventory UI suppression takes precedence", configuration);
            int migration = configuration.IndexOf(
                "if (!MiddleLeftAnchorMigrationApplied.Value)", StringComparison.Ordinal);
            int anchorWrite = configuration.IndexOf("Anchor.Value = migrated", migration,
                StringComparison.Ordinal);
            int markerWrite = configuration.IndexOf(
                "MiddleLeftAnchorMigrationApplied.Value = true", migration,
                StringComparison.Ordinal);
            TestAssert.True(migration >= 0 && anchorWrite > migration && markerWrite > anchorWrite,
                "The legacy default must migrate before the one-time marker is committed.");
        }

        private static void ReleaseRecordsAreAligned()
        {
            string changelog = File.ReadAllText(TestPaths.PluginFile("CHANGELOG.md"));
            string testing = File.ReadAllText(TestPaths.PluginFile("TESTING.md"));
            TestAssert.Contains("## 1.0.0", changelog);
            TestAssert.Contains("Runic Awareness 1.0.0", testing);
            TestAssert.Contains("missing or incompatible optional Portals metadata hides cleanly", testing);
            TestAssert.Contains("interactive UI suppression", testing);
            TestAssert.Contains("left-middle", changelog);
        }

        private static void IconIsExact()
        {
            byte[] png = File.ReadAllBytes(TestPaths.PluginFile("icon.png"));
            TestAssert.True(png.Length > 24);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            TestAssert.True(png.Take(8).SequenceEqual(signature));
            TestAssert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
            TestAssert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            TestAssert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }
    }
}
