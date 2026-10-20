using System;

namespace RunicSentinel.Core
{
    internal readonly struct CommandWorkspaceLayout
    {
        public const float Divider = 12;
        public readonly float Width, Height, ConsoleWidth, GuideWidth, TopHeight, CatalogHeight;
        public CommandWorkspaceLayout(float width, float height, float scale, float columns, float rows)
        {
            scale = Clamp(scale, .8f, 1.5f);
            float minColumn = 280 * scale, minTop = 360 * scale, minCatalog = 200 * scale;
            Width = Math.Max(width, minColumn * 2 + Divider + 160 * scale);
            Height = Math.Max(height, minTop + minCatalog + Divider + 160 * scale);
            ConsoleWidth = Clamp((Width - Divider) * columns, minColumn, Width - Divider - minColumn);
            GuideWidth = Width - Divider - ConsoleWidth;
            TopHeight = Clamp((Height - Divider) * rows, minTop, Height - Divider - minCatalog);
            CatalogHeight = Height - Divider - TopHeight;
        }
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
