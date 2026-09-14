using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RunicSentinel.Admission;
using RunicSentinel.Contracts;
using RunicSentinel.Runtime;

namespace RunicSentinel.Core
{
    internal enum SentinelRemoteAdmissionMode
    {
        Disabled = 0,
        Optional = 1,
        Required = 2
    }

    internal readonly struct SentinelAdmissionCheck
    {
        internal SentinelAdmissionCheck(bool compatible, string reason)
        {
            Compatible = compatible;
            Reason = reason ?? string.Empty;
        }

        internal bool Compatible { get; }
        internal string Reason { get; }
    }

    /// <summary>
    /// Server-gated, direct-connection admission. The remote inventory remains self-reported
    /// compatibility evidence, but its transport, challenge, bounds, digest, and policy decision
    /// are all owned by the authoritative server.
    /// </summary>
    internal sealed class SentinelNetworkCompatibility : IDisposable
    {
        internal const int MaximumTrackedConnections = 64;
        internal const int MaximumChallengeAttempts = 3;
#if !RUNIC_SENTINEL_SERVER_ONLY
        internal const int MaximumReportAttempts = 3;
        internal const int MaximumResumeAttempts = 3;
#endif
        internal const int MaximumOuterPackageBytes = AdmissionProtocolV2.MaximumFrameBytes + 8;
        internal const long AdmissionGraceSeconds = 20L;
        internal const long ResumeGraceSeconds = 10L;
        internal const long PeerInfoGraceSeconds = 120L;

        private static readonly long RetryTicks = DurationTicks(2L);
        private static readonly long DecisionRetryTicks = DurationTicks(1L);
        private static readonly long DisconnectGraceTicks = DurationTicks(1L);
        private const string EvidenceProviderId = "runic.sentinel.network";

        private static readonly object ActiveLock = new object();
        private static readonly FieldInfo RpcFunctionsField = typeof(ZRpc).GetField(
            "m_functions", BindingFlags.Instance | BindingFlags.NonPublic);
#if !RUNIC_SENTINEL_SERVER_ONLY
        private static readonly FieldInfo InviteSecretKeyField = typeof(ZNet).GetField(
            "m_inviteSecretKey", BindingFlags.Static | BindingFlags.NonPublic);
#endif
        private static SentinelNetworkCompatibility _active;

        private readonly object _gate = new object();
        private readonly SentinelRuntime _runtime;
        private readonly SentinelRemoteAdmissionMode _mode;
        private readonly ISentinelEvidenceProviderLease _evidence;
        private readonly Dictionary<ZRpc, ServerConnection> _serverConnections =
            new Dictionary<ZRpc, ServerConnection>();

        private ZNet _network;
#if !RUNIC_SENTINEL_SERVER_ONLY
        private ClientConnection _clientConnection;
        private ZNetPeer _blockedClientPeer;
        private ZRpc _blockedClientRpc;
        private ISocket _blockedClientSocket;
#endif
        private long _nextConnectionOrdinal;
        private bool _disposed;

        internal SentinelNetworkCompatibility(
            SentinelRuntime runtime,
            EvidenceLedger evidence,
            SentinelRemoteAdmissionMode mode)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (mode < SentinelRemoteAdmissionMode.Disabled ||
                mode > SentinelRemoteAdmissionMode.Required)
                throw new ArgumentOutOfRangeException(nameof(mode));
            ValidatePatchSeams();
            _mode = mode;
            _evidence = (evidence ?? throw new ArgumentNullException(nameof(evidence)))
                .RegisterProvider(EvidenceProviderId);
            lock (ActiveLock)
            {
                if (_active != null && !_active._disposed)
                    throw new InvalidOperationException("Sentinel admission transport is already active.");
                _active = this;
            }
        }

        internal bool IsActive
        {
            get
            {
                lock (_gate)
                    return !_disposed && _mode != SentinelRemoteAdmissionMode.Disabled &&
                           _network != null;
            }
        }

        internal SentinelRemoteAdmissionMode Mode
        {
            get { lock (_gate) return _mode; }
        }

        internal void Tick()
        {
            lock (_gate)
            {
                if (_disposed || _mode == SentinelRemoteAdmissionMode.Disabled) return;
                ZNet network = ZNet.instance;
                if (network == null) return;
                EnsureNetworkLocked(network);
                long nowTicks = MonotonicTicks();
                if (network.IsServer()) TickServerLocked(network, nowTicks);
#if RUNIC_SENTINEL_SERVER_ONLY
                else
                {
                    ClearConnectionsLocked();
                    _network = null;
                }
#else
                else TickClientLocked(network, nowTicks);
#endif
            }
        }

        internal static SentinelAdmissionCheck Evaluate(
            SentinelRuntime runtime,
            SentinelPolicy policy,
            AdmissionClientProfile profile,
            string role)
        {
            if (runtime == null || policy == null)
                return Fail("sentinel-server-policy-unavailable");
            if (profile == null)
                return Fail("sentinel-client-profile-invalid");
            AdmissionDecision decision;
            try
            {
                decision = runtime.EvaluateAdmissionClientProfile(
                    policy,
                    profile,
                    role == "administrator" ? "administrator" : "player");
            }
            catch
            {
                return Fail("sentinel-client-profile-invalid");
            }
            if (decision == null || decision.Disposition != AdmissionDisposition.Allow)
                return Fail(ReasonFor(decision));
            return new SentinelAdmissionCheck(true, "compatible");
        }

        internal static bool DisconnectsForFailure(SentinelRemoteAdmissionMode mode) =>
            mode == SentinelRemoteAdmissionMode.Required;

