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
            builder.Append("LAST KNOWN PIN: ").Append(pin.Label)
                .Append("\nStraight-line: ").Append(FormatDistance(distance))
                .Append(" ").Append(direction);
            float vertical = pin.Y - playerY;
            if (Math.Abs(vertical) >= 10f)
                builder.Append(" | elevation ")
                    .Append(vertical > 0f ? "+" : string.Empty)
                    .Append(vertical.ToString("0", CultureInfo.InvariantCulture)).Append(" m");
            if (pin.Category == KnownPinCategory.Tombstone)
                builder.Append("\nTopology warning: route, shore, and passability are not inferred; no remote recovery.");
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
            builder.Append("LIVE LOCAL SHIP: ").Append(BoundedText.Label(speed))
                .Append(sailUp ? " | sail up" : " | sail down")
                .Append("\nWind power: ")
                .Append(Math.Max(0f, Math.Min(1f, windFactor))
                    .ToString("0%", CultureInfo.InvariantCulture))
                .Append(" | intensity ")
                .Append(Math.Max(0f, Math.Min(1f, windIntensity))
                    .ToString("0%", CultureInfo.InvariantCulture))
                .Append(" | current biome ").Append(BoundedText.Label(currentBiome));
            if (selectedDistance.HasValue && !double.IsNaN(selectedDistance.Value) &&
                !double.IsInfinity(selectedDistance.Value) && selectedDistance.Value >= 0d)
                builder.Append("\nSelected known pin: ")
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
                    .ToString("0", CultureInfo.InvariantCulture) + " m";
            return (meters / 1000d).ToString(
                       meters < 10000d ? "0.0" : "0",
                       CultureInfo.InvariantCulture) + " km";
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
