using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using RunicInteraction.Core;
using RunicInteraction.Integration;

namespace RunicInteraction.Tests
{
    internal static class PluginContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin identity and assembly version are exact", IdentityIsExact);
            TestRunner.Run("plugin has no hard Runic runtime dependency", RuntimeIsStandalone);
            TestRunner.Run("project embeds no Foundation implementation", ProjectIsStandalone);
            TestRunner.Run("startup audits installed targets before patching", StartupAuditsBeforePatching);
            TestRunner.Run("controller config refreshes only the local bounded catalog", BindingRefreshIsLocal);
            TestRunner.Run("shutdown unpatches and clears local state", ShutdownIsComplete);
            TestRunner.Run("retired capability and door transport files are absent", RetiredFilesAreAbsent);
        }

        private static void IdentityIsExact()
        {
            BepInPlugin identity = TestAssert.NotNull(
                typeof(Plugin).GetCustomAttribute<BepInPlugin>(),
                "BepInPlugin attribute is missing.");
            TestAssert.Equal("chazman.RunicInteraction", Plugin.Guid);
            TestAssert.Equal("Runic Interaction", Plugin.Name);
            TestAssert.Equal("1.0.0", Plugin.Version);
            TestAssert.Equal("runic.interaction", Plugin.ModuleId);
            TestAssert.Equal(Plugin.Guid, identity.GUID);
            TestAssert.Equal(Plugin.Name, identity.Name);
            TestAssert.Equal(Plugin.Version, identity.Version.ToString());
            TestAssert.Equal(new Version(1, 0, 0, 0),
                typeof(Plugin).Assembly.GetName().Version);
            TestAssert.Equal(
                Plugin.Version,
                typeof(Plugin).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        }

        private static void RuntimeIsStandalone()
        {
            TestAssert.Equal(0,
                typeof(Plugin).GetCustomAttributes<BepInDependency>().Count());
            string[] references = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(value => value.Name)
                .ToArray();
            foreach (string forbidden in new[]
                     {
                         "RunicCore", "RunicPersistence", "RunicPermissions",
                         "RunicTransactions", "RunicStorage", "RunicCrafting",
                         "RunicProduction", "RunicAgriculture"
                     })
                TestAssert.False(
                    references.Contains(forbidden, StringComparer.OrdinalIgnoreCase),
                    "Unexpected runtime reference: " + forbidden);
        }

        private static void ProjectIsStandalone()
        {
            string project = File.ReadAllText(TestPaths.PluginFile(
                "RunicInteraction.csproj"));
            foreach (string forbidden in new[]
                     {
                         "ProjectReference", "RunicCore", "RunicPersistence",
                         "RunicPermissions", "RunicTransactions"
                     })
                TestAssert.False(project.Contains(
                    forbidden, StringComparison.Ordinal));
        }

        private static void StartupAuditsBeforePatching()
        {
            MethodInfo awake = Method(typeof(Plugin), "Awake");
            IReadOnlyList<MethodBase> calls = IlReader.Calls(awake);
            int audit = IlReader.CallIndex(
                calls, typeof(ValheimAccess), "Initialize");
            int patch = IlReader.CallIndex(
                calls, typeof(Harmony), nameof(Harmony.PatchAll));
            TestAssert.True(audit >= 0 && patch > audit,
                "Startup must verify exact Valheim signatures before patching.");
        }

        private static void BindingRefreshIsLocal()
        {
            MethodInfo changed = Method(typeof(Plugin), "OnConfigurationChanged");
            MethodInfo refresh = Method(typeof(Plugin), "RefreshKeybindings");
            TestAssert.True(IlReader.Calls(
                changed, typeof(Plugin), "RefreshKeybindings"));
            TestAssert.True(IlReader.Calls(
                refresh, typeof(Plugin), "ReplaceKeybindings"));
            FieldInfo field = typeof(Plugin).GetField(
                "_keybindings", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.NotNull(field, "Local keybinding catalog is missing.");
            TestAssert.Equal(typeof(List<KeybindingDescriptor>), field.FieldType);
            TestAssert.Equal(InteractionInputBindings.BindingCount,
                InteractionInputBindings.CreateDescriptors(
                    InteractionInputBindings.DefaultPickupBypassControllerModifier).Count);
        }

        private static void ShutdownIsComplete()
        {
            MethodInfo shutdown = Method(typeof(Plugin), "ShutdownRuntime");
            TestAssert.True(IlReader.Calls(
                shutdown, typeof(Harmony), nameof(Harmony.UnpatchSelf)));
            TestAssert.True(IlReader.Calls(
                shutdown, typeof(InteractionRuntime), "Shutdown"));
            TestAssert.True(IlReader.Calls(
                shutdown, typeof(Plugin), "DisposeKeybindings"));
        }

        private static void RetiredFilesAreAbsent()
        {
            string root = Path.Combine(
                TestPaths.RepositoryRoot, "RunicInteraction");
            foreach (string name in new[]
                     {
                         "InteractionCapabilities.cs", "DoorRemoteProtocol.cs",
                         "DoorAuthorityRuntime.cs", "AuthoritativeDoorWardZdoIndex.cs"
                     })
                TestAssert.False(
                    Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).Any(),
                    "Retired file remains: " + name);
        }

        private static MethodInfo Method(Type type, string name) =>
            TestAssert.NotNull(type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic),
                type.FullName + "." + name + " is missing.");
    }
}
