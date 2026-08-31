using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public readonly struct PlantResourceRequirement
    {
        public PlantResourceRequirement(string resourceId, int amount)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            ResourceId = resourceId;
            Amount = amount;
        }

        public string ResourceId { get; }
        public int Amount { get; }
    }

    /// <summary>Pure multi-resource budget math shared by preview and commit tests.</summary>
    public static class SeedResourceMath
    {
        public static int MaximumPlantings(
            IEnumerable<PlantResourceRequirement> requirements,
            IReadOnlyDictionary<string, int> available)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (available == null) throw new ArgumentNullException(nameof(available));

            var requiredPerPlant = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (PlantResourceRequirement requirement in requirements)
            {
                requiredPerPlant.TryGetValue(requirement.ResourceId, out int current);
                requiredPerPlant[requirement.ResourceId] = AddSaturated(current, requirement.Amount);
            }
            if (requiredPerPlant.Count == 0) return int.MaxValue;

            int maximum = int.MaxValue;
            foreach (KeyValuePair<string, int> requirement in requiredPerPlant)
            {
                available.TryGetValue(requirement.Key, out int count);
                maximum = Math.Min(maximum, Math.Max(0, count) / requirement.Value);
            }
            return maximum;
        }

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;
    }
}
