using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunicProduction.Core
{
    internal sealed class NearbyIngredientSourceStock
    {
        private readonly IReadOnlyDictionary<string, int> _exactCounts;

        internal NearbyIngredientSourceStock(
            string sourceId,
            IReadOnlyDictionary<string, int> exactCounts)
        {
            SourceId = StockDomainValidation.RequireStableText(
                sourceId, nameof(sourceId), 200);
            if (exactCounts == null) throw new ArgumentNullException(nameof(exactCounts));
            var copy = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in exactCounts)
            {
                if (!StockDomainValidation.IsExactPrefabId(pair.Key) || pair.Value < 0)
                    throw new ArgumentException(
                        "Nearby source counts must use exact prefab IDs and non-negative amounts.",
                        nameof(exactCounts));
                if (pair.Value != 0) copy.Add(pair.Key, pair.Value);
            }
            if (copy.Count > ReplenishmentTargetAuthorization.MaximumRequirements)
                throw new ArgumentOutOfRangeException(
                    nameof(exactCounts),
                    "Nearby source stock is bounded to the selected recipe requirements.");
            _exactCounts = new ReadOnlyDictionary<string, int>(copy);
        }

        internal string SourceId { get; }
        internal IReadOnlyDictionary<string, int> ExactCounts => _exactCounts;

        internal int Count(string prefabId) =>
            _exactCounts.TryGetValue(prefabId, out int count) ? count : 0;
    }

    internal sealed class NearbyIngredientStagingContribution
    {
        internal NearbyIngredientStagingContribution(
            int sourceIndex,
            string sourceId,
            string prefabId,
            int amount)
        {
            if (sourceIndex < 0 || sourceIndex >= NearbyIngredientStagingPlanner.HardMaximumSources)
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            SourceId = StockDomainValidation.RequireStableText(
                sourceId, nameof(sourceId), 200);
            if (!StockDomainValidation.IsExactPrefabId(prefabId))
                throw new ArgumentException("An exact ingredient prefab is required.", nameof(prefabId));
            if (amount <= 0 || amount > StockDomainValidation.MaximumItemAmount)
                throw new ArgumentOutOfRangeException(nameof(amount));
            SourceIndex = sourceIndex;
            PrefabId = prefabId;
            Amount = amount;
        }

        internal int SourceIndex { get; }
        internal string SourceId { get; }
        internal string PrefabId { get; }
        internal int Amount { get; }
    }

    internal sealed class NearbyIngredientStagingPlan
    {
        private readonly ReadOnlyCollection<ReplenishmentRequirement> _requirements;
        private readonly ReadOnlyCollection<NearbyIngredientStagingContribution> _contributions;

        internal NearbyIngredientStagingPlan(
            IEnumerable<ReplenishmentRequirement> requirements,
            IEnumerable<NearbyIngredientStagingContribution> contributions,
            int nextTransferAmount)
        {
            var requirementCopy = requirements?.ToList() ??
                                  throw new ArgumentNullException(nameof(requirements));
            var contributionCopy = contributions?.ToList() ??
                                   throw new ArgumentNullException(nameof(contributions));
            if (requirementCopy.Count == 0 || requirementCopy.Any(value => value == null) ||
                contributionCopy.Count == 0 || contributionCopy.Any(value => value == null))
                throw new ArgumentException("A staging plan requires exact requirements and contributions.");
            if (requirementCopy.Count > ReplenishmentTargetAuthorization.MaximumRequirements ||
                contributionCopy.Count > NearbyIngredientStagingPlanner.HardMaximumContributions ||
                requirementCopy.Select(value => value.PrefabId)
                    .Distinct(StringComparer.Ordinal).Count() != requirementCopy.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(requirements),
                    "A staging plan exceeds its exact requirement/contribution bounds or is not normalized.");
            if (nextTransferAmount <= 0 ||
                nextTransferAmount > NearbyIngredientStagingPlanner.HardMaximumPullAmount ||
                nextTransferAmount > contributionCopy[0].Amount)
                throw new ArgumentOutOfRangeException(nameof(nextTransferAmount));
            _requirements = new ReadOnlyCollection<ReplenishmentRequirement>(requirementCopy);
            _contributions = new ReadOnlyCollection<NearbyIngredientStagingContribution>(contributionCopy);
            NextTransferAmount = nextTransferAmount;
        }

        internal IReadOnlyList<ReplenishmentRequirement> Requirements => _requirements;
        internal IReadOnlyList<NearbyIngredientStagingContribution> Contributions => _contributions;
        internal NearbyIngredientStagingContribution NextContribution => _contributions[0];
        internal int NextTransferAmount { get; }
    }

    /// <summary>
    /// Produces a bounded, deterministic staging allocation from already-authorized source
    /// snapshots. Requirements are normalized by exact prefab. Every source independently keeps
    /// its configured reserve, and the Input chest is filled to reserve plus one complete batch.
    /// Source order is caller-owned (the runtime supplies distance then numeric ZDOID order).
    /// </summary>
    internal static class NearbyIngredientStagingPlanner
    {
        internal const int HardMaximumSources = 64;
        internal const int HardMaximumContributions = 1024;
        internal const int HardMaximumPullAmount = 10;

        internal static bool TryPlan(
            IEnumerable<ReplenishmentRequirement> requirements,
            IReadOnlyDictionary<string, int> inputExactCounts,
            IReadOnlyList<NearbyIngredientSourceStock> orderedSources,
            IngredientReservePolicy reserves,
            int pullBatchSize,
            out NearbyIngredientStagingPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (inputExactCounts == null || orderedSources == null || reserves == null ||
                !reserves.IsValid || pullBatchSize <= 0 ||
                pullBatchSize > HardMaximumPullAmount ||
                orderedSources.Count == 0 || orderedSources.Count > HardMaximumSources)
            {
                failure = "nearby-stage.invalid-input";
                return false;
            }
            foreach (KeyValuePair<string, int> pair in inputExactCounts)
                if (!StockDomainValidation.IsExactPrefabId(pair.Key) || pair.Value < 0)
                {
                    failure = "nearby-stage.input-count-invalid";
                    return false;
                }
            if (!TryNormalize(requirements, out List<ReplenishmentRequirement> normalized, out failure))
                return false;

            var remaining = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (ReplenishmentRequirement requirement in normalized)
            {
                if (!reserves.TryGetProtectedReserve(
                        requirement.PrefabId, out int inputReserve))
                {
                    failure = "nearby-stage.reserve-policy-unavailable";
                    return false;
                }
                inputExactCounts.TryGetValue(requirement.PrefabId, out int inputCount);
                if (inputCount < 0)
                {
                    failure = "nearby-stage.input-count-invalid";
                    return false;
                }
                long target = (long)inputReserve + requirement.Amount;
                remaining[requirement.PrefabId] = Math.Max(0L, target - inputCount);
            }
            if (remaining.Values.All(value => value == 0L))
            {
                failure = "nearby-stage.input-already-sufficient";
                return false;
            }

            var contributions = new List<NearbyIngredientStagingContribution>();
            var seenSources = new HashSet<string>(StringComparer.Ordinal);
            for (int sourceIndex = 0; sourceIndex < orderedSources.Count; sourceIndex++)
            {
                NearbyIngredientSourceStock source = orderedSources[sourceIndex];
                if (source == null || !seenSources.Add(source.SourceId))
                {
                    failure = "nearby-stage.source-invalid-or-duplicate";
                    return false;
                }
                foreach (ReplenishmentRequirement requirement in normalized)
                {
                    long deficit = remaining[requirement.PrefabId];
                    if (deficit == 0L) continue;
                    long available = reserves.AvailableAboveReserve(
                        requirement.PrefabId, source.Count(requirement.PrefabId));
                    int take = (int)Math.Min(deficit, Math.Min(
                        available, StockDomainValidation.MaximumItemAmount));
                    if (take <= 0) continue;
                    contributions.Add(new NearbyIngredientStagingContribution(
                        sourceIndex, source.SourceId, requirement.PrefabId, take));
                    if (contributions.Count > HardMaximumContributions)
                    {
                        failure = "nearby-stage.contribution-limit-exceeded";
                        return false;
                    }
                    remaining[requirement.PrefabId] = deficit - take;
                }
            }

            KeyValuePair<string, long> missing = remaining.FirstOrDefault(pair => pair.Value != 0L);
            if (missing.Value != 0L)
            {
                failure = "nearby-stage.aggregate-insufficient:" + missing.Key;
                return false;
            }
            if (contributions.Count == 0)
            {
                failure = "nearby-stage.no-positive-contribution";
                return false;
            }
            int next = Math.Min(contributions[0].Amount, pullBatchSize);
            plan = new NearbyIngredientStagingPlan(normalized, contributions, next);
            return true;
        }

        private static bool TryNormalize(
            IEnumerable<ReplenishmentRequirement> requirements,
            out List<ReplenishmentRequirement> normalized,
            out string failure)
        {
            normalized = new List<ReplenishmentRequirement>();
            failure = string.Empty;
            var amounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            int count = 0;
            if (requirements != null)
                foreach (ReplenishmentRequirement requirement in requirements)
                {
                    if (requirement == null || ++count > ReplenishmentTargetAuthorization.MaximumRequirements)
                    {
                        failure = "nearby-stage.requirements-invalid-or-unbounded";
                        return false;
                    }
                    amounts.TryGetValue(requirement.PrefabId, out long previous);
                    long sum = previous + requirement.Amount;
                    if (sum <= 0L || sum > StockDomainValidation.MaximumItemAmount)
                    {
                        failure = "nearby-stage.requirement-amount-out-of-range";
                        return false;
                    }
                    amounts[requirement.PrefabId] = sum;
                }
            if (amounts.Count == 0)
            {
                failure = "nearby-stage.requirements-empty";
                return false;
            }
            foreach (KeyValuePair<string, long> pair in amounts)
                normalized.Add(new ReplenishmentRequirement(pair.Key, (int)pair.Value));
            return true;
        }
    }
}
