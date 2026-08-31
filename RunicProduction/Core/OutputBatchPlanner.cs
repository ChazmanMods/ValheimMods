using System;
using System.Collections.Generic;

namespace RunicProduction.Core
{
    public sealed class OutputBatchRequirement
    {
        internal OutputBatchRequirement(string inputPrefab, int amount)
        {
            InputPrefab = inputPrefab;
            Amount = amount;
        }

        public string InputPrefab { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// Models vanilla Smelter's s_spawnOre/s_spawnAmount batching before the next input is
    /// consumed. A type change flushes the old batch before queuing the new unit.
    /// </summary>
    public static class OutputBatchPlanner
    {
        public static IReadOnlyList<OutputBatchRequirement> Plan(
            string processedInputPrefab,
            int processedAmount,
            string nextInputPrefab)
        {
            if (processedAmount < 0) throw new ArgumentOutOfRangeException(nameof(processedAmount));
            string processed = processedInputPrefab ?? string.Empty;
            string next = nextInputPrefab ?? string.Empty;
            if (processedAmount > 0 && processed.Length == 0)
                throw new ArgumentException(
                    "A processed batch amount requires its stable input prefab.",
                    nameof(processedInputPrefab));

            var result = new List<OutputBatchRequirement>(2);
            if (processedAmount > 0 && string.Equals(processed, next, StringComparison.Ordinal))
            {
                result.Add(new OutputBatchRequirement(processed, checked(processedAmount + 1)));
                return result.AsReadOnly();
            }
            if (processedAmount > 0)
                result.Add(new OutputBatchRequirement(processed, processedAmount));
            if (next.Length > 0)
                result.Add(new OutputBatchRequirement(next, 1));
            return result.AsReadOnly();
        }
    }
}
