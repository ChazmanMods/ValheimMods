using System;
using System.IO;
using System.Text.Json;

namespace RunicInventory.Tests
{
    internal static class DocumentationTests
    {
        internal static void Register()
        {
            TestRunner.Run("manifest identity version and dependencies are truthful", ManifestIsTruthful);
            TestRunner.Run("README states native-row capacity and dedicated-client ownership", ReadmeIsTruthful);
            TestRunner.Run("config example documents all exact keyboard and controller defaults", ConfigIsComplete);
            TestRunner.Run("testing guide records installed target and lossless boundaries", TestingGuideIsComplete);
            TestRunner.Run("changelog records compatibility and uninstall behavior", ChangelogIsComplete);
            TestRunner.Run("icon is exactly 256 by 256 pixels", IconIsExactSize);
        }

        private static void ManifestIsTruthful()
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(TestPaths.Module("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicInventory", root.GetProperty("name").GetString());
            TestAssert.Equal("1.0.0", root.GetProperty("version_number").GetString());
            string[] dependencies = new string[root.GetProperty("dependencies").GetArrayLength()];
            int index = 0;
            foreach (JsonElement dependency in root.GetProperty("dependencies").EnumerateArray())
                dependencies[index++] = dependency.GetString();
            TestAssert.Equal("denikson-BepInExPack_Valheim-5.4.2333", dependencies[0]);
            TestAssert.Equal(1, dependencies.Length);
        }

        private static void ReadmeIsTruthful()
        {
            string text = File.ReadAllText(TestPaths.Module("README.md"));
            foreach (string expected in new[]
                     {
                         "native 8×4", "bottom row", "five equipment", "three quick",
                         "does not add carrying capacity", "abrupt uninstall", "full item metadata",
                         "InventoryIntegrationApi", "optional reflection", "short-lived mutation scope",
                         "subtly outlined", "There is no gameplay HUD", "Picking up or crafting",
                         "swaps the previous occupant", "no Foundation runtime dependency"
                     })
                TestAssert.Contains(text, expected);
            TestAssert.False(text.Contains("durable journal", StringComparison.OrdinalIgnoreCase));
        }

        private static void ConfigIsComplete()
        {
            string text = File.ReadAllText(TestPaths.Module("RunicInventory.cfg.example"));
            foreach (string expected in new[]
                     {
                         "UseQuickSlot1 = Alpha1 + LeftAlt", "UseQuickSlot2 = Alpha2 + LeftAlt",
                         "UseQuickSlot3 = Alpha3 + LeftAlt", "SortSelectedRows = I + LeftAlt",
                         "ToggleFocusedSlotLock = L + LeftAlt", "ModifierAction = JoyAltKeys",
                         "UseQuickSlot1Action = JoyMap", "UseQuickSlot2Action = JoyButtonY",
                         "UseQuickSlot3Action = JoyRBumper", "SortSelectedRowsAction = JoyButtonA",
                         "ToggleFocusedSlotLockAction = JoyButtonB"
                     })
                TestAssert.Contains(text, expected);
            TestAssert.Contains(text, "ShowRoleLabels = true");
            TestAssert.Contains(text, "ShowInventoryStatus = false");
        }

        private static void TestingGuideIsComplete()
        {
            string text = File.ReadAllText(TestPaths.Module("TESTING.md"));
            foreach (string expected in new[]
                     {
                         "0.221.12", "death", "tombstone", "logout", "disable", "uninstall",
                         "dedicated", "no item loss", "independently installed", "optional reflection API",
                         "Foundation DLLs absent"
                     })
                TestAssert.Contains(text, expected);
        }

        private static void ChangelogIsComplete()
        {
            string text = File.ReadAllText(TestPaths.Module("CHANGELOG.md"));
            TestAssert.Contains(text, "1.0.0");
            TestAssert.Contains(text, "native bottom row");
            TestAssert.Contains(text, "remote dedicated");
            TestAssert.Contains(text, "migration-safe");
            foreach (string expected in new[]
                     {
                         "standalone", "Foundation runtime dependencies", "short-lived per-mod mutation scope",
                         "optional reflection API", "native owner-local"
                     })
                TestAssert.Contains(text, expected);
        }

        private static void IconIsExactSize()
        {
            byte[] bytes = File.ReadAllBytes(TestPaths.Module("icon.png"));
            TestAssert.True(bytes.Length >= 24, "PNG is too short.");
            TestAssert.Equal((byte)0x89, bytes[0]);
            TestAssert.Equal((byte)'P', bytes[1]);
            TestAssert.Equal((byte)'N', bytes[2]);
            TestAssert.Equal((byte)'G', bytes[3]);
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            TestAssert.Equal(256, width);
            TestAssert.Equal(256, height);
        }
    }
}
