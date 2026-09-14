using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using RunicInventory.Api;
using RunicInventory.Core;
using RunicInventory.Integration;

namespace RunicInventory.Tests
{
    internal static class BetterArcheryCompatibilityTests
    {
        internal static void Register()
        {
            TestRunner.Run("quiver and Runic row stay disjoint for every native pocket size", EveryPocketSize);
            TestRunner.Run("quiver upgrade migration preserves ammo roles and slot locks", ExistingRunicRow);
            TestRunner.Run("quiver full inventory disable refuses without moving any item", FullDisable);
            TestRunner.Run("quiver-disabled migration needs ordinary space for its arrows", DisableQuiver);
            TestRunner.Run("quiver reserve cannot accept miscellaneous items on migration", InvalidQuiverCells);
            TestRunner.Run("quiver resize preserves native progression and roles", PocketResize);
            TestRunner.Run("audited Better Archery binary exposes the exact integration methods", AuditedBinary);
            TestRunner.Run("quiver layouts and empty locks round trip at every pocket size", LayoutPersistence);
            TestRunner.Run("quiver-disabled full backpack refuses without dropping ammo", FullQuiverDisable);
            TestRunner.Run("combined layout clears bootstrap metadata before player load", LoadMarker);
            TestRunner.Run("all conflicting Better Archery prefix bodies have direct HarmonyX guards", HarmonyXGuards);
            TestRunner.Run("cleanup retains items outside a resized Runic inventory", CleanupRetention);
        }
        private static DedicatedRowItem Item(int x, int y, InventoryItemCategory category, bool equipped = false) =>
            new DedicatedRowItem(new InventorySlotCoordinate(x, y), category, equipped);
        private static void EveryPocketSize()
        {
            for (int rows = 4; rows <= 9; rows++)
            {
                var items = new[] { Item(0, rows + 1, InventoryItemCategory.Ammunition), Item(1, rows + 1, InventoryItemCategory.Ammunition),
                    Item(2, rows + 1, InventoryItemCategory.Ammunition), Item(3, 0, InventoryItemCategory.Helmet, true),
                    Item(5, rows, InventoryItemCategory.Consumable) };
                TestAssert.True(DedicatedRowPlan.TryCreate(rows, true, rows, items, null, out var positions, out _, out string reason, true, rows + 1), reason);
                for (int i = 0; i < 3; i++) TestAssert.Equal(new InventorySlotCoordinate(i, rows + 1), positions[i]);
                TestAssert.Equal(new InventorySlotCoordinate(0, rows + 2), positions[3]);
                TestAssert.Equal(new InventorySlotCoordinate(5, rows + 2), positions[4]);
                TestAssert.Equal(positions.Length, positions.Distinct().Count());
            }
        }
        private static void ExistingRunicRow()
        {
            var items = new[] { Item(1, 4, InventoryItemCategory.Chest, true), Item(5, 4, InventoryItemCategory.Consumable),
                Item(0, 5, InventoryItemCategory.Ammunition), Item(4, 0, InventoryItemCategory.Material) };
            var locks = new[] { new InventorySlotCoordinate(5, 4), new InventorySlotCoordinate(0, 5), new InventorySlotCoordinate(7, 4) };
            TestAssert.True(DedicatedRowPlan.TryCreate(4, true, 4, items, locks, out var positions, out var mapped, out _, true, 5));
            TestAssert.Equal(new InventorySlotCoordinate(1, 6), positions[0]);
            TestAssert.Equal(new InventorySlotCoordinate(5, 6), positions[1]);
            TestAssert.Equal(new InventorySlotCoordinate(0, 5), positions[2]);
            TestAssert.True(mapped.Contains(new InventorySlotCoordinate(7, 6)));
            TestAssert.True(mapped.Contains(new InventorySlotCoordinate(5, 6)));
            TestAssert.True(mapped.Contains(new InventorySlotCoordinate(0, 5)));
        }
        private static void FullDisable()
        {
            var items = new List<DedicatedRowItem>();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) items.Add(Item(x, y, InventoryItemCategory.Material));
            items.Add(Item(0, 5, InventoryItemCategory.Ammunition));
            items.Add(Item(1, 6, InventoryItemCategory.Chest, true));
            TestAssert.False(DedicatedRowPlan.TryCreate(4, false, 6, items, null, out var positions, out _, out string reason, true, 5));
            TestAssert.True(positions == null);
            TestAssert.Equal("extra-row.clear-space-before-shrinking", reason);
            items.RemoveAt(0);
            TestAssert.True(DedicatedRowPlan.TryCreate(4, false, 6, items, null, out positions, out _, out _, true, 5));
            TestAssert.Equal(new InventorySlotCoordinate(0, 5), positions[31]);
            TestAssert.Equal(new InventorySlotCoordinate(0, 0), positions[32]);
        }
        private static void DisableQuiver()
        {
            var items = new[] { Item(0, 5, InventoryItemCategory.Ammunition), Item(5, 6, InventoryItemCategory.Consumable) };
            TestAssert.True(DedicatedRowPlan.TryCreate(4, true, 6, items, null, out var positions, out _, out _, false, 5));
            TestAssert.True(positions[0].Y < 4);
            TestAssert.Equal(new InventorySlotCoordinate(5, 4), positions[1]);
        }
        private static void InvalidQuiverCells()
        {
            var items = new[] { Item(0, 4, InventoryItemCategory.Material), Item(0, 5, InventoryItemCategory.Material), Item(7, 5, InventoryItemCategory.Ammunition) };
            TestAssert.True(DedicatedRowPlan.TryCreate(4, true, -1, items, null, out var positions, out _, out _, true, 5));
            TestAssert.True(positions.All(p => p.Y < 4));
        }
        private static void PocketResize()
        {
            for (int before = 4; before <= 9; before++) for (int after = 4; after <= 9; after++)
            {
                var items = new[] { Item(2, before + 1, InventoryItemCategory.Ammunition), Item(5, before + 2, InventoryItemCategory.Consumable) };
                TestAssert.True(DedicatedRowPlan.TryCreate(after, true, before + 2, items, null, out var positions, out _, out _, true, before + 1));
                TestAssert.Equal(new InventorySlotCoordinate(2, after + 1), positions[0]);
                TestAssert.Equal(new InventorySlotCoordinate(5, after + 2), positions[1]);
            }
        }
        private static void AuditedBinary()
        {
            string path = @"E:\Valheim Mods\_research\BetterArchery-compat\1.9.99\BetterArchery\plugins\BetterArchery.dll";
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                TestAssert.Equal(BetterArcheryCompatibility.AuditedHash, Convert.ToHexString(sha.ComputeHash(stream)));
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var api = assembly.MainModule.GetType("BetterArchery.BetterArchery");
            TestAssert.Equal("System.Int32", api.Fields.Single(f => f.Name == "QuiverRowIndex").FieldType.FullName);
            TestAssert.True(api.Fields.Any(f => f.Name == "ConfigQuiverEnabled"));
            TestAssert.True(api.Fields.Any(f => f.Name == "HoldingKeyCode"));
            TestAssert.Equal("UnityEngine.KeyCode", api.Methods.Single(m => m.Name == "GetBindingKeycode").ReturnType.FullName);
            TestAssert.Equal("System.Boolean", api.Methods.Single(m => m.Name == "IsQuiverEquipped").ReturnType.FullName);
            var grid = assembly.MainModule.GetType("BetterArchery.InventoryGrid_UpdateGui_Patch");
            TestAssert.Equal(2, grid.Methods.Single(m => m.Name == "HideModRows").Parameters.Count);
            TestAssert.True(assembly.MainModule.GetType("BetterArchery.Tombstone").NestedTypes.Single(t => t.Name == "TombStone_OnTakeAllSuccess_Patch").Methods.Any(m => m.Name == "Postfix"));
            TestAssert.True(assembly.MainModule.GetType("BetterArchery.TerminalAwake_Patch").NestedTypes.Single(t => t.Name == "<>c").Methods.Any(m => m.Name == "<Postfix>b__0_0"));
        }

