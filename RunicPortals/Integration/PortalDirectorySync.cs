using System;
using System.Collections.Generic;
using RunicPortals.Api;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Supplies a joining client with the authorized portal ZDOs required by the map picker.
    /// Valheim normally replicates ZDOs by loaded zone, so a client cannot discover a remote
    /// network destination until it has visited that destination by some other means. The server
    /// already owns the authoritative portal list; this bounded request asks it to force-send only
    /// the matching endpoints that the authenticated player may discover and arrive through.
    /// Existing graph and route validation still runs again on the client before travel.
    /// </summary>
    internal sealed partial class PortalRuntime
    {
        private const string DirectoryRequestRpc = "RunicPortals.Directory.Request.v1";
        private const string DirectoryResponseRpc = "RunicPortals.Directory.Response.v1";
        private const string MapDirectoryRequestRpc = "RunicPortals.MapDirectory.Request.v1";
        private const string MapDirectoryResponseRpc = "RunicPortals.MapDirectory.Response.v1";
        private const int LegacyDirectoryWireSchema = 1;
        private const int DirectoryWireSchema = 2;
        private const int MapDirectoryWireSchema = 1;
        private const int DirectoryTerminalMarker = 0x50524431;
        private const int MapDirectoryTerminalMarker = 0x50524D31;
        private const int MaximumDirectoryEnvelopeBytes = 2048;
        private const int MaximumDirectoryEndpointsSent = PortalContractLimits.MaximumGraphEndpoints;
        private const int MaximumDirectoryAttempts = 8;
        private const float MaximumDirectoryRequestDistanceMeters = 16f;
        private const float DirectoryRequestTimeoutSeconds = 8f;
        private const float DirectoryRetrySeconds = 0.75f;
        private const float DirectoryArrivalPollSeconds = 0.1f;
        private const float MapDirectoryRefreshSeconds = 15f;

        private ZRoutedRpc _directoryRegisteredRpc;
        private string _mapDirectoryContext = string.Empty;
        private string _mapDirectoryRequestId = string.Empty;
        private float _nextMapDirectoryRequest;

        private void TickDirectorySyncTransport()
        {
            ZRoutedRpc routed = ZRoutedRpc.instance;
            if (routed == null || ReferenceEquals(_directoryRegisteredRpc, routed)) return;
            routed.Register<ZPackage>(DirectoryRequestRpc, ReceiveDirectoryRequest);
            routed.Register<ZPackage>(DirectoryResponseRpc, ReceiveDirectoryResponse);
            routed.Register<ZPackage>(MapDirectoryRequestRpc, ReceiveMapDirectoryRequest);
            routed.Register<ZPackage>(MapDirectoryResponseRpc, ReceiveMapDirectoryResponse);
            _directoryRegisteredRpc = routed;
        }

        private void ShutdownDirectorySyncTransport()
        {
            _directoryRegisteredRpc = null;
            _mapDirectoryContext = string.Empty;
            _mapDirectoryRequestId = string.Empty;
            _nextMapDirectoryRequest = 0f;
        }

        private bool BeginDirectorySync(
            PickerSession session,
            PortalEndpoint source,
            List<PickerCandidate> locallyKnown,
            bool locallyTruncated)
        {
            ZNet network = ZNet.instance;
            if (session == null || source == null || network == null || network.IsServer())
                return false;
            TickDirectorySyncTransport();
            ZNetPeer server = network.GetServerPeer();
            if (_directoryRegisteredRpc == null || server == null || !server.IsReady())
                return false;
            ZNetView sourceView = session.Source == null
                ? null
                : session.Source.GetComponent<ZNetView>();
            ZDO sourceZdo = sourceView != null && sourceView.IsValid()
                ? sourceView.GetZDO()
                : null;
            if (sourceZdo == null || !sourceZdo.IsValid() || sourceZdo.m_uid.IsNone())
                return false;

            session.DirectoryRequestId = Guid.NewGuid().ToString("N");
            session.DirectorySourceZdoId = sourceZdo.m_uid;
            session.DirectorySourceRevision = source.Revision;
            session.DirectoryDeadline = Time.realtimeSinceStartup + DirectoryRequestTimeoutSeconds;
            session.DirectoryNextAttempt = Time.realtimeSinceStartup;
            session.DirectoryAttempts = 0;
            session.DirectoryResponseReceived = false;
            session.DirectoryExpectedCount = 0;
            session.DirectoryFailure = string.Empty;
            session.DirectoryLocallyKnown = locallyKnown ?? new List<PickerCandidate>();
            session.DirectoryLocallyTruncated = locallyTruncated;
            SendDirectoryRequest(session, network, server);
            Message(session.Player, global::Runic.Localization.RunicText.Get("text_c716445bcf38"));
            return true;
        }

        private void SendDirectoryRequest(PickerSession session, ZNet network, ZNetPeer server)
        {
            if (session == null || network == null || server == null || !server.IsReady() ||
                _directoryRegisteredRpc == null ||
                session.DirectoryAttempts >= MaximumDirectoryAttempts) return;
            ZPackage package = WriteDirectoryRequest(
                session.DirectoryRequestId,
                session.DirectorySourceZdoId,
                session.SourcePortalId,
                session.DirectorySourceRevision,
                session.NetworkId);
            if (package.Size() > MaximumDirectoryEnvelopeBytes) return;
            _directoryRegisteredRpc.InvokeRoutedRPC(server.m_uid, DirectoryRequestRpc, package);
            session.DirectoryAttempts++;
            session.DirectoryNextAttempt = Time.realtimeSinceStartup + DirectoryRetrySeconds;
        }

        private void ReceiveDirectoryRequest(long sender, ZPackage package)
        {
            ZNet network = ZNet.instance;
            if (!_ready || network == null || !network.IsServer() || ZRoutedRpc.instance == null)
                return;
            byte[] envelope = null;
            try { envelope = package?.GetArray(); }
            catch { }
            if (!TryReadDirectoryRequest(
                    envelope == null ? package : new ZPackage(envelope),
                    out string requestId, out string sourceId,
                    out ZDOID sourceZdoId, out long sourceRevision, out string networkId))
            {
                if (envelope != null && TryReadLegacyDirectoryRequest(
                        new ZPackage(envelope), out string legacyRequestId))
                {
                    SentinelSecurityBridge.Report(
                        sender, "portal-directory-protocol-outdated", legacyRequestId, 2,
                        "The server rejected a valid legacy portal-directory protocol; the client must update Runic Portals.");
                    ZRoutedRpc.instance.InvokeRoutedRPC(
                        sender,
                        DirectoryResponseRpc,
                        WriteDirectoryResponse(
                            LegacyDirectoryWireSchema,
                            legacyRequestId,
                            false,
                            0,
                            "client-update-required"));
                    return;
                }
                SentinelSecurityBridge.Report(
                    sender, "portal-directory-envelope-invalid", "portal-directory-envelope", 3,
                    "The server rejected a malformed bounded portal-directory request.");
                return;
            }
            if (!TryResolvePeerPlayer(sender, out long playerId, out Vector3 playerPosition))
            {
                SentinelSecurityBridge.Report(
                    sender, "portal-directory-identity-unbound", requestId, 2,
                    "The directory request had no exact current transport-owned player.");
                return;
            }

            bool accepted = false;
            string reason = "source-unavailable";
            int sent = 0;
            _index.MarkDirty();
            ZDO sourceZdo = ZDOMan.instance?.GetZDO(sourceZdoId);
            if (sourceZdo == null || !sourceZdo.IsValid() || sourceZdo.m_uid != sourceZdoId)
                reason = "source-object-missing";
            else if (!string.Equals(sourceZdo.m_uid.ToString(), sourceId, StringComparison.Ordinal))
                reason = "source-id-mismatch";
            else if (!PortalZdoCodec.TryRead(
                         sourceZdo, out PortalEndpoint authoritativeSource, out _))
                reason = "source-record-invalid";
            else if ((sourceZdo.GetPosition() - playerPosition).sqrMagnitude >
                     MaximumDirectoryRequestDistanceMeters * MaximumDirectoryRequestDistanceMeters)
                reason = "source-too-far";
            else if (!ValheimContracts.SourceWardAllows(
                         sourceZdo.GetPosition(), playerId, out bool wardEvidencePending))
                reason = wardEvidencePending
                    ? "source-ward-unavailable"
                    : "source-ward-denied";
            else if (!_index.Rebuild(Time.realtimeSinceStartup,
                         PortalConfig.IndexRefreshSeconds?.Value ?? 2f))
                reason = "directory-index-unavailable";
            else if (!_graph.TryGetEndpoint(
                         authoritativeSource.PortalId, out PortalEndpoint source))
                reason = "source-not-indexed";
            else
            {
                // The exact nearby server ZDO is authoritative. Revision and network values in
                // the client request are freshness hints only; rejecting a harmless replication
                // race here made a portal fail on first use.
                if (source.Revision != sourceRevision ||
                    !string.Equals(source.NetworkId, networkId, StringComparison.Ordinal))
                    ZDOMan.instance.ForceSendZDO(sender, sourceZdo.m_uid);
                string traveler = PortalPermissionAdapter.Identity(playerId);
                PortalDirectoryResult directory = _graph.Query(new PortalDirectoryQuery(
                    traveler,
                    source.PortalId,
                    source.NetworkId,
                    string.Empty,
                    Math.Min(PortalContractLimits.MaximumDirectoryResults,
                        MaximumDirectoryEndpointsSent),
                    false));
                if (directory.StopCode == RouteStopCode.Ready)
                {
                    accepted = true;
                    reason = "ok";
                    for (int index = 0;
                         index < directory.Entries.Count && sent < MaximumDirectoryEndpointsSent;
                         index++)
                    {
                        PortalDirectoryEntry entry = directory.Entries[index];
                        if (!_graph.TryGetEndpoint(entry.PortalId, out PortalEndpoint destination) ||
                            !_index.TryGetZdo(entry.PortalId, out ZDO zdo) ||
                            !ValheimContracts.DestinationWardAllows(
                                ValheimContracts.ResolveWard(zdo.GetPosition(), playerId)) ||
                            !_permissions.Allows(
                                destination, traveler, PortalAccessAction.ViewDiscover) ||
                            !_permissions.Allows(
                                destination, traveler, PortalAccessAction.Arrive)) continue;
                        ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
                        sent++;
                    }
                    if (sent == 0) reason = "none-authorized";
                }
                else reason = "directory-" + directory.StopCode.ToString().ToLowerInvariant();
            }
            if (!accepted)
                Diagnostics.Trace("Portal directory request rejected: " + reason + ".");
            ZRoutedRpc.instance.InvokeRoutedRPC(
                sender,
                DirectoryResponseRpc,
                WriteDirectoryResponse(requestId, accepted, sent, reason));
        }

        private void ReceiveDirectoryResponse(long sender, ZPackage package)
        {
            ZNet network = ZNet.instance;
            PickerSession session = _mapPicker;
            if (!_ready || network == null || network.IsServer() || session == null ||
                !TryReadDirectoryResponse(package, out string requestId, out bool accepted,
                    out int count, out string reason) ||
                !string.Equals(requestId, session.DirectoryRequestId, StringComparison.Ordinal)) return;
            ZNetPeer server = network.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_uid != sender) return;
            float now = Time.realtimeSinceStartup;
            if (!accepted &&
                string.Equals(reason, "source-ward-unavailable", StringComparison.Ordinal) &&
                session.DirectoryAttempts < MaximumDirectoryAttempts &&
                now < session.DirectoryDeadline)
            {
                session.DirectoryResponseReceived = false;
                session.DirectoryExpectedCount = 0;
                session.DirectoryFailure = string.Empty;
                session.DirectoryNextAttempt = now + DirectoryRetrySeconds;
                return;
            }
            session.DirectoryResponseReceived = true;
            session.DirectoryExpectedCount = accepted ? Math.Max(0, count) : 0;
            session.DirectoryFailure = accepted ? string.Empty : reason;
            session.DirectoryNextAttempt = now;
        }

        private void TickDirectoryPicker(PickerSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.DirectoryRequestId)) return;
            float now = Time.realtimeSinceStartup;
            if (!session.DirectoryResponseReceived)
            {
                if (now >= session.DirectoryDeadline)
                {
                    CompleteOrCancelDirectoryFallback(session,
                        "Portal directory request timed out before the server responded.");
                    return;
                }
                if (now >= session.DirectoryNextAttempt)
                {
                    ZNet network = ZNet.instance;
                    ZNetPeer server = network?.GetServerPeer();
                    if (network != null && server != null)
                        SendDirectoryRequest(session, network, server);
                }
                return;
            }
            if (!string.IsNullOrEmpty(session.DirectoryFailure))
            {
                CompleteOrCancelDirectoryFallback(session,
                    "Portal directory is unavailable (" + session.DirectoryFailure + ").");
                return;
            }
            if (session.DirectoryExpectedCount == 0)
            {
                CompleteOrCancelDirectoryFallback(session,
                    "No authorized online arrival portals are available in network '" +
                    session.NetworkId + "'.");
                return;
            }
            if (now < session.DirectoryNextAttempt) return;
            session.DirectoryNextAttempt = now + DirectoryArrivalPollSeconds;
            string failure = "The source portal changed while its directory was loading.";
            if (!TryReadVisibleEndpoint(session.Source, out PortalEndpoint source) ||
                !TryBuildPickerCandidates(source, session.Player,
                    out List<PickerCandidate> candidates,
                    out bool truncated,
                    out failure))
            {
                CompleteOrCancelDirectoryFallback(session, failure);
                return;
            }
            if (candidates.Count < session.DirectoryExpectedCount &&
                now < session.DirectoryDeadline) return;
            if (candidates.Count == 0)
            {
                CompleteOrCancelDirectoryFallback(session,
                    "The authorized portal records did not arrive before the directory timed out.");
                return;
            }
            session.DirectoryRequestId = string.Empty;
            session.DirectoryLocallyKnown = null;
            CompletePicker(
                candidates,
                truncated || candidates.Count < session.DirectoryExpectedCount);
        }

        private void CompleteOrCancelDirectoryFallback(PickerSession session, string failure)
        {
            List<PickerCandidate> fallback = session?.DirectoryLocallyKnown;
            bool truncated = session?.DirectoryLocallyTruncated ?? false;
            if (session != null)
            {
                session.DirectoryRequestId = string.Empty;
                session.DirectoryLocallyKnown = null;
            }
            if (fallback != null && fallback.Count != 0)
                CompletePicker(fallback, truncated);
            else
                CancelMapPicker(failure, true);
        }

        /// <summary>
        /// Warms the ordinary large-map directory from the authoritative server. A Valheim client
        /// normally knows only portal ZDOs from zones it has already visited; without this request
        /// the P overlay can never be a world-wide portal map on a dedicated server.
        /// </summary>
        private void TickMapDirectorySync(string context, float realtime)
        {
            ZNet network = ZNet.instance;
            if (string.IsNullOrEmpty(context) || network == null || network.IsServer()) return;
            TickDirectorySyncTransport();
            ZNetPeer server = network.GetServerPeer();
            if (_directoryRegisteredRpc == null || server == null || !server.IsReady()) return;
            if (!string.Equals(_mapDirectoryContext, context, StringComparison.Ordinal))
            {
                _mapDirectoryContext = context;
                _mapDirectoryRequestId = string.Empty;
                _nextMapDirectoryRequest = 0f;
            }
            if (realtime < _nextMapDirectoryRequest) return;
            _nextMapDirectoryRequest = realtime + MapDirectoryRefreshSeconds;
            _mapDirectoryRequestId = Guid.NewGuid().ToString("N");
            ZPackage package = WriteMapDirectoryRequest(_mapDirectoryRequestId);
            if (package.Size() <= MaximumDirectoryEnvelopeBytes)
                _directoryRegisteredRpc.InvokeRoutedRPC(
                    server.m_uid, MapDirectoryRequestRpc, package);
        }

        private void ReceiveMapDirectoryRequest(long sender, ZPackage package)
        {
            ZNet network = ZNet.instance;
            if (!_ready || network == null || !network.IsServer() || ZRoutedRpc.instance == null)
                return;
            if (!TryReadMapDirectoryRequest(package, out string requestId))
            {
                SentinelSecurityBridge.Report(
                    sender, "portal-map-envelope-invalid", "portal-map-envelope", 3,
                    "The server rejected a malformed bounded portal-map request.");
                return;
            }
            if (!TryResolvePeerPlayer(sender, out long playerId, out _))
            {
                SentinelSecurityBridge.Report(
                    sender, "portal-map-identity-unbound", requestId, 2,
                    "The portal-map request had no exact current transport-owned player.");
                return;
            }

            bool accepted = false;
            bool truncated = false;
            string reason = "directory-index-unavailable";
            int sent = 0;
            _index.MarkDirty();
            if (_index.Rebuild(Time.realtimeSinceStartup,
                    PortalConfig.IndexRefreshSeconds?.Value ?? 2f))
            {
                List<ZDO> portals = ValheimContracts.PortalObjects();
                if (portals != null && portals.Count <= ValheimContracts.MaximumPortalObjectsScanned)
                {
                    accepted = true;
                    reason = "ok";
                    string traveler = PortalPermissionAdapter.Identity(playerId);
                    for (int index = 0; index < portals.Count; index++)
                    {
                        ZDO zdo = portals[index];
                        if (zdo == null || !zdo.IsValid() || zdo.m_uid.IsNone()) continue;
                        bool visible = PortalZdoCodec.GetMode(zdo) ==
                                       (int)PortalMode.StandardPair;
                        if (!visible && PortalZdoCodec.TryRead(
                                zdo, out PortalEndpoint endpoint, out _))
                        {
                            visible = endpoint.OnlineState == PortalOnlineState.Online &&
                                      ValheimContracts.DestinationWardAllows(
                                          ValheimContracts.ResolveWard(
                                              zdo.GetPosition(), playerId)) &&
                                      _permissions.Allows(
                                          endpoint,
                                          traveler,
                                          PortalAccessAction.ViewDiscover);
                        }
                        if (!visible) continue;
                        if (sent >= MaximumDirectoryEndpointsSent)
                        {
                            truncated = true;
                            break;
                        }
                        ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
                        sent++;
                    }
                }
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                sender,
                MapDirectoryResponseRpc,
                WriteMapDirectoryResponse(requestId, accepted, sent, truncated, reason));
        }

        private void ReceiveMapDirectoryResponse(long sender, ZPackage package)
        {
            ZNet network = ZNet.instance;
            if (!_ready || network == null || network.IsServer() ||
                !TryReadMapDirectoryResponse(
                    package, out string requestId, out bool accepted, out _, out bool truncated,
                    out string reason)) return;
            ZNetPeer server = network.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_uid != sender ||
                !string.Equals(
                    requestId, _mapDirectoryRequestId, StringComparison.Ordinal)) return;
            _mapDirectoryRequestId = string.Empty;
            if (!accepted)
                Diagnostics.Trace("Portal map directory request rejected: " + reason + ".");
            _nextMapOverlayRefresh = 0f;
            _mapOverlay?.SetRefreshState(false, truncated, !accepted);
        }

        private static ZPackage WriteMapDirectoryRequest(string requestId)
        {
            var package = new ZPackage();
            package.Write(MapDirectoryWireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(MapDirectoryTerminalMarker);
            return package;
        }

        private static bool TryReadMapDirectoryRequest(
            ZPackage package,
            out string requestId)
        {
            requestId = string.Empty;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != MapDirectoryWireSchema) return false;
                requestId = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
                       package.ReadInt() == MapDirectoryTerminalMarker &&
                       package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private static ZPackage WriteMapDirectoryResponse(
            string requestId,
            bool accepted,
            int count,
            bool truncated,
            string reason)
        {
            var package = new ZPackage();
            package.Write(MapDirectoryWireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(accepted);
            package.Write(Math.Max(0, Math.Min(MaximumDirectoryEndpointsSent, count)));
            package.Write(truncated);
            package.Write(BoundedDirectoryReason(reason));
            package.Write(MapDirectoryTerminalMarker);
            return package;
        }

        private static bool TryReadMapDirectoryResponse(
            ZPackage package,
            out string requestId,
            out bool accepted,
            out int count,
            out bool truncated,
            out string reason)
        {
            requestId = reason = string.Empty;
            accepted = truncated = false;
            count = 0;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != MapDirectoryWireSchema) return false;
                requestId = package.ReadString();
                accepted = package.ReadBool();
                count = package.ReadInt();
                truncated = package.ReadBool();
                reason = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
                       count >= 0 && count <= MaximumDirectoryEndpointsSent &&
                       reason.Length <= 64 &&
                       package.ReadInt() == MapDirectoryTerminalMarker &&
                       package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private static bool TryResolvePeerPlayer(
            long sender,
            out long playerId,
            out Vector3 position)
        {
            playerId = 0L;
            position = Vector3.zero;
            ZNet network = ZNet.instance;
            ZNetPeer peer = network?.GetPeer(sender);
            if (network == null || !network.IsServer() || peer == null || peer.m_uid != sender ||
                !peer.IsReady() || peer.m_characterID.IsNone() || ZDOMan.instance == null)
                return false;
            ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (character == null || !character.IsValid() || character.GetOwner() != sender ||
                ZDOMan.instance.GetZDO(peer.m_characterID) != character) return false;
            GameObject prefab = ZNetScene.instance?.GetPrefab(character.GetPrefab());
            playerId = character.GetLong(ZDOVars.s_playerID, 0L);
            position = character.GetPosition();
            return prefab != null && prefab.GetComponent<Player>() != null && playerId != 0L &&
                   !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z);
        }

        private static ZPackage WriteDirectoryRequest(
            string requestId,
            ZDOID sourceZdoId,
            string sourcePortalId,
            long sourceRevision,
            string networkId)
        {
            var package = new ZPackage();
            package.Write(DirectoryWireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(sourceZdoId);
            package.Write(sourcePortalId ?? string.Empty);
            package.Write(sourceRevision);
            package.Write(networkId ?? string.Empty);
            package.Write(DirectoryTerminalMarker);
            return package;
        }

        private static bool TryReadDirectoryRequest(
            ZPackage package,
            out string requestId,
            out string sourcePortalId,
            out ZDOID sourceZdoId,
            out long sourceRevision,
            out string networkId)
        {
            requestId = sourcePortalId = networkId = string.Empty;
            sourceZdoId = ZDOID.None;
            sourceRevision = -1L;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != DirectoryWireSchema) return false;
                requestId = package.ReadString();
                sourceZdoId = package.ReadZDOID();
                sourcePortalId = package.ReadString();
                sourceRevision = package.ReadLong();
                networkId = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
                       !sourceZdoId.IsNone() &&
                       sourcePortalId.Length > 0 &&
                       sourcePortalId.Length <= PortalContractLimits.MaximumPortalIdLength &&
                       sourceRevision >= 0L &&
                       networkId.Length > 0 &&
                       networkId.Length <= PortalContractLimits.MaximumNetworkIdLength &&
                       package.ReadInt() == DirectoryTerminalMarker &&
                       package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private static ZPackage WriteDirectoryResponse(
            string requestId,
            bool accepted,
            int count,
            string reason)
        {
            return WriteDirectoryResponse(
                DirectoryWireSchema, requestId, accepted, count, reason);
        }

        private static ZPackage WriteDirectoryResponse(
            int wireSchema,
            string requestId,
            bool accepted,
            int count,
            string reason)
        {
            var package = new ZPackage();
            package.Write(wireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(accepted);
            package.Write(Math.Max(0, Math.Min(MaximumDirectoryEndpointsSent, count)));
            package.Write(BoundedDirectoryReason(reason));
            package.Write(DirectoryTerminalMarker);
            return package;
        }

        private static bool TryReadLegacyDirectoryRequest(
            ZPackage package,
            out string requestId)
        {
            requestId = string.Empty;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != LegacyDirectoryWireSchema) return false;
                requestId = package.ReadString();
                string sourcePortalId = package.ReadString();
                long sourceRevision = package.ReadLong();
                string networkId = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
                       sourcePortalId.Length > 0 &&
                       sourcePortalId.Length <= PortalContractLimits.MaximumPortalIdLength &&
                       sourceRevision >= 0L &&
                       networkId.Length > 0 &&
                       networkId.Length <= PortalContractLimits.MaximumNetworkIdLength &&
                       package.ReadInt() == DirectoryTerminalMarker &&
                       package.GetPos() == package.Size();
            }
            catch
            {
                requestId = string.Empty;
                return false;
            }
        }

        private static bool TryReadDirectoryResponse(
            ZPackage package,
            out string requestId,
            out bool accepted,
            out int count,
            out string reason)
        {
            requestId = reason = string.Empty;
            accepted = false;
            count = 0;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != DirectoryWireSchema) return false;
                requestId = package.ReadString();
                accepted = package.ReadBool();
                count = package.ReadInt();
                reason = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
                       count >= 0 && count <= MaximumDirectoryEndpointsSent &&
                       reason.Length <= 64 &&
                       package.ReadInt() == DirectoryTerminalMarker &&
                       package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private static bool CanonicalDirectoryRequestId(string value) =>
            value != null && value.Length == 32 &&
            Guid.TryParseExact(value, "N", out Guid parsed) &&
            string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);

        private static string BoundedDirectoryReason(string value)
        {
            string safe = (value ?? string.Empty).Replace("\r", string.Empty)
                .Replace("\n", string.Empty).Trim();
            return safe.Length <= 64 ? safe : safe.Substring(0, 64);
        }
    }
}