        internal static bool AllowsPeerInfo(
            SentinelRemoteAdmissionMode mode,
            bool exactConnection,
            bool compliant,
            bool nativeHandshakeReleased,
            bool denied) =>
            mode != SentinelRemoteAdmissionMode.Required ||
            exactConnection && compliant && nativeHandshakeReleased && !denied;

        internal static void ObserveConnection(ZNet network, ZNetPeer peer)
        {
            SentinelNetworkCompatibility active = Current();
            active?.ObserveConnectionCore(network, peer);
        }

        internal static bool BeforeServerHandshake(
            ZNet network,
            ZRpc rpc,
            string secretKey,
            out bool approvedResume)
        {
            approvedResume = false;
            SentinelNetworkCompatibility active = Current();
            return active == null || active.BeforeServerHandshakeCore(
                network, rpc, secretKey, out approvedResume);
        }

        internal static void AfterServerHandshake(ZNet network, ZRpc rpc, bool approvedResume)
        {
            if (!approvedResume) return;
            Current()?.AfterServerHandshakeCore(network, rpc);
        }

        internal static void ServerHandshakeFailed(ZNet network, ZRpc rpc, bool approvedResume)
        {
            if (!approvedResume) return;
            Current()?.ServerHandshakeFailedCore(network, rpc);
        }

        internal static bool BeforePeerInfo(
            ZNet network,
            ZRpc rpc,
            out bool approvedAdmission)
        {
            approvedAdmission = false;
            SentinelNetworkCompatibility active = Current();
            return active == null || active.BeforePeerInfoCore(
                network, rpc, out approvedAdmission);
        }

        internal static void AfterPeerInfo(ZNet network, ZRpc rpc, bool approvedAdmission)
        {
            if (!approvedAdmission) return;
            Current()?.AfterPeerInfoCore(network, rpc);
        }

        internal static void PeerInfoFailed(ZNet network, ZRpc rpc, bool approvedAdmission)
        {
            if (!approvedAdmission) return;
            Current()?.PeerInfoFailedCore(network, rpc);
        }

        internal static void ForgetConnection(ZNet network, ZNetPeer peer)
        {
            Current()?.ForgetConnectionCore(network, peer);
        }

        internal static void ForgetNetwork(ZNet network)
        {
            Current()?.ForgetNetworkCore(network);
        }

