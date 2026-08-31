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
        private const int DirectoryWireSchema = 1;
        private const int DirectoryTerminalMarker = 0x50524431;
        private const int MaximumDirectoryEnvelopeBytes = 2048;
        private const int MaximumDirectoryEndpointsSent = 512;
        private const float MaximumDirectoryRequestDistanceMeters = 16f;
        private const float DirectoryRequestTimeoutSeconds = 6f;
        private const float DirectoryRetrySeconds = 1.5f;
        private const float DirectoryArrivalPollSeconds = 0.1f;

        private ZRoutedRpc _directoryRegisteredRpc;

        private void TickDirectorySyncTransport()
        {
            ZRoutedRpc routed = ZRoutedRpc.instance;
            if (routed == null || ReferenceEquals(_directoryRegisteredRpc, routed)) return;
            routed.Register<ZPackage>(DirectoryRequestRpc, ReceiveDirectoryRequest);
            routed.Register<ZPackage>(DirectoryResponseRpc, ReceiveDirectoryResponse);
            _directoryRegisteredRpc = routed;
        }

        private void ShutdownDirectorySyncTransport()
        {
            _directoryRegisteredRpc = null;
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

            session.DirectoryRequestId = Guid.NewGuid().ToString("N");
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
            Message(session.Player, "Loading authorized portals from the server...");
            return true;
        }

        private void SendDirectoryRequest(PickerSession session, ZNet network, ZNetPeer server)
        {
            if (session == null || network == null || server == null || !server.IsReady() ||
                _directoryRegisteredRpc == null || session.DirectoryAttempts >= 3) return;
            ZPackage package = WriteDirectoryRequest(
                session.DirectoryRequestId,
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
            if (!_ready || network == null || !network.IsServer() || ZRoutedRpc.instance == null ||
                !TryReadDirectoryRequest(package, out string requestId, out string sourceId,
                    out long sourceRevision, out string networkId) ||
                !TryResolvePeerPlayer(sender, out long playerId, out Vector3 playerPosition)) return;

            bool accepted = false;
            string reason = "source-unavailable";
            int sent = 0;
            _index.MarkDirty();
            if (_index.Rebuild(Time.realtimeSinceStartup,
                    PortalConfig.IndexRefreshSeconds?.Value ?? 2f) &&
                _graph.TryGetEndpoint(sourceId, out PortalEndpoint source) &&
                _index.TryGetZdo(sourceId, out ZDO sourceZdo) &&
                source.Revision == sourceRevision &&
                string.Equals(source.NetworkId, networkId, StringComparison.Ordinal) &&
                (sourceZdo.GetPosition() - playerPosition).sqrMagnitude <=
                    MaximumDirectoryRequestDistanceMeters * MaximumDirectoryRequestDistanceMeters &&
                ValheimContracts.WardAllows(
                    ValheimContracts.ResolveWard(sourceZdo.GetPosition(), playerId)))
            {
                string traveler = PortalPermissionAdapter.Identity(playerId);
                PortalDirectoryResult directory = _graph.Query(new PortalDirectoryQuery(
                    traveler,
                    sourceId,
                    networkId,
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
            session.DirectoryResponseReceived = true;
            session.DirectoryExpectedCount = accepted ? Math.Max(0, count) : 0;
            session.DirectoryFailure = accepted ? string.Empty : reason;
            session.DirectoryNextAttempt = Time.realtimeSinceStartup;
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
            return prefab != null && prefab.GetComponent<Player>() != null && playerId > 0L &&
                   !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z);
        }

        private static ZPackage WriteDirectoryRequest(
            string requestId,
            string sourcePortalId,
            long sourceRevision,
            string networkId)
        {
            var package = new ZPackage();
            package.Write(DirectoryWireSchema);
            package.Write(requestId ?? string.Empty);
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
            out long sourceRevision,
            out string networkId)
        {
            requestId = sourcePortalId = networkId = string.Empty;
            sourceRevision = -1L;
            try
            {
                if (package == null || package.Size() < 1 ||
                    package.Size() > MaximumDirectoryEnvelopeBytes ||
                    package.ReadInt() != DirectoryWireSchema) return false;
                requestId = package.ReadString();
                sourcePortalId = package.ReadString();
                sourceRevision = package.ReadLong();
                networkId = package.ReadString();
                return CanonicalDirectoryRequestId(requestId) &&
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
            var package = new ZPackage();
            package.Write(DirectoryWireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(accepted);
            package.Write(Math.Max(0, Math.Min(MaximumDirectoryEndpointsSent, count)));
            package.Write(BoundedDirectoryReason(reason));
            package.Write(DirectoryTerminalMarker);
            return package;
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
