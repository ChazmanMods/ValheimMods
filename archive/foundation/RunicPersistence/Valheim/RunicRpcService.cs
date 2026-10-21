using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Runic.Foundation.Core;
using Steamworks;
using UnityEngine;

namespace Runic.Foundation.Persistence
{
    /// <summary>
    /// Valheim adapter for the bounded Foundation RPC contracts. Incoming actor identity is bound
    /// to the ZRpc object registered for one ZNetPeer. ZRoutedRpc sender IDs are never accepted.
    /// </summary>
    public sealed class RunicRpcService :
        IRunicRpcService,
        IRunicRpcPeerAdmissionService,
        IDisposable
    {
        public const string DirectRpcName = "chazman.RunicPersistence.FoundationRpc.v1";
        public const int MaximumPendingRequestsPerPeer = 64;
        public const int MaximumFramesPerSecond = 64;
        public const int MaximumBytesPerSecond = 512 * 1024;
        public const int MaximumEndpoints = 256;
        public const int MaximumPeerRequirements = 128;
        public const int MaximumClaimProviders = 64;
        public const int MaximumClaimEvaluators = 64;
        public const int MaximumSessions = 64;
        public const int MaximumDisconnectAttempts = 3;
        public const int DisconnectRetryMilliseconds = 250;

        private static readonly FieldInfo RpcFunctionsField =
            typeof(ZRpc).GetField("m_functions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SteamConnectionField =
            typeof(ZSteamSocket).GetField("m_con", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly object _gate = new object();
        private readonly Dictionary<ZRpc, PeerSession> _sessions =
            new Dictionary<ZRpc, PeerSession>(ReferenceComparer<ZRpc>.Instance);
        private readonly Dictionary<long, PeerSession> _peers =
            new Dictionary<long, PeerSession>();
        private readonly Dictionary<string, EndpointEntry> _endpoints =
            new Dictionary<string, EndpointEntry>(StringComparer.Ordinal);
        private readonly Dictionary<long, RequirementEntry> _requirements =
            new Dictionary<long, RequirementEntry>();
        private readonly Dictionary<long, ClaimProviderEntry> _claimProviders =
            new Dictionary<long, ClaimProviderEntry>();
        private readonly Dictionary<string, ClaimEvaluatorEntry> _claimEvaluators =
            new Dictionary<string, ClaimEvaluatorEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PeerEvaluatorEntry> _peerEvaluators =
            new Dictionary<string, PeerEvaluatorEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingRequest> _pending =
            new Dictionary<string, PendingRequest>(StringComparer.Ordinal);
        private readonly ManualLogSource _log;
        private readonly RunicRegistry _registry;
        private readonly RunicRpcRuntimeContext _runtime;
        private long _nextRegistrationToken;
        private PlayerBindingEntry _playerBinding;
        private PeerSession _serverSession;
        private bool _disposed;

        internal RunicRpcService(ManualLogSource log)
            : this(log, RunicRegistry.Shared, RunicRpcRuntimeContext.Production)
        {
        }

        internal RunicRpcService(ManualLogSource log, RunicRegistry registry)
            : this(log, registry, RunicRpcRuntimeContext.Production)
        {
        }

        internal RunicRpcService(
            ManualLogSource log,
            RunicRegistry registry,
            RunicRpcRuntimeContext runtime)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool IsServer => _runtime.IsServer;

        public bool IsServerConnectionReady
        {
            get
            {
                lock (_gate)
                    return !_disposed && _serverSession != null &&
                           _serverSession.Ready && IsSessionCurrentLocked(_serverSession);
            }
        }

        public event EventHandler<RpcPeerEventArgs> PeerReady;
        public event EventHandler<RpcPeerEventArgs> PeerDisconnected;
        public event EventHandler<RpcAdmissionRejectedEventArgs> AdmissionRejected;

        public IDisposable RegisterEndpoint(
            ModuleRegistration module,
            RpcEndpointDescriptor descriptor,
            RunicRpcHandler handler)
        {
            ValidateModuleLease(module);
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!string.Equals(module.Descriptor.ModuleId, descriptor.ModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("Endpoint module ID does not match its active module lease.");
            if (descriptor.ProtocolVersion != module.Descriptor.ProtocolVersion.Major)
                throw new InvalidOperationException(
                    "Endpoint protocol must equal its owning module hello protocol major.");
            if (!module.Descriptor.HasCapability(descriptor.CapabilityId))
                throw new InvalidOperationException("Endpoint capability is not owned by its module lease.");

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_endpoints.Count >= MaximumEndpoints)
                    throw new InvalidOperationException("The bounded RPC endpoint registry is full.");
                if (_endpoints.ContainsKey(descriptor.EndpointId))
                    throw new InvalidOperationException("RPC endpoint is already registered: " + descriptor.EndpointId);
                long token = NextTokenLocked();
                _endpoints.Add(descriptor.EndpointId, new EndpointEntry(module, descriptor, handler, token));
                return new RegistrationLease(() => UnregisterEndpoint(descriptor.EndpointId, token));
            }
        }

        public IDisposable RegisterPeerRequirement(
            ModuleRegistration module,
            RpcPeerRequirement requirement)
        {
            ValidateModuleLease(module);
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_requirements.Count >= MaximumPeerRequirements)
                    throw new InvalidOperationException("The bounded peer-requirement registry is full.");
                long token = NextTokenLocked();
                _requirements.Add(token, new RequirementEntry(module, requirement, token));
                return new RegistrationLease(() => UnregisterRequirement(token));
            }
        }