        public void Dispose()
        {
            lock (ActiveLock)
            {
                if (ReferenceEquals(_active, this)) _active = null;
            }
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                ClearConnectionsLocked();
                _network = null;
            }
            try { _evidence?.Dispose(); }
            catch { }
        }

        private static SentinelNetworkCompatibility Current()
        {
            lock (ActiveLock)
                return _active != null && !_active._disposed ? _active : null;
        }

        private void ObserveConnectionCore(ZNet network, ZNetPeer peer)
        {
            if (network == null || peer?.m_rpc == null) return;
            lock (_gate)
            {
                if (_disposed || _mode == SentinelRemoteAdmissionMode.Disabled) return;
                EnsureNetworkLocked(network);
                if (network.IsServer()) AddServerConnectionLocked(peer);
#if !RUNIC_SENTINEL_SERVER_ONLY
                else RegisterClientConnectionLocked(peer);
#endif
            }
        }

        private bool BeforeServerHandshakeCore(
            ZNet network,
            ZRpc rpc,
            string secretKey,
            out bool approvedResume)
        {
            approvedResume = false;
            if (network == null || rpc == null || _mode == SentinelRemoteAdmissionMode.Disabled)
                return true;
            lock (_gate)
            {
                if (_disposed || !network.IsServer()) return true;
                EnsureNetworkLocked(network);
                if (!_serverConnections.TryGetValue(rpc, out ServerConnection state))
                {
                    ZNetPeer peer = FindExactPeer(network, rpc);
                    state = AddServerConnectionLocked(peer);
                }
                if (state == null || !state.IsExact())
                {
                    if (_mode == SentinelRemoteAdmissionMode.Required && state != null)
                        DenyLocked(state, "sentinel-peer-binding-invalid", MonotonicTicks());
                    return _mode != SentinelRemoteAdmissionMode.Required;
                }

                long nowTicks = MonotonicTicks();
                if (_mode == SentinelRemoteAdmissionMode.Optional)
                {
                    state.NativeReleased = true;
                    BeginAdmissionLocked(state, nowTicks);
                    return true;
                }
                string boundedSecret = secretKey ?? string.Empty;
                if (boundedSecret.Length > 1024)
                {
                    FailAdmissionLocked(state, "sentinel-native-handshake-secret-invalid", nowTicks);
                    return false;
                }
                if (!state.NativeHandshakeObserved)
                {
                    state.NativeHandshakeObserved = true;
                    state.NativeHandshakeSecret = boundedSecret;
                    BeginAdmissionLocked(state, nowTicks);
                    return false;
                }
                if (state.ApprovedAwaitingResume)
                {
                    if (!string.Equals(
                            state.NativeHandshakeSecret, boundedSecret, StringComparison.Ordinal))
                    {
                        FailAdmissionLocked(state, "sentinel-native-handshake-secret-changed", nowTicks);
                        return false;
                    }
                    if (!PolicyIsCurrentLocked(state))
                    {
                        FailAdmissionLocked(state, "sentinel-policy-changed", nowTicks);
                        return false;
                    }
                    state.ApprovedAwaitingResume = false;
                    state.ReleaseInProgress = true;
                    approvedResume = true;
                    return true;
                }
                if (state.NativeReleased || state.ReleaseInProgress || state.Denied)
                    return false;
                BeginAdmissionLocked(state, nowTicks);
                return false;
            }
        }

        private void AfterServerHandshakeCore(ZNet network, ZRpc rpc)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network) || rpc == null ||
                    !_serverConnections.TryGetValue(rpc, out ServerConnection state)) return;
                state.ReleaseInProgress = false;
                if (!PolicyIsCurrentLocked(state))
                {
                    FailAdmissionLocked(
                        state, "sentinel-policy-changed", MonotonicTicks());
                    return;
                }
                state.NativeReleased = true;
                state.PeerInfoDeadlineTicks =
                    MonotonicTicks() + DurationTicks(PeerInfoGraceSeconds);
            }
        }

        private void ServerHandshakeFailedCore(ZNet network, ZRpc rpc)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network) || rpc == null ||
                    !_serverConnections.TryGetValue(rpc, out ServerConnection state)) return;
                state.ReleaseInProgress = false;
                FailAdmissionLocked(
                    state, "sentinel-native-handshake-failed", MonotonicTicks());
            }
        }

        private bool BeforePeerInfoCore(
            ZNet network,
            ZRpc rpc,
            out bool approvedAdmission)
        {
            approvedAdmission = false;
            if (network == null || rpc == null || _mode == SentinelRemoteAdmissionMode.Disabled)
                return true;
            lock (_gate)
            {
                if (_disposed || !network.IsServer()) return true;
                EnsureNetworkLocked(network);
                if (!_serverConnections.TryGetValue(rpc, out ServerConnection state))
                {
                    ZNetPeer peer = FindExactPeer(network, rpc);
                    state = AddServerConnectionLocked(peer);
                }

                if (_mode == SentinelRemoteAdmissionMode.Optional)
                {
                    if (state != null && state.IsExact())
                    {
                        state.NativeReleased = true;
                        BeginAdmissionLocked(state, MonotonicTicks());
                    }
                    return true;
                }

                bool structurallyAllowed = AllowsPeerInfo(
                        _mode,
                        state != null && state.IsExact(),
                        state?.Compliant ?? false,
                        state?.NativeReleased ?? false,
                        state?.Denied ?? true);
                if (structurallyAllowed && !PolicyIsCurrentLocked(state))
                {
                    FailAdmissionLocked(
                        state, "sentinel-policy-changed", MonotonicTicks());
                    return false;
                }
                if (structurallyAllowed)
                {
                    approvedAdmission = true;
                    return true;
                }

                if (state != null && state.IsExact() && !state.Denied)
                {
                    long nowTicks = MonotonicTicks();
                    BeginAdmissionLocked(state, nowTicks);
                    FailAdmissionLocked(state, "sentinel-peer-info-before-admission", nowTicks);
                }
                return false;
            }
        }

        private void AfterPeerInfoCore(ZNet network, ZRpc rpc)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network) || rpc == null ||
                    !_serverConnections.TryGetValue(rpc, out ServerConnection state) ||
                    !state.IsExact()) return;
                if (!PolicyIsCurrentLocked(state))
                {
                    FailAdmissionLocked(
                        state, "sentinel-policy-changed", MonotonicTicks());
                    return;
                }
                bool ready;
                try { ready = state.Peer.IsReady(); }
                catch { ready = false; }
                if (!ready)
                {
                    FailAdmissionLocked(
                        state, "sentinel-native-peer-info-incomplete", MonotonicTicks());
                    return;
                }
                state.PeerInfoReleased = true;
                state.PeerInfoDeadlineTicks = 0L;
            }
        }

        private void PeerInfoFailedCore(ZNet network, ZRpc rpc)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network) || rpc == null ||
                    !_serverConnections.TryGetValue(rpc, out ServerConnection state)) return;
                FailAdmissionLocked(
                    state, "sentinel-native-peer-info-failed", MonotonicTicks());
            }
        }

        private void ForgetConnectionCore(ZNet network, ZNetPeer peer)
        {
            if (peer?.m_rpc == null) return;
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network)) return;
                RemoveConnectionLocked(peer.m_rpc);
            }
        }

        private void ForgetNetworkCore(ZNet network)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(network, _network)) return;
                ClearConnectionsLocked();
                _network = null;
            }
        }

        private void EnsureNetworkLocked(ZNet network)
        {
            if (ReferenceEquals(_network, network)) return;
            ClearConnectionsLocked();
            _network = network;
        }

        private void TickServerLocked(ZNet network, long nowTicks)
        {
            List<ZNetPeer> peers;
            try { peers = network.GetPeers(); }
            catch { return; }
            int inspected = 0;
            if (peers != null)
            {
                foreach (ZNetPeer peer in peers)
                {
                    if (inspected++ >= MaximumTrackedConnections) break;
                    if (peer?.m_rpc != null) AddServerConnectionLocked(peer);
                }
            }

            var states = new List<ServerConnection>(_serverConnections.Values);
            foreach (ServerConnection state in states)
            {
                if (!state.IsExact() || !SocketConnected(state.Peer))
                {
                    RemoveConnectionLocked(state.Rpc);
                    continue;
                }
                if (state.Denied)
                {
                    if (state.DisconnectAtTicks != 0L && nowTicks >= state.DisconnectAtTicks)
                    {
                        ZNetPeer exact = state.Peer;
                        RemoveConnectionLocked(state.Rpc);
                        try { network.Disconnect(exact); }
                        catch { }
                    }
                    continue;
                }
                if (state.PeerInfoReleased && state.Compliant) continue;
                if (state.Compliant)
                {
                    if (!PolicyIsCurrentLocked(state))
                    {
                        FailAdmissionLocked(state, "sentinel-policy-changed", nowTicks);
                        continue;
                    }
                    if (!state.NativeReleased && state.ResumeDeadlineTicks != 0L &&
                        nowTicks >= state.ResumeDeadlineTicks)
                    {
                        FailAdmissionLocked(
                            state, "sentinel-native-handshake-resume-timeout", nowTicks);
                        continue;
                    }
                    if (state.NativeReleased && !state.PeerInfoReleased &&
                        state.PeerInfoDeadlineTicks != 0L &&
                        nowTicks >= state.PeerInfoDeadlineTicks)
                    {
                        FailAdmissionLocked(state, "sentinel-peer-info-timeout", nowTicks);
                        continue;
                    }
                    if (!state.NativeReleased && state.ApprovedAwaitingResume &&
                        state.DecisionAttempts < MaximumChallengeAttempts &&
                        nowTicks >= state.NextDecisionTicks)
                        SendDecisionLocked(state, true, true, "compatible", nowTicks);
                    continue;
                }
                if (state.StartedTicks == 0L) continue;
                if (nowTicks >= state.DeadlineTicks)
                {
                    string reason = state.Challenge == null
                        ? "sentinel-server-policy-unavailable"
                        : "sentinel-client-absent";
                    FailAdmissionLocked(state, reason, nowTicks);
                    continue;
                }
                EnsureChallengeLocked(state, nowTicks);
                if (state.Challenge != null && !state.Compliant &&
                    state.ChallengeAttempts < MaximumChallengeAttempts &&
                    nowTicks >= state.NextChallengeTicks)
                    SendChallengeLocked(state, nowTicks);
            }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void TickClientLocked(ZNet network, long nowTicks)
        {
            ZNetPeer server = null;
            ClientConnection retained = _clientConnection;
            if (retained != null && retained.IsExact(retained.Rpc) &&
                ReferenceEquals(FindExactPeer(network, retained.Rpc), retained.Peer))
                server = retained.Peer;
            if (server == null)
            {
                try { server = network.GetServerPeer(); }
                catch { server = null; }
            }
            if (server?.m_rpc == null)
            {
                ClearClientLocked();
                return;
            }
            RegisterClientConnectionLocked(server);
            ClientConnection state = _clientConnection;
            if (state?.Challenge == null || state.DecisionReceived ||
                state.ReportAttempts >= MaximumReportAttempts ||
                nowTicks < state.NextReportTicks || !SocketConnected(state.Peer)) return;
            SendClientReportLocked(state, nowTicks);
        }
#endif

        private ServerConnection AddServerConnectionLocked(ZNetPeer peer)
        {
            ZRpc rpc = peer?.m_rpc;
            if (rpc == null) return null;
            if (_serverConnections.TryGetValue(rpc, out ServerConnection existing))
                return existing.IsExact() ? existing : null;
            if (_serverConnections.Count >= MaximumTrackedConnections)
            {
                if (_mode == SentinelRemoteAdmissionMode.Required)
                {
                    try { _network?.Disconnect(peer); }
                    catch { }
                }
                return null;
            }
            long ordinal = _nextConnectionOrdinal == long.MaxValue
                ? long.MaxValue
                : ++_nextConnectionOrdinal;
            var state = new ServerConnection(peer, rpc, ordinal);
            _serverConnections.Add(rpc, state);
            if (!TryRegisterDirectHandler(rpc, out object registered, out string failure))
            {
                if (_mode == SentinelRemoteAdmissionMode.Required)
                    FailAdmissionLocked(state, failure, MonotonicTicks());
                else
                    _runtime.RecordAdmissionFailure(failure);
                return state;
            }
            state.RegisteredHandler = registered;
            if (_mode == SentinelRemoteAdmissionMode.Required)
                BeginAdmissionLocked(state, MonotonicTicks());
            return state;
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void RegisterClientConnectionLocked(ZNetPeer server)
        {
            if (server?.m_rpc == null) return;
            if (_clientConnection != null &&
                ReferenceEquals(_clientConnection.Peer, server) &&
                ReferenceEquals(_clientConnection.Rpc, server.m_rpc)) return;
            if (ReferenceEquals(_blockedClientPeer, server) &&
                ReferenceEquals(_blockedClientRpc, server.m_rpc) &&
                ReferenceEquals(_blockedClientSocket, server.m_socket)) return;
            ClearClientLocked();
            if (!TryRegisterDirectHandler(
                    server.m_rpc, out object registered, out string failure))
            {
                _blockedClientPeer = server;
                _blockedClientRpc = server.m_rpc;
                _blockedClientSocket = server.m_socket;
                if (!string.Equals(
                        failure, "sentinel-rpc-name-collision", StringComparison.Ordinal))
                    _runtime.RecordAdmissionFailure(failure);
                return;
            }
            if (!TryReadInviteSecretKey(out string inviteSecretKey))
            {
                UnregisterOwnedHandler(server.m_rpc, registered);
                _blockedClientPeer = server;
                _blockedClientRpc = server.m_rpc;
                _blockedClientSocket = server.m_socket;
                _runtime.RecordAdmissionFailure("sentinel-invite-secret-unavailable");
                return;
            }
            _clientConnection = new ClientConnection(
                server, server.m_rpc, registered, inviteSecretKey);
        }
#endif

        private void BeginAdmissionLocked(ServerConnection state, long nowTicks)
        {
            if (state.StartedTicks != 0L || state.RegisteredHandler == null) return;
            state.StartedTicks = nowTicks;
            state.DeadlineTicks = nowTicks + DurationTicks(AdmissionGraceSeconds);
            EnsureChallengeLocked(state, nowTicks);
            if (state.Challenge != null) SendChallengeLocked(state, nowTicks);
        }

        private void EnsureChallengeLocked(ServerConnection state, long nowTicks)
        {
            if (state.Challenge != null || state.Denied) return;
            SentinelPolicy policy;
            try
            {
                if (!_runtime.TryGetVerifiedPolicy(out policy) || policy == null) return;
            }
            catch { return; }
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long remaining = Math.Max(
                1L,
                Math.Min(
                    AdmissionProtocolV2.MaximumChallengeLifetimeSeconds,
                    (state.DeadlineTicks - nowTicks) / Stopwatch.Frequency));
            state.Policy = policy;
            state.Challenge = AdmissionProtocolV2.CreateChallenge(nowUnix, remaining);
            state.NextChallengeTicks = nowTicks;
            state.ChallengeAttempts = 0;
        }

        private void SendChallengeLocked(ServerConnection state, long nowTicks)
        {
            if (state.Challenge == null || !state.IsExact()) return;
            try
            {
                InvokeFrame(state.Rpc, AdmissionProtocolV2.EncodeChallenge(state.Challenge));
                state.ChallengeAttempts++;
                state.NextChallengeTicks = nowTicks + RetryTicks;
            }
            catch
            {
                state.NextChallengeTicks = nowTicks + RetryTicks;
            }
        }

        private void ReceiveDirect(ZRpc rpc, ZPackage package)
        {
            lock (_gate)
            {
                if (_disposed || rpc == null || !TryReadFrame(package, out byte[] frame) ||
                    !AdmissionProtocolV2.TryGetKind(frame, out AdmissionMessageKind kind, out _))
                    return;
                ZNet network = _network;
                if (network == null) return;
                if (network.IsServer())
                {
                    if (kind == AdmissionMessageKind.Report)
                        ReceiveServerReportLocked(rpc, frame, MonotonicTicks());
                    return;
                }
#if !RUNIC_SENTINEL_SERVER_ONLY
                if (_clientConnection == null || !_clientConnection.IsExact(rpc)) return;
                if (kind == AdmissionMessageKind.Challenge)
                    ReceiveClientChallengeLocked(_clientConnection, frame, MonotonicTicks());
                else if (kind == AdmissionMessageKind.Decision)
                    ReceiveClientDecisionLocked(_clientConnection, frame);
#endif
            }
        }

        private void ReceiveServerReportLocked(ZRpc rpc, byte[] frame, long nowTicks)
        {
            if (!_serverConnections.TryGetValue(rpc, out ServerConnection state) ||
                state.Denied || !state.IsExact()) return;
            if (!AdmissionProtocolV2.TryDecodeReport(frame, out AdmissionReport report, out string failure))
            {
                FailAdmissionLocked(state, failure, nowTicks);
                return;
            }
            if (state.Challenge == null || state.Policy == null)
            {
                FailAdmissionLocked(state, "sentinel-challenge-unavailable", nowTicks);
                return;
            }
            if (!PolicyIsCurrentLocked(state))
            {
                FailAdmissionLocked(state, "sentinel-policy-changed", nowTicks);
                return;
            }
            if (state.AcceptedReportDigest.Length != 0)
            {
                if (string.Equals(state.AcceptedReportDigest, report.ProfileDigest, StringComparison.Ordinal))
                    SendDecisionLocked(
                        state,
                        state.Compliant,
                        _mode == SentinelRemoteAdmissionMode.Required && !state.NativeReleased,
                        state.Compliant ? "compatible" : "sentinel-report-rejected",
                        nowTicks);
                else FailAdmissionLocked(state, "sentinel-report-equivocation", nowTicks);
                return;
            }
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!AdmissionProtocolV2.TryValidateReport(
                    state.Challenge,
                    report,
                    nowUnix,
                    out AdmissionClientProfile profile,
                    out failure))
            {
                FailAdmissionLocked(state, failure, nowTicks);
                return;
            }
            _runtime.ObserveRemoteAdmissionProfile(profile);

            string role = "player";
            if (SentinelTransportIdentity.TryResolveConnection(
                    state.Peer,
                    out string authority,
                    out string subject))
            {
                if (_runtime.IsBanned(authority, subject))
                {
                    FailAdmissionLocked(state, "sentinel-account-banned", nowTicks);
                    return;
                }
                if (_runtime.IsAdministrator(authority, subject)) role = "administrator";
            }
            SentinelAdmissionCheck check = Evaluate(_runtime, state.Policy, profile, role);
            state.AcceptedReportDigest = report.ProfileDigest;
            if (!check.Compatible)
            {
                FailAdmissionLocked(state, check.Reason, nowTicks);
                return;
            }
            state.Compliant = true;
            state.ApprovedAwaitingResume =
                _mode == SentinelRemoteAdmissionMode.Required && !state.NativeReleased;
            state.ResumeDeadlineTicks = state.ApprovedAwaitingResume
                ? nowTicks + DurationTicks(ResumeGraceSeconds)
                : 0L;
            SendDecisionLocked(
                state,
                true,
                state.ApprovedAwaitingResume,
                "compatible",
                nowTicks);
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void ReceiveClientChallengeLocked(
            ClientConnection state,
            byte[] frame,
            long nowTicks)
        {
            if (!AdmissionProtocolV2.TryDecodeChallenge(
                    frame, out AdmissionChallenge challenge, out _) ||
                !AdmissionProtocolV2.IsChallengeCurrent(
                    challenge, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _)) return;
            if (state.Challenge == null || !string.Equals(
                    state.Challenge.RequestId, challenge.RequestId, StringComparison.Ordinal))
            {
                state.Challenge = challenge;
                state.ReportAttempts = 0;
                state.DecisionReceived = false;
                state.ResumeSent = false;
                state.ResumeAttempts = 0;
            }
            state.NextReportTicks = nowTicks;
            SendClientReportLocked(state, nowTicks);
        }

        private void SendClientReportLocked(ClientConnection state, long nowTicks)
        {
            if (state?.Challenge == null || state.ReportAttempts >= MaximumReportAttempts ||
                !state.IsExact(state.Rpc)) return;
            if (!_runtime.TryGetAdmissionClientProfile(out AdmissionClientProfile profile))
            {
                state.NextReportTicks = nowTicks + RetryTicks;
                return;
            }
            try
            {
                AdmissionReport report = AdmissionProtocolV2.CreateReport(
                    state.Challenge,
                    profile,
                    SentinelVersion.Current,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                InvokeFrame(state.Rpc, AdmissionProtocolV2.EncodeReport(report));
                state.ReportAttempts++;
                state.NextReportTicks = nowTicks + RetryTicks;
            }
            catch
            {
                state.NextReportTicks = nowTicks + RetryTicks;
            }
        }

        private void ReceiveClientDecisionLocked(ClientConnection state, byte[] frame)
        {
            if (!AdmissionProtocolV2.TryDecodeDecision(
                    frame, out AdmissionDecisionMessage decision, out _) ||
                !AdmissionProtocolV2.IsDecisionFresh(
                    decision, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ||
                state.Challenge == null || !string.Equals(
                    state.Challenge.RequestId, decision.RequestId, StringComparison.Ordinal)) return;
            if (!decision.Accepted)
            {
                state.DecisionReceived = true;
                _runtime.RecordAdmissionFailure(decision.ReasonCode);
                return;
            }
            if (!decision.ResumeHandshake)
            {
                state.DecisionReceived = true;
                return;
            }
            if (state.ResumeSent || state.ResumeAttempts >= MaximumResumeAttempts) return;
            state.ResumeAttempts++;
            try
            {
                state.Rpc.Invoke("ServerHandshake", state.NativeHandshakeSecret);
                state.ResumeSent = true;
                state.DecisionReceived = true;
            }
            catch
            {
                _runtime.RecordAdmissionFailure("sentinel-native-handshake-resume-failed");
            }
        }
#endif

        private void SendDecisionLocked(
            ServerConnection state,
            bool accepted,
            bool resume,
            string reason,
            long nowTicks)
        {
            if (state.Challenge == null || !state.IsExact()) return;
            long sequence = state.Policy?.Sequence ?? 0L;
            string profile = state.Policy?.Profile ?? string.Empty;
            try
            {
                var message = new AdmissionDecisionMessage(
                    state.Challenge.RequestId,
                    accepted,
                    accepted && resume,
                    CanonicalReason(reason),
                    sequence,
                    profile,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                InvokeFrame(state.Rpc, AdmissionProtocolV2.EncodeDecision(message));
                state.DecisionAttempts++;
                state.NextDecisionTicks = nowTicks + DecisionRetryTicks;
            }
            catch { state.NextDecisionTicks = nowTicks + DecisionRetryTicks; }
        }

        private void FailAdmissionLocked(ServerConnection state, string reason, long nowTicks)
        {
            string bounded = CanonicalReason(reason);
            RecordLocked(state, bounded);
            _runtime.RecordAdmissionFailure(bounded);
            if (_mode == SentinelRemoteAdmissionMode.Required)
            {
                SendDecisionLocked(state, false, false, bounded, nowTicks);
                DenyLocked(state, bounded, nowTicks);
            }
            else
            {
                ResetOptionalAdmissionExchangeLocked(state);
            }
        }

        private static void ResetOptionalAdmissionExchangeLocked(ServerConnection state)
        {
            state.StartedTicks = 0L;
            state.DeadlineTicks = 0L;
            state.NextChallengeTicks = 0L;
            state.NextDecisionTicks = 0L;
            state.ResumeDeadlineTicks = 0L;
            state.ChallengeAttempts = 0;
            state.DecisionAttempts = 0;
            state.Challenge = null;
            state.Policy = null;
            state.AcceptedReportDigest = string.Empty;
            state.Compliant = false;
            state.ApprovedAwaitingResume = false;
            state.ReleaseInProgress = false;
        }

        private void DenyLocked(ServerConnection state, string reason, long nowTicks)
        {
            state.Denied = true;
            state.DenialReason = CanonicalReason(reason);
            state.ApprovedAwaitingResume = false;
            state.ReleaseInProgress = false;
            state.DisconnectAtTicks = nowTicks + DisconnectGraceTicks;
        }

        private void RecordLocked(ServerConnection state, string reason)
        {
            ISentinelEvidenceSink sink = _evidence?.Sink;
            if (sink == null || state == null) return;
            string correlation = state.Challenge?.RequestId ??
                                 "connection-" + state.Ordinal.ToString();
            sink.TryAppend(
                "connection:" + state.Ordinal.ToString(),
                "sentinel.remote-admission",
                correlation,
                _mode == SentinelRemoteAdmissionMode.Required
                    ? FindingConfidence.High
                    : FindingConfidence.Moderate,
                _mode == SentinelRemoteAdmissionMode.Required
                    ? EnforcementAction.Disconnect
                    : EnforcementAction.Warn,
                "reason-" + CanonicalReason(reason) + ".direct-peer-bound",
                out _);
        }

        private static string ReasonFor(AdmissionDecision decision)
        {
            if (decision == null) return "sentinel-policy-not-allow";
            if (decision.Disposition == AdmissionDisposition.Quarantine)
                return "sentinel-policy-quarantine";
            if (decision.Findings != null && decision.Findings.Count != 0)
            {
                string rule = decision.Findings[0]?.Rule;
                if (AdmissionValidation.IsAtom(rule, 1, 64))
                    return "sentinel-policy-" + rule.ToLowerInvariant();
            }
            return "sentinel-policy-not-allow";
        }

        private static string CanonicalReason(string value) =>
            AdmissionValidation.IsReason(value) ? value : "sentinel-admission-rejected";

        private static long MonotonicTicks() => Stopwatch.GetTimestamp();

        private static long DurationTicks(long seconds) =>
            checked(seconds * Stopwatch.Frequency);

        private bool PolicyIsCurrentLocked(ServerConnection state)
        {
            if (state?.Policy == null) return false;
            try
            {
                return _runtime.TryGetVerifiedPolicy(out SentinelPolicy current) &&
                       ReferenceEquals(current, state.Policy);
            }
            catch { return false; }
        }

        private static SentinelAdmissionCheck Fail(string reason) =>
            new SentinelAdmissionCheck(false, CanonicalReason(reason));

        private static ZNetPeer FindExactPeer(ZNet network, ZRpc rpc)
        {
            List<ZNetPeer> peers;
            try { peers = network?.GetPeers(); }
            catch { return null; }
            if (peers == null) return null;
            int inspected = 0;
            foreach (ZNetPeer peer in peers)
            {
                if (inspected++ >= MaximumTrackedConnections) break;
                if (peer != null && ReferenceEquals(peer.m_rpc, rpc)) return peer;
            }
            return null;
        }

        private static bool SocketConnected(ZNetPeer peer)
        {
            try { return peer?.m_socket != null && peer.m_socket.IsConnected(); }
            catch { return false; }
        }

        private static void InvokeFrame(ZRpc rpc, byte[] frame)
        {
            if (rpc == null || frame == null || frame.Length == 0 ||
                frame.Length > AdmissionProtocolV2.MaximumFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(frame));
            var package = new ZPackage(frame);
            if (package.Size() > MaximumOuterPackageBytes)
                throw new InvalidOperationException("Admission package exceeded its wire bound.");
            rpc.Invoke(AdmissionProtocolV2.DirectRpcName, package);
        }

        private static bool TryReadFrame(ZPackage package, out byte[] frame)
        {
            frame = null;
            try
            {
                if (package == null || package.Size() <= 0 ||
                    package.Size() > MaximumOuterPackageBytes) return false;
                frame = package.GetArray();
                return frame != null && frame.Length > 0 &&
                       frame.Length <= AdmissionProtocolV2.MaximumFrameBytes;
            }
            catch { frame = null; return false; }
        }

        private void RemoveConnectionLocked(ZRpc rpc)
        {
            if (rpc == null || !_serverConnections.TryGetValue(
                    rpc, out ServerConnection state)) return;
            _serverConnections.Remove(rpc);
            UnregisterOwnedHandler(rpc, state.RegisteredHandler);
        }

        private void ClearConnectionsLocked()
        {
            foreach (ServerConnection state in new List<ServerConnection>(_serverConnections.Values))
                UnregisterOwnedHandler(state.Rpc, state.RegisteredHandler);
            _serverConnections.Clear();
#if !RUNIC_SENTINEL_SERVER_ONLY
            ClearClientLocked();
#endif
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private void ClearClientLocked()
        {
            if (_clientConnection != null)
                UnregisterOwnedHandler(
                    _clientConnection.Rpc, _clientConnection.RegisteredHandler);
            _clientConnection = null;
            _blockedClientPeer = null;
            _blockedClientRpc = null;
            _blockedClientSocket = null;
        }
#endif

        private bool TryRegisterDirectHandler(
            ZRpc rpc,
            out object registered,
            out string failure)
        {
            registered = null;
            failure = "sentinel-rpc-registration-failed";
            if (!TryGetFunctionMap(rpc, out IDictionary functions))
            {
                failure = "sentinel-rpc-registry-unavailable";
                return false;
            }
            int key = StableHash(AdmissionProtocolV2.DirectRpcName);
            try
            {
                if (functions.Contains(key))
                {
                    failure = "sentinel-rpc-name-collision";
                    return false;
                }
                rpc.Register<ZPackage>(AdmissionProtocolV2.DirectRpcName, ReceiveDirect);
                registered = functions[key];
                return registered != null;
            }
            catch
            {
                registered = null;
                return false;
            }
        }

        private static void UnregisterOwnedHandler(ZRpc rpc, object registered)
        {
            if (rpc == null || registered == null ||
                !TryGetFunctionMap(rpc, out IDictionary functions)) return;
            int key = StableHash(AdmissionProtocolV2.DirectRpcName);
            try
            {
                if (ReferenceEquals(functions[key], registered))
                    rpc.Unregister(AdmissionProtocolV2.DirectRpcName);
            }
            catch { }
        }

        private static bool TryGetFunctionMap(ZRpc rpc, out IDictionary functions)
        {
            functions = null;
            if (rpc == null || RpcFunctionsField == null) return false;
            try
            {
                functions = RpcFunctionsField.GetValue(rpc) as IDictionary;
                return functions != null;
            }
            catch
            {
                functions = null;
                return false;
            }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int first = 5381;
                int second = first;
                int index = 0;
                while (index < value.Length && value[index] != '\0')
                {
                    first = ((first << 5) + first) ^ value[index];
                    if (index == value.Length - 1 || value[index + 1] == '\0') break;
                    second = ((second << 5) + second) ^ value[index + 1];
                    index += 2;
                }
                return first + second * 1566083941;
            }
        }

        private static void ValidatePatchSeams()
        {
            ValidateVoid("OnNewConnection", typeof(ZNetPeer));
            ValidateVoid("RPC_ServerHandshake", typeof(ZRpc), typeof(string));
            ValidateVoid("RPC_PeerInfo", typeof(ZRpc), typeof(ZPackage));
            ValidateVoid("Disconnect", typeof(ZNetPeer));
#if !RUNIC_SENTINEL_SERVER_ONLY
            if (InviteSecretKeyField == null || !InviteSecretKeyField.IsStatic ||
                InviteSecretKeyField.FieldType != typeof(string))
                throw new MissingFieldException(typeof(ZNet).FullName, "m_inviteSecretKey");
#endif
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private static bool TryReadInviteSecretKey(out string value)
        {
            value = string.Empty;
            if (InviteSecretKeyField == null) return false;
            try
            {
                value = InviteSecretKeyField.GetValue(null) as string ?? string.Empty;
                return value.Length <= 1024;
            }
            catch
            {
                value = string.Empty;
                return false;
            }
        }
#endif

        private static void ValidateVoid(string name, params Type[] parameters)
        {
            MethodInfo method = AccessTools.DeclaredMethod(typeof(ZNet), name, parameters);
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                throw new MissingMethodException(typeof(ZNet).FullName, name);
        }

        private sealed class ServerConnection
        {
            internal ServerConnection(ZNetPeer peer, ZRpc rpc, long ordinal)
            {
                Peer = peer;
                Rpc = rpc;
                Socket = peer?.m_socket;
                Ordinal = ordinal;
                AcceptedReportDigest = string.Empty;
                DenialReason = string.Empty;
            }

            internal ZNetPeer Peer { get; }
            internal ZRpc Rpc { get; }
            internal ISocket Socket { get; }
            internal long Ordinal { get; }
            internal long StartedTicks;
            internal long DeadlineTicks;
            internal long NextChallengeTicks;
            internal long NextDecisionTicks;
            internal long DisconnectAtTicks;
            internal long ResumeDeadlineTicks;
            internal long PeerInfoDeadlineTicks;
            internal int ChallengeAttempts;
            internal int DecisionAttempts;
            internal AdmissionChallenge Challenge;
            internal SentinelPolicy Policy;
            internal string AcceptedReportDigest;
            internal string DenialReason;
            internal string NativeHandshakeSecret = string.Empty;
            internal bool NativeHandshakeObserved;
            internal object RegisteredHandler;
            internal bool Compliant;
            internal bool ApprovedAwaitingResume;
            internal bool ReleaseInProgress;
            internal bool NativeReleased;
            internal bool PeerInfoReleased;
            internal bool Denied;

            internal bool IsExact() => Peer != null && Rpc != null &&
                ReferenceEquals(Peer.m_rpc, Rpc) && ReferenceEquals(Peer.m_socket, Socket);
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        private sealed class ClientConnection
        {
            internal ClientConnection(
                ZNetPeer peer,
                ZRpc rpc,
                object registeredHandler,
                string nativeHandshakeSecret)
            {
                Peer = peer;
                Rpc = rpc;
                RegisteredHandler = registeredHandler;
                NativeHandshakeSecret = nativeHandshakeSecret ?? string.Empty;
            }

            internal ZNetPeer Peer { get; }
            internal ZRpc Rpc { get; }
            internal object RegisteredHandler { get; }
            internal string NativeHandshakeSecret { get; }
            internal AdmissionChallenge Challenge;
            internal long NextReportTicks;
            internal int ReportAttempts;
            internal int ResumeAttempts;
            internal bool DecisionReceived;
            internal bool ResumeSent;

            internal bool IsExact(ZRpc rpc) => ReferenceEquals(Rpc, rpc) &&
                Peer != null && ReferenceEquals(Peer.m_rpc, Rpc);
        }
#endif
    }

#if !RUNIC_SENTINEL_SERVER_ONLY
    [HarmonyPatch(typeof(ZNet), "OnNewConnection", new Type[] { typeof(ZNetPeer) })]
    internal static class SentinelNewConnectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ZNet __instance,
            [HarmonyArgument(0)] ZNetPeer peer) =>
            SentinelNetworkCompatibility.ObserveConnection(__instance, peer);
    }
#endif

    [HarmonyPatch(
        typeof(ZNet), "RPC_ServerHandshake",
        new Type[] { typeof(ZRpc), typeof(string) })]
    internal static class SentinelServerHandshakePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            [HarmonyArgument(1)] string secretKey,
            ref bool __state) =>
            SentinelNetworkCompatibility.BeforeServerHandshake(
                __instance, rpc, secretKey, out __state);

        [HarmonyPostfix]
        private static void Postfix(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            bool __state) =>
            SentinelNetworkCompatibility.AfterServerHandshake(
                __instance, rpc, __state);

        [HarmonyFinalizer]
        private static Exception Finalizer(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            bool __state,
            Exception __exception)
        {
            if (__exception != null)
                SentinelNetworkCompatibility.ServerHandshakeFailed(
                    __instance, rpc, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo", new Type[] { typeof(ZRpc), typeof(ZPackage) })]
    internal static class SentinelPeerInfoPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            ref bool __state) =>
            SentinelNetworkCompatibility.BeforePeerInfo(
                __instance, rpc, out __state);

        [HarmonyPostfix]
        private static void Postfix(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            bool __state) =>
            SentinelNetworkCompatibility.AfterPeerInfo(
                __instance, rpc, __state);

        [HarmonyFinalizer]
        private static Exception Finalizer(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc,
            bool __state,
            Exception __exception)
        {
            if (__exception != null)
                SentinelNetworkCompatibility.PeerInfoFailed(
                    __instance, rpc, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZNet), "Disconnect", new Type[] { typeof(ZNetPeer) })]
    internal static class SentinelDisconnectPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            ZNet __instance,
            [HarmonyArgument(0)] ZNetPeer peer) =>
            SentinelNetworkCompatibility.ForgetConnection(__instance, peer);
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class SentinelNetworkDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ZNet __instance) =>
            SentinelNetworkCompatibility.ForgetNetwork(__instance);
    }
}
