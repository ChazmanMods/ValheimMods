using System;
using System.Collections.Generic;
using RunicSentinel.Contracts;

namespace RunicSentinel.Core
{
    internal enum SentinelRemoteAdmissionMode
    {
        Disabled = 0,
        Optional = 1,
        Required = 2
    }

    internal interface ISentinelNetworkProfileSource
    {
        bool TryGetNetworkProfile(out SentinelNetworkProfile profile);
    }

    internal sealed class SentinelNetworkProfile
    {
        internal SentinelNetworkProfile(
            string snapshotDigest,
            long snapshotCapturedUnixSeconds,
            string policyDigest,
            long policySequence,
            string policyProfile,
            AdmissionDisposition disposition)
        {
            SnapshotDigest = SecurityContractValidation.RequireLowerHex(
                snapshotDigest, 64, nameof(snapshotDigest));
            PolicyDigest = SecurityContractValidation.RequireLowerHex(
                policyDigest, 64, nameof(policyDigest));
            if (snapshotCapturedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(snapshotCapturedUnixSeconds));
            if (policySequence < 0L)
                throw new ArgumentOutOfRangeException(nameof(policySequence));
            if (!SecurityContractValidation.IsDisposition(disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition));
            SnapshotCapturedUnixSeconds = snapshotCapturedUnixSeconds;
            PolicySequence = policySequence;
            PolicyProfile = SecurityContractValidation.RequireAtom(
                policyProfile, 1, 64, nameof(policyProfile));
            Disposition = disposition;
        }

        internal string SnapshotDigest { get; }
        internal long SnapshotCapturedUnixSeconds { get; }
        internal string PolicyDigest { get; }
        internal long PolicySequence { get; }
        internal string PolicyProfile { get; }
        internal AdmissionDisposition Disposition { get; }
    }

    internal sealed class SentinelAdmissionEnvelope
    {
        internal string RequestId { get; set; }
        internal string Version { get; set; }
        internal string SnapshotDigest { get; set; }
        internal long SnapshotCapturedUnixSeconds { get; set; }
        internal string PolicyDigest { get; set; }
        internal long PolicySequence { get; set; }
        internal string PolicyProfile { get; set; }
        internal AdmissionDisposition Disposition { get; set; }
        internal long IssuedUnixSeconds { get; set; }
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
    /// A deliberately small post-connect compatibility exchange owned solely by Sentinel. Valheim's
    /// routed-RPC sender ID is re-bound to the current ready peer before any decision is acted upon.
    /// Requests, retries, and duplicate results are bounded and remain in memory only.
    /// </summary>
    internal sealed class SentinelNetworkCompatibility : IDisposable
    {
        internal const string RequestRpcName = "runic.sentinel.admission.request.v1";
        internal const string ResponseRpcName = "runic.sentinel.admission.response.v1";
        internal const string PluginVersion = "1.0.0";
        internal const int WireSchema = 1;
        internal const int MaximumRequestBytes = 1024;
        internal const int MaximumResponseBytes = 256;
        internal const int MaximumCachedRequests = 256;

        private const int TerminalMarker = 0x534E5431;
        private const long MaximumRequestAgeSeconds = 300L;
        private const long MaximumFutureSkewSeconds = 60L;
        private const long CacheLifetimeTicks = TimeSpan.TicksPerMinute;
        private const long RequestTimeoutTicks = 6L * TimeSpan.TicksPerSecond;
        private const long RetryIntervalTicks = 2L * TimeSpan.TicksPerSecond;
        private const long SuccessIntervalTicks = 30L * TimeSpan.TicksPerSecond;
        private const long FailureIntervalTicks = 10L * TimeSpan.TicksPerSecond;
        private const int MaximumAttempts = 3;
        private const string EvidenceProviderId = "runic.sentinel.network";

        private readonly object _gate = new object();
        private readonly ISentinelNetworkProfileSource _profiles;
        private readonly SentinelRemoteAdmissionMode _mode;
        private readonly ISentinelEvidenceProviderLease _evidence;
        private readonly Dictionary<string, CachedDecision> _decisions =
            new Dictionary<string, CachedDecision>(StringComparer.Ordinal);
        private readonly Queue<CachedDecision> _decisionOrder = new Queue<CachedDecision>();

