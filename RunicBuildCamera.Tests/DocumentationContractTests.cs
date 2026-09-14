using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RunicBuildCamera.Tests
{
    internal static class DocumentationContractTests
    {
        private static readonly IReadOnlyDictionary<string, string> ExpectedSettings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["General.Enabled"] = "true",
                ["Controls.ToggleShortcut"] = "B",
                ["Controls.InvertMouseHorizontal"] = "false",
                ["Controls.InvertMouseVertical"] = "false",
                ["Controls.InvertControllerHorizontal"] = "false",
                ["Controls.InvertControllerVertical"] = "false",
                ["Camera.CameraRange"] = "60",
                ["Camera.MoveSpeed"] = "10",
                ["Camera.FastMoveMultiplier"] = "3",
                ["Camera.WorldRelativeMovement"] = "false",
                ["Remote Actions.RemoteActionDistance"] = "100",
                ["Pickup.PickupEnabled"] = "true",
                ["Pickup.PickupRange"] = "10",
                ["Pickup.PickupIntervalSeconds"] = "0.25",
                ["Mist.DemisterFollowCamera"] = "true",
                ["Mist.DemisterRangeMultiplier"] = "2",
                ["Diagnostics.VerboseLogging"] = "false"
            };

        internal static void Register()
        {
            TestRunner.Run("manifest identity and dependency are aligned", ManifestIdentityIsAligned);
            TestRunner.Run("configuration surface is complete and standalone", ConfigurationSurfaceIsComplete);
            TestRunner.Run("example configuration defaults are exact", ExampleConfigurationDefaultsAreExact);
            TestRunner.Run("README configuration table matches the example", ReadmeConfigurationMatchesExample);
            TestRunner.Run("documentation defines the safety and multiplayer boundary", DocumentationDefinesSafetyBoundary);
            TestRunner.Run("changelog and assembly share version 1.0.3", ChangelogVersionIsAligned);
        }

        private static void ManifestIdentityIsAligned()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.PluginFile("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicBuildCamera", root.GetProperty("name").GetString());
            TestAssert.Equal("1.0.3", root.GetProperty("version_number").GetString());
            TestAssert.True(root.GetProperty("description").GetString()
                .Contains("detached camera", StringComparison.OrdinalIgnoreCase));
            string[] dependencies = root.GetProperty("dependencies")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();
            TestAssert.SequenceEqual(
                new[] { "denikson-BepInExPack_Valheim-5.4.2350" }, dependencies);
            TestAssert.False(dependencies.Any(item =>
                item.Contains("Precision", StringComparison.OrdinalIgnoreCase) ||
                item.Contains("Runic", StringComparison.OrdinalIgnoreCase)),
                "Manifest adds a Runic suite dependency.");
        }

        private static void ConfigurationSurfaceIsComplete()
        {
            string[] actual = typeof(BuildCameraConfig).GetProperties(
                    BindingFlags.Static | BindingFlags.NonPublic)
                .Where(property => property.PropertyType.IsGenericType &&
                                   property.PropertyType.GetGenericTypeDefinition().FullName ==
                                   "BepInEx.Configuration.ConfigEntry`1")
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] expected = ExpectedSettings.Keys
                .Select(key => key.Substring(key.IndexOf('.') + 1))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            TestAssert.SequenceEqual(expected, actual);
            TestAssert.Equal(60f, BuildCameraConfig.DefaultCameraRange);
            TestAssert.Equal(100f, BuildCameraConfig.CameraRangeHardMaximum);
            TestAssert.Equal(100f, BuildCameraConfig.DefaultRemoteActionDistance);
            TestAssert.Equal(100f, BuildCameraConfig.RemoteActionDistanceHardMaximum);
        }

        private static void ExampleConfigurationDefaultsAreExact()
        {
            IReadOnlyDictionary<string, string> actual = ParseConfiguration(
                File.ReadAllText(TestPaths.PluginFile("RunicBuildCamera.cfg.example")));
            TestAssert.Equal(ExpectedSettings.Count, actual.Count,
                "Example configuration has missing or undocumented settings.");
            foreach (KeyValuePair<string, string> expected in ExpectedSettings)
            {
                TestAssert.True(actual.TryGetValue(expected.Key, out string value),
                    "Example configuration is missing " + expected.Key + ".");
                TestAssert.Equal(expected.Value, value,
                    "Default drifted for " + expected.Key + ".");
            }
        }

        private static void ReadmeConfigurationMatchesExample()
        {
            string readme = File.ReadAllText(TestPaths.PluginFile("README.md"));
            foreach (KeyValuePair<string, string> setting in ExpectedSettings)
            {
                int separator = setting.Key.IndexOf('.');
                string section = setting.Key.Substring(0, separator);
                string key = setting.Key.Substring(separator + 1);
                string tablePrefix = "| " + section + " | " + key + " | " + setting.Value + " |";
                TestAssert.True(readme.Contains(tablePrefix, StringComparison.Ordinal),
                    "README table drifted for " + setting.Key + ".");
            }
            TestAssert.True(readme.Contains(Plugin.Guid, StringComparison.Ordinal));
            TestAssert.True(readme.Contains("Version " + Plugin.Version, StringComparison.Ordinal));
        }

        private static void DocumentationDefinesSafetyBoundary()
        {
            string readme = File.ReadAllText(TestPaths.PluginFile("README.md"));
            string normalized = Regex.Replace(readme, @"\s+", " ");
            string[] required =
            {
                "standalone and requires only BepInEx",
                "does not search, open, or transfer items from chests",
                "player has an active Wisplight demister",
                "avatar remains at the original world position and remains vulnerable",
                "not a server trust boundary",
                "No source code, binaries, documentation text, artwork, or other assets"
            };
            foreach (string phrase in required)
                TestAssert.True(normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    "README omits required boundary: " + phrase);
        }

        private static void ChangelogVersionIsAligned()
        {
            string changelog = File.ReadAllText(TestPaths.PluginFile("CHANGELOG.md"));
            TestAssert.True(changelog.Contains("## " + Plugin.Version, StringComparison.Ordinal));
            TestAssert.True(changelog.Contains(Plugin.Guid, StringComparison.Ordinal));
            TestAssert.True(changelog.Contains(
                "no dependency on Runic", StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyDictionary<string, string> ParseConfiguration(string text)
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = null;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int equals = line.IndexOf('=');
                TestAssert.True(equals > 0 && !string.IsNullOrEmpty(section),
                    "Malformed configuration line: " + line);
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                TestAssert.True(settings.TryAdd(section + "." + key, value),
                    "Duplicate setting " + section + "." + key + ".");
            }
            return settings;
        }
    }
}
