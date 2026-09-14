using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelAdminControl : IDisposable
    {
        private const string RequestRpc = "runic.sentinel.admin.request.v1";
        private const string ResponseRpc = "runic.sentinel.admin.response.v1";
        private const int WireSchema = 1;
        private const int TerminalMarker = unchecked((int)0x51A73E19);
        private const int MaximumEnvelopeBytes = 180 * 1024;
#if !RUNIC_SENTINEL_SERVER_ONLY
        private const int MaximumPending = 16;
#endif
        private const int MaximumReplayEntries = 256;
        private const int MaximumTrackedPeers = 64;
#if !RUNIC_SENTINEL_SERVER_ONLY
        private static readonly long RequestLifetimeTicks = TimeSpan.FromSeconds(30).Ticks;
#endif
        private static readonly long ReplayLifetimeTicks = TimeSpan.FromMinutes(1).Ticks;

        private readonly SentinelRuntime _runtime;
        private readonly SentinelManagedPolicyService _managed;
        private readonly SentinelOperatorCommands _commands;
#if !RUNIC_SENTINEL_SERVER_ONLY
        private readonly Dictionary<string, Pending> _pending =
            new Dictionary<string, Pending>(StringComparer.Ordinal);
#endif
        private readonly Dictionary<string, CachedResponse> _cache =
            new Dictionary<string, CachedResponse>(StringComparer.Ordinal);
        private readonly Queue<string> _cacheOrder = new Queue<string>();
        private readonly Dictionary<ZRpc, ServerConnection> _serverConnections =
            new Dictionary<ZRpc, ServerConnection>();
        private ZNet _network;
#if !RUNIC_SENTINEL_SERVER_ONLY
        private ClientConnection _clientConnection;
#endif
        private long _nextConnectionOrdinal;
        private bool _disposed;

        internal SentinelAdminControl(
            SentinelRuntime runtime,
            SentinelManagedPolicyService managed,
            SentinelOperatorCommands commands)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _managed = managed ?? throw new ArgumentNullException(nameof(managed));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        internal void Tick()
        {
            if (_disposed) return;
            ZNet network = ZNet.instance;
            if (network == null)
            {
                if (_network != null)
                {
                    ClearConnections("The authoritative server administrator channel disconnected.");
                    _network = null;
                }
            }
            else
            {
                EnsureNetwork(network);
                if (network.IsServer()) TickServer(network);
#if RUNIC_SENTINEL_SERVER_ONLY
                else
                {
                    ClearConnections("Runic Sentinel Server is inert on a non-authoritative client.");
                    _network = null;
                }
#else
                else TickClient(network);
#endif
            }
            long now = DateTime.UtcNow.Ticks;
#if !RUNIC_SENTINEL_SERVER_ONLY
            foreach (string id in new List<string>(_pending.Keys))
                if (_pending.TryGetValue(id, out Pending pending) && pending.Expires <= now)
                {
                    _pending.Remove(id);
                    Fail(pending, "The server did not answer the administrator request in time.");
                }
#endif
            ExpireCache(now);
        }

        private void EnsureNetwork(ZNet network)
        {
            if (ReferenceEquals(network, _network)) return;
            ClearConnections("The authoritative server administrator channel changed.");
            _network = network;
        }

        private void TickServer(ZNet network)
        {
#if !RUNIC_SENTINEL_SERVER_ONLY
            ClearClientConnection("The authoritative server administrator channel changed.");
#endif
            List<ZNetPeer> peers;
            try { peers = network.GetPeers(); }
            catch { peers = null; }

            var ready = new Dictionary<ZRpc, ZNetPeer>();
            int inspected = 0;
            if (peers != null)
            {
                foreach (ZNetPeer peer in peers)
                {
                    if (inspected++ >= MaximumTrackedPeers) break;
                    ZRpc rpc = peer?.m_rpc;
                    if (rpc == null || !IsReady(peer) || ready.ContainsKey(rpc)) continue;
                    ready.Add(rpc, peer);
                }
            }

            foreach (ZRpc rpc in new List<ZRpc>(_serverConnections.Keys))
            {
                if (!ready.TryGetValue(rpc, out ZNetPeer peer) ||
                    !_serverConnections.TryGetValue(rpc, out ServerConnection connection) ||
                    !ReferenceEquals(connection.Peer, peer) ||
                    !ReferenceEquals(peer.m_rpc, rpc))
                    RemoveServerConnection(rpc);
            }

            foreach (KeyValuePair<ZRpc, ZNetPeer> item in ready)
            {
                if (_serverConnections.ContainsKey(item.Key) ||
                    _serverConnections.Count >= MaximumTrackedPeers) continue;
                try { item.Key.Register<ZPackage>(RequestRpc, ReceiveRequest); }
                catch { continue; }
                long ordinal = _nextConnectionOrdinal == long.MaxValue
                    ? long.MaxValue
                    : ++_nextConnectionOrdinal;
                _serverConnections.Add(
                    item.Key,
                    new ServerConnection(item.Value, item.Key, ordinal));
            }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void TickClient(ZNet network)
        {
            ClearServerConnections();
            ZNetPeer server;
            try { server = network.GetServerPeer(); }
            catch { server = null; }
            if (server?.m_rpc == null || !IsReady(server))
            {
                ClearClientConnection("The authoritative server administrator channel is unavailable.");
                return;
            }
            if (_clientConnection != null &&
                ReferenceEquals(_clientConnection.Peer, server) &&
                ReferenceEquals(_clientConnection.Rpc, server.m_rpc)) return;

            ClearClientConnection("The authoritative server administrator channel changed.");
            try { server.m_rpc.Register<ZPackage>(ResponseRpc, ReceiveResponse); }
            catch { return; }
            _clientConnection = new ClientConnection(server, server.m_rpc);
        }

        internal void RequestStatus(Action<bool, SentinelAdminDocument, string> callback)
        {
            if (TryExecuteLocal("status", Array.Empty<byte>(), out bool ok, out byte[] payload, out string reason))
            {
                callback?.Invoke(ok, DecodeDocument(payload), reason);
                return;
            }
            Submit("status", Array.Empty<byte>(), new Pending { Status = callback });
        }

        internal void Apply(SentinelAdminDocument document, Action<bool, string> callback)
        {
            byte[] bytes;
            try { bytes = SentinelAdminProtocol.Encode(document); }
            catch (Exception exception) { callback?.Invoke(false, exception.Message); return; }
            if (TryExecuteLocal("apply", bytes, out bool ok, out byte[] payload, out string reason))
            {
                callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(payload) : reason);
                return;
            }
            Submit("apply", bytes, new Pending { Result = callback });
        }

        internal void RunTool(string tool, Action<bool, string> callback)
        {
            byte[] bytes;
            try { bytes = SentinelAdminProtocol.EncodeTool(tool); }
            catch (Exception exception) { callback?.Invoke(false, exception.Message); return; }
            if (TryExecuteLocal("tool", bytes, out bool ok, out byte[] payload, out string reason))
            {
                callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(payload) : reason);
                return;
            }
            Submit("tool", bytes, new Pending { Result = callback });
        }

        internal void SaveCapacity(string revision, bool enabled, int players, Action<bool, string> callback)
        {
            byte[] bytes;
            try { bytes = SentinelCapacityProtocol.Encode(revision, enabled, players); }
            catch (Exception error) { callback?.Invoke(false, error.Message); return; }
            if (TryExecuteLocal("capacity", bytes, out bool ok, out byte[] response, out string reason))
            {
                callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(response) : reason);
                return;
            }
            Submit("capacity", bytes, new Pending { Result = callback });
        }

        private bool TryExecuteLocal(
            string action, byte[] payload, out bool accepted, out byte[] response, out string reason)
        {
            accepted = false;
            response = Array.Empty<byte>();
            reason = string.Empty;
            ZNet network = ZNet.instance;
            if (network == null || !network.IsServer()) return false;
            if (!SentinelTransportIdentity.TryResolveLocal(out string authority, out string subject))
            { reason = "The host backend identity is unavailable."; return true; }
            Execute(authority, subject, action, payload,
                SentinelServerAdministrator.IsAdministrator(network, null, authority, subject),
                out accepted, out response, out reason);
            return true;
        }

        private void Submit(string action, byte[] payload, Pending pending)
        {
            if (_disposed || pending == null) { Fail(pending, "Administrator control is unavailable."); return; }
            ZNet network = ZNet.instance;
            ClientConnection connection = _clientConnection;
            if (network == null || !ReferenceEquals(network, _network) || network.IsServer() ||
                !IsExactClientConnection(network, connection) || _pending.Count >= MaximumPending)
            { Fail(pending, "The authoritative server administrator channel is unavailable."); return; }
            string id = Guid.NewGuid().ToString("N");
            pending.Id = id;
            pending.Action = action;
            pending.Connection = connection.Rpc;
            bool setup = action == "tool" && SentinelAdminProtocol.TryDecodeTool(payload, out string requestedTool) &&
                         requestedTool == "bootstrap";
            pending.Expires = DateTime.UtcNow.Ticks + (setup ? TimeSpan.FromMinutes(2).Ticks : RequestLifetimeTicks);
            _pending.Add(id, pending);
            ZPackage package = WriteRequest(id, action, payload, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (package.Size() > MaximumEnvelopeBytes)
            { _pending.Remove(id); Fail(pending, "Administrator request exceeded its bounded size."); return; }
            try { connection.Rpc.Invoke(RequestRpc, package); }
            catch
            {
                _pending.Remove(id);
                Fail(pending, "The authoritative server administrator channel is unavailable.");
            }
        }
#endif

        private void ReceiveRequest(ZRpc rpc, ZPackage package)
        {
            ZNet network = ZNet.instance;
            if (_disposed || network == null || !ReferenceEquals(network, _network) ||
                !network.IsServer() || rpc == null ||
                !_serverConnections.TryGetValue(rpc, out ServerConnection connection)) return;
            ZNetPeer peer = FindExactReadyPeer(network, rpc);
            if (peer == null || !ReferenceEquals(peer, connection.Peer) ||
                !ReferenceEquals(peer.m_rpc, connection.Rpc)) return;
            if (!TryReadRequest(package, out string id, out string action, out byte[] payload, out long issued))
            { SendResponse(connection, string.Empty, false, "Malformed administrator request.", Array.Empty<byte>()); return; }
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (issued < nowUnix - 30L || issued > nowUnix + 30L)
            { SendResponse(connection, id, false, "Administrator request expired.", Array.Empty<byte>()); return; }
            string key = connection.Ordinal.ToString(CultureInfo.InvariantCulture) + ":" + id;
            byte[] digest = Digest(action, payload, issued);
            ExpireCache(DateTime.UtcNow.Ticks);
            if (!SentinelTransportIdentity.TryResolvePeer(peer, out string authority, out string subject, out string identityReason))
            { SendResponse(connection, id, false, "Authenticated backend identity is unavailable (" + identityReason + ").", Array.Empty<byte>()); return; }
            bool serverAdministrator = SentinelServerAdministrator.IsAdministrator(network, peer, authority, subject);
            if (!SentinelAdministratorRules.Allows(_runtime.IsAdministrator(authority, subject),
                    serverAdministrator, _runtime.IsBanned(authority, subject)))
            { SendResponse(connection, id, false, "Server administrator access required. Ask the owner to add your account ID to the server's adminlist.txt, or grant a signed Sentinel role.", Array.Empty<byte>()); return; }
            if (_cache.TryGetValue(key, out CachedResponse cached))
            {
                if (!Fixed(cached.RequestDigest, digest))
                    SendResponse(connection, id, false, "Administrator request identity was reused.", Array.Empty<byte>());
                else SendResponse(connection, id, cached.Accepted, cached.Reason, cached.Payload);
                return;
            }
            Execute(authority, subject, action, payload,
                serverAdministrator,
                out bool accepted, out byte[] response, out string reason);
            Cache(key, digest, accepted, reason, response);
            SendResponse(connection, id, accepted, reason, response);
        }

        private void Execute(
            string authority, string subject, string action, byte[] payload, bool serverAdministrator,
            out bool accepted, out byte[] response, out string reason)
        {
            accepted = false;
            response = Array.Empty<byte>();
            reason = "Server administrator access required. Ask the owner to add your account ID to the server's adminlist.txt, or grant a signed Sentinel role. Use full Sentinel for F3; SentinelClient only handles admission.";
            if (_runtime.IsBanned(authority, subject)) { reason = "This account is banned."; return; }
            if (!SentinelAdministratorRules.Allows(_runtime.IsAdministrator(authority, subject), serverAdministrator, false)) return;
            try
            {
                if (action == "status" && payload.Length == 0)
                {
                    SentinelAdminDocument status = _managed.CreateDocument(
                        "Authenticated by " + authority + " backend identity.");
                    SentinelCapacityBridge.Populate(status);
                    status.AdministratorSource = serverAdministrator ? "Server administrator / local host" : "Signed Sentinel role";
                    status.SetupAvailable = serverAdministrator && _managed.CanInitialize;
                    if (status.SetupAvailable)
                        status.Status = "Server administrator verified. Click Set Up Sentinel to create your server-owned signing key and administrator policy. Admission mode will not change.";
                    else if (!status.ManagedSigningKey)
                        status.Status = "Signing is not ready. Wait for the server snapshot and refresh. If a policy, key, or trust pin already exists, the server owner must repair or import that configuration; first-time setup will not replace it.";
                    response = SentinelAdminProtocol.Encode(status);
                }
                else if (action == "apply" && SentinelAdminProtocol.TryDecode(payload, out SentinelAdminDocument document))
                    response = SentinelAdminProtocol.EncodeMessage(_managed.Apply(document, authority, subject));
                else if (action == "capacity" && SentinelCapacityProtocol.TryDecode(payload, out string revision, out bool enabled, out int players))
                    response = SentinelAdminProtocol.EncodeMessage(SentinelCapacityBridge.Save(revision, enabled, players));
                else if (action == "tool" && SentinelAdminProtocol.TryDecodeTool(payload, out string tool))
                {
                    if (tool == "bootstrap")
                    {
                        // Never accept a caller-supplied identity or administrator flag.
                        if (!serverAdministrator) { reason = "First-time setup requires a server administrator or the local host."; return; }
                        response = SentinelAdminProtocol.EncodeMessage(
                            _managed.InitializeFromServerAdministrator(authority, subject));
                    }
                    else response = SentinelAdminProtocol.EncodeMessage(RunToolCore(tool));
                }
                else { reason = "Administrator operation is invalid."; return; }
                accepted = true;
                reason = "ok";
            }
            catch (Exception exception) { reason = Bounded(exception.Message); }
        }

        private string RunToolCore(string tool)
        {
            if (tool == "report") return "Support report created: " + _commands.WriteReport();
            if (tool == "networks") return "Network map created: " + _commands.WriteReport(true);
            if (tool == "backup") return "Verified backup created: " +
                SentinelTransitionBackup.CreateVerifiedBackupNow("runic-sentinel-admin-tool");
            throw new InvalidOperationException("Unknown administrator tool.");
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void ReceiveResponse(ZRpc rpc, ZPackage package)
        {
            ZNet network = ZNet.instance;
            ClientConnection connection = _clientConnection;
            if (_disposed || network == null || !ReferenceEquals(network, _network) ||
                network.IsServer() || !IsExactClientConnection(network, connection) ||
                !ReferenceEquals(connection.Rpc, rpc) ||
                !TryReadResponse(package, out string id, out bool accepted, out string reason, out byte[] payload) ||
                !_pending.TryGetValue(id, out Pending pending) ||
                !ReferenceEquals(pending.Connection, rpc)) return;
            _pending.Remove(id);
            if (pending.Status != null)
            {
                SentinelAdminDocument document = accepted ? DecodeDocument(payload) : null;
                pending.Status(accepted && document != null, document,
                    accepted && document == null ? "Server returned an invalid document." : reason);
            }
            else pending.Result?.Invoke(accepted,
                accepted ? SentinelAdminProtocol.DecodeMessage(payload) : reason);
        }

        private static SentinelAdminDocument DecodeDocument(byte[] payload) =>
            SentinelAdminProtocol.TryDecode(payload, out SentinelAdminDocument value) ? value : null;

        private static ZPackage WriteRequest(string id, string action, byte[] payload, long issued)
        {
            var package = new ZPackage();
            package.Write(WireSchema); package.Write(id); package.Write(action); package.Write(issued);
            package.Write(payload ?? Array.Empty<byte>()); package.Write(TerminalMarker);
            return package;
        }
#endif

        private static bool TryReadRequest(
            ZPackage package, out string id, out string action, out byte[] payload, out long issued)
        {
            id = action = string.Empty; payload = null; issued = 0L;
            try
            {
                if (package == null || package.Size() < 1 || package.Size() > MaximumEnvelopeBytes ||
                    package.ReadInt() != WireSchema) return false;
                id = package.ReadString(); action = package.ReadString(); issued = package.ReadLong();
                payload = package.ReadByteArray();
                return CanonicalId(id) && (action == "status" || action == "apply" || action == "tool" || action == "capacity") &&
                       payload != null && payload.Length <= SentinelAdminProtocol.MaximumWireBytes &&
                       package.ReadInt() == TerminalMarker && package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private void SendResponse(
            ServerConnection connection,
            string id,
            bool accepted,
            string reason,
            byte[] payload)
        {
            ZNet network = _network;
            if (_disposed || network == null || !network.IsServer() ||
                connection == null || !ReferenceEquals(
                    FindExactReadyPeer(network, connection.Rpc), connection.Peer)) return;
            ZPackage package = WriteResponse(id, accepted, reason, payload);
            if (package.Size() <= MaximumEnvelopeBytes)
            {
                try { connection.Rpc.Invoke(ResponseRpc, package); }
                catch { }
            }
        }

        private static ZPackage WriteResponse(string id, bool accepted, string reason, byte[] payload)
        {
            var package = new ZPackage();
            package.Write(WireSchema); package.Write(id ?? string.Empty); package.Write(accepted);
            package.Write(Bounded(reason)); package.Write(payload ?? Array.Empty<byte>()); package.Write(TerminalMarker);
            return package;
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private static bool TryReadResponse(
            ZPackage package, out string id, out bool accepted, out string reason, out byte[] payload)
        {
            id = reason = string.Empty; accepted = false; payload = null;
            try
            {
                if (package == null || package.Size() < 1 || package.Size() > MaximumEnvelopeBytes ||
                    package.ReadInt() != WireSchema) return false;
                id = package.ReadString(); accepted = package.ReadBool(); reason = package.ReadString();
                payload = package.ReadByteArray();
                return CanonicalId(id) && reason.Length <= 512 && payload != null &&
                       payload.Length <= SentinelAdminProtocol.MaximumWireBytes &&
                       package.ReadInt() == TerminalMarker && package.GetPos() == package.Size();
            }
            catch { return false; }
        }
#endif

        private static ZNetPeer FindExactReadyPeer(ZNet network, ZRpc rpc)
        {
            if (network == null || rpc == null || !network.IsServer()) return null;
            List<ZNetPeer> peers;
            try { peers = network.GetPeers(); }
            catch { return null; }
            if (peers == null) return null;
            int inspected = 0;
            foreach (ZNetPeer peer in peers)
            {
                if (inspected++ >= MaximumTrackedPeers) break;
                if (peer != null && ReferenceEquals(peer.m_rpc, rpc) && IsReady(peer)) return peer;
            }
            return null;
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private static bool IsExactClientConnection(ZNet network, ClientConnection connection)
        {
            if (network == null || connection == null || network.IsServer()) return false;
            ZNetPeer server;
            try { server = network.GetServerPeer(); }
            catch { return false; }
            return server != null && IsReady(server) &&
                   ReferenceEquals(server, connection.Peer) &&
                   ReferenceEquals(server.m_rpc, connection.Rpc);
        }
#endif

        private static bool IsReady(ZNetPeer peer)
        {
            try { return peer != null && peer.IsReady(); }
            catch { return false; }
        }

        private void RemoveServerConnection(ZRpc rpc)
        {
            if (rpc == null || !_serverConnections.Remove(rpc)) return;
            try { rpc.Unregister(RequestRpc); }
            catch { }
        }

        private void ClearServerConnections()
        {
            foreach (ZRpc rpc in new List<ZRpc>(_serverConnections.Keys))
            {
                try { rpc?.Unregister(RequestRpc); }
                catch { }
            }
            _serverConnections.Clear();
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void ClearClientConnection(string reason)
        {
            ClientConnection connection = _clientConnection;
            if (connection == null) return;
            _clientConnection = null;
            try { connection.Rpc?.Unregister(ResponseRpc); }
            catch { }
            FailPendingFor(connection.Rpc, reason);
        }
#endif

        private void ClearConnections(string reason)
        {
            ClearServerConnections();
#if !RUNIC_SENTINEL_SERVER_ONLY
            ClearClientConnection(reason);
#endif
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void FailPendingFor(ZRpc rpc, string reason)
        {
            if (rpc == null || _pending.Count == 0) return;
            var failed = new List<Pending>();
            foreach (string id in new List<string>(_pending.Keys))
            {
                if (!_pending.TryGetValue(id, out Pending pending) ||
                    !ReferenceEquals(pending.Connection, rpc)) continue;
                _pending.Remove(id);
                failed.Add(pending);
            }
            foreach (Pending pending in failed) Fail(pending, reason);
        }
#endif

        private void Cache(string key, byte[] digest, bool accepted, string reason, byte[] payload)
        {
            while (_cache.Count >= MaximumReplayEntries && _cacheOrder.Count != 0)
                _cache.Remove(_cacheOrder.Dequeue());
            _cache[key] = new CachedResponse
            {
                RequestDigest = digest, Accepted = accepted, Reason = Bounded(reason),
                Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone(),
                Expires = DateTime.UtcNow.Ticks + ReplayLifetimeTicks
            };
            _cacheOrder.Enqueue(key);
        }

        private void ExpireCache(long now)
        {
            while (_cacheOrder.Count != 0)
            {
                string key = _cacheOrder.Peek();
                if (_cache.TryGetValue(key, out CachedResponse value) && value.Expires > now) break;
                _cacheOrder.Dequeue(); _cache.Remove(key);
            }
        }

        private static byte[] Digest(string action, byte[] payload, long issued)
        {
            byte[] prefix = System.Text.Encoding.UTF8.GetBytes(action + "\n" + issued.ToString(CultureInfo.InvariantCulture) + "\n");
            byte[] all = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, all, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, all, prefix.Length, payload.Length);
            using SHA256 sha = SHA256.Create(); return sha.ComputeHash(all);
        }

        private static bool Fixed(byte[] left, byte[] right) =>
            left != null && right != null && left.Length == right.Length &&
            CryptographicOperations.FixedTimeEquals(left, right);
        private static bool CanonicalId(string id) => id != null && id.Length == 32 && Guid.TryParseExact(id, "N", out _);
        private static string Bounded(string reason) => string.IsNullOrWhiteSpace(reason)
            ? "Administrator operation failed." : reason.Length <= 512 ? reason : reason.Substring(0, 512);
#if !RUNIC_SENTINEL_SERVER_ONLY
        private static void Fail(Pending pending, string reason)
        { pending?.Status?.Invoke(false, null, reason); pending?.Result?.Invoke(false, reason); }
#endif

        public void Dispose()
        {
            _disposed = true;
            ClearConnections("Administrator control stopped.");
#if !RUNIC_SENTINEL_SERVER_ONLY
            foreach (Pending pending in _pending.Values) Fail(pending, "Administrator control stopped.");
            _pending.Clear(); _cache.Clear(); _cacheOrder.Clear(); _network = null;
#else
            _cache.Clear(); _cacheOrder.Clear(); _network = null;
#endif
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private sealed class Pending
        {
            internal string Id;
            internal string Action;
            internal ZRpc Connection;
            internal long Expires;
            internal Action<bool, SentinelAdminDocument, string> Status;
            internal Action<bool, string> Result;
        }
#endif

        private sealed class ServerConnection
        {
            internal ServerConnection(ZNetPeer peer, ZRpc rpc, long ordinal)
            {
                Peer = peer;
                Rpc = rpc;
                Ordinal = ordinal;
            }

            internal ZNetPeer Peer { get; }
            internal ZRpc Rpc { get; }
            internal long Ordinal { get; }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private sealed class ClientConnection
        {
            internal ClientConnection(ZNetPeer peer, ZRpc rpc)
            {
                Peer = peer;
                Rpc = rpc;
            }

            internal ZNetPeer Peer { get; }
            internal ZRpc Rpc { get; }
        }
#endif

        private sealed class CachedResponse
        {
            internal byte[] RequestDigest;
            internal bool Accepted;
            internal string Reason;
            internal byte[] Payload;
            internal long Expires;
        }
    }
}
