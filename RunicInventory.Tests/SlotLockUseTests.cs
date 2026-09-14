using System;
using System.IO;
using System.Reflection;
using RunicInventory.Api;
using RunicInventory.Core;
using RunicSafety.Api;
using RunicSafety.Services;
using RunicStorage.Engine;

namespace RunicInventory.Tests
{
    internal static class SlotLockUseTests
    {
        internal static void Register()
        {
            TestRunner.Run("slot retention permits native use but not transfers or disposal", UseAllowlist);
            TestRunner.Run("use protection preserves transfer locks and uncertain provider states", SeparateProtectionViews);
            TestRunner.Run("locked consumables pass Safety while quest equipped and disposal protections remain", SafetyDecisions);
            TestRunner.Run("native use and stack replenishment are not intercepted by retention locks", NativeUseWiring);
            TestRunner.Run("Storage's actual adapter still retains locked ammunition after a use query", StorageStillRetains);
            TestRunner.Run("native ammunition consumption uses the original inventory removal path", NativeAmmoContract);
        }

        private static void UseAllowlist()
        {
            foreach (string action in new[] { "using it", "cooking it", "processing it", "fermenting it" })
                TestAssert.True(SlotLockUsePolicy.AllowsUse(action), action);
            foreach (string action in new[] { null, "", "dropping it", "displaying it", "incinerating it", "sacrificing it", "moving it" })
                TestAssert.False(SlotLockUsePolicy.AllowsUse(action), action);
        }

        private static void SeparateProtectionViews()
        {
            var provider = new Query();
            var item = new ItemDrop.ItemData();
            InventoryIntegrationApi.Attach(provider);
            try
            {
                foreach (ItemProtectionState state in Enum.GetValues(typeof(ItemProtectionState)))
                {
                    provider.State = state;
                    TestAssert.True(InventoryIntegrationApi.TryGetProtection(item, out int transfer));
                    TestAssert.Equal((int)state, transfer);
                    TestAssert.True(InventoryIntegrationApi.TryGetUseProtection(item, out int use));
                    TestAssert.Equal(state == ItemProtectionState.Locked ? 1 : (int)state, use);
                }
                provider.Governed = false;
                TestAssert.False(InventoryIntegrationApi.TryGetUseProtection(item, out _));
                provider.Fail = true;
                TestAssert.True(InventoryIntegrationApi.TryGetUseProtection(item, out int unknown));
                TestAssert.Equal(0, unknown);
                TestAssert.False(InventoryIntegrationApi.TryGetUseProtection(new object(), out _));
                TestAssert.False(InventoryIntegrationApi.TryGetUseProtection(null, out _));
            }
            finally { InventoryIntegrationApi.Detach(provider); }
        }

        private static void SafetyDecisions()
        {
            var provider = new Query();
            var item = new ItemDrop.ItemData();
            var policy = new ProtectedItemPolicy(new CorrelatedDiagnosticBuffer(), () => true, () => false);
            InventoryIntegrationApi.Attach(provider);
            try
            {
                InventoryIntegrationApi.TryGetUseProtection(item, out int use);
                InventoryIntegrationApi.TryGetProtection(item, out int transfer);
                foreach (ProtectionDestination destination in new[] { ProtectionDestination.SmelterInput, ProtectionDestination.SmelterFuel,
                    ProtectionDestination.CookingStation, ProtectionDestination.CookingFuel, ProtectionDestination.Fermenter })
                {
                    ItemProtectionDecision Evaluate(int state, bool equipped = false, bool quest = false, bool rare = false) =>
                        policy.Evaluate(new ItemProtectionRequest(new ProtectedItemDescriptor("item", equipped, quest,
                            state == 1 ? ItemLockState.Unlocked : state == 2 ? ItemLockState.Locked : ItemLockState.Unknown,
                            rare), destination, "slot-use", false, true, item));
                    TestAssert.Equal(ProtectionOutcome.Allow, Evaluate(use).Outcome);
                    TestAssert.Equal(ProtectionReason.Locked, Evaluate(transfer).Reason);
                    TestAssert.Equal(ProtectionReason.Equipped, Evaluate(use, equipped: true).Reason);
                    TestAssert.Equal(ProtectionReason.QuestItem, Evaluate(use, quest: true).Reason);
                    TestAssert.Equal(ProtectionOutcome.RequireConfirmation, Evaluate(use, rare: true).Outcome);
                    TestAssert.Equal(ProtectionReason.ProviderUnavailable, Evaluate(0).Reason);
                }
            }
            finally { InventoryIntegrationApi.Detach(provider); }
        }

        private static void NativeUseWiring()
        {
            string runtime = File.ReadAllText(TestPaths.Module("Integration", "InventoryRuntime.cs"));
            string patches = File.ReadAllText(TestPaths.Module("Integration", "HarmonyPatches.cs"));
            TestAssert.Contains(runtime, "if (SlotLockUsePolicy.AllowsUse(action)) return true;");
            TestAssert.Contains(patches, "AllowItemAction(__instance, __0, __1, \"using it\")");
            TestAssert.Contains(patches, "AllowItemAction(__instance, __0, __1, \"dropping it\")");
            TestAssert.False(patches.Contains("class FindFreeStackPatch"));
            TestAssert.False(runtime.Contains("ReplaceLockedFreeStack"));
            TestAssert.False(runtime.Contains("capacity > 0 && (!enforceLocks"));
            int start = runtime.IndexOf("internal void UseQuick", StringComparison.Ordinal);
            TestAssert.True(start >= 0);
            int end = runtime.IndexOf("\n        private ", start + 1, StringComparison.Ordinal);
            string quick = runtime.Substring(start, end - start);
            TestAssert.False(quick.Contains("IsLocked("));
            TestAssert.Contains(quick, "UseItem(");
        }

        private static void StorageStillRetains()
        {
            var provider = new Query();
            var arrows = new ItemDrop.ItemData();
            InventoryIntegrationApi.Attach(provider);
            try
            {
                TestAssert.True(InventoryIntegrationApi.TryGetUseProtection(arrows, out int use));
                TestAssert.Equal(1, use);
                TestAssert.True(StorageItemProtection.TryCapture(new[] { arrows }, out StorageProtectionSnapshot snapshot, out string reason), reason);
                TestAssert.Equal(StorageProtectionState.Locked, snapshot.StateAt(0));
                provider.State = ItemProtectionState.Unlocked;
                TestAssert.True(StorageItemProtection.TryCapture(new[] { arrows }, out snapshot, out reason), reason);
                TestAssert.Equal(StorageProtectionState.Unlocked, snapshot.StateAt(0));
                provider.Fail = true;
                TestAssert.False(StorageItemProtection.TryCapture(new[] { arrows }, out _, out _));
            }
            finally { InventoryIntegrationApi.Detach(provider); }
        }

        private static void NativeAmmoContract()
        {
            MethodInfo method = typeof(Attack).GetMethod("UseAmmo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(ItemDrop.ItemData).MakeByRefType() }, null);
            TestAssert.NotNull(method);
            TestAssert.True(IlReader.Calls(method, typeof(Inventory), "RemoveItem"));
        }

        private sealed class Query : IItemProtectionQuery
        {
            internal ItemProtectionState State = ItemProtectionState.Locked;
            internal bool Governed = true;
            internal bool Fail;
            public bool TryGetProtection(object item, out ItemProtectionState state)
            {
                if (Fail) throw new InvalidOperationException("injected");
                state = State;
                return Governed;
            }
        }
    }
}
