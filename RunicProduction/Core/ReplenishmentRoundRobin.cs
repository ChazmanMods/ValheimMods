using System;

namespace RunicProduction.Core
{
    internal enum ReplenishmentTargetStopReason
    {
        Ready = 0,
        ExemplarAbsent = 1,
        ProducerIneligible = 2,
        IngredientsInsufficient = 3,
        CompleteOutputDoesNotFit = 4
    }

    /// <summary>One tick's read-only eligibility observation for an authorized target.</summary>
    internal readonly struct ReplenishmentTargetReadiness
    {
        internal ReplenishmentTargetReadiness(
            bool exemplarPresent,
            bool producerEligible,
            bool ingredientsSufficient,
            bool completeOutputFits)
        {
            ExemplarPresent = exemplarPresent;
            ProducerEligible = producerEligible;
            IngredientsSufficient = ingredientsSufficient;
            CompleteOutputFits = completeOutputFits;
        }

        internal bool ExemplarPresent { get; }
        internal bool ProducerEligible { get; }
        internal bool IngredientsSufficient { get; }
        internal bool CompleteOutputFits { get; }
        internal bool CanRunCompleteAction =>
            ExemplarPresent && ProducerEligible && IngredientsSufficient && CompleteOutputFits;

        internal ReplenishmentTargetStopReason StopReason =>
            !ExemplarPresent
                ? ReplenishmentTargetStopReason.ExemplarAbsent
                : !ProducerEligible
                    ? ReplenishmentTargetStopReason.ProducerIneligible
                    : !IngredientsSufficient
                        ? ReplenishmentTargetStopReason.IngredientsInsufficient
                        : !CompleteOutputFits
                            ? ReplenishmentTargetStopReason.CompleteOutputDoesNotFit
                            : ReplenishmentTargetStopReason.Ready;

        internal static ReplenishmentTargetReadiness Ready { get; } =
            new ReplenishmentTargetReadiness(true, true, true, true);
    }

    internal sealed class ReplenishmentSelectionResult
    {
        private ReplenishmentSelectionResult(
            ReplenishmentTargetAuthorization target,
            int selectedIndex,
            int nextCursor,
            int inspectedTargets)
        {
            Target = target;
            SelectedIndex = selectedIndex;
            NextCursor = nextCursor;
            InspectedTargets = inspectedTargets;
        }

        internal bool HasAction => Target != null;
        internal ReplenishmentTargetAuthorization Target { get; }
        internal int SelectedIndex { get; }
        internal int NextCursor { get; }
        internal int InspectedTargets { get; }

        internal static ReplenishmentSelectionResult Action(
            ReplenishmentTargetAuthorization target,
            int selectedIndex,
            int nextCursor,
            int inspectedTargets) =>
            new ReplenishmentSelectionResult(
                target ?? throw new ArgumentNullException(nameof(target)),
                selectedIndex,
                nextCursor,
                inspectedTargets);

        internal static ReplenishmentSelectionResult None(
            int nextCursor,
            int inspectedTargets) =>
            new ReplenishmentSelectionResult(null, -1, nextCursor, inspectedTargets);
    }

    /// <summary>
    /// Selects no more than one complete producer action. A fully blocked pass advances the start
    /// cursor by one so newly unblocked targets do not inherit a permanent ordering advantage.
    /// </summary>
    internal static class DeterministicReplenishmentSelector
    {
        internal static ReplenishmentSelectionResult Select(
            ReplenishmentPlan plan,
            Func<ReplenishmentTargetAuthorization, ReplenishmentTargetReadiness> observe)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (observe == null) throw new ArgumentNullException(nameof(observe));
            int count = plan.Targets.Count;
            if (count == 0) return ReplenishmentSelectionResult.None(0, 0);

            int start = plan.Cursor;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                ReplenishmentTargetAuthorization target = plan.Targets[index];
                if (!observe(target).CanRunCompleteAction) continue;
                return ReplenishmentSelectionResult.Action(
                    target,
                    index,
                    (index + 1) % count,
                    offset + 1);
            }

            return ReplenishmentSelectionResult.None((start + 1) % count, count);
        }
    }
}