        private static void LayoutPersistence()
        {
            for (int rows = 4; rows <= 9; rows++)
            {
                TestAssert.True(TopologyLayout.TryCreate(8, rows + 3, out var layout, out _));
                var locks = new[] { new InventorySlotCoordinate(0, rows + 1), new InventorySlotCoordinate(7, rows + 2) };
                TestAssert.True(TopologyPersistenceCodec.TryEncode(layout, locks, out var payload, out _));
                TestAssert.True(TopologyPersistenceCodec.TryDecode(payload, out var state, out _));
                TestAssert.Equal(rows + 3, state.Height);
                TestAssert.True(locks.All(c => state.LockedSlots().Contains(c)));
            }
        }

        private static void FullQuiverDisable()
        {
            var items = new List<DedicatedRowItem>();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) items.Add(Item(x, y, InventoryItemCategory.Material));
            items.Add(Item(0, 5, InventoryItemCategory.Ammunition));
            TestAssert.False(DedicatedRowPlan.TryCreate(4, true, 6, items, null, out var result, out _, out var reason, false, 5));
            TestAssert.True(result == null);
            TestAssert.Equal("extra-row.clear-space-before-shrinking", reason);
        }

        private static void LoadMarker()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(DedicatedRowPlan).Assembly.Location);
            var method = assembly.MainModule.GetType("RunicInventory.Integration.InventoryRuntime").Methods.Single(m => m.Name == "OnPlayerLoadStarted");
            TestAssert.True(method.Body.Instructions.Any(i => Equals(i.Operand, DedicatedRowPlan.QuiverMetadataKey)));
        }

        private static void HarmonyXGuards()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(DedicatedRowPlan).Assembly.Location);
            var adapter = assembly.MainModule.GetType("RunicInventory.Integration.BetterArcheryCompatibility");
            var initialize = adapter.Methods.Single(m => m.Name == "Initialize");
            foreach (string guard in new[] { "AllowArcheryResize", "AllowArcheryFind", "AllowArcheryCapacity" })
            {
                TestAssert.True(initialize.Body.Instructions.Any(i => Equals(i.Operand, guard)));
                var method = adapter.Methods.Single(m => m.Name == guard);
                TestAssert.Equal("System.Boolean&", method.Parameters.Single(p => p.Name == "__result").ParameterType.FullName);
            }
            foreach (string target in new[] { "BetterArchery.Player_SetInventorySize_Patch", "BetterArchery.Inventory_FindEmptySlot_Patch", "BetterArchery.Inventory_HaveEmptySlot_Patch" })
                TestAssert.True(initialize.Body.Instructions.Any(i => Equals(i.Operand, target)));
            var cleanup = assembly.MainModule.GetType("RunicInventory.Integration.InventoryRuntime").Methods.Single(m => m.Name == "AllowInvalidItemCleanup");
            TestAssert.False(cleanup.Body.Instructions.Any(i => i.Operand is MethodReference method &&
                (method.Name == "DropItem" || method.Name == "RemoveItem" || method.Name == "RemoveAll")));
            TestAssert.True(cleanup.Body.Instructions.Any(i => i.Operand is MethodReference method && method.DeclaringType.Name == "InventoryCleanupPolicy"));
        }

        private static void CleanupRetention()
        {
            for (int rows = 4; rows <= 9; rows++)
            {
                var positions = Enumerable.Range(0, 5).Select(x => new CleanupCoordinate(x, rows + 2)).ToArray();
                TestAssert.Equal(InventoryCleanupDecision.RetainAndExpand,
                    InventoryCleanupPolicy.Evaluate(8, rows + 2, positions, out int height));
                TestAssert.Equal(rows + 3, height);
                TestAssert.Equal(InventoryCleanupDecision.NativeAllowed, InventoryCleanupPolicy.Evaluate(8, height, positions, out _));
            }
            TestAssert.Equal(InventoryCleanupDecision.RetainWithoutResize,
                InventoryCleanupPolicy.Evaluate(8, 6, new[] { new CleanupCoordinate(0, int.MaxValue) }, out int retained));
            TestAssert.Equal(6, retained);
            TestAssert.Equal(InventoryCleanupDecision.RetainWithoutResize, InventoryCleanupPolicy.Evaluate(8, 6, null, out _));
        }
    }
}
