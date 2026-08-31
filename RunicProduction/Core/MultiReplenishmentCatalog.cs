using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunicProduction.Contracts;
using RunicProduction.Integration;

namespace RunicProduction.Core
{
    /// <summary>The persistent lifecycle of one independently managed replenishment destination.</summary>
    internal enum ReplenishmentDestinationState
    {
        Active = 1,
        Draining = 2,
        Faulted = 3,
        NeedsRefresh = 4
    }

    internal enum ReplenishmentCatalogChangeCode
    {
        Applied = 0,
        NoChange = 1,
        SoftLimitReached = 2,
        HardLimitReached = 3,
        TargetAuthorizationLimitReached = 4,
        DuplicateLinkId = 5,
        DuplicateTarget = 6,
        LinkNotFound = 7,
        LinkIdentityChanged = 8,
        RevisionExhausted = 9,
        OrdinalExhausted = 10
    }

    /// <summary>
    /// Immutable, exact destination authorization. The mutable legacy link DTO is copied both on
    /// ingress and egress so a caller cannot change a published catalog behind its back.
    /// </summary>
    internal sealed class ReplenishmentDestinationRecord
    {
        internal ReplenishmentDestinationRecord(
            int slot,
            long ordinal,
            int recordRevision,
            ReplenishmentDestinationState state,
            ProductionStopCode faultCode,
            StoredProductionLink link,
            ReplenishmentPlan plan)
        {
            if (slot < 0 || slot >= MultiReplenishmentCatalog.HardMaximumDestinations)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (ordinal < 1L) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (recordRevision < 1) throw new ArgumentOutOfRangeException(nameof(recordRevision));
            if (!Enum.IsDefined(typeof(ReplenishmentDestinationState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (!Enum.IsDefined(typeof(ProductionStopCode), faultCode))
                throw new ArgumentOutOfRangeException(nameof(faultCode));
            if (state == ReplenishmentDestinationState.Faulted)
            {
                if (faultCode == ProductionStopCode.Ready)
                    throw new ArgumentException(
                        "A faulted destination requires an exact non-ready stop code.",
                        nameof(faultCode));
            }
            else if (faultCode != ProductionStopCode.Ready)
            {
                throw new ArgumentException(
                    "Only a faulted destination may carry a stop code.",
                    nameof(faultCode));
            }

            StoredProductionLink exactLink = CopyAndValidateLink(link, nameof(link));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (!string.Equals(plan.Link.LinkId, exactLink.LinkId, StringComparison.Ordinal) ||
                !PlanMatchesTarget(plan.Link, exactLink) ||
                plan.Link.Revision != exactLink.Revision ||
                plan.AuthorizedPlayerId != exactLink.OwnerId)
                throw new ArgumentException(
                    "The replenishment plan must be bound to the exact stored link revision and owner.",
                    nameof(plan));

            Slot = slot;
            Ordinal = ordinal;
            RecordRevision = recordRevision;
            State = state;
            FaultCode = faultCode;
            _link = exactLink;
        }

        private readonly StoredProductionLink _link;

        internal int Slot { get; }
        internal long Ordinal { get; }
        internal int RecordRevision { get; }
        internal ReplenishmentDestinationState State { get; }
        internal ProductionStopCode FaultCode { get; }
        internal string LinkId => _link.LinkId;
        internal ReplenishmentPlan Plan { get; }
        internal StoredProductionLink Link => CopyLink(_link);

        internal ReplenishmentDestinationRecord With(
            int nextRecordRevision,
            ReplenishmentDestinationState state,
            ProductionStopCode faultCode,
            StoredProductionLink link,
            ReplenishmentPlan plan) =>
            new ReplenishmentDestinationRecord(
                Slot,
                Ordinal,
                nextRecordRevision,
                state,
                faultCode,
                link,
                plan);

        internal static bool SameExactLink(
            StoredProductionLink left,
            StoredProductionLink right) =>
            left != null && right != null &&
            left.Role == right.Role &&
            SameTargetIdentity(left, right) &&
            left.ExpectedPosition == right.ExpectedPosition &&
            left.OwnerId == right.OwnerId &&
            left.StationOwnerId == right.StationOwnerId &&
            left.TargetOwnerId == right.TargetOwnerId &&
            left.Revision == right.Revision &&
            string.Equals(left.TargetToken, right.TargetToken, StringComparison.Ordinal) &&
            string.Equals(left.LinkId, right.LinkId, StringComparison.Ordinal);

        internal static bool SameTargetIdentity(
            StoredProductionLink left,
            StoredProductionLink right) =>
            MultiReplenishmentCatalogPolicy.SameTargetIdentity(left, right);

        internal static StoredProductionLink CopyLink(StoredProductionLink source) =>
            source == null
                ? null
                : new StoredProductionLink
                {
                    LinkId = source.LinkId,
                    Role = source.Role,
                    TargetToken = source.TargetToken,
                    TargetPrefabHash = source.TargetPrefabHash,
                    Target = source.Target,
                    ExpectedPosition = source.ExpectedPosition,
                    OwnerId = source.OwnerId,
                    StationOwnerId = source.StationOwnerId,
                    TargetOwnerId = source.TargetOwnerId,
                    Revision = source.Revision
                };

        private static StoredProductionLink CopyAndValidateLink(
            StoredProductionLink source,
            string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            if (source.Role != ProductionLinkRole.Replenishment || source.Target.IsNone() ||
                source.Revision < 1 ||
                !StockDomainValidation.IsLegacyOrWorldObjectIdentity(
                    source.TargetToken,
                    source.TargetPrefabHash) ||
                !IsStableText(source.LinkId, 200) ||
                !IsFinite(source.ExpectedPosition))
                throw new ArgumentException(
                    "An exact, finite replenishment link is required.",
                    parameterName);
            return CopyLink(source);
        }

        private static bool PlanMatchesTarget(
            ReplenishmentLinkBinding binding,
            StoredProductionLink link)
        {
            if (string.IsNullOrEmpty(binding.TargetToken))
                return string.Equals(
                    binding.TargetId,
                    link.Target.ToString(),
                    StringComparison.Ordinal);
            return binding.TargetPrefabHash == link.TargetPrefabHash &&
                   string.Equals(
                       binding.TargetToken,
                       link.TargetToken,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       binding.TargetId,
                       link.TargetToken,
                       StringComparison.Ordinal);
        }

        private static bool IsStableText(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
            foreach (char character in value)
                if (char.IsControl(character)) return false;
            return true;
        }

        private static bool IsFinite(UnityEngine.Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    /// <summary>
    /// One station's bounded many-destination catalog. Records are canonicalized by stable ordinal;
    /// slot numbers are persistence locations and never determine scheduling order.
    /// </summary>
    internal sealed class MultiReplenishmentCatalog
    {
        internal const int HardMaximumDestinations = 16;
        internal const int MaximumTargetAuthorizations = 32;

        private readonly ReadOnlyCollection<ReplenishmentDestinationRecord> _destinations;

        internal MultiReplenishmentCatalog(
            string catalogId,
            string stationId,
            int revision,
            long nextOrdinal,
            long destinationCursorOrdinal,
            IEnumerable<ReplenishmentDestinationRecord> destinations)
        {
            CatalogId = RequireStableText(catalogId, nameof(catalogId), 200);
            StationId = RequireStableText(stationId, nameof(stationId), 200);
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            if (nextOrdinal < 1L) throw new ArgumentOutOfRangeException(nameof(nextOrdinal));

            var copy = new List<ReplenishmentDestinationRecord>();
            if (destinations != null)
            {
                foreach (ReplenishmentDestinationRecord destination in destinations)
                {
                    if (destination == null)
                        throw new ArgumentException(
                            "Catalog destinations cannot contain null records.",
                            nameof(destinations));
                    copy.Add(destination);
                    if (copy.Count > HardMaximumDestinations)
                        throw new ArgumentOutOfRangeException(nameof(destinations));
                }
            }
            copy.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));

            var slots = new HashSet<int>();
            var ordinals = new HashSet<long>();
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<ZDOID>();
            var targetTokens = new HashSet<string>(StringComparer.Ordinal);
            int authorizationCount = 0;
            long maximumOrdinal = 0L;
            foreach (ReplenishmentDestinationRecord destination in copy)
            {
                StoredProductionLink link = destination.Link;
                if (!slots.Add(destination.Slot) ||
                    !ordinals.Add(destination.Ordinal) ||
                    !linkIds.Add(destination.LinkId))
                    throw new ArgumentException(
                        "Slots, ordinals, and link IDs must be unique within a station catalog.",
                        nameof(destinations));
                if (!targets.Add(link.Target))
                    throw new ArgumentException(
                        "A target chest may appear at most once in one station catalog.",
                        nameof(destinations));
                if (!string.IsNullOrEmpty(link.TargetToken) &&
                    !targetTokens.Add(link.TargetToken))
                    throw new ArgumentException(
                        "A target chest may appear at most once in one station catalog.",
                        nameof(destinations));
                if (!string.Equals(
                        destination.Plan.Link.StationId,
                        StationId,
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        "Every destination plan must be bound to this exact station.",
                        nameof(destinations));
                authorizationCount = checked(
                    authorizationCount + destination.Plan.Targets.Count);
                if (authorizationCount > MaximumTargetAuthorizations)
                    throw new ArgumentOutOfRangeException(nameof(destinations));
                maximumOrdinal = Math.Max(maximumOrdinal, destination.Ordinal);
            }
            if (nextOrdinal <= maximumOrdinal)
                throw new ArgumentOutOfRangeException(
                    nameof(nextOrdinal),
                    "The next ordinal must be greater than every destination ordinal.");
            if (copy.Count == 0)
            {
                if (destinationCursorOrdinal != 0L)
                    throw new ArgumentOutOfRangeException(nameof(destinationCursorOrdinal));
            }
            else if (destinationCursorOrdinal != 0L &&
                     !ordinals.Contains(destinationCursorOrdinal))
            {
                throw new ArgumentOutOfRangeException(nameof(destinationCursorOrdinal));
            }

            Revision = revision;
            NextOrdinal = nextOrdinal;
            DestinationCursorOrdinal = destinationCursorOrdinal;
            TargetAuthorizationCount = authorizationCount;
            _destinations = copy.AsReadOnly();
        }

        internal string CatalogId { get; }
        internal string StationId { get; }
        internal int Revision { get; }
        internal long NextOrdinal { get; }
        internal long DestinationCursorOrdinal { get; }
        internal int TargetAuthorizationCount { get; }
        internal IReadOnlyList<ReplenishmentDestinationRecord> Destinations => _destinations;

        internal bool TryGetByLinkId(
            string linkId,
            out ReplenishmentDestinationRecord destination)
        {
            foreach (ReplenishmentDestinationRecord candidate in _destinations)
                if (string.Equals(candidate.LinkId, linkId, StringComparison.Ordinal))
                {
                    destination = candidate;
                    return true;
                }
            destination = null;
            return false;
        }

        internal bool TryGetBySlot(
            int slot,
            out ReplenishmentDestinationRecord destination)
        {
            foreach (ReplenishmentDestinationRecord candidate in _destinations)
                if (candidate.Slot == slot)
                {
                    destination = candidate;
                    return true;
                }
            destination = null;
            return false;
        }

        internal bool TryGetByTarget(
            ZDOID target,
            out ReplenishmentDestinationRecord destination)
        {
            foreach (ReplenishmentDestinationRecord candidate in _destinations)
                if (candidate.Link.Target == target)
                {
                    destination = candidate;
                    return true;
                }
            destination = null;
            return false;
        }

        internal bool TryGetByTargetIdentity(
            StoredProductionLink link,
            out ReplenishmentDestinationRecord destination)
        {
            if (link != null)
                foreach (ReplenishmentDestinationRecord candidate in _destinations)
                    if (MultiReplenishmentCatalogPolicy.SameTargetIdentity(
                            candidate.Link,
                            link))
                    {
                        destination = candidate;
                        return true;
                    }
            destination = null;
            return false;
        }

        internal bool TryGetByOrdinal(
            long ordinal,
            out ReplenishmentDestinationRecord destination)
        {
            foreach (ReplenishmentDestinationRecord candidate in _destinations)
                if (candidate.Ordinal == ordinal)
                {
                    destination = candidate;
                    return true;
                }
            destination = null;
            return false;
        }

        /// <summary>Returns active destinations once each, beginning at the persisted cursor.</summary>
        internal IReadOnlyList<ReplenishmentDestinationRecord> ActiveRoundRobinOrder()
        {
            var active = new List<ReplenishmentDestinationRecord>();
            foreach (ReplenishmentDestinationRecord destination in _destinations)
                if (destination.State == ReplenishmentDestinationState.Active)
                    active.Add(destination);
            if (active.Count < 2 || DestinationCursorOrdinal == 0L)
                return active.AsReadOnly();

            int start = active.FindIndex(destination =>
                destination.Ordinal >= DestinationCursorOrdinal);
            if (start <= 0) return active.AsReadOnly();
            var ordered = new List<ReplenishmentDestinationRecord>(active.Count);
            for (int offset = 0; offset < active.Count; offset++)
                ordered.Add(active[(start + offset) % active.Count]);
            return ordered.AsReadOnly();
        }

        private static string RequireStableText(
            string value,
            string parameterName,
            int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable identifier is required.", parameterName);
            if (value.Length > maximum) throw new ArgumentOutOfRangeException(parameterName);
            foreach (char character in value)
                if (char.IsControl(character))
                    throw new ArgumentException(
                        "Stable identifiers cannot contain control characters.",
                        parameterName);
            return value;
        }
    }

    /// <summary>
    /// Pure deterministic mutations for a catalog. The configured soft limit is consulted only by
    /// Add; refresh, drain, fault recovery, cursor movement, and removal remain available after a
    /// configured soft limit is lowered.
    /// </summary>
    internal static class MultiReplenishmentCatalogPolicy
    {
        internal static MultiReplenishmentCatalog CreateEmpty(
            string catalogId,
            string stationId) =>
            new MultiReplenishmentCatalog(
                catalogId,
                stationId,
                1,
                1L,
                0L,
                Array.Empty<ReplenishmentDestinationRecord>());

        internal static bool TryAdd(
            MultiReplenishmentCatalog catalog,
            StoredProductionLink link,
            ReplenishmentPlan plan,
            int softMaximumDestinations,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord added,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            ValidateSoftMaximum(softMaximumDestinations);
            var candidate = new ReplenishmentDestinationRecord(
                FindFirstFreeSlot(catalog),
                catalog.NextOrdinal,
                1,
                ReplenishmentDestinationState.Active,
                ProductionStopCode.Ready,
                link,
                plan);
            ValidateStation(catalog, candidate);

            if (catalog.TryGetByLinkId(candidate.LinkId, out _))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.DuplicateLinkId,
                    out updated,
                    out added,
                    out code);
            if (catalog.TryGetByTargetIdentity(candidate.Link, out _))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.DuplicateTarget,
                    out updated,
                    out added,
                    out code);
            if (catalog.Destinations.Count >= MultiReplenishmentCatalog.HardMaximumDestinations)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.HardLimitReached,
                    out updated,
                    out added,
                    out code);
            if (catalog.Destinations.Count >= softMaximumDestinations)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.SoftLimitReached,
                    out updated,
                    out added,
                    out code);
            if (catalog.TargetAuthorizationCount + plan.Targets.Count >
                MultiReplenishmentCatalog.MaximumTargetAuthorizations)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.TargetAuthorizationLimitReached,
                    out updated,
                    out added,
                    out code);
            if (catalog.NextOrdinal == long.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.OrdinalExhausted,
                    out updated,
                    out added,
                    out code);
            if (catalog.Revision == int.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.RevisionExhausted,
                    out updated,
                    out added,
                    out code);

