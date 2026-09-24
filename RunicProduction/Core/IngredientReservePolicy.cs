using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    /// <summary>
    /// Exact-prefab reserves for replenishment input. Invalid configuration produces
    /// a non-operational policy so a caller that accidentally retains the out value still fails
    /// closed instead of consuming stock.
    /// </summary>
    internal sealed class IngredientReservePolicy
    {
        internal const int MaximumRules = 128;
        internal const int MaximumReserveAmount = 1000000;
        private const int MaximumConfigurationCharacters = 32768;

        private readonly Dictionary<string, int> _protectedAmounts;

        private IngredientReservePolicy(
            bool isValid,
            int defaultProtectedReserve,
            Dictionary<string, int> protectedAmounts)
        {
            IsValid = isValid;
            DefaultProtectedReserve = defaultProtectedReserve;
            _protectedAmounts = protectedAmounts;
        }

        internal bool IsValid { get; }
        internal int DefaultProtectedReserve { get; }
        internal int RuleCount => _protectedAmounts.Count;

        internal static bool TryParse(
            int defaultProtectedReserve,
            string protectedPrefabAmounts,
            out IngredientReservePolicy policy,
            out string failure)
        {
            policy = FailClosed();
            failure = string.Empty;
            if (defaultProtectedReserve < 0 ||
                defaultProtectedReserve > MaximumReserveAmount)
            {
                failure = global::Runic.Localization.RunicText.Format("text_23ab434c5522", MaximumReserveAmount);
                return false;
            }

            string text = protectedPrefabAmounts ?? string.Empty;
            if (text.Length > MaximumConfigurationCharacters)
            {
                failure = global::Runic.Localization.RunicText.Get("text_5a89c652fafa");
                return false;
            }

            var rules = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(text))
            {
                string[] entries = text.Split(new[] { ';' }, StringSplitOptions.None);
                if (entries.Length > MaximumRules)
                {
                    failure = global::Runic.Localization.RunicText.Format("text_c42660082614", MaximumRules);
                    return false;
                }

                foreach (string rawEntry in entries)
                {
                    if (string.IsNullOrWhiteSpace(rawEntry))
                    {
                        failure = global::Runic.Localization.RunicText.Get("text_86e508e094a3");
                        return false;
                    }

                    int separator = rawEntry.IndexOf('=');
                    if (separator <= 0 || separator != rawEntry.LastIndexOf('='))
                    {
                        failure = global::Runic.Localization.RunicText.Get("text_3c74481469ca");
                        return false;
                    }

                    string prefabId = rawEntry.Substring(0, separator).Trim();
                    string amountText = rawEntry.Substring(separator + 1).Trim();
                    if (!StockDomainValidation.IsExactPrefabId(prefabId))
                    {
                        failure = global::Runic.Localization.RunicText.Get("text_b58fe0de0ad2");
                        return false;
                    }
                    if (!TryParseAmount(amountText, out int amount))
                    {
                        failure = global::Runic.Localization.RunicText.Format("text_8340527e30fc", MaximumReserveAmount);
                        return false;
                    }
                    if (rules.ContainsKey(prefabId))
                    {
                        failure = global::Runic.Localization.RunicText.Get("text_d0d680b879f1");
                        return false;
                    }
                    rules.Add(prefabId, amount);
                }
            }

            policy = new IngredientReservePolicy(true, defaultProtectedReserve, rules);
            return true;
        }

        internal bool TryGetProtectedReserve(string prefabId, out int protectedReserve)
        {
            protectedReserve = 0;
            if (!IsValid || !StockDomainValidation.IsExactPrefabId(prefabId)) return false;
            protectedReserve = _protectedAmounts.TryGetValue(prefabId, out int configured)
                ? configured
                : DefaultProtectedReserve;
            return true;
        }

        internal long AvailableAboveReserve(string prefabId, long currentAmount)
        {
            if (currentAmount < 0L ||
                !TryGetProtectedReserve(prefabId, out int protectedReserve)) return 0L;
            long available = currentAmount - protectedReserve;
            return available > 0L ? available : 0L;
        }

        internal bool AllowsConsumption(
            string prefabId,
            long currentAmount,
            long requestedAmount) =>
            requestedAmount > 0L &&
            AvailableAboveReserve(prefabId, currentAmount) >= requestedAmount;

        private static IngredientReservePolicy FailClosed() =>
            new IngredientReservePolicy(
                false,
                MaximumReserveAmount,
                new Dictionary<string, int>(StringComparer.Ordinal));

        private static bool TryParseAmount(string value, out int amount)
        {
            amount = 0;
            if (string.IsNullOrEmpty(value)) return false;
            long parsed = 0L;
            foreach (char character in value)
            {
                if (character < '0' || character > '9') return false;
                parsed = parsed * 10L + (character - '0');
                if (parsed > MaximumReserveAmount) return false;
            }
            amount = (int)parsed;
            return true;
        }
    }
}
