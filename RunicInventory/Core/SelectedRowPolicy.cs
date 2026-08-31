using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicInventory.Core
{
    internal static class SelectedRowPolicy
    {
        internal const int MaximumTokens = 16;
        internal const int MaximumCharacters = 256;

        internal static bool TryParse(string text, TopologyLayout layout, out IReadOnlyList<int> rows, out string reasonCode)
        {
            rows = Array.Empty<int>();
            if (layout == null)
            {
                reasonCode = "sort.layout-null";
                return false;
            }
            if (text != null && text.Length > MaximumCharacters)
            {
                reasonCode = "sort.region-character-bound";
                return false;
            }
            var unique = new SortedSet<int>();
            string[] values = (text ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length > MaximumTokens)
            {
                reasonCode = "sort.region-token-bound";
                return false;
            }
            foreach (string candidate in values)
            {
                if (!int.TryParse(candidate.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
                    row <= 0 || row >= layout.SpecialRow)
                {
                    reasonCode = "sort.region-unsafe";
                    return false;
                }
                unique.Add(row);
            }
            if (unique.Count == 0)
            {
                for (int row = 1; row < layout.SpecialRow; row++) unique.Add(row);
            }
            var copy = new List<int>(unique);
            rows = copy.AsReadOnly();
            reasonCode = "ok";
            return true;
        }
    }
}