        private ZRoutedRpc _registeredRpc;
        private PendingRequest _pending;
        private long _nextRequestTicks;
        private bool _disposed;

        internal SentinelNetworkCompatibility(
            ISentinelNetworkProfileSource profiles,
            EvidenceLedger evidence,
            SentinelRemoteAdmissionMode mode)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            if (mode < SentinelRemoteAdmissionMode.Disabled ||
                mode > SentinelRemoteAdmissionMode.Required)
                throw new ArgumentOutOfRangeException(nameof(mode));
            _mode = mode;
            if (mode != SentinelRemoteAdmissionMode.Disabled)
                _evidence = (evidence ?? throw new ArgumentNullException(nameof(evidence)))
                    .RegisterProvider(EvidenceProviderId);
        }

        internal bool IsActive
        {
            get
            {
                lock (_gate)
                    return !_disposed && _mode != SentinelRemoteAdmissionMode.Disabled &&
                           _registeredRpc != null;
            }
        }

        internal void Tick()
        {
            lock (_gate)
            {
                if (_disposed || _mode == SentinelRemoteAdmissionMode.Disabled) return;
                ZRoutedRpc routed = ZRoutedRpc.instance;
                ZNet network = ZNet.instance;
                if (routed == null || network == null) return;
                RegisterLocked(routed);

                long nowTicks = DateTime.UtcNow.Ticks;
                ExpireDecisionsLocked(nowTicks);
                if (network.IsServer())
                {
                    _pending = null;
                    return;
                }

                ZNetPeer server = network.GetServerPeer();
                long serverId = server?.m_uid ?? 0L;
                if (server == null || server.m_uid != serverId || !server.IsReady())
                {
                    _pending = null;
                    _nextRequestTicks = Math.Max(_nextRequestTicks, nowTicks + FailureIntervalTicks);
                    return;
                }

                if (_pending != null)
                {
                    if (nowTicks >= _pending.DeadlineTicks)
                    {
                        _pending = null;
                        _nextRequestTicks = nowTicks + FailureIntervalTicks;
                        return;
                    }
                    if (nowTicks >= _pending.NextAttemptTicks &&
                        _pending.Attempts < MaximumAttempts)
                    {
                        SendLocked(routed, serverId, _pending, nowTicks);
                    }
                    return;
                }

                if (nowTicks < _nextRequestTicks) return;
                SentinelNetworkProfile profile;
                try
                {
                    if (!_profiles.TryGetNetworkProfile(out profile) || profile == null)
                    {
                        _nextRequestTicks = nowTicks + FailureIntervalTicks;
                        return;
                    }
                }
                catch (Exception)
                {
                    _nextRequestTicks = nowTicks + FailureIntervalTicks;
                    return;
                }

                var envelope = new SentinelAdmissionEnvelope
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Version = PluginVersion,
                    SnapshotDigest = profile.SnapshotDigest,
                    SnapshotCapturedUnixSeconds = profile.SnapshotCapturedUnixSeconds,
                    PolicyDigest = profile.PolicyDigest,
                    PolicySequence = profile.PolicySequence,
                    PolicyProfile = profile.PolicyProfile,
                    Disposition = profile.Disposition,
                    IssuedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                _pending = new PendingRequest(
                    envelope,
                    nowTicks + RequestTimeoutTicks,
                    nowTicks);
                SendLocked(routed, serverId, _pending, nowTicks);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _pending = null;
                _registeredRpc = null;
                _decisions.Clear();
                _decisionOrder.Clear();
            }
            try { _evidence?.Dispose(); }
            catch (Exception) { }
        }

