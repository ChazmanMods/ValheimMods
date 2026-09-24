using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    /// <summary>Strict, bounded exact-ID policy for optional CookingStation adapters.</summary>
    internal sealed class CookingStationPrefabPolicy
    {
        private const int MaximumEntries = 128;
        private const int MaximumIdentifierCharacters = 128;
        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _denied;

        private CookingStationPrefabPolicy(HashSet<string> allowed, HashSet<string> denied)
        {
            _allowed = allowed;
            _denied = denied;
        }

        internal int AllowedCount => _allowed.Count;
        internal int DeniedCount => _denied.Count;

        internal bool Allows(string prefabId) =>
            IsValidIdentifier(prefabId) &&
            _allowed.Contains(prefabId) &&
            !_denied.Contains(prefabId);

        internal static bool TryParse(
            string allowText,
            string denyText,
            out CookingStationPrefabPolicy policy,
            out string failure)
        {
            policy = null;
            failure = string.Empty;
            if (!TryParseSet(allowText, "allow", out HashSet<string> allowed, out failure) ||
                !TryParseSet(denyText, "deny", out HashSet<string> denied, out failure))
                return false;
            policy = new CookingStationPrefabPolicy(allowed, denied);
            return true;
        }

        private static bool TryParseSet(
            string text,
            string label,
            out HashSet<string> values,
            out string failure)
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            failure = string.Empty;
            string[] entries = (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length > MaximumEntries)
            {
                failure = global::Runic.Localization.RunicText.Format("text_cce80544864d", label, MaximumEntries);
                return false;
            }
            foreach (string entry in entries)
            {
                string value = entry.Trim();
                if (!IsValidIdentifier(value))
                {
                    failure = global::Runic.Localization.RunicText.Format("text_c6a7a7e76ccc", label);
                    return false;
                }
                values.Add(value);
            }
            return true;
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumIdentifierCharacters) return false;
            foreach (char character in value)
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'))
                    return false;
            return true;
        }
    }
}
