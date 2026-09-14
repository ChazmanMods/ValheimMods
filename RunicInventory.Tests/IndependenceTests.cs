using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using RunicInventory.Api;
using RunicInventory.Core;
using RunicInventory.Integration;

namespace RunicInventory.Tests
{
    internal static class IndependenceTests
    {
        internal static void Register()
        {
            TestRunner.Run("Inventory DLL has no Foundation runtime references", NoFoundationReferences);
            TestRunner.Run("Inventory owns one short-lived mutation scope", LocalMutationScopeIsBounded);
            TestRunner.Run("Inventory optional protection seam is exact", OptionalProtectionApiIsExact);
            TestRunner.Run("Inventory role UI and pointer lock use exact native elements", NativeRoleUiIsExact);
            TestRunner.Run("Inventory overlays yield to modals and repair scope", OverlayAndRepairScopeAreExact);
            TestRunner.Run("Inventory durable framework files are removed", DurableFilesAreRemoved);
        }

        private static void NoFoundationReferences()
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(TestPaths.BuiltPlugin);
            string[] forbidden =
                { "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions" };
            TestAssert.False(assembly.MainModule.AssemblyReferences.Any(reference =>
                forbidden.Contains(reference.Name, StringComparer.Ordinal)));
        }

        private static void LocalMutationScopeIsBounded()
        {
            string source = File.ReadAllText(TestPaths.Module("Integration", "InventoryRuntime.cs"));
            TestAssert.Contains(source, "Interlocked.CompareExchange(ref _mutationActive, 1, 0)");
            TestAssert.Contains(source, "Interlocked.Exchange(ref owner._mutationActive, 0)");
            TestAssert.False(source.Contains("RunicMutationGate", StringComparison.Ordinal));
            TestAssert.False(source.Contains("Journal", StringComparison.Ordinal));
        }

        private static void OptionalProtectionApiIsExact()
        {
            MethodInfo method = typeof(InventoryIntegrationApi).GetMethod(
                nameof(InventoryIntegrationApi.TryGetProtection),
                BindingFlags.Public | BindingFlags.Static);
            TestAssert.True(method != null);
            ParameterInfo[] parameters = method.GetParameters();
            TestAssert.Equal(2, parameters.Length);
            TestAssert.Equal(typeof(object), parameters[0].ParameterType);
            TestAssert.Equal(typeof(int).MakeByRefType(), parameters[1].ParameterType);
            TestAssert.True(parameters[1].IsOut);
            TestAssert.False(InventoryIntegrationApi.TryGetProtection(new object(), out int state));
            TestAssert.Equal(0, state);

            MethodInfo runtimeQuery = typeof(InventoryRuntime).GetMethod(
                nameof(InventoryRuntime.TryGetProtection),
                BindingFlags.Public | BindingFlags.Instance);
            TestAssert.True(runtimeQuery != null);
            TestAssert.True(IlReader.Calls(
                runtimeQuery,
                typeof(ItemProtectionAvailabilityPolicy),
                nameof(ItemProtectionAvailabilityPolicy.Classify)));
        }

        private static void DurableFilesAreRemoved()
        {
            foreach (string relative in new[]
                     {
                         "Core/InventoryDurableJournal.cs",
                         "Core/InventoryDurableRecoveryBridge.cs",
                         "Integration/InventoryDurableOperationRuntime.cs",
                         "Integration/InventoryDurableOperationPatches.cs"
                     })
                TestAssert.False(File.Exists(TestPaths.Module(relative.Split('/'))));
        }

        private static void NativeRoleUiIsExact()
        {
            string runtime = File.ReadAllText(
                TestPaths.Module("Integration", "InventoryRuntime.cs"));
            string contracts = File.ReadAllText(
                TestPaths.Module("Integration", "ValheimContracts.cs"));
            string patches = File.ReadAllText(
                TestPaths.Module("Integration", "HarmonyPatches.cs"));
            TestAssert.Contains(runtime, "TryTogglePointerLock");
            TestAssert.Contains(runtime, "KeyCode.LeftAlt");
            TestAssert.Contains(runtime, "SameItemReferences(items, refreshedItems)");
            TestAssert.Contains(contracts, "GetElement");
            TestAssert.Contains(patches, "InventoryGridRightClickLockPatch");
            TestAssert.Contains(patches, "OnRightDown");
        }

        private static void OverlayAndRepairScopeAreExact()
        {
            string runtime = File.ReadAllText(
                TestPaths.Module("Integration", "InventoryRuntime.cs"));
            string contracts = File.ReadAllText(
                TestPaths.Module("Integration", "ValheimContracts.cs"));
            string patches = File.ReadAllText(
                TestPaths.Module("Integration", "HarmonyPatches.cs"));
            TestAssert.Contains(runtime, "ValheimContracts.InventoryModalVisible()");
            TestAssert.Contains(contracts, "m_splitDialog");
            TestAssert.Contains(contracts, "m_variantDialog");
            TestAssert.Contains(runtime, "DrawLockedSlotOverlay");
            TestAssert.Contains(runtime, "new Color(1f, 0.84f, 0.08f, 1f)");
            TestAssert.Contains(runtime, "_repairAllowanceDepth > 0");
            TestAssert.Contains(patches, "InventoryRepairAllowancePatch");
            TestAssert.Contains(patches, "BeginRepairAllowance");
            TestAssert.Contains(patches, "EndRepairAllowance");
            TestAssert.False(runtime.Contains(
                "wasLocked ? \"unlocked", StringComparison.Ordinal),
                "Successful lock toggles still emit a center-screen message.");
        }
    }
}
