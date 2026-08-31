using System;
using System.Collections.Generic;

namespace RunicCrafting.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<(string Name, Action Run)>
            {
                (nameof(CraftingTests.InventoryIsAlwaysAllocatedBeforeNearbyContainers), CraftingTests.InventoryIsAlwaysAllocatedBeforeNearbyContainers),
                (nameof(CraftingTests.LastResourceRaceHasOneWinner), CraftingTests.LastResourceRaceHasOneWinner),
                (nameof(CraftingTests.StationAndMaterialPermissionsAreIndependent), CraftingTests.StationAndMaterialPermissionsAreIndependent),
                (nameof(CraftingTests.FailedCraftRestoresEveryEarlierRemoval), CraftingTests.FailedCraftRestoresEveryEarlierRemoval),
                (nameof(CraftingTests.ExactCostIsConsumedOnceAcrossSources), CraftingTests.ExactCostIsConsumedOnceAcrossSources),
                (nameof(CraftingTests.DlcPrerequisiteFailsClosed), CraftingTests.DlcPrerequisiteFailsClosed),
                (nameof(CraftingTests.DisabledNearbyCraftingHasAnExplicitStatus), CraftingTests.DisabledNearbyCraftingHasAnExplicitStatus),
                (nameof(CraftingTests.RuntimePrerequisitesReportTheFirstActionableGate), CraftingTests.RuntimePrerequisitesReportTheFirstActionableGate),
                (nameof(CraftingTests.WorkshopDenialsRetainTheirExactReason), CraftingTests.WorkshopDenialsRetainTheirExactReason),
                (nameof(CraftingTests.RequirementRowsShowCountsOrTheBlockingState), CraftingTests.RequirementRowsShowCountsOrTheBlockingState),
                (nameof(CraftingTests.CompactRequirementAmountUsesCombinedAvailability), CraftingTests.CompactRequirementAmountUsesCombinedAvailability),
                (nameof(CraftingTests.NearbySufficiencyStopsTheVanillaShortagePulse), CraftingTests.NearbySufficiencyStopsTheVanillaShortagePulse),
                (nameof(CraftingTests.PlacementLifecycleCommitsOnlyAfterVanillaProgress), CraftingTests.PlacementLifecycleCommitsOnlyAfterVanillaProgress),
                (nameof(CraftingTests.SmelterPlacementConsumesStoneAndCoresExactlyOnce), CraftingTests.SmelterPlacementConsumesStoneAndCoresExactlyOnce),
                (nameof(CraftingTests.RejectedSmelterPlacementRestoresStoneAndCores), CraftingTests.RejectedSmelterPlacementRestoresStoneAndCores),
                (nameof(CraftingTests.VanillaStationlessDefaultsCoverRequestedBuildables), CraftingTests.VanillaStationlessDefaultsCoverRequestedBuildables),
                (nameof(CraftingTests.StationlessPrefabRulesAreExactAndDenyWins), CraftingTests.StationlessPrefabRulesAreExactAndDenyWins),
                (nameof(CraftingTests.StationlessWildcardRequiresExplicitOptIn), CraftingTests.StationlessWildcardRequiresExplicitOptIn),
                (nameof(CraftingTests.InvalidStationlessDenyRulesDenyEverything), CraftingTests.InvalidStationlessDenyRulesDenyEverything),
                (nameof(CraftingTests.StationlessFirePlacementConsumesExactMaterialsOnce), CraftingTests.StationlessFirePlacementConsumesExactMaterialsOnce),
                (nameof(CraftingTests.RejectedStationlessPlacementRestoresEverySource), CraftingTests.RejectedStationlessPlacementRestoresEverySource),
                (nameof(CraftingTests.MissingRequiredStationNeverFallsBackToPlayerLocalScope), CraftingTests.MissingRequiredStationNeverFallsBackToPlayerLocalScope),
                (nameof(CraftingTests.UnchangedSpatialMembershipIsNotRebuilt), CraftingTests.UnchangedSpatialMembershipIsNotRebuilt),
                (nameof(StandaloneArchitectureTests.PackageHasNoFoundationDependencies), StandaloneArchitectureTests.PackageHasNoFoundationDependencies),
                (nameof(StandaloneArchitectureTests.RuntimeHasNoDurableOrGlobalMutationLayer), StandaloneArchitectureTests.RuntimeHasNoDurableOrGlobalMutationLayer),
                (nameof(StandaloneArchitectureTests.ContainerMutationRequiresNativeOwnership), StandaloneArchitectureTests.ContainerMutationRequiresNativeOwnership)
            };

            int failures = 0;
            foreach ((string name, Action run) in tests)
            {
                try
                {
                    run();
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + name + ": " + exception);
                }
            }
            Console.WriteLine((tests.Count - failures) + "/" + tests.Count + " tests passed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
