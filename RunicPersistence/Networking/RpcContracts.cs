using System;
using System.Collections.Generic;
using Runic.Foundation.Core;

namespace Runic.Foundation.Persistence
{
    /// <summary>
    /// Strength of the identity that the receiving server can bind to the current connection.
    /// This describes transport/account binding only; client process, plugin, inventory, and
    /// response validation remain separate responsibilities.
    /// </summary>
    public enum RpcIdentityAssurance
    {
        None = 0,
        ConnectionBound = 1,
        BackendAccount = 2
    }

    /// <summary>
    /// TransportOwnedCharacter requires a current server-side Player ZDO whose owner is the
    /// request's direct peer UID. AccountBoundPlayer additionally requires a server-owned,
    /// reverse-unique persisted binding between the backend account and the Player ZDO's claimed
    /// Valheim player ID. The latter fails closed when no binding resolver is installed.
    /// </summary>
    public enum RpcActorAssurance
    {
        TransportOwnedCharacter = 1,
        AccountBoundPlayer = 2
    }

    public enum RpcPlayerBindingStatus
    {
        Verified = 0,
        Missing = 1,
        Ambiguous = 2,
        Stale = 3,
        Conflict = 4
    }

    public interface IRpcPlayerBindingResolver
    {
        /// <summary>
        /// Verifies a persisted bijection in both directions. The player ID is read from the
        /// transport-owned Player ZDO but remains client-originated and must not be trusted unless
        /// this method returns Verified. Implementations must never auto-enrol inside this call.
        /// </summary>
        RpcPlayerBindingStatus Resolve(RpcPeerIdentity identity, long claimedPlayerId);
    }

    public enum RpcEndpointDirection
    {
        ClientToServer = 0,
        ServerToClient = 1,
        Bidirectional = 2
    }

    public enum RpcOperationKind
    {
        ReadOnly = 0,
        Mutation = 1
    }

    /// <summary>
    /// SessionOnly means replay safety ends when the server session ends. HandlerDurable means
    /// the endpoint handler owns a persistent operation/idempotency journal and must consult it
    /// before every commit after reconnect or restart.
    /// </summary>
    public enum RpcReplayDurability
    {
        SessionOnly = 0,
        HandlerDurable = 1
    }

    public enum RpcResultCode
    {
        Success = 0,
        InvalidRequest = 1,
        /// <summary>
        /// The handler made no durable admission and performed no mutation. An exact mutation
        /// retry may re-enter the handler after the response has been sent.
        /// </summary>
        NotReady = 2,
        IncompatiblePeer = 3,
        Unauthorized = 4,
        /// <summary>
        /// The handler made no durable admission and performed no mutation. An exact mutation
        /// retry may re-enter the handler after the response has been sent.
        /// </summary>
        RateLimited = 5,
        /// <summary>
        /// The handler made no durable admission and performed no mutation. An exact mutation
        /// retry may re-enter the handler after the response has been sent.
        /// </summary>
        CapacityReached = 6,
        ReplayConflict = 7,
        /// <summary>
        /// The authoritative deadline expired before durable admission or mutation. An exact
        /// mutation retry may re-enter the handler after the response has been sent.
        /// </summary>
        TimedOut = 8,
        Cancelled = 9,
        /// <summary>
        /// SessionOnly handlers treat this as terminal because an exception can follow an
        /// unjournaled partial mutation. HandlerDurable handlers may re-enter because their
        /// contract requires persistent idempotency to be consulted before every commit.
        /// </summary>
        HandlerFailed = 10,
        ConnectionClosed = 11,
        ProtocolViolation = 12,
        NotFound = 13,
        /// <summary>
        /// The authoritative receiver durably admitted a bounded asynchronous operation. The
        /// response payload is the operation token; final state is obtained through a separate
        /// read-only status endpoint. It is not a claim that the mutation has completed.
        /// </summary>
        Accepted = 14
    }

