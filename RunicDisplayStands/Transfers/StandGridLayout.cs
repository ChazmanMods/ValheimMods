using System;

namespace RunicDisplayStands
{
    internal static class StandGridLayout
    {
        internal static int Width(int slots) => Math.Min(8, Math.Max(1, slots));
        internal static int Height(int slots) => Math.Max(1, (slots + Width(slots) - 1) / Width(slots));
        internal static Vector2i Position(int index, int width) => new Vector2i(index % width, index / width);
    }
}
