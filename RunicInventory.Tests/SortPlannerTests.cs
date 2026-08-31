using System;
using System.Collections.Generic;
using System.Linq;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class SortPlannerTests
    {
        internal static void Register()
        {
            TestRunner.Run("regional sort preserves hotbar special locked and equipped slots", PreservesProtectedRegions);
            TestRunner.Run("regional sort is deterministic and stable", IsDeterministic);
            TestRunner.Run("regional sort touches only explicitly selected rows", SelectedRowsOnly);
            TestRunner.Run("regional sort rejects duplicate live coordinates", DuplicateCoordinatesRejected);
            TestRunner.Run("regional sort rejects unsafe row selection", UnsafeRegionRejected);
            TestRunner.Run("regional sort handles an already sorted region without moves", AlreadySortedNoMoves);
            TestRunner.Run("regional sort quality and weight tie-breaks are deterministic", TieBreaksAreDeterministic);
        }

        private static void PreservesProtectedRegions()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var items = new List<SortItemDescriptor>
            {
                Item(0, 0, 1, "hotbar-z"),
                Item(0, 1, 2, "b"),
                Item(1, 1, 2, "a"),
                Item(2, 1, 2, "locked"),
                Item(3, 1, 2, "equipped", equipped: true),
                Item(5, 3, 2, "quick")
            };
            var locks = new[] { new InventorySlotCoordinate(2, 1) };
            TestAssert.True(SafeSortPlanner.TryPlan(layout, items, locks, new[] { 1, 2 }, out SafeSortPlan plan, out _));
            foreach (SortMove move in plan.Moves)
            {
                TestAssert.False(move.Source.Equals(new InventorySlotCoordinate(0, 0)));
                TestAssert.False(move.Source.Equals(new InventorySlotCoordinate(2, 1)));
                TestAssert.False(move.Source.Equals(new InventorySlotCoordinate(3, 1)));
                TestAssert.False(move.Source.Equals(new InventorySlotCoordinate(5, 3)));
                TestAssert.True(move.Destination.Y == 1 || move.Destination.Y == 2);
                TestAssert.False(move.Destination.Equals(new InventorySlotCoordinate(2, 1)));
                TestAssert.False(move.Destination.Equals(new InventorySlotCoordinate(3, 1)));
            }
        }

        private static void IsDeterministic()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var items = new[] { Item(4, 2, 4, "z"), Item(1, 1, 2, "a"), Item(0, 2, 1, "m") };
            SafeSortPlanner.TryPlan(layout, items, Array.Empty<InventorySlotCoordinate>(), new[] { 1, 2 }, out SafeSortPlan a, out _);
            SafeSortPlanner.TryPlan(layout, items.Reverse(), Array.Empty<InventorySlotCoordinate>(), new[] { 2, 1 }, out SafeSortPlan b, out _);
            TestAssert.Equal(Describe(a), Describe(b));
        }

        private static void SelectedRowsOnly()
        {
            TopologyLayout.TryCreate(8, 5, out TopologyLayout layout, out _);
            var items = new[] { Item(2, 1, 2, "z"), Item(1, 2, 2, "b"), Item(0, 2, 2, "a") };
            SafeSortPlanner.TryPlan(layout, items, null, new[] { 2 }, out SafeSortPlan plan, out _);
            foreach (SortMove move in plan.Moves)
            {
                TestAssert.Equal(2, move.Source.Y);
                TestAssert.Equal(2, move.Destination.Y);
            }
        }

        private static void DuplicateCoordinatesRejected()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var items = new[] { Item(0, 1, 1, "a"), Item(0, 1, 2, "b") };
            TestAssert.False(SafeSortPlanner.TryPlan(layout, items, null, new[] { 1 }, out _, out string code));
            TestAssert.Equal("sort.items-invalid", code);
        }

        private static void UnsafeRegionRejected()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            TestAssert.False(SafeSortPlanner.TryPlan(layout, Array.Empty<SortItemDescriptor>(), null, new[] { 0 }, out _, out string code));
            TestAssert.Equal("sort.region-unsafe", code);
        }

        private static void AlreadySortedNoMoves()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var items = new[] { Item(0, 1, 1, "a"), Item(1, 1, 1, "b") };
            TestAssert.True(SafeSortPlanner.TryPlan(layout, items, null, new[] { 1 }, out SafeSortPlan plan, out _));
            TestAssert.False(plan.ChangesAnything);
            TestAssert.Equal(2, plan.MovableCount);
        }

        private static void TieBreaksAreDeterministic()
        {
            TopologyLayout.TryCreate(8, 4, out TopologyLayout layout, out _);
            var items = new[]
            {
                new SortItemDescriptor(new InventorySlotCoordinate(0, 1), 1, "same", 1, 1f, false),
                new SortItemDescriptor(new InventorySlotCoordinate(1, 1), 1, "same", 2, 1f, false),
                new SortItemDescriptor(new InventorySlotCoordinate(2, 1), 1, "same", 2, 2f, false)
            };
            SafeSortPlanner.TryPlan(layout, items, null, new[] { 1 }, out SafeSortPlan plan, out _);
            string description = Describe(plan);
            TestAssert.Contains(description, "2,1>0,1");
            TestAssert.Contains(description, "0,1>2,1");
        }

        private static SortItemDescriptor Item(int x, int y, int category, string name, bool equipped = false) =>
            new SortItemDescriptor(new InventorySlotCoordinate(x, y), category, name, 1, 1f, equipped);

        private static string Describe(SafeSortPlan plan)
        {
            var targets = new Dictionary<InventorySlotCoordinate, InventorySlotCoordinate>();
            foreach (SortMove move in plan.Moves) targets[move.Source] = move.Destination;
            return string.Join(";", targets.OrderBy(pair => pair.Key).Select(pair => pair.Key + ">" + pair.Value));
        }
    }
}
