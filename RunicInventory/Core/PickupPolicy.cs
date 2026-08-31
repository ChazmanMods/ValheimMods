using System;
using System.Collections.Generic;

namespace RunicInventory.Core
{
    internal sealed class PickupFilterSet
    {
        internal const int MaximumRules = 128;
        internal const int MaximumInputCharacters = 16384;
        private readonly HashSet<string> _rules;

        private PickupFilterSet(HashSet<string> rules, bool truncated)
        {
            _rules = rules;
            Truncated = truncated;
        }

        internal int Count => _rules.Count;
        internal bool Truncated { get; }
        internal bool Matches(string prefabId, string sharedName) =>
            !string.IsNullOrEmpty(prefabId) && _rules.Contains(prefabId) ||
            !string.IsNullOrEmpty(sharedName) && _rules.Contains(sharedName);

        internal static PickupFilterSet Parse(string text)
        {
            var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool truncated = false;
            if (text != null && text.Length > MaximumInputCharacters)
                return new PickupFilterSet(rules, truncated: true);
            if (!string.IsNullOrEmpty(text))
            {
                string[] values = text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string candidate in values)
                {
                    string value = candidate.Trim();
                    if (value.Length == 0 || value.Length > 128 || ContainsUnsafe(value)) continue;
                    if (rules.Count >= MaximumRules && !rules.Contains(value))
                    {
                        truncated = true;
                        continue;
                    }
                    rules.Add(value);
                }
            }
            return new PickupFilterSet(rules, truncated);
        }

        private static bool ContainsUnsafe(string value)
        {
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]) || value[index] == '<' || value[index] == '>') return true;
            return false;
        }
    }

    internal readonly struct PickupDecision
    {
        internal PickupDecision(
            bool filtered,
            bool encumbered,
            int overflowItems,
            int acceptedItems,
            float resultingWeight)
        {
            Filtered = filtered;
            Encumbered = encumbered;
            OverflowItems = overflowItems;
            AcceptedItems = acceptedItems;
            ResultingWeight = resultingWeight;
        }

        internal bool Filtered { get; }
        internal bool Encumbered { get; }
        internal int OverflowItems { get; }
        internal int AcceptedItems { get; }
        internal float ResultingWeight { get; }
        internal bool Fits => !Filtered && OverflowItems == 0;
    }

    internal static class PickupPlanner
    {
        internal static PickupDecision Evaluate(
            int stack,
            int maximumStack,
            int compatibleStackCapacity,
            int emptyGeneralSlots,
            float unitWeight,
            float currentWeight,
            float maximumCarryWeight,
            bool filtered)
        {
            if (stack < 0) throw new ArgumentOutOfRangeException(nameof(stack));
            if (maximumStack <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStack));
            if (compatibleStackCapacity < 0) throw new ArgumentOutOfRangeException(nameof(compatibleStackCapacity));
            if (emptyGeneralSlots < 0 || emptyGeneralSlots > 128) throw new ArgumentOutOfRangeException(nameof(emptyGeneralSlots));
            if (!FiniteNonNegative(unitWeight) || !FiniteNonNegative(currentWeight) || !FiniteNonNegative(maximumCarryWeight))
                throw new ArgumentOutOfRangeException("Weights must be finite and non-negative.");
            long slotCapacity = (long)emptyGeneralSlots * maximumStack;
            long capacity = Math.Min(int.MaxValue, (long)compatibleStackCapacity + slotCapacity);
            int accepted = filtered ? 0 : (int)Math.Min(stack, capacity);
            int overflow = filtered ? stack : stack - accepted;
            double resulting = currentWeight + (double)accepted * unitWeight;
            float boundedWeight = resulting >= float.MaxValue ? float.MaxValue : (float)resulting;
            return new PickupDecision(filtered, boundedWeight > maximumCarryWeight, overflow, accepted, boundedWeight);
        }

        private static bool FiniteNonNegative(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }
}
