using System;
using System.Collections.Generic;

namespace RunicStorage.Engine;

// A missing selected reference must yield no work, never a whole-inventory fallback.
internal static class QuickStackSelection
{
    internal static List<T> Capture<T>(IReadOnlyList<T> carried, bool single, T selected) where T : class
    {
        var result = new List<T>();
        foreach (T item in carried)
        {
            if (!single) result.Add(item);
            else if (selected != null && ReferenceEquals(item, selected))
            {
                result.Add(item);
                break;
            }
        }
        return result;
    }
}