    /// <summary>
    /// Defines when a mutation response releases its exact per-session replay reservation.
    /// Codes classified here as transient are a handler assertion that no unjournaled admission
    /// or mutation occurred. Returning one after an unjournaled side effect violates the RPC
    /// endpoint contract.
    /// </summary>
    public static class RpcMutationReplayPolicy
    {
        public static bool ReleasesReservation(
            RpcResultCode code,
            RpcReplayDurability durability)
        {
            if (code == RpcResultCode.NotReady ||
                code == RpcResultCode.RateLimited ||
                code == RpcResultCode.CapacityReached ||
                code == RpcResultCode.TimedOut)
                return true;
            return code == RpcResultCode.HandlerFailed &&
                   durability == RpcReplayDurability.HandlerDurable;
        }
    }

    public sealed class RpcPeerIdentity
    {
        public const int MaximumAuthorityLength = 64;
        public const int MaximumSubjectLength = 256;
        private static readonly System.Text.UTF8Encoding StrictUtf8 =
            new System.Text.UTF8Encoding(false, true);

        public RpcPeerIdentity(
            string authority,
            string subjectId,
            RpcIdentityAssurance assurance)
        {
            Authority = RequireAtom(authority, MaximumAuthorityLength, nameof(authority));
            SubjectId = RequireSafeText(subjectId, MaximumSubjectLength, nameof(subjectId));
            if (!Enum.IsDefined(typeof(RpcIdentityAssurance), assurance))
                throw new ArgumentOutOfRangeException(nameof(assurance));
            Assurance = assurance;
        }

        public string Authority { get; }
        public string SubjectId { get; }
        public RpcIdentityAssurance Assurance { get; }
        public string CanonicalKey => Authority + ":" + Uri.EscapeDataString(SubjectId);

        private static string RequireAtom(string value, int maximum, string name)
        {
            string exact = value ?? string.Empty;
            if (exact.Length == 0 || exact.Length > maximum)
                throw new ArgumentOutOfRangeException(name);
            for (int index = 0; index < exact.Length; index++)
            {
                char character = exact[index];
                bool valid = character >= 'a' && character <= 'z' ||
                             character >= '0' && character <= '9' ||
                             character == '.' || character == '-' || character == '_';
                if (!valid) throw new ArgumentException("Identity authority is not canonical.", name);
            }
            return exact;
        }

