using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BepInEx.Configuration;

namespace RunicInteraction.Tests
{
    internal static class DocumentationContractTests
    {
        private static readonly IReadOnlyDictionary<string, string> ExpectedSettings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["General.Enabled"] = "true",
                ["Features.HoldToRepeat"] = "true",
                ["Features.TransferGestures"] = "true",
                ["Features.DragTransfer"] = "false",
                ["Features.AutoCloseDoors"] = "false",
                ["Features.EquipmentRestore"] = "true",
                ["Features.MenuMemory"] = "true",
                ["Features.TextEntryPolish"] = "true",
                ["Features.PickupFilters"] = "true",
                ["Door Auto-Close.DelaySeconds"] = "4",
                ["Door Auto-Close.RecentUseSafetySeconds"] = "1.5",
                ["Door Auto-Close.ObstructionRadiusMeters"] = "0.9",
                ["Text Entry.PortalCharacterLimit"] = "10",
                ["Text Entry.SignCharacterLimit"] = "50",
                ["Text Entry.TameCharacterLimit"] = "10",
                ["Text Entry.CommitRangeMeters"] = "6",
                ["Pickup Filter.Items"] = string.Empty,
                ["Pickup Filter.AllowQuestItemFiltering"] = "false",
                ["Controller.PickupBypassModifierAction"] = "JoyRStick",
                ["Diagnostics.VerboseLogging"] = "false"
            };

        internal static void Register()
        {
            TestRunner.Run("manifest identity version and dependencies are aligned", ManifestIsAligned);
            TestRunner.Run("example config contains every and only runtime setting", ConfigIsComplete);
            TestRunner.Run("README config defaults exactly match the example", ReadmeDefaultsMatch);
            TestRunner.Run("README documents authority fail-closed and uninstall boundaries", ReadmeDocumentsBoundaries);
            TestRunner.Run("localization catalog is valid and complete", LocalizationIsValid);
            TestRunner.Run("changelog version and disabled gates are explicit", ChangelogIsAligned);
            TestRunner.Run("icon is a valid square PNG", IconIsValidPng);
        }

        private static void ManifestIsAligned()
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(TestPaths.PluginFile("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicInteraction", root.GetProperty("name").GetString());
            TestAssert.Equal(Plugin.Version, root.GetProperty("version_number").GetString());
            string[] dependencies = root.GetProperty("dependencies").EnumerateArray()
                .Select(item => item.GetString()).ToArray();
            TestAssert.SequenceEqual(new[]
            {
                "denikson-BepInExPack_Valheim-5.4.2333"
            }, dependencies);
        }

        private static void ConfigIsComplete()
        {
            string[] runtime = typeof(InteractionConfig).GetProperties(
                    BindingFlags.Static | BindingFlags.NonPublic)
                .Where(property => property.PropertyType.IsGenericType &&
                                   property.PropertyType.GetGenericTypeDefinition() == typeof(ConfigEntry<>))
                .Select(property => property.Name)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            string[] documented = new[]
                {
                    "Enabled", "HoldToRepeat", "TransferGestures", "DragTransfer",
                    "AutoCloseDoors", "EquipmentRestore", "MenuMemory", "TextEntryPolish",
                    "PickupFilters", "DoorDelaySeconds", "DoorRecentUseSeconds",
                    "DoorObstructionRadius", "PortalTextLimit", "SignTextLimit", "TameTextLimit",
                    "TextCommitRange", "FilteredPickupItems", "FilterQuestItems",
                    "PickupBypassControllerModifier", "VerboseLogging"
                }
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            TestAssert.SequenceEqual(documented, runtime);

            IReadOnlyDictionary<string, string> parsed = ParseConfig(
                File.ReadAllText(TestPaths.PluginFile("RunicInteraction.cfg.example")));
            TestAssert.Equal(ExpectedSettings.Count, parsed.Count);
            foreach (KeyValuePair<string, string> setting in ExpectedSettings)
            {
                TestAssert.True(parsed.TryGetValue(setting.Key, out string actual),
                    "Missing config setting " + setting.Key + ".");
                TestAssert.Equal(setting.Value, actual, "Default drifted for " + setting.Key + ".");
            }
        }

