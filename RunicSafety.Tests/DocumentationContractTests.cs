using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace RunicSafety.Tests
{
    internal static class DocumentationContractTests
    {
        private static readonly IReadOnlyDictionary<string, string> ExpectedSettings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["General.Enabled"] = "true",
                ["Confirmations.RareItemSacrifice"] = "true",
                ["Confirmations.OccupiedContainerDestruction"] = "true",
                ["Confirmations.ShipOrCartDestruction"] = "true",
                ["Confirmations.PortalOverwrite"] = "true",
                ["Confirmations.RepeatWindowSeconds"] = "4",
                ["Protected Items.Enabled"] = "true",
                ["Protected Items.AdministratorBypass"] = "false",
                ["Protected Items.RarePrefabNames"] =
                    "DragonEgg,DvergrKey,DvergrKeyFragment,QueenDrop,Sealbreaker,Wishbone,TrophyTheQueen,TrophySeekerQueen",
                ["Migration Backups.RootDirectory"] = "BepInEx/config/RunicSafety/backups",
                ["Migration Backups.RetentionCount"] = "5",
                ["Migration Backups.MaximumFiles"] = "32",
                ["Migration Backups.MaximumTotalMiB"] = "2048"
            };

        internal static void Register()
        {
            TestRunner.Run("manifest declares only BepInEx", ManifestAligned);
            TestRunner.Run("example config defaults are complete", ConfigComplete);
            TestRunner.Run("README documents every default", ReadmeDefaults);
            TestRunner.Run("README documents standalone recovery limits", ReadmeRecoveryLimits);
            TestRunner.Run("README documents backup failure invariants", ReadmeBackupInvariants);
            TestRunner.Run("README documents optional Inventory integration", ReadmeOptionalIntegration);
            TestRunner.Run("localization catalog is valid and complete", LocalizationComplete);
            TestRunner.Run("changelog records Foundation removal", ChangelogAligned);
            TestRunner.Run("Thunderstore icon is exactly 256x256 PNG", IconExact);
            TestRunner.Run("project has no Runic project reference", ProjectIndependent);
            TestRunner.Run("package surface contains required files", PackageSurfaceComplete);
        }

        private static void ManifestAligned()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.Safety("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicSafety", root.GetProperty("name").GetString());
            TestAssert.Equal("1.0.5", root.GetProperty("version_number").GetString());
            TestAssert.SequenceEqual(
                new[] { "denikson-BepInExPack_Valheim-5.4.2350" },
                root.GetProperty("dependencies").EnumerateArray().Select(item => item.GetString()));
        }

        private static void ConfigComplete()
        {
            IReadOnlyDictionary<string, string> actual = ParseConfig(
                File.ReadAllText(TestPaths.Safety("RunicSafety.cfg.example")));
            TestAssert.Equal(ExpectedSettings.Count, actual.Count);
            foreach (KeyValuePair<string, string> expected in ExpectedSettings)
            {
                TestAssert.True(actual.TryGetValue(expected.Key, out string value), expected.Key);
                TestAssert.Equal(expected.Value, value, expected.Key);
            }
        }

        private static void ReadmeDefaults()
        {
            string readme = File.ReadAllText(TestPaths.Safety("README.md"));
            foreach (KeyValuePair<string, string> expected in ExpectedSettings.Where(item =>
                         item.Key != "Protected Items.RarePrefabNames"))
                TestAssert.Contains("`" + expected.Key + " = " + expected.Value + "`", readme,
                    expected.Key);
            TestAssert.Contains("maximum 256 exact prefab IDs", readme);
        }

        private static void ReadmeRecoveryLimits()
        {
            string readme = File.ReadAllText(TestPaths.Safety("README.md"));
            foreach (string phrase in new[]
                     {
                         "does not suppress death", "does not suppress death, move items",
                         "does not create, destroy, or write",
                         "never starts a recovery state machine"
                     }) TestAssert.Contains(phrase, readme);
        }

        private static void ReadmeBackupInvariants()
        {
            string readme = File.ReadAllText(TestPaths.Safety("README.md"));
            foreach (string phrase in new[]
                     {
                         "SHA-256", "atomic same-root directory rename", "per-root single-flight",
                         "failed or cancelled backup never invokes", "Source files are",
                         "auto-restore multi-file saves"
                     }) TestAssert.Contains(phrase, readme);
        }

        private static void ReadmeOptionalIntegration()
        {
            string readme = File.ReadAllText(TestPaths.Safety("README.md"));
            TestAssert.Contains("BepInEx is the only package dependency", readme);
            TestAssert.Contains("discovers its public item-protection seam by reflection", readme);
            TestAssert.Contains("SafetyIntegrationApi", readme);
            TestAssert.Contains("No caller must install Safety", readme);
        }

        private static void LocalizationComplete()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.Safety("localization", "English.json")));
            foreach (string key in new[]
                     {
                         "runicsafety_confirm_container", "runicsafety_confirm_vehicle",
                         "runicsafety_confirm_portal", "runicsafety_confirm_rare",
                         "runicsafety_protected_denied", "runicsafety_provider_missing",
                         "runicsafety_recovery_unsafe"
                     })
                TestAssert.True(document.RootElement.TryGetProperty(key, out JsonElement value) &&
                                !string.IsNullOrWhiteSpace(value.GetString()), key);
        }

        private static void ChangelogAligned()
        {
            string changelog = File.ReadAllText(TestPaths.Safety("CHANGELOG.md"));
            TestAssert.Contains("## 1.0.4", changelog);
            TestAssert.Contains("Removed Runic Core, Persistence, and Transactions", changelog);
            TestAssert.Contains("Reduced the Thunderstore manifest to BepInEx only", changelog);
        }

        private static void IconExact()
        {
            byte[] bytes = File.ReadAllBytes(TestPaths.Safety("icon.png"));
            TestAssert.True(bytes.Length > 1024);
            TestAssert.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
            TestAssert.Equal(256, ReadBigEndian(bytes, 16));
            TestAssert.Equal(256, ReadBigEndian(bytes, 20));
        }

        private static void ProjectIndependent()
        {
            string project = File.ReadAllText(TestPaths.Safety("RunicSafety.csproj"));
            TestAssert.False(project.Contains("ProjectReference", StringComparison.Ordinal));
            foreach (string name in new[]
                     { "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions" })
                TestAssert.False(project.Contains(name, StringComparison.Ordinal), name);
        }

        private static void PackageSurfaceComplete()
        {
            foreach (string file in new[]
                     {
                         "RunicSafety.csproj", "Plugin.cs", "README.md", "CHANGELOG.md",
                         "manifest.json", "icon.png", "RunicSafety.cfg.example"
                     }) TestAssert.True(File.Exists(TestPaths.Safety(file)), file);
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
                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int equals = line.IndexOf('=');
                TestAssert.True(equals > 0 && section != null, line);
                result.Add(
                    section + "." + line.Substring(0, equals).Trim(),
                    line.Substring(equals + 1).Trim());
            }
            return result;
        }

        private static int ReadBigEndian(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 | bytes[offset + 3];
    }
}
