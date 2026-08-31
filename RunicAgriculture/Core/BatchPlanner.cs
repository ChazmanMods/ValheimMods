using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public enum InvalidPositionPolicy
    {
        SkipInvalid = 0,
        BlockConfirmation = 1
    }

    public enum ResourceShortfallPolicy
    {
        TruncatePredictably = 0,
        BlockConfirmation = 1
    }

    public readonly struct PlacementBudget
    {
        public PlacementBudget(int seedActions, int durabilityActions, int staminaActions)
        {
            if (seedActions < 0) throw new ArgumentOutOfRangeException(nameof(seedActions));
            if (durabilityActions < 0) throw new ArgumentOutOfRangeException(nameof(durabilityActions));
            if (staminaActions < 0) throw new ArgumentOutOfRangeException(nameof(staminaActions));
            SeedActions = seedActions;
            DurabilityActions = durabilityActions;
            StaminaActions = staminaActions;
        }

        public int SeedActions { get; }
        public int DurabilityActions { get; }
        public int StaminaActions { get; }
        public int MaximumSuccessfulActions =>
            Math.Min(SeedActions, Math.Min(DurabilityActions, StaminaActions));
    }

    public readonly struct BatchDecision
    {
        public BatchDecision(int index, bool shouldPlace, string reasonCode)
        {
            Index = index;
            ShouldPlace = shouldPlace;
            ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        }

        public int Index { get; }
        public bool ShouldPlace { get; }
        public string ReasonCode { get; }
    }

    public sealed class BatchPlan
    {
        internal BatchPlan(IReadOnlyList<BatchDecision> decisions, bool blocked, string reasonCode)
        {
            Decisions = decisions;
            Blocked = blocked;
            ReasonCode = reasonCode;
        }

        public IReadOnlyList<BatchDecision> Decisions { get; }
        public bool Blocked { get; }
        public string ReasonCode { get; }
        public int SuccessfulCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < Decisions.Count; index++)
                    if (Decisions[index].ShouldPlace) count++;
                return count;
            }
        }
    }

    public static class BatchPlanner
    {
        /// <summary>
        /// Builds visual readiness after the complete configured footprint has been validated.
        /// Valid cells are selected in caller order up to the combined available-resource limit;
        /// remaining valid cells receive NoSeeds so shortage can be rendered red while terrain
        /// failures use a different treatment. Transient
        /// stamina and tool durability are batch-action requirements and never paint ground red.
        /// </summary>
        public static BatchPlan PlanPreview(
            IReadOnlyList<PlacementValidationResult> validations) =>
            PlanPreview(validations, int.MaxValue);

        public static BatchPlan PlanPreview(
            IReadOnlyList<PlacementValidationResult> validations,
            int maximumSelectedCells)
        {
            if (validations == null) throw new ArgumentNullException(nameof(validations));
            if (maximumSelectedCells < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumSelectedCells));
            var decisions = new List<BatchDecision>(validations.Count);
            int selected = 0;
            for (int index = 0; index < validations.Count; index++)
            {
                PlacementValidationResult validation = validations[index];
                bool shouldPlace = validation.IsValid && selected < maximumSelectedCells;
                if (shouldPlace) selected++;
                decisions.Add(new BatchDecision(
                    index,
                    shouldPlace,
                    validation.IsValid && !shouldPlace
                        ? AgricultureReasonCodes.NoSeeds
                        : validation.ReasonCode));
            }
            return new BatchPlan(
                decisions.AsReadOnly(),
                false,
                AgricultureReasonCodes.Valid);
        }

        public static BatchPlan Plan(
            IReadOnlyList<PlacementValidationResult> validations,
            PlacementBudget budget,
            InvalidPositionPolicy invalidPolicy,
            ResourceShortfallPolicy shortfallPolicy)
        {
            if (validations == null) throw new ArgumentNullException(nameof(validations));
            if (!Enum.IsDefined(typeof(InvalidPositionPolicy), invalidPolicy))
                throw new ArgumentOutOfRangeException(nameof(invalidPolicy));
            if (!Enum.IsDefined(typeof(ResourceShortfallPolicy), shortfallPolicy))
                throw new ArgumentOutOfRangeException(nameof(shortfallPolicy));

            int validCount = 0;
            bool hasInvalid = false;
            for (int index = 0; index < validations.Count; index++)
            {
                if (validations[index].IsValid) validCount++;
                else hasInvalid = true;
            }

            if (hasInvalid && invalidPolicy == InvalidPositionPolicy.BlockConfirmation)
                return BlockAll(validations.Count, AgricultureReasonCodes.InvalidBatchBlocked);

            if (validCount > budget.MaximumSuccessfulActions &&
                shortfallPolicy == ResourceShortfallPolicy.BlockConfirmation)
                return BlockAll(validations.Count, AgricultureReasonCodes.CostBatchBlocked);

            var decisions = new List<BatchDecision>(validations.Count);
            int successes = 0;
            for (int index = 0; index < validations.Count; index++)
            {
                PlacementValidationResult validation = validations[index];
                if (!validation.IsValid)
                {
                    decisions.Add(new BatchDecision(index, false, validation.ReasonCode));
                    continue;
                }

                if (successes < budget.MaximumSuccessfulActions)
                {
                    decisions.Add(new BatchDecision(index, true, AgricultureReasonCodes.Valid));
                    successes++;
                    continue;
                }

                decisions.Add(new BatchDecision(index, false, ExhaustedReason(budget, successes)));
            }

            return new BatchPlan(decisions.AsReadOnly(), false, AgricultureReasonCodes.Valid);
        }

        private static BatchPlan BlockAll(int count, string reason)
        {
            var decisions = new List<BatchDecision>(count);
            for (int index = 0; index < count; index++)
                decisions.Add(new BatchDecision(index, false, reason));
            return new BatchPlan(decisions.AsReadOnly(), true, reason);
        }

        private static string ExhaustedReason(PlacementBudget budget, int successes)
        {
            if (successes >= budget.SeedActions) return AgricultureReasonCodes.NoSeeds;
            if (successes >= budget.DurabilityActions) return AgricultureReasonCodes.NoDurability;
            return AgricultureReasonCodes.NoStamina;
        }
    }
}
