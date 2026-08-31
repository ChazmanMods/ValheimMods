using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal interface IPortalAccessEvaluator
    {
        bool Allows(PortalEndpoint endpoint, string travelerStableId, PortalAccessAction action);
    }

    internal sealed class PortalGraph
    {
        private static readonly PortalEndpoint[] EmptyEndpoints = Array.Empty<PortalEndpoint>();
        private readonly Dictionary<string, PortalEndpoint> _byId;
        private readonly Dictionary<string, PortalEndpoint[]> _byNetwork;

        internal PortalGraph(IEnumerable<PortalEndpoint> endpoints, int maximumEndpoints)
        {
            if (maximumEndpoints < 1 || maximumEndpoints > PortalContractLimits.MaximumGraphEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumEndpoints));
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            var list = new List<PortalEndpoint>();
            _byId = new Dictionary<string, PortalEndpoint>(StringComparer.Ordinal);
            foreach (PortalEndpoint endpoint in endpoints)
            {
                if (endpoint == null) throw new ArgumentException("Graph endpoints cannot contain null.", nameof(endpoints));
                if (_byId.ContainsKey(endpoint.PortalId))
                    throw new ArgumentException("Duplicate stable portal ID: " + endpoint.PortalId, nameof(endpoints));
                if (list.Count >= maximumEndpoints)
                    throw new PortalGraphLimitException(maximumEndpoints);
                _byId.Add(endpoint.PortalId, endpoint);
                list.Add(endpoint);
            }

            list.Sort(CompareEndpoint);
            Endpoints = list.ToArray();
            _byNetwork = BuildNetworks(Endpoints);
        }

        internal PortalEndpoint[] Endpoints { get; }
        internal int Count => Endpoints.Length;

        internal bool TryGet(string portalId, out PortalEndpoint endpoint)
        {
            endpoint = null;
            return !string.IsNullOrEmpty(portalId) && _byId.TryGetValue(portalId, out endpoint);
        }

        internal PortalDirectoryResult Query(
            PortalDirectoryQuery query,
            IPortalAccessEvaluator access)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (access == null) throw new ArgumentNullException(nameof(access));

            if (!ValidateSource(query, access, out RouteStopCode sourceStop, out PortalEndpoint source))
                return new PortalDirectoryResult(Array.Empty<PortalDirectoryEntry>(), false, sourceStop);

            PortalEndpoint[] candidates = Network(query.NetworkId);
            var visible = new List<PortalEndpoint>(Math.Min(candidates.Length, query.MaximumResults + 1));
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool truncated = false;

            for (int index = 0; index < candidates.Length; index++)
            {
                PortalEndpoint endpoint = candidates[index];
                if (!EligibleDirectoryEndpoint(endpoint, query, access)) continue;
                if (names.TryGetValue(endpoint.DisplayName, out int count))
                    names[endpoint.DisplayName] = count + 1;
                else
                    names.Add(endpoint.DisplayName, 1);
                if (visible.Count < query.MaximumResults) visible.Add(endpoint);
                else truncated = true;
            }

            var entries = new PortalDirectoryEntry[visible.Count];
            for (int index = 0; index < visible.Count; index++)
            {
                PortalEndpoint endpoint = visible[index];
                entries[index] = new PortalDirectoryEntry(endpoint, names[endpoint.DisplayName] > 1);
            }
            return new PortalDirectoryResult(entries, truncated, RouteStopCode.Ready);
        }

        internal PortalNameResolution ResolveName(
            PortalDirectoryQuery scope,
            string displayName,
            IPortalAccessEvaluator access)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (access == null) throw new ArgumentNullException(nameof(access));
            string normalized = PortalText.Require(displayName, PortalContractLimits.MaximumNameLength,
                nameof(displayName));
            if (!ValidateSource(scope, access, out RouteStopCode sourceStop, out PortalEndpoint source))
                return new PortalNameResolution(sourceStop, Array.Empty<PortalDirectoryEntry>());

            PortalEndpoint[] endpoints = Network(scope.NetworkId);
            var matches = new List<PortalEndpoint>();
            for (int index = 0; index < endpoints.Length; index++)
            {
                PortalEndpoint endpoint = endpoints[index];
                if (!string.Equals(endpoint.DisplayName, normalized, StringComparison.OrdinalIgnoreCase) ||
                    !EligibleDirectoryEndpoint(endpoint, scope, access)) continue;
                matches.Add(endpoint);
                if (matches.Count > PortalContractLimits.MaximumDirectoryResults)
                    return new PortalNameResolution(RouteStopCode.GraphLimitExceeded,
                        Array.Empty<PortalDirectoryEntry>());
            }

            if (matches.Count == 0)
                return new PortalNameResolution(RouteStopCode.NotFoundOrUnauthorized,
                    Array.Empty<PortalDirectoryEntry>());
            bool duplicate = matches.Count > 1;
            var entries = new PortalDirectoryEntry[matches.Count];
            for (int index = 0; index < matches.Count; index++)
                entries[index] = new PortalDirectoryEntry(matches[index], duplicate);
            return new PortalNameResolution(
                duplicate ? RouteStopCode.DuplicateName : RouteStopCode.Ready,
                entries);
        }

        internal RoutePlan Plan(RoutePlanRequest request, IPortalAccessEvaluator access)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (access == null) throw new ArgumentNullException(nameof(access));

            if (!_byId.TryGetValue(request.SourcePortalId, out PortalEndpoint source) ||
                !_byId.TryGetValue(request.DestinationPortalId, out PortalEndpoint destination) ||
                !CanDiscover(source, request.TravelerStableId, access) ||
                !CanDiscover(destination, request.TravelerStableId, access))
                return Stop(RouteStopCode.NotFoundOrUnauthorized, request, null, null, false);
            if (source.Mode != PortalMode.Network || source.OnlineState != PortalOnlineState.Online)
                return Stop(RouteStopCode.SourceUnavailable, request, source, destination, false);
            if (destination.Mode != PortalMode.Network || destination.OnlineState != PortalOnlineState.Online)
                return Stop(RouteStopCode.DestinationUnavailable, request, source, destination, false);
            // The exact NetworkName is the routing boundary. Access is evaluated independently
            // for the source and destination, so a traveler may depart a public endpoint and
            // arrive at an authorized private or Group endpoint with the same exact name.
            if (!string.Equals(source.NetworkId, destination.NetworkId, StringComparison.Ordinal))
                return Stop(RouteStopCode.NetworkMismatch, request, source, destination, false);
            if (string.Equals(source.PortalId, destination.PortalId, StringComparison.Ordinal))
                return Stop(RouteStopCode.SameEndpoint, request, source, destination, false);
            if (request.SourceRevision >= 0 && request.SourceRevision != source.Revision ||
                request.DestinationRevision >= 0 && request.DestinationRevision != destination.Revision)
                return Stop(RouteStopCode.StaleSelection, request, source, destination, false);
            if (!source.PermitsDeparture ||
                !access.Allows(source, request.TravelerStableId, PortalAccessAction.Depart))
                return Stop(RouteStopCode.DepartureDenied, request, source, destination, false);
            if (!destination.AcceptsArrival ||
                !access.Allows(destination, request.TravelerStableId, PortalAccessAction.Arrive))
                return Stop(RouteStopCode.ArrivalDenied, request, source, destination, false);

            RouteStopCode policyStop = TravelPolicyStop(request.TravelPolicy);
            if (policyStop != RouteStopCode.Ready)
                return Stop(policyStop, request, source, destination, false);

            bool reverseAllowed = destination.PermitsDeparture && source.AcceptsArrival &&
                access.Allows(destination, request.TravelerStableId, PortalAccessAction.Depart) &&
                access.Allows(source, request.TravelerStableId, PortalAccessAction.Arrive);
            bool oneWay = !reverseAllowed;
            if (oneWay && !request.OneWayAcknowledged)
                return Stop(RouteStopCode.OneWayWarningRequired, request, source, destination, true);
            return Stop(RouteStopCode.Ready, request, source, destination, oneWay);
        }

        private bool ValidateSource(
            PortalDirectoryQuery query,
            IPortalAccessEvaluator access,
            out RouteStopCode stop,
            out PortalEndpoint source)
        {
            stop = RouteStopCode.Ready;
            source = null;
            if (query.SourcePortalId.Length == 0) return true;
            if (!_byId.TryGetValue(query.SourcePortalId, out source) ||
                source.Mode != PortalMode.Network ||
                !string.Equals(source.NetworkId, query.NetworkId, StringComparison.Ordinal) ||
                !CanDiscover(source, query.TravelerStableId, access))
            {
                stop = RouteStopCode.NotFoundOrUnauthorized;
                return false;
            }
            if (source.OnlineState != PortalOnlineState.Online || !source.PermitsDeparture ||
                !access.Allows(source, query.TravelerStableId, PortalAccessAction.Depart))
            {
                stop = RouteStopCode.SourceUnavailable;
                return false;
            }
            return true;
        }

        private static bool EligibleDirectoryEndpoint(
            PortalEndpoint endpoint,
            PortalDirectoryQuery query,
            IPortalAccessEvaluator access)
        {
            if (endpoint.Mode != PortalMode.Network ||
                string.Equals(endpoint.PortalId, query.SourcePortalId, StringComparison.Ordinal) ||
                !endpoint.AcceptsArrival ||
                endpoint.OnlineState == PortalOnlineState.Disabled ||
                endpoint.OnlineState == PortalOnlineState.Destroyed ||
                endpoint.OnlineState == PortalOnlineState.Stale ||
                !query.IncludeOffline && endpoint.OnlineState != PortalOnlineState.Online ||
                !CanDiscover(endpoint, query.TravelerStableId, access) ||
                !access.Allows(endpoint, query.TravelerStableId, PortalAccessAction.Arrive)) return false;
            if (query.Search.Length == 0) return true;
            return endpoint.DisplayName.IndexOf(query.Search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   endpoint.OwnerDisplayName.IndexOf(query.Search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   endpoint.KnownBiome.IndexOf(query.Search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanDiscover(
            PortalEndpoint endpoint,
            string travelerStableId,
            IPortalAccessEvaluator access) =>
            access.Allows(endpoint, travelerStableId, PortalAccessAction.ViewDiscover);

        private PortalEndpoint[] Network(string networkId) =>
            _byNetwork.TryGetValue(networkId, out PortalEndpoint[] endpoints) ? endpoints : EmptyEndpoints;

        private static Dictionary<string, PortalEndpoint[]> BuildNetworks(PortalEndpoint[] endpoints)
        {
            var lists = new Dictionary<string, List<PortalEndpoint>>(StringComparer.Ordinal);
            for (int index = 0; index < endpoints.Length; index++)
            {
                PortalEndpoint endpoint = endpoints[index];
                if (!lists.TryGetValue(endpoint.NetworkId, out List<PortalEndpoint> list))
                {
                    list = new List<PortalEndpoint>();
                    lists.Add(endpoint.NetworkId, list);
                }
                list.Add(endpoint);
            }
            var result = new Dictionary<string, PortalEndpoint[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<PortalEndpoint>> pair in lists)
                result.Add(pair.Key, pair.Value.ToArray());
            return result;
        }

        private static int CompareEndpoint(PortalEndpoint left, PortalEndpoint right)
        {
            int network = string.Compare(left.NetworkId, right.NetworkId, StringComparison.Ordinal);
            if (network != 0) return network;
            int name = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (name != 0) return name;
            int owner = string.Compare(left.OwnerStableId, right.OwnerStableId, StringComparison.Ordinal);
            return owner != 0
                ? owner
                : string.Compare(left.PortalId, right.PortalId, StringComparison.Ordinal);
        }

        private static RoutePlan Stop(
            RouteStopCode stop,
            RoutePlanRequest request,
            PortalEndpoint source,
            PortalEndpoint destination,
            bool oneWay) =>
            new RoutePlan(
                stop,
                request.SourcePortalId,
                request.DestinationPortalId,
                oneWay,
                source?.Revision ?? -1,
                destination?.Revision ?? -1);

        private static RouteStopCode TravelPolicyStop(TravelPolicyState policy)
        {
            switch (policy)
            {
                case TravelPolicyState.Allowed: return RouteStopCode.Ready;
                case TravelPolicyState.RestrictedItems: return RouteStopCode.RestrictedItems;
                case TravelPolicyState.PortalsDisabled: return RouteStopCode.PortalsDisabled;
                case TravelPolicyState.BossTravelBlocked: return RouteStopCode.BossTravelBlocked;
                default: return RouteStopCode.PolicyUnknown;
            }
        }
    }

    internal sealed class PortalGraphLimitException : InvalidOperationException
    {
        internal PortalGraphLimitException(int maximum)
            : base("Portal graph exceeded the configured endpoint limit of " + maximum + ".")
        {
            Maximum = maximum;
        }

        internal int Maximum { get; }
    }
}
