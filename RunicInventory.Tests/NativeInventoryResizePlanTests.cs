using System;
using System.Collections.Generic;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class NativeInventoryResizePlanTests
    {
        internal static void Register()
        {
            TestRunner.Run("native pocket expansion swaps only old and new role rows", RowsSwapExactly);
            TestRunner.Run("native pocket expansion carries role-row locks to the new bottom", LocksFollowRoleRow);
            TestRunner.Run("native pocket resize rejects shrink and malformed lock evidence", UnsafeResizeRejected);
        }

        private static void RowsSwapExactly()
        {
            Layout(4, out TopologyLayout previous);
            Layout(7, out TopologyLayout current);
            for (int row = 0; row < current.Height; row++)
            {
                int expected = row == 3 ? 6 : row == 6 ? 3 : row;
                TestAssert.Equal(expected,
                    NativeInventoryResizePlan.MapRow(row, previous.SpecialRow, current.SpecialRow));
            }
        }

        private static void LocksFollowRoleRow()
        {
            Layout(4, out TopologyLayout previous);
            Layout(6, out TopologyLayout current);
            TestAssert.True(NativeInventoryResizePlan.TryCreate(
                previous,
                current,
                new[]
                {
                    new InventorySlotCoordinate(2, 1),
                    new InventorySlotCoordinate(7, previous.SpecialRow)
                },
                out IReadOnlyList<InventorySlotCoordinate> locks,
                out string reason), reason);
            TestAssert.Equal(2, locks.Count);
            TestAssert.Equal(new InventorySlotCoordinate(2, 1), locks[0]);
            TestAssert.Equal(new InventorySlotCoordinate(7, current.SpecialRow), locks[1]);
        }

        private static void UnsafeResizeRejected()
        {
            Layout(6, out TopologyLayout tall);
            Layout(4, out TopologyLayout shortLayout);
            TestAssert.False(NativeInventoryResizePlan.TryCreate(
                tall, shortLayout, Array.Empty<InventorySlotCoordinate>(), out _, out _));
            TestAssert.False(NativeInventoryResizePlan.TryCreate(
                shortLayout,
                tall,
                new[] { new InventorySlotCoordinate(0, 4) },
                out _,
                out _));
        }

        private static void Layout(int height, out TopologyLayout layout)
        {
            TestAssert.True(TopologyLayout.TryCreate(8, height, out layout, out string reason), reason);
        }
    }
}
