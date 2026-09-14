using System;
using System.Collections.Generic;
using Local = RunicSentinel.Contracts;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelEnforcementRuntime : IDisposable
    {
        private const int MaximumTrackedPeers = 256;
        private readonly object _gate = new object();
        private readonly SentinelRuntime _runtime;
        private readonly Local.ISentinelEvidenceProviderLease _evidence;
        private readonly Dictionary<long, EscalationState> _states =
            new Dictionary<long, EscalationState>();
        private bool _disposed;

        internal SentinelEnforcementRuntime(SentinelRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _evidence = runtime.Evidence.RegisterProvider("runic.sentinel.enforcement");
        }

        internal bool ReportRejectedServerRequest(
            string sourceModuleId,
            long peerId,
            string actor,
            string rule,
            string correlationId,
            Local.FindingConfidence confidence,
            string detail)
        {
            if (sourceModuleId != "runic.portals" || peerId == 0L ||
                ZNet.instance == null || !ZNet.instance.IsServer()) return false;
            bool disconnect = false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (_gate)
            {
                if (_disposed) return false;
                Prune(now);
                long window = Math.Max(10, Math.Min(600,
                    SentinelConfig.EnforcementWindowSeconds?.Value ?? 60));
                int veryHigh = Math.Max(1, Math.Min(10,
                    SentinelConfig.VeryHighDisconnectCount?.Value ?? 2));
                int high = Math.Max(1, Math.Min(20,
                    SentinelConfig.HighDisconnectCount?.Value ?? 3));
                if (!_states.TryGetValue(peerId, out EscalationState state) ||
                    now - state.Started > window) state = new EscalationState(now);
                if (confidence >= Local.FindingConfidence.High) state.High++;
                if (confidence >= Local.FindingConfidence.VeryHigh) state.VeryHigh++;
                disconnect = confidence == Local.FindingConfidence.Conclusive ||
                             state.VeryHigh >= veryHigh || state.High >= high;
                _states[peerId] = state;
                _evidence.Sink.TryAppend(
                    Safe(actor, "peer:" + peerId), Safe(rule, "security-violation"),
                    Safe(correlationId, Guid.NewGuid().ToString("N")), confidence,
                    disconnect ? Local.EnforcementAction.Disconnect : Local.EnforcementAction.Cancel,
                    Safe(detail, "request-denied"), out _);
            }
            if (!disconnect) return true;
            try
            {
                ZNetPeer peer = ZNet.instance.GetPeer(peerId);
                if (peer != null && peer.m_uid == peerId && peer.IsReady())
                    ZNet.instance.Disconnect(peer);
            }
            catch { }
            return true;
        }

        private void Prune(long now)
        {
            long window = Math.Max(10, Math.Min(600,
                SentinelConfig.EnforcementWindowSeconds?.Value ?? 60));
            var expired = new List<long>();
            foreach (KeyValuePair<long, EscalationState> pair in _states)
                if (now - pair.Value.Started > window) expired.Add(pair.Key);
            foreach (long id in expired) _states.Remove(id);
            if (_states.Count < MaximumTrackedPeers) return;
            long oldestId = 0L;
            long oldest = long.MaxValue;
            foreach (KeyValuePair<long, EscalationState> pair in _states)
                if (pair.Value.Started < oldest) { oldest = pair.Value.Started; oldestId = pair.Key; }
            _states.Remove(oldestId);
        }

        private static string Safe(string value, string fallback)
        {
            string result = string.IsNullOrEmpty(value) ? fallback : value;
            if (result.Length > 128) result = result.Substring(0, 128);
            return result;
        }

        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; _states.Clear(); }
            try { _evidence.Dispose(); } catch { }
        }

        private sealed class EscalationState
        {
            internal EscalationState(long started) { Started = started; }
            internal long Started { get; }
            internal int High { get; set; }
            internal int VeryHigh { get; set; }
        }
    }
}
