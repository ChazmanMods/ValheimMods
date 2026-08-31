using System;
using System.Collections.Generic;

namespace RunicTransactions.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<(string Name, Action Run)>
            {
                (nameof(CoordinatorTests.DeterministicOrderingAndCanonicalization), CoordinatorTests.DeterministicOrderingAndCanonicalization),
                (nameof(CoordinatorTests.InsufficientResourcesNeverPartiallyReserve), CoordinatorTests.InsufficientResourcesNeverPartiallyReserve),
                (nameof(CoordinatorTests.PermissionDenialNeverPartiallyReserve), CoordinatorTests.PermissionDenialNeverPartiallyReserve),
                (nameof(CoordinatorTests.DefaultAuthorizerFailsClosed), CoordinatorTests.DefaultAuthorizerFailsClosed),
                (nameof(CoordinatorTests.LastResourceRaceHasExactlyOneWinner), CoordinatorTests.LastResourceRaceHasExactlyOneWinner),
                (nameof(CoordinatorTests.ExplicitRollbackReleasesAllEndpoints), CoordinatorTests.ExplicitRollbackReleasesAllEndpoints),
                (nameof(CoordinatorTests.RevalidationFailureRollsBackWithoutPartialConsumption), CoordinatorTests.RevalidationFailureRollsBackWithoutPartialConsumption),
                (nameof(CoordinatorTests.PermissionIsRevalidatedAtCommit), CoordinatorTests.PermissionIsRevalidatedAtCommit),
                (nameof(CoordinatorTests.IdempotencyKeyCannotConsumeTwice), CoordinatorTests.IdempotencyKeyCannotConsumeTwice),
                (nameof(CoordinatorTests.CapacityFailsClosedUntilTerminalPrune), CoordinatorTests.CapacityFailsClosedUntilTerminalPrune),
                (nameof(CoordinatorTests.ExpiredLeaseAutomaticallyReleasesEntireBatch), CoordinatorTests.ExpiredLeaseAutomaticallyReleasesEntireBatch),
                (nameof(CoordinatorTests.TerminalRetentionBoundsIdempotencyTombstones), CoordinatorTests.TerminalRetentionBoundsIdempotencyTombstones),
                (nameof(CoordinatorTests.CommitAndExpirySerializeWithoutDoubleMutation), CoordinatorTests.CommitAndExpirySerializeWithoutDoubleMutation),
                (nameof(CoordinatorTests.ReentrantAuthorizerCannotCommitPreparingEntry), CoordinatorTests.ReentrantAuthorizerCannotCommitPreparingEntry),
                (nameof(CoordinatorTests.EndpointVersionAdvancesForAvailabilityMutations), CoordinatorTests.EndpointVersionAdvancesForAvailabilityMutations),
                (nameof(CoordinatorTests.ExtremeClockSaturatesLeaseBeforeReserving), CoordinatorTests.ExtremeClockSaturatesLeaseBeforeReserving),
                (nameof(ContainerContractTests.QueryResultsAreBoundedDeterministicAndImmutable), ContainerContractTests.QueryResultsAreBoundedDeterministicAndImmutable),
                (nameof(ContainerContractTests.FailedQueryCannotDiscloseEndpoints), ContainerContractTests.FailedQueryCannotDiscloseEndpoints),
                (nameof(ContainerContractTests.TransferContractsRejectUnsafeShapes), ContainerContractTests.TransferContractsRejectUnsafeShapes),
                (nameof(ContainerContractTests.WorldPositionRejectsNonFiniteCoordinates), ContainerContractTests.WorldPositionRejectsNonFiniteCoordinates),
                (nameof(ContainerContractTests.ValheimIdentityFormattingRoundTrips), ContainerContractTests.ValheimIdentityFormattingRoundTrips)
                ,(nameof(WorldObjectIdentityTests.TokensRequireCanonicalNonEmptyGuidN), WorldObjectIdentityTests.TokensRequireCanonicalNonEmptyGuidN)
                ,(nameof(WorldObjectIdentityTests.MarkerCapacityRejectsOnlyNewPublicationAtTheBoundary), WorldObjectIdentityTests.MarkerCapacityRejectsOnlyNewPublicationAtTheBoundary)
                ,(nameof(WorldObjectIdentityTests.IdentityIndexWorkScalesLinearlyAtWorldSizes), WorldObjectIdentityTests.IdentityIndexWorkScalesLinearlyAtWorldSizes)
                ,(nameof(WorldObjectIdentityTests.MarkerPreservingDuplicateMutationFailsClosed), WorldObjectIdentityTests.MarkerPreservingDuplicateMutationFailsClosed)
                ,(nameof(WorldObjectIdentityTests.CacheReuseRequiresTheExactWorldMutationGeneration), WorldObjectIdentityTests.CacheReuseRequiresTheExactWorldMutationGeneration)
                ,(nameof(WorldObjectIdentityTests.InstalledIdentityMutationSurfaceIsHooked), WorldObjectIdentityTests.InstalledIdentityMutationSurfaceIsHooked)
                ,(nameof(WorldObjectIdentityTests.StableLockSchemaRoundTripsWithoutRawZdoIds), WorldObjectIdentityTests.StableLockSchemaRoundTripsWithoutRawZdoIds)
                ,(nameof(WorldObjectIdentityTests.LegacyLockSchemaRemainsExplicitAndFailClosed), WorldObjectIdentityTests.LegacyLockSchemaRemainsExplicitAndFailClosed)
                ,(nameof(WorldObjectIdentityTests.StableHeaderSchemaPersistsTokensAndDropsRawCaches), WorldObjectIdentityTests.StableHeaderSchemaPersistsTokensAndDropsRawCaches)
                ,(nameof(WorldObjectIdentityTests.LegacyHeaderSchemaRemainsParseableWithoutRebinding), WorldObjectIdentityTests.LegacyHeaderSchemaRemainsParseableWithoutRebinding)
                ,(nameof(WorldObjectIdentityTests.IdentityUpgradeEnvelopePreservesOldToNewMappingWithoutAuthority), WorldObjectIdentityTests.IdentityUpgradeEnvelopePreservesOldToNewMappingWithoutAuthority)
                ,(nameof(WorldObjectIdentityTests.LegacyClaimRewriteAcceptsOnlyExactOldOrExactNewEvidence), WorldObjectIdentityTests.LegacyClaimRewriteAcceptsOnlyExactOldOrExactNewEvidence)
                ,(nameof(WorldObjectIdentityTests.RemappedLegacyOrphanCleanupAcceptsOnlySafeCrashStates), WorldObjectIdentityTests.RemappedLegacyOrphanCleanupAcceptsOnlySafeCrashStates)
                ,(nameof(WorldObjectIdentityTests.LegacyClaimDiscoveryRequiresExactOneToOneCoverage), WorldObjectIdentityTests.LegacyClaimDiscoveryRequiresExactOneToOneCoverage)
                ,(nameof(WorldObjectIdentityTests.LegacyClaimDiscoveryBoundsAndLoadedEndpointsFailClosed), WorldObjectIdentityTests.LegacyClaimDiscoveryBoundsAndLoadedEndpointsFailClosed)
                ,(nameof(MutationGateTests.CrossModuleGateRejectsReentrancyAndStaleDisposal), MutationGateTests.CrossModuleGateRejectsReentrancyAndStaleDisposal)
                ,(nameof(OwnershipReturnRpcTests.ReturnRequestCacheIsRateLimitedPrunedAndBounded), OwnershipReturnRpcTests.ReturnRequestCacheIsRateLimitedPrunedAndBounded)
                ,(nameof(OwnershipReturnRpcTests.ClientAcceptsOnlyExactCurrentDirectServerSession), OwnershipReturnRpcTests.ClientAcceptsOnlyExactCurrentDirectServerSession)
                ,(nameof(DurableCompositeTests.TokenIssueIsDurableExactAndExplicitlyCancelled), DurableCompositeTests.TokenIssueIsDurableExactAndExplicitlyCancelled)
                ,(nameof(DurableCompositeTests.RequestLookupIsTwoHashReadOnlyAndRestartExact), DurableCompositeTests.RequestLookupIsTwoHashReadOnlyAndRestartExact)
                ,(nameof(DurableCompositeTests.LegacySingleHashCatalogMigratesWithoutChangingLookup), DurableCompositeTests.LegacySingleHashCatalogMigratesWithoutChangingLookup)
                ,(nameof(DurableCompositeTests.RootPrecedesClaimsAndPreparedPrecedesMutation), DurableCompositeTests.RootPrecedesClaimsAndPreparedPrecedesMutation)
                ,(nameof(DurableCompositeTests.EndpointBoundsAreSortedUniqueAndMetadataNeutral), DurableCompositeTests.EndpointBoundsAreSortedUniqueAndMetadataNeutral)
                ,(nameof(DurableCompositeTests.UnrelatedDomainMutationFailsBeforeCommit), DurableCompositeTests.UnrelatedDomainMutationFailsBeforeCommit)
                ,(nameof(DurableCompositeTests.CrossModuleHistoryIsOrderedAndAbaSafe), DurableCompositeTests.CrossModuleHistoryIsOrderedAndAbaSafe)
                ,(nameof(DurableCompositeTests.OutstandingIsAccountScopedAndCarriesExactManifest), DurableCompositeTests.OutstandingIsAccountScopedAndCarriesExactManifest)
                ,(nameof(DurableCompositeTests.TokenDispositionSurvivesAcknowledgedCheckpointCompaction), DurableCompositeTests.TokenDispositionSurvivesAcknowledgedCheckpointCompaction)
                ,(nameof(DurableCompositeTests.AbortedReconciliationHoldSurvivesRestartAndCheckpoint), DurableCompositeTests.AbortedReconciliationHoldSurvivesRestartAndCheckpoint)
                ,(nameof(DurableCompositeTests.OwnerStartupEnumerationRecoversWithoutActorReconnect), DurableCompositeTests.OwnerStartupEnumerationRecoversWithoutActorReconnect)
                ,(nameof(DurableCompositeTests.CheckpointNeedsTwoGenerationsAndRetainsIssuedTokens), DurableCompositeTests.CheckpointNeedsTwoGenerationsAndRetainsIssuedTokens)
                ,(nameof(DurableCompositeTests.StagedCheckpointRecoveryRebindsRemappedMarkerIdentity), DurableCompositeTests.StagedCheckpointRecoveryRebindsRemappedMarkerIdentity)
                ,(nameof(DurableCompositeTests.CheckpointCaptureIgnoresLaterWorldStateAndHoldsMutationBarrier), DurableCompositeTests.CheckpointCaptureIgnoresLaterWorldStateAndHoldsMutationBarrier)
                ,(nameof(DurableCompositeTests.ForensicStagedFileAndReparsePathFailClosed), DurableCompositeTests.ForensicStagedFileAndReparsePathFailClosed)
                ,(nameof(DurableCompositeTests.WorldObjectCreateCommitsAndPreparedAbortCompensates), DurableCompositeTests.WorldObjectCreateCommitsAndPreparedAbortCompensates)
                ,(nameof(DurableCompositeTests.MixedRemovalAndCreationReplaysAsOneOrderedRoot), DurableCompositeTests.MixedRemovalAndCreationReplaysAsOneOrderedRoot)
                ,(nameof(DurableCompositeTests.WorldObjectDuplicateTokenQuarantinesBeforePrepare), DurableCompositeTests.WorldObjectDuplicateTokenQuarantinesBeforePrepare)
                ,(nameof(DurableCompositeTests.WorldObjectUpdateCommitsRecoversAndCompensates), DurableCompositeTests.WorldObjectUpdateCommitsRecoversAndCompensates)
                ,(nameof(DurableCompositeTests.WorldObjectDomainAllowsExactlySixtyFourEndpoints), DurableCompositeTests.WorldObjectDomainAllowsExactlySixtyFourEndpoints)
                ,(nameof(DurableCompositeTests.DeferredOwnerUpdateAndRemovalResumeAfterDisconnect), DurableCompositeTests.DeferredOwnerUpdateAndRemovalResumeAfterDisconnect)
                ,(nameof(DurableCompositeTests.ExistingObjectEnrollmentIsJournalFirstAndMutationLast), DurableCompositeTests.ExistingObjectEnrollmentIsJournalFirstAndMutationLast)
                ,(nameof(DurableCompositeTests.ExistingObjectEnrollmentResumesAcrossEveryClaimingCrashCut), DurableCompositeTests.ExistingObjectEnrollmentResumesAcrossEveryClaimingCrashCut)
                ,(nameof(DurableCompositeTests.ExistingObjectEnrollmentConflictsAndOwnerLossFailClosed), DurableCompositeTests.ExistingObjectEnrollmentConflictsAndOwnerLossFailClosed)
                ,(nameof(DurableCompositeTests.CustodyExactIntentLifecycleIsExplicitAndNeverTimeoutInferred), DurableCompositeTests.CustodyExactIntentLifecycleIsExplicitAndNeverTimeoutInferred)
                ,(nameof(DurableCompositeTests.PeerAdmissionIsTransportAccountScopedAndProfileExact), DurableCompositeTests.PeerAdmissionIsTransportAccountScopedAndProfileExact)
                ,(nameof(CompositeWorldSaveCheckpointTests.InstalledSavePipelineSupportsExactDurableCheckpoint), CompositeWorldSaveCheckpointTests.InstalledSavePipelineSupportsExactDurableCheckpoint)
                ,(nameof(CompositeWorldSaveCheckpointTests.MetadataReplaceCannotConsumeDatabaseCandidate), CompositeWorldSaveCheckpointTests.MetadataReplaceCannotConsumeDatabaseCandidate)
                ,(nameof(CompositeWorldSaveCheckpointTests.MarkerCreationAssignsExactNonzeroPrefab), CompositeWorldSaveCheckpointTests.MarkerCreationAssignsExactNonzeroPrefab)
                ,(nameof(CompositeWorldSaveCheckpointTests.PrefabZeroNeverBecomesPersistent), CompositeWorldSaveCheckpointTests.PrefabZeroNeverBecomesPersistent)
                ,(nameof(CompositeWorldSaveCheckpointTests.RepeatedRefreshesReuseOneMarker), CompositeWorldSaveCheckpointTests.RepeatedRefreshesReuseOneMarker)
                ,(nameof(CompositeWorldSaveCheckpointTests.FailedCreationCannotRetryWithoutBound), CompositeWorldSaveCheckpointTests.FailedCreationCannotRetryWithoutBound)
                ,(nameof(CompositeWorldSaveCheckpointTests.MarkerSurvivesSaveReloadModel), CompositeWorldSaveCheckpointTests.MarkerSurvivesSaveReloadModel)
                ,(nameof(CompositeWorldSaveCheckpointTests.BoundedUpdatesProduceZeroHashZeroGrowth), CompositeWorldSaveCheckpointTests.BoundedUpdatesProduceZeroHashZeroGrowth)
                ,(nameof(CompositeWorldSaveCheckpointTests.RemappedMarkerIsRediscoveredByLogicalIdentity), CompositeWorldSaveCheckpointTests.RemappedMarkerIsRediscoveredByLogicalIdentity)
                ,(nameof(CompositeWorldSaveCheckpointTests.StaleNumericHintNeverCreatesAReplacement), CompositeWorldSaveCheckpointTests.StaleNumericHintNeverCreatesAReplacement)
                ,(nameof(CompositeWorldSaveCheckpointTests.OneLogicalMarkerSurvivesSaveReloadModel), CompositeWorldSaveCheckpointTests.OneLogicalMarkerSurvivesSaveReloadModel)
                ,(nameof(CompositeWorldSaveCheckpointTests.ZeroLogicalMarkersUsesOnlyBoundedBootstrapCreation), CompositeWorldSaveCheckpointTests.ZeroLogicalMarkersUsesOnlyBoundedBootstrapCreation)
                ,(nameof(CompositeWorldSaveCheckpointTests.DuplicateLogicalMarkersFailWithoutCreation), CompositeWorldSaveCheckpointTests.DuplicateLogicalMarkersFailWithoutCreation)
                ,(nameof(CompositeWorldSaveCheckpointTests.ForeignWrongSchemaAndMalformedMarkersAreRejected), CompositeWorldSaveCheckpointTests.ForeignWrongSchemaAndMalformedMarkersAreRejected)
            };

            int failures = 0;
            foreach ((string name, Action run) in tests)
            {
                try
                {
                    run();
                    System.Console.WriteLine($"PASS {name}");
                }
                catch (Exception exception)
                {
                    failures++;
                    System.Console.Error.WriteLine($"FAIL {name}: {exception}");
                }
            }

            System.Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
