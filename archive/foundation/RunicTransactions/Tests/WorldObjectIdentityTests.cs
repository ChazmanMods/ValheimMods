using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicTransactions.Contracts;
using RunicTransactions.Valheim;

namespace RunicTransactions.Tests
{
    internal static class WorldObjectIdentityTests
    {
        private const string StationToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string EndpointAToken = "22222222222222222222222222222222";
        private const string EndpointBToken = "33333333333333333333333333333333";
        private static readonly string JournalHash = new string('a', 64);

        public static void TokensRequireCanonicalNonEmptyGuidN()
        {
            TestAssert.True(
                WorldObjectToken.TryParse(StationToken, out WorldObjectToken parsed),
                "A canonical lowercase non-empty GUID-N token must parse.");
            TestAssert.Equal(StationToken, parsed.Value, "The token must round-trip exactly.");
            TestAssert.False(
                WorldObjectToken.TryParse(StationToken.ToUpperInvariant(), out _),
                "Uppercase token spellings are not canonical.");
            TestAssert.False(
                WorldObjectToken.TryParse(new string('0', 32), out _),
                "The empty GUID must not authorize an object.");
            TestAssert.False(
                WorldObjectToken.TryParse("{11111111-1111-1111-1111-111111111111}", out _),
                "Non-N GUID spellings are not canonical.");
            TestAssert.Equal(
                "valheim.world-object:" + StationToken,
                ValheimIdentityIds.WorldObjectEndpoint(StationToken).Value,
                "The transaction endpoint identity must use the shared stable-token prefix.");
            TestAssert.Equal(
                WorldObjectIdentityStatus.Invalid,
                WorldObjectIdentity.Resolve("not-a-token", out _),
                "Malformed tokens must fail before touching world state.");
        }

        public static void MarkerCapacityRejectsOnlyNewPublicationAtTheBoundary()
        {
            int maximum = WorldObjectIdentity.MaximumIndexedObjects;
            TestAssert.Equal(
                WorldObjectIdentityStatus.Ready,
                WorldObjectIdentity.EvaluateMarkerCapacityForEnsure(
                    WorldObjectIdentityStatus.Missing, maximum - 1),
                "The final free marker slot must remain usable.");
            TestAssert.Equal(
                WorldObjectIdentityStatus.CapacityExceeded,
                WorldObjectIdentity.EvaluateMarkerCapacityForEnsure(
                    WorldObjectIdentityStatus.Missing, maximum),
                "A new token must be rejected before mutation at the exact cap.");
            TestAssert.Equal(
                WorldObjectIdentityStatus.CapacityExceeded,
                WorldObjectIdentity.EvaluateMarkerCapacityForEnsure(
                    WorldObjectIdentityStatus.Invalid, maximum),
                "Publishing the marker for token-first crash evidence also requires free capacity.");
            TestAssert.Equal(
                WorldObjectIdentityStatus.Ready,
                WorldObjectIdentity.EvaluateMarkerCapacityForEnsure(
                    WorldObjectIdentityStatus.Ready, maximum),
                "An existing Ready token publishes no marker and must remain usable at the cap.");
            TestAssert.Equal(
                WorldObjectIdentityStatus.CapacityExceeded,
                WorldObjectIdentity.EvaluateMarkerCapacityForEnsure(
                    WorldObjectIdentityStatus.Missing, maximum + 1),
                "An already over-cap world must not publish another marker.");
        }

