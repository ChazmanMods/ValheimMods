using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunicProduction.Core
{
    /// <summary>Strict, bounded, deny-first exact-prefab policy shared by stock adapters.</summary>
    internal sealed class StockStationPrefabPolicy
    {
        private const int MaximumEntries = 128;
        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _denied;
        private readonly ReadOnlyCollection<string> _effectiveAllowed;

        private StockStationPrefabPolicy(HashSet<string> allowed, HashSet<string> denied)
        {
            _allowed = allowed;
            _denied = denied;
            _effectiveAllowed = allowed
                .Where(value => !denied.Contains(value))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        internal int AllowedCount => _allowed.Count;
        internal int DeniedCount => _denied.Count;
        internal IReadOnlyList<string> EffectiveAllowedIds => _effectiveAllowed;
        internal bool Allows(string prefabId) =>
            StockDomainValidation.IsExactPrefabId(prefabId) &&
            _allowed.Contains(prefabId) &&
            !_denied.Contains(prefabId);

        internal static bool TryParse(
            string allowedText,
            string deniedText,
            out StockStationPrefabPolicy policy,
            out string failure)
        {
            policy = null;
            failure = string.Empty;
            if (!TryParseSet(allowedText, "allow", out HashSet<string> allowed, out failure) ||
                !TryParseSet(deniedText, "deny", out HashSet<string> denied, out failure))
                return false;
            policy = new StockStationPrefabPolicy(allowed, denied);
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
            if (string.IsNullOrWhiteSpace(text)) return true;
            string[] entries = text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.None);
            if (entries.Length > MaximumEntries)
            {
                failure = $"The stock station {label} list exceeds {MaximumEntries} entries.";
                return false;
            }
            foreach (string entry in entries)
            {
                string value = entry.Trim();
                if (!StockDomainValidation.IsExactPrefabId(value))
                {
                    failure = $"The stock station {label} list contains an invalid exact prefab ID.";
                    return false;
                }
                if (!values.Add(value))
                {
                    failure = $"The stock station {label} list contains a duplicate exact prefab ID.";
                    return false;
                }
            }
            return true;
        }
    }
}
