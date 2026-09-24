using System;
using System.Collections.Generic;
using RunicAutomation;
using RunicCrafting.Domain;
using RunicCrafting.Integration;

namespace RunicCrafting.Tests
{
    internal static class InventorySafetyTests
    {
        internal static void CrossModuleGateRejectsNestedCraft()
        {
            MutationGate.EndSession();
            TestAssert.True(MutationGate.TryBegin("storage", out var outer));
            using (outer)
            {
                TestAssert.False(MutationGate.TryBegin("production", out _));
                var engine = new ExactMaterialTransactionEngine();
                TestAssert.False(engine.TryBegin(new[] { new MaterialRequirement("wood", 1) },
                    new[] { Source("wood") }, out _, out string reason));
                TestAssert.Equal("mutation-busy", reason);
                TestAssert.True(outer.Id == MutationGate.Current.Id);
            }
            TestAssert.True(MutationGate.TryBegin("production", out var next)); next.Dispose();
        }
        private static FakeMaterialSource Source(string id) => new FakeMaterialSource(id,
            MaterialSourceKind.PlayerInventory, 0, new Dictionary<string, int> { ["wood"] = 1 });

        internal static void FailedRollbackBlocksReuseAndReleasesGate()
        {
            MutationGate.EndSession();
            var first = Source("first"); first.FailRestore = true;
            var second = Source("second"); second.FailNextTake = true;
            var engine = new ExactMaterialTransactionEngine();
            TestAssert.False(engine.TryBegin(new[] { new MaterialRequirement("wood", 2) },
                new[] { first, second }, out _, out string reason));
            TestAssert.True(reason.StartsWith("recovery-needs-inspection:"));
            TestAssert.True(MutationGate.IsBlocked("first") && MutationGate.IsBlocked("second"));
            TestAssert.True(MutationGate.Current == null);
            TestAssert.False(engine.TryBegin(new[] { new MaterialRequirement("wood", 1) },
                new[] { second }, out _, out reason));
            TestAssert.True(reason.StartsWith("endpoint-needs-inspection:"));
            MutationGate.EndSession();
        }

        internal static void LostAuthorityNeverRestoresOldSnapshot()
        {
            MutationGate.EndSession();
            var inventory = new Inventory("chest", null, 2, 1);
            inventory.GetAllItems().Add(new ItemDrop.ItemData { Prefab = 1, m_stack = 5 });
            bool owner = true;
            var source = new ValheimMaterialSource("chest", inventory, MaterialSourceKind.NearbyContainer,
                0, new[] { "1" }, () => owner, () => owner);
            TestAssert.True(source.TryTake("1", 2, out var restore));
            owner = false;
            TestAssert.False(restore.Restore());
            TestAssert.Equal(3, inventory.GetAllItems()[0].m_stack);
            TestAssert.True(MutationGate.IsBlocked(inventory));
            MutationGate.EndSession();
        }

        internal static void ForeignCallbackEditIsPreserved()
        {
            MutationGate.EndSession();
            var inventory = new Inventory("chest", null, 2, 1);
            var item = new ItemDrop.ItemData { Prefab = 1, m_stack = 5 };
            inventory.GetAllItems().Add(item);
            inventory.m_onChanged = () => { item.m_customData["foreign"] = "keep"; throw new Exception("callback"); };
            var source = new ValheimMaterialSource("chest", inventory, MaterialSourceKind.NearbyContainer,
                0, new[] { "1" }, () => true);
            bool indeterminate = false;
            try { source.TryTake("1", 2, out _); } catch (MutationIndeterminateException) { indeterminate = true; }
            TestAssert.True(indeterminate);
            TestAssert.Equal("keep", item.m_customData["foreign"]);
            TestAssert.Equal(3, item.m_stack);
            TestAssert.True(MutationGate.IsBlocked(inventory));
            MutationGate.EndSession();
        }
    }
}
