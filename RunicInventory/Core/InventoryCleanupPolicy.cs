using System;
using System.Collections.Generic;

namespace RunicInventory.Core
{
    internal enum InventoryCleanupDecision { NativeAllowed, RetainAndExpand, RetainWithoutResize }
    internal readonly struct CleanupCoordinate
    {
        internal CleanupCoordinate(int x, int y) { X = x; Y = y; }
        internal int X { get; }
        internal int Y { get; }
    }

    internal static class InventoryCleanupPolicy
    {
        internal static InventoryCleanupDecision Evaluate(int width, int height,
            IReadOnlyList<CleanupCoordinate> positions, out int retainedHeight)
        {
            retainedHeight = height;
            if (positions == null) return InventoryCleanupDecision.RetainWithoutResize;
            bool invalid = false, bounded = width == 8 && height >= 0 && height <= 12;
            foreach (var position in positions)
            {
                invalid |= position.X < 0 || position.X >= width || position.Y < 0 || position.Y >= height;
                bounded &= position.X >= 0 && position.X < 8 && position.Y >= 0 && position.Y < 12;
                if (position.Y >= 0 && position.Y < 12) retainedHeight = Math.Max(retainedHeight, position.Y + 1);
            }
            if (!invalid) return InventoryCleanupDecision.NativeAllowed;
            if (bounded) return InventoryCleanupDecision.RetainAndExpand;
            retainedHeight = height;
            return InventoryCleanupDecision.RetainWithoutResize;
        }
    }
}