        private static string RequireSafeText(string value, int maximum, string name)
        {
            string exact = value ?? string.Empty;
            if (exact.Length == 0 || exact.Length > maximum)
                throw new ArgumentOutOfRangeException(name);
            if (char.IsWhiteSpace(exact[0]) || char.IsWhiteSpace(exact[exact.Length - 1]))
                throw new ArgumentException(
                    "Identity subject must not have leading or trailing whitespace.",
                    name);
            for (int index = 0; index < exact.Length; index++)
                if (char.IsControl(exact[index]))
                    throw new ArgumentException("Identity subject contains a control character.", name);
            try { StrictUtf8.GetByteCount(exact); }
            catch (System.Text.EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Identity subject contains an unpaired UTF-16 surrogate.",
                    name,
                    exception);
            }
            return exact;
        }
    }

    public sealed class RpcEndpointDescriptor
    {
        public const int DefaultMaximumPayloadBytes = 64 * 1024;
        public const int HardMaximumPayloadBytes = 128 * 1024;

        public RpcEndpointDescriptor(
            string moduleId,
            string endpointId,
            string capabilityId,
            int protocolVersion,
            RpcEndpointDirection direction,
            RpcOperationKind operationKind,
            RpcReplayDurability replayDurability = RpcReplayDurability.SessionOnly,
            RpcIdentityAssurance minimumIdentityAssurance = RpcIdentityAssurance.BackendAccount,
            int maximumPayloadBytes = DefaultMaximumPayloadBytes)
        {
            ModuleId = RunicIdentifier.Require(moduleId, nameof(moduleId));
            EndpointId = RunicIdentifier.Require(endpointId, nameof(endpointId));
            CapabilityId = RunicIdentifier.Require(capabilityId, nameof(capabilityId));
            if (!EndpointId.StartsWith(ModuleId + ".", StringComparison.Ordinal))
                throw new ArgumentException("Endpoint ID must be namespaced by its module ID.", nameof(endpointId));
            if (protocolVersion < 1) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            if (!Enum.IsDefined(typeof(RpcEndpointDirection), direction))
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (!Enum.IsDefined(typeof(RpcOperationKind), operationKind))
                throw new ArgumentOutOfRangeException(nameof(operationKind));
            if (!Enum.IsDefined(typeof(RpcReplayDurability), replayDurability))
                throw new ArgumentOutOfRangeException(nameof(replayDurability));
            if (!Enum.IsDefined(typeof(RpcIdentityAssurance), minimumIdentityAssurance))
                throw new ArgumentOutOfRangeException(nameof(minimumIdentityAssurance));
            if (maximumPayloadBytes < 0 || maximumPayloadBytes > HardMaximumPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));

            ProtocolVersion = protocolVersion;
            Direction = direction;
            OperationKind = operationKind;
            ReplayDurability = replayDurability;
            MinimumIdentityAssurance = minimumIdentityAssurance;
            MaximumPayloadBytes = maximumPayloadBytes;
        }

        public string ModuleId { get; }
        public string EndpointId { get; }
        public string CapabilityId { get; }
        public int ProtocolVersion { get; }
        public RpcEndpointDirection Direction { get; }
        public RpcOperationKind OperationKind { get; }
        public RpcReplayDurability ReplayDurability { get; }
        public RpcIdentityAssurance MinimumIdentityAssurance { get; }
        public int MaximumPayloadBytes { get; }
        public bool RequiresIdempotencyKey => OperationKind == RpcOperationKind.Mutation;
    }

    /// <summary>
    /// A server-owned join requirement. It is evaluated against the remote profile before the
    /// server runs vanilla RPC_PeerInfo. It detects installation and version mismatches reported
    /// by the remote profile; it is not an anti-cheat boundary.
    /// </summary>
    public sealed class RpcPeerRequirement
    {
        public RpcPeerRequirement(
            string requiredModuleId,
            string minimumSemanticVersion,
            string maximumSemanticVersion,
            int minimumProtocol,
            int maximumProtocol)
        {
            RequiredModuleId = RunicIdentifier.Require(requiredModuleId, nameof(requiredModuleId));
            MinimumSemanticVersion = SemanticVersion.Parse(minimumSemanticVersion);
            MaximumSemanticVersion = SemanticVersion.Parse(maximumSemanticVersion);
            if (MaximumSemanticVersion.CompareTo(MinimumSemanticVersion) < 0)
                throw new ArgumentException("Maximum semantic version precedes minimum semantic version.");
            if (minimumProtocol < 1 || maximumProtocol < minimumProtocol)
                throw new ArgumentOutOfRangeException(nameof(minimumProtocol));
            MinimumProtocol = minimumProtocol;
            MaximumProtocol = maximumProtocol;
        }

        public string RequiredModuleId { get; }
        public SemanticVersion MinimumSemanticVersion { get; }
        public SemanticVersion MaximumSemanticVersion { get; }
        public int MinimumProtocol { get; }
        public int MaximumProtocol { get; }

        public bool IsSatisfiedBy(ModuleProtocolState module)
        {
            if (module == null ||
                !string.Equals(module.ModuleId, RequiredModuleId, StringComparison.Ordinal) ||
                module.ProtocolVersion < MinimumProtocol || module.ProtocolVersion > MaximumProtocol)
                return false;
            try
            {
                SemanticVersion version = SemanticVersion.Parse(module.SemanticVersion);
                return version.CompareTo(MinimumSemanticVersion) >= 0 &&
                       version.CompareTo(MaximumSemanticVersion) <= 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is FormatException)
            {
                return false;
            }
        }
    }

    public sealed class RpcPeerSnapshot
    {
        internal RpcPeerSnapshot(
            long peerId,
            string sessionId,
            RpcPeerIdentity identity,
            bool ready,
            bool current,
            ProtocolHello remoteHello,
            long connectedUtcTicks)
        {
            PeerId = peerId;
            SessionId = sessionId ?? string.Empty;
            Identity = identity;
            Ready = ready;
            Current = current;
            RemoteHello = remoteHello;
            ConnectedUtcTicks = connectedUtcTicks;
        }

        public long PeerId { get; }
        public string SessionId { get; }
        public RpcPeerIdentity Identity { get; }
        public bool Ready { get; }
        public bool Current { get; }
        public ProtocolHello RemoteHello { get; }
        public long ConnectedUtcTicks { get; }
    }

    public sealed class RpcActorSnapshot
    {
        internal RpcActorSnapshot(
            RpcPeerSnapshot peer,
            ZDOID characterId,
            long claimedPlayerId,
            RpcActorAssurance assurance,
            bool isServerAdmin)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            CharacterId = characterId;
            ClaimedPlayerId = claimedPlayerId;
            Assurance = assurance;
            IsServerAdmin = isServerAdmin;
        }

        public RpcPeerSnapshot Peer { get; }
        public ZDOID CharacterId { get; }
        /// <summary>
        /// Client-originated unless Assurance is AccountBoundPlayer. Never use this value for a
        /// persistent owner/group/admin decision at TransportOwnedCharacter assurance.
        /// </summary>
        public long ClaimedPlayerId { get; }
        public RpcActorAssurance Assurance { get; }
        public bool HasVerifiedPlayerBinding => Assurance == RpcActorAssurance.AccountBoundPlayer;
        /// <summary>
        /// Evaluated by the receiving server against the exact current session socket. False on
        /// clients, stale sessions, lookup exceptions, and unsupported identities.
        /// </summary>
        public bool IsServerAdmin { get; }
    }

    public sealed class RpcRequestContext
    {
        private readonly byte[] _payload;
        private readonly Func<bool> _isCurrent;

        internal RpcRequestContext(
            RpcPeerSnapshot peer,
            RpcEndpointDescriptor endpoint,
            string correlationId,
            string idempotencyKey,
            byte[] payload,
            long receivedUtcTicks,
            long deadlineUtcTicks,
            bool receiverIsServer,
            Func<bool> isCurrent)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
            IdempotencyKey = idempotencyKey ?? string.Empty;
            _payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
            ReceivedUtcTicks = receivedUtcTicks;
            DeadlineUtcTicks = deadlineUtcTicks;
            ReceiverIsServer = receiverIsServer;
            _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        }

        public RpcPeerSnapshot Peer { get; }
        public RpcEndpointDescriptor Endpoint { get; }
        public string CorrelationId { get; }
        public string IdempotencyKey { get; }
        public byte[] Payload => (byte[])_payload.Clone();
        public long ReceivedUtcTicks { get; }
        public long DeadlineUtcTicks { get; }
        public bool ReceiverIsServer { get; }
        public bool IsConnectionCurrent => _isCurrent();
    }

    public sealed class RpcHandlerResult
    {
        private readonly byte[] _payload;

        public RpcHandlerResult(RpcResultCode code, string reasonCode, byte[] payload = null)
        {
            if (!Enum.IsDefined(typeof(RpcResultCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            ReasonCode = RequireReason(reasonCode, code);
            if (payload != null && payload.Length > RpcEndpointDescriptor.HardMaximumPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload));
            _payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
        }

        public RpcResultCode Code { get; }
        public string ReasonCode { get; }
        public byte[] Payload => (byte[])_payload.Clone();
        public bool Success => Code == RpcResultCode.Success;
        public bool OperationAccepted => Code == RpcResultCode.Accepted;

        public static RpcHandlerResult Ok(byte[] payload = null) =>
            new RpcHandlerResult(RpcResultCode.Success, "ok", payload);

        public static RpcHandlerResult Deny(string reasonCode) =>
            new RpcHandlerResult(RpcResultCode.Unauthorized, reasonCode);

        /// <summary>
        /// Returns a replay-cached admission result for work that must continue outside the ZRpc
        /// callback. A HandlerDurable endpoint must persist the operation token and intent before
        /// returning this value; clients query final state through a separate read-only endpoint.
        /// </summary>
        public static RpcHandlerResult Accept(byte[] operationToken) =>
            operationToken == null || operationToken.Length == 0
                ? throw new ArgumentException("An accepted operation requires a bounded token.", nameof(operationToken))
                : new RpcHandlerResult(RpcResultCode.Accepted, "operation-accepted", operationToken);

        private static string RequireReason(string value, RpcResultCode code)
        {
            string exact = string.IsNullOrEmpty(value)
                ? code.ToString().ToLowerInvariant()
                : value;
            return RunicIdentifier.Require(exact, nameof(value));
        }
    }

    public delegate RpcHandlerResult RunicRpcHandler(RpcRequestContext request);
    public delegate void RunicRpcCompletion(RpcResponse response);

    public sealed class RpcResponse
    {
        private readonly byte[] _payload;

        internal RpcResponse(
            string correlationId,
            RpcResultCode code,
            string reasonCode,
            byte[] payload,
            bool replayed)
        {
            CorrelationId = correlationId ?? string.Empty;
            Code = code;
            ReasonCode = reasonCode ?? string.Empty;
            _payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
            Replayed = replayed;
        }

        public string CorrelationId { get; }
        public RpcResultCode Code { get; }
        public string ReasonCode { get; }
        public byte[] Payload => (byte[])_payload.Clone();
        public bool Replayed { get; }
        public bool Success => Code == RpcResultCode.Success;
        public bool OperationAccepted => Code == RpcResultCode.Accepted;
    }

    public sealed class RpcRequestHandle : IDisposable
    {
        private readonly Action<string> _cancel;
        private bool _disposed;

        internal RpcRequestHandle(string correlationId, Action<string> cancel)
        {
            CorrelationId = correlationId;
            _cancel = cancel;
        }

        public string CorrelationId { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cancel?.Invoke(CorrelationId);
        }
    }

    public sealed class RpcPeerEventArgs : EventArgs
    {
        internal RpcPeerEventArgs(RpcPeerSnapshot peer, string reasonCode)
        {
            Peer = peer;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public RpcPeerSnapshot Peer { get; }
        public string ReasonCode { get; }
    }

    public sealed class RpcSendResult
    {
        internal RpcSendResult(bool accepted, string reasonCode, RpcRequestHandle handle)
        {
            Accepted = accepted;
            ReasonCode = reasonCode ?? string.Empty;
            Handle = handle;
        }

        public bool Accepted { get; }
        public string ReasonCode { get; }
        public RpcRequestHandle Handle { get; }
    }

    public interface IRunicRpcService
    {
        bool IsServer { get; }
        bool IsServerConnectionReady { get; }
        event EventHandler<RpcPeerEventArgs> PeerReady;
        event EventHandler<RpcPeerEventArgs> PeerDisconnected;

        IDisposable RegisterEndpoint(
            ModuleRegistration module,
            RpcEndpointDescriptor descriptor,
            RunicRpcHandler handler);

        IDisposable RegisterPeerRequirement(
            ModuleRegistration module,
            RpcPeerRequirement requirement);

        IDisposable RegisterPlayerBindingResolver(
            ModuleRegistration module,
            IRpcPlayerBindingResolver resolver);

        IDisposable RegisterHandshakeClaimProvider(
            ModuleRegistration module,
            RunicHandshakeClaimProvider provider);

        IDisposable RegisterHandshakeClaimEvaluator(
            ModuleRegistration module,
            string evaluatorId,
            RunicHandshakeClaimEvaluator evaluator);

        RpcSendResult SendToServer(
            ModuleRegistration module,
            string endpointId,
            byte[] payload,
            string idempotencyKey,
            TimeSpan timeout,
            RunicRpcCompletion completion);

        RpcSendResult SendToPeer(
            ModuleRegistration module,
            RpcPeerSnapshot expectedPeer,
            string endpointId,
            byte[] payload,
            string idempotencyKey,
            TimeSpan timeout,
            RunicRpcCompletion completion);

        bool TryGetPeer(long peerId, out RpcPeerSnapshot peer);
        bool TryResolveActor(
            RpcPeerSnapshot expectedPeer,
            RpcActorAssurance minimumAssurance,
            out RpcActorSnapshot actor,
            out string reasonCode);
        IReadOnlyList<RpcPeerSnapshot> GetPeers();
        bool TryDisconnectPeer(long peerId, string reasonCode);
    }

    /// <summary>
    /// Backward-compatible extension surface for server admission decisions that require the
    /// exact transport-bound account before vanilla RPC_PeerInfo. Ordinary claim evaluators stay
    /// on IRunicRpcService and receive self-reported claims only.
    /// </summary>
    public interface IRunicRpcPeerAdmissionService
    {
        IDisposable RegisterHandshakePeerEvaluator(
            ModuleRegistration module,
            string evaluatorId,
            RunicHandshakePeerEvaluator evaluator);
    }
}
