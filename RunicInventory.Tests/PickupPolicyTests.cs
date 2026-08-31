using System;
using System.Text;
using RunicInventory.Core;

namespace RunicInventory.Tests
{
    internal static class PickupPolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run("pickup planner reports exact compatible-stack fit", ExistingStacksFit);
            TestRunner.Run("pickup planner reports safe-slot overflow", OverflowIsExact);
            TestRunner.Run("pickup planner reports projected encumbrance", EncumbranceIsExact);
            TestRunner.Run("pickup filter never reports accepted items", FilterDeniesWithoutMutation);
            TestRunner.Run("pickup planner saturates extreme capacity and weight", ExtremeValuesAreBounded);
            TestRunner.Run("pickup filter matches exact prefab and shared tokens", ExactFilterMatches);
            TestRunner.Run("pickup filter caps rules at 128", FilterRuleBound);
            TestRunner.Run("pickup filter rejects unsafe rich-text and control tokens", UnsafeTokensRejected);
            TestRunner.Run("million-character pickup filter fails before split allocation", OversizeFilterFailsEarly);
            TestRunner.Run("pickup planner rejects invalid numeric facts", InvalidFactsRejected);
        }

        private static void ExistingStacksFit()
        {
            PickupDecision decision = PickupPlanner.Evaluate(10, 50, 10, 0, 1f, 20f, 100f, false);
            TestAssert.Equal(10, decision.AcceptedItems);
            TestAssert.Equal(0, decision.OverflowItems);
            TestAssert.True(decision.Fits);
            TestAssert.Equal(30f, decision.ResultingWeight);
        }

        private static void OverflowIsExact()
        {
            PickupDecision decision = PickupPlanner.Evaluate(120, 50, 10, 1, 0.1f, 0f, 100f, false);
            TestAssert.Equal(60, decision.AcceptedItems);
            TestAssert.Equal(60, decision.OverflowItems);
            TestAssert.False(decision.Fits);
        }

        private static void EncumbranceIsExact()
        {
            PickupDecision decision = PickupPlanner.Evaluate(2, 10, 0, 1, 4f, 95f, 100f, false);
            TestAssert.True(decision.Encumbered);
            TestAssert.Equal(103f, decision.ResultingWeight);
        }

        private static void FilterDeniesWithoutMutation()
        {
            PickupDecision decision = PickupPlanner.Evaluate(5, 10, 10, 10, 1f, 0f, 100f, true);
            TestAssert.True(decision.Filtered);
            TestAssert.Equal(0, decision.AcceptedItems);
            TestAssert.Equal(5, decision.OverflowItems);
            TestAssert.Equal(0f, decision.ResultingWeight);
        }

        private static void ExtremeValuesAreBounded()
        {
            PickupDecision decision = PickupPlanner.Evaluate(int.MaxValue, int.MaxValue, int.MaxValue, 128,
                float.MaxValue / 2f, float.MaxValue / 2f, float.MaxValue, false);
            TestAssert.Equal(int.MaxValue, decision.AcceptedItems);
            TestAssert.Equal(float.MaxValue, decision.ResultingWeight);
        }

        private static void ExactFilterMatches()
        {
            PickupFilterSet filters = PickupFilterSet.Parse("Stone;$item_wood");
            TestAssert.True(filters.Matches("Stone", string.Empty));
            TestAssert.True(filters.Matches(string.Empty, "$item_wood"));
            TestAssert.False(filters.Matches("StoneGolem", "$item_wooden"));
        }

        private static void FilterRuleBound()
        {
            var input = new StringBuilder();
            for (int index = 0; index < 140; index++) input.Append("Item").Append(index).Append(',');
            PickupFilterSet filters = PickupFilterSet.Parse(input.ToString());
            TestAssert.Equal(128, filters.Count);
            TestAssert.True(filters.Truncated);
        }

        private static void UnsafeTokensRejected()
        {
            PickupFilterSet filters = PickupFilterSet.Parse("Stone,<color=red>Wood,Good\u0001Bad");
            TestAssert.Equal(1, filters.Count);
            TestAssert.True(filters.Matches("Stone", null));
        }

        private static void OversizeFilterFailsEarly()
        {
            PickupFilterSet filters = PickupFilterSet.Parse(new string('x', 1000000));
            TestAssert.Equal(0, filters.Count);
            TestAssert.True(filters.Truncated);
        }

        private static void InvalidFactsRejected()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(() => PickupPlanner.Evaluate(-1, 1, 0, 0, 0, 0, 0, false));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => PickupPlanner.Evaluate(1, 0, 0, 0, 0, 0, 0, false));
            TestAssert.Throws<ArgumentOutOfRangeException>(() => PickupPlanner.Evaluate(1, 1, 0, 0, float.NaN, 0, 0, false));
        }
    }
}
