using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    internal static class PreviewPoolMaintenance
    {
        internal static int PruneUnavailable<T>(
            IList<T> entries,
            Func<T, bool> isUnavailable)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (isUnavailable == null) throw new ArgumentNullException(nameof(isUnavailable));
            int removed = 0;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (!isUnavailable(entries[index])) continue;
                entries.RemoveAt(index);
                removed++;
            }
            return removed;
        }
    }
}
