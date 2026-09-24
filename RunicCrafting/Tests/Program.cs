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
                (nameof(RecipeRegressionTests.WorkbenchSplitMaterialsCommitAndRestore), RecipeRegressionTests.WorkbenchSplitMaterialsCommitAndRestore),
                (nameof(RecipeRegressionTests.SplitMaterialsFailSafelyWhenChestChanges), RecipeRegressionTests.SplitMaterialsFailSafelyWhenChestChanges),
                ("CrossModuleGateRejectsNestedCraft", InventorySafetyTests.CrossModuleGateRejectsNestedCraft),
                ("FailedRollbackBlocksReuseAndReleasesGate", InventorySafetyTests.FailedRollbackBlocksReuseAndReleasesGate),
                ("LostAuthorityNeverRestoresOldSnapshot", InventorySafetyTests.LostAuthorityNeverRestoresOldSnapshot),
                ("ForeignCallbackEditIsPreserved", InventorySafetyTests.ForeignCallbackEditIsPreserved),
                ("DrawerCraftingConsumptionAndRefund", DrawerMaterialTests.DrawerCraftingConsumptionAndRefund),
                ("DrawerManualStagingUsesNativeStackLimit", DrawerMaterialTests.DrawerManualStagingUsesNativeStackLimit),
                (nameof(AreaRepairTests.RadiusAndAccessAreBounded), AreaRepairTests.RadiusAndAccessAreBounded),
                (nameof(AreaRepairTests.BatchesAreBoundedAndCancelable), AreaRepairTests.BatchesAreBoundedAndCancelable),
                (nameof(AreaRepairTests.RuntimeUsesNativeRepairAndDisablesCleanly), AreaRepairTests.RuntimeUsesNativeRepairAndDisablesCleanly),
                (nameof(AreaRepairTests.HammerPreviewRowsAndConsumptionUsePlayerRange), AreaRepairTests.HammerPreviewRowsAndConsumptionUsePlayerRange),
                (nameof(AreaRepairTests.ForgeCostsCommitOnceAndRollbackExactly), AreaRepairTests.ForgeCostsCommitOnceAndRollbackExactly),
                (nameof(AreaRepairTests.InstalledRepairContractIsOwnerDirected), AreaRepairTests.InstalledRepairContractIsOwnerDirected),
                (nameof(PreviewAnswerTests.AnswersExpireAtQuarterSecondWithoutSliding), PreviewAnswerTests.AnswersExpireAtQuarterSecondWithoutSliding),
                (nameof(PreviewAnswerTests.OnlyWatchedChangesInvalidateAndEpochRejectsRaces), PreviewAnswerTests.OnlyWatchedChangesInvalidateAndEpochRejectsRaces),
                (nameof(PreviewAnswerTests.AnswerAndDependencyBoundsCannotReturnUntrackedResults), PreviewAnswerTests.AnswerAndDependencyBoundsCannotReturnUntrackedResults),
                (nameof(PreviewAnswerTests.RequirementKeysAreCanonicalBoundedAndCollisionSafe), PreviewAnswerTests.RequirementKeysAreCanonicalBoundedAndCollisionSafe),
                (nameof(PreviewAnswerTests.ReleasedIlKeepsCraftClicksOutsideUiMemo), PreviewAnswerTests.ReleasedIlKeepsCraftClicksOutsideUiMemo),
                (nameof(InstalledValheimContractTests.PreviewRefreshAndInvalidationHooksMatchInstalledValheim), InstalledValheimContractTests.PreviewRefreshAndInvalidationHooksMatchInstalledValheim),
                (nameof(PreviewCacheTests.UnchangedPayloadIsReusedAcrossThousandsOfQueries), PreviewCacheTests.UnchangedPayloadIsReusedAcrossThousandsOfQueries),
                (nameof(PreviewCacheTests.PayloadChangesInvalidateOnlyAffectedChest), PreviewCacheTests.PayloadChangesInvalidateOnlyAffectedChest),
                (nameof(PreviewCacheTests.IdentityDimensionsAndWorldLevelInvalidate), PreviewCacheTests.IdentityDimensionsAndWorldLevelInvalidate),
                (nameof(PreviewCacheTests.SessionPlayerAndDatabaseChangesClearEvidence), PreviewCacheTests.SessionPlayerAndDatabaseChangesClearEvidence),
                (nameof(PreviewCacheTests.CacheHasLruEntryAndByteBounds), PreviewCacheTests.CacheHasLruEntryAndByteBounds),
                (nameof(PreviewCacheTests.EmptyAndFailedSnapshotsCannotReuseOldValues), PreviewCacheTests.EmptyAndFailedSnapshotsCannotReuseOldValues),
                (nameof(PreviewCacheTests.MaterialCountsAreImmutableFilteredAndSaturating), PreviewCacheTests.MaterialCountsAreImmutableFilteredAndSaturating),
                (nameof(PreviewCacheTests.PreviewCacheIsOutsideWritableAndPermissionPaths), PreviewCacheTests.PreviewCacheIsOutsideWritableAndPermissionPaths),
                (nameof(PreviewCacheTests.InstalledLoaderConfirmsReportedAllocationPath), PreviewCacheTests.InstalledLoaderConfirmsReportedAllocationPath),
                (nameof(ManualInteractionTests.MovesExactlyOneRealItemAndPreservesMetadata), ManualInteractionTests.MovesExactlyOneRealItemAndPreservesMetadata),
                (nameof(ManualInteractionTests.FullProtectedMissingAndChangedSourcesDoNotMove), ManualInteractionTests.FullProtectedMissingAndChangedSourcesDoNotMove),
                (nameof(ManualInteractionTests.FailedAndPartiallyAppliedInsertionRestoresBothInventories), ManualInteractionTests.FailedAndPartiallyAppliedInsertionRestoresBothInventories),
                (nameof(ManualInteractionTests.CookingAndFuelGatesPreserveVanillaActions), ManualInteractionTests.CookingAndFuelGatesPreserveVanillaActions),
                (nameof(InstalledValheimContractTests.ManualInteractionHooksMatchInstalledValheim), InstalledValheimContractTests.ManualInteractionHooksMatchInstalledValheim),
                (nameof(RecipeRegressionTests.OrdinaryWorkbenchIgnoresUpgraderIngredient), RecipeRegressionTests.OrdinaryWorkbenchIgnoresUpgraderIngredient),
                (nameof(RecipeRegressionTests.UpgraderStationStillRequiresItsIngredient), RecipeRegressionTests.UpgraderStationStillRequiresItsIngredient),
                (nameof(RecipeRegressionTests.UpgradeBatchAndPieceCostsRemainCorrect), RecipeRegressionTests.UpgradeBatchAndPieceCostsRemainCorrect),
                (nameof(RecipeRegressionTests.WornEquipmentDoesNotBlockConsumptionOrRollback), RecipeRegressionTests.WornEquipmentDoesNotBlockConsumptionOrRollback),
                (nameof(RecipeRegressionTests.ChestPayloadOnlyAllowsNativeDurabilityConversion), RecipeRegressionTests.ChestPayloadOnlyAllowsNativeDurabilityConversion),
                (nameof(RecipeRegressionTests.PreviewAndConsumptionUseSameStationFilter), RecipeRegressionTests.PreviewAndConsumptionUseSameStationFilter),
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
                (nameof(StandaloneArchitectureTests.ContainerMutationRequiresNativeOwnership), StandaloneArchitectureTests.ContainerMutationRequiresNativeOwnership),
                (nameof(InstalledValheimContractTests.InventoryChangedUsesValheim10Signature), InstalledValheimContractTests.InventoryChangedUsesValheim10Signature),
                (nameof(InstalledValheimContractTests.CraftOutputAddItemUsesValheim10Signature), InstalledValheimContractTests.CraftOutputAddItemUsesValheim10Signature),
                (nameof(InstalledValheimContractTests.PlacePieceUsesValheim10Signature), InstalledValheimContractTests.PlacePieceUsesValheim10Signature),
                (nameof(InstalledValheimContractTests.PrivateReflectionBridgeMatchesValheim10), InstalledValheimContractTests.PrivateReflectionBridgeMatchesValheim10)
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
