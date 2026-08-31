using System;
using System.Globalization;
using System.Text;

namespace RunicAwareness.Core
{
    internal static class BoundedText
    {
        internal const int MaximumLabelCharacters = 96;

        internal static string Sanitize(string value, int maximumCharacters, int maximumLines)
        {
            maximumCharacters = Math.Max(1, maximumCharacters);
            maximumLines = Math.Max(1, maximumLines);
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(Math.Min(maximumCharacters, value.Length));
            int lines = 1;
            bool lastWasSpace = false;
            int inspectionLimit = Math.Min(
                value.Length,
                Math.Max(256, maximumCharacters * 4));
            for (int index = 0; index < inspectionLimit && builder.Length < maximumCharacters; index++)
            {
                char current = value[index];
                if (current == '<')
                {
                    int close = FindTagEnd(value, index, 64);
                    if (close >= 0)
                    {
                        index = close;
                        continue;
                    }
                    current = '[';
                }
                else if (current == '>')
                {
                    current = ']';
                }

                if (current == '\r') continue;
                if (current == '\n')
                {
                    if (lines >= maximumLines) break;
                    TrimTrailingSpace(builder);
                    if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
                    {
                        builder.Append('\n');
                        lines++;
                    }
                    lastWasSpace = false;
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(current);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format ||
                    IsBidiControl(current))
                    continue;

                if (current == '\t' || char.IsWhiteSpace(current))
                {
                    if (!lastWasSpace && builder.Length > 0 && builder[builder.Length - 1] != '\n')
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }
                    continue;
                }

                builder.Append(current);
                lastWasSpace = false;
            }

            TrimTrailingSpace(builder);
            while (builder.Length > 0 && builder[builder.Length - 1] == '\n') builder.Length--;
            if ((builder.Length == maximumCharacters && value.Length > maximumCharacters) ||
                inspectionLimit < value.Length)
                AddEllipsis(builder, maximumCharacters);
            return builder.ToString();
        }

        internal static string Label(string value) =>
            Sanitize(value, MaximumLabelCharacters, 1);

        internal static ulong HashBounded(string value, int maximumCharacters)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                if (value == null) return hash;
                int count = Math.Min(Math.Max(0, maximumCharacters), value.Length);
                for (int index = 0; index < count; index++)
                {
                    hash ^= value[index];
                    hash *= 1099511628211UL;
                }
                hash ^= (ulong)Math.Min(value.Length, maximumCharacters + 1);
                return hash * 1099511628211UL;
            }
        }

        internal static int CountLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int lines = 1;
            for (int index = 0; index < value.Length; index++)
                if (value[index] == '\n') lines++;
            return lines;
        }

        private static int FindTagEnd(string value, int start, int maximumDistance)
        {
            int end = Math.Min(value.Length, start + maximumDistance + 1);
            for (int index = start + 1; index < end; index++)
                if (value[index] == '>') return index;
            return -1;
        }

        private static bool IsBidiControl(char value) =>
            value >= '\u202A' && value <= '\u202E' ||
            value >= '\u2066' && value <= '\u2069' ||
            value >= '\u200B' && value <= '\u200F' ||
            value == '\u061C' || value == '\uFEFF';

        private static void TrimTrailingSpace(StringBuilder builder)
        {
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ') builder.Length--;
        }

        private static void AddEllipsis(StringBuilder builder, int maximumCharacters)
        {
            const char ellipsis = '\u2026';
            if (maximumCharacters == 1)
            {
                builder[0] = ellipsis;
                return;
            }
            builder.Length = Math.Min(builder.Length, maximumCharacters - 1);
            builder.Append(ellipsis);
        }
    }
}
