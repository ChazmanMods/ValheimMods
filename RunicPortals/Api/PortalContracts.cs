using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicPortals.Api
{
    public static class PortalContractLimits
    {
        public const int MaximumPortalIdLength = 96;
        public const int MaximumNameLength = 64;
        public const int MaximumNetworkIdLength = 64;
        public const int MaximumOwnerLabelLength = 64;
        public const int MaximumBiomeLabelLength = 48;
        public const int MaximumSearchLength = 64;
        public const int MaximumApprovedIdentities = 64;
        public const int MaximumGraphEndpoints = 2048;
        public const int MaximumDirectoryResults = MaximumGraphEndpoints;
        public const int MaximumDiagnostics = 256;
        public const int MaximumReturnRoutes = 256;
        public const int MaximumSelections = 256;
    }

    public enum PortalMode
    {
        StandardPair = 0,
        Network = 1
    }

    public enum PortalNetworkKind
    {
        Public = 1,
        Personal = 2,
        Ward = 3,
        Group = 4,
        Custom = 5
    }

    public enum PortalOnlineState
    {
        Online = 1,
        Offline = 2,
        Disabled = 3,
        Destroyed = 4,
        Stale = 5
    }

    public enum PortalPolicyKind
    {
        Everyone = 1,
        Approved = 2,
        Owner = 3,
        Ward = 4,
        WardWithExceptions = 5,
        Group = 6,
        Hidden = 7,
        Disabled = 8
    }

    public enum PortalAccessAction
    {
        ViewDiscover = 1,
        Arrive = 2,
        Depart = 3,
        Edit = 4,
        Invite = 5,
        Publish = 6
    }

    public enum TravelPolicyState
    {
        Allowed = 1,
        RestrictedItems = 2,
        PortalsDisabled = 3,
        BossTravelBlocked = 4,
        Unknown = 5
    }

    public enum RouteStopCode
    {
        Ready = 0,
        FeatureDisabled = 1,
        NotFoundOrUnauthorized = 2,
        SourceUnavailable = 3,
        DestinationUnavailable = 4,
        NetworkMismatch = 5,
        SameEndpoint = 6,
        DepartureDenied = 7,
        ArrivalDenied = 8,
        RestrictedItems = 9,
        PortalsDisabled = 10,
        BossTravelBlocked = 11,
        PolicyUnknown = 12,
        OneWayWarningRequired = 13,
        DuplicateName = 14,
        GraphLimitExceeded = 15,
        StaleSelection = 16,
        AuthorityUnavailable = 17,
        TeleportRejected = 18
    }

    public sealed class PortalAccessPolicy
    {
        private readonly string[] _approved;
        private readonly ReadOnlyCollection<string> _approvedView;

        public PortalAccessPolicy(
            PortalPolicyKind kind,
            IEnumerable<string> approvedStableIds = null,
            string groupId = "")
        {
            if (!Enum.IsDefined(typeof(PortalPolicyKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            GroupId = PortalText.NormalizeOptional(groupId, PortalContractLimits.MaximumNetworkIdLength,
                nameof(groupId));
            var unique = new SortedSet<string>(StringComparer.Ordinal);
            if (approvedStableIds != null)
            {
                foreach (string identity in approvedStableIds)
                {
                    string normalized = PortalText.Require(identity, PortalContractLimits.MaximumPortalIdLength,
                        nameof(approvedStableIds));
                    unique.Add(normalized);
                    if (unique.Count > PortalContractLimits.MaximumApprovedIdentities)
                        throw new ArgumentOutOfRangeException(nameof(approvedStableIds));
                }
            }
            _approved = new string[unique.Count];
            unique.CopyTo(_approved);
            _approvedView = Array.AsReadOnly(_approved);
            if (Kind == PortalPolicyKind.Group && GroupId.Length == 0)
                throw new ArgumentException("Group policy requires a group identifier.", nameof(groupId));
        }

        public PortalPolicyKind Kind { get; }
        public IReadOnlyList<string> ApprovedStableIds => _approvedView;
        public string GroupId { get; }

        public bool IsExplicitlyApproved(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return false;
            return Array.BinarySearch(_approved, stableId, StringComparer.Ordinal) >= 0;
        }
    }

    public sealed class PortalAccessProfile
    {
        private static readonly PortalAccessPolicy DisabledPolicy =
            new PortalAccessPolicy(PortalPolicyKind.Disabled);
        private readonly Dictionary<PortalAccessAction, PortalAccessPolicy> _policies;
        private readonly ReadOnlyDictionary<PortalAccessAction, PortalAccessPolicy> _view;

        public PortalAccessProfile(
            IEnumerable<KeyValuePair<PortalAccessAction, PortalAccessPolicy>> policies)
        {
            _policies = new Dictionary<PortalAccessAction, PortalAccessPolicy>();
            if (policies == null) throw new ArgumentNullException(nameof(policies));
            foreach (KeyValuePair<PortalAccessAction, PortalAccessPolicy> pair in policies)
            {
                if (!Enum.IsDefined(typeof(PortalAccessAction), pair.Key) || pair.Value == null)
                    throw new ArgumentException("Access profiles cannot contain unknown actions or null policies.",
                        nameof(policies));
                if (_policies.ContainsKey(pair.Key))
                    throw new ArgumentException("Access profiles cannot contain duplicate actions.", nameof(policies));
                _policies.Add(pair.Key, pair.Value);
            }
            _view = new ReadOnlyDictionary<PortalAccessAction, PortalAccessPolicy>(_policies);
        }

        public IReadOnlyDictionary<PortalAccessAction, PortalAccessPolicy> Policies => _view;

        public PortalAccessPolicy GetPolicy(PortalAccessAction action)
        {
            return _policies.TryGetValue(action, out PortalAccessPolicy policy)
                ? policy
                : DisabledPolicy;
        }

        public static PortalAccessProfile PublicNetwork { get; } = CreatePublicNetwork();
        public static PortalAccessProfile PrivateNetwork { get; } = CreatePrivateNetwork();

        public static PortalAccessProfile ForGroup(string groupId)
        {
            var group = new PortalAccessPolicy(PortalPolicyKind.Group, groupId: groupId);
            var owner = new PortalAccessPolicy(PortalPolicyKind.Owner);
            return new PortalAccessProfile(new[]
            {
                Pair(PortalAccessAction.ViewDiscover, group),
                Pair(PortalAccessAction.Arrive, group),
                Pair(PortalAccessAction.Depart, group),
                Pair(PortalAccessAction.Edit, owner),
                Pair(PortalAccessAction.Invite, owner),
                Pair(PortalAccessAction.Publish, owner)
            });
        }

        private static PortalAccessProfile CreatePublicNetwork()
        {
            var everyone = new PortalAccessPolicy(PortalPolicyKind.Everyone);
            var owner = new PortalAccessPolicy(PortalPolicyKind.Owner);
            return new PortalAccessProfile(new[]
            {
                Pair(PortalAccessAction.ViewDiscover, everyone),
                Pair(PortalAccessAction.Arrive, everyone),
                Pair(PortalAccessAction.Depart, everyone),
                Pair(PortalAccessAction.Edit, owner),
                Pair(PortalAccessAction.Invite, owner),
                Pair(PortalAccessAction.Publish, owner)
            });
        }

        private static PortalAccessProfile CreatePrivateNetwork()
        {
            var owner = new PortalAccessPolicy(PortalPolicyKind.Owner);
            return new PortalAccessProfile(new[]
            {
                Pair(PortalAccessAction.ViewDiscover, owner),
                Pair(PortalAccessAction.Arrive, owner),
                Pair(PortalAccessAction.Depart, owner),
                Pair(PortalAccessAction.Edit, owner),
                Pair(PortalAccessAction.Invite, owner),
                Pair(PortalAccessAction.Publish, owner)
            });
        }

        private static KeyValuePair<PortalAccessAction, PortalAccessPolicy> Pair(
            PortalAccessAction action, PortalAccessPolicy policy) =>
            new KeyValuePair<PortalAccessAction, PortalAccessPolicy>(action, policy);
    }

    public sealed class PortalEndpoint
    {
        public PortalEndpoint(
            string portalId,
            PortalMode mode,
            string displayName,
            string networkId,
            PortalNetworkKind networkKind,
            string ownerStableId,
            string ownerDisplayName,
            PortalOnlineState onlineState,
            bool acceptsArrival,
            bool permitsDeparture,
            PortalAccessProfile access,
            long revision,
            string knownBiome = "")
        {
            PortalId = PortalText.Require(portalId, PortalContractLimits.MaximumPortalIdLength,
                nameof(portalId));
            if (!Enum.IsDefined(typeof(PortalMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (!Enum.IsDefined(typeof(PortalNetworkKind), networkKind))
                throw new ArgumentOutOfRangeException(nameof(networkKind));
            if (!Enum.IsDefined(typeof(PortalOnlineState), onlineState))
                throw new ArgumentOutOfRangeException(nameof(onlineState));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Mode = mode;
            DisplayName = PortalText.NormalizeOptional(displayName, PortalContractLimits.MaximumNameLength,
                nameof(displayName));
            NetworkId = PortalText.NormalizeOptional(networkId, PortalContractLimits.MaximumNetworkIdLength,
                nameof(networkId));
            NetworkKind = networkKind;
            OwnerStableId = PortalText.NormalizeOptional(ownerStableId,
                PortalContractLimits.MaximumPortalIdLength, nameof(ownerStableId));
            OwnerDisplayName = PortalText.NormalizeOptional(ownerDisplayName,
                PortalContractLimits.MaximumOwnerLabelLength, nameof(ownerDisplayName));
            OnlineState = onlineState;
            AcceptsArrival = acceptsArrival;
            PermitsDeparture = permitsDeparture;
            Access = access ?? throw new ArgumentNullException(nameof(access));
            Revision = revision;
            KnownBiome = PortalText.NormalizeOptional(knownBiome, PortalContractLimits.MaximumBiomeLabelLength,
                nameof(knownBiome));
            if (Mode == PortalMode.Network &&
                (DisplayName.Length == 0 || NetworkId.Length == 0 || OwnerStableId.Length == 0))
                throw new ArgumentException("Network endpoints require a name, network, and stable owner.");
        }

        public string PortalId { get; }
        public PortalMode Mode { get; }
        public string DisplayName { get; }
        public string NetworkId { get; }
        public PortalNetworkKind NetworkKind { get; }
        public string OwnerStableId { get; }
        public string OwnerDisplayName { get; }
        public PortalOnlineState OnlineState { get; }
        public bool AcceptsArrival { get; }
        public bool PermitsDeparture { get; }
        public PortalAccessProfile Access { get; }
        public long Revision { get; }
        public string KnownBiome { get; }
    }

    public sealed class PortalDirectoryQuery
    {
        public PortalDirectoryQuery(
            string travelerStableId,
            string sourcePortalId,
            string networkId,
            string search = "",
            int maximumResults = 32,
            bool includeOffline = false)
        {
            TravelerStableId = PortalText.Require(travelerStableId,
                PortalContractLimits.MaximumPortalIdLength, nameof(travelerStableId));
            SourcePortalId = PortalText.NormalizeOptional(sourcePortalId,
                PortalContractLimits.MaximumPortalIdLength, nameof(sourcePortalId));
            NetworkId = PortalText.Require(networkId, PortalContractLimits.MaximumNetworkIdLength,
                nameof(networkId));
            Search = PortalText.NormalizeOptional(search, PortalContractLimits.MaximumSearchLength, nameof(search));
            if (maximumResults < 1 || maximumResults > PortalContractLimits.MaximumDirectoryResults)
                throw new ArgumentOutOfRangeException(nameof(maximumResults));
            MaximumResults = maximumResults;
            IncludeOffline = includeOffline;
        }

        public string TravelerStableId { get; }
        public string SourcePortalId { get; }
        public string NetworkId { get; }
        public string Search { get; }
        public int MaximumResults { get; }
        public bool IncludeOffline { get; }
    }

    public sealed class PortalDirectoryEntry
    {
        internal PortalDirectoryEntry(PortalEndpoint endpoint, bool duplicateName)
        {
            PortalId = endpoint.PortalId;
            DisplayName = endpoint.DisplayName;
            NetworkId = endpoint.NetworkId;
            OwnerDisplayName = endpoint.OwnerDisplayName;
            OnlineState = endpoint.OnlineState;
            KnownBiome = endpoint.KnownBiome;
            AcceptsArrival = endpoint.AcceptsArrival;
            PermitsDeparture = endpoint.PermitsDeparture;
            DuplicateName = duplicateName;
            // Stable IDs are already bounded. Use the complete ID so two ZDO creators with the
            // same local object suffix can never produce an ambiguous directory label.
            Disambiguator = duplicateName ? endpoint.PortalId : string.Empty;
        }

        public string PortalId { get; }
        public string DisplayName { get; }
        public string NetworkId { get; }
        public string OwnerDisplayName { get; }
        public PortalOnlineState OnlineState { get; }
        public string KnownBiome { get; }
        public bool AcceptsArrival { get; }
        public bool PermitsDeparture { get; }
        public bool DuplicateName { get; }
        public string Disambiguator { get; }

    }

    public sealed class PortalDirectoryResult
    {
        internal PortalDirectoryResult(
            IEnumerable<PortalDirectoryEntry> entries,
            bool truncated,
            RouteStopCode stopCode)
        {
            Entries = Array.AsReadOnly(new List<PortalDirectoryEntry>(entries).ToArray());
            Truncated = truncated;
            StopCode = stopCode;
        }

        public IReadOnlyList<PortalDirectoryEntry> Entries { get; }
        public bool Truncated { get; }
        public RouteStopCode StopCode { get; }
    }

    public sealed class PortalNameResolution
    {
        internal PortalNameResolution(RouteStopCode stopCode, IEnumerable<PortalDirectoryEntry> candidates)
        {
            StopCode = stopCode;
            Candidates = Array.AsReadOnly(new List<PortalDirectoryEntry>(candidates).ToArray());
        }

        public RouteStopCode StopCode { get; }
        public IReadOnlyList<PortalDirectoryEntry> Candidates { get; }
        public bool IsUnique => StopCode == RouteStopCode.Ready && Candidates.Count == 1;
    }

    public sealed class RoutePlanRequest
    {
        public RoutePlanRequest(
            string travelerStableId,
            string sourcePortalId,
            string destinationPortalId,
            TravelPolicyState travelPolicy,
            bool oneWayAcknowledged,
            long sourceRevision = -1,
            long destinationRevision = -1)
        {
            TravelerStableId = PortalText.Require(travelerStableId,
                PortalContractLimits.MaximumPortalIdLength, nameof(travelerStableId));
            SourcePortalId = PortalText.Require(sourcePortalId,
                PortalContractLimits.MaximumPortalIdLength, nameof(sourcePortalId));
            DestinationPortalId = PortalText.Require(destinationPortalId,
                PortalContractLimits.MaximumPortalIdLength, nameof(destinationPortalId));
            if (!Enum.IsDefined(typeof(TravelPolicyState), travelPolicy))
                throw new ArgumentOutOfRangeException(nameof(travelPolicy));
            if (sourceRevision < -1 || destinationRevision < -1)
                throw new ArgumentOutOfRangeException(nameof(sourceRevision));
            TravelPolicy = travelPolicy;
            OneWayAcknowledged = oneWayAcknowledged;
            SourceRevision = sourceRevision;
            DestinationRevision = destinationRevision;
        }

        public string TravelerStableId { get; }
        public string SourcePortalId { get; }
        public string DestinationPortalId { get; }
        public TravelPolicyState TravelPolicy { get; }
        public bool OneWayAcknowledged { get; }
        public long SourceRevision { get; }
        public long DestinationRevision { get; }
    }

    public sealed class RoutePlan
    {
        internal RoutePlan(
            RouteStopCode stopCode,
            string sourcePortalId,
            string destinationPortalId,
            bool oneWay,
            long sourceRevision,
            long destinationRevision)
        {
            StopCode = stopCode;
            SourcePortalId = sourcePortalId ?? string.Empty;
            DestinationPortalId = destinationPortalId ?? string.Empty;
            IsOneWay = oneWay;
            SourceRevision = sourceRevision;
            DestinationRevision = destinationRevision;
        }

        public RouteStopCode StopCode { get; }
        public string SourcePortalId { get; }
        public string DestinationPortalId { get; }
        public bool IsOneWay { get; }
        public long SourceRevision { get; }
        public long DestinationRevision { get; }
        public bool IsReady => StopCode == RouteStopCode.Ready;
        public bool IsAdvisoryOnly => true;
    }

    public interface IPortalDirectoryService
    {
        PortalDirectoryResult Query(PortalDirectoryQuery query);
        PortalNameResolution ResolveName(PortalDirectoryQuery scope, string displayName);
    }

    public interface IPortalRoutePlanner
    {
        RoutePlan Plan(RoutePlanRequest request);
    }

    public interface IPortalStatusService
    {
        bool FeatureEnabled { get; }
        bool RuntimeReady { get; }
        bool LocalHostMutationAvailable { get; }
        bool DedicatedMutationTransportAvailable { get; }
        int IndexedEndpointCount { get; }
        string DisabledReason { get; }
    }

    internal static class PortalText
    {
        internal static string Require(string value, int maximum, string parameter)
        {
            string normalized = NormalizeOptional(value, maximum, parameter);
            if (normalized.Length == 0) throw new ArgumentException("A value is required.", parameter);
            return normalized;
        }

        internal static string NormalizeOptional(string value, int maximum, string parameter)
        {
            string raw = value ?? string.Empty;
            if (raw.Length > maximum) throw new ArgumentOutOfRangeException(parameter);
            string normalized = raw.Trim();
            if (normalized.Length > maximum) throw new ArgumentOutOfRangeException(parameter);
            for (int index = 0; index < normalized.Length; index++)
            {
                char character = normalized[index];
                if (char.IsControl(character))
                    throw new ArgumentException("Control characters are not permitted.", parameter);
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || index + 1 >= normalized.Length ||
                    !char.IsLowSurrogate(normalized[index + 1]))
                    throw new ArgumentException("Malformed Unicode is not permitted.", parameter);
                index++;
            }
            return normalized;
        }
    }
}
