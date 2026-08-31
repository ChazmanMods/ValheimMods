using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using RunicInventory.Api;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class PerformanceTests
    {
        internal static void Register()
        {
            TestRunner.Run("100-item pure sort plan remains within the native bound", HundredItemPlan);
            TestRunner.Run("1k-item adversarial sort stops after bounded evidence", ThousandItemPlanIsBounded);
            TestRunner.Run("10k-item adversarial sort has the same bounded scan ceiling", TenThousandItemPlanIsBounded);
            TestRunner.Run("1k and 10k oversized sort allocations remain size-independent", OversizedAllocationIsBounded);
            TestRunner.Run("10k invalid topology proofs allocate no growing cache", InvalidTopologyProofsAreConstant);
            TestRunner.Run("100k pickup previews are allocation-free pure arithmetic", PickupPlannerDoesNotAllocate);
        }

        private static void HundredItemPlan()
        {
            TopologyLayout.TryCreate(8, 16, out TopologyLayout layout, out _);
            var counting = new CountingEnumerable<SortItemDescriptor>(Items(100));
            var rows = new List<int>();
            for (int row = 1; row < 15; row++) rows.Add(row);
            Stopwatch watch = Stopwatch.StartNew();
            bool result = SafeSortPlanner.TryPlan(layout, counting, null, rows, out SafeSortPlan plan, out string code);
            watch.Stop();
            TestAssert.True(result, code);
            TestAssert.Equal(100, counting.MoveNextCount);
            TestAssert.True(plan.MovableCount <= 100);
            TestAssert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "Pure 100-item planner exceeded 2 seconds.");
        }

        private static void ThousandItemPlanIsBounded()
        {
            TopologyLayout.TryCreate(8, 16, out TopologyLayout layout, out _);
            var counting = new CountingEnumerable<SortItemDescriptor>(Items(1000));
            TestAssert.False(SafeSortPlanner.TryPlan(layout, counting, null, new[] { 1 }, out _, out _));
            TestAssert.True(counting.MoveNextCount <= 129,
                "Planner inspected " + counting.MoveNextCount + " items instead of stopping at its native evidence bound.");
        }

        private static void TenThousandItemPlanIsBounded()
        {
            TopologyLayout.TryCreate(8, 16, out TopologyLayout layout, out _);
            var counting = new CountingEnumerable<SortItemDescriptor>(Items(10000));
            TestAssert.False(SafeSortPlanner.TryPlan(layout, counting, null, new[] { 1 }, out _, out _));
            TestAssert.True(counting.MoveNextCount <= 129,
                "Planner inspected " + counting.MoveNextCount + " items instead of stopping at its native evidence bound.");
        }

        private static void OversizedAllocationIsBounded()
        {
            TopologyLayout.TryCreate(8, 16, out TopologyLayout layout, out _);
            SafeSortPlanner.TryPlan(layout, Items(1000), null, new[] { 1 }, out _, out _);
            long before1k = GC.GetAllocatedBytesForCurrentThread();
            SafeSortPlanner.TryPlan(layout, Items(1000), null, new[] { 1 }, out _, out _);
            long allocation1k = GC.GetAllocatedBytesForCurrentThread() - before1k;
            long before10k = GC.GetAllocatedBytesForCurrentThread();
            SafeSortPlanner.TryPlan(layout, Items(10000), null, new[] { 1 }, out _, out _);
            long allocation10k = GC.GetAllocatedBytesForCurrentThread() - before10k;
            TestAssert.True(allocation10k <= allocation1k + 4096,
                "10k input allocated " + allocation10k + " bytes versus 1k " + allocation1k + ".");
        }

        private static void InvalidTopologyProofsAreConstant()
        {
            TopologyLayout.TryCreate(7, 4, out _, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
                TopologyLayout.TryCreate(7, 4, out _, out _);
            long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            TestAssert.True(allocation < 65536, "Invalid proofs allocated " + allocation + " bytes.");
        }

        private static void PickupPlannerDoesNotAllocate()
        {
            PickupPlanner.Evaluate(1, 50, 0, 1, 1f, 0f, 300f, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int accepted = 0;
            for (int index = 0; index < 100000; index++)
                accepted += PickupPlanner.Evaluate(1, 50, 0, 1, 1f, 0f, 300f, false).AcceptedItems;
            long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            TestAssert.Equal(100000, accepted);
            TestAssert.True(allocation < 1024, "Pure pickup arithmetic allocated " + allocation + " bytes.");
        }

        private static IEnumerable<SortItemDescriptor> Items(int count)
        {
            for (int index = 0; index < count; index++)
            {
                int bounded = index % 128;
                yield return new SortItemDescriptor(
                    new InventorySlotCoordinate(bounded % 8, bounded / 8),
                    index % 8,
                    "item-" + index.ToString("D5"),
                    index % 5,
                    index % 20,
                    false);
            }
        }

        private sealed class CountingEnumerable<T> : IEnumerable<T>
        {
            private readonly IEnumerable<T> _source;
            internal CountingEnumerable(IEnumerable<T> source) { _source = source; }
            internal int MoveNextCount { get; private set; }
            public IEnumerator<T> GetEnumerator() => new CountingEnumerator(this, _source.GetEnumerator());
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class CountingEnumerator : IEnumerator<T>
            {
                private readonly CountingEnumerable<T> _owner;
                private readonly IEnumerator<T> _inner;
                internal CountingEnumerator(CountingEnumerable<T> owner, IEnumerator<T> inner) { _owner = owner; _inner = inner; }
                public T Current => _inner.Current;
                object IEnumerator.Current => Current;
                public bool MoveNext() { bool result = _inner.MoveNext(); if (result) _owner.MoveNextCount++; return result; }
                public void Reset() => _inner.Reset();
                public void Dispose() => _inner.Dispose();
            }
        }
    }
}
