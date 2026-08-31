using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    /// <summary>Strict, bounded and case-sensitive exact prefab policy for Fermenter adapters.</summary>
    internal sealed class FermenterPrefabPolicy
    {
        private const int MaximumEntries = 128;
        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _denied;

        private FermenterPrefabPolicy(HashSet<string> allowed, HashSet<string> denied)
        {
            _allowed = allowed;
            _denied = denied;
        }

        internal int AllowedCount => _allowed.Count;
        internal int DeniedCount => _denied.Count;

        internal bool Allows(string prefabId) =>
            StockDomainValidation.IsExactPrefabId(prefabId) &&
            _allowed.Contains(prefabId) &&
            !_denied.Contains(prefabId);

        internal static bool TryParse(
            string allowText,
            string denyText,
            out FermenterPrefabPolicy policy,
            out string failure)
        {
            policy = null;
            failure = string.Empty;
            if (!TryParseSet(allowText, "allow", out HashSet<string> allowed, out failure) ||
                !TryParseSet(denyText, "deny", out HashSet<string> denied, out failure))
                return false;
            policy = new FermenterPrefabPolicy(allowed, denied);
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
            string[] entries = (text ?? string.Empty).Split(
                new[] { ',', ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length > MaximumEntries)
            {
                failure = $"The Fermenter {label} list exceeds {MaximumEntries} entries.";
                return false;
            }
            foreach (string raw in entries)
            {
                string value = raw.Trim();
                if (!StockDomainValidation.IsExactPrefabId(value))
                {
                    failure = $"The Fermenter {label} list contains an invalid exact prefab ID.";
                    return false;
                }
                values.Add(value);
            }
            return true;
        }
    }
}
