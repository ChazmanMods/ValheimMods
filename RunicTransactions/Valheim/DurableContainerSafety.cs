using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using UnityEngine;

namespace RunicTransactions.Valheim
{
    public enum DurableEndpointLockReadState
    {
        Absent = 0,
        Valid = 1,
        Invalid = 2
    }

    public enum DurableEndpointLockPhase
    {
        Prepared = 1,
        Committed = 2
    }

    internal enum LegacyClaimRewriteDecision
    {
        Reject = 0,
        RewriteLegacy = 1,
        AlreadyStable = 2
    }

    internal sealed class BoundedReturnRequestCache
    {
        internal const int MaximumEntries = 4096;

        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly SortedSet<CacheEntry> _oldest = new SortedSet<CacheEntry>();
        private long _sequence;

        internal int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        internal bool TryAdmit(string key, double now, double retryWindowSeconds)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 256 ||
                double.IsNaN(now) || double.IsInfinity(now) ||
                double.IsNaN(retryWindowSeconds) || double.IsInfinity(retryWindowSeconds) ||
                retryWindowSeconds <= 0d)
                return false;
            lock (_gate)
            {
                PruneLocked(now, retryWindowSeconds);
                if (_entries.TryGetValue(key, out CacheEntry existing))
                {
                    if (now < existing.Timestamp || now - existing.Timestamp < retryWindowSeconds)
                        return false;
                    _entries.Remove(key);
                    _oldest.Remove(existing);
                }
                if (_sequence == long.MaxValue) return false;
                while (_entries.Count >= MaximumEntries)
                {
                    CacheEntry oldest = _oldest.Min;
                    if (oldest == null) return false;
                    _oldest.Remove(oldest);
                    _entries.Remove(oldest.Key);
                }
                var admitted = new CacheEntry(key, now, ++_sequence);
                _entries.Add(key, admitted);
                _oldest.Add(admitted);
                return true;
            }
        }

        internal int Prune(double now, double retryWindowSeconds)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) ||
                double.IsNaN(retryWindowSeconds) || double.IsInfinity(retryWindowSeconds) ||
                retryWindowSeconds <= 0d)
                return 0;
            lock (_gate) return PruneLocked(now, retryWindowSeconds);
        }

        private int PruneLocked(double now, double retryWindowSeconds)
        {
            int removed = 0;
            while (_oldest.Count > 0)
            {
                CacheEntry oldest = _oldest.Min;
                if (now < oldest.Timestamp || now - oldest.Timestamp < retryWindowSeconds)
                    break;
                _oldest.Remove(oldest);
                if (_entries.TryGetValue(oldest.Key, out CacheEntry current) &&
                    ReferenceEquals(current, oldest))
                {
                    _entries.Remove(oldest.Key);
                    removed++;
                }
            }
            return removed;
        }

        internal bool Contains(string key)
        {
            lock (_gate) return _entries.ContainsKey(key);
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _oldest.Clear();
                _sequence = 0;
            }
        }

        private sealed class CacheEntry : IComparable<CacheEntry>
        {
            internal CacheEntry(string key, double timestamp, long sequence)
            {
                Key = key;
                Timestamp = timestamp;
                Sequence = sequence;
            }

            internal string Key { get; }
            internal double Timestamp { get; }
            internal long Sequence { get; }

            public int CompareTo(CacheEntry other)
            {
                if (other == null) return 1;
                int timestamp = Timestamp.CompareTo(other.Timestamp);
                if (timestamp != 0) return timestamp;
                int sequence = Sequence.CompareTo(other.Sequence);
                return sequence != 0
                    ? sequence
                    : string.Compare(Key, other.Key, StringComparison.Ordinal);
            }
        }
    }

    public sealed class DurableEndpointLockRecord
    {
        /// <summary>
        /// Compatibility constructor for a schema-one claim whose station and endpoint were
        /// persisted as restart-unstable numeric ZDOID strings.
        /// </summary>
        public DurableEndpointLockRecord(
            string operationId,
            string moduleId,
            string stationId,
            string endpointId,
            string journalKind,
            string beforeFingerprint,
            string afterFingerprint,
            DurableEndpointLockPhase phase)
            : this(
                operationId,
                moduleId,
                string.Empty,
                string.Empty,
                RequireText(stationId, nameof(stationId), 200),
                RequireText(endpointId, nameof(endpointId), 200),
                journalKind,
                beforeFingerprint,
                afterFingerprint,
                phase)
        {
        }

        private DurableEndpointLockRecord(
            string operationId,
            string moduleId,
            string stationToken,
            string endpointToken,
            string stationId,
            string endpointId,
            string journalKind,
            string beforeFingerprint,
            string afterFingerprint,
            DurableEndpointLockPhase phase)
        {
            OperationId = RequireText(operationId, nameof(operationId), 200);
            ModuleId = RequireText(moduleId, nameof(moduleId), 128);
            StationToken = stationToken ?? string.Empty;
            EndpointToken = endpointToken ?? string.Empty;
            StationId = RequireText(stationId, nameof(stationId), 200);
            EndpointId = RequireText(endpointId, nameof(endpointId), 200);
            JournalKind = RequireText(journalKind, nameof(journalKind), 64);
            BeforeFingerprint = RequireText(
                beforeFingerprint, nameof(beforeFingerprint), 256);
            AfterFingerprint = RequireText(
                afterFingerprint, nameof(afterFingerprint), 256);
            if (string.Equals(
                    BeforeFingerprint, AfterFingerprint, StringComparison.Ordinal))
                throw new ArgumentException(
                    "A durable endpoint claim must describe a changed endpoint.",
                    nameof(afterFingerprint));
            if (!Enum.IsDefined(typeof(DurableEndpointLockPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            Phase = phase;
        }

        public string OperationId { get; }
        public string ModuleId { get; }
        public string StationToken { get; }
        public string EndpointToken { get; }
        public string StationId { get; }
        public string EndpointId { get; }
        public bool IsLegacyIdentity => string.IsNullOrEmpty(EndpointToken);
        public string JournalKind { get; }
        public string BeforeFingerprint { get; }
        public string AfterFingerprint { get; }
        public DurableEndpointLockPhase Phase { get; }

        public DurableEndpointLockRecord WithPhase(DurableEndpointLockPhase phase) =>
            IsLegacyIdentity
                ? new DurableEndpointLockRecord(
                    OperationId,
                    ModuleId,
                    StationId,
                    EndpointId,
                    JournalKind,
                    BeforeFingerprint,
                    AfterFingerprint,
                    phase)
                : CreateStable(
                    OperationId,
                    ModuleId,
                    StationToken,
                    EndpointToken,
                    JournalKind,
                    BeforeFingerprint,
                    AfterFingerprint,
                    phase);

        public bool MatchesExact(DurableEndpointLockRecord other) =>
            other != null &&
            string.Equals(OperationId, other.OperationId, StringComparison.Ordinal) &&
            string.Equals(ModuleId, other.ModuleId, StringComparison.Ordinal) &&
            string.Equals(StationToken, other.StationToken, StringComparison.Ordinal) &&
            string.Equals(EndpointToken, other.EndpointToken, StringComparison.Ordinal) &&
            string.Equals(StationId, other.StationId, StringComparison.Ordinal) &&
            string.Equals(EndpointId, other.EndpointId, StringComparison.Ordinal) &&
            string.Equals(JournalKind, other.JournalKind, StringComparison.Ordinal) &&
            string.Equals(
                BeforeFingerprint, other.BeforeFingerprint, StringComparison.Ordinal) &&
            string.Equals(
                AfterFingerprint, other.AfterFingerprint, StringComparison.Ordinal) &&
            Phase == other.Phase;

        internal static DurableEndpointLockRecord CreateStable(
            string operationId,
            string moduleId,
            string stationToken,
            string endpointToken,
            string journalKind,
            string beforeFingerprint,
            string afterFingerprint,
            DurableEndpointLockPhase phase)
        {
            if (!WorldObjectToken.TryParse(stationToken, out WorldObjectToken station))
                throw new ArgumentException(
                    "A canonical world-object station token is required.",
                    nameof(stationToken));
            if (!WorldObjectToken.TryParse(endpointToken, out WorldObjectToken endpoint))
                throw new ArgumentException(
                    "A canonical world-object endpoint token is required.",
                    nameof(endpointToken));
            return new DurableEndpointLockRecord(
                operationId,
                moduleId,
                station.Value,
                endpoint.Value,
                WorldObjectIdentity.EndpointId(station.Value),
                WorldObjectIdentity.EndpointId(endpoint.Value),
                journalKind,
                beforeFingerprint,
                afterFingerprint,
                phase);
        }

        private static string RequireText(
            string value,
            string parameterName,
            int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable value is required.", parameterName);
            if (value.Length > maximum) throw new ArgumentOutOfRangeException(parameterName);
            foreach (char character in value)
                if (char.IsControl(character))
                    throw new ArgumentException(
                        "Stable values cannot contain control characters.", parameterName);
            return value;
        }
    }

    /// <summary>
    /// Shared Valheim endpoint authority and durable exclusion used by every Runic inventory
    /// adapter. A persisted claim never expires by time: only its exact owning recovery operation
    /// may commit or clear it.
    /// </summary>
    public static class DurableContainerSafety
    {
        public const string LockStorageKey = "runic.transactions.endpoint-lock.record";

        private const int SchemaVersion = 2;
        private const int LegacySchemaVersion = 1;
        private const int OwnershipRpcSchemaVersion = 1;
        private const int MaximumEncodedCharacters = 4096;
        internal const string ReturnRequestEndpoint = "runic.transactions.container.owner-return";
        private const float ReturnRequestIntervalSeconds = 1f;

        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerView =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<WearNTear, ZNetView> WearView =
            AccessTools.FieldRefAccess<WearNTear, ZNetView>("m_nview");
        private static readonly MethodInfo ContainerLoadMethod =
            AccessTools.Method(typeof(Container), "Load") ??
            throw new MissingMethodException(typeof(Container).FullName, "Load");
        private static readonly MethodInfo ContainerSaveMethod =
            AccessTools.Method(typeof(Container), "Save") ??
            throw new MissingMethodException(typeof(Container).FullName, "Save");
        private static readonly FieldInfo ContainerLoadingField =
            AccessTools.Field(typeof(Container), "m_loading") ??
            throw new MissingFieldException(typeof(Container).FullName, "m_loading");
        private static readonly MethodInfo ServerPeerIdMethod =
            AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID") ??
            throw new MissingMethodException(typeof(ZRoutedRpc).FullName, "GetServerPeerID");
        private static readonly BoundedReturnRequestCache LastReturnRequests =
            new BoundedReturnRequestCache();

        private static ManualLogSource _log;
        private static IRunicRpcService _rpcService;
        private static ModuleRegistration _moduleRegistration;

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
            _rpcService = null;
            _moduleRegistration = null;
            LastReturnRequests.Clear();
        }

        internal static void Shutdown()
        {
            _rpcService = null;
            _moduleRegistration = null;
            LastReturnRequests.Clear();
            _log = null;
        }

        internal static void Update() => LastReturnRequests.Prune(
            Time.realtimeSinceStartup,
            ReturnRequestIntervalSeconds);

        internal static IDisposable ConfigureRpc(
            ModuleRegistration moduleRegistration,
            IRunicRpcService rpcService)
        {
            if (moduleRegistration == null) throw new ArgumentNullException(nameof(moduleRegistration));
            if (rpcService == null) throw new ArgumentNullException(nameof(rpcService));
            IDisposable registration = rpcService.RegisterEndpoint(
                moduleRegistration,
                new RpcEndpointDescriptor(
                    RunicTransactions.Plugin.ModuleId,
                    ReturnRequestEndpoint,
                    RunicCapabilityIds.ContainerOwnershipReturn,
                    1,
                    RpcEndpointDirection.ServerToClient,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.ConnectionBound,
                    64),
                HandleReturnRequest);
            _moduleRegistration = moduleRegistration;
            _rpcService = rpcService;
            return registration;
        }

        public static DurableEndpointLockReadState Read(
            Container container,
            out DurableEndpointLockRecord record)
        {
            ZDO zdo = Zdo(container);
            if (zdo == null) return ReturnAbsent(out record);
            DurableEndpointLockReadState state = Parse(
                zdo.GetString(LockStorageKey, string.Empty), out record);
            if (state == DurableEndpointLockReadState.Valid &&
                !RecordMatchesEndpoint(zdo, record))
            {
                record = null;
                return DurableEndpointLockReadState.Invalid;
            }
            return state;
        }

        /// <summary>
        /// Reads the bytes physically stored on an endpoint without accepting the legacy numeric
        /// endpoint ID as current authority.  This is internal and is used only by
        /// the coordinator's guarded schema-one identity upgrade: the caller must correlate the
        /// old claim with its station header, immutable fingerprints, and the exact live Container
        /// before any stable identity is published.
        /// </summary>
        internal static DurableEndpointLockReadState ReadPersistedForIdentityUpgrade(
            Container container,
            out DurableEndpointLockRecord record)
        {
            ZDO zdo = Zdo(container);
            return zdo == null
                ? ReturnAbsent(out record)
                : Parse(zdo.GetString(LockStorageKey, string.Empty), out record);
        }

        /// <summary>
        /// Replaces exactly one proven schema-one claim with its stable equivalent.  A retry may
        /// observe either the exact old record or the exact new record; every other value is
        /// retained and rejected.  The station header remains legacy until all endpoint rewrites
        /// have completed, so this helper never changes station-side authority.
        /// </summary>
        internal static bool TryUpgradePersistedClaimIdentity(
            Container container,
            DurableEndpointLockRecord expectedLegacy,
            DurableEndpointLockRecord expectedStable,
            out string failure)
        {
            failure = string.Empty;
            if (container == null || expectedLegacy == null || expectedStable == null ||
                !expectedLegacy.IsLegacyIdentity || expectedStable.IsLegacyIdentity)
            {
                failure = "Exact legacy and stable endpoint claims are required for identity upgrade.";
                return false;
            }
            if (!TrySynchronizeServerOwned(container, out _))
            {
                failure = "The endpoint is not synchronized, closed, and server-owned.";
                return false;
            }
            ZDO zdo = Zdo(container);
            if (zdo == null || !RecordMatchesEndpoint(zdo, expectedStable))
            {
                failure = "The proposed stable claim is not uniquely bound to this exact endpoint.";
                return false;
            }

            DurableEndpointLockReadState state = Parse(
                zdo.GetString(LockStorageKey, string.Empty),
                out DurableEndpointLockRecord current);
            LegacyClaimRewriteDecision decision = EvaluateLegacyClaimRewrite(
                expectedLegacy, expectedStable, state, current);
            if (decision == LegacyClaimRewriteDecision.AlreadyStable) return true;
            if (decision != LegacyClaimRewriteDecision.RewriteLegacy)
            {
                failure = state == DurableEndpointLockReadState.Invalid
                    ? "The endpoint has a corrupt durable claim."
                    : "The endpoint claim changed or belongs to another durable operation.";
                return false;
            }

            string encoded = Serialize(expectedStable);
            zdo.Set(LockStorageKey, encoded);
            if (!string.Equals(
                    zdo.GetString(LockStorageKey, string.Empty), encoded,
                    StringComparison.Ordinal) ||
                Read(container, out DurableEndpointLockRecord persisted) !=
                    DurableEndpointLockReadState.Valid ||
                !persisted.MatchesExact(expectedStable))
            {
                failure = "The stable endpoint claim did not publish exactly.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Clears only the exact schema-one claim supplied by a terminal/unpublished orphan
        /// cleanup. This bypasses current numeric-ID binding solely because the caller has already
        /// correlated the physically stored record with its legacy station header and immutable
        /// terminal fingerprints. A missing record is an idempotent success; a different record is
        /// never cleared.
        /// </summary>
        internal static bool TryClearPersistedLegacyClaimForIdentityUpgrade(
            Container container,
            DurableEndpointLockRecord expectedLegacy,
            out string failure)
        {
            failure = string.Empty;
            if (container == null || expectedLegacy == null ||
                !expectedLegacy.IsLegacyIdentity)
            {
                failure = "An exact schema-one endpoint claim is required for orphan cleanup.";
                return false;
            }
            if (!TrySynchronizeServerOwned(container, out _))
            {
                failure = "The endpoint is not synchronized, closed, and server-owned.";
                return false;
            }
            ZDO zdo = Zdo(container);
            if (zdo == null)
            {
                failure = "The exact endpoint ZDO is unavailable.";
                return false;
            }
            DurableEndpointLockReadState state = Parse(
                zdo.GetString(LockStorageKey, string.Empty),
                out DurableEndpointLockRecord current);
            if (state == DurableEndpointLockReadState.Absent) return true;
            if (state != DurableEndpointLockReadState.Valid || current == null ||
                !current.MatchesExact(expectedLegacy))
            {
                failure = state == DurableEndpointLockReadState.Invalid
                    ? "The endpoint has a corrupt durable claim."
                    : "The endpoint claim changed or belongs to another durable operation.";
                return false;
            }
            zdo.Set(LockStorageKey, string.Empty);
            if (Parse(
                    zdo.GetString(LockStorageKey, string.Empty), out _) ==
                DurableEndpointLockReadState.Absent) return true;
            failure = "The exact schema-one endpoint claim did not clear.";
            return false;
        }

        internal static LegacyClaimRewriteDecision EvaluateLegacyClaimRewrite(
            DurableEndpointLockRecord expectedLegacy,
            DurableEndpointLockRecord expectedStable,
            DurableEndpointLockReadState currentState,
            DurableEndpointLockRecord current)
        {
            if (expectedLegacy == null || expectedStable == null ||
                !expectedLegacy.IsLegacyIdentity || expectedStable.IsLegacyIdentity ||
                currentState != DurableEndpointLockReadState.Valid || current == null)
                return LegacyClaimRewriteDecision.Reject;
            if (current.MatchesExact(expectedStable))
                return LegacyClaimRewriteDecision.AlreadyStable;
            return current.MatchesExact(expectedLegacy)
                ? LegacyClaimRewriteDecision.RewriteLegacy
                : LegacyClaimRewriteDecision.Reject;
        }

        public static bool BlocksMutation(
            Container container,
            string allowedOperationId,
            out string reason)
        {
            DurableEndpointLockReadState state = Read(container, out DurableEndpointLockRecord record);
            if (state == DurableEndpointLockReadState.Absent)
            {
                reason = string.Empty;
                return false;
            }
            if (state == DurableEndpointLockReadState.Invalid)
            {
                reason = "The container has a corrupt durable Runic endpoint claim.";
                return true;
            }
            if (!string.IsNullOrEmpty(allowedOperationId) && string.Equals(
                    record.OperationId, allowedOperationId, StringComparison.Ordinal))
            {
                reason = string.Empty;
                return false;
            }
            reason = "The container is reserved by durable Runic operation " +
                     record.OperationId + ".";
            return true;
        }

        public static bool TryAcquire(
            Container container,
            DurableEndpointLockRecord record,
            out string failure)
        {
            failure = string.Empty;
            if (record == null)
            {
                failure = "A complete durable endpoint claim is required.";
                return false;
            }
            if (!TrySynchronizeServerOwned(container, out _))
            {
                failure = "The endpoint is not synchronized, closed, and server-owned.";
                return false;
            }
            ZDO zdo = Zdo(container);
            if (zdo == null || !RecordMatchesEndpoint(zdo, record))
            {
                failure = "The endpoint identity changed before its claim was published.";
                return false;
            }
            DurableEndpointLockReadState existingState = Parse(
                zdo.GetString(LockStorageKey, string.Empty),
                out DurableEndpointLockRecord existing);
            if (existingState == DurableEndpointLockReadState.Valid &&
                existing.MatchesExact(record))
            {
                // Idempotence is required when adopting a journal that predates the shared
                // coordinator: a crash can leave an exact first claim without a station header.
                return true;
            }
            if (existingState != DurableEndpointLockReadState.Absent)
            {
                failure = existingState == DurableEndpointLockReadState.Invalid
                    ? "The endpoint has a corrupt durable Runic claim."
                    : "The endpoint already has a different durable Runic claim.";
                return false;
            }
            string encoded = Serialize(record);
            zdo.Set(LockStorageKey, encoded);
            if (!string.Equals(zdo.GetString(LockStorageKey, string.Empty), encoded,
                    StringComparison.Ordinal))
            {
                failure = "The endpoint claim did not publish exactly.";
                return false;
            }
            return true;
        }

        public static bool TryMarkCommitted(
            Container container,
            string operationId,
            out string failure)
        {
            failure = string.Empty;
            if (!TryReadExact(container, operationId, out ZDO zdo,
                    out DurableEndpointLockRecord record, out failure)) return false;
            string encoded = Serialize(record.WithPhase(DurableEndpointLockPhase.Committed));
            zdo.Set(LockStorageKey, encoded);
            return string.Equals(
                zdo.GetString(LockStorageKey, string.Empty), encoded, StringComparison.Ordinal);
        }

        public static bool TryClear(
            Container container,
            string operationId,
            out string failure)
        {
            failure = string.Empty;
            if (!TryReadExact(container, operationId, out ZDO zdo, out _, out failure))
                return false;
            zdo.Set(LockStorageKey, string.Empty);
            return string.IsNullOrEmpty(zdo.GetString(LockStorageKey, string.Empty));
        }

        public static bool TrySynchronizeServerOwned(
            Container container,
            out Inventory inventory)
        {
            inventory = null;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || container == null)
                return false;
            ZNetView view = View(container);
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner())
            {
                RequestOwnershipReturn(container);
                return false;
            }
            return TrySynchronizeLocallyOwned(container, out inventory);
        }

        public static void RequestOwnershipReturn(Container container)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || container == null) return;
            if (_rpcService == null || _moduleRegistration == null) return;
            ZNetView view = View(container);
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || view.IsOwner()) return;
            long owner = zdo.GetOwner();
            if (owner == 0L || owner == ZNet.GetUID()) return;
            if (!_rpcService.TryGetPeer(owner, out RpcPeerSnapshot peer)) return;
            string key = zdo.m_uid + ":" + owner;
            double now = Time.realtimeSinceStartup;
            if (!LastReturnRequests.TryAdmit(key, now, ReturnRequestIntervalSeconds)) return;
            var package = new ZPackage();
            package.Write(OwnershipRpcSchemaVersion);
            package.Write(zdo.m_uid);
            _rpcService.SendToPeer(
                _moduleRegistration,
                peer,
                ReturnRequestEndpoint,
                package.GetArray(),
                Guid.NewGuid().ToString("N"),
                TimeSpan.FromSeconds(3),
                null);
        }

        public static bool TryReturnLocalOwnership(Container container)
        {
            if (container == null || ZNet.instance == null || ZNet.instance.IsServer() ||
                container.IsInUse()) return false;
            if (!TrySynchronizeLocallyOwned(container, out _)) return false;
            ZNetView view = View(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner()
                ? view.GetZDO()
                : null;
            long server = ServerPeerId();
            if (zdo == null || server == 0L) return false;
            try { ContainerSaveMethod.Invoke(container, null); }
            catch { return false; }
            ZDOID target = zdo.m_uid;
            zdo.SetOwner(server);
            ZDOMan.instance?.ForceSendZDO(server, target);
            return true;
        }

        public static string Serialize(DurableEndpointLockRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var package = new ZPackage();
            package.Write(record.IsLegacyIdentity ? LegacySchemaVersion : SchemaVersion);
            package.Write(record.OperationId);
            package.Write(record.ModuleId);
            if (record.IsLegacyIdentity)
            {
                package.Write(record.StationId);
                package.Write(record.EndpointId);
            }
            else
            {
                package.Write(record.StationToken);
                package.Write(record.EndpointToken);
            }
            package.Write(record.JournalKind);
            package.Write(record.BeforeFingerprint);
            package.Write(record.AfterFingerprint);
            package.Write((int)record.Phase);
            string encoded = package.GetBase64();
            if (encoded.Length == 0 || encoded.Length > MaximumEncodedCharacters)
                throw new InvalidOperationException("The durable endpoint claim exceeds its bound.");
            return encoded;
        }

        public static DurableEndpointLockReadState Parse(
            string encoded,
            out DurableEndpointLockRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(encoded)) return DurableEndpointLockReadState.Absent;
            if (encoded.Length > MaximumEncodedCharacters) return DurableEndpointLockReadState.Invalid;
            try
            {
                var package = new ZPackage(encoded);
                int schema = package.ReadInt();
                if (schema != SchemaVersion && schema != LegacySchemaVersion)
                    return DurableEndpointLockReadState.Invalid;
                string operationId = package.ReadString();
                string moduleId = package.ReadString();
                string stationIdentity = package.ReadString();
                string endpointIdentity = package.ReadString();
                string journalKind = package.ReadString();
                string before = package.ReadString();
                string after = package.ReadString();
                var phase = (DurableEndpointLockPhase)package.ReadInt();
                DurableEndpointLockRecord loaded = schema == SchemaVersion
                    ? DurableEndpointLockRecord.CreateStable(
                        operationId, moduleId, stationIdentity, endpointIdentity,
                        journalKind, before, after, phase)
                    : new DurableEndpointLockRecord(
                        operationId, moduleId, stationIdentity, endpointIdentity,
                        journalKind, before, after, phase);
                if (package.GetPos() != package.Size()) return DurableEndpointLockReadState.Invalid;
                record = loaded;
                return DurableEndpointLockReadState.Valid;
            }
            catch
            {
                record = null;
                return DurableEndpointLockReadState.Invalid;
            }
        }

        internal static bool DenyOpen(Container container, long sender)
        {
            if (!BlocksMutation(container, null, out _)) return false;
            try { View(container)?.InvokeRPC(sender, "OpenRespons", false); }
            catch { }
            return true;
        }

        internal static bool DenyStack(Container container, long sender)
        {
            if (!BlocksMutation(container, null, out _)) return false;
            try { View(container)?.InvokeRPC(sender, "RPC_StackResponse", false); }
            catch { }
            return true;
        }

        internal static bool DenyTakeAll(Container container, long sender)
        {
            if (!BlocksMutation(container, null, out _)) return false;
            try { View(container)?.InvokeRPC(sender, "TakeAllRespons", false); }
            catch { }
            return true;
        }

        internal static bool DenyLockedContainerMaintenance(Container container) =>
            Read(container, out _) != DurableEndpointLockReadState.Absent;

        internal static bool DenyLockedEndpointDestruction(WearNTear wear)
        {
            if (wear == null) return false;
            ZNetView wearView = WearView(wear);
            ZDO wearZdo = wearView != null && wearView.IsValid() ? wearView.GetZDO() : null;
            if (wearZdo == null) return false;
            Container[] candidates = wear.GetComponentsInChildren<Container>(true);
            if (candidates == null || candidates.Length == 0 || candidates.Length > 64)
                return false;
            foreach (Container container in candidates)
            {
                ZDO containerZdo = Zdo(container);
                if (containerZdo == null || containerZdo.m_uid != wearZdo.m_uid ||
                    Read(container, out _) == DurableEndpointLockReadState.Absent) continue;
                float health = wearZdo.GetFloat(ZDOVars.s_health, wear.m_health);
                if (health <= 0f)
                    wearZdo.Set(ZDOVars.s_health,
                        wear.m_health > 1f ? 1f : wear.m_health > 0f ? wear.m_health : 1f);
                _log?.LogWarning(
                    "Container destruction was deferred because a durable Runic endpoint claim is unresolved.");
                return true;
            }
            return false;
        }

        private static bool TrySynchronizeLocallyOwned(
            Container container,
            out Inventory inventory)
        {
            inventory = null;
            ZNetView view = View(container);
            ZDO zdo = view != null && view.IsValid() && view.IsOwner()
                ? view.GetZDO()
                : null;
            if (zdo == null || container.IsInUse()) return false;
            ZDOID exactId = zdo.m_uid;
            try { ContainerLoadMethod.Invoke(container, null); }
            catch { return false; }
            view = View(container);
            ZDO current = view != null && view.IsValid() && view.IsOwner()
                ? view.GetZDO()
                : null;
            if (current == null || current.m_uid != exactId ||
                (bool)ContainerLoadingField.GetValue(container)) return false;
            inventory = container.GetInventory();
            if (inventory == null) return false;
            string persisted = current.GetString(ZDOVars.s_items, string.Empty);
            if (string.IsNullOrEmpty(persisted))
                return inventory.GetAllItems().Count == 0;
            var snapshot = new ZPackage();
            inventory.Save(snapshot);
            return string.Equals(snapshot.GetBase64(), persisted, StringComparison.Ordinal);
        }

        private static bool TryReadExact(
            Container container,
            string operationId,
            out ZDO zdo,
            out DurableEndpointLockRecord record,
            out string failure)
        {
            zdo = null;
            record = null;
            failure = string.Empty;
            if (!TrySynchronizeServerOwned(container, out _))
            {
                failure = "The endpoint is not synchronized and server-owned.";
                return false;
            }
            zdo = Zdo(container);
            DurableEndpointLockReadState state = Read(container, out record);
            if (state != DurableEndpointLockReadState.Valid || !string.Equals(
                    record.OperationId, operationId, StringComparison.Ordinal) ||
                zdo == null || !RecordMatchesEndpoint(zdo, record))
            {
                failure = state == DurableEndpointLockReadState.Invalid
                    ? "The endpoint claim is corrupt."
                    : "The endpoint claim is absent or owned by another operation.";
                return false;
            }
            return true;
        }

        private static RpcHandlerResult HandleReturnRequest(RpcRequestContext request)
        {
            if (!IsExactCurrentServerRequest(request, _rpcService) ||
                ZNet.instance == null || ZNet.instance.IsServer() ||
                _rpcService == null || _rpcService.IsServer)
                return RpcHandlerResult.Deny("ownership-return-not-authoritative");
            try
            {
                var package = new ZPackage(request.Payload);
                if (package.ReadInt() != OwnershipRpcSchemaVersion)
                    return new RpcHandlerResult(
                        RpcResultCode.InvalidRequest,
                        "ownership-return-schema-invalid");
                ZDOID target = package.ReadZDOID();
                if (target.IsNone() || package.GetPos() != package.Size())
                    return new RpcHandlerResult(
                        RpcResultCode.InvalidRequest,
                        "ownership-return-payload-invalid");
                GameObject root = ZNetScene.instance?.FindInstance(target);
                Container container = ResolveExactContainer(root, target);
                if (container == null)
                    return new RpcHandlerResult(
                        RpcResultCode.NotFound,
                        "ownership-return-container-missing");
                return TryReturnLocalOwnership(container)
                    ? RpcHandlerResult.Ok()
                    : new RpcHandlerResult(
                        RpcResultCode.NotReady,
                        "ownership-return-deferred");
            }
            catch (Exception exception)
            {
                _log?.LogWarning(
                    "Durable container ownership return failed closed: " +
                    exception.GetType().Name + ".");
                return new RpcHandlerResult(
                    RpcResultCode.InvalidRequest,
                    "ownership-return-payload-invalid");
            }
        }

        internal static bool IsExactCurrentServerRequest(
            RpcRequestContext request,
            IRunicRpcService rpcService)
        {
            if (request == null || rpcService == null || rpcService.IsServer ||
                request.ReceiverIsServer || !request.IsConnectionCurrent ||
                !rpcService.IsServerConnectionReady || request.Peer == null ||
                request.Peer.PeerId == 0 || request.Peer.Identity == null ||
                request.Peer.Identity.Assurance != RpcIdentityAssurance.ConnectionBound ||
                !string.Equals(
                    request.Peer.Identity.Authority,
                    "valheim.server",
                    StringComparison.Ordinal) ||
                !rpcService.TryGetPeer(request.Peer.PeerId, out RpcPeerSnapshot current) ||
                current == null || !current.Current || !current.Ready ||
                current.Identity == null ||
                current.Identity.Assurance != RpcIdentityAssurance.ConnectionBound ||
                !string.Equals(current.SessionId, request.Peer.SessionId, StringComparison.Ordinal) ||
                !string.Equals(
                    current.Identity.Authority,
                    request.Peer.Identity.Authority,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    current.Identity.SubjectId,
                    request.Peer.Identity.SubjectId,
                    StringComparison.Ordinal))
                return false;
            return true;
        }

        private static Container ResolveExactContainer(GameObject root, ZDOID expected)
        {
            if (root == null) return null;
            Container result = null;
            Container[] candidates = root.GetComponentsInChildren<Container>(true);
            if (candidates == null || candidates.Length > 64) return null;
            foreach (Container candidate in candidates)
            {
                ZDO zdo = Zdo(candidate);
                if (zdo == null || zdo.m_uid != expected) continue;
                if (result != null && !ReferenceEquals(result, candidate)) return null;
                result = candidate;
            }
            Container parent = root.GetComponentInParent<Container>();
            if (parent != null && Zdo(parent)?.m_uid == expected)
            {
                if (result != null && !ReferenceEquals(result, parent)) return null;
                result = parent;
            }
            return result;
        }

        /// <summary>
        /// Read-only loaded-instance resolution used while correlating a schema-one claim after
        /// Valheim remapped its numeric ZDOID. Persisted claim bytes, not proximity, select the ZDO.
        /// </summary>
        internal static Container ResolveExactLoadedContainerForIdentityUpgrade(ZDOID expected) =>
            expected.IsNone()
                ? null
                : ResolveExactContainer(ZNetScene.instance?.FindInstance(expected), expected);

        private static ZNetView View(Container container) =>
            container == null ? null : ContainerView(container);

        private static ZDO Zdo(Container container) => View(container)?.GetZDO();

        private static bool RecordMatchesEndpoint(
            ZDO zdo,
            DurableEndpointLockRecord record)
        {
            if (zdo == null || record == null) return false;
            if (record.IsLegacyIdentity)
                return string.Equals(
                    zdo.m_uid.ToString(), record.EndpointId, StringComparison.Ordinal);
            if (WorldObjectIdentity.Read(zdo, out WorldObjectToken token) !=
                    WorldObjectIdentityStatus.Ready ||
                !string.Equals(
                    token.Value, record.EndpointToken, StringComparison.Ordinal))
                return false;
            return WorldObjectIdentity.Resolve(token, out ZDO exact) ==
                       WorldObjectIdentityStatus.Ready &&
                   exact != null && exact.m_uid == zdo.m_uid;
        }

        private static long ServerPeerId()
        {
            try
            {
                return ZRoutedRpc.instance == null
                    ? 0L
                    : (long)ServerPeerIdMethod.Invoke(ZRoutedRpc.instance, null);
            }
            catch { return 0L; }
        }

        private static DurableEndpointLockReadState ReturnAbsent(
            out DurableEndpointLockRecord record)
        {
            record = null;
            return DurableEndpointLockReadState.Absent;
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
    internal static class DurableContainerRequestOpenPatch
    {
        private static bool Prefix(Container __instance, long __0) =>
            !DurableContainerSafety.DenyOpen(__instance, __0);
    }

    [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
    internal static class DurableContainerRequestStackPatch
    {
        private static bool Prefix(Container __instance, long __0) =>
            !DurableContainerSafety.DenyStack(__instance, __0);
    }

    [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
    internal static class DurableContainerRequestTakeAllPatch
    {
        private static bool Prefix(Container __instance, long __0) =>
            !DurableContainerSafety.DenyTakeAll(__instance, __0);
    }

    // CheckForChanges contains vanilla's auto-destroy-empty branch, which destroys the
    // ZNetView directly and never crosses WearNTear.Destroy. Keep every maintenance action
    // frozen while a durable endpoint claim exists so a source emptied by the first half of a
    // transaction cannot disappear before its journal is reconciled and its claim is released.
    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    [HarmonyPriority(Priority.First)]
    internal static class DurableContainerCheckForChangesPatch
    {
        private static bool Prefix(Container __instance) =>
            !DurableContainerSafety.DenyLockedContainerMaintenance(__instance);
    }

    [HarmonyPatch(typeof(WearNTear), "Destroy", new[] { typeof(HitData), typeof(bool) })]
    [HarmonyPriority(Priority.First)]
    internal static class DurableContainerDestroyPatch
    {
        private static bool Prefix(WearNTear __instance) =>
            !DurableContainerSafety.DenyLockedEndpointDestruction(__instance);
    }
}