        public static void IdentityIndexWorkScalesLinearlyAtWorldSizes()
        {
            int[] sizes = { 100, 1000, 10000 };
            long previousReadyWork = 0L;
            long previousMintWork = 0L;
            foreach (int size in sizes)
            {
                WorldObjectIdentity.IdentityIndexWorkProfile ready =
                    WorldObjectIdentity.ProfileSequentialEnsuresForTests(size);
                TestAssert.Equal(size, ready.ObjectCount,
                    "The instrumented ready-object population must be exact.");
                TestAssert.Equal((long)size, ready.FullScanVisits,
                    "Ready identities must be indexed by one initial linear visit only.");
                TestAssert.Equal(0L, ready.IncrementalMutationVisits,
                    "Re-ensuring Ready identities must not manufacture mutation work.");
                TestAssert.Equal((long)size, ready.LookupVisits,
                    "Each Ready Ensure requires one constant-time uniqueness lookup.");
                TestAssert.Equal(2L * size, ready.TotalVisits,
                    "Ready Ensure instrumentation must stay exactly linear.");

                WorldObjectIdentity.IdentityIndexWorkProfile minted =
                    WorldObjectIdentity.ProfileSequentialMintingForTests(size);
                TestAssert.Equal(0L, minted.FullScanVisits,
                    "An already initialized empty index must not rescan while identities are minted.");
                TestAssert.Equal(2L * size, minted.IncrementalMutationVisits,
                    "Token-first plus marker publication must cause two bounded updates per mint.");
                TestAssert.Equal((long)size, minted.LookupVisits,
                    "Each minted identity requires one constant-time uniqueness proof.");
                TestAssert.Equal(3L * size, minted.TotalVisits,
                    "Sequential minting instrumentation must stay exactly linear.");

                if (previousReadyWork != 0L)
                {
                    TestAssert.Equal(previousReadyWork * 10L, ready.TotalVisits,
                        "A tenfold world-size increase must cause exactly tenfold ready-index work.");
                    TestAssert.Equal(previousMintWork * 10L, minted.TotalVisits,
                        "A tenfold world-size increase must cause exactly tenfold mint-index work.");
                }
                previousReadyWork = ready.TotalVisits;
                previousMintWork = minted.TotalVisits;
            }
        }

        public static void MarkerPreservingDuplicateMutationFailsClosed()
        {
            WorldObjectIdentity.IdentityDuplicateMutationProbe probe =
                WorldObjectIdentity.ProbeDuplicateMutationForTests();
            TestAssert.Equal(2, probe.MarkerCountBefore,
                "The two-object snapshot must begin with two presence markers.");
            TestAssert.Equal(probe.MarkerCountBefore, probe.MarkerCountAfter,
                "Direct token injection must leave marker membership unchanged in this regression.");
            TestAssert.Equal(1, probe.OwnersBefore,
                "The requested token must begin uniquely owned.");
            TestAssert.Equal(2, probe.OwnersAfter,
                "The incremental token mutation must expose the injected duplicate as ambiguous.");
            TestAssert.Equal(1, probe.OwnersAfterOriginalMoves,
                "Moving the original owner away must promote the remaining duplicate exactly.");
            TestAssert.Equal(0, probe.OwnersAfterAllMove,
                "Moving the final owner away must remove the stale token entry completely.");
        }

        public static void CacheReuseRequiresTheExactWorldMutationGeneration()
        {
            TestAssert.True(
                WorldObjectIdentity.CanReuseIndexSnapshotForTests(
                    sameWorld: true, snapshotGeneration: 41L,
                    currentGeneration: 41L, dirty: false),
                "An exact clean snapshot in the same world may be reused.");
            TestAssert.False(
                WorldObjectIdentity.CanReuseIndexSnapshotForTests(
                    sameWorld: false, snapshotGeneration: 41L,
                    currentGeneration: 41L, dirty: false),
                "A ZDOMan/world replacement must reject the old snapshot.");
            TestAssert.False(
                WorldObjectIdentity.CanReuseIndexSnapshotForTests(
                    sameWorld: true, snapshotGeneration: 41L,
                    currentGeneration: 42L, dirty: false),
                "An unincorporated identity mutation must reject the old snapshot.");
            TestAssert.False(
                WorldObjectIdentity.CanReuseIndexSnapshotForTests(
                    sameWorld: true, snapshotGeneration: 42L,
                    currentGeneration: 42L, dirty: true),
                "Unreadable mutation evidence must keep the cache fail closed.");
            TestAssert.False(
                WorldObjectIdentity.CanReuseIndexSnapshotForTests(
                    sameWorld: true, snapshotGeneration: 0L,
                    currentGeneration: 0L, dirty: false),
                "An uninitialized generation must never masquerade as a valid snapshot.");
        }

