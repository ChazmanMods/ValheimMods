using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using RunicAwareness.Integration;

namespace RunicAwareness.Tests
{
    internal static class PluginContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin identity and version are exact", IdentityIsExact);
            TestRunner.Run("plugin has no hard Runic runtime dependency", RuntimeIsStandalone);
            TestRunner.Run("no Runic gameplay assembly is referenced", PeersAreNotReferenced);
            TestRunner.Run("project has no Runic project reference", ProjectIsStandalone);
            TestRunner.Run(
                "UI suppression contract is exact",
                SuppressionIsExact);
            TestRunner.Run("dedicated batch gate precedes Harmony patching", BatchGatePrecedesPatching);
            TestRunner.Run("language changes invalidate every display capture", LanguageChangesInvalidateDisplay);
        }

        private static void IdentityIsExact()
        {
            BepInPlugin identity = TestAssert.NotNull(
                typeof(Plugin).GetCustomAttribute<BepInPlugin>());
            TestAssert.Equal("chazman.RunicAwareness", Plugin.Guid);
            TestAssert.Equal("Runic Awareness", Plugin.Name);
            TestAssert.Equal("1.0.0", Plugin.Version);
            TestAssert.Equal(Plugin.Guid, identity.GUID);
            TestAssert.Equal(Plugin.Name, identity.Name);
            TestAssert.Equal(Plugin.Version, identity.Version.ToString());
            TestAssert.Equal(new Version(1, 0, 0, 0), typeof(Plugin).Assembly.GetName().Version);
            TestAssert.Equal(
                "1.0.0",
                typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        }

        private static void RuntimeIsStandalone()
        {
            BepInDependency[] dependencies = typeof(Plugin)
                .GetCustomAttributes<BepInDependency>()
                .ToArray();
            TestAssert.Equal(0, dependencies.Length);
        }

        private static void PeersAreNotReferenced()
        {
            string[] actual = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(name => name.Name)
                .ToArray();
            string[] forbidden =
            {
                "RunicCore", "RunicProduction", "RunicAgriculture", "RunicStorage", "RunicCrafting",
                "RunicTransactions", "RunicPermissions", "RunicPersistence", "RunicInteraction",
                "RunicPortals"
            };
            foreach (string peer in forbidden)
                TestAssert.False(actual.Contains(peer, StringComparer.OrdinalIgnoreCase),
                    "Awareness hard-references optional peer " + peer + ".");
        }

        private static void ProjectIsStandalone()
        {
            string project = File.ReadAllText(TestPaths.PluginFile("RunicAwareness.csproj"));
            TestAssert.Equal(0, Count(project, "<ProjectReference"));
            foreach (string forbidden in new[]
                     {
                         "RunicCore", "RunicPersistence", "RunicPermissions",
                         "RunicTransactions", "RunicProduction"
                     })
                TestAssert.DoesNotContain(forbidden, project);
        }

        private static void SuppressionIsExact()
        {
            MethodInfo suppress = typeof(AwarenessRuntime).GetMethod(
                "ShouldSuppressOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach ((Type owner, string method) in new[]
                     {
                         (typeof(Menu), nameof(Menu.IsVisible)),
                         (typeof(Console), nameof(Console.IsVisible)),
                         (typeof(TextInput), nameof(TextInput.IsVisible)),
                         (typeof(StoreGui), nameof(StoreGui.IsVisible)),
                         (typeof(InventoryGui), nameof(InventoryGui.IsVisible)),
                         (typeof(Minimap), nameof(Minimap.IsOpen)),
                         (typeof(Hud), nameof(Hud.IsPieceSelectionVisible)),
                         (typeof(PlayerCustomizaton), nameof(PlayerCustomizaton.IsBarberGuiVisible)),
                         (typeof(Feedback), nameof(Feedback.IsVisible)),
                         (typeof(UnifiedPopup), nameof(UnifiedPopup.IsVisible)),
                         (typeof(ConnectPanel), nameof(ConnectPanel.IsVisible)),
                         (typeof(TextViewer), nameof(TextViewer.IsVisible)),
                         (typeof(Chat), nameof(Chat.HasFocus)),
                         (typeof(Chat), nameof(Chat.IsChatDialogWindowVisible)),
                         (typeof(Game), nameof(Game.IsPaused)),
                         (typeof(OptionalPortalPanelAdapter),
                             nameof(OptionalPortalPanelAdapter.SetupPanelVisible))
                     })
                TestAssert.True(IlReader.Calls(suppress, owner, method), owner.Name + "." + method);
            TestAssert.True(IlReader.Calls(suppress, typeof(ZInput), "get_VirtualKeyboardOpen"));
        }

        private static void BatchGatePrecedesPatching()
        {
            MethodInfo awake = typeof(Plugin).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(awake, typeof(ValheimContracts),
                nameof(ValheimContracts.VerifyInstalledSignatures)));
            TestAssert.True(IlReader.Calls(awake, typeof(HarmonyLib.Harmony),
                nameof(HarmonyLib.Harmony.PatchAll)));
            TestAssert.True(IlReader.AccessesField(awake, typeof(UnityEngine.Application),
                "isBatchMode" ) || File.ReadAllText(TestPaths.PluginFile("Plugin.cs"))
                    .Contains("if (!Application.isBatchMode)", StringComparison.Ordinal));
        }

        private static void LanguageChangesInvalidateDisplay()
        {
            string source = File.ReadAllText(TestPaths.PluginFile("Plugin.cs"));
            TestAssert.Contains("Localization.OnLanguageChange += OnLanguageChange", source);
            TestAssert.Contains("Localization.OnLanguageChange -= OnLanguageChange", source);
            MethodInfo handler = typeof(Plugin).GetMethod(
                "OnLanguageChange", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(handler, typeof(ComfortCapture), nameof(ComfortCapture.Reset)));
            TestAssert.True(IlReader.Calls(handler, typeof(ContextCapture), nameof(ContextCapture.Reset)));
            TestAssert.True(IlReader.Calls(handler, typeof(HoverItemCapture), nameof(HoverItemCapture.Reset)));
            TestAssert.True(IlReader.Calls(
                handler,
                typeof(AwarenessRuntime),
                nameof(AwarenessRuntime.OnLocalizationChanged)));
        }

        private static int Count(string value, string needle)
        {
            int count = 0;
            for (int offset = 0; (offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0;
                 offset += needle.Length)
                count++;
            return count;
        }
    }
}
