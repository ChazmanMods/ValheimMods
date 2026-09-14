using System;
using System.Globalization;

namespace RunicClock
{
    public enum ClockAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

    // Pure formatting and geometry: no game state is changed.
    internal static class ClockModel
    {
        internal static bool TryGameTime(double dayFraction, bool use24Hour, out string text)
        {
            text = string.Empty;
            if (double.IsNaN(dayFraction) || double.IsInfinity(dayFraction) || dayFraction < 0 || dayFraction > 1) return false;
            int minute = dayFraction == 1 ? 0 : Math.Min(1439, (int)Math.Floor(dayFraction * 1440));
            text = FormatTime(minute / 60, minute % 60, use24Hour);
            return true;
        }

        internal static string FormatTime(int hour, int minute, bool use24Hour)
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) throw new ArgumentOutOfRangeException();
            if (use24Hour) return hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
            int twelveHour = hour % 12;
            return (twelveHour == 0 ? 12 : twelveHour).ToString(CultureInfo.InvariantCulture) + ":" +
                minute.ToString("00", CultureInfo.InvariantCulture) + (hour < 12 ? " AM" : " PM");
        }

        internal static double Clamp(double value, double min, double max, double fallback) =>
            double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

        internal static (double X, double Y) Position(ClockAnchor anchor, double safeX, double safeY,
            double safeWidth, double safeHeight, double width, double height, double offsetX, double offsetY)
        {
            int index = (int)anchor;
            if (index < 0 || index > 8) index = (int)ClockAnchor.TopCenter;
            double roomX = Math.Max(0, safeWidth - width), roomY = Math.Max(0, safeHeight - height);
            double x = safeX + roomX * (index % 3) / 2 + Clamp(offsetX, -4096, 4096, 0);
            double y = safeY + roomY * (index / 3) / 2 + Clamp(offsetY, -4096, 4096, 0);
            return (Math.Max(safeX, Math.Min(safeX + roomX, x)), Math.Max(safeY, Math.Min(safeY + roomY, y)));
        }
    }
}
