using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace RunicBuildCamera.Tests
{
    internal static class PluginContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("plugin identity and versions are exact", PluginIdentityAndVersionsAreExact);
            TestRunner.Run("BuildCameraCHE dependency is soft and unique", BuildCameraCheDependencyIsSoft);
            TestRunner.Run("plugin binary has no Runic module references", PluginHasNoRunicModuleReferences);
            TestRunner.Run("project has no Precision or Foundation reference", ProjectHasNoPrecisionOrFoundationReference);
            TestRunner.Run("hard conflict guard recognizes BuildCameraCHE contracts", ConflictGuardRecognizesBuildCameraChe);
            TestRunner.Run("startup checks conflicts before Harmony patching", StartupChecksConflictBeforePatching);
            TestRunner.Run("plugin failure cleanup preserves vanilla paths", PluginFailureCleanupPreservesVanilla);
        }

        private static void PluginIdentityAndVersionsAreExact()
        {
            Type plugin = typeof(Plugin);
            BepInPlugin identity = TestAssert.NotNull(
                plugin.GetCustomAttribute<BepInPlugin>(), "Plugin lacks BepInPlugin identity.");
            TestAssert.Equal("chazman.RunicBuildCamera", Plugin.Guid);
            TestAssert.Equal("Runic Build Camera", Plugin.Name);
            TestAssert.Equal("1.0.0", Plugin.Version);
            TestAssert.Equal(Plugin.Guid, identity.GUID);
            TestAssert.Equal(Plugin.Name, identity.Name);
            TestAssert.Equal(Plugin.Version, identity.Version.ToString());

            AssemblyName assemblyName = plugin.Assembly.GetName();
            TestAssert.Equal("RunicBuildCamera", assemblyName.Name);
            TestAssert.Equal(new Version(1, 0, 0, 0), assemblyName.Version);
            AssemblyInformationalVersionAttribute informational = TestAssert.NotNull(
                plugin.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>(),
                "Assembly informational version is missing.");
            TestAssert.Equal(Plugin.Version, informational.InformationalVersion);
        }

        private static void BuildCameraCheDependencyIsSoft()
        {
            BepInDependency[] dependencies = typeof(Plugin)
                .GetCustomAttributes<BepInDependency>()
                .ToArray();
            TestAssert.Equal(1, dependencies.Length);
            TestAssert.Equal(CompatibilityGuard.BuildCameraCheGuid, dependencies[0].DependencyGUID);
            TestAssert.Equal(
                BepInDependency.DependencyFlags.SoftDependency,
                dependencies[0].Flags);
        }

        private static void PluginHasNoRunicModuleReferences()
        {
            string[] forbidden =
            {
                "RunicPrecisionBuildTool",
                "RunicCore",
                "RunicPermissions",
                "RunicPersistence",
                "RunicTransactions",
                "RunicStorage",
                "RunicCrafting",
                "RunicAgriculture",
                "RunicProduction",
                "RunicIntegrity"
            };
            string[] actual = typeof(Plugin).Assembly.GetReferencedAssemblies()
                .Select(item => item.Name)
                .ToArray();
            foreach (string name in forbidden)
                TestAssert.False(actual.Contains(name, StringComparer.OrdinalIgnoreCase),
                    "Standalone camera hard-references " + name + ".");
        }

        private static void ProjectHasNoPrecisionOrFoundationReference()
        {
            string project = File.ReadAllText(TestPaths.PluginFile("RunicBuildCamera.csproj"));
            string[] forbidden =
            {
                "RunicPrecisionBuildTool",
                "RunicCore",
                "RunicPermissions",
                "RunicPersistence",
                "RunicTransactions",
                "RunicStorage",
                "RunicCrafting",
                "RunicAgriculture",
                "RunicProduction"
            };
            foreach (string value in forbidden)
                TestAssert.False(project.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "Project file mentions forbidden dependency " + value + ".");
            TestAssert.False(project.Contains("<ProjectReference", StringComparison.OrdinalIgnoreCase),
                "Standalone project must not carry any project reference.");
        }

        private static void ConflictGuardRecognizesBuildCameraChe()
        {
            TestAssert.Equal("Azumatt.BuildCameraCHE", CompatibilityGuard.BuildCameraCheGuid);
            MethodInfo guard = typeof(CompatibilityGuard).GetMethod(
                "TryFindHardConflict",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.NotNull(guard, "Compatibility guard entry point is missing.");
            TestAssert.True(IlReader.LoadsString(guard, "BuildCameraCHE"));
            TestAssert.True(IlReader.LoadsString(
                guard, "Build Camera Custom Hammers Edition"));
            TestAssert.True(IlReader.LoadsString(
                guard, "Valheim_Build_Camera.Valheim_Build_CameraPlugin"));
            TestAssert.False(
                IlReader.Calls(guard, typeof(AccessTools), nameof(AccessTools.TypeByName)),
                "Normal missing-conflict checks must not use the noisy global AccessTools type probe.");
            TestAssert.True(
                IlReader.Calls(guard, typeof(AppDomain), "GetAssemblies"),
                "Renamed Build Camera assemblies are not detected from the already-loaded set.");
        }

        private static void StartupChecksConflictBeforePatching()
        {
            MethodInfo awake = typeof(Plugin).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(awake);
            int conflict = IlReader.CallIndex(
                calls, typeof(CompatibilityGuard), "TryFindHardConflict");
            int adapter = IlReader.CallIndex(calls, typeof(Integration.ValheimAdapter), "Initialize");
            int patchAll = IlReader.CallIndex(calls, typeof(Harmony), nameof(Harmony.PatchAll));
            int runtime = IlReader.CallIndex(
                calls, typeof(Integration.BuildCameraRuntime), "Initialize");
            TestAssert.True(conflict >= 0 && adapter > conflict && patchAll > adapter && runtime > patchAll,
                "Startup order must be conflict guard -> adapter verification -> patch -> runtime.");
        }

        private static void PluginFailureCleanupPreservesVanilla()
        {
            MethodInfo disable = typeof(Plugin).GetMethod(
                "DisableRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.True(IlReader.ReferencesMethod(
                disable, typeof(Integration.RemotePickupRuntime), "Shutdown"),
                "Remote pickup cleanup is not independently scheduled.");
            TestAssert.True(IlReader.ReferencesMethod(
                disable, typeof(Integration.DemisterRuntime), "Shutdown"),
                "Demister cleanup is not independently scheduled.");
            TestAssert.True(IlReader.ReferencesMethod(
                disable, typeof(Integration.BuildCameraRuntime), "Shutdown"),
                "Camera cleanup is not independently scheduled.");
            TestAssert.True(IlReader.ReferencesMethod(
                disable, typeof(Integration.ValheimAdapter), "Shutdown"),
                "Range restoration cleanup is not independently scheduled.");
            IReadOnlyList<MethodBase> calls = IlReader.Calls(disable);
            TestAssert.True(calls.Count(method =>
                    method.DeclaringType == typeof(Plugin) && method.Name == "TryCleanup") >= 4,
                "Cleanup subsystems are no longer isolated from one another.");
            TestAssert.True(IlReader.CallIndex(calls, typeof(Harmony), nameof(Harmony.UnpatchSelf)) >= 0,
                "Plugin cleanup does not unpatch its own Harmony owner.");

            MethodInfo update = typeof(Plugin).GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                update, typeof(Integration.BuildCameraRuntime), "ForceStop"));
            TestAssert.True(IlReader.Calls(
                update, typeof(Integration.RemotePickupRuntime), "Reset"));
            TestAssert.True(IlReader.Calls(
                update, typeof(Integration.DemisterRuntime), "OnCameraExit"));
        }
    }
}
