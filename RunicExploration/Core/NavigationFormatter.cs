using System;
using System.Globalization;
using System.Text;

namespace RunicExploration.Core
{
    internal static class NavigationFormatter
    {
        private static readonly string[] Directions =
        {
            "N", "NE", "E", "SE", "S", "SW", "W", "NW"
        };

        internal static string FormatSelected(
            KnownPinRecord pin,
            float playerX,
            float playerY,
            float playerZ)
        {
            if (!Finite(playerX) || !Finite(playerY) || !Finite(playerZ)) return string.Empty;
            float deltaX = pin.X - playerX;
            float deltaZ = pin.Z - playerZ;
            double distance = Math.Sqrt((double)deltaX * deltaX + (double)deltaZ * deltaZ);
            string direction = Direction(deltaX, deltaZ);
            var builder = new StringBuilder(256);
            builder.Append(global::Runic.Localization.RunicText.Get("text_cffc013a4f0b")).Append(pin.Label)
                .Append(global::Runic.Localization.RunicText.Get("text_6eef4ae0d04d")).Append(FormatDistance(distance))
                .Append(" ").Append(direction);
            float vertical = pin.Y - playerY;
            if (Math.Abs(vertical) >= 10f)
                builder.Append(global::Runic.Localization.RunicText.Get("text_0ed1c3c629c1"))
                    .Append(vertical > 0f ? "+" : string.Empty)
                    .Append(vertical.ToString("0", CultureInfo.InvariantCulture)).Append(global::Runic.Localization.RunicText.Get("text_506df2a2a61a"));
            if (pin.Category == KnownPinCategory.Tombstone)
                builder.Append(global::Runic.Localization.RunicText.Get("text_7ffb306c90a6"));
            return BoundedText.Sanitize(builder.ToString(), 384, 3);
        }

        internal static string FormatSailing(
            string speed,
            bool sailUp,
            float windFactor,
            float windIntensity,
            string currentBiome,
            double? selectedDistance)
        {
            if (!Finite(windFactor) || !Finite(windIntensity)) return string.Empty;
            var builder = new StringBuilder(256);
            builder.Append(global::Runic.Localization.RunicText.Get("text_4fae6d01bd98")).Append(BoundedText.Label(speed))
                .Append(sailUp ? global::Runic.Localization.RunicText.Get("text_203cee3a6a56") : global::Runic.Localization.RunicText.Get("text_6e7c9b5fb958"))
                .Append(global::Runic.Localization.RunicText.Get("text_12b6ee864037"))
                .Append(Math.Max(0f, Math.Min(1f, windFactor))
                    .ToString("0%", CultureInfo.InvariantCulture))
                .Append(global::Runic.Localization.RunicText.Get("text_b0e1b6246549"))
                .Append(Math.Max(0f, Math.Min(1f, windIntensity))
                    .ToString("0%", CultureInfo.InvariantCulture))
                .Append(global::Runic.Localization.RunicText.Get("text_650f1576a4ce")).Append(BoundedText.Label(currentBiome));
            if (selectedDistance.HasValue && !double.IsNaN(selectedDistance.Value) &&
                !double.IsInfinity(selectedDistance.Value) && selectedDistance.Value >= 0d)
                builder.Append(global::Runic.Localization.RunicText.Get("text_0476ff504597"))
                    .Append(FormatDistance(selectedDistance.Value));
            return BoundedText.Sanitize(builder.ToString(), 384, 3);
        }

        internal static string Direction(float deltaX, float deltaZ)
        {
            if (!Finite(deltaX) || !Finite(deltaZ)) return string.Empty;
            if (Math.Abs(deltaX) < 0.001f && Math.Abs(deltaZ) < 0.001f) return "here";
            double degrees = Math.Atan2(deltaX, deltaZ) * 180d / Math.PI;
            if (degrees < 0d) degrees += 360d;
            int index = (int)Math.Floor((degrees + 22.5d) / 45d) & 7;
            return Directions[index];
        }

        internal static string FormatDistance(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0d)
                return "unknown";
            if (meters < 1000d)
                return Math.Round(meters, MidpointRounding.AwayFromZero)
                    .ToString("0", CultureInfo.InvariantCulture) + global::Runic.Localization.RunicText.Get("text_506df2a2a61a");
            return (meters / 1000d).ToString(
                       meters < 10000d ? "0.0" : "0",
                       CultureInfo.InvariantCulture) + global::Runic.Localization.RunicText.Get("text_408b05c97145");
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