        public IDisposable RegisterPlayerBindingResolver(
            ModuleRegistration module,
            IRpcPlayerBindingResolver resolver)
        {
            ValidateModuleLease(module);
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (!module.Descriptor.HasCapability(RunicCapabilityIds.ActorIdentityBinding))
                throw new InvalidOperationException(
                    "The actor-binding resolver module does not own the required capability.");
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_playerBinding != null)
                    throw new InvalidOperationException("An actor-binding resolver is already registered.");
                long token = NextTokenLocked();
                _playerBinding = new PlayerBindingEntry(module, resolver, token);
                return new RegistrationLease(() => UnregisterPlayerBinding(token));
            }
        }

        public IDisposable RegisterHandshakeClaimProvider(
            ModuleRegistration module,
            RunicHandshakeClaimProvider provider)
        {
            ValidateModuleLease(module);
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_claimProviders.Count >= MaximumClaimProviders)
                    throw new InvalidOperationException("The bounded handshake-claim provider registry is full.");
                long token = NextTokenLocked();
                _claimProviders.Add(token, new ClaimProviderEntry(module, provider, token));
                return new RegistrationLease(() => UnregisterClaimProvider(token));
            }
        }

        public IDisposable RegisterHandshakeClaimEvaluator(
            ModuleRegistration module,
            string evaluatorId,
            RunicHandshakeClaimEvaluator evaluator)
        {
            ValidateModuleLease(module);
            evaluatorId = RunicIdentifier.Require(evaluatorId, nameof(evaluatorId));
            if (!evaluatorId.StartsWith(module.Descriptor.ModuleId + ".", StringComparison.Ordinal))
                throw new ArgumentException("Claim evaluator ID must be namespaced by its module.", nameof(evaluatorId));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_claimEvaluators.Count >= MaximumClaimEvaluators)
                    throw new InvalidOperationException("The bounded handshake-claim evaluator registry is full.");
                if (_claimEvaluators.ContainsKey(evaluatorId))
                    throw new InvalidOperationException("Handshake-claim evaluator is already registered.");
                long token = NextTokenLocked();
                _claimEvaluators.Add(
                    evaluatorId,
                    new ClaimEvaluatorEntry(module, evaluatorId, evaluator, token));
                return new RegistrationLease(() => UnregisterClaimEvaluator(evaluatorId, token));
            }
        }

        public IDisposable RegisterHandshakePeerEvaluator(
            ModuleRegistration module,
            string evaluatorId,
            RunicHandshakePeerEvaluator evaluator)
        {
            ValidateModuleLease(module);
            evaluatorId = RunicIdentifier.Require(evaluatorId, nameof(evaluatorId));
            if (!evaluatorId.StartsWith(module.Descriptor.ModuleId + ".", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Peer evaluator ID must be namespaced by its module.", nameof(evaluatorId));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_peerEvaluators.Count >= MaximumClaimEvaluators)
                    throw new InvalidOperationException(
                        "The bounded handshake peer-evaluator registry is full.");
                if (_peerEvaluators.ContainsKey(evaluatorId))
                    throw new InvalidOperationException(
                        "Handshake peer evaluator is already registered.");
                long token = NextTokenLocked();
                _peerEvaluators.Add(
                    evaluatorId,
                    new PeerEvaluatorEntry(module, evaluatorId, evaluator, token));
                return new RegistrationLease(() => UnregisterPeerEvaluator(evaluatorId, token));
            }
        }

        public RpcSendResult SendToServer(
            ModuleRegistration module,
            string endpointId,
            byte[] payload,
            string idempotencyKey,
            TimeSpan timeout,
            RunicRpcCompletion completion)
        {
            ValidateModuleLease(module);
            lock (_gate)
            {
                if (_disposed) return Rejected("service-unavailable");
                if (IsServer) return Rejected("already-server");
                return SendLocked(
                    module,
                    _serverSession,
                    endpointId,
                    payload,
                    idempotencyKey,
                    timeout,
                    completion,
                    false);
            }
        }

        public RpcSendResult SendToPeer(
            ModuleRegistration module,
            RpcPeerSnapshot expectedPeer,
            string endpointId,
            byte[] payload,
            string idempotencyKey,
            TimeSpan timeout,
            RunicRpcCompletion completion)
        {
            ValidateModuleLease(module);
            lock (_gate)
            {
                if (_disposed) return Rejected("service-unavailable");
                if (!IsServer) return Rejected("not-server");
                if (expectedPeer == null) return Rejected("expected-peer-required");
                _peers.TryGetValue(expectedPeer.PeerId, out PeerSession session);
                if (session == null ||
                    !string.Equals(session.SessionId, expectedPeer.SessionId, StringComparison.Ordinal))
                    return Rejected("stale-peer-session");
                return SendLocked(
                    module,
                    session,
                    endpointId,
                    payload,
                    idempotencyKey,
                    timeout,
                    completion,
                    true);
            }
        }

        public bool TryGetPeer(long peerId, out RpcPeerSnapshot peer)
        {
            lock (_gate)
            {
                peer = null;
                if (_disposed) return false;
                PeerSession session;
                if (IsServer)
                {
                    if (!_peers.TryGetValue(peerId, out session)) return false;
                }
                else
                {
                    session = _serverSession;
                    if (session == null || session.PeerId != peerId) return false;
                }
                if (!TryRefreshIdentityLocked(session, out string identityReason))
                {
                    MarkFailedLocked(session, identityReason);
                    return false;
                }
                peer = SnapshotLocked(session);
                return peer.Current;
            }
        }

        public bool TryResolveActor(
            RpcPeerSnapshot expectedPeer,
            RpcActorAssurance minimumAssurance,
            out RpcActorSnapshot actor,
            out string reasonCode)
        {
            actor = null;
            reasonCode = "actor-unavailable";
            if (!Enum.IsDefined(typeof(RpcActorAssurance), minimumAssurance))
                throw new ArgumentOutOfRangeException(nameof(minimumAssurance));

            lock (_gate)
            {
                if (_disposed || !IsServer)
                {
                    reasonCode = "actor-resolution-server-only";
                    return false;
                }
                if (expectedPeer == null ||
                    !_peers.TryGetValue(expectedPeer.PeerId, out PeerSession session) ||
                    !string.Equals(session.SessionId, expectedPeer.SessionId, StringComparison.Ordinal) ||
                    !session.Ready || !session.Admitted || !IsSessionCurrentLocked(session))
                {
                    reasonCode = "stale-peer-session";
                    return false;
                }
                if (!TryRefreshIdentityLocked(session, out reasonCode))
                {
                    MarkFailedLocked(session, reasonCode);
                    return false;
                }
                if (!TryResolveTransportOwnedCharacterLocked(
                        session,
                        out ZDOID characterId,
                        out long claimedPlayerId,
                        out reasonCode))
                    return false;

                RpcActorAssurance achieved = RpcActorAssurance.TransportOwnedCharacter;
                if (minimumAssurance == RpcActorAssurance.AccountBoundPlayer)
                {
                    if (session.Identity == null ||
                        session.Identity.Assurance < RpcIdentityAssurance.BackendAccount)
                    {
                        reasonCode = "backend-account-required";
                        return false;
                    }
                    if (_playerBinding == null ||
                        !_registry.IsModuleRegistrationActive(_playerBinding.Owner))
                    {
                        reasonCode = "actor-binding-unavailable";
                        return false;
                    }
                    RpcPlayerBindingStatus status;
                    try { status = _playerBinding.Resolver.Resolve(session.Identity, claimedPlayerId); }
                    catch
                    {
                        reasonCode = "actor-binding-failed";
                        return false;
                    }
                    if (status != RpcPlayerBindingStatus.Verified)
                    {
                        reasonCode = BindingFailureReason(status);
                        return false;
                    }
                    achieved = RpcActorAssurance.AccountBoundPlayer;
                }

                actor = new RpcActorSnapshot(
                    SnapshotLocked(session),
                    characterId,
                    claimedPlayerId,
                    achieved,
                    IsServerAdminLocked(session));
                reasonCode = "actor-resolved";
                return true;
            }
        }

        public IReadOnlyList<RpcPeerSnapshot> GetPeers()
        {
            lock (_gate)
            {
                if (!IsServer)
                {
                    if (_disposed || _serverSession == null) return Array.Empty<RpcPeerSnapshot>();
                    RpcPeerSnapshot server = SnapshotLocked(_serverSession);
                    return server.Current
                        ? new[] { server }
                        : Array.Empty<RpcPeerSnapshot>();
                }
                var snapshots = new List<RpcPeerSnapshot>(_peers.Count);
                foreach (PeerSession session in _peers.Values.OrderBy(value => value.PeerId))
                {
                    if (!TryRefreshIdentityLocked(session, out string identityReason))
                    {
                        MarkFailedLocked(session, identityReason);
                        continue;
                    }
                    RpcPeerSnapshot snapshot = SnapshotLocked(session);
                    if (snapshot.Current) snapshots.Add(snapshot);
                }
                return snapshots.AsReadOnly();
            }
        }

        public bool TryDisconnectPeer(long peerId, string reasonCode)
        {
            RunicIdentifier.Require(reasonCode, nameof(reasonCode));
            lock (_gate)
            {
                if (_disposed || !IsServer || !_peers.TryGetValue(peerId, out PeerSession session) ||
                    !IsSessionCurrentLocked(session))
                    return false;
                MarkFailedLocked(session, reasonCode);
                return true;
            }
        }

        internal bool OnConnectionStarted(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return false;
            string failure = string.Empty;
            try
            {
                lock (_gate)
                {
                    if (_disposed) return false;
                    if (_sessions.ContainsKey(peer.m_rpc)) return true;
                    if (_sessions.Count >= MaximumSessions)
                    {
                        failure = "provisional-session-capacity";
                        return RejectNewConnection(peer, failure);
                    }
                    if (HasRpcNameCollision(peer.m_rpc))
                    {
                        failure = "rpc-name-collision";
                        return RejectNewConnection(peer, failure);
                    }
                    bool localIsServer = _runtime.IsServer;
                    var session = new PeerSession(peer, localIsServer);
                    _sessions.Add(peer.m_rpc, session);
                    try
                    {
                        session.SessionId = localIsServer ? Guid.NewGuid().ToString("N") : string.Empty;
                        session.OwnNonce = RpcHandshakeCodec.CreateNonce();
                        if (localIsServer) session.Identity = ResolveIdentity(session);
                        session.ConnectedUtcTicks = _runtime.UtcNowTicks;
                        session.HandshakeDeadlineUtcTicks = SaturatingAdd(
                            _runtime.UtcNowTicks,
                            TimeSpan.FromSeconds(10).Ticks);
                        peer.m_rpc.Register<ZPackage>(DirectRpcName, ReceivePackage);
                        if (!localIsServer) _serverSession = session;
                        return true;
                    }
                    catch (Exception exception)
                    {
                        MarkFailedLocked(session, "connection-start-failed");
                        failure = "connection-start-failed-" + exception.GetType().Name;
                    }
                }
            }
            catch (Exception exception)
            {
                failure = "connection-start-failed-" + exception.GetType().Name;
                FailClosed(peer.m_rpc, "connection-start-failed", exception);
            }
            _log.LogError("Runic RPC rejected a provisional connection (" + failure + ").");
            return RejectNewConnection(peer, "connection-start-failed");
        }

        internal void OnServerHandshake(ZRpc rpc)
        {
            PeerSession session;
            RpcWireFrame challenge = null;
            lock (_gate)
            {
                if (_disposed || !IsServer || !_sessions.TryGetValue(rpc, out session) ||
                    session.ChallengeSent)
                    return;
                ProtocolHello hello = BuildLocalHello(session.OwnNonce);
                challenge = NewFrameLocked(session, RpcFrameKind.Challenge);
                challenge.Payload = RpcHandshakeCodec.Encode(new RpcHandshakeProfile
                {
                    Nonce = session.OwnNonce,
                    EchoNonce = Array.Empty<byte>(),
                    Hello = hello
                });
                session.ChallengeSent = true;
            }
            SendFrame(session, challenge);
        }

        internal void OnClientHandshake(ZRpc rpc)
        {
            PeerSession session;
            RpcWireFrame offer;
            lock (_gate)
            {
                if (_disposed || IsServer || !_sessions.TryGetValue(rpc, out session)) return;
                session.Handshake.MarkClientHandshake();
                offer = TryCreateOfferLocked(session);
            }
            if (offer != null) SendFrame(session, offer);
        }

        internal bool CanSendPeerInfo(ZRpc rpc, string password, out string reasonCode)
        {
            lock (_gate)
            {
                if (_disposed || IsServer)
                {
                    reasonCode = string.Empty;
                    return true;
                }
                if (!_sessions.TryGetValue(rpc, out PeerSession session))
                {
                    reasonCode = "runic-rpc-handler-unavailable";
                    return false;
                }
                bool allowed = session.Handshake.TryAllowOrQueuePeerInfo(password);
                reasonCode = allowed ? string.Empty :
                    (session.CompatibilityReason.Length == 0
                        ? "runic-handshake-missing"
                        : session.CompatibilityReason);
                return allowed;
            }
        }

        internal bool CanReceivePeerInfo(ZRpc rpc, out string reasonCode)
        {
            lock (_gate)
            {
                if (_disposed || !IsServer)
                {
                    reasonCode = string.Empty;
                    return true;
                }
                if (!_sessions.TryGetValue(rpc, out PeerSession session))
                {
                    reasonCode = "runic-rpc-handler-unavailable";
                    return false;
                }
                bool allowed = session.OfferReceived && session.CompatibilityAccepted;
                reasonCode = allowed ? string.Empty :
                    (session.CompatibilityReason.Length == 0
                        ? "runic-handshake-missing"
                        : session.CompatibilityReason);
                return allowed;
            }
        }

        internal void OnPeerAdmitted(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            RpcPeerEventArgs readyEvent = null;
            lock (_gate)
            {
                if (_disposed || !_sessions.TryGetValue(peer.m_rpc, out PeerSession session)) return;
                session.PeerId = peer.m_uid;
                session.Admitted = true;
                if (!TryRefreshIdentityLocked(session, out string identityReason))
                {
                    MarkFailedLocked(session, identityReason);
                    return;
                }
                if (session.LocalIsServer && session.PeerId != 0)
                {
                    if (_peers.TryGetValue(session.PeerId, out PeerSession existing) &&
                        !ReferenceEquals(existing, session))
                    {
                        MarkFailedLocked(session, "peer-uid-collision");
                        return;
                    }
                    _peers.Add(session.PeerId, session);
                }
                session.Ready = session.CompatibilityAccepted && session.Accepted &&
                    (!session.LocalIsServer ||
                     session.Identity.Assurance >= RpcIdentityAssurance.ConnectionBound);
                if (session.Ready) readyEvent = new RpcPeerEventArgs(SnapshotLocked(session), "ready");
            }
            Raise(PeerReady, readyEvent);
        }

        internal void OnConnectionClosed(ZNetPeer peer)
        {
            if (peer == null) return;
            OnConnectionClosed(peer.m_rpc, "connection-closed");
        }

        /// <summary>
        /// ZRoutedRpc.RemovePeer is not, by itself, a connection-close signal.  Vanilla calls it
        /// from ZNet.ClearPlayerData during RPC_ServerHandshake, before the peer has been admitted
        /// to ZRoutedRpc.  Preserve that exact provisional direct session; real disconnects are
        /// covered by ZNet.Disconnect, network shutdown, the handshake deadline, and transport
        /// close detection.  Once AddPeer has admitted the exact session, RemovePeer is a valid
        /// routed-lifecycle close signal.
        /// </summary>
        internal void OnRoutedPeerRemoving(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            bool admitted;
            lock (_gate)
            {
                admitted = _sessions.TryGetValue(peer.m_rpc, out PeerSession session) &&
                    ReferenceEquals(session.Peer, peer) && session.Admitted;
            }
            if (admitted) OnConnectionClosed(peer.m_rpc, "routed-peer-removed");
        }

        internal void Tick()
        {
            List<PendingRequest> timedOut = new List<PendingRequest>();
            List<KeyValuePair<PeerSession, RpcDisconnectAction>> disconnect =
                new List<KeyValuePair<PeerSession, RpcDisconnectAction>>();
            long now = _runtime.UtcNowTicks;
            lock (_gate)
            {
                if (_disposed) return;
                foreach (PendingRequest request in _pending.Values)
                    if (request.DeadlineUtcTicks <= now) timedOut.Add(request);
                foreach (PendingRequest request in timedOut) _pending.Remove(request.CorrelationId);
                foreach (PeerSession session in _sessions.Values)
                {
                    if (!session.Admitted && now >= session.HandshakeDeadlineUtcTicks)
                    {
                        MarkFailedLocked(session, "runic-handshake-timed-out");
                    }
                    RpcDisconnectAction action = session.Disconnect.TakeAction(now);
                    if (action != RpcDisconnectAction.None)
                        disconnect.Add(new KeyValuePair<PeerSession, RpcDisconnectAction>(session, action));
                }
                foreach (PeerSession session in _sessions.Values) session.Replay.Prune(now);
            }

            foreach (PendingRequest request in timedOut)
                Complete(request, RpcResultCode.TimedOut, "request-timed-out", null, false);
            foreach (KeyValuePair<PeerSession, RpcDisconnectAction> item in disconnect)
            {
                PeerSession session = item.Key;
                try
                {
                    if (item.Value == RpcDisconnectAction.Disconnect && session.Peer != null)
                        _runtime.Disconnect(session.Peer);
                    else
                    {
                        session.Peer?.Dispose();
                        OnConnectionClosed(session.Rpc, "transport-close-fallback");
                    }
                }
                catch (Exception exception)
                {
                    _log.LogWarning(
                        "Runic RPC peer disconnect attempt failed (" +
                        exception.GetType().Name + "); rejection remains latched.");
                }
            }
        }

        public void Dispose()
        {
            List<PendingRequest> pending;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                pending = _pending.Values.ToList();
                _pending.Clear();
                _sessions.Clear();
                _peers.Clear();
                _endpoints.Clear();
                _requirements.Clear();
                _claimProviders.Clear();
                _claimEvaluators.Clear();
                _peerEvaluators.Clear();
                _playerBinding = null;
                _serverSession = null;
            }
            foreach (PendingRequest request in pending)
                Complete(request, RpcResultCode.ConnectionClosed, "service-disposed", null, false);
        }

        internal void OnNetworkStopped()
        {
            List<ZRpc> connections;
            lock (_gate) connections = _sessions.Keys.ToList();
            foreach (ZRpc rpc in connections) OnConnectionClosed(rpc, "network-stopped");
        }

        internal void RejectProvisional(ZRpc rpc, string reasonCode)
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(rpc, out PeerSession session))
                {
                    MarkFailedLocked(session, reasonCode);
                }
            }
        }

        internal bool IsEndpointOwnerCurrent(string endpointId)
        {
            lock (_gate)
                return _endpoints.TryGetValue(endpointId ?? string.Empty, out EndpointEntry endpoint) &&
                       _registry.IsModuleRegistrationActive(endpoint.Module);
        }

        private void ReceivePackage(ZRpc rpc, ZPackage package)
        {
            RpcTransportBoundary.Run(
                () => ReceivePackageCore(rpc, package),
                exception => FailClosed(rpc, "direct-rpc-callback-failed", exception));
        }

        private void ReceivePackageCore(ZRpc rpc, ZPackage package)
        {
            if (rpc == null || package == null) return;
            int size;
            try { size = package.Size(); }
            catch { ProtocolViolation(rpc, "unreadable-frame"); return; }
            if (size <= 0 || size > RpcWireCodec.MaximumFrameBytes)
            {
                ProtocolViolation(rpc, "frame-size");
                return;
            }
            byte[] bytes;
            try { bytes = package.GetArray(); }
            catch { ProtocolViolation(rpc, "unreadable-frame"); return; }
            if (!RpcWireCodec.TryDecode(bytes, out RpcWireFrame frame, out string error))
            {
                ProtocolViolation(rpc, error);
                return;
            }

            PeerSession session;
            lock (_gate)
            {
                if (_disposed || !_sessions.TryGetValue(rpc, out session)) return;
                if (!session.Rate.TryAccept(_runtime.UtcNowTicks, bytes.Length))
                {
                    MarkFailedLocked(session, "rpc-rate-limit");
                    return;
                }
                if (!ValidateIncomingSequenceLocked(session, frame))
                {
                    MarkFailedLocked(session, "rpc-replay-or-sequence-gap");
                    return;
                }
            }

            switch (frame.Kind)
            {
                case RpcFrameKind.Challenge:
                    ReceiveChallenge(session, frame);
                    break;
                case RpcFrameKind.Offer:
                    ReceiveOffer(session, frame);
                    break;
                case RpcFrameKind.Accept:
                    ReceiveAccept(session, frame);
                    break;
                case RpcFrameKind.Reject:
                    ReceiveReject(session, frame);
                    break;
                case RpcFrameKind.Request:
                    ReceiveRequest(session, frame);
                    break;
                case RpcFrameKind.Response:
                    ReceiveResponse(session, frame);
                    break;
                case RpcFrameKind.Cancel:
                    ReceiveCancel(session, frame);
                    break;
                default:
                    ProtocolViolation(rpc, "unknown-frame-kind");
                    break;
            }
        }

        private void ReceiveChallenge(PeerSession session, RpcWireFrame frame)
        {
            if (session.LocalIsServer || !RpcHandshakeCodec.TryDecode(frame.Payload, out RpcHandshakeProfile profile) ||
                profile.EchoNonce.Length != 0 ||
                !string.Equals(profile.Hello.Nonce, RpcWireCodec.ToLowerHex(profile.Nonce), StringComparison.Ordinal))
            {
                ProtocolViolation(session.Rpc, "invalid-challenge");
                return;
            }
            RpcWireFrame offer;
            lock (_gate)
            {
                if (session.Handshake.ChallengeReceived)
                {
                    MarkFailedLocked(session, "duplicate-challenge");
                    return;
                }
                session.SessionId = frame.SessionId;
                session.RemoteNonce = profile.Nonce;
                session.RemoteHello = profile.Hello;
                session.CompatibilityAccepted = EvaluateCompatibilityLocked(
                    session, profile.Hello, out string reason);
                session.CompatibilityReason = reason;
                session.Handshake.MarkChallenge(session.CompatibilityAccepted);
                offer = TryCreateOfferLocked(session);
            }
            if (offer != null) SendFrame(session, offer);
            ResumePeerInfoIfReady(session);
        }

        private void ReceiveOffer(PeerSession session, RpcWireFrame frame)
        {
            if (!session.LocalIsServer || !RpcHandshakeCodec.TryDecode(frame.Payload, out RpcHandshakeProfile profile) ||
                !RpcHandshakeCodec.FixedEquals(profile.EchoNonce, session.OwnNonce) ||
                !string.Equals(profile.Hello.Nonce, RpcWireCodec.ToLowerHex(profile.Nonce), StringComparison.Ordinal))
            {
                ProtocolViolation(session.Rpc, "invalid-offer");
                return;
            }

            RpcWireFrame response;
            lock (_gate)
            {
                if (session.OfferReceived)
                {
                    MarkFailedLocked(session, "duplicate-offer");
                    return;
                }
                session.RemoteNonce = profile.Nonce;
                session.RemoteHello = profile.Hello;
                session.OfferReceived = true;
                bool identityCurrent = TryRefreshIdentityLocked(
                    session, out string reason);
                session.CompatibilityAccepted = identityCurrent &&
                    EvaluateCompatibilityLocked(session, profile.Hello, out reason);
                session.CompatibilityReason = reason;
                response = NewFrameLocked(
                    session,
                    session.CompatibilityAccepted ? RpcFrameKind.Accept : RpcFrameKind.Reject);
                response.ReasonCode = session.CompatibilityAccepted ? "compatible" : reason;
                response.Payload = (byte[])profile.Nonce.Clone();
                session.Accepted = session.CompatibilityAccepted;
            }
            SendFrame(session, response);
        }

        private void ReceiveAccept(PeerSession session, RpcWireFrame frame)
        {
            if (session.LocalIsServer || frame.Payload.Length != RpcHandshakeCodec.NonceBytes ||
                !RpcHandshakeCodec.FixedEquals(frame.Payload, session.OwnNonce))
            {
                ProtocolViolation(session.Rpc, "invalid-accept");
                return;
            }
            lock (_gate)
            {
                session.Accepted = true;
                session.Ready = session.Admitted && session.CompatibilityAccepted;
            }
        }

        private void ReceiveReject(PeerSession session, RpcWireFrame frame)
        {
            string reason;
            lock (_gate)
            {
                session.Accepted = false;
                session.CompatibilityAccepted = false;
                session.CompatibilityReason = string.IsNullOrWhiteSpace(frame.ReasonCode)
                    ? "peer-rejected"
                    : frame.ReasonCode;
                MarkFailedLocked(session, session.CompatibilityReason);
                reason = session.CompatibilityReason;
            }
            try { AdmissionRejected?.Invoke(this, new RpcAdmissionRejectedEventArgs(reason)); }
            catch { }
        }

        private void ReceiveRequest(PeerSession session, RpcWireFrame frame)
        {
            EndpointEntry endpoint = null;
            RpcPeerSnapshot peer;
            long received = _runtime.UtcNowTicks;
            long deadline;
            string denial = null;
            lock (_gate)
            {
                if (!session.Ready || !session.Admitted || !IsSessionCurrentLocked(session))
                    denial = "peer-not-ready";
                else if (!TryRefreshIdentityLocked(session, out string identityReason))
                {
                    denial = identityReason;
                    MarkFailedLocked(session, identityReason);
                }
                else if (!_endpoints.TryGetValue(frame.EndpointId, out endpoint) ||
                         !string.Equals(endpoint.Descriptor.ModuleId, frame.ModuleId, StringComparison.Ordinal))
                    denial = "endpoint-not-found";
                else if (!_registry.IsModuleRegistrationActive(endpoint.Module))
                    denial = "endpoint-owner-stale";
                else if (!DirectionAllowsReceive(endpoint.Descriptor, session.LocalIsServer))
                    denial = "endpoint-direction-denied";
                else if (frame.Payload.Length > endpoint.Descriptor.MaximumPayloadBytes)
                    denial = "endpoint-payload-too-large";
                else if (session.Identity == null ||
                         session.Identity.Assurance < endpoint.Descriptor.MinimumIdentityAssurance)
                    denial = "identity-assurance-insufficient";
                else if (!RemoteSupportsEndpoint(session, endpoint.Descriptor))
                    denial = "endpoint-peer-incompatible";
                else if (frame.TimeoutMilliseconds < 100)
                    denial = "request-timeout-invalid";
                else if (endpoint.Descriptor.RequiresIdempotencyKey &&
                         string.IsNullOrWhiteSpace(frame.IdempotencyKey))
                    denial = "idempotency-key-required";
                if (denial != null)
                {
                    var deniedResult = new RpcHandlerResult(
                        denial == "endpoint-not-found" ? RpcResultCode.NotFound : RpcResultCode.Unauthorized,
                        denial);
                    if (endpoint != null)
                        ReportSecurityDenial(
                            endpoint.Module,
                            session,
                            frame,
                            deniedResult,
                            FindingConfidence.High);
                    SendResult(session, frame, deniedResult, false);
                    return;
                }
                peer = SnapshotLocked(session);
                deadline = SaturatingAdd(received, TimeSpan.FromMilliseconds(frame.TimeoutMilliseconds).Ticks);
            }

            string replayKey = null;
            string fingerprint = null;
            if (endpoint.Descriptor.OperationKind == RpcOperationKind.Mutation)
            {
                replayKey = session.SessionId + "|" + frame.ModuleId + "|" + frame.EndpointId + "|" +
                            frame.IdempotencyKey;
                fingerprint = RpcWireCodec.Fingerprint(frame);
                RpcReplayAdmission admission = session.Replay.TryBegin(
                    replayKey,
                    fingerprint,
                    received,
                    out RpcHandlerResult cached);
                if (admission == RpcReplayAdmission.Replay)
                {
                    SendResult(session, frame, cached, true);
                    return;
                }
                if (admission != RpcReplayAdmission.New)
                {
                    RpcResultCode code = admission == RpcReplayAdmission.CapacityReached
                        ? RpcResultCode.CapacityReached
                        : RpcResultCode.ReplayConflict;
                    var replayDenied = new RpcHandlerResult(
                        code,
                        admission == RpcReplayAdmission.InProgress
                            ? "request-in-progress"
                            : admission == RpcReplayAdmission.CapacityReached
                                ? "replay-capacity-reached"
                                : "idempotency-conflict");
                    if (admission == RpcReplayAdmission.Conflict)
                        ReportSecurityDenial(
                            endpoint.Module,
                            session,
                            frame,
                            replayDenied,
                            FindingConfidence.VeryHigh);
                    SendResult(session, frame, replayDenied, false);
                    return;
                }
            }

            RpcHandlerResult result;
            try
            {
                var context = new RpcRequestContext(
                    peer,
                    endpoint.Descriptor,
                    frame.CorrelationId,
                    frame.IdempotencyKey,
                    frame.Payload,
                    received,
                    deadline,
                    session.LocalIsServer,
                    () => IsSessionCurrent(session));
                result = endpoint.Handler(context) ??
                         new RpcHandlerResult(RpcResultCode.HandlerFailed, "handler-returned-null");
                if (result.Payload.Length > endpoint.Descriptor.MaximumPayloadBytes)
                    result = new RpcHandlerResult(RpcResultCode.HandlerFailed, "handler-payload-too-large");
            }
            catch (Exception exception)
            {
                _log.LogWarning(
                    "Runic RPC handler failed for " + frame.EndpointId + ": " +
                    exception.GetType().Name + ": " + exception.Message);
                result = new RpcHandlerResult(RpcResultCode.HandlerFailed, "handler-threw");
            }

            if (result.Code == RpcResultCode.Unauthorized ||
                result.Code == RpcResultCode.ReplayConflict ||
                result.ReasonCode.StartsWith("security-", StringComparison.Ordinal))
                ReportSecurityDenial(
                    endpoint.Module,
                    session,
                    frame,
                    result,
                    result.Code == RpcResultCode.ReplayConflict
                        ? FindingConfidence.VeryHigh
                        : result.ReasonCode.StartsWith("security-", StringComparison.Ordinal)
                            ? FindingConfidence.High
                            : FindingConfidence.Moderate);

            if (replayKey != null)
            {
                if (RpcMutationReplayPolicy.ReleasesReservation(
                        result.Code, endpoint.Descriptor.ReplayDurability))
                {
                    SendResult(session, frame, result, false);
                    session.Replay.Abandon(replayKey, fingerprint);
                    return;
                }
                else
                    session.Replay.Complete(
                        replayKey, fingerprint, result, _runtime.UtcNowTicks);
            }
            SendResult(session, frame, result, false);
        }

        private void ReportSecurityDenial(
            ModuleRegistration provider,
            PeerSession session,
            RpcWireFrame frame,
            RpcHandlerResult result,
            FindingConfidence confidence)
        {
            try
            {
                if (!IsServer || provider == null || session == null || frame == null || result == null ||
                    !_registry.TryGetService(
                        RunicCapabilityIds.SecurityEnforcement,
                        out ISentinelEnforcementService sentinel)) return;
                string actor = session.Identity?.CanonicalKey ?? "unknown-actor";
                sentinel.ReportViolation(
                    provider,
                    session.Peer?.m_uid ?? 0L,
                    actor,
                    result.ReasonCode,
                    string.IsNullOrEmpty(frame.CorrelationId)
                        ? "uncorrelated"
                        : frame.CorrelationId,
                    confidence,
                    frame.ModuleId + "." + frame.EndpointId + ".denied");
            }
            catch
            {
                // Security telemetry cannot turn an already-denied request into an allowed one.
            }
        }

        private void ReceiveResponse(PeerSession session, RpcWireFrame frame)
        {
            PendingRequest pending = null;
            lock (_gate)
            {
                if (_pending.TryGetValue(frame.CorrelationId, out PendingRequest candidate) &&
                    ReferenceEquals(candidate.Session, session) &&
                    string.Equals(candidate.ModuleId, frame.ModuleId, StringComparison.Ordinal) &&
                    string.Equals(candidate.EndpointId, frame.EndpointId, StringComparison.Ordinal))
                {
                    pending = candidate;
                    _pending.Remove(frame.CorrelationId);
                }
            }
            if (pending != null)
                Complete(pending, frame.ResultCode, frame.ReasonCode, frame.Payload, frame.Replayed);
        }

        private void ReceiveCancel(PeerSession session, RpcWireFrame frame)
        {
            // Handlers are synchronous on Valheim's network-update thread. A cancel can prevent a
            // not-yet-dispatched queued request in a future transport version, but it can never
            // roll back a handler that already committed. Durable endpoints own rollback journals.
        }

        private RpcSendResult SendLocked(
            ModuleRegistration module,
            PeerSession session,
            string endpointId,
            byte[] payload,
            string idempotencyKey,
            TimeSpan timeout,
            RunicRpcCompletion completion,
            bool localIsServer)
        {
            if (session == null || !session.Ready || !session.Admitted || !IsSessionCurrentLocked(session))
                return Rejected("peer-not-ready");
            if (!_endpoints.TryGetValue(endpointId ?? string.Empty, out EndpointEntry endpoint))
                return Rejected("endpoint-not-registered");
            if (!_registry.IsModuleRegistrationActive(endpoint.Module))
                return Rejected("endpoint-owner-stale");
            if (!ReferenceEquals(endpoint.Module, module) &&
                !string.Equals(endpoint.Descriptor.ModuleId, module.Descriptor.ModuleId, StringComparison.Ordinal))
                return Rejected("endpoint-module-mismatch");
            if (!DirectionAllowsSend(endpoint.Descriptor, localIsServer))
                return Rejected("endpoint-direction-denied");
            payload = payload ?? Array.Empty<byte>();
            if (payload.Length > endpoint.Descriptor.MaximumPayloadBytes)
                return Rejected("endpoint-payload-too-large");
            if (!TryNormalizeIdempotencyKey(
                    idempotencyKey,
                    endpoint.Descriptor.RequiresIdempotencyKey,
                    out idempotencyKey,
                    out string idempotencyFailure))
                return Rejected(idempotencyFailure);
            if (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromSeconds(60))
                return Rejected("timeout-out-of-range");
            if (!RemoteSupportsEndpoint(session, endpoint.Descriptor))
                return Rejected("endpoint-peer-incompatible");
            int peerPending = _pending.Values.Count(value => ReferenceEquals(value.Session, session));
            if (peerPending >= MaximumPendingRequestsPerPeer)
                return Rejected("pending-capacity-reached");

            string correlationId = Guid.NewGuid().ToString("N");
            long now = _runtime.UtcNowTicks;
            var frame = NewFrameLocked(session, RpcFrameKind.Request);
            frame.CorrelationId = correlationId;
            frame.ModuleId = endpoint.Descriptor.ModuleId;
            frame.EndpointId = endpoint.Descriptor.EndpointId;
            frame.IdempotencyKey = idempotencyKey;
            frame.TimeoutMilliseconds = checked((int)timeout.TotalMilliseconds);
            frame.Payload = (byte[])payload.Clone();
            var pending = new PendingRequest(
                session,
                correlationId,
                frame.ModuleId,
                frame.EndpointId,
                SaturatingAdd(now, timeout.Ticks),
                completion);
            _pending.Add(correlationId, pending);
            try
            {
                SendFrame(session, frame);
            }
            catch
            {
                _pending.Remove(correlationId);
                return Rejected("send-failed");
            }
            return new RpcSendResult(
                true,
                "accepted",
                new RpcRequestHandle(correlationId, CancelPending));
        }

        internal static bool TryNormalizeIdempotencyKey(
            string value,
            bool required,
            out string normalized,
            out string reasonCode)
        {
            return RpcWireCodec.TryValidateIdempotencyKey(
                value,
                required,
                out normalized,
                out reasonCode);
        }

        private void CancelPending(string correlationId)
        {
            PendingRequest pending = null;
            RpcWireFrame cancel = null;
            lock (_gate)
            {
                if (_pending.TryGetValue(correlationId, out pending))
                {
                    _pending.Remove(correlationId);
                    if (IsSessionCurrentLocked(pending.Session))
                    {
                        cancel = NewFrameLocked(pending.Session, RpcFrameKind.Cancel);
                        cancel.CorrelationId = correlationId;
                        cancel.ModuleId = pending.ModuleId;
                        cancel.EndpointId = pending.EndpointId;
                    }
                }
            }
            if (cancel != null) SendFrame(pending.Session, cancel);
            if (pending != null)
                Complete(pending, RpcResultCode.Cancelled, "request-cancelled", null, false);
        }

        private void SendResult(
            PeerSession session,
            RpcWireFrame request,
            RpcHandlerResult result,
            bool replayed)
        {
            RpcWireFrame response;
            lock (_gate)
            {
                if (!IsSessionCurrentLocked(session)) return;
                response = NewFrameLocked(session, RpcFrameKind.Response);
                response.CorrelationId = request.CorrelationId;
                response.ModuleId = request.ModuleId;
                response.EndpointId = request.EndpointId;
                response.ResultCode = result.Code;
                response.ReasonCode = result.ReasonCode;
                response.Payload = result.Payload;
                response.Replayed = replayed;
            }
            SendFrame(session, response);
        }

        private static void SendFrame(PeerSession session, RpcWireFrame frame)
        {
            byte[] encoded = RpcWireCodec.Encode(frame);
            session.Rpc.Invoke(DirectRpcName, new object[] { new ZPackage(encoded) });
        }

        private void ProtocolViolation(ZRpc rpc, string reason)
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(rpc, out PeerSession session))
                {
                    string boundedReason = string.IsNullOrWhiteSpace(reason)
                        ? "protocol-violation"
                        : RunicIdentifier.IsValid(reason) ? reason : "protocol-violation";
                    MarkFailedLocked(session, boundedReason);
                }
            }
        }

        internal void FailClosed(ZRpc rpc, string reasonCode, Exception exception)
        {
            string reason = BoundedReason(reasonCode, "transport-boundary-failed");
            bool tracked = false;
            try
            {
                lock (_gate)
                {
                    if (rpc != null && _sessions.TryGetValue(rpc, out PeerSession session))
                    {
                        MarkFailedLocked(session, reason);
                        tracked = true;
                    }
                }
            }
            catch { }
            if (!tracked)
            {
                try { rpc?.GetSocket()?.Close(); }
                catch { }
            }
            try
            {
                _log.LogWarning(
                    "Runic RPC failed closed at " + reason +
                    (exception == null ? "." : " (" + exception.GetType().Name + ")."));
            }
            catch { }
        }

        private void MarkFailedLocked(PeerSession session, string reasonCode)
        {
            if (session == null || session.Closed) return;
            string reason = BoundedReason(reasonCode, "transport-failed");
            session.CompatibilityAccepted = false;
            session.CompatibilityReason = reason;
            session.Ready = false;
            session.DisconnectReason = reason;
            session.Handshake.Close();
            session.Disconnect.MarkPending(_runtime.UtcNowTicks);
        }

        private bool RejectNewConnection(ZNetPeer peer, string reasonCode)
        {
            try { _log.LogWarning("Runic RPC refused connection: " + BoundedReason(reasonCode, "refused") + "."); }
            catch { }
            try { peer?.Dispose(); }
            catch { }
            return false;
        }

        private static string BoundedReason(string value, string fallback)
        {
            string candidate = string.IsNullOrEmpty(value) ? fallback : value;
            return candidate.Length <= 128 && RunicIdentifier.IsValid(candidate) ? candidate : fallback;
        }

        private void OnConnectionClosed(ZRpc rpc, string reason)
        {
            if (rpc == null) return;
            PeerSession session;
            List<PendingRequest> pending;
            RpcPeerEventArgs disconnected = null;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(rpc, out session)) return;
                _sessions.Remove(rpc);
                if (session.PeerId != 0 && _peers.TryGetValue(session.PeerId, out PeerSession current) &&
                    ReferenceEquals(current, session))
                    _peers.Remove(session.PeerId);
                if (ReferenceEquals(_serverSession, session)) _serverSession = null;
                session.Ready = false;
                session.Closed = true;
                session.Handshake.Close();
                session.Disconnect.MarkClosed();
                pending = _pending.Values.Where(value => ReferenceEquals(value.Session, session)).ToList();
                foreach (PendingRequest request in pending) _pending.Remove(request.CorrelationId);
                if (session.Admitted) disconnected = new RpcPeerEventArgs(SnapshotLocked(session), reason);
            }
            foreach (PendingRequest request in pending)
                Complete(request, RpcResultCode.ConnectionClosed, reason, null, false);
            Raise(PeerDisconnected, disconnected);
        }

        private bool ValidateIncomingSequenceLocked(PeerSession session, RpcWireFrame frame)
        {
            if (frame.Kind == RpcFrameKind.Challenge && !session.LocalIsServer &&
                !session.Handshake.ChallengeReceived && session.SessionId.Length == 0)
            {
                if (frame.Sequence != 1) return false;
                session.IncomingSequence = frame.Sequence;
                return true;
            }
            if (!string.Equals(frame.SessionId, session.SessionId, StringComparison.Ordinal)) return false;
            if (session.IncomingSequence == long.MaxValue || frame.Sequence != session.IncomingSequence + 1)
                return false;
            session.IncomingSequence = frame.Sequence;
            return true;
        }

        private RpcWireFrame NewFrameLocked(PeerSession session, RpcFrameKind kind)
        {
            if (session.OutgoingSequence == long.MaxValue)
                throw new InvalidOperationException("RPC sequence space exhausted.");
            return new RpcWireFrame
            {
                Kind = kind,
                SessionId = session.SessionId,
                Sequence = ++session.OutgoingSequence,
                ResultCode = RpcResultCode.Success,
                Payload = Array.Empty<byte>()
            };
        }

        private RpcWireFrame TryCreateOfferLocked(PeerSession session)
        {
            if (session.LocalIsServer || !session.Handshake.TryTakeOffer())
                return null;
            ProtocolHello hello = BuildLocalHello(session.OwnNonce);
            RpcWireFrame offer = NewFrameLocked(session, RpcFrameKind.Offer);
            offer.Payload = RpcHandshakeCodec.Encode(new RpcHandshakeProfile
            {
                Nonce = session.OwnNonce,
                EchoNonce = session.RemoteNonce,
                Hello = hello
            });
            return offer;
        }

        private void ResumePeerInfoIfReady(PeerSession session)
        {
            string password;
            lock (_gate)
            {
                if (_disposed || session.LocalIsServer ||
                    !session.Handshake.TryTakeQueuedPeerInfo(out password))
                    return;
            }
            try
            {
                _runtime.ResumePeerInfo(session.Rpc, password);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    MarkFailedLocked(session, "peer-info-resume-failed");
                }
                _log.LogWarning(
                    "Runic compatibility could not resume PeerInfo (" +
                    exception.GetType().Name + ").");
            }
        }

        private ProtocolHello BuildLocalHello(byte[] nonce)
        {
            IReadOnlyList<ModuleDescriptor> descriptors = _registry.GetModules();
            if (descriptors.Count > ProtocolHello.MaximumModules)
                throw new InvalidOperationException("Local Runic module inventory exceeds the handshake bound.");
            var modules = descriptors.Select(descriptor => new ModuleProtocolState(
                descriptor.ModuleId,
                descriptor.SemanticVersion.ToString(),
                descriptor.ProtocolVersion.Major,
                descriptor.OwnedCapabilities));
            var claims = new List<RpcHandshakeClaim>();
            foreach (ClaimProviderEntry entry in _claimProviders.Values.OrderBy(value => value.Token))
            {
                if (!_registry.IsModuleRegistrationActive(entry.Owner)) continue;
                IEnumerable<RpcHandshakeClaim> provided = entry.Provider() ??
                    Array.Empty<RpcHandshakeClaim>();
                foreach (RpcHandshakeClaim claim in provided)
                {
                    if (claim == null ||
                        !claim.Key.StartsWith(entry.Owner.Descriptor.ModuleId + ".", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "A handshake claim provider returned a foreign or null claim.");
                    claims.Add(claim);
                }
            }
            return new ProtocolHello(RpcWireCodec.ToLowerHex(nonce), modules, claims);
        }

        private bool EvaluateCompatibilityLocked(
            PeerSession session,
            ProtocolHello remote,
            out string reason)
        {
            foreach (RequirementEntry entry in _requirements.Values.OrderBy(value => value.Token))
            {
                if (!_registry.IsModuleRegistrationActive(entry.Owner)) continue;
                ModuleProtocolState candidate = remote.Modules.FirstOrDefault(module =>
                    string.Equals(module.ModuleId, entry.Requirement.RequiredModuleId, StringComparison.Ordinal));
                if (!entry.Requirement.IsSatisfiedBy(candidate))
                {
                    reason = "incompatible-" + entry.Requirement.RequiredModuleId;
                    return false;
                }
            }
            foreach (ClaimEvaluatorEntry entry in
                     _claimEvaluators.Values.OrderBy(value => value.EvaluatorId, StringComparer.Ordinal))
            {
                if (!_registry.IsModuleRegistrationActive(entry.Owner)) continue;
                RpcHandshakeClaimEvaluation evaluation;
                try { evaluation = entry.Evaluator(remote.Claims); }
                catch
                {
                    reason = "claim-evaluator-failed-" + entry.EvaluatorId;
                    return false;
                }
                if (evaluation == null || !evaluation.Accepted)
                {
                    reason = evaluation == null
                        ? "claim-evaluator-null-" + entry.EvaluatorId
                        : evaluation.ReasonCode;
                    return false;
                }
            }
            foreach (PeerEvaluatorEntry entry in
                     _peerEvaluators.Values.OrderBy(value => value.EvaluatorId, StringComparer.Ordinal))
            {
                if (!_registry.IsModuleRegistrationActive(entry.Owner)) continue;
                RpcHandshakeClaimEvaluation evaluation;
                try
                {
                    if (!TryRefreshIdentityLocked(session, out reason)) return false;
                    evaluation = entry.Evaluator(new RpcHandshakePeerContext(
                        session.Identity,
                        session.SessionId,
                        RpcWireCodec.ToLowerHex(session.OwnNonce),
                        remote.Nonce,
                        remote,
                        session.LocalIsServer,
                        IsSessionCurrentLocked(session)));
                }
                catch
                {
                    reason = "peer-evaluator-failed-" + entry.EvaluatorId;
                    return false;
                }
                if (evaluation == null || !evaluation.Accepted)
                {
                    reason = evaluation == null
                        ? "peer-evaluator-null-" + entry.EvaluatorId
                        : evaluation.ReasonCode;
                    return false;
                }
            }
            reason = "compatible";
            return true;
        }

        private static bool RemoteSupportsEndpoint(PeerSession session, RpcEndpointDescriptor descriptor)
        {
            if (session.RemoteHello == null) return false;
            ModuleProtocolState module = session.RemoteHello.Modules.FirstOrDefault(candidate =>
                string.Equals(candidate.ModuleId, descriptor.ModuleId, StringComparison.Ordinal));
            return module != null && module.ProtocolVersion == descriptor.ProtocolVersion &&
                   module.Capabilities.Contains(descriptor.CapabilityId, StringComparer.Ordinal);
        }

        private static bool DirectionAllowsSend(RpcEndpointDescriptor descriptor, bool localIsServer) =>
            descriptor.Direction == RpcEndpointDirection.Bidirectional ||
            localIsServer && descriptor.Direction == RpcEndpointDirection.ServerToClient ||
            !localIsServer && descriptor.Direction == RpcEndpointDirection.ClientToServer;

        private static bool DirectionAllowsReceive(RpcEndpointDescriptor descriptor, bool localIsServer) =>
            descriptor.Direction == RpcEndpointDirection.Bidirectional ||
            localIsServer && descriptor.Direction == RpcEndpointDirection.ClientToServer ||
            !localIsServer && descriptor.Direction == RpcEndpointDirection.ServerToClient;

        private RpcPeerIdentity ResolveIdentity(PeerSession session)
        {
            ZNetPeer peer = session.Peer;
            if (_runtime.TryResolveIdentity(
                    peer,
                    session.SessionId,
                    session.LocalIsServer,
                    out RpcPeerIdentity injectedIdentity) && injectedIdentity != null)
                return injectedIdentity;
            string connectionSubject = "session-" +
                (string.IsNullOrEmpty(session.SessionId) ? Guid.NewGuid().ToString("N") : session.SessionId);
            if (!session.LocalIsServer)
                return new RpcPeerIdentity(
                    "valheim.server",
                    connectionSubject,
                    RpcIdentityAssurance.ConnectionBound);

            if (peer.m_socket is ZSteamSocket steam &&
                TryResolveAuthenticatedSteamSubject(steam, out string steamSubject))
                return new RpcPeerIdentity(
                    "steam",
                    steamSubject,
                    RpcIdentityAssurance.BackendAccount);

            if (peer.m_socket is ZPlayFabSocket playFab &&
                TryCreateIdentity(
                    "playfab.entity",
                    playFab.m_remotePlayerId,
                    RpcIdentityAssurance.BackendAccount,
                    out RpcPeerIdentity playFabIdentity))
                return playFabIdentity;

            return new RpcPeerIdentity(
                "valheim.transport",
                connectionSubject,
                RpcIdentityAssurance.ConnectionBound);
        }

        /// <summary>
        /// Steam can expose authenticated remote connection information only after
        /// OnNewConnection. Permit the exact live server session to upgrade once from its
        /// generated connection identity to a transport-proven backend account. A proven
        /// backend identity is immutable for that session; a later transport-query failure is
        /// treated as unavailable new evidence rather than as a downgrade.
        /// </summary>
        private bool TryRefreshIdentityLocked(PeerSession session, out string reason)
        {
            reason = "transport-identity-current";
            RpcPeerIdentity resolved;
            try { resolved = ResolveIdentity(session); }
            catch
            {
                reason = "transport-identity-unavailable";
                return false;
            }
            if (session.Identity == null)
            {
                session.Identity = resolved;
                return true;
            }
            if (SameIdentity(session.Identity, resolved)) return true;
            if (session.LocalIsServer &&
                IsExactProvisionalTransportIdentity(session, session.Identity) &&
                resolved.Assurance == RpcIdentityAssurance.BackendAccount)
            {
                session.Identity = resolved;
                return true;
            }
            if (session.LocalIsServer &&
                session.Identity.Assurance == RpcIdentityAssurance.BackendAccount &&
                IsExactProvisionalTransportIdentity(session, resolved))
            {
                // Authentication was already established for this immutable direct session.
                return true;
            }
            reason = "transport-identity-changed";
            return false;
        }

        private static bool SameIdentity(RpcPeerIdentity left, RpcPeerIdentity right) =>
            left != null && right != null &&
            left.Assurance == right.Assurance &&
            string.Equals(left.Authority, right.Authority, StringComparison.Ordinal) &&
            string.Equals(left.SubjectId, right.SubjectId, StringComparison.Ordinal);

        private static bool IsExactProvisionalTransportIdentity(
            PeerSession session,
            RpcPeerIdentity identity) =>
            session != null && identity != null && session.LocalIsServer &&
            identity.Assurance == RpcIdentityAssurance.ConnectionBound &&
            string.Equals(identity.Authority, "valheim.transport", StringComparison.Ordinal) &&
            string.Equals(
                identity.SubjectId,
                "session-" + session.SessionId,
                StringComparison.Ordinal);

        private static bool TryResolveAuthenticatedSteamSubject(
            ZSteamSocket steam,
            out string subject)
        {
            subject = string.Empty;
            if (steam == null || SteamConnectionField == null) return false;
            try
            {
                string hostSubject = steam.GetHostName();
                string peerSubject = steam.GetPeerID().ToString();
                object rawHandle = SteamConnectionField.GetValue(steam);
                if (!(rawHandle is HSteamNetConnection handle) ||
                    !TryGetSteamConnectionInfo(handle, out SteamNetConnectionInfo_t info))
                    return false;
                string remoteSubject = info.m_identityRemote.GetSteamID().ToString();
                return TryClassifyAuthenticatedSteamIdentity(
                    hostSubject,
                    peerSubject,
                    remoteSubject,
                    info.m_nFlags,
                    out subject);
            }
            catch
            {
                subject = string.Empty;
                return false;
            }
        }

        private static bool TryGetSteamConnectionInfo(
            HSteamNetConnection handle,
            out SteamNetConnectionInfo_t info)
        {
            info = default;
            try
            {
                // Steam exposes separate client and game-server API contexts. Calling the
                // client context in a headless dedicated process throws even for a healthy
                // ZSteamSocket, so select the initialized authoritative backend explicitly.
                if (ZNet.instance != null && ZNet.instance.IsDedicated())
                    return SteamGameServerNetworkingSockets.GetConnectionInfo(handle, out info);
                return SteamNetworkingSockets.GetConnectionInfo(handle, out info);
            }
            catch
            {
                info = default;
                return false;
            }
        }

        internal static bool TryClassifyAuthenticatedSteamIdentity(
            string hostSubject,
            string peerSubject,
            string remoteSubject,
            int connectionFlags,
            out string subject)
        {
            subject = string.Empty;
            const int unauthenticatedFlag = 1;
            if ((connectionFlags & unauthenticatedFlag) != 0 ||
                !TryCanonicalNonZeroUInt64(hostSubject, out ulong host) ||
                !TryCanonicalNonZeroUInt64(peerSubject, out ulong peer) ||
                !TryCanonicalNonZeroUInt64(remoteSubject, out ulong remote) ||
                host != peer || peer != remote)
                return false;
            subject = peer.ToString();
            return true;
        }

        private static bool TryCanonicalNonZeroUInt64(string value, out ulong parsed) =>
            ulong.TryParse(value, out parsed) && parsed != 0 &&
            string.Equals(parsed.ToString(), value, StringComparison.Ordinal);

        private static bool TryCreateIdentity(
            string authority,
            string subject,
            RpcIdentityAssurance assurance,
            out RpcPeerIdentity identity)
        {
            identity = null;
            if (string.IsNullOrWhiteSpace(subject)) return false;
            try
            {
                identity = new RpcPeerIdentity(authority, subject, assurance);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is FormatException)
            {
                return false;
            }
        }

        private bool TryResolveTransportOwnedCharacterLocked(
            PeerSession session,
            out ZDOID characterId,
            out long claimedPlayerId,
            out string reason)
        {
            characterId = ZDOID.None;
            claimedPlayerId = 0;
            reason = "actor-character-missing";
            if (_runtime.TryResolveActor(
                    session.Peer,
                    session.PeerId,
                    out characterId,
                    out claimedPlayerId,
                    out reason))
                return true;
            ZDOID announced = session.Peer.m_characterID;
            if (announced.IsNone() || announced.ID == 0 || announced.UserID == 0)
                return false;

            // A dedicated server does not instantiate a Unity Player object for a remote
            // client, so Player.GetAllPlayers() cannot establish remote actor authority.  The
            // synchronized Player ZDO is present on the server and is the stronger boundary:
            // resolve only the exact character ID attached to this direct ZNetPeer, require its
            // current owner to be this peer UID, require the registered vanilla Player prefab,
            // and read Player.GetPlayerID's exact ZDO key.  AccountBoundPlayer additionally
            // verifies that client-authored player ID through the persisted reverse binding.
            try
            {
                if (ZDOMan.instance == null || ZNetScene.instance == null)
                {
                    reason = "actor-world-unavailable";
                    return false;
                }
                ZDO zdo = ZDOMan.instance.GetZDO(announced);
                if (zdo == null)
                {
                    reason = "actor-character-missing";
                    return false;
                }
                int prefabHash = zdo.GetPrefab();
                UnityEngine.GameObject prefab = prefabHash == 0
                    ? null
                    : ZNetScene.instance.GetPrefab(prefabHash);
                bool registeredPlayerPrefab =
                    prefabHash == "Player".GetStableHashCode() &&
                    prefab != null &&
                    string.Equals(prefab.name, "Player", StringComparison.Ordinal) &&
                    prefab.GetComponent<Player>() != null;
                return TryClassifyTransportOwnedCharacterEvidence(
                    announced,
                    zdo.m_uid,
                    session.PeerId,
                    zdo.GetOwner(),
                    zdo.IsValid(),
                    registeredPlayerPrefab,
                    zdo.GetLong(ZDOVars.s_playerID, 0L),
                    out characterId,
                    out claimedPlayerId,
                    out reason);
            }
            catch
            {
                reason = "actor-character-evidence-unavailable";
                return false;
            }
        }

        internal static bool TryClassifyTransportOwnedCharacterEvidence(
            ZDOID announced,
            ZDOID actual,
            long expectedOwner,
            long actualOwner,
            bool valid,
            bool registeredPlayerPrefab,
            long playerId,
            out ZDOID characterId,
            out long claimedPlayerId,
            out string reason)
        {
            characterId = ZDOID.None;
            claimedPlayerId = 0;
            if (announced.IsNone() || announced.ID == 0 || announced.UserID == 0)
            {
                reason = "actor-character-missing";
                return false;
            }
            if (actual != announced)
            {
                reason = "actor-character-id-mismatch";
                return false;
            }
            if (!valid)
            {
                reason = "actor-character-invalid";
                return false;
            }
            if (expectedOwner == 0 || actualOwner != expectedOwner)
            {
                reason = "actor-character-owner-mismatch";
                return false;
            }
            if (!registeredPlayerPrefab)
            {
                reason = "actor-character-prefab-mismatch";
                return false;
            }
            if (playerId == 0)
            {
                reason = "actor-player-id-missing";
                return false;
            }
            characterId = announced;
            claimedPlayerId = playerId;
            reason = "actor-transport-owned";
            return true;
        }

        private static string BindingFailureReason(RpcPlayerBindingStatus status)
        {
            switch (status)
            {
                case RpcPlayerBindingStatus.Missing: return "actor-binding-missing";
                case RpcPlayerBindingStatus.Ambiguous: return "actor-binding-ambiguous";
                case RpcPlayerBindingStatus.Stale: return "actor-binding-stale";
                case RpcPlayerBindingStatus.Conflict: return "actor-binding-conflict";
                default: return "actor-binding-failed";
            }
        }

        private bool IsServerAdminLocked(PeerSession session)
        {
            if (session == null || !session.LocalIsServer ||
                !IsSessionCurrentLocked(session) || session.Identity == null)
                return false;
            if (_registry.TryGetService(
                    RunicCapabilityIds.SecurityRoles,
                    out ISentinelRoleService signedRoles) &&
                signedRoles.PolicyReady &&
                signedRoles.IsAdministrator(
                    session.Identity.Authority,
                    session.Identity.SubjectId))
                return true;
            if (!IsEligibleSteamAdminIdentity(session.Identity)) return false;
            if (_runtime.TryIsAdmin(session.Peer, out bool injected)) return injected;
            if (!(session.Peer?.m_socket is ZSteamSocket steam) ||
                !TryResolveAuthenticatedSteamSubject(steam, out string transportSubject) ||
                !SteamAdminIdentityMatchesTransport(session.Identity, transportSubject) ||
                ZNet.instance == null)
                return false;
            try
            {
                return ZNet.instance.IsAdmin(session.Identity.SubjectId);
            }
            catch { return false; }
        }

        internal static bool IsEligibleSteamAdminIdentity(RpcPeerIdentity identity) =>
            identity != null && identity.Assurance == RpcIdentityAssurance.BackendAccount &&
            string.Equals(identity.Authority, "steam", StringComparison.Ordinal) &&
            TryCanonicalNonZeroUInt64(identity.SubjectId, out _);

        internal static bool SteamAdminIdentityMatchesTransport(
            RpcPeerIdentity identity,
            string transportSubject) =>
            IsEligibleSteamAdminIdentity(identity) &&
            TryCanonicalNonZeroUInt64(transportSubject, out _) &&
            string.Equals(identity.SubjectId, transportSubject, StringComparison.Ordinal);

        private bool IsSessionCurrent(PeerSession session)
        {
            lock (_gate) return IsSessionCurrentLocked(session);
        }

        private bool IsSessionCurrentLocked(PeerSession session) =>
            !_disposed && session != null && !session.Closed && session.Rpc != null &&
            _sessions.TryGetValue(session.Rpc, out PeerSession current) && ReferenceEquals(current, session) &&
            session.Rpc.IsConnected();

        private RpcPeerSnapshot SnapshotLocked(PeerSession session) => new RpcPeerSnapshot(
            session.PeerId,
            session.SessionId,
            session.Identity,
            session.Ready,
            IsSessionCurrentLocked(session),
            session.RemoteHello,
            session.ConnectedUtcTicks);

        private bool HasRpcNameCollision(ZRpc rpc)
        {
            if (RpcFunctionsField == null) return true;
            try
            {
                var functions = RpcFunctionsField.GetValue(rpc) as IDictionary;
                return functions == null || functions.Contains(DirectRpcName.GetStableHashCode());
            }
            catch { return true; }
        }

        private void ValidateModuleLease(ModuleRegistration module)
        {
            if (module == null || !_registry.IsModuleRegistrationActive(module))
                throw new InvalidOperationException("An active module registration lease is required.");
        }

        private long NextTokenLocked()
        {
            if (_nextRegistrationToken == long.MaxValue)
                throw new InvalidOperationException("RPC registration token space exhausted.");
            return ++_nextRegistrationToken;
        }

        private bool UnregisterEndpoint(string endpointId, long token)
        {
            lock (_gate)
            {
                if (!_endpoints.TryGetValue(endpointId, out EndpointEntry entry) || entry.Token != token)
                    return false;
                _endpoints.Remove(endpointId);
                return true;
            }
        }

        private bool UnregisterRequirement(long token)
        {
            lock (_gate) return _requirements.Remove(token);
        }

        private bool UnregisterPlayerBinding(long token)
        {
            lock (_gate)
            {
                if (_playerBinding == null || _playerBinding.Token != token) return false;
                _playerBinding = null;
                return true;
            }
        }

        private bool UnregisterClaimProvider(long token)
        {
            lock (_gate) return _claimProviders.Remove(token);
        }

        private bool UnregisterClaimEvaluator(string evaluatorId, long token)
        {
            lock (_gate)
            {
                if (!_claimEvaluators.TryGetValue(evaluatorId, out ClaimEvaluatorEntry entry) ||
                    entry.Token != token)
                    return false;
                _claimEvaluators.Remove(evaluatorId);
                return true;
            }
        }

        private bool UnregisterPeerEvaluator(string evaluatorId, long token)
        {
            lock (_gate)
            {
                if (!_peerEvaluators.TryGetValue(
                        evaluatorId, out PeerEvaluatorEntry entry) || entry.Token != token)
                    return false;
                _peerEvaluators.Remove(evaluatorId);
                return true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RunicRpcService));
        }

        private static RpcSendResult Rejected(string reason) =>
            new RpcSendResult(false, reason, null);

        private static void Complete(
            PendingRequest request,
            RpcResultCode code,
            string reason,
            byte[] payload,
            bool replayed)
        {
            if (request?.Completion == null) return;
            try
            {
                request.Completion(new RpcResponse(
                    request.CorrelationId,
                    code,
                    reason,
                    payload,
                    replayed));
            }
            catch { }
        }

        private static void Raise(EventHandler<RpcPeerEventArgs> handlers, RpcPeerEventArgs arguments)
        {
            if (handlers == null || arguments == null) return;
            foreach (EventHandler<RpcPeerEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(null, arguments); }
                catch { }
            }
        }

        private static long SaturatingAdd(long left, long right) =>
            left > DateTime.MaxValue.Ticks - right ? DateTime.MaxValue.Ticks : left + right;

        private sealed class PeerSession
        {
            internal PeerSession(ZNetPeer peer, bool localIsServer)
            {
                Peer = peer;
                Rpc = peer.m_rpc;
                LocalIsServer = localIsServer;
                Rate = new RpcRateWindow();
                Replay = new RpcReplayLedger(256);
                Handshake = new RpcHandshakeGate();
                Disconnect = new RpcDisconnectRetryGate(
                    MaximumDisconnectAttempts,
                    TimeSpan.FromMilliseconds(DisconnectRetryMilliseconds).Ticks);
            }

            internal readonly ZNetPeer Peer;
            internal readonly ZRpc Rpc;
            internal readonly bool LocalIsServer;
            internal readonly RpcRateWindow Rate;
            internal readonly RpcReplayLedger Replay;
            internal readonly RpcHandshakeGate Handshake;
            internal readonly RpcDisconnectRetryGate Disconnect;
            internal string SessionId;
            internal byte[] OwnNonce;
            internal byte[] RemoteNonce = Array.Empty<byte>();
            internal ProtocolHello RemoteHello;
            internal RpcPeerIdentity Identity;
            internal long PeerId;
            internal long ConnectedUtcTicks;
            internal long HandshakeDeadlineUtcTicks;
            internal long IncomingSequence;
            internal long OutgoingSequence;
            internal bool ChallengeSent;
            internal bool OfferReceived;
            internal bool CompatibilityAccepted;
            internal string CompatibilityReason = string.Empty;
            internal bool Accepted;
            internal bool Admitted;
            internal bool Ready;
            internal bool Closed;
            internal string DisconnectReason = string.Empty;
        }

        private sealed class EndpointEntry
        {
            internal EndpointEntry(
                ModuleRegistration module,
                RpcEndpointDescriptor descriptor,
                RunicRpcHandler handler,
                long token)
            {
                Module = module;
                Descriptor = descriptor;
                Handler = handler;
                Token = token;
            }

            internal readonly ModuleRegistration Module;
            internal readonly RpcEndpointDescriptor Descriptor;
            internal readonly RunicRpcHandler Handler;
            internal readonly long Token;
        }

        private sealed class RequirementEntry
        {
            internal RequirementEntry(ModuleRegistration owner, RpcPeerRequirement requirement, long token)
            {
                Owner = owner;
                Requirement = requirement;
                Token = token;
            }

            internal readonly ModuleRegistration Owner;
            internal readonly RpcPeerRequirement Requirement;
            internal readonly long Token;
        }

        private sealed class PlayerBindingEntry
        {
            internal PlayerBindingEntry(
                ModuleRegistration owner,
                IRpcPlayerBindingResolver resolver,
                long token)
            {
                Owner = owner;
                Resolver = resolver;
                Token = token;
            }

            internal readonly ModuleRegistration Owner;
            internal readonly IRpcPlayerBindingResolver Resolver;
            internal readonly long Token;
        }

        private sealed class ClaimProviderEntry
        {
            internal ClaimProviderEntry(
                ModuleRegistration owner,
                RunicHandshakeClaimProvider provider,
                long token)
            {
                Owner = owner;
                Provider = provider;
                Token = token;
            }

            internal readonly ModuleRegistration Owner;
            internal readonly RunicHandshakeClaimProvider Provider;
            internal readonly long Token;
        }

        private sealed class ClaimEvaluatorEntry
        {
            internal ClaimEvaluatorEntry(
                ModuleRegistration owner,
                string evaluatorId,
                RunicHandshakeClaimEvaluator evaluator,
                long token)
            {
                Owner = owner;
                EvaluatorId = evaluatorId;
                Evaluator = evaluator;
                Token = token;
            }

            internal readonly ModuleRegistration Owner;
            internal readonly string EvaluatorId;
            internal readonly RunicHandshakeClaimEvaluator Evaluator;
            internal readonly long Token;
        }

        private sealed class PeerEvaluatorEntry
        {
            internal PeerEvaluatorEntry(
                ModuleRegistration owner,
                string evaluatorId,
                RunicHandshakePeerEvaluator evaluator,
                long token)
            {
                Owner = owner;
                EvaluatorId = evaluatorId;
                Evaluator = evaluator;
                Token = token;
            }

            internal readonly ModuleRegistration Owner;
            internal readonly string EvaluatorId;
            internal readonly RunicHandshakePeerEvaluator Evaluator;
            internal readonly long Token;
        }

        private sealed class PendingRequest
        {
            internal PendingRequest(
                PeerSession session,
                string correlationId,
                string moduleId,
                string endpointId,
                long deadlineUtcTicks,
                RunicRpcCompletion completion)
            {
                Session = session;
                CorrelationId = correlationId;
                ModuleId = moduleId;
                EndpointId = endpointId;
                DeadlineUtcTicks = deadlineUtcTicks;
                Completion = completion;
            }

            internal readonly PeerSession Session;
            internal readonly string CorrelationId;
            internal readonly string ModuleId;
            internal readonly string EndpointId;
            internal readonly long DeadlineUtcTicks;
            internal readonly RunicRpcCompletion Completion;
        }

        private sealed class RpcRateWindow
        {
            private readonly Queue<RateEntry> _entries = new Queue<RateEntry>();
            private int _bytes;

            internal bool TryAccept(long nowUtcTicks, int bytes)
            {
                long cutoff = nowUtcTicks - TimeSpan.TicksPerSecond;
                while (_entries.Count > 0 && _entries.Peek().UtcTicks <= cutoff)
                    _bytes -= _entries.Dequeue().Bytes;
                if (_entries.Count >= MaximumFramesPerSecond ||
                    bytes < 0 || _bytes > MaximumBytesPerSecond - bytes)
                    return false;
                _entries.Enqueue(new RateEntry(nowUtcTicks, bytes));
                _bytes += bytes;
                return true;
            }
        }

        private readonly struct RateEntry
        {
            internal RateEntry(long utcTicks, int bytes)
            {
                UtcTicks = utcTicks;
                Bytes = bytes;
            }
            internal readonly long UtcTicks;
            internal readonly int Bytes;
        }

        private sealed class RegistrationLease : IDisposable
        {
            private Action _release;
            internal RegistrationLease(Action release) { _release = release; }
            public void Dispose() { System.Threading.Interlocked.Exchange(ref _release, null)?.Invoke(); }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T left, T right) => ReferenceEquals(left, right);
            public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