        public static void InstalledIdentityMutationSurfaceIsHooked()
        {
            Type[] patchTypes =
            {
                typeof(WorldObjectIdentityStringSetPatch),
                typeof(WorldObjectIdentityIntSetPatch),
                typeof(WorldObjectIdentityStringAddPatch),
                typeof(WorldObjectIdentityIntAddPatch),
                typeof(WorldObjectIdentityIntRemovePatch),
                typeof(WorldObjectIdentityIntReleasePatch),
                typeof(WorldObjectIdentityStringReleasePatch),
                typeof(WorldObjectIdentityReleasePatch),
                typeof(WorldObjectIdentityResetPatch)
            };
            foreach (Type patchType in patchTypes)
                TestAssert.True(
                    patchType.CustomAttributes.Any(attribute =>
                        attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"),
                    patchType.Name + " must remain discoverable by Plugin.PatchAll().");

            BindingFlags staticMethods = BindingFlags.Static |
                                         BindingFlags.Public |
                                         BindingFlags.NonPublic;
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "Set", staticMethods, null,
                    new[] { typeof(ZDOID), typeof(int), typeof(string) }, null) != null,
                "The installed string Set mutation boundary changed.");
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "Set", staticMethods, null,
                    new[] { typeof(ZDOID), typeof(int), typeof(int) }, null) != null,
                "The installed int Set mutation boundary changed.");
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "Add", staticMethods, null,
                    new[] { typeof(ZDOID), typeof(int), typeof(string) }, null) != null,
                "The installed string Add/load boundary changed.");
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "Add", staticMethods, null,
                    new[] { typeof(ZDOID), typeof(int), typeof(int) }, null) != null,
                "The installed int Add/load boundary changed.");
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "RemoveInt", staticMethods, null,
                    new[] { typeof(ZDOID), typeof(int) }, null) != null,
                "The installed marker removal boundary changed.");
            TestAssert.True(typeof(ZDOExtraData).GetMethod(
                    "ReleaseInts", staticMethods) != null &&
                typeof(ZDOExtraData).GetMethod("ReleaseStrings", staticMethods) != null &&
                typeof(ZDOExtraData).GetMethod("Release", staticMethods, null,
                    new[] { typeof(ZDO), typeof(ZDOID) }, null) != null &&
                typeof(ZDOExtraData).GetMethod("Reset", staticMethods) != null,
                "The installed unload/reset mutation boundaries changed.");

        }

        public static void StableLockSchemaRoundTripsWithoutRawZdoIds()
        {
            DurableEndpointLockRecord original = DurableEndpointLockRecord.CreateStable(
                "operation", "module", StationToken, EndpointAToken, JournalHash,
                "before", "after", DurableEndpointLockPhase.Prepared);
            string encoded = DurableContainerSafety.Serialize(original);
            TestAssert.Equal(
                DurableEndpointLockReadState.Valid,
                DurableContainerSafety.Parse(encoded, out DurableEndpointLockRecord loaded),
                "A schema-two stable claim must parse.");
            TestAssert.False(loaded.IsLegacyIdentity, "Schema two must remain token-authoritative.");
            TestAssert.Equal(StationToken, loaded.StationToken, "Station token must round-trip.");
            TestAssert.Equal(EndpointAToken, loaded.EndpointToken, "Endpoint token must round-trip.");
            TestAssert.Equal(
                WorldObjectIdentity.EndpointId(EndpointAToken),
                loaded.EndpointId,
                "Compatibility endpoint text must be stable, not a numeric ZDOID.");
            TestAssert.True(original.MatchesExact(loaded), "Stable claim round-trip must be exact.");
        }

        public static void LegacyLockSchemaRemainsExplicitAndFailClosed()
        {
#pragma warning disable CS0618
            var original = new DurableEndpointLockRecord(
                "operation", "module", "7:8", "9:10", JournalHash,
                "before", "after", DurableEndpointLockPhase.Prepared);
#pragma warning restore CS0618
            string encoded = DurableContainerSafety.Serialize(original);
            TestAssert.Equal(
                DurableEndpointLockReadState.Valid,
                DurableContainerSafety.Parse(encoded, out DurableEndpointLockRecord loaded),
                "A bounded schema-one claim must remain parseable for recovery.");
            TestAssert.True(loaded.IsLegacyIdentity, "Schema one must stay explicitly legacy.");
            TestAssert.Equal("7:8", loaded.StationId, "Legacy station ID must not be guessed.");
            TestAssert.Equal("9:10", loaded.EndpointId, "Legacy endpoint ID must not be guessed.");
            TestAssert.Equal(string.Empty, loaded.EndpointToken, "Legacy records have no token.");
        }

        public static void StableHeaderSchemaPersistsTokensAndDropsRawCaches()
        {
            var bindings = new List<DurableOperationEndpointBinding>
            {
                new DurableOperationEndpointBinding(
                    EndpointAToken, new ZDOID(7L, 8U), "a-before", "a-after"),
                new DurableOperationEndpointBinding(
                    EndpointBToken, new ZDOID(9L, 10U), "b-before", "b-after")
            };
            var original = new DurableOperationHeaderRecord(
                "operation", "module", StationToken, "5:6", "journal.key", JournalHash,
                true, DurableOperationPhase.Committed, bindings);
            string encoded = DurableOperationCoordinator.SerializeHeader(original);
            TestAssert.Equal(
                DurableOperationHeaderReadState.Valid,
                DurableOperationCoordinator.ParseHeader(
                    encoded, out DurableOperationHeaderRecord loaded),
                "A schema-two stable header must parse.");
            TestAssert.False(loaded.IsLegacyIdentity, "Schema two must remain token-authoritative.");
            TestAssert.Equal(StationToken, loaded.StationToken, "Station token must round-trip.");
            TestAssert.Equal(
                string.Empty, loaded.StationId,
                "A raw station ZDOID cache must not be persisted.");
            TestAssert.Equal(EndpointAToken, loaded.Endpoints[0].EndpointToken,
                "Endpoint token order must round-trip.");
            TestAssert.True(loaded.Endpoints[0].EndpointId.IsNone(),
                "A raw endpoint ZDOID cache must not be persisted.");
        }

        public static void LegacyHeaderSchemaRemainsParseableWithoutRebinding()
        {
            var bindings = new List<DurableOperationEndpointBinding>
            {
                new DurableOperationEndpointBinding(
                    new ZDOID(7L, 8U), "before", "after")
            };
            var original = new DurableOperationHeaderRecord(
                "operation", "module", "5:6", "journal.key", JournalHash,
                false, DurableOperationPhase.Claiming, bindings);
            string encoded = DurableOperationCoordinator.SerializeHeader(original);
            TestAssert.Equal(
                DurableOperationHeaderReadState.Valid,
                DurableOperationCoordinator.ParseHeader(
                    encoded, out DurableOperationHeaderRecord loaded),
                "A bounded schema-one header must remain parseable for exact local recovery.");
            TestAssert.True(loaded.IsLegacyIdentity, "Schema one must stay explicitly legacy.");
            TestAssert.Equal("5:6", loaded.StationId, "Legacy station ID must remain exact.");
            TestAssert.Equal(new ZDOID(7L, 8U), loaded.Endpoints[0].EndpointId,
                "Legacy endpoint ID must remain exact.");
            TestAssert.Equal(string.Empty, loaded.Endpoints[0].EndpointToken,
                "Legacy bindings must never invent a stable token.");
        }

        public static void IdentityUpgradeEnvelopePreservesOldToNewMappingWithoutAuthority()
        {
            var bindings = new List<DurableOperationEndpointBinding>
            {
                new DurableOperationEndpointBinding(
                    new ZDOID(7L, 8U), "a-before", "a-after"),
                new DurableOperationEndpointBinding(
                    new ZDOID(9L, 10U), "b-before", "b-after")
            };
            var legacy = new DurableOperationHeaderRecord(
                "operation", "module", "5:6", "journal.key", JournalHash,
                true, DurableOperationPhase.Claiming, bindings);
            var tokens = new Dictionary<ZDOID, string>
            {
                [new ZDOID(7L, 8U)] = EndpointAToken,
                [new ZDOID(9L, 10U)] = EndpointBToken
            };

            string encoded = DurableOperationCoordinator.SerializeIdentityUpgradeEnvelope(
                legacy, StationToken, tokens);
            TestAssert.Equal(
                DurableOperationHeaderReadState.Corrupt,
                DurableOperationCoordinator.ParseHeader(encoded, out _),
                "A transition envelope must never be accepted as the authoritative header.");
            TestAssert.True(
                DurableOperationCoordinator.TryParseIdentityUpgradeEnvelopeForTests(
                    encoded,
                    out DurableOperationHeaderRecord loaded,
                    out string loadedStationToken,
                    out IReadOnlyDictionary<ZDOID, string> loadedTokens),
                "A bounded transition envelope must remain retryable.");
            TestAssert.True(loaded.IsLegacyIdentity,
                "The retry projection must retain schema-one authority evidence.");
            TestAssert.Equal("5:6", loaded.StationId,
                "The old station ID must remain available only as correlation evidence.");
            TestAssert.Equal(StationToken, loadedStationToken,
                "The proposed station token must round-trip exactly.");
            TestAssert.Equal(EndpointAToken, loadedTokens[new ZDOID(7L, 8U)],
                "The first old endpoint must retain its exact proposed token.");
            TestAssert.Equal(EndpointBToken, loadedTokens[new ZDOID(9L, 10U)],
                "The second old endpoint must retain its exact proposed token.");
        }

        public static void LegacyClaimRewriteAcceptsOnlyExactOldOrExactNewEvidence()
        {
#pragma warning disable CS0618
            var legacy = new DurableEndpointLockRecord(
                "operation", "module", "5:6", "7:8", JournalHash,
                "before", "after", DurableEndpointLockPhase.Prepared);
            var foreign = new DurableEndpointLockRecord(
                "different", "module", "5:6", "7:8", JournalHash,
                "before", "after", DurableEndpointLockPhase.Prepared);
#pragma warning restore CS0618
            DurableEndpointLockRecord stable = DurableEndpointLockRecord.CreateStable(
                "operation", "module", StationToken, EndpointAToken, JournalHash,
                "before", "after", DurableEndpointLockPhase.Prepared);

            TestAssert.Equal(
                LegacyClaimRewriteDecision.RewriteLegacy,
                DurableContainerSafety.EvaluateLegacyClaimRewrite(
                    legacy, stable, DurableEndpointLockReadState.Valid, legacy),
                "An exact old claim must be rewritten once.");
            TestAssert.Equal(
                LegacyClaimRewriteDecision.AlreadyStable,
                DurableContainerSafety.EvaluateLegacyClaimRewrite(
                    legacy, stable, DurableEndpointLockReadState.Valid, stable),
                "A crash retry must accept the exact stable claim idempotently.");
            TestAssert.Equal(
                LegacyClaimRewriteDecision.Reject,
                DurableContainerSafety.EvaluateLegacyClaimRewrite(
                    legacy, stable, DurableEndpointLockReadState.Valid, foreign),
                "A different valid claim must never be overwritten.");
            TestAssert.Equal(
                LegacyClaimRewriteDecision.Reject,
                DurableContainerSafety.EvaluateLegacyClaimRewrite(
                    legacy, stable, DurableEndpointLockReadState.Invalid, legacy),
                "Corrupt persisted evidence must fail closed even when object fields resemble the old claim.");
        }

        public static void RemappedLegacyOrphanCleanupAcceptsOnlySafeCrashStates()
        {
            var bindings = new List<DurableOperationEndpointBinding>
            {
                new DurableOperationEndpointBinding(
                    new ZDOID(7L, 8U), "before", "after")
            };
            var unpublished = new DurableOperationHeaderRecord(
                "operation", "module", "5:6", "journal.key", JournalHash,
                false, DurableOperationPhase.Claiming, bindings);
            DurableOperationHeaderRecord published = unpublished.WithJournalPublished();
            DurableOperationHeaderRecord committed =
                published.WithPhase(DurableOperationPhase.Committed);
            DurableOperationHeaderRecord rolledBack =
                published.WithPhase(DurableOperationPhase.RolledBackExact);

            TestAssert.True(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    unpublished, envelopePresent: false, journalPresent: false),
                "A journal-free unpublished Claiming header cannot have begun inventory mutation.");
            TestAssert.False(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    published, envelopePresent: false, journalPresent: false),
                "A published Claiming header missing its journal must never be destructively cleared.");
            TestAssert.True(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    committed, envelopePresent: false, journalPresent: false),
                "A Committed header may clear only after independent After-fingerprint proof.");
            TestAssert.True(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    rolledBack, envelopePresent: false, journalPresent: false),
                "A RolledBackExact header may clear only after independent Before-fingerprint proof.");
            TestAssert.False(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    committed, envelopePresent: true, journalPresent: false),
                "A partially published schema-three mapping must complete, not take the raw-header cleanup path.");
            TestAssert.False(
                DurableOperationCoordinator.IsRemappedLegacyOrphanCleanupEligible(
                    committed, envelopePresent: false, journalPresent: true),
                "Any surviving exact journal must remain authoritative recovery evidence.");
        }

        public static void LegacyClaimDiscoveryRequiresExactOneToOneCoverage()
        {
            var bindings = new List<DurableOperationEndpointBinding>
            {
                new DurableOperationEndpointBinding(
                    new ZDOID(7L, 8U), "a-before", "a-after"),
                new DurableOperationEndpointBinding(
                    new ZDOID(9L, 10U), "b-before", "b-after")
            };
            var header = new DurableOperationHeaderRecord(
                "operation", "module", "5:6", "journal.key", JournalHash,
                false, DurableOperationPhase.Claiming, bindings);
#pragma warning disable CS0618
            var claimA = new DurableEndpointLockRecord(
                "operation", "module", "5:6", "7:8", JournalHash,
                "a-before", "a-after", DurableEndpointLockPhase.Prepared);
            var claimB = new DurableEndpointLockRecord(
                "operation", "module", "5:6", "9:10", JournalHash,
                "b-before", "b-after", DurableEndpointLockPhase.Prepared);
#pragma warning restore CS0618
            var complete = new List<KeyValuePair<ZDOID, DurableEndpointLockRecord>>
            {
                new KeyValuePair<ZDOID, DurableEndpointLockRecord>(
                    new ZDOID(101L, 1U), claimA),
                new KeyValuePair<ZDOID, DurableEndpointLockRecord>(
                    new ZDOID(102L, 2U), claimB)
            };
            TestAssert.True(
                DurableOperationCoordinator.TryMatchLegacyClaimCandidateIds(
                    header, complete, complete.Count,
                    out IReadOnlyList<ZDOID> exactIds, out string completeFailure),
                "Every binding with one exact physical claim must be discoverable: " +
                completeFailure);
            TestAssert.Equal(new ZDOID(101L, 1U), exactIds[0],
                "Candidates must follow immutable header binding order.");
            TestAssert.Equal(new ZDOID(102L, 2U), exactIds[1],
                "Candidates must follow immutable header binding order.");

            var incomplete = new List<KeyValuePair<ZDOID, DurableEndpointLockRecord>>
            {
                complete[0]
            };
            TestAssert.False(
                DurableOperationCoordinator.TryMatchLegacyClaimCandidateIds(
                    header, incomplete, incomplete.Count, out _, out _),
                "A missing physical claim must retain the header and fail closed.");

            var duplicate = new List<KeyValuePair<ZDOID, DurableEndpointLockRecord>>(complete)
            {
                new KeyValuePair<ZDOID, DurableEndpointLockRecord>(
                    new ZDOID(103L, 3U), claimA)
            };
            TestAssert.False(
                DurableOperationCoordinator.TryMatchLegacyClaimCandidateIds(
                    header, duplicate, duplicate.Count, out _, out _),
                "Two physical claims matching one binding must be ambiguous and fail closed.");
        }

        public static void LegacyClaimDiscoveryBoundsAndLoadedEndpointsFailClosed()
        {
            TestAssert.True(
                DurableOperationCoordinator.IsLegacyClaimIndexCountWithinBounds(16384),
                "The final bounded claim-index entry must remain inspectable.");
            TestAssert.False(
                DurableOperationCoordinator.IsLegacyClaimIndexCountWithinBounds(16385),
                "An over-cap claim index must not be scanned.");

            var exactIds = new List<ZDOID>
            {
                new ZDOID(101L, 1U),
                new ZDOID(102L, 2U)
            };
            TestAssert.False(
                DurableOperationCoordinator.TryRequireLoadedLegacyClaimCandidates(
                    exactIds, id => id == exactIds[0], out _),
                "An exact claim on an unloaded endpoint must retain the operation.");
            TestAssert.True(
                DurableOperationCoordinator.TryRequireLoadedLegacyClaimCandidates(
                    exactIds, _ => true, out string loadedFailure),
                "Two distinct exact loaded endpoints must pass: " + loadedFailure);
            TestAssert.False(
                DurableOperationCoordinator.TryRequireLoadedLegacyClaimCandidates(
                    new List<ZDOID> { exactIds[0], exactIds[0] }, _ => true, out _),
                "Duplicate current endpoint IDs must fail closed even when loaded.");
        }
    }
}
