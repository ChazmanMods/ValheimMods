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
        private readonly SentinelPlayerCommandService _playerCommands=new SentinelPlayerCommandService();
#if !RUNIC_SENTINEL_SERVER_ONLY
        private readonly RunicSentinel.PlayerActions.PlayerActionReceiver _playerReceiver=new RunicSentinel.PlayerActions.PlayerActionReceiver();
#endif

        internal SentinelAdminControl(
            SentinelRuntime runtime,
            SentinelManagedPolicyService managed,
            SentinelOperatorCommands commands)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _managed = managed ?? throw new ArgumentNullException(nameof(managed));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            RunicSentinel.Devcommands.SentinelBridge.Control=this;
        }

        internal void Tick()
        {
            if (_disposed) return;
            ZNet network = ZNet.instance;
            _playerCommands.Tick(network);
#if !RUNIC_SENTINEL_SERVER_ONLY
            _playerReceiver.Tick();
#endif
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
                if (network.IsServer()) { TickServer(network); SentinelPlayerReports.Tick(network); }
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

        internal void ChangePerson(string action,string account,bool enabled,Action<bool,string> callback)
        {
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(action+"\n"+account+"\n"+(enabled?"1":"0"));
            if(bytes.Length>1024){callback(false,"Person action too large.");return;}
            if(TryExecuteLocal("person",bytes,out bool ok,out byte[] response,out string reason)){callback(ok,ok?SentinelAdminProtocol.DecodeMessage(response):reason);return;}
            Submit("person",bytes,new Pending{Result=callback});
        }
        internal void PlayerAction(string payload,bool result,Action<bool,string> callback)
        {
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(payload);
            if(bytes.Length>1024){callback(false,"Player action too large.");return;}
            string action=result?"player-action-result":"player-action";
            if(TryExecuteLocal(action,bytes,out bool ok,out byte[] response,out string reason)){callback(ok,ok?SentinelAdminProtocol.DecodeMessage(response):reason);return;}
            Submit(action,bytes,new Pending{Result=callback});
        }
        internal void DownloadReport(string file,int offset,Action<bool,string> callback)
        {
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(file+"\n"+offset.ToString(CultureInfo.InvariantCulture));
            if(bytes.Length>512){callback(false,"Invalid report filename.");return;}
            if(TryExecuteLocal("report-read",bytes,out bool ok,out byte[] response,out string reason)){callback(ok,ok?SentinelAdminProtocol.DecodeMessage(response):reason);return;}
            Submit("report-read",bytes,new Pending{Result=callback});
        }
        internal void RequestPlayers(Action<bool, string> callback)
        {
            if (TryExecuteLocal("players", Array.Empty<byte>(), out bool ok, out byte[] response, out string reason))
            { callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(response) : reason); return; }
            Submit("players", Array.Empty<byte>(), new Pending { Result = callback });
        }

        internal void RequestPlayerReport(string account, Action<bool, string> callback)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(account ?? "");
            if (bytes.Length > 512) { callback?.Invoke(false, "Invalid player identity."); return; }
            if (TryExecuteLocal("player-report", bytes, out bool ok, out byte[] response, out string reason))
            { callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(response) : reason); return; }
            Submit("player-report", bytes, new Pending { Result = callback });
        }

        internal void RequestPlayerDetail(string account, Action<bool, string> callback)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(account ?? "");
            if (bytes.Length == 0 || bytes.Length > 512) { callback?.Invoke(false, "Invalid player identity."); return; }
            if (TryExecuteLocal("player-detail", bytes, out bool ok, out byte[] response, out string reason))
            { callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(response) : reason); return; }
            Submit("player-detail", bytes, new Pending { Result = callback });
        }

        internal void AuthorizeCommand(string command, Action<bool, string> callback)
        {
            byte[] bytes;
            try { bytes = SentinelCommandRequest.Encode(command); }
            catch (Exception error) { callback?.Invoke(false, error.Message); return; }
            if (TryExecuteLocal("command", bytes, out bool ok, out byte[] response, out string reason))
            {
                callback?.Invoke(ok, ok ? SentinelAdminProtocol.DecodeMessage(response) : reason);
                return;
            }
            Submit("command", bytes, new Pending { Result = callback });
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
            { reason = global::Runic.Localization.RunicText.Get("text_fb030b0d298a"); return true; }
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

        internal bool AllowsCommandPeer(ZRpc rpc)
        {
            if(_disposed||ZNet.instance==null||!ZNet.instance.IsServer()||rpc==null)return false;
            var peer=FindExactReadyPeer(ZNet.instance,rpc);
            if(peer==null||!SentinelTransportIdentity.TryResolvePeer(peer,out string authority,out string subject,out _))return false;
            return SentinelAdministratorRules.Allows(_runtime.IsAdministrator(authority,subject),SentinelServerAdministrator.IsAdministrator(ZNet.instance,peer,authority,subject),_runtime.IsBanned(authority,subject));
        }

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
            reason = global::Runic.Localization.RunicText.Get("text_aa7e5e814e8c");
            if (_runtime.IsBanned(authority, subject)) { reason = global::Runic.Localization.RunicText.Get("text_1227b2f5325d"); return; }
            if (!SentinelAdministratorRules.Allows(_runtime.IsAdministrator(authority, subject), serverAdministrator, false)) return;
            try
            {
                if (action == "status" && payload.Length == 0)
                {
                    SentinelAdminDocument status = _managed.CreateDocument(
                        "Authenticated by " + authority + " backend identity.");
                    SentinelCapacityBridge.Populate(status);
                    // Include the first roster in the already authenticated status response.
                    // Subsequent updates use the smaller, read-only players request.
                    status.Players = SentinelPeople.List(_runtime,_managed);
                    status.ServerName=HarmonyLib.AccessTools.Field(typeof(ZNet),"m_ServerName")?.GetValue(null) as string??"";status.WorldName=ZNet.instance.GetWorldName();
                    status.OnlinePlayers=ZNet.instance.GetPeers().FindAll(p=>p.IsReady()&&!p.m_server).Count.ToString();
                    status.ServerUptime=TimeSpan.FromSeconds(UnityEngine.Time.realtimeSinceStartup).ToString(@"d\.hh\:mm\:ss");
                    var findings=_runtime.Evidence.ReadAfter(0L,256).Entries;
                    var evidenceText=new System.Text.StringBuilder();
                    for(int index=Math.Max(0,findings.Count-12);index<findings.Count;index++)
                    {
                        var finding=findings[index];string detail=finding.Detail??"";
                        if(detail.Length>320)detail=detail.Substring(0,320)+"…";
                        evidenceText.Append(DateTimeOffset.FromUnixTimeSeconds(finding.UnixSeconds).ToString("u")).Append(" | ").Append(finding.ProviderModuleId).Append(" | ").Append(finding.Rule).Append(" | ").Append(finding.Confidence).Append(" | ").Append(finding.EffectiveAction).AppendLine().AppendLine(detail).AppendLine();
                    }
                    status.RecentFindings=evidenceText.ToString();
                    status.AdministratorSource = serverAdministrator ? global::Runic.Localization.RunicText.Get("text_80c92c7139bc") : global::Runic.Localization.RunicText.Get("text_3ef5ba4be378");
                    status.SetupAvailable = serverAdministrator && _managed.CanInitialize;
                    if (status.SetupAvailable)
                        status.Status = global::Runic.Localization.RunicText.Get("text_e8335fddd41c");
                    else if (!status.ManagedSigningKey)
                        status.Status = global::Runic.Localization.RunicText.Get("text_625e0faa00e5");
                    try { response = SentinelAdminProtocol.Encode(status); }
                    catch (System.IO.InvalidDataException) when (!string.IsNullOrEmpty(status.Players))
                    { status.Players="";response=SentinelAdminProtocol.Encode(status); }
                }
                else if (action == "apply" && SentinelAdminProtocol.TryDecode(payload, out SentinelAdminDocument document))
                    response = SentinelAdminProtocol.EncodeMessage(_managed.Apply(document, authority, subject));
                else if (action == "command" && SentinelCommandRequest.TryDecode(payload, out string command))
                {
                    SentinelPlayerReports.Record(authority+":"+subject, "Admin command request", command);
                    response = SentinelAdminProtocol.EncodeMessage(SentinelCommandProvider.Authorize(command,authority+":"+subject));
                }
                else if (action == "players" && payload.Length == 0)
                    response = SentinelAdminProtocol.EncodeMessage(SentinelPeople.List(_runtime,_managed));
                else if (action == "person" && payload.Length > 0 && payload.Length<=1024)
                    response=SentinelAdminProtocol.EncodeMessage(SentinelPeople.Apply(new System.Text.UTF8Encoding(false,true).GetString(payload),authority,subject,_runtime,_managed));
                else if(action=="player-action"&&payload.Length>0&&payload.Length<=1024)
                    response=SentinelAdminProtocol.EncodeMessage(_playerCommands.Start(System.Text.Encoding.UTF8.GetString(payload),authority+":"+subject));
                else if(action=="player-action-result"&&payload.Length==32)
                    response=SentinelAdminProtocol.EncodeMessage(_playerCommands.Result(System.Text.Encoding.UTF8.GetString(payload),authority+":"+subject));
                else if(action=="report-read"&&payload.Length>0&&payload.Length<=512)
                    response=SentinelAdminProtocol.EncodeMessage(_commands.ReadReportChunk(new System.Text.UTF8Encoding(false,true).GetString(payload)));
                else if (action == "player-detail" && payload.Length > 0 && payload.Length <= 512)
                    response = SentinelAdminProtocol.EncodeMessage(SentinelPlayerReports.Detail(new System.Text.UTF8Encoding(false, true).GetString(payload), _runtime));
                else if (action == "player-report" && payload.Length <= 512)
                    response = SentinelAdminProtocol.EncodeMessage(SentinelPlayerReports.Create(new System.Text.UTF8Encoding(false, true).GetString(payload), _runtime));
                else if (action == "capacity" && SentinelCapacityProtocol.TryDecode(payload, out string revision, out bool enabled, out int players))
                    response = SentinelAdminProtocol.EncodeMessage(SentinelCapacityBridge.Save(revision, enabled, players));
                else if (action == "tool" && SentinelAdminProtocol.TryDecodeTool(payload, out string tool))
                {
                    if (tool == "bootstrap")
                    {
                        // Never accept a caller-supplied identity or administrator flag.
                        if (!serverAdministrator) { reason = global::Runic.Localization.RunicText.Get("text_b2e487da9365"); return; }
                        response = SentinelAdminProtocol.EncodeMessage(
                            _managed.InitializeFromServerAdministrator(authority, subject));
                    }
                    else response = SentinelAdminProtocol.EncodeMessage(RunToolCore(tool));
                }
                else { reason = global::Runic.Localization.RunicText.Get("text_19d7b1fe99f5"); return; }
                accepted = true;
                reason = "ok";
            }
            catch (Exception exception) { reason = Bounded(exception.Message); }
        }

        private string RunToolCore(string tool)
        {
            if (tool == "report"||tool=="networks")
            {
                string path=_commands.WriteReport(tool=="networks");
                return SentinelJson.Write(new SentinelToolReport{path=path,file=System.IO.Path.GetFileName(path),title=tool=="networks"?"Production & portal networks":"Server health & support"});
            }
            if (tool == "backup") return global::Runic.Localization.RunicText.Get("text_a5a9bf91e5d5") +
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
                return CanonicalId(id) && (action == "status" || action == "apply" || action == "tool" || action == "capacity" || action == "command" || action == "player-report" || action == "players" || action == "player-detail" || action == "person" || action=="report-read" || action=="player-action" || action=="player-action-result") &&
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
            ? global::Runic.Localization.RunicText.Get("text_b4b29205a24e") : reason.Length <= 512 ? reason : reason.Substring(0, 512);
#if !RUNIC_SENTINEL_SERVER_ONLY
        private static void Fail(Pending pending, string reason)
        { pending?.Status?.Invoke(false, null, reason); pending?.Result?.Invoke(false, reason); }
#endif

        public void Dispose()
        {
            _playerCommands.Dispose();
#if !RUNIC_SENTINEL_SERVER_ONLY
            _playerReceiver.Dispose();
#endif
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