        internal static SentinelAdmissionCheck Evaluate(
            SentinelNetworkProfile local,
            SentinelAdmissionEnvelope remote,
            long nowUnixSeconds)
        {
            if (local == null) return Fail("sentinel-server-policy-unavailable");
            if (remote == null || !CanonicalRequestId(remote.RequestId) ||
                !string.Equals(remote.Version, PluginVersion, StringComparison.Ordinal) ||
                !LowerHex(remote.SnapshotDigest) || !LowerHex(remote.PolicyDigest) ||
                !CanonicalProfile(remote.PolicyProfile) || remote.PolicySequence < 0L ||
                remote.SnapshotCapturedUnixSeconds < 0L ||
                remote.Disposition < AdmissionDisposition.Unavailable ||
                remote.Disposition > AdmissionDisposition.Deny)
                return Fail("sentinel-request-malformed");
            if (remote.IssuedUnixSeconds < nowUnixSeconds - MaximumRequestAgeSeconds ||
                remote.IssuedUnixSeconds > nowUnixSeconds + MaximumFutureSkewSeconds ||
                remote.SnapshotCapturedUnixSeconds > nowUnixSeconds + MaximumFutureSkewSeconds)
                return Fail("sentinel-request-stale");
            if (!string.Equals(local.SnapshotDigest, remote.SnapshotDigest, StringComparison.Ordinal))
                return Fail("sentinel-snapshot-mismatch");
            if (!string.Equals(local.PolicyDigest, remote.PolicyDigest, StringComparison.Ordinal) ||
                local.PolicySequence != remote.PolicySequence ||
                !string.Equals(local.PolicyProfile, remote.PolicyProfile, StringComparison.Ordinal))
                return Fail("sentinel-policy-mismatch");
            if (local.Disposition != AdmissionDisposition.Allow ||
                remote.Disposition != AdmissionDisposition.Allow)
                return Fail("sentinel-policy-not-allow");
            return new SentinelAdmissionCheck(true, "compatible");
        }

        internal static ZPackage WriteRequest(SentinelAdmissionEnvelope value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var package = new ZPackage();
            package.Write(WireSchema);
            package.Write(value.RequestId ?? string.Empty);
            package.Write(value.Version ?? string.Empty);
            package.Write(value.SnapshotDigest ?? string.Empty);
            package.Write(value.SnapshotCapturedUnixSeconds);
            package.Write(value.PolicyDigest ?? string.Empty);
            package.Write(value.PolicySequence);
            package.Write(value.PolicyProfile ?? string.Empty);
            package.Write((int)value.Disposition);
            package.Write(value.IssuedUnixSeconds);
            package.Write(TerminalMarker);
            return package;
        }

