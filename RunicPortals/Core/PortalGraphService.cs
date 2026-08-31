using System;
using System.Collections.Generic;
using RunicPortals.Api;

namespace RunicPortals.Core
{
    internal sealed class PortalGraphService : IPortalDirectoryService, IPortalRoutePlanner
    {
        private readonly object _sync = new object();
        private readonly IPortalAccessEvaluator _access;
        private PortalGraph _graph;
        private int _maximumEndpoints;
        private RouteStopCode _snapshotStop;

        internal PortalGraphService(IPortalAccessEvaluator access, int maximumEndpoints)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access));
            SetMaximumEndpoints(maximumEndpoints);
            _graph = new PortalGraph(Array.Empty<PortalEndpoint>(), _maximumEndpoints);
        }

        internal int Count
        {
            get { lock (_sync) return _graph.Count; }
        }

        internal RouteStopCode SnapshotStop
        {
            get { lock (_sync) return _snapshotStop; }
        }

        internal void SetMaximumEndpoints(int maximumEndpoints)
        {
            if (maximumEndpoints < 1 || maximumEndpoints > PortalContractLimits.MaximumGraphEndpoints)
                throw new ArgumentOutOfRangeException(nameof(maximumEndpoints));
            lock (_sync) _maximumEndpoints = maximumEndpoints;
        }

        internal bool TryReplaceAuthoritativeSnapshot(
            IEnumerable<PortalEndpoint> endpoints,
            out string failure)
        {
            failure = string.Empty;
            try
            {
                int maximum;
                lock (_sync) maximum = _maximumEndpoints;
                var candidate = new PortalGraph(endpoints, maximum);
                lock (_sync)
                {
                    _graph = candidate;
                    _snapshotStop = RouteStopCode.Ready;
                }
                return true;
            }
            catch (PortalGraphLimitException exception)
            {
                lock (_sync) _snapshotStop = RouteStopCode.GraphLimitExceeded;
                failure = exception.Message;
                return false;
            }
            catch (ArgumentException exception)
            {
                lock (_sync) _snapshotStop = RouteStopCode.StaleSelection;
                failure = exception.Message;
                return false;
            }
        }

        internal void RejectAuthoritativeSnapshot(RouteStopCode stop)
        {
            if (stop == RouteStopCode.Ready)
                throw new ArgumentOutOfRangeException(nameof(stop));
            lock (_sync) _snapshotStop = stop;
        }

        public PortalDirectoryResult Query(PortalDirectoryQuery query)
        {
            lock (_sync)
            {
                if (_snapshotStop != RouteStopCode.Ready)
                    return new PortalDirectoryResult(Array.Empty<PortalDirectoryEntry>(), false, _snapshotStop);
                return _graph.Query(query, _access);
            }
        }

        public PortalNameResolution ResolveName(PortalDirectoryQuery scope, string displayName)
        {
            lock (_sync)
            {
                if (_snapshotStop != RouteStopCode.Ready)
                    return new PortalNameResolution(_snapshotStop, Array.Empty<PortalDirectoryEntry>());
                return _graph.ResolveName(scope, displayName, _access);
            }
        }

        public RoutePlan Plan(RoutePlanRequest request)
        {
            lock (_sync)
            {
                if (_snapshotStop != RouteStopCode.Ready)
                    return new RoutePlan(_snapshotStop, request?.SourcePortalId,
                        request?.DestinationPortalId, false, -1, -1);
                return _graph.Plan(request, _access);
            }
        }

        internal bool TryGetEndpoint(string portalId, out PortalEndpoint endpoint)
        {
            lock (_sync)
            {
                if (_snapshotStop != RouteStopCode.Ready)
                {
                    endpoint = null;
                    return false;
                }
                return _graph.TryGet(portalId, out endpoint);
            }
        }

        internal PortalEndpoint[] SnapshotEndpoints()
        {
            lock (_sync)
            {
                if (_snapshotStop != RouteStopCode.Ready)
                    return Array.Empty<PortalEndpoint>();
                return (PortalEndpoint[])_graph.Endpoints.Clone();
            }
        }
    }
}
