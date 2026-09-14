using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BepInEx;
using HarmonyLib;
using RunicExploration.Integration;
using UnityEngine;

namespace RunicExploration.Tests
{
    internal static class PluginAndDocumentationTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin identity and assembly version are exact", IdentityIsExact);
            TestRunner.Run("plugin has no hard Runic runtime dependency", RuntimeIsStandalone);
            TestRunner.Run("optional peers are not assembly references", OptionalPeersAreNotReferences);
            TestRunner.Run("project has no Runic project reference", ProjectIsStandalone);
            TestRunner.Run("dedicated process gates all Harmony patching", BatchGatePrecedesPatch);
            TestRunner.Run("plugin exposes no registry capability or protocol surface", NoSharedProtocolSurface);
            TestRunner.Run("manifest identity dependencies and description align", ManifestAligns);
            TestRunner.Run("example configuration exposes every public control", ConfigIsComplete);
            TestRunner.Run("documentation states privacy truth and limits", DocumentationIsTruthful);
            TestRunner.Run("icon is an exact 256 by 256 PNG", IconIsExact);
        }

        private static void IdentityIsExact()
        {
            BepInPlugin identity = TestAssert.NotNull(
                typeof(Plugin).GetCustomAttribute<BepInPlugin>());
            TestAssert.Equal("chazman.RunicExploration", Plugin.Guid);
            TestAssert.Equal("Runic Exploration", Plugin.Name);
            TestAssert.Equal("1.0.2", Plugin.Version);
            TestAssert.Equal(Plugin.Guid, identity.GUID);
            TestAssert.Equal(Plugin.Name, identity.Name);
            TestAssert.Equal(Plugin.Version, identity.Version.ToString());
            TestAssert.Equal(new System.Version(1, 0, 2, 0),
                typeof(Plugin).Assembly.GetName().Version);
            TestAssert.Equal("1.0.2", typeof(Plugin).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion);
        }

        private static void RuntimeIsStandalone()
        {
            BepInDependency[] dependencies = typeof(Plugin)
                .GetCustomAttributes<BepInDependency>().ToArray();
            TestAssert.Equal(0, dependencies.Length);
        }

        private static void OptionalPeersAreNotReferences()
        {
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(name => name.Name).ToArray();
            foreach (string optional in new[]
                     {
                         "RunicCore", "RunicPersistence", "RunicPermissions",
                         "RunicTransactions", "RunicPortals", "RunicAwareness"
                     })
                TestAssert.False(references.Contains(optional, StringComparer.OrdinalIgnoreCase));
            string manifest = File.ReadAllText(TestPaths.Plugin("manifest.json"));
            TestAssert.DoesNotContain("RunicPortals", manifest);
            TestAssert.DoesNotContain("RunicAwareness", manifest);
        }

        private static void ProjectIsStandalone()
        {
            string project = File.ReadAllText(TestPaths.Plugin("RunicExploration.csproj"));
            TestAssert.Equal(0, Count(project, "<ProjectReference"));
            TestAssert.DoesNotContain("RunicCore", project);
            TestAssert.DoesNotContain("RunicPortals.csproj", project);
            TestAssert.DoesNotContain("RunicAwareness.csproj", project);
        }

        private static void BatchGatePrecedesPatch()
        {
            MethodInfo awake = typeof(Plugin).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            int batch = IlReader.FirstCallOffset(awake, typeof(Application), "get_isBatchMode");
            int patch = IlReader.FirstCallOffset(awake, typeof(Harmony), nameof(Harmony.PatchAll));
            TestAssert.True(batch >= 0 && patch > batch);
            string source = File.ReadAllText(TestPaths.Plugin("Plugin.cs"));
            TestAssert.Contains("if (!Application.isBatchMode)", source);
            TestAssert.Contains("intentionally inert and unpatched", source);
            TestAssert.True(IlReader.Calls(awake, typeof(ValheimContracts),
                nameof(ValheimContracts.VerifyInstalledSignatures)));
        }

        private static void NoSharedProtocolSurface()
        {
            string source = File.ReadAllText(TestPaths.Plugin("Plugin.cs"));
            foreach (string forbidden in new[]
                     {
                         "RegisterModule", "RunicRegistry", "KnownWorldCapability",
                         "ProtocolVersion", "ModuleId"
                     })
                TestAssert.DoesNotContain(forbidden, source);
        }

        private static void ManifestAligns()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(TestPaths.Plugin("manifest.json")));
            JsonElement root = document.RootElement;
            TestAssert.Equal("RunicExploration", root.GetProperty("name").GetString());
            TestAssert.Equal("1.0.2", root.GetProperty("version_number").GetString());
            string description = root.GetProperty("description").GetString();
            TestAssert.Contains("explored world", description);
            string[] dependencies = root.GetProperty("dependencies").EnumerateArray()
                .Select(item => item.GetString()).ToArray();
            TestAssert.Equal(1, dependencies.Length);
            TestAssert.True(dependencies.Contains("denikson-BepInExPack_Valheim-5.4.2350"));
        }

        private static void ConfigIsComplete()
        {
            string config = File.ReadAllText(TestPaths.Plugin("RunicExploration.cfg.example"));
            foreach (string key in new[]
                     {
                         "Enabled", "KnownPinBrowser", "SelectedPinNavigation", "SailingReadout",
                         "IncludeSharedPins", "RefreshIntervalSeconds", "MaximumVisibleResults",
                         "UiScale", "ControllerScaleMultiplier", "PanelSide", "VerboseLogging"
                     })
                TestAssert.Contains(key, config);
        }

        private static void DocumentationIsTruthful()
        {
            string readme = File.ReadAllText(TestPaths.Plugin("README.md"));
            foreach (string term in new[]
                     {
                         "LAST KNOWN", "LIVE LOCAL SHIP", "10,000",
                         "4,096", "24", "256", "64", "48", "no-map", "Dedicated/batch",
                          "WorldGenerator", "ZoneSystem", "no remote recovery", "runtime-discovered",
                          "does not add a controller cursor", "unknown or unsaved pin", "Authority",
                          "multiplayer", "server authority", "sends no multiplayer RPC",
                          "never a world or server mutation"
                     })
                TestAssert.Contains(term, readme);
            TestAssert.Contains("Runic Portals and Runic Awareness are not", readme);
            TestAssert.Contains("does not query directory", readme);
            string testing = File.ReadAllText(TestPaths.Plugin("TESTING.md"));
            TestAssert.Contains("100/1,000/10,000", testing);
            TestAssert.Contains("unknown pins are rejected before name/type/owner reads", testing);
            string change = File.ReadAllText(TestPaths.Plugin("CHANGELOG.md"));
            TestAssert.Contains("1.0.2", change);
            TestAssert.Contains("world-location scans", change);
        }

        private static void IconIsExact()
        {
            byte[] png = File.ReadAllBytes(TestPaths.Plugin("icon.png"));
            TestAssert.True(png.Length > 24);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            TestAssert.True(png.Take(8).SequenceEqual(signature));
            TestAssert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
            TestAssert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            TestAssert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }

        private static int Count(string value, string needle)
        {
            int count = 0;
            for (int offset = 0; (offset = value.IndexOf(needle, offset,
                     StringComparison.Ordinal)) >= 0; offset += needle.Length) count++;
            return count;
        }
    }
}
