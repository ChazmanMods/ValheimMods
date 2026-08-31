using System;
using System.Globalization;

namespace RunicStorage.Engine
{
    // BepInEx Configuration Manager renders enum-backed entries as a dropdown. Keep these
    // identifiers as ordinary color names so players never have to enter or recognize hex.
    internal enum StorageSearchMenuFontColor
    {
        LightGray,
        White,
        Gold,
        Yellow,
        Orange,
        Red,
        Pink,
        Purple,
        Blue,
        Cyan,
        Turquoise,
        Green,
        Lime
    }

    internal readonly struct StorageSearchMenuColor : IEquatable<StorageSearchMenuColor>
    {
        internal StorageSearchMenuColor(byte red, byte green, byte blue, byte alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        internal byte Red { get; }
        internal byte Green { get; }
        internal byte Blue { get; }
        internal byte Alpha { get; }

        public bool Equals(StorageSearchMenuColor other) =>
            Red == other.Red && Green == other.Green &&
            Blue == other.Blue && Alpha == other.Alpha;

        public override bool Equals(object value) =>
            value is StorageSearchMenuColor other && Equals(other);

        public override int GetHashCode() =>
            ((Red * 397) ^ Green) * 397 ^ Blue * 397 ^ Alpha;

    }

    internal readonly struct StorageSearchMenuLayout
    {
        internal StorageSearchMenuLayout(
            int fontSize,
            int controlHeight,
            int itemHeight,
            int actionButtonWidth,
            int preferredWindowWidth,
            int preferredWindowHeight,
            int nonScrollHeight)
        {
            FontSize = fontSize;
            ControlHeight = controlHeight;
            ItemHeight = itemHeight;
            ActionButtonWidth = actionButtonWidth;
            PreferredWindowWidth = preferredWindowWidth;
            PreferredWindowHeight = preferredWindowHeight;
            NonScrollHeight = nonScrollHeight;
        }

        internal int FontSize { get; }
        internal int ControlHeight { get; }
        internal int ItemHeight { get; }
        internal int ActionButtonWidth { get; }
        internal int PreferredWindowWidth { get; }
        internal int PreferredWindowHeight { get; }
        internal int NonScrollHeight { get; }
    }

    internal static class StorageSearchMenuAppearance
    {
        internal const int MinimumFontSize = 10;
        internal const int MaximumFontSize = 32;
        internal const int DefaultFontSize = 14;
        internal const StorageSearchMenuFontColor DefaultFontColor =
            StorageSearchMenuFontColor.LightGray;

        internal static int ClampFontSize(int value) =>
            Math.Max(MinimumFontSize, Math.Min(MaximumFontSize, value));

        internal static StorageSearchMenuColor ResolveFontColor(
            StorageSearchMenuFontColor value)
        {
            switch (value)
            {
                case StorageSearchMenuFontColor.White:
                    return Color(0xFF, 0xFF, 0xFF);
                case StorageSearchMenuFontColor.Gold:
                    return Color(0xFF, 0xD7, 0x00);
                case StorageSearchMenuFontColor.Yellow:
                    return Color(0xFF, 0xFF, 0x00);
                case StorageSearchMenuFontColor.Orange:
                    return Color(0xFF, 0xA5, 0x00);
                case StorageSearchMenuFontColor.Red:
                    return Color(0xFF, 0x40, 0x40);
                case StorageSearchMenuFontColor.Pink:
                    return Color(0xFF, 0xC0, 0xCB);
                case StorageSearchMenuFontColor.Purple:
                    return Color(0xC0, 0x80, 0xFF);
                case StorageSearchMenuFontColor.Blue:
                    return Color(0x40, 0xA0, 0xFF);
                case StorageSearchMenuFontColor.Cyan:
                    return Color(0x00, 0xFF, 0xFF);
                case StorageSearchMenuFontColor.Turquoise:
                    return Color(0x40, 0xE0, 0xD0);
                case StorageSearchMenuFontColor.Green:
                    return Color(0x40, 0xD0, 0x60);
                case StorageSearchMenuFontColor.Lime:
                    return Color(0x00, 0xFF, 0x00);
                case StorageSearchMenuFontColor.LightGray:
                default:
                    // Preserve the original Alt+F menu's readable default appearance.
                    return Color(0xE6, 0xE6, 0xE6);
            }
        }

        // Existing 1.0.0 configurations stored MenuFontColor as an arbitrary hex string. Before
        // the enum entry is bound, normalize that orphaned value to the nearest named palette
        // color. This is migration-only; hex is no longer exposed as a player-facing setting.
        internal static StorageSearchMenuFontColor NormalizeFontColorConfigValue(string value)
        {
            string candidate = (value ?? string.Empty).Trim();
            if (Enum.TryParse(candidate, true, out StorageSearchMenuFontColor named) &&
                Enum.IsDefined(typeof(StorageSearchMenuFontColor), named))
                return named;

            return TryParseLegacyHexColor(candidate, out StorageSearchMenuColor legacy)
                ? FindNearestNamedColor(legacy)
                : DefaultFontColor;
        }

        private static bool TryParseLegacyHexColor(
            string value,
            out StorageSearchMenuColor color)
        {
            string hex = (value ?? string.Empty).Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8)
            {
                color = default;
                return false;
            }

            byte parsedAlpha = 0xFF;
            if (!TryByte(hex, 0, out byte red) ||
                !TryByte(hex, 2, out byte green) ||
                !TryByte(hex, 4, out byte blue) ||
                (hex.Length == 8 && !TryByte(hex, 6, out parsedAlpha)))
            {
                color = default;
                return false;
            }

            color = new StorageSearchMenuColor(red, green, blue, parsedAlpha);
            return true;
        }

        private static StorageSearchMenuFontColor FindNearestNamedColor(
            StorageSearchMenuColor legacy)
        {
            StorageSearchMenuFontColor nearest = DefaultFontColor;
            long nearestDistance = long.MaxValue;
            foreach (StorageSearchMenuFontColor candidate in
                     Enum.GetValues(typeof(StorageSearchMenuFontColor)))
            {
                StorageSearchMenuColor color = ResolveFontColor(candidate);
                long red = legacy.Red - color.Red;
                long green = legacy.Green - color.Green;
                long blue = legacy.Blue - color.Blue;
                long distance = red * red + green * green + blue * blue;
                if (distance >= nearestDistance) continue;
                nearest = candidate;
                nearestDistance = distance;
            }
            return nearest;
        }

        internal static StorageSearchMenuLayout LayoutFor(int configuredFontSize)
        {
            int fontSize = ClampFontSize(configuredFontSize);
            int growth = Math.Max(0, fontSize - DefaultFontSize);
            return new StorageSearchMenuLayout(
                fontSize,
                Math.Max(24, fontSize + 8),
                Math.Max(30, fontSize + 12),
                Math.Max(62, fontSize * 3 + 16),
                560 + growth * 10,
                610 + growth * 8,
                82 + fontSize * 2);
        }

        private static bool TryByte(string value, int start, out byte result) =>
            byte.TryParse(
                value.Substring(start, 2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out result);

        private static StorageSearchMenuColor Color(byte red, byte green, byte blue) =>
            new StorageSearchMenuColor(red, green, blue, 0xFF);
    }
}
