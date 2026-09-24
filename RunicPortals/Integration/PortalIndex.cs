using System;
using System.Collections.Generic;
using RunicPortals.Api;
using RunicPortals.Core;

namespace RunicPortals.Integration
{
    internal sealed class PortalIndex
    {
        private readonly PortalGraphService _graph;
        private readonly CorrelatedDiagnosticBuffer _diagnostics;
        private readonly Dictionary<string, ZDO> _byId =
            new Dictionary<string, ZDO>(StringComparer.Ordinal);
        private readonly List<ZDO> _sorted = new List<ZDO>();
        private readonly List<PortalEndpoint> _endpoints = new List<PortalEndpoint>();
        private float _nextRefresh;
        private bool _dirty = true;

        internal PortalIndex(PortalGraphService graph, CorrelatedDiagnosticBuffer diagnostics)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        internal int Count => _graph.Count;

        internal void MarkDirty() => _dirty = true;

        internal bool Tick(float realtime, float interval)
        {
            if (!ValheimContracts.IsServer || !_dirty && realtime < _nextRefresh) return false;
            Rebuild(realtime, interval);
            return true;
        }

        internal bool Rebuild(float realtime, float interval)
        {
            _nextRefresh = realtime + Math.Max(0.5f, interval);
            _dirty = false;
            List<ZDO> source = ValheimContracts.PortalObjects();
            if (source == null)
            {
                _graph.RejectAuthoritativeSnapshot(RouteStopCode.AuthorityUnavailable);
                _diagnostics.Record(PortalDiagnosticCode.SnapshotRejected,
                    RouteStopCode.AuthorityUnavailable);
                return false;
            }
            if (source.Count > ValheimContracts.MaximumPortalObjectsScanned)
            {
                _graph.RejectAuthoritativeSnapshot(RouteStopCode.GraphLimitExceeded);
                _diagnostics.Record(PortalDiagnosticCode.SnapshotRejected,
                    RouteStopCode.GraphLimitExceeded);
                return false;
            }

            _sorted.Clear();
            for (int index = 0; index < source.Count; index++)
            {
                ZDO zdo = source[index];
                if (zdo != null && zdo.IsValid() && !zdo.m_uid.IsNone()) _sorted.Add(zdo);
            }
            _sorted.Sort(CompareZdo);
            _endpoints.Clear();
            var candidateMap = new Dictionary<string, ZDO>(StringComparer.Ordinal);
            for (int index = 0; index < _sorted.Count; index++)
            {
                ZDO zdo = _sorted[index];
                if (!PortalZdoCodec.TryReadDestination(zdo, out PortalEndpoint endpoint, out string failure))
                {
                    if (failure == "schema-unsupported" || failure == "mode-unsupported" ||
                        failure == "record-invalid")
                        _diagnostics.Record(PortalDiagnosticCode.SnapshotRejected,
                            RouteStopCode.StaleSelection, zdo.m_uid.ToString());
                    continue;
                }
                if (candidateMap.ContainsKey(endpoint.PortalId))
                {
                    _graph.RejectAuthoritativeSnapshot(RouteStopCode.StaleSelection);
                    _diagnostics.Record(PortalDiagnosticCode.SnapshotRejected,
                        RouteStopCode.StaleSelection, endpoint.PortalId);
                    return false;
                }
                _endpoints.Add(endpoint);
                candidateMap.Add(endpoint.PortalId, zdo);
            }

            if (!_graph.TryReplaceAuthoritativeSnapshot(_endpoints, out string graphFailure))
            {
                Diagnostics.Warning("Portal graph snapshot rejected: " + graphFailure);
                _diagnostics.Record(PortalDiagnosticCode.SnapshotRejected, _graph.SnapshotStop);
                return false;
            }
            _byId.Clear();
            foreach (KeyValuePair<string, ZDO> pair in candidateMap) _byId.Add(pair.Key, pair.Value);
            _diagnostics.Record(PortalDiagnosticCode.SnapshotAccepted);
            return true;
        }

        internal bool TryGetZdo(string portalId, out ZDO zdo)
        {
            zdo = null;
            if (!_byId.TryGetValue(portalId ?? string.Empty, out ZDO candidate) ||
                candidate == null || !candidate.IsValid() || candidate.m_uid.IsNone()) return false;
            if (ZDOMan.instance == null || ZDOMan.instance.GetZDO(candidate.m_uid) != candidate) return false;
            zdo = candidate;
            return true;
        }

        internal void Clear()
        {
            _byId.Clear();
            _sorted.Clear();
            _endpoints.Clear();
            _graph.TryReplaceAuthoritativeSnapshot(Array.Empty<PortalEndpoint>(), out _);
            _dirty = true;
            _nextRefresh = 0f;
        }

        private static int CompareZdo(ZDO left, ZDO right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int user = left.m_uid.UserID.CompareTo(right.m_uid.UserID);
            return user != 0 ? user : left.m_uid.ID.CompareTo(right.m_uid.ID);
        }
    }
}