        internal static bool TryReadRequest(ZPackage package, out SentinelAdmissionEnvelope value)
        {
            value = null;
            try
            {
                if (package == null || package.Size() <= 0 || package.Size() > MaximumRequestBytes ||
                    package.ReadInt() != WireSchema)
                    return false;
                var candidate = new SentinelAdmissionEnvelope
                {
                    RequestId = package.ReadString(),
                    Version = package.ReadString(),
                    SnapshotDigest = package.ReadString(),
                    SnapshotCapturedUnixSeconds = package.ReadLong(),
                    PolicyDigest = package.ReadString(),
                    PolicySequence = package.ReadLong(),
                    PolicyProfile = package.ReadString(),
                    Disposition = (AdmissionDisposition)package.ReadInt(),
                    IssuedUnixSeconds = package.ReadLong()
                };
                if (package.ReadInt() != TerminalMarker || package.GetPos() != package.Size() ||
                    !CanonicalRequestId(candidate.RequestId) ||
                    candidate.Version == null || candidate.Version.Length < 1 ||
                    candidate.Version.Length > 32 || !LowerHex(candidate.SnapshotDigest) ||
                    !LowerHex(candidate.PolicyDigest) || !CanonicalProfile(candidate.PolicyProfile))
                    return false;
                value = candidate;
                return true;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        private void RegisterLocked(ZRoutedRpc routed)
        {
            if (ReferenceEquals(_registeredRpc, routed)) return;
            routed.Register<ZPackage>(RequestRpcName, ReceiveRequest);
            routed.Register<ZPackage>(ResponseRpcName, ReceiveResponse);
            _registeredRpc = routed;
            _pending = null;
            _nextRequestTicks = 0L;
        }

        private void ReceiveRequest(long sender, ZPackage package)
        {
            lock (_gate)
            {
                if (_disposed || _mode == SentinelRemoteAdmissionMode.Disabled) return;
                ZNet network = ZNet.instance;
                ZRoutedRpc routed = ZRoutedRpc.instance;
                if (network == null || routed == null || !network.IsServer()) return;
                ZNetPeer peer = network.GetPeer(sender);
                if (peer == null || peer.m_uid != sender || !peer.IsReady()) return;

                long nowTicks = DateTime.UtcNow.Ticks;
                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                ExpireDecisionsLocked(nowTicks);
                if (!TryReadRequest(package, out SentinelAdmissionEnvelope remote))
                {
                    RecordLocked(sender, string.Empty, "sentinel-request-malformed", false);
                    DisconnectRequiredLocked(network, peer);
                    return;
                }

                string cacheKey = sender.ToString() + ":" + remote.RequestId;
                if (_decisions.TryGetValue(cacheKey, out CachedDecision cached) &&
                    cached.ExpiresTicks > nowTicks)
                {
                    SendResponseLocked(routed, sender, remote.RequestId,
                        cached.Compatible, cached.Reason);
                    if (!cached.Compatible) DisconnectRequiredLocked(network, peer);
                    return;
                }

                SentinelNetworkProfile local = null;
                try { _profiles.TryGetNetworkProfile(out local); }
                catch (Exception) { local = null; }
                SentinelAdmissionCheck check = Evaluate(local, remote, nowUnix);
                CacheLocked(cacheKey, check, nowTicks + CacheLifetimeTicks);
                SendResponseLocked(routed, sender, remote.RequestId,
                    check.Compatible, check.Reason);
                if (!check.Compatible)
                {
                    RecordLocked(sender, remote.RequestId, check.Reason, true);
                    DisconnectRequiredLocked(network, peer);
                }
            }
        }

        private void ReceiveResponse(long sender, ZPackage package)
        {
            lock (_gate)
            {
                if (_disposed || _pending == null) return;
                ZNet network = ZNet.instance;
                ZRoutedRpc routed = ZRoutedRpc.instance;
                if (network == null || routed == null || network.IsServer()) return;
                long serverId = network.GetServerPeer()?.m_uid ?? 0L;
                ZNetPeer server = network.GetPeer(sender);
                if (sender != serverId || server == null || server.m_uid != sender ||
                    !server.IsReady() ||
                    !TryReadResponse(package, out string requestId,
                        out bool compatible, out string reason) ||
                    !string.Equals(requestId, _pending.Envelope.RequestId, StringComparison.Ordinal))
                    return;
                _pending = null;
                _nextRequestTicks = DateTime.UtcNow.Ticks +
                                    (compatible ? SuccessIntervalTicks : FailureIntervalTicks);
                if (!compatible) RecordLocked(sender, requestId, reason, true);
            }
        }

        private static void SendLocked(
            ZRoutedRpc routed,
            long serverId,
            PendingRequest pending,
            long nowTicks)
        {
            ZPackage package = WriteRequest(pending.Envelope);
            if (package.Size() > MaximumRequestBytes) return;
            routed.InvokeRoutedRPC(serverId, RequestRpcName, package);
            pending.Attempts++;
            pending.NextAttemptTicks = nowTicks + RetryIntervalTicks;
        }

        private static void SendResponseLocked(
            ZRoutedRpc routed,
            long peerId,
            string requestId,
            bool compatible,
            string reason)
        {
            ZPackage package = WriteResponse(requestId, compatible, reason);
            if (package.Size() <= MaximumResponseBytes)
                routed.InvokeRoutedRPC(peerId, ResponseRpcName, package);
        }

        private static ZPackage WriteResponse(string requestId, bool compatible, string reason)
        {
            var package = new ZPackage();
            package.Write(WireSchema);
            package.Write(requestId ?? string.Empty);
            package.Write(compatible);
            package.Write(BoundedReason(reason));
            package.Write(TerminalMarker);
            return package;
        }

        private static bool TryReadResponse(
            ZPackage package,
            out string requestId,
            out bool compatible,
            out string reason)
        {
            requestId = string.Empty;
            compatible = false;
            reason = string.Empty;
            try
            {
                if (package == null || package.Size() <= 0 ||
                    package.Size() > MaximumResponseBytes || package.ReadInt() != WireSchema)
                    return false;
                requestId = package.ReadString();
                compatible = package.ReadBool();
                reason = package.ReadString();
                return CanonicalRequestId(requestId) && CanonicalReason(reason) &&
                       package.ReadInt() == TerminalMarker && package.GetPos() == package.Size();
            }
            catch (Exception)
            {
                requestId = string.Empty;
                compatible = false;
                reason = string.Empty;
                return false;
            }
        }

        private void RecordLocked(
            long peerId,
            string requestId,
            string reason,
            bool authenticated)
        {
            ISentinelEvidenceSink sink = _evidence?.Sink;
            if (sink == null) return;
            string correlation = CanonicalRequestId(requestId)
                ? requestId
                : "malformed-" + peerId.ToString();
            string detail = "reason-" + BoundedReason(reason) +
                            (authenticated ? ".authenticated-peer" : ".peer-bound");
            sink.TryAppend(
                "peer:" + peerId,
                "sentinel.remote-admission",
                correlation,
                _mode == SentinelRemoteAdmissionMode.Required
                    ? FindingConfidence.High
                    : FindingConfidence.Moderate,
                _mode == SentinelRemoteAdmissionMode.Required
                    ? EnforcementAction.Disconnect
                    : EnforcementAction.Warn,
                detail,
                out _);
        }

        private void DisconnectRequiredLocked(ZNet network, ZNetPeer exactPeer)
        {
            if (_mode == SentinelRemoteAdmissionMode.Required && exactPeer != null)
                network.Disconnect(exactPeer);
        }

        private void CacheLocked(string key, SentinelAdmissionCheck check, long expiresTicks)
        {
            while (_decisionOrder.Count >= MaximumCachedRequests)
            {
                CachedDecision oldest = _decisionOrder.Dequeue();
                if (_decisions.TryGetValue(oldest.Key, out CachedDecision current) &&
                    ReferenceEquals(oldest, current))
                    _decisions.Remove(oldest.Key);
            }
            var entry = new CachedDecision(key, check.Compatible, check.Reason, expiresTicks);
            _decisions[key] = entry;
            _decisionOrder.Enqueue(entry);
        }

        private void ExpireDecisionsLocked(long nowTicks)
        {
            while (_decisionOrder.Count > 0 && _decisionOrder.Peek().ExpiresTicks <= nowTicks)
            {
                CachedDecision expired = _decisionOrder.Dequeue();
                if (_decisions.TryGetValue(expired.Key, out CachedDecision current) &&
                    ReferenceEquals(expired, current))
                    _decisions.Remove(expired.Key);
            }
        }

        private static SentinelAdmissionCheck Fail(string reason) =>
            new SentinelAdmissionCheck(false, reason);

        private static bool CanonicalRequestId(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool LowerHex(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool CanonicalProfile(string value)
        {
            if (value == null || value.Length < 1 || value.Length > 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '.' || character == '-' || character == '_';
                if (!safe) return false;
            }
            return true;
        }

        private static bool CanonicalReason(string value)
        {
            if (value == null || value.Length < 1 || value.Length > 96) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') || character == '-' ||
                      character == '.')) return false;
            }
            return true;
        }

        private static string BoundedReason(string value) =>
            CanonicalReason(value) ? value : "sentinel-request-rejected";

        private sealed class PendingRequest
        {
            internal PendingRequest(
                SentinelAdmissionEnvelope envelope,
                long deadlineTicks,
                long nextAttemptTicks)
            {
                Envelope = envelope;
                DeadlineTicks = deadlineTicks;
                NextAttemptTicks = nextAttemptTicks;
            }

            internal SentinelAdmissionEnvelope Envelope { get; }
            internal long DeadlineTicks { get; }
            internal long NextAttemptTicks { get; set; }
            internal int Attempts { get; set; }
        }

        private sealed class CachedDecision
        {
            internal CachedDecision(
                string key,
                bool compatible,
                string reason,
                long expiresTicks)
            {
                Key = key;
                Compatible = compatible;
                Reason = reason;
                ExpiresTicks = expiresTicks;
            }

            internal string Key { get; }
            internal bool Compatible { get; }
            internal string Reason { get; }
            internal long ExpiresTicks { get; }
        }
    }
}