        private static void ReadmeDefaultsMatch()
        {
            string readme = File.ReadAllText(TestPaths.PluginFile("README.md"));
            foreach (KeyValuePair<string, string> setting in ExpectedSettings)
            {
                int separator = setting.Key.LastIndexOf('.');
                string section = setting.Key.Substring(0, separator);
                string key = setting.Key.Substring(separator + 1);
                string shown = setting.Value.Length == 0 ? "empty" : setting.Value;
                TestAssert.True(readme.Contains(
                        "| " + section + " | " + key + " | " + shown + " |",
                        StringComparison.Ordinal),
                    "README default drifted for " + setting.Key + ".");
            }
        }

        private static void ReadmeDocumentsBoundaries()
        {
            string text = File.ReadAllText(TestPaths.PluginFile("README.md"));
            foreach (string phrase in new[]
                     {
                         "Valheim 0.221.12",
                         "only runtime requirement is BepInExPack",
                         "every feature is independently toggleable",
                         "no world migration",
                         "no custom network RPC",
                         "native Valheim ownership",
                         "session-only",
                         "explicit disabled gate",
                         "no enemy selection, tactical logic, or combat automation",
                         "drop remains where it was",
                         "optional Runic Inventory",
                         "no persistent operation",
                         "no world cleanup is required"
                     })
                TestAssert.True(text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    "README omits boundary: " + phrase);
        }

        private static void LocalizationIsValid()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.PluginFile(Path.Combine("localization", "English.json"))));
            JsonElement root = document.RootElement;
            TestAssert.Equal("English", root.GetProperty("language").GetString());
            JsonElement features = root.GetProperty("features");
            foreach (string id in new[]
                     {
                         "hold-repeat", "transfer-gesture", "door-auto-close",
                         "equipment-restore", "menu-memory", "text-entry", "pickup-filter"
                     })
                TestAssert.True(features.TryGetProperty(id, out _), "Missing localized feature " + id + ".");
            TestAssert.True(root.GetProperty("disabled_gates").TryGetProperty("drag-transfer", out _));
            TestAssert.True(root.GetProperty("disabled_gates").TryGetProperty("filter-memory", out _));
        }

        private static void ChangelogIsAligned()
        {
            string text = File.ReadAllText(TestPaths.PluginFile("CHANGELOG.md"));
            TestAssert.True(text.Contains("## " + Plugin.Version, StringComparison.Ordinal));
            TestAssert.True(text.Contains(Plugin.Guid, StringComparison.Ordinal));
            TestAssert.True(text.Contains("drag-sweep transfer", StringComparison.OrdinalIgnoreCase));
            TestAssert.True(text.Contains("no world data", StringComparison.OrdinalIgnoreCase));
        }

        private static void IconIsValidPng()
        {
            byte[] bytes = File.ReadAllBytes(TestPaths.PluginFile("icon.png"));
            TestAssert.True(bytes.Length > 1024, "Icon is unexpectedly small.");
            TestAssert.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
            int width = ReadBigEndianInt32(bytes, 16);
            int height = ReadBigEndianInt32(bytes, 20);
            TestAssert.True(width == 256 && height == 256,
                "Thunderstore icon must be exactly 256x256; got " + width + "x" + height + ".");
        }

        private static IReadOnlyDictionary<string, string> ParseConfig(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = null;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int equals = line.IndexOf('=');
                TestAssert.True(equals > 0 && section != null, "Malformed config line: " + line);
                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                TestAssert.True(result.TryAdd(section + "." + key, value),
                    "Duplicate config setting " + section + "." + key + ".");
            }
            return result;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 | bytes[offset + 3];
    }
}
