using System;
using System.Collections.Generic;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    // Valheim 1.0 pocket upgrades grow the native inventory after Player.Load. Runic's role row
    // remains the bottom row, so an expansion swaps the old and new bottom rows without changing
    // any other native coordinate. Shrinks remain vanilla-owned and are deliberately unsupported.
    internal static class NativeInventoryResizePlan
    {
        internal static bool TryCreate(
            TopologyLayout previous,
            TopologyLayout current,
            IEnumerable<InventorySlotCoordinate> previousLocks,
            out IReadOnlyList<InventorySlotCoordinate> currentLocks,
            out string reasonCode)
        {
            currentLocks = Array.Empty<InventorySlotCoordinate>();
            if (previous == null || current == null || previous.Width != current.Width ||
                current.Height <= previous.Height)
            {
                reasonCode = "resize.expansion-required";
                return false;
            }

            var mapped = new List<InventorySlotCoordinate>();
            var unique = new HashSet<InventorySlotCoordinate>();
            if (previousLocks != null)
            {
                foreach (InventorySlotCoordinate coordinate in previousLocks)
                {
                    if (!previous.InBounds(coordinate))
                    {
                        reasonCode = "resize.lock-out-of-bounds";
                        return false;
                    }
                    var destination = new InventorySlotCoordinate(
                        coordinate.X,
                        MapRow(coordinate.Y, previous.SpecialRow, current.SpecialRow));
                    if (!current.InBounds(destination) || !unique.Add(destination))
                    {
                        reasonCode = "resize.lock-map-invalid";
                        return false;
                    }
                    mapped.Add(destination);
                }
            }
            mapped.Sort((left, right) =>
            {
                int row = left.Y.CompareTo(right.Y);
                return row != 0 ? row : left.X.CompareTo(right.X);
            });
            currentLocks = mapped.AsReadOnly();
            reasonCode = "ok";
            return true;
        }

        internal static int MapRow(int row, int previousSpecialRow, int currentSpecialRow)
        {
            if (row == previousSpecialRow) return currentSpecialRow;
            if (row == currentSpecialRow) return previousSpecialRow;
            return row;
        }
    }
}