            var records = new List<ReplenishmentDestinationRecord>(catalog.Destinations)
            {
                candidate
            };
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal + 1L,
                catalog.DestinationCursorOrdinal == 0L
                    ? candidate.Ordinal
                    : catalog.DestinationCursorOrdinal,
                records);
            added = candidate;
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        internal static bool TryRefresh(
            MultiReplenishmentCatalog catalog,
            string existingLinkId,
            StoredProductionLink refreshedLink,
            ReplenishmentPlan refreshedPlan,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord refreshed,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByLinkId(existingLinkId, out ReplenishmentDestinationRecord current))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkNotFound,
                    out updated,
                    out refreshed,
                    out code);

            var candidate = new ReplenishmentDestinationRecord(
                current.Slot,
                current.Ordinal,
                current.RecordRevision == int.MaxValue
                    ? current.RecordRevision
                    : current.RecordRevision + 1,
                ReplenishmentDestinationState.Active,
                ProductionStopCode.Ready,
                refreshedLink,
                refreshedPlan);
            ValidateStation(catalog, candidate);
            StoredProductionLink currentLink = current.Link;
            StoredProductionLink candidateLink = candidate.Link;
            if (!string.Equals(current.LinkId, candidate.LinkId, StringComparison.Ordinal) ||
                !SameTargetIdentity(currentLink, candidateLink) ||
                candidateLink.Revision < currentLink.Revision)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkIdentityChanged,
                    out updated,
                    out refreshed,
                    out code);
            if (current.RecordRevision == int.MaxValue || catalog.Revision == int.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.RevisionExhausted,
                    out updated,
                    out refreshed,
                    out code);
            int nextAuthorizationCount = catalog.TargetAuthorizationCount -
                                         current.Plan.Targets.Count +
                                         refreshedPlan.Targets.Count;
            if (nextAuthorizationCount > MultiReplenishmentCatalog.MaximumTargetAuthorizations)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.TargetAuthorizationLimitReached,
                    out updated,
                    out refreshed,
                    out code);

            var records = Replace(catalog, current, candidate);
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                catalog.DestinationCursorOrdinal,
                records);
            refreshed = candidate;
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        /// <summary>
        /// Replaces only the restart-unstable target identity of one schema-1 destination after
        /// runtime resolution proved its unique container. This is not a user refresh: the link,
        /// plan, lifecycle, fault, fairness, and authorization revisions remain unchanged while
        /// the destination record and catalog each advance exactly once.
        /// </summary>
        internal static bool TryUpgradeLegacyDestinationIdentity(
            MultiReplenishmentCatalog catalog,
            string existingLinkId,
            StoredProductionLink upgradedLink,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord upgraded,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByLinkId(
                    existingLinkId,
                    out ReplenishmentDestinationRecord current))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkNotFound,
                    out updated,
                    out upgraded,
                    out code);

            StoredProductionLink legacyLink = current.Link;
            if (!IsExactLegacyIdentityUpgrade(legacyLink, upgradedLink) ||
                !ReplenishmentPlanStore.TryPrepareLegacyTargetRebind(
                    current.Plan,
                    legacyLink,
                    upgradedLink,
                    out ReplenishmentPlan reboundPlan,
                    out _))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkIdentityChanged,
                    out updated,
                    out upgraded,
                    out code);
            if (current.RecordRevision == int.MaxValue || catalog.Revision == int.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.RevisionExhausted,
                    out updated,
                    out upgraded,
                    out code);

            upgraded = current.With(
                current.RecordRevision + 1,
                current.State,
                current.FaultCode,
                upgradedLink,
                reboundPlan);
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                catalog.DestinationCursorOrdinal,
                Replace(catalog, current, upgraded));
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        /// <summary>
        /// Atomically advances both fairness dimensions after one committed batch: the selected
        /// destination's target cursor and the station's next-destination cursor. Persistence
        /// permits exactly one catalog revision per transition, so these two logically coupled
        /// successor values must never be published as separate catalog mutations.
        /// </summary>
        internal static bool TryAdvanceAfterSelection(
            MultiReplenishmentCatalog catalog,
            string selectedLinkId,
            ReplenishmentPlan advancedPlan,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord advanced,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByLinkId(
                    selectedLinkId, out ReplenishmentDestinationRecord current))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkNotFound,
                    out updated,
                    out advanced,
                    out code);
            if (current.State != ReplenishmentDestinationState.Active ||
                advancedPlan == null ||
                !string.Equals(
                    advancedPlan.Link.LinkId, current.LinkId, StringComparison.Ordinal) ||
                advancedPlan.Link.Revision != current.Link.Revision ||
                !PlanMatchesLink(advancedPlan.Link, current.Link))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkIdentityChanged,
                    out updated,
                    out advanced,
                    out code);
            if (current.RecordRevision == int.MaxValue || catalog.Revision == int.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.RevisionExhausted,
                    out updated,
                    out advanced,
                    out code);

            int nextAuthorizationCount = catalog.TargetAuthorizationCount -
                                         current.Plan.Targets.Count +
                                         advancedPlan.Targets.Count;
            if (nextAuthorizationCount > MultiReplenishmentCatalog.MaximumTargetAuthorizations)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.TargetAuthorizationLimitReached,
                    out updated,
                    out advanced,
                    out code);

            advanced = current.With(
                current.RecordRevision + 1,
                ReplenishmentDestinationState.Active,
                ProductionStopCode.Ready,
                current.Link,
                advancedPlan);
            var records = Replace(catalog, current, advanced);
            var active = new List<ReplenishmentDestinationRecord>();
            foreach (ReplenishmentDestinationRecord destination in records)
                if (destination.State == ReplenishmentDestinationState.Active)
                    active.Add(destination);
            active.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
            int selectedIndex = active.FindIndex(destination =>
                destination.Ordinal == current.Ordinal);
            if (selectedIndex < 0)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkNotFound,
                    out updated,
                    out advanced,
                    out code);
            long nextDestinationCursor =
                active[(selectedIndex + 1) % active.Count].Ordinal;
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                nextDestinationCursor,
                records);
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        internal static bool TrySetState(
            MultiReplenishmentCatalog catalog,
            string linkId,
            ReplenishmentDestinationState state,
            ProductionStopCode faultCode,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord changed,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByLinkId(linkId, out ReplenishmentDestinationRecord current))
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.LinkNotFound,
                    out updated,
                    out changed,
                    out code);
            if (current.State == state && current.FaultCode == faultCode)
            {
                updated = catalog;
                changed = current;
                code = ReplenishmentCatalogChangeCode.NoChange;
                return true;
            }
            if (current.RecordRevision == int.MaxValue || catalog.Revision == int.MaxValue)
                return Reject(
                    catalog,
                    ReplenishmentCatalogChangeCode.RevisionExhausted,
                    out updated,
                    out changed,
                    out code);

            var candidate = current.With(
                current.RecordRevision + 1,
                state,
                faultCode,
                current.Link,
                current.Plan);
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                catalog.DestinationCursorOrdinal,
                Replace(catalog, current, candidate));
            changed = candidate;
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        internal static bool TryRemove(
            MultiReplenishmentCatalog catalog,
            string linkId,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord removed,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByLinkId(linkId, out removed))
            {
                updated = catalog;
                code = ReplenishmentCatalogChangeCode.LinkNotFound;
                return false;
            }
            if (catalog.Revision == int.MaxValue)
            {
                updated = catalog;
                removed = null;
                code = ReplenishmentCatalogChangeCode.RevisionExhausted;
                return false;
            }

            var records = new List<ReplenishmentDestinationRecord>();
            foreach (ReplenishmentDestinationRecord destination in catalog.Destinations)
                if (!ReferenceEquals(destination, removed)) records.Add(destination);
            long cursor = catalog.DestinationCursorOrdinal;
            if (records.Count == 0)
            {
                cursor = 0L;
            }
            else if (cursor == removed.Ordinal)
            {
                cursor = records[0].Ordinal;
                foreach (ReplenishmentDestinationRecord destination in records)
                    if (destination.Ordinal > removed.Ordinal)
                    {
                        cursor = destination.Ordinal;
                        break;
                    }
            }
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                cursor,
                records);
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        internal static bool TryAdvanceCursorAfter(
            MultiReplenishmentCatalog catalog,
            long selectedOrdinal,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentCatalogChangeCode code)
        {
            RequireCatalog(catalog);
            if (!catalog.TryGetByOrdinal(selectedOrdinal, out _))
            {
                updated = catalog;
                code = ReplenishmentCatalogChangeCode.LinkNotFound;
                return false;
            }
            IReadOnlyList<ReplenishmentDestinationRecord> active =
                catalog.ActiveRoundRobinOrder();
            if (active.Count == 0)
            {
                updated = catalog;
                code = ReplenishmentCatalogChangeCode.NoChange;
                return true;
            }
            var sortedActive = new List<ReplenishmentDestinationRecord>(active);
            sortedActive.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
            int selectedIndex = sortedActive.FindIndex(destination =>
                destination.Ordinal == selectedOrdinal);
            long cursor = selectedIndex < 0
                ? sortedActive[0].Ordinal
                : sortedActive[(selectedIndex + 1) % sortedActive.Count].Ordinal;
            if (cursor == catalog.DestinationCursorOrdinal)
            {
                updated = catalog;
                code = ReplenishmentCatalogChangeCode.NoChange;
                return true;
            }
            if (catalog.Revision == int.MaxValue)
            {
                updated = catalog;
                code = ReplenishmentCatalogChangeCode.RevisionExhausted;
                return false;
            }
            updated = Rebuild(
                catalog,
                catalog.Revision + 1,
                catalog.NextOrdinal,
                cursor,
                catalog.Destinations);
            code = ReplenishmentCatalogChangeCode.Applied;
            return true;
        }

        /// <summary>Stages the one supported legacy singleton as slot zero without touching a ZDO.</summary>
        internal static MultiReplenishmentCatalog FromLegacySingleton(
            string catalogId,
            string stationId,
            StoredProductionLink legacyLink,
            ReplenishmentPlan legacyPlan)
        {
            var destination = new ReplenishmentDestinationRecord(
                0,
                1L,
                1,
                ReplenishmentDestinationState.Active,
                ProductionStopCode.Ready,
                legacyLink,
                legacyPlan);
            if (!string.Equals(
                    destination.Plan.Link.StationId,
                    stationId,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The legacy plan is not bound to the requested station.",
                    nameof(legacyPlan));
            return new MultiReplenishmentCatalog(
                catalogId,
                stationId,
                1,
                2L,
                1L,
                new[] { destination });
        }

        private static MultiReplenishmentCatalog Rebuild(
            MultiReplenishmentCatalog catalog,
            int revision,
            long nextOrdinal,
            long cursor,
            IEnumerable<ReplenishmentDestinationRecord> records) =>
            new MultiReplenishmentCatalog(
                catalog.CatalogId,
                catalog.StationId,
                revision,
                nextOrdinal,
                cursor,
                records);

        private static List<ReplenishmentDestinationRecord> Replace(
            MultiReplenishmentCatalog catalog,
            ReplenishmentDestinationRecord current,
            ReplenishmentDestinationRecord replacement)
        {
            var records = new List<ReplenishmentDestinationRecord>(catalog.Destinations.Count);
            foreach (ReplenishmentDestinationRecord destination in catalog.Destinations)
                records.Add(ReferenceEquals(destination, current) ? replacement : destination);
            return records;
        }

        private static int FindFirstFreeSlot(MultiReplenishmentCatalog catalog)
        {
            var occupied = new bool[MultiReplenishmentCatalog.HardMaximumDestinations];
            foreach (ReplenishmentDestinationRecord destination in catalog.Destinations)
                occupied[destination.Slot] = true;
            for (int slot = 0; slot < occupied.Length; slot++)
                if (!occupied[slot]) return slot;
            return MultiReplenishmentCatalog.HardMaximumDestinations - 1;
        }

        private static void ValidateStation(
            MultiReplenishmentCatalog catalog,
            ReplenishmentDestinationRecord destination)
        {
            if (!string.Equals(
                    catalog.StationId,
                    destination.Plan.Link.StationId,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "The replenishment plan is not bound to this station catalog.",
                    nameof(destination));
        }

        internal static bool SameTargetIdentity(
            StoredProductionLink left,
            StoredProductionLink right)
        {
            if (left == null || right == null) return false;
            bool leftStable = StockDomainValidation.IsWorldObjectToken(left.TargetToken);
            bool rightStable = StockDomainValidation.IsWorldObjectToken(right.TargetToken);
            if (leftStable || rightStable)
                return leftStable && rightStable &&
                       left.TargetPrefabHash != 0 &&
                       left.TargetPrefabHash == right.TargetPrefabHash &&
                       string.Equals(left.TargetToken, right.TargetToken, StringComparison.Ordinal);
            return left.Target == right.Target;
        }

        private static bool IsExactLegacyIdentityUpgrade(
            StoredProductionLink legacyLink,
            StoredProductionLink upgradedLink) =>
            legacyLink != null && upgradedLink != null &&
            string.IsNullOrEmpty(legacyLink.TargetToken) &&
            legacyLink.TargetPrefabHash == 0 &&
            upgradedLink.TargetPrefabHash != 0 &&
            StockDomainValidation.IsWorldObjectToken(upgradedLink.TargetToken) &&
            !upgradedLink.Target.IsNone() &&
            legacyLink.Role == ProductionLinkRole.Replenishment &&
            upgradedLink.Role == legacyLink.Role &&
            upgradedLink.ExpectedPosition == legacyLink.ExpectedPosition &&
            upgradedLink.OwnerId == legacyLink.OwnerId &&
            upgradedLink.StationOwnerId == legacyLink.StationOwnerId &&
            upgradedLink.TargetOwnerId == legacyLink.TargetOwnerId &&
            upgradedLink.Revision == legacyLink.Revision &&
            string.Equals(
                upgradedLink.LinkId,
                legacyLink.LinkId,
                StringComparison.Ordinal);

        private static bool PlanMatchesLink(
            ReplenishmentLinkBinding binding,
            StoredProductionLink link)
        {
            if (binding == null || link == null) return false;
            if (string.IsNullOrEmpty(binding.TargetToken))
                return string.Equals(
                    binding.TargetId,
                    link.Target.ToString(),
                    StringComparison.Ordinal);
            return binding.TargetPrefabHash == link.TargetPrefabHash &&
                   string.Equals(
                       binding.TargetToken,
                       link.TargetToken,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       binding.TargetId,
                       link.TargetToken,
                       StringComparison.Ordinal);
        }

        private static void ValidateSoftMaximum(int softMaximumDestinations)
        {
            if (softMaximumDestinations < 1 ||
                softMaximumDestinations > MultiReplenishmentCatalog.HardMaximumDestinations)
                throw new ArgumentOutOfRangeException(nameof(softMaximumDestinations));
        }

        private static void RequireCatalog(MultiReplenishmentCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        }

        private static bool Reject(
            MultiReplenishmentCatalog catalog,
            ReplenishmentCatalogChangeCode rejection,
            out MultiReplenishmentCatalog updated,
            out ReplenishmentDestinationRecord destination,
            out ReplenishmentCatalogChangeCode code)
        {
            updated = catalog;
            destination = null;
            code = rejection;
            return false;
        }
    }
}
