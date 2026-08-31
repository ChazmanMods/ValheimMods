using System;
using System.Text;

namespace RunicAwareness.Core
{
    internal static class TimerFormatter
    {
        internal const int MaximumDisplaySeconds = 359999;

        internal static int Bucket(float remainingSeconds, AwarenessTimerPrecision precision)
        {
            if (float.IsNaN(remainingSeconds) || float.IsInfinity(remainingSeconds) ||
                remainingSeconds <= 0f)
                return 0;
            int unit = Math.Max(1, (int)precision);
            double bounded = Math.Min(MaximumDisplaySeconds, remainingSeconds);
            return Math.Min(
                MaximumDisplaySeconds,
                checked((int)Math.Ceiling(bounded / unit) * unit));
        }

        internal static void Append(StringBuilder builder, int bucketSeconds)
        {
            int total = Math.Max(0, Math.Min(MaximumDisplaySeconds, bucketSeconds));
            int hours = total / 3600;
            int minutes = total / 60 % 60;
            int seconds = total % 60;
            if (hours > 0)
            {
                builder.Append(hours).Append(':');
                AppendTwoDigits(builder, minutes);
                builder.Append(':');
                AppendTwoDigits(builder, seconds);
                return;
            }
            builder.Append(minutes).Append(':');
            AppendTwoDigits(builder, seconds);
        }

        internal static string Format(int bucketSeconds)
        {
            var builder = new StringBuilder(10);
            Append(builder, bucketSeconds);
            return builder.ToString();
        }

        private static void AppendTwoDigits(StringBuilder builder, int value)
        {
            if (value < 10) builder.Append('0');
            builder.Append(value);
        }
    }
}
