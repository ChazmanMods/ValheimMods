using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class DedicatedRowTests
    {
        internal static void Register()
        {
            TestRunner.Run("dedicated row leaves all 32 normal cells intact", FullNormalInventory);
            TestRunner.Run("equipped armor moves into extra row without changing ordinary items", EquippedArmor);
            TestRunner.Run("legacy roles and their locks migrate without bringing materials", LegacyMigration);
            TestRunner.Run("role-slot category matrix rejects all unrelated items", RoleMatrix);
            TestRunner.Run("pocket upgrades add capacity without accumulating extra rows", PocketGrowth);
            TestRunner.Run("disable relocates all role items or refuses entire shrink", DisableSafety);
            TestRunner.Run("duplicate positions and equipped roles are rejected", InvalidEvidence);
            TestRunner.Run("randomized dedicated-row migrations conserve every item and legal cell", RandomizedPlans);
            TestRunner.Run("row lifecycle bypasses destructive native shrink and guards both add overloads", Wiring);
            TestRunner.Run("unsafe disable is rejected and the settings toggle has a dynamic read-only drawer", DisableControl);
            TestRunner.Run("native pocket purchases use the native row key and containers restore saved extra rows", NativeSizeContracts);
        }

        private static DedicatedRowItem Item(int x, int y, InventoryItemCategory kind = InventoryItemCategory.Material, bool equipped = false) =>
            new DedicatedRowItem(new InventorySlotCoordinate(x, y), kind, equipped);
        private static InventorySlotCoordinate[] Plan(int rows, bool enabled, int previous, List<DedicatedRowItem> items,
            IReadOnlyList<InventorySlotCoordinate> locks = null)
        {
            TestAssert.True(DedicatedRowPlan.TryCreate(rows, enabled, previous, items, locks, out var result, out _, out string reason), reason);
            TestAssert.Equal(items.Count, result.Length);
            TestAssert.Equal(items.Count, result.Distinct().Count());
            for (int i = 0; i < result.Length; i++)
            {
                TestAssert.True(result[i].Y < rows + (enabled ? 1 : 0));
                if (enabled && result[i].Y == rows)
                    TestAssert.True(TopologyLayout.Accepts((InventoryRoleKind)(result[i].X + 1), items[i].Category));
            }
            return result;
        }
        private static List<DedicatedRowItem> Full(int rows) => Enumerable.Range(0, rows * 8).Select(i => Item(i % 8, i / 8)).ToList();
        private static void FullNormalInventory()
        {
            var items = Full(4);
            var result = Plan(4, true, -1, items);
            for (int i = 0; i < items.Count; i++) TestAssert.Equal(items[i].Position, result[i]);
        }
        private static void EquippedArmor()
        {
            var items = Full(4);
            items[0] = Item(0, 0, InventoryItemCategory.Helmet, true);
            var result = Plan(4, true, -1, items);
            TestAssert.Equal(new InventorySlotCoordinate(0, 4), result[0]);
            for (int i = 1; i < items.Count; i++) TestAssert.Equal(items[i].Position, result[i]);
        }
        private static void LegacyMigration()
        {
            var items = new List<DedicatedRowItem> { Item(0, 3, InventoryItemCategory.Helmet), Item(5, 3, InventoryItemCategory.Consumable), Item(6, 3) };
            TestAssert.True(DedicatedRowPlan.TryCreate(4, true, 3, items, new[] { items[0].Position, items[2].Position }, out var result, out var locks, out _));
            TestAssert.Equal(new InventorySlotCoordinate(0, 4), result[0]);
            TestAssert.Equal(new InventorySlotCoordinate(5, 4), result[1]);
            TestAssert.Equal(new InventorySlotCoordinate(6, 3), result[2]);
            TestAssert.True(locks.Contains(result[0]) && locks.Contains(result[2]));
        }
        private static void RoleMatrix()
        {
            foreach (InventoryRoleKind role in Enum.GetValues(typeof(InventoryRoleKind)))
                foreach (InventoryItemCategory kind in Enum.GetValues(typeof(InventoryItemCategory)))
                {
                    bool expected = (int)role <= 5 ? (int)kind == (int)role + 2 :
                        kind == InventoryItemCategory.Consumable || kind == InventoryItemCategory.Tool || kind == InventoryItemCategory.Utility;
                    TestAssert.Equal(expected, TopologyLayout.Accepts(role, kind));
                }
        }
        private static void PocketGrowth()
        {
            var items = new List<DedicatedRowItem> { Item(0, 4, InventoryItemCategory.Helmet, true), Item(7, 4, InventoryItemCategory.Tool) };
            int old = 4;
            foreach (int rows in new[] { 4, 5, 5, 7, 9, 9 })
            {
                var result = Plan(rows, true, old, items);
                TestAssert.True(result.All(p => p.Y == rows));
                items = items.Select((item, i) => new DedicatedRowItem(result[i], item.Category, item.Equipped)).ToList();
                old = rows;
            }
        }
        private static void DisableSafety()
        {
            var items = Full(4);
            items.Add(Item(0, 4, InventoryItemCategory.Helmet, true));
            TestAssert.False(DedicatedRowPlan.TryCreate(4, false, 4, items, null, out var failed, out _, out _));
            TestAssert.True(failed == null);
            items.RemoveAt(2);
            var result = Plan(4, false, 4, items);
            TestAssert.True(result.All(p => p.Y < 4));
            TestAssert.Equal(new InventorySlotCoordinate(2, 0), result[result.Length - 1]);
        }
        private static void InvalidEvidence()
        {
            foreach (var items in new[] {
                new List<DedicatedRowItem> { Item(0, 0), Item(0, 0) },
                new List<DedicatedRowItem> { Item(0, 0, InventoryItemCategory.Helmet, true), Item(1, 0, InventoryItemCategory.Helmet, true) } })
                TestAssert.False(DedicatedRowPlan.TryCreate(4, true, -1, items, null, out _, out _, out _));
            TestAssert.False(DedicatedRowPlan.TryCreate(10, true, -1, Full(4), null, out _, out _, out _));
        }
        private static void RandomizedPlans()
        {
            var random = new Random(54821);
            for (int test = 0; test < 500; test++)
            {
                int rows = random.Next(4, 10);
                var items = Full(rows).Where(_ => random.Next(3) != 0).ToList();
                for (int i = 0; i < items.Count; i++)
                    items[i] = new DedicatedRowItem(items[i].Position, (InventoryItemCategory)random.Next(10), false);
                var result = Plan(rows, true, rows - 1, items);
                var migrated = items.Select((item, i) => new DedicatedRowItem(result[i], item.Category, item.Equipped)).ToList();
                var again = Plan(rows, true, rows, migrated);
                for (int i = 0; i < result.Length; i++) TestAssert.Equal(result[i], again[i]);
            }
        }
        private static void Wiring()
        {
            string patches = File.ReadAllText(TestPaths.Module("Integration/HarmonyPatches.cs"));
            TestAssert.Contains(patches, "runtime.SetNativeInventorySize(__instance, __0)");
            TestAssert.Contains(patches, "class PositionedInventoryAddPatch");
            TestAssert.Contains(patches, "class PositionedInventoryPublicAddPatch");
            string runtime = File.ReadAllText(TestPaths.Module("Integration/InventoryRuntime.cs"));
            TestAssert.Contains(runtime, "AllowPositionedAddition(source, displaced");
            TestAssert.Contains(runtime, "player.m_customData?.Remove(DedicatedRowPlan.MetadataKey)");
            string row = File.ReadAllText(TestPaths.Module("Integration/DedicatedInventoryRow.cs"));
            TestAssert.Contains(row, "InventoryEvidence.VerifyUnchangedExceptPosition");
            TestAssert.Contains(row, "InventoryEvidence.RestorePositions(before)");
            TestAssert.False(row.Contains(".DropInvalidItems("));
            TestAssert.False(row.Contains(".RemoveItem("));
        }

        private static void DisableControl()
        {
            string runtime = File.ReadAllText(TestPaths.Module("Integration/InventoryRuntime.cs"));
            TestAssert.Contains(runtime, "!CanDisable(out string disableReason)");
            TestAssert.Contains(runtime, "RestoreEnabled(disableReason)");
            string row = File.ReadAllText(TestPaths.Module("Integration/DedicatedInventoryRow.cs"));
            TestAssert.Contains(row, "InventoryConfig.Enabled.Value = true");
            TestAssert.Contains(row, "there is no room to move the items in the extra row");
            var attrType = typeof(RunicInventory.Integration.ConfigurationManagerAttributes);
            TestAssert.Equal(typeof(bool), attrType.GetProperty("ReadOnly").PropertyType);
            TestAssert.Equal(typeof(Action<BepInEx.Configuration.ConfigEntryBase>), attrType.GetField("CustomDrawer").FieldType);
            string drawer = File.ReadAllText(TestPaths.Module("Integration/InventorySettingsDrawer.cs"));
            TestAssert.Contains(drawer, "GUI.enabled = previous && (!enabled.Value || canDisable)");
            TestAssert.Contains(drawer, "GUILayout.Label(reason");
            string config = File.ReadAllText(TestPaths.Module("InventoryConfig.cs"));
            TestAssert.Contains(config, "new Integration.ConfigurationManagerAttributes()");
        }

        private static void NativeSizeContracts()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo buy = typeof(StoreGui).GetMethod("BuySelectedItem", flags);
            TestAssert.True(IlReader.Calls(buy, typeof(Player), "TryGetUniqueKeyValue"));
            TestAssert.True(IlReader.Calls(buy, typeof(Player), "SetInventorySize"));
            TestAssert.False(IlReader.Calls(buy, typeof(Inventory), "GetHeight"));
            MethodInfo update = typeof(Container).GetMethod("UpdateRows", flags);
            TestAssert.True(IlReader.Calls(update, typeof(Inventory), "GetAllItems"));
            TestAssert.True(IlReader.Calls(update, typeof(Inventory), "SetHeight"));
            TestAssert.True(IlReader.Calls(typeof(Container).GetMethod("Load", flags), typeof(Container), "UpdateRows"));
            MethodInfo grave = typeof(Inventory).GetMethod("MoveInventoryToGrave", flags);
            TestAssert.True(IlReader.Accesses(grave, typeof(Inventory), "m_height"));
        }
    }
}
