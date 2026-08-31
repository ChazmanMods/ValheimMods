using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal sealed class SortItemDescriptor
    {
        internal SortItemDescriptor(
            InventorySlotCoordinate coordinate,
            int category,
            string stableName,
            int quality,
            float weight,
            bool equipped)
        {
            if (category < 0 || category > 1024) throw new ArgumentOutOfRangeException(nameof(category));
            if (string.IsNullOrEmpty(stableName) || stableName.Length > 160)
                throw new ArgumentException("A bounded stable item name is required.", nameof(stableName));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                throw new ArgumentOutOfRangeException(nameof(weight));
            Coordinate = coordinate;
            Category = category;
            StableName = stableName;
            Quality = quality;
            Weight = weight;
            Equipped = equipped;
        }

        internal InventorySlotCoordinate Coordinate { get; }
        internal int Category { get; }
        internal string StableName { get; }
        internal int Quality { get; }
        internal float Weight { get; }
        internal bool Equipped { get; }
    }

    internal readonly struct SortMove
    {
        internal SortMove(InventorySlotCoordinate source, InventorySlotCoordinate destination)
        {
            Source = source;
            Destination = destination;
        }

        internal InventorySlotCoordinate Source { get; }
        internal InventorySlotCoordinate Destination { get; }
    }

    internal sealed class SafeSortPlan
    {
        private readonly ReadOnlyCollection<SortMove> _moves;

        internal SafeSortPlan(IEnumerable<SortMove> moves, int movableCount)
        {
            _moves = new List<SortMove>(moves ?? Array.Empty<SortMove>()).AsReadOnly();
            MovableCount = movableCount;
        }

        internal IReadOnlyList<SortMove> Moves => _moves;
        internal int MovableCount { get; }
        internal bool ChangesAnything => _moves.Count != 0;
    }

    internal static class SafeSortPlanner
    {
        internal static bool TryPlan(
            TopologyLayout layout,
            IEnumerable<SortItemDescriptor> items,
            IEnumerable<InventorySlotCoordinate> locks,
            IEnumerable<int> selectedRows,
            out SafeSortPlan plan,
            out string reasonCode)
        {
            plan = null;
            if (layout == null)
            {
                reasonCode = "sort.layout-null";
                return false;
            }
            var lockSet = new HashSet<InventorySlotCoordinate>();
            if (locks != null)
            {
                foreach (InventorySlotCoordinate coordinate in locks)
                {
                    if (!layout.InBounds(coordinate) || !lockSet.Add(coordinate))
                    {
                        reasonCode = "sort.locks-invalid";
                        return false;
                    }
                }
            }

            var rows = new SortedSet<int>();
            if (selectedRows != null)
            {
                foreach (int row in selectedRows)
                {
                    if (row <= 0 || row >= layout.SpecialRow)
                    {
                        reasonCode = "sort.region-unsafe";
                        return false;
                    }
                    rows.Add(row);
                }
            }
            if (rows.Count == 0)
            {
                reasonCode = "sort.region-empty";
                return false;
            }

            var occupied = new Dictionary<InventorySlotCoordinate, SortItemDescriptor>();
            if (items != null)
            {
                foreach (SortItemDescriptor item in items)
                {
                    if (item == null || !layout.InBounds(item.Coordinate) || occupied.ContainsKey(item.Coordinate))
                    {
                        reasonCode = "sort.items-invalid";
                        return false;
                    }
                    occupied.Add(item.Coordinate, item);
                    if (occupied.Count > InventoryTopologySnapshot.MaximumNativeSlots)
                    {
                        reasonCode = "sort.item-bound-exceeded";
                        return false;
                    }
                }
            }

            var movable = new List<SortItemDescriptor>();
            foreach (SortItemDescriptor item in occupied.Values)
            {
                if (!rows.Contains(item.Coordinate.Y) || item.Equipped || lockSet.Contains(item.Coordinate)) continue;
                movable.Add(item);
            }
            movable.Sort(CompareItems);

            var available = new List<InventorySlotCoordinate>();
            foreach (int row in rows)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    var coordinate = new InventorySlotCoordinate(x, row);
                    if (lockSet.Contains(coordinate)) continue;
                    if (occupied.TryGetValue(coordinate, out SortItemDescriptor fixedItem) && fixedItem.Equipped) continue;
                    available.Add(coordinate);
                }
            }
            if (movable.Count > available.Count)
            {
                reasonCode = "sort.capacity-proof-failed";
                return false;
            }

            var moves = new List<SortMove>();
            var sources = new HashSet<InventorySlotCoordinate>();
            var destinations = new HashSet<InventorySlotCoordinate>();
            for (int index = 0; index < movable.Count; index++)
            {
                InventorySlotCoordinate source = movable[index].Coordinate;
                InventorySlotCoordinate destination = available[index];
                if (source.Equals(destination)) continue;
                if (!sources.Add(source) || !destinations.Add(destination))
                {
                    reasonCode = "sort.permutation-not-unique";
                    return false;
                }
                moves.Add(new SortMove(source, destination));
            }
            plan = new SafeSortPlan(moves, movable.Count);
            reasonCode = "ok";
            return true;
        }

        private static int CompareItems(SortItemDescriptor left, SortItemDescriptor right)
        {
            int category = left.Category.CompareTo(right.Category);
            if (category != 0) return category;
            int name = StringComparer.Ordinal.Compare(left.StableName, right.StableName);
            if (name != 0) return name;
            int quality = right.Quality.CompareTo(left.Quality);
            if (quality != 0) return quality;
            int weight = right.Weight.CompareTo(left.Weight);
            if (weight != 0) return weight;
            return left.Coordinate.CompareTo(right.Coordinate);
        }
    }
}
