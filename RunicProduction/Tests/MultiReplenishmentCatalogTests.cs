using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicProduction.Contracts;
using RunicProduction.Core;
using RunicProduction.Integration;
using UnityEngine;

namespace RunicProduction.Tests
{
    internal static class MultiReplenishmentCatalogTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() =>
            new List<KeyValuePair<string, Action>>
            {
                Case("multi-destination hard and soft bounds are exact", BoundsAreExact),
                Case("multi-destination identity stays stable across lifecycle changes", IdentityIsStable),
                Case("multi-destination target authorizations are globally bounded", TargetsAreGloballyBounded),
                Case("multi-destination round robin skips non-active links", RoundRobinSkipsInactive),
                Case("target and destination cursors advance in one atomic revision", CursorsAdvanceAtomically),
                Case("one chest may be authorized independently by several stations", SharedChestAcrossStationsIsIndependent),
                Case("legacy singleton migration is staged in slot zero", LegacyMigrationIsPure),
                Case("legacy destination identity upgrades one exact slot without semantic refresh",
                    LegacyIdentityUpgradeIsGuarded),
                Case("catalog index and slots round-trip with exact cross-checks", PersistenceCrossChecksExactIdentity),
                Case("catalog transitions resolve every single-slot crash boundary", TransitionsAreCrashSafe),
                Case("catalog persistence rejects aggregate records over 64 KiB", AggregateStorageIsBounded),
                Case("empty replenishment plans round-trip with an exact principal",
                    EmptyPlansRoundTrip),
                Case("legacy plan principals migrate only from unanimous nonempty targets",
                    LegacyPlanPrincipalsAreGuarded)
            }.AsReadOnly();

