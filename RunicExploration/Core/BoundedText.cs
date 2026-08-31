using System;
using System.Globalization;
using System.Text;

namespace RunicExploration.Core
{
    internal static class BoundedText
    {
        internal const int MaximumLabelCharacters = 64;
        internal const int MaximumQueryCharacters = 48;
        internal const int MaximumRawInspectionCharacters = 256;

        internal static string Label(string value) =>
            Sanitize(value, MaximumLabelCharacters, 1);

        internal static string Query(string value) =>
            Sanitize(value, MaximumQueryCharacters, 1).ToLowerInvariant();

        internal static string Sanitize(string value, int maximumCharacters, int maximumLines)
        {
            maximumCharacters = Math.Max(1, maximumCharacters);
            maximumLines = Math.Max(1, maximumLines);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int inspectionLimit = Math.Min(
                value.Length,
                Math.Min(
                    MaximumRawInspectionCharacters,
                    Math.Max(64, maximumCharacters * 4)));
            var builder = new StringBuilder(Math.Min(maximumCharacters, inspectionLimit));
            int lines = 1;
            bool space = false;
            for (int index = 0; index < inspectionLimit && builder.Length < maximumCharacters; index++)
            {
                char current = value[index];
                if (current == '<') current = '[';
                else if (current == '>') current = ']';
                if (current == '\r') continue;
                if (current == '\n')
                {
                    if (lines >= maximumLines) break;
                    TrimSpace(builder);
                    if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
                    {
                        builder.Append('\n');
                        lines++;
                    }
                    space = false;
                    continue;
                }
                UnicodeCategory category = char.GetUnicodeCategory(current);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format ||
                    category == UnicodeCategory.Surrogate || IsBidi(current)) continue;
                if (current == '\t' || char.IsWhiteSpace(current))
                {
                    if (!space && builder.Length > 0 && builder[builder.Length - 1] != '\n')
                    {
                        builder.Append(' ');
                        space = true;
                    }
                    continue;
                }
                builder.Append(current);
                space = false;
            }
            TrimSpace(builder);
            while (builder.Length > 0 && builder[builder.Length - 1] == '\n') builder.Length--;
            if (inspectionLimit < value.Length || builder.Length == maximumCharacters &&
                value.Length > maximumCharacters)
                AddEllipsis(builder, maximumCharacters);
            return builder.ToString();
        }

        internal static ulong Hash(string value, int maximumCharacters)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                if (value == null) return hash;
                int count = Math.Min(
                    value.Length,
                    Math.Max(0, Math.Min(MaximumRawInspectionCharacters, maximumCharacters)));
                for (int index = 0; index < count; index++)
                {
                    hash ^= value[index];
                    hash *= 1099511628211UL;
                }
                // Length is O(1) and distinguishes oversized values without scanning their tail.
                hash ^= (ulong)value.Length;
                return hash * 1099511628211UL;
            }
        }

        private static bool IsBidi(char value) =>
            value >= '\u202A' && value <= '\u202E' ||
            value >= '\u2066' && value <= '\u2069' ||
            value >= '\u200B' && value <= '\u200F' ||
            value == '\u061C' || value == '\uFEFF';

        private static void TrimSpace(StringBuilder builder)
        {
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ') builder.Length--;
        }

        private static void AddEllipsis(StringBuilder builder, int maximumCharacters)
        {
            if (maximumCharacters == 1)
            {
                if (builder.Length == 0) builder.Append('\u2026');
                else builder[0] = '\u2026';
                return;
            }
            builder.Length = Math.Min(builder.Length, maximumCharacters - 1);
            builder.Append('\u2026');
        }
    }
}
