using System;
using System.IO;
using RunicInventory.Api;
using RunicInventory.Core;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicInventory.Tests
{
    internal static class ProtectionLifecycleTests
    {
        internal static void Register()
        {
            TestRunner.Run("startup failure yields to vanilla but active-provider failure keeps locks protected", StartupLifecycle);
            TestRunner.Run("role-layout fallback retains independent slot lock queries", RoleFallbackLocks);
            TestRunner.Run("mouse lock targets the clicked cell rather than remembered gamepad focus", ClickedCellWiring);
        }

        private static void StartupLifecycle()
        {
            var item = new ItemDrop.ItemData();
            InventoryIntegrationApi.BeginInitialization();
            TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out int state));
            TestAssert.Equal(0, state);
            InventoryIntegrationApi.MarkStartupFailed();
            TestAssert.False(InventoryIntegrationApi.TryGetProtection(item, out state));
            VerifySafetyDecision(item, ProtectionOutcome.Allow, ProtectionReason.None);
            InventoryIntegrationApi.BeginInitialization();
            TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out state));
            var provider = new FakeQuery();
            InventoryIntegrationApi.Attach(provider);
            try
            {
                TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out state));
                TestAssert.Equal(2, state);
                VerifySafetyDecision(item, ProtectionOutcome.Deny, ProtectionReason.Locked);
                provider.Fail = true;
                TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out state));
                TestAssert.Equal(0, state);
                VerifySafetyDecision(item, ProtectionOutcome.Deny, ProtectionReason.ProviderUnavailable);
            }
            finally { InventoryIntegrationApi.Detach(provider); }
            InventoryIntegrationApi.MarkStartupFailed();
            TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out state));
            TestAssert.Equal(0, state);
        }

        private static void VerifySafetyDecision(ItemDrop.ItemData item, ProtectionOutcome outcome, ProtectionReason reason)
        {
            bool governed = InventoryIntegrationApi.TryGetProtection(item, out int state);
            ItemLockState lockState = !governed ? ItemLockState.NotApplicable :
                state == 1 ? ItemLockState.Unlocked : state == 2 ? ItemLockState.Locked : ItemLockState.Unknown;
            var policy = new ProtectedItemPolicy(new CorrelatedDiagnosticBuffer(), () => false, () => false);
            foreach (ProtectionDestination destination in new[] { ProtectionDestination.CookingStation, ProtectionDestination.CookingFuel, ProtectionDestination.SmelterFuel })
            {
                var request = new ItemProtectionRequest(
                    new ProtectedItemDescriptor("RawMeat", false, false, lockState, false),
                    destination, "regression", false, true, item);
                ItemProtectionDecision decision = policy.Evaluate(request);
                TestAssert.Equal(outcome, decision.Outcome);
                TestAssert.Equal(reason, decision.Reason);
            }
        }

        private static void RoleFallbackLocks()
        {
            foreach (string reason in new[] { "topology.clear-incompatible-special-row", "topology.equipped-item-outside-role" })
            {
                TestAssert.True(ItemProtectionAvailabilityPolicy.SupportsIndependentLocks(
                    InventoryAuthorityMode.MigrationSafeCompatibility, reason));
                TestAssert.Equal(ItemProtectionAvailability.EvaluateItem,
                    ItemProtectionAvailabilityPolicy.Classify(true, InventoryAuthorityMode.MigrationSafeCompatibility, reason, false, false));
                TestAssert.Equal(ItemProtectionAvailability.Unknown,
                    ItemProtectionAvailabilityPolicy.Classify(true, InventoryAuthorityMode.MigrationSafeCompatibility, reason, false, true));
            }
            TestAssert.False(ItemProtectionAvailabilityPolicy.SupportsIndependentLocks(
                InventoryAuthorityMode.MigrationSafeCompatibility, "persistence.digest-mismatch"));
            TestAssert.False(ItemProtectionAvailabilityPolicy.SupportsIndependentLocks(
                InventoryAuthorityMode.RemoteDedicatedCompatibility, "topology.clear-incompatible-special-row"));
            TestAssert.Equal(ItemProtectionState.Unlocked, ItemProtectionDomain.ClassifyExactMember(false, false));
            TestAssert.Equal(ItemProtectionState.Locked, ItemProtectionDomain.ClassifyExactMember(false, true));
        }

        private static void ClickedCellWiring()
        {
            string runtime = File.ReadAllText(TestPaths.Module("Integration", "InventoryRuntime.cs"));
            string patches = File.ReadAllText(TestPaths.Module("Integration", "HarmonyPatches.cs"));
            string contracts = File.ReadAllText(TestPaths.Module("Integration", "ValheimContracts.cs"));
            TestAssert.Contains(patches, "TryTogglePointerLock(__instance, __0)");
            TestAssert.Contains(contracts, "_buttonPosition(grid, clicked.gameObject)");
            int start = runtime.IndexOf("internal bool TryTogglePointerLock", StringComparison.Ordinal);
            int end = runtime.IndexOf("private void ToggleLockAt", start, StringComparison.Ordinal);
            string click = runtime.Substring(start, end - start);
            TestAssert.Contains(click, "RequireLockState()");
            TestAssert.Contains(click, "TryClickedSlot(grid, clicked");
            TestAssert.False(click.Contains("TryFocusedSlot"));
            TestAssert.Contains(runtime, "if (CanEnforceLocks()) DrawLockedSlotOverlay();");
            TestAssert.Contains(runtime, "TopologyActive && itemY == _layout.SpecialRow");
        }

        private sealed class FakeQuery : IItemProtectionQuery
        {
            internal bool Fail;
            public bool TryGetProtection(object item, out ItemProtectionState state)
            {
                if (Fail) throw new InvalidOperationException("Injected active provider failure");
                state = ItemProtectionState.Locked;
                return true;
            }
        }
    }
}
