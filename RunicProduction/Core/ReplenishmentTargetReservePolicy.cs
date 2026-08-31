using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    internal enum ReplenishmentDemandState
    {
        Invalid = 0,
        ExemplarAbsent = 1,
        ReserveSatisfied = 2,
        ProductionNeeded = 3
    }

    /// <summary>
    /// Read-only demand for one exact output prefab.  CurrentCount deliberately excludes
    /// in-flight station work: a physical exemplar must remain in the destination even when
    /// already-committed output would otherwise make the projected count positive.
    /// </summary>
    internal readonly struct ReplenishmentTargetDemand
    {
        internal ReplenishmentTargetDemand(
            ReplenishmentDemandState state,
            int reserve,
            long currentCount,
            long inFlightCount)
        {
            if (!Enum.IsDefined(typeof(ReplenishmentDemandState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (reserve < 0 || currentCount < 0L || inFlightCount < 0L ||
                currentCount > long.MaxValue - inFlightCount)
                throw new ArgumentOutOfRangeException(nameof(currentCount));
            State = state;
            Reserve = reserve;
            CurrentCount = currentCount;
            InFlightCount = inFlightCount;
        }

        internal ReplenishmentDemandState State { get; }
        internal int Reserve { get; }
        internal long CurrentCount { get; }
        internal long InFlightCount { get; }
        internal long ProjectedCount => CurrentCount + InFlightCount;
        internal bool NeedsProduction => State == ReplenishmentDemandState.ProductionNeeded;
    }

    /// <summary>
    /// Exact-prefab output reserves for exemplar-driven replenishment.  This policy is separate
    /// from ingredient reserves: ingredients protect donor stock, while this policy decides
    /// whether another complete producer batch may start for a destination.
    /// </summary>
    internal sealed class ReplenishmentTargetReservePolicy
    {
        internal const int MaximumRules = 128;
        internal const int MaximumReserveAmount = 1000000;
        private const int MaximumConfigurationCharacters = 32768;

        private readonly Dictionary<string, int> _targetAmounts;

        private ReplenishmentTargetReservePolicy(
            bool isValid,
            int defaultTargetReserve,
            Dictionary<string, int> targetAmounts)
        {
            IsValid = isValid;
            DefaultTargetReserve = defaultTargetReserve;
            _targetAmounts = targetAmounts;
        }

        internal bool IsValid { get; }
        internal int DefaultTargetReserve { get; }
        internal int RuleCount => _targetAmounts.Count;

        internal static bool TryParse(
            int defaultTargetReserve,
            string exactPrefabReserves,
            out ReplenishmentTargetReservePolicy policy,
            out string failure)
        {
            policy = FailClosed();
            failure = string.Empty;
            if (defaultTargetReserve < 1 ||
                defaultTargetReserve > MaximumReserveAmount)
            {
                failure =
                    $"DefaultReserve must be between 1 and {MaximumReserveAmount}.";
                return false;
            }

            string text = exactPrefabReserves ?? string.Empty;
            if (text.Length > MaximumConfigurationCharacters)
            {
                failure = "The replenishment reserve configuration is too long.";
                return false;
            }

            var rules = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(text))
            {
                string[] entries = text.Split(new[] { ';' }, StringSplitOptions.None);
                if (entries.Length > MaximumRules)
                {
                    failure =
                        $"The replenishment reserve configuration exceeds {MaximumRules} rules.";
                    return false;
                }

                foreach (string rawEntry in entries)
                {
                    if (string.IsNullOrWhiteSpace(rawEntry))
                    {
                        failure =
                            "The replenishment reserve configuration contains an empty rule.";
                        return false;
                    }
                    int separator = rawEntry.IndexOf('=');
                    if (separator <= 0 || separator != rawEntry.LastIndexOf('='))
                    {
                        failure =
                            "Each replenishment reserve rule must contain exactly one '=' separator.";
                        return false;
                    }
                    string prefabId = rawEntry.Substring(0, separator).Trim();
                    string amountText = rawEntry.Substring(separator + 1).Trim();
                    if (!StockDomainValidation.IsExactPrefabId(prefabId))
                    {
                        failure =
                            "The replenishment reserve configuration contains an invalid exact prefab ID.";
                        return false;
                    }
                    if (!TryParseAmount(amountText, out int amount))
                    {
                        failure =
                            $"Replenishment reserves must be decimal integers between 1 and {MaximumReserveAmount}.";
                        return false;
                    }
                    if (rules.ContainsKey(prefabId))
                    {
                        failure =
                            "The replenishment reserve configuration contains a duplicate exact prefab ID.";
                        return false;
                    }
                    rules.Add(prefabId, amount);
                }
            }

            policy = new ReplenishmentTargetReservePolicy(
                true,
                defaultTargetReserve,
                rules);
            return true;
        }

        internal bool TryGetTargetReserve(string prefabId, out int targetReserve)
        {
            targetReserve = 0;
            if (!IsValid || !StockDomainValidation.IsExactPrefabId(prefabId)) return false;
            targetReserve = _targetAmounts.TryGetValue(prefabId, out int configured)
                ? configured
                : DefaultTargetReserve;
            return true;
        }

        internal bool TryEvaluate(
            string prefabId,
            long currentExactCount,
            long inFlightOutputCount,
            out ReplenishmentTargetDemand demand)
        {
            demand = default;
            if (currentExactCount < 0L || inFlightOutputCount < 0L ||
                currentExactCount > long.MaxValue - inFlightOutputCount ||
                !TryGetTargetReserve(prefabId, out int reserve)) return false;

            ReplenishmentDemandState state = currentExactCount == 0L
                ? ReplenishmentDemandState.ExemplarAbsent
                : currentExactCount + inFlightOutputCount >= reserve
                    ? ReplenishmentDemandState.ReserveSatisfied
                    : ReplenishmentDemandState.ProductionNeeded;
            demand = new ReplenishmentTargetDemand(
                state,
                reserve,
                currentExactCount,
                inFlightOutputCount);
            return true;
        }

        internal bool NeedsProduction(
            string prefabId,
            long currentExactCount,
            long inFlightOutputCount,
            out int reserve)
        {
            reserve = 0;
            if (!TryEvaluate(
                    prefabId,
                    currentExactCount,
                    inFlightOutputCount,
                    out ReplenishmentTargetDemand demand)) return false;
            reserve = demand.Reserve;
            return demand.NeedsProduction;
        }

        private static ReplenishmentTargetReservePolicy FailClosed() =>
            new ReplenishmentTargetReservePolicy(
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
                parsed = parsed * 10L + character - '0';
                if (parsed > MaximumReserveAmount) return false;
            }
            if (parsed < 1L) return false;
            amount = (int)parsed;
            return true;
        }
    }
}
