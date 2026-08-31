using System;
using System.Collections.Generic;
using System.Reflection;
using RunicAwareness.Core;

namespace RunicAwareness.Integration
{
    /// <summary>
    /// Resolves only one bounded localization key. It bypasses Localization.Localize,
    /// whose expanding parser can copy and cache an arbitrarily large third-party translation.
    /// The direct dictionary lookup returns the existing value; BoundedText then inspects only a
    /// bounded prefix and retains at most one short, sanitized label.
    /// </summary>
    internal static class BoundedLocalization
    {
        internal const int MaximumTokenCharacters = 128;
        private static readonly FieldInfo Translations = ResolveTranslations();

        internal static bool IsSupported => Translations != null;

        internal static string Label(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            if (!TryGetTranslationKey(raw, out string key))
                return BoundedText.Label(raw);

            try
            {
                Localization localization = Localization.instance;
                string translated = key;
                if (localization != null && Translations != null)
                {
                    var values = Translations.GetValue(localization) as
                        Dictionary<string, string>;
                    if (values != null && values.TryGetValue(key, out string existing) &&
                        existing != null)
                        translated = existing;
                }
                return BoundedText.Label(translated);
            }
            catch
            {
                return BoundedText.Label(key);
            }
        }

        internal static bool TryGetTranslationKey(string raw, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 ||
                raw.Length > MaximumTokenCharacters || raw[0] != '$')
                return false;

            int start = 1;
            while (start < raw.Length && raw[start] == '$') start++;
            if (start == raw.Length) return false;
            for (int index = start; index < raw.Length; index++)
            {
                char character = raw[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '_' || character == '-' || character == '.';
                if (!safe) return false;
            }

            key = raw.Substring(start);
            if (key.StartsWith("KEY_", StringComparison.Ordinal))
            {
                key = null;
                return false;
            }
            return true;
        }

        private static FieldInfo ResolveTranslations()
        {
            try
            {
                FieldInfo field = typeof(Localization).GetField(
                    "m_translations",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return field != null && field.FieldType == typeof(Dictionary<string, string>) &&
                       !field.IsStatic
                    ? field
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
