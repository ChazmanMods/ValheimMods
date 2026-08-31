using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicCrafting.Domain
{
    public sealed class ExactMaterialPlanner
    {
        public const int MaximumRequirements = 256;
        public const int MaximumSources = 129;

        public bool TryPlan(
            IEnumerable<MaterialRequirement> requirements,
            IEnumerable<MaterialSourceSnapshot> sources,
            out MaterialPlan plan,
            out string denialReason)
        {
            plan = null;
            denialReason = string.Empty;
            if (requirements == null || sources == null)
            {
                denialReason = "invalid-request";
                return false;
            }

            List<MaterialRequirement> canonicalRequirements;
            List<MaterialSourceSnapshot> orderedSources;
            try
            {
                canonicalRequirements = CanonicalizeRequirements(requirements);
                orderedSources = sources.Take(MaximumSources + 1).ToList();
            }
            catch (Exception)
            {
                denialReason = "invalid-request";
                return false;
            }

            if (canonicalRequirements.Count == 0 || orderedSources.Count > MaximumSources)
            {
                denialReason = orderedSources.Count > MaximumSources ? "source-limit" : "no-requirements";
                return false;
            }

            if (orderedSources.Any(source => source == null) ||
                orderedSources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != orderedSources.Count)
            {
                denialReason = "invalid-sources";
                return false;
            }

            orderedSources.Sort(CompareSources);
            var lines = new List<MaterialPlanLine>();
            foreach (MaterialRequirement requirement in canonicalRequirements)
            {
                int remaining = requirement.Quantity;
                foreach (MaterialSourceSnapshot source in orderedSources)
                {
                    int available = source.Available(requirement.ResourceId);
                    if (available <= 0) continue;
                    int take = Math.Min(available, remaining);
                    lines.Add(new MaterialPlanLine(source.SourceId, requirement.ResourceId, take));
                    remaining -= take;
                    if (remaining == 0) break;
                }

                if (remaining != 0)
                {
                    denialReason = "missing:" + requirement.ResourceId;
                    return false;
                }
            }

            plan = new MaterialPlan(canonicalRequirements, lines);
            return true;
        }

        private static List<MaterialRequirement> CanonicalizeRequirements(
            IEnumerable<MaterialRequirement> requirements)
        {
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int count = 0;
            foreach (MaterialRequirement requirement in requirements)
            {
                if (++count > MaximumRequirements || requirement == null)
                    throw new ArgumentException("Invalid requirement list.", nameof(requirements));
                totals.TryGetValue(requirement.ResourceId, out int existing);
                totals[requirement.ResourceId] = checked(existing + requirement.Quantity);
            }

            return totals.Select(pair => new MaterialRequirement(pair.Key, pair.Value)).ToList();
        }

        private static int CompareSources(MaterialSourceSnapshot left, MaterialSourceSnapshot right)
        {
            int comparison = left.Kind.CompareTo(right.Kind);
            if (comparison != 0) return comparison;
            comparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (comparison != 0) return comparison;
            return StringComparer.Ordinal.Compare(left.SourceId, right.SourceId);
        }
    }
}
