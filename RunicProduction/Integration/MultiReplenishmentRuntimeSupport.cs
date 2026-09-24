using System;
using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    /// <summary>
    /// Reads the current bounded destination catalog and upgrades the former singleton
    /// Replenishment record when its link and plan still agree exactly.
    /// </summary>
    internal static class MultiReplenishmentRuntimeSupport
    {
        internal static StoredRecordState ReadOrMigrate(
            ZDO stationZdo,
            string stationId,
            bool initializeEmpty,
            out MultiReplenishmentCatalog catalog,
            out string failure)
        {
            catalog = null;
            failure = string.Empty;
            if (stationZdo == null || string.IsNullOrWhiteSpace(stationId))
                return Fail(global::Runic.Localization.RunicText.Get("text_a036e6714536"), out failure);

            StoredRecordState state = MultiReplenishmentCatalogStore.Normalize(
                stationZdo, out catalog);
            StoredProductionLink legacyLink = ProductionLinkStore.Load(
                stationZdo, ProductionLinkRole.Replenishment);
            StoredRecordState legacyPlanState = ReplenishmentPlanStore.Read(
                stationZdo, out ReplenishmentPlan legacyPlan);

            if (state == StoredRecordState.Invalid)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_ec318aee709c"),
                    out failure);
            if (state == StoredRecordState.Valid)
            {
                if (ProductionEndpointIdentity.IsCanonicalToken(catalog.StationId) &&
                    !string.Equals(catalog.StationId, stationId, StringComparison.Ordinal))
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_678b3690ae29"),
                        out failure);
                if (!LegacyDebrisMatchesCatalog(
                        catalog, legacyLink, legacyPlanState, legacyPlan))
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_d2d9497e662e"),
                        out failure);
                if (legacyLink != null || legacyPlanState != StoredRecordState.Absent)
                    ClearLegacySingleton(stationZdo);
                return StoredRecordState.Valid;
            }

            if (legacyLink != null || legacyPlanState != StoredRecordState.Absent)
            {
                string legacyStationId = legacyPlan?.Link?.StationId ?? string.Empty;
                bool stableBinding =
                    ProductionEndpointIdentity.IsCanonicalToken(legacyStationId);
                string exactBinding = stableBinding ? stationId : legacyStationId;
                if (legacyLink == null || legacyPlanState != StoredRecordState.Valid ||
                    stableBinding && !string.Equals(
                        legacyStationId, stationId, StringComparison.Ordinal) ||
                    !ReplenishmentPlanStore.MatchesLink(
                        legacyPlan, legacyLink, exactBinding))
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_3d716dd1438d"),
                        out failure);
                try
                {
                    MultiReplenishmentCatalog migrated =
                        MultiReplenishmentCatalogPolicy.FromLegacySingleton(
                            Guid.NewGuid().ToString("N"),
                            exactBinding,
                            legacyLink,
                            legacyPlan);
                    MultiReplenishmentCatalogCommitCode commit =
                        MultiReplenishmentCatalogStore.TryInitialize(
                            stationZdo, migrated);
                    if (commit != MultiReplenishmentCatalogCommitCode.Committed &&
                        commit != MultiReplenishmentCatalogCommitCode.NoChange ||
                        MultiReplenishmentCatalogStore.Read(
                            stationZdo, out catalog) != StoredRecordState.Valid ||
                        !MultiReplenishmentCatalogStore.CatalogsEqual(
                            catalog, migrated))
                        return Fail(
                            global::Runic.Localization.RunicText.Get("text_e3bcd01fb432"),
                            out failure);
                    ClearLegacySingleton(stationZdo);
                    return StoredRecordState.Valid;
                }
                catch (Exception exception)
                {
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_92c34b86d4a7") +
                        exception.GetType().Name + ".",
                        out failure);
                }
            }

            if (!initializeEmpty) return StoredRecordState.Absent;
            try
            {
                MultiReplenishmentCatalog empty =
                    MultiReplenishmentCatalogPolicy.CreateEmpty(
                        Guid.NewGuid().ToString("N"), stationId);
                MultiReplenishmentCatalogCommitCode commit =
                    MultiReplenishmentCatalogStore.TryInitialize(stationZdo, empty);
                if (commit != MultiReplenishmentCatalogCommitCode.Committed &&
                    commit != MultiReplenishmentCatalogCommitCode.NoChange ||
                    MultiReplenishmentCatalogStore.Read(
                        stationZdo, out catalog) != StoredRecordState.Valid)
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_b63fd60cec46"),
                        out failure);
                return StoredRecordState.Valid;
            }
            catch (Exception exception)
            {
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_6bd6043dafd5") +
                    exception.GetType().Name + ".",
                    out failure);
            }
        }

        internal static int SoftMaximumDestinations => Math.Max(
            1,
            Math.Min(
                MultiReplenishmentCatalog.HardMaximumDestinations,
                ProductionConfig.MaximumLinksPerRole?.Value ?? 8));

        internal static bool LegacyDebrisMatchesCatalog(
            MultiReplenishmentCatalog catalog,
            StoredProductionLink legacyLink,
            StoredRecordState legacyPlanState,
            ReplenishmentPlan legacyPlan)
        {
            if (catalog == null || legacyPlanState == StoredRecordState.Invalid)
                return false;
            bool hasPlan = legacyPlanState == StoredRecordState.Valid;
            if (hasPlan != (legacyPlan != null)) return false;
            if (legacyLink == null && !hasPlan) return true;
            int matches = 0;
            foreach (ReplenishmentDestinationRecord destination in catalog.Destinations)
            {
                if (legacyLink != null &&
                    !ReplenishmentDestinationRecord.SameExactLink(
                        destination.Link, legacyLink)) continue;
                if (hasPlan && !PlansEqual(destination.Plan, legacyPlan)) continue;
                matches++;
            }
            return matches == 1;
        }

        private static bool PlansEqual(
            ReplenishmentPlan left,
            ReplenishmentPlan right)
        {
            if (left == null || right == null) return false;
            try
            {
                return string.Equals(
                    ReplenishmentPlanStore.Serialize(left),
                    ReplenishmentPlanStore.Serialize(right),
                    StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static void ClearLegacySingleton(ZDO stationZdo)
        {
            ProductionLinkStore.Clear(
                stationZdo, ProductionLinkRole.Replenishment);
            ReplenishmentPlanStore.Clear(stationZdo);
        }

        private static StoredRecordState Fail(
            string detail,
            out string failure)
        {
            failure = detail;
            return StoredRecordState.Invalid;
        }
    }
}