        private static void EmptyPlansRoundTrip()
        {
            StoredProductionLink link = Link(31, "link.empty-plan");
            ReplenishmentPlan plan = Plan(link, "station.multi", 1);
            Equal(0, plan.Targets.Count);
            Equal(77L, plan.AuthorizedPlayerId);
            Equal("Tester", plan.AuthorizedPlayerName);

            string encodedPlan = ReplenishmentPlanStore.Serialize(plan);
            Equal(
                StoredRecordState.Valid,
                ReplenishmentPlanStore.Parse(
                    encodedPlan,
                    out ReplenishmentPlan parsedPlan));
            Equal(0, parsedPlan.Targets.Count);
            Equal(77L, parsedPlan.AuthorizedPlayerId);
            Equal("Tester", parsedPlan.AuthorizedPlayerName);

            MultiReplenishmentCatalog catalog = Empty();
            True(MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                link,
                plan,
                16,
                out catalog,
                out _,
                out _));
            MultiReplenishmentCatalogPublication publication =
                MultiReplenishmentCatalogStore.Prepare(catalog);
            Equal(
                StoredRecordState.Valid,
                MultiReplenishmentCatalogStore.Parse(
                    publication.FinalIndexRecord,
                    publication.SlotRecords,
                    out MultiReplenishmentCatalog parsedCatalog,
                    out bool wasTransition));
            True(!wasTransition);
            Equal(1, parsedCatalog.Destinations.Count);
            Equal(0, parsedCatalog.Destinations[0].Plan.Targets.Count);
            Equal(77L, parsedCatalog.Destinations[0].Plan.AuthorizedPlayerId);
            Equal("Tester", parsedCatalog.Destinations[0].Plan.AuthorizedPlayerName);
        }

        private static void LegacyPlanPrincipalsAreGuarded()
        {
            StoredProductionLink link = Link(32, "link.legacy-principal");
            ReplenishmentPlan plan = Plan(
                link,
                "station.multi",
                1,
                Target("LegacyFood", 4));
            string legacyStandalone = Convert.ToBase64String(
                LegacyPlanBytes(plan, plan.Targets, includeSchema: true));
            Equal(
                StoredRecordState.Valid,
                ReplenishmentPlanStore.Parse(
                    legacyStandalone,
                    out ReplenishmentPlan migrated));
            Equal(77L, migrated.AuthorizedPlayerId);
            Equal("Tester", migrated.AuthorizedPlayerName);

            MethodInfo readEmbedded = typeof(MultiReplenishmentCatalogStore).GetMethod(
                "ReadPlan",
                BindingFlags.Static | BindingFlags.NonPublic);
            True(readEmbedded != null);
            var embeddedPackage = new ZPackage(
                LegacyPlanBytes(plan, plan.Targets, includeSchema: false));
            var embedded = (ReplenishmentPlan)readEmbedded.Invoke(
                null,
                new object[] { embeddedPackage, 2, 77L });
            Equal(77L, embedded.AuthorizedPlayerId);
            Equal("Tester", embedded.AuthorizedPlayerName);

            ReplenishmentPlan empty = Plan(link, "station.multi", 1);
            string legacyEmpty = Convert.ToBase64String(
                LegacyPlanBytes(empty, empty.Targets, includeSchema: true));
            Equal(
                StoredRecordState.Invalid,
                ReplenishmentPlanStore.Parse(legacyEmpty, out _));
            Throws<TargetInvocationException>(() => readEmbedded.Invoke(
                null,
                new object[]
                {
                    new ZPackage(LegacyPlanBytes(
                        empty,
                        empty.Targets,
                        includeSchema: false)),
                    2,
                    77L
                }));

            var otherPrincipal = new ReplenishmentTargetAuthorization(
                "OtherLegacyFood",
                ReplenishmentProducerKind.DirectRecipe,
                "OtherRecipe",
                Signature(7),
                1,
                "$piece_cauldron",
                1,
                88L,
                "Other",
                new[] { new ReplenishmentRequirement("Carrot", 1) });
            var mixed = new[] { plan.Targets[0], otherPrincipal };
            string legacyMixed = Convert.ToBase64String(
                LegacyPlanBytes(plan, mixed, includeSchema: true));
            Equal(
                StoredRecordState.Invalid,
                ReplenishmentPlanStore.Parse(legacyMixed, out _));
        }

        private static void CursorsAdvanceAtomically()
        {
            MultiReplenishmentCatalog catalog = Empty();
            StoredProductionLink firstLink = Link(0);
            var firstPlan = new ReplenishmentPlan(
                "plan.atomic",
                1,
                0,
                "piece_cauldron",
                ReplenishmentProducerKind.DirectRecipe,
                ReplenishmentPlanStore.Bind(firstLink, "station.multi"),
                77L,
                "Tester",
                new[] { Target("FoodA", 1), Target("FoodB", 2) });
            True(MultiReplenishmentCatalogPolicy.TryAdd(
                catalog, firstLink, firstPlan, 16,
                out catalog, out ReplenishmentDestinationRecord first, out _));
            True(Add(catalog, 1, 16, out catalog, out ReplenishmentDestinationRecord second));
            int beforeRevision = catalog.Revision;
            MultiReplenishmentCatalogPublication before =
                MultiReplenishmentCatalogStore.Prepare(catalog);

            True(MultiReplenishmentCatalogPolicy.TryAdvanceAfterSelection(
                catalog,
                first.LinkId,
                first.Plan.WithCursor(1),
                out MultiReplenishmentCatalog advanced,
                out ReplenishmentDestinationRecord advancedFirst,
                out _));
            Equal(beforeRevision + 1, advanced.Revision);
            Equal(1, advancedFirst.Plan.Cursor);
            Equal(second.Ordinal, advanced.DestinationCursorOrdinal);
            MultiReplenishmentCatalogPublication after =
                MultiReplenishmentCatalogStore.Prepare(advanced);
            string transition = MultiReplenishmentCatalogStore.CreateTransitionIndex(
                before, after, out int changedSlot);
            Equal(first.Slot, changedSlot);
            string[] bodies = before.SlotRecords.ToArray();
            bodies[changedSlot] = after.SlotRecord(changedSlot);
            Equal(StoredRecordState.Valid,
                MultiReplenishmentCatalogStore.Parse(
                    transition, bodies, out MultiReplenishmentCatalog recovered, out _));
            True(MultiReplenishmentCatalogStore.CatalogsEqual(advanced, recovered));
        }

        private static void SharedChestAcrossStationsIsIndependent()
        {
            StoredProductionLink first = Link(7, "station-a-link");
            StoredProductionLink second = Link(7, "station-b-link");
            MultiReplenishmentCatalog stationA =
                MultiReplenishmentCatalogPolicy.CreateEmpty("catalog.a", "station.a");
            MultiReplenishmentCatalog stationB =
                MultiReplenishmentCatalogPolicy.CreateEmpty("catalog.b", "station.b");
            True(MultiReplenishmentCatalogPolicy.TryAdd(
                stationA,
                first,
                Plan(first, "station.a", 1, Target("FoodA", 1)),
                8,
                out stationA,
                out _,
                out _));
            True(MultiReplenishmentCatalogPolicy.TryAdd(
                stationB,
                second,
                Plan(second, "station.b", 1, Target("FoodB", 2)),
                8,
                out stationB,
                out _,
                out _));
            Equal(first.Target, stationA.Destinations[0].Link.Target);
            Equal(second.Target, stationB.Destinations[0].Link.Target);
            True(!string.Equals(
                stationA.Destinations[0].LinkId,
                stationB.Destinations[0].LinkId,
                StringComparison.Ordinal));
        }

        private static void BoundsAreExact()
        {
            MultiReplenishmentCatalog catalog = Empty();
            True(Add(catalog, 0, 1, out catalog, out ReplenishmentDestinationRecord first));
            Equal(0, first.Slot);
            Equal(1L, first.Ordinal);

            True(!MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                Link(1),
                Plan(Link(1), "station.multi", 1, Target("Food01", 1)),
                1,
                out MultiReplenishmentCatalog unchanged,
                out _,
                out ReplenishmentCatalogChangeCode softCode));
            Equal(ReplenishmentCatalogChangeCode.SoftLimitReached, softCode);
            True(ReferenceEquals(catalog, unchanged));

            // Lowering the setting blocks only additions: state, refresh, and remove still apply.
            True(MultiReplenishmentCatalogPolicy.TrySetState(
                catalog,
                first.LinkId,
                ReplenishmentDestinationState.NeedsRefresh,
                ProductionStopCode.Ready,
                out catalog,
                out _,
                out _));
            StoredProductionLink refreshedLink = Link(0, first.LinkId, first.Link.Revision);
            True(MultiReplenishmentCatalogPolicy.TryRefresh(
                catalog,
                first.LinkId,
                refreshedLink,
                Plan(refreshedLink, "station.multi", 2, Target("Food00", 2)),
                out catalog,
                out _,
                out _));
            True(MultiReplenishmentCatalogPolicy.TryRemove(
                catalog, first.LinkId, out catalog, out _, out _));
            Equal(0, catalog.Destinations.Count);

            catalog = Empty();
            for (int index = 0; index < MultiReplenishmentCatalog.HardMaximumDestinations; index++)
                True(Add(catalog, index, 16, out catalog, out _));
            Equal(16, catalog.Destinations.Count);
            True(!MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                Link(99),
                Plan(Link(99), "station.multi", 1, Target("Overflow", 99)),
                16,
                out _,
                out _,
                out ReplenishmentCatalogChangeCode hardCode));
            Equal(ReplenishmentCatalogChangeCode.HardLimitReached, hardCode);

            Equal(16, MultiReplenishmentCatalogStore.FixedSlotStorageKeys.Count);
            Equal(16, MultiReplenishmentCatalogStore.FixedSlotStorageKeys.Distinct().Count());
            True(!MultiReplenishmentCatalogStore.FixedSlotStorageKeys.Contains(
                MultiReplenishmentCatalogStore.IndexStorageKey));
        }

        private static void IdentityIsStable()
        {
            MultiReplenishmentCatalog catalog = Empty();
            True(Add(catalog, 0, 16, out catalog, out ReplenishmentDestinationRecord first));
            True(Add(catalog, 1, 16, out catalog, out ReplenishmentDestinationRecord second));
            True(MultiReplenishmentCatalogPolicy.TryRemove(
                catalog, first.LinkId, out catalog, out _, out _));
            True(Add(catalog, 2, 16, out catalog, out ReplenishmentDestinationRecord third));
            Equal(0, third.Slot);
            Equal(3L, third.Ordinal);

            StoredProductionLink exact = third.Link;
            var refreshedLink = Link(2, exact.LinkId, exact.Revision + 1);
            True(MultiReplenishmentCatalogPolicy.TryRefresh(
                catalog,
                third.LinkId,
                refreshedLink,
                Plan(refreshedLink, "station.multi", 2, Target("Food02", 2)),
                out catalog,
                out ReplenishmentDestinationRecord refreshed,
                out _));
            Equal(third.LinkId, refreshed.LinkId);
            Equal(third.Slot, refreshed.Slot);
            Equal(third.Ordinal, refreshed.Ordinal);
            Equal(third.RecordRevision + 1, refreshed.RecordRevision);

            True(MultiReplenishmentCatalogPolicy.TrySetState(
                catalog,
                refreshed.LinkId,
                ReplenishmentDestinationState.Draining,
                ProductionStopCode.Ready,
                out catalog,
                out ReplenishmentDestinationRecord draining,
                out _));
            Equal(refreshed.Ordinal, draining.Ordinal);
            Equal(refreshed.RecordRevision + 1, draining.RecordRevision);

            // Egress is a defensive copy of the mutable compatibility DTO.
            StoredProductionLink escaped = draining.Link;
            escaped.LinkId = "mutated";
            escaped.Revision = 999;
            True(catalog.TryGetByTarget(Link(2).Target, out ReplenishmentDestinationRecord durable));
            Equal(refreshed.LinkId, durable.LinkId);
            Equal(refreshed.Link.Revision, durable.Link.Revision);

            True(!MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                Link(2, "different-link", 1),
                Plan(Link(2, "different-link", 1), "station.multi", 1, Target("Other", 1)),
                16,
                out _,
                out _,
                out ReplenishmentCatalogChangeCode duplicateTarget));
            Equal(ReplenishmentCatalogChangeCode.DuplicateTarget, duplicateTarget);
            True(catalog.TryGetByLinkId(second.LinkId, out _));
        }

        private static void TargetsAreGloballyBounded()
        {
            MultiReplenishmentCatalog catalog = Empty();
            StoredProductionLink first = Link(0);
            ReplenishmentTargetAuthorization[] targets = Enumerable.Range(0, 32)
                .Select(index => Target("Global" + index.ToString("00"), index))
                .ToArray();
            True(MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                first,
                Plan(first, "station.multi", 1, targets),
                16,
                out catalog,
                out _,
                out _));
            Equal(32, catalog.TargetAuthorizationCount);

            StoredProductionLink second = Link(1);
            True(!MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                second,
                Plan(second, "station.multi", 1, Target("OneTooMany", 33)),
                16,
                out _,
                out _,
                out ReplenishmentCatalogChangeCode code));
            Equal(ReplenishmentCatalogChangeCode.TargetAuthorizationLimitReached, code);
        }

        private static void RoundRobinSkipsInactive()
        {
            MultiReplenishmentCatalog catalog = Empty();
            True(Add(catalog, 0, 16, out catalog, out ReplenishmentDestinationRecord first));
            True(Add(catalog, 1, 16, out catalog, out ReplenishmentDestinationRecord second));
            True(Add(catalog, 2, 16, out catalog, out ReplenishmentDestinationRecord third));
            Equal(first.Ordinal, catalog.ActiveRoundRobinOrder()[0].Ordinal);
            True(MultiReplenishmentCatalogPolicy.TryAdvanceCursorAfter(
                catalog, first.Ordinal, out catalog, out _));
            Equal(second.Ordinal, catalog.ActiveRoundRobinOrder()[0].Ordinal);
            True(MultiReplenishmentCatalogPolicy.TrySetState(
                catalog,
                second.LinkId,
                ReplenishmentDestinationState.Draining,
                ProductionStopCode.Ready,
                out catalog,
                out _,
                out _));
            IReadOnlyList<ReplenishmentDestinationRecord> order =
                catalog.ActiveRoundRobinOrder();
            Equal(2, order.Count);
            Equal(third.Ordinal, order[0].Ordinal);
            Equal(first.Ordinal, order[1].Ordinal);
        }

        private static void LegacyMigrationIsPure()
        {
            StoredProductionLink legacy = Link(7, "legacy-link", 4);
            ReplenishmentPlan legacyPlan = Plan(
                legacy,
                "station.legacy",
                9,
                Target("LegacyFood", 1));
            MultiReplenishmentCatalog migrated =
                MultiReplenishmentCatalogPolicy.FromLegacySingleton(
                    "catalog.legacy",
                    "station.legacy",
                    legacy,
                    legacyPlan);
            Equal(1, migrated.Destinations.Count);
            Equal(0, migrated.Destinations[0].Slot);
            Equal(1L, migrated.Destinations[0].Ordinal);
            Equal(2L, migrated.NextOrdinal);
            Equal("legacy-link", migrated.Destinations[0].LinkId);
            Equal(ReplenishmentDestinationState.Active, migrated.Destinations[0].State);
            True(MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated, legacy, StoredRecordState.Valid, legacyPlan));
            True(MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated, null, StoredRecordState.Valid, legacyPlan));
            True(MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated, legacy, StoredRecordState.Absent, null));
            True(MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated, null, StoredRecordState.Absent, null));
            True(!MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated,
                Link(8, "other-link", 1),
                StoredRecordState.Absent,
                null));
            True(!MultiReplenishmentRuntimeSupport.LegacyDebrisMatchesCatalog(
                migrated,
                null,
                StoredRecordState.Invalid,
                null));
        }

        private static void LegacyIdentityUpgradeIsGuarded()
        {
            const string token = "0123456789abcdef0123456789abcdef";
            StoredProductionLink legacy = Link(7, "legacy-upgrade-link", 4);
            ReplenishmentPlan legacyPlan = Plan(
                legacy,
                "station.legacy.opaque",
                9,
                Target("LegacyUpgradeFood", 1));
            MultiReplenishmentCatalog catalog =
                MultiReplenishmentCatalogPolicy.FromLegacySingleton(
                    "catalog.legacy.upgrade",
                    "station.legacy.opaque",
                    legacy,
                    legacyPlan);
            True(MultiReplenishmentCatalogPolicy.TrySetState(
                catalog,
                legacy.LinkId,
                ReplenishmentDestinationState.Faulted,
                ProductionStopCode.OutputFull,
                out catalog,
                out ReplenishmentDestinationRecord faulted,
                out _));

            var resolved = new StoredProductionLink
            {
                LinkId = legacy.LinkId,
                Role = legacy.Role,
                TargetToken = token,
                TargetPrefabHash = 328745978,
                Target = new ZDOID(900L, 123U),
                ExpectedPosition = legacy.ExpectedPosition,
                OwnerId = legacy.OwnerId,
                StationOwnerId = legacy.StationOwnerId,
                TargetOwnerId = legacy.TargetOwnerId,
                Revision = legacy.Revision
            };
            True(ReplenishmentPlanStore.TryPrepareLegacyTargetRebind(
                faulted.Plan,
                faulted.Link,
                resolved,
                out ReplenishmentPlan preparedPlan,
                out string encodedPlan));
            Equal(faulted.Plan.PlanId, preparedPlan.PlanId);
            Equal(faulted.Plan.Revision, preparedPlan.Revision);
            Equal(faulted.Plan.Cursor, preparedPlan.Cursor);
            Equal(faulted.Plan.Link.StationId, preparedPlan.Link.StationId);
            Equal(token, preparedPlan.Link.TargetId);
            Equal(token, preparedPlan.Link.TargetToken);
            Equal(328745978, preparedPlan.Link.TargetPrefabHash);
            Equal(StoredRecordState.Valid,
                ReplenishmentPlanStore.Parse(encodedPlan, out ReplenishmentPlan parsedPlan));
            Equal(token, parsedPlan.Link.TargetToken);

            int catalogRevision = catalog.Revision;
            int recordRevision = faulted.RecordRevision;
            True(MultiReplenishmentCatalogPolicy.TryUpgradeLegacyDestinationIdentity(
                catalog,
                legacy.LinkId,
                resolved,
                out MultiReplenishmentCatalog upgradedCatalog,
                out ReplenishmentDestinationRecord upgraded,
                out ReplenishmentCatalogChangeCode code));
            Equal(ReplenishmentCatalogChangeCode.Applied, code);
            Equal(catalogRevision + 1, upgradedCatalog.Revision);
            Equal(recordRevision + 1, upgraded.RecordRevision);
            Equal(ReplenishmentDestinationState.Faulted, upgraded.State);
            Equal(ProductionStopCode.OutputFull, upgraded.FaultCode);
            Equal(legacy.Revision, upgraded.Link.Revision);
            Equal(resolved.Target, upgraded.Link.Target);
            Equal(token, upgraded.Link.TargetToken);
            Equal(token, upgraded.Plan.Link.TargetId);
            Equal(faulted.Plan.PlanId, upgraded.Plan.PlanId);
            Equal(faulted.Plan.Revision, upgraded.Plan.Revision);
            Equal(faulted.Plan.Cursor, upgraded.Plan.Cursor);
            Equal(faulted.Plan.Link.StationId, upgraded.Plan.Link.StationId);
            MultiReplenishmentCatalogPublication durable =
                MultiReplenishmentCatalogStore.Prepare(upgradedCatalog);
            Equal(StoredRecordState.Valid,
                MultiReplenishmentCatalogStore.Parse(
                    durable.FinalIndexRecord,
                    durable.SlotRecords,
                    out MultiReplenishmentCatalog durableRoundTrip,
                    out bool wasTransition));
            True(!wasTransition);
            Equal(
                token,
                durableRoundTrip.Destinations[0].Link.TargetToken);
            Equal(
                token,
                durableRoundTrip.Destinations[0].Plan.Link.TargetToken);

            var alteredOwner = new StoredProductionLink
            {
                LinkId = legacy.LinkId,
                Role = legacy.Role,
                TargetToken = token,
                TargetPrefabHash = resolved.TargetPrefabHash,
                Target = resolved.Target,
                ExpectedPosition = legacy.ExpectedPosition,
                OwnerId = legacy.OwnerId + 1L,
                StationOwnerId = legacy.StationOwnerId,
                TargetOwnerId = legacy.TargetOwnerId,
                Revision = legacy.Revision
            };
            True(!MultiReplenishmentCatalogPolicy.TryUpgradeLegacyDestinationIdentity(
                catalog,
                legacy.LinkId,
                alteredOwner,
                out MultiReplenishmentCatalog rejectedCatalog,
                out _,
                out code));
            Equal(ReplenishmentCatalogChangeCode.LinkIdentityChanged, code);
            True(ReferenceEquals(catalog, rejectedCatalog));
        }

        private static void PersistenceCrossChecksExactIdentity()
        {
            MultiReplenishmentCatalog catalog = Empty();
            True(Add(catalog, 0, 16, out catalog, out _));
            True(Add(catalog, 1, 16, out catalog, out _));
            MultiReplenishmentCatalogPublication publication =
                MultiReplenishmentCatalogStore.Prepare(catalog);
            True(publication.AggregateEncodedCharacters <= 64 * 1024);
            True(publication.AggregateDecodedBytes <= 64 * 1024);
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                publication.FinalIndexRecord,
                publication.SlotRecords,
                out MultiReplenishmentCatalog loaded,
                out bool transition));
            True(!transition);
            True(MultiReplenishmentCatalogStore.CatalogsEqual(catalog, loaded));

            string[] damaged = publication.SlotRecords.ToArray();
            byte[] body = Convert.FromBase64String(damaged[0]);
            body[body.Length - 1] ^= 0x01;
            damaged[0] = Convert.ToBase64String(body);
            Equal(StoredRecordState.Invalid, MultiReplenishmentCatalogStore.Parse(
                publication.FinalIndexRecord, damaged, out _, out _));

            string[] swapped = publication.SlotRecords.ToArray();
            (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
            Equal(StoredRecordState.Invalid, MultiReplenishmentCatalogStore.Parse(
                publication.FinalIndexRecord, swapped, out _, out _));

            byte[] index = Convert.FromBase64String(publication.FinalIndexRecord);
            for (int length = 1; length < index.Length; length++)
                Equal(StoredRecordState.Invalid, MultiReplenishmentCatalogStore.Parse(
                    Convert.ToBase64String(index.Take(length).ToArray()),
                    publication.SlotRecords,
                    out _,
                    out _));
            Equal(StoredRecordState.Invalid, MultiReplenishmentCatalogStore.Parse(
                "not-base64", publication.SlotRecords, out _, out _));
        }

        private static void TransitionsAreCrashSafe()
        {
            MultiReplenishmentCatalog oldCatalog = Empty();
            MultiReplenishmentCatalogPublication oldPublication =
                MultiReplenishmentCatalogStore.Prepare(oldCatalog);
            True(Add(oldCatalog, 0, 16, out MultiReplenishmentCatalog addedCatalog, out _));
            MultiReplenishmentCatalogPublication addedPublication =
                MultiReplenishmentCatalogStore.Prepare(addedCatalog);
            string addTransition = MultiReplenishmentCatalogStore.CreateTransitionIndex(
                oldPublication, addedPublication, out int addSlot);
            Equal(0, addSlot);
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                addTransition,
                oldPublication.SlotRecords,
                out MultiReplenishmentCatalog beforeAddSlot,
                out bool addWasTransition));
            True(addWasTransition);
            True(MultiReplenishmentCatalogStore.CatalogsEqual(oldCatalog, beforeAddSlot));
            string[] afterAddSlot = oldPublication.SlotRecords.ToArray();
            afterAddSlot[addSlot] = addedPublication.SlotRecord(addSlot);
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                addTransition,
                afterAddSlot,
                out MultiReplenishmentCatalog committedAdd,
                out _));
            True(MultiReplenishmentCatalogStore.CatalogsEqual(addedCatalog, committedAdd));

            ReplenishmentDestinationRecord current = addedCatalog.Destinations[0];
            StoredProductionLink sameLink = current.Link;
            True(MultiReplenishmentCatalogPolicy.TryRefresh(
                addedCatalog,
                current.LinkId,
                sameLink,
                Plan(sameLink, "station.multi", 2, Target("Food00", 2)),
                out MultiReplenishmentCatalog refreshedCatalog,
                out _,
                out _));
            MultiReplenishmentCatalogPublication refreshedPublication =
                MultiReplenishmentCatalogStore.Prepare(refreshedCatalog);
            string refreshTransition = MultiReplenishmentCatalogStore.CreateTransitionIndex(
                addedPublication, refreshedPublication, out int refreshSlot);
            Equal(0, refreshSlot);
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                refreshTransition,
                addedPublication.SlotRecords,
                out MultiReplenishmentCatalog beforeRefreshSlot,
                out _));
            True(MultiReplenishmentCatalogStore.CatalogsEqual(
                addedCatalog, beforeRefreshSlot));
            string[] afterRefreshSlot = addedPublication.SlotRecords.ToArray();
            afterRefreshSlot[refreshSlot] = refreshedPublication.SlotRecord(refreshSlot);
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                refreshTransition,
                afterRefreshSlot,
                out MultiReplenishmentCatalog committedRefresh,
                out _));
            True(MultiReplenishmentCatalogStore.CatalogsEqual(
                refreshedCatalog, committedRefresh));

            True(MultiReplenishmentCatalogPolicy.TryRemove(
                refreshedCatalog,
                current.LinkId,
                out MultiReplenishmentCatalog removedCatalog,
                out _,
                out _));
            MultiReplenishmentCatalogPublication removedPublication =
                MultiReplenishmentCatalogStore.Prepare(removedCatalog);
            string removeTransition = MultiReplenishmentCatalogStore.CreateTransitionIndex(
                refreshedPublication, removedPublication, out int removeSlot);
            Equal(0, removeSlot);
            // The transition itself commits a removal: the new index ignores the old inert body.
            Equal(StoredRecordState.Valid, MultiReplenishmentCatalogStore.Parse(
                removeTransition,
                refreshedPublication.SlotRecords,
                out MultiReplenishmentCatalog committedRemove,
                out _));
            True(MultiReplenishmentCatalogStore.CatalogsEqual(
                removedCatalog, committedRemove));

            MultiReplenishmentCatalog twoChanges = new MultiReplenishmentCatalog(
                oldCatalog.CatalogId,
                oldCatalog.StationId,
                oldCatalog.Revision + 1,
                3L,
                1L,
                new[]
                {
                    Record(0, 1L, Link(10), "station.multi"),
                    Record(1, 2L, Link(11), "station.multi")
                });
            Throws<InvalidOperationException>(() =>
                MultiReplenishmentCatalogStore.CreateTransitionIndex(
                    oldPublication,
                    MultiReplenishmentCatalogStore.Prepare(twoChanges),
                    out _));
        }

        private static void AggregateStorageIsBounded()
        {
            MultiReplenishmentCatalog catalog = Empty();
            for (int destination = 0; destination < 16; destination++)
            {
                StoredProductionLink link = Link(destination);
                var targets = new[]
                {
                    LargeTarget(destination, 0),
                    LargeTarget(destination, 1)
                };
                True(MultiReplenishmentCatalogPolicy.TryAdd(
                    catalog,
                    link,
                    Plan(link, "station.multi", 1, targets),
                    16,
                    out catalog,
                    out _,
                    out _));
            }
            Equal(32, catalog.TargetAuthorizationCount);
            Throws<InvalidOperationException>(() =>
                MultiReplenishmentCatalogStore.Prepare(catalog));
        }

        private static bool Add(
            MultiReplenishmentCatalog catalog,
            int id,
            int softMaximum,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord added)
        {
            StoredProductionLink link = Link(id);
            return MultiReplenishmentCatalogPolicy.TryAdd(
                catalog,
                link,
                Plan(link, "station.multi", 1, Target("Food" + id.ToString("00"), id)),
                softMaximum,
                out updated,
                out added,
                out _);
        }

        private static MultiReplenishmentCatalog Empty() =>
            MultiReplenishmentCatalogPolicy.CreateEmpty("catalog.multi", "station.multi");

        private static ReplenishmentDestinationRecord Record(
            int slot,
            long ordinal,
            StoredProductionLink link,
            string stationId) =>
            new ReplenishmentDestinationRecord(
                slot,
                ordinal,
                1,
                ReplenishmentDestinationState.Active,
                ProductionStopCode.Ready,
                link,
                Plan(link, stationId, 1, Target("Direct" + slot, slot)));

        private static StoredProductionLink Link(
            int id,
            string linkId = null,
            int revision = 1) =>
            new StoredProductionLink
            {
                LinkId = linkId ?? "link." + id,
                Role = ProductionLinkRole.Replenishment,
                Target = new ZDOID(500L, (uint)(id + 1)),
                ExpectedPosition = new Vector3(id, 2f, 3f),
                OwnerId = 77L,
                StationOwnerId = 77L,
                TargetOwnerId = 77L,
                Revision = revision
            };

        private static ReplenishmentPlan Plan(
            StoredProductionLink link,
            string stationId,
            int revision,
            params ReplenishmentTargetAuthorization[] targets)
        {
            long playerId = targets.Length == 0
                ? link.OwnerId
                : targets[0].AuthorizedPlayerId;
            string playerName = targets.Length == 0
                ? "Tester"
                : targets[0].AuthorizedPlayerName;
            return new ReplenishmentPlan(
                "plan." + link.LinkId,
                revision,
                0,
                "piece_cauldron",
                ReplenishmentProducerKind.DirectRecipe,
                ReplenishmentPlanStore.Bind(link, stationId),
                playerId,
                playerName,
                targets);
        }

        private static ReplenishmentTargetAuthorization Target(string output, int seed) =>
            new ReplenishmentTargetAuthorization(
                output,
                ReplenishmentProducerKind.DirectRecipe,
                "Recipe" + seed,
                Signature(seed),
                1,
                "$piece_cauldron",
                1,
                77L,
                "Tester",
                new[] { new ReplenishmentRequirement("Carrot", 1) });

        private static ReplenishmentTargetAuthorization LargeTarget(int destination, int target)
        {
            string suffix = destination.ToString("00") + target;
            var requirements = new List<ReplenishmentRequirement>();
            for (int index = 0; index < 16; index++)
            {
                string requirement = new string('R', 118) +
                                     destination.ToString("00") +
                                     target +
                                     index.ToString("00");
                requirements.Add(new ReplenishmentRequirement(requirement, 1));
            }
            return new ReplenishmentTargetAuthorization(
                "LargeFood" + suffix,
                ReplenishmentProducerKind.DirectRecipe,
                new string('P', 250) + suffix,
                Signature(destination * 2 + target),
                1,
                new string('S', 128),
                1,
                77L,
                new string('N', 128),
                requirements);
        }

        /// <summary>
        /// Writes the former schema-2 plan layout.  The catalog store embeds the same body
        /// without the leading schema integer, so one fixture covers both migration readers.
        /// </summary>
        private static byte[] LegacyPlanBytes(
            ReplenishmentPlan plan,
            IReadOnlyList<ReplenishmentTargetAuthorization> targets,
            bool includeSchema)
        {
            var package = new ZPackage();
            if (includeSchema) package.Write(2);
            package.Write(plan.PlanId);
            package.Write(plan.Revision);
            package.Write(plan.Cursor);
            package.Write(plan.StationPrefabId);
            package.Write((int)plan.AdapterKind);
            package.Write(plan.Link.LinkId);
            package.Write(plan.Link.StationId);
            package.Write(plan.Link.TargetId);
            package.Write(plan.Link.TargetToken);
            package.Write(plan.Link.TargetPrefabHash);
            package.Write(plan.Link.Revision);
            package.Write(targets.Count);
            foreach (ReplenishmentTargetAuthorization target in targets)
            {
                package.Write(target.OutputPrefabId);
                package.Write((int)target.ProducerKind);
                package.Write(target.ProducerId);
                package.Write(target.CopyProducerSignature());
                package.Write(target.OutputAmount);
                package.Write(target.RequiredStationName);
                package.Write(target.RequiredStationLevel);
                package.Write(target.AuthorizedPlayerId);
                package.Write(target.AuthorizedPlayerName);
                package.Write(target.Requirements.Count);
                foreach (ReplenishmentRequirement requirement in target.Requirements)
                {
                    package.Write(requirement.PrefabId);
                    package.Write(requirement.Amount);
                }
            }
            return package.GetArray();
        }

        private static byte[] Signature(int seed)
        {
            var bytes = new byte[ReplenishmentTargetAuthorization.ProducerSignatureBytes];
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = (byte)(seed + index);
            return bytes;
        }

        private static KeyValuePair<string, Action> Case(string name, Action action) =>
            new KeyValuePair<string, Action>(name, action);

        private static void True(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Expected true.");
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }

        private static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                "Expected " + typeof(TException).Name + ".");
        }
    }
}
