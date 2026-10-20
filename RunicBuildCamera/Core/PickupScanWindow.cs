using System;
using System.Collections.Generic;

namespace RunicBuildCamera.Core
{
    internal static class PickupScanWindow
    {
        // Snapshot before pickup can remove/swap entries in Valheim's live item registry.
        // Advancing even past rejected items prevents a full buffer starving later drops.
        internal static int CopyNext<T>(IReadOnlyList<T> items, T[] buffer, ref int cursor)
        {
            if (items == null || items.Count == 0 || buffer.Length == 0)
            {
                cursor = 0;
                return 0;
            }
            int total = items.Count;
            if (cursor < 0 || cursor >= total) cursor = 0;
            int count = Math.Min(total, buffer.Length);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = items[cursor];
                cursor = (cursor + 1) % total;
            }
            return count;
        }
    }
}
