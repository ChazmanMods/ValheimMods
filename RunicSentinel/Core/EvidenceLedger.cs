using System;
using System.Collections.Generic;
using System.Globalization;
using Runic.Foundation.Core;
using RunicSentinel.Contracts;

namespace RunicSentinel.Core
{
    internal sealed class EvidenceLedger : ISentinelEvidenceService
    {
        internal const int Capacity = 256;
        internal const int MaximumProviders = 32;
        internal const int CapacityPerProvider = Capacity / MaximumProviders;

        private readonly object _gate = new object();
        private readonly Dictionary<string, ProviderState> _providers =
            new Dictionary<string, ProviderState>(StringComparer.Ordinal);
        private long _sequence;
        private long _token;
        private long _policySequence;

        public ISentinelEvidenceProviderLease RegisterProvider(string providerId)
        {
            string moduleId = RunicIdentifier.Require(providerId, nameof(providerId));
            lock (_gate)
            {
                if (_token == long.MaxValue)
                    throw new InvalidOperationException("The evidence provider token space is exhausted.");
                _providers.TryGetValue(moduleId, out ProviderState state);
                if (state != null && IsActiveLocked(state))
                    throw new InvalidOperationException("An active lease owns this evidence provider ID.");
                if (state == null)
                {
                    PruneInactiveLocked();
                    if (_providers.Count >= MaximumProviders)
                        throw new InvalidOperationException("The bounded evidence provider registry is full.");
                    state = new ProviderState(moduleId);
                    _providers.Add(moduleId, state);
                }
                long token = ++_token;
                state.Token = token;
                var sink = new ProviderSink(this, moduleId, token);
                return new ProviderLease(this, moduleId, token, sink);
            }
        }

        public EvidenceReadSnapshot ReadAfter(long sequence, int maximum)
        {
            maximum = Math.Max(0, Math.Min(Capacity, maximum));
            long after = Math.Max(0L, sequence);
            lock (_gate)
            {
                var entries = new List<SecurityEvidence>();
                var providers = new List<EvidenceProviderStatus>(_providers.Count);
                foreach (ProviderState state in _providers.Values)
                {
                    foreach (SecurityEvidence evidence in state.Entries)
                        if (evidence.Sequence > after) entries.Add(evidence);
                    providers.Add(new EvidenceProviderStatus(
                        state.ModuleId,
                        IsActiveLocked(state),
                        state.Entries.Count,
                        state.Accepted,
                        state.Dropped));
                }
                entries.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
                if (entries.Count > maximum) entries.RemoveRange(maximum, entries.Count - maximum);
                providers.Sort((left, right) =>
                    StringComparer.Ordinal.Compare(left.ProviderModuleId, right.ProviderModuleId));
                return new EvidenceReadSnapshot(
                    _sequence,
                    _policySequence,
                    entries,
                    providers);
            }
        }

        internal void SetPolicySequence(long sequence)
        {
            lock (_gate) _policySequence = Math.Max(0L, sequence);
        }

        private bool TryAppend(
            string moduleId,
            long token,
            string actor,
            string rule,
            string correlationId,
            FindingConfidence confidence,
            EnforcementAction requestedAction,
            string detail,
            out SecurityEvidence accepted)
        {
            accepted = null;
            lock (_gate)
            {
                if (!_providers.TryGetValue(moduleId, out ProviderState state) ||
                    state.Token != token) return false;
                if (!IsActiveLocked(state) || !BoundedSafe(actor, 128) ||
                    !BoundedSafe(rule, 128) || !BoundedSafe(correlationId, 128) ||
                    !BoundedSafe(detail, 512) || !IsConfidence(confidence) ||
                    !IsAction(requestedAction) || _sequence == long.MaxValue)
                {
                    state.Dropped = SaturatingIncrement(state.Dropped);
                    return false;
                }

                EnforcementAction effective = requestedAction;
                if (confidence <= FindingConfidence.Moderate && effective > EnforcementAction.Warn)
                    effective = EnforcementAction.Warn;
                if (confidence < FindingConfidence.Conclusive && effective == EnforcementAction.Ban)
                    effective = EnforcementAction.Quarantine;
                long next = _sequence + 1L;
                accepted = new SecurityEvidence(
                    next,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    moduleId,
                    actor,
                    rule,
                    correlationId,
                    confidence,
                    requestedAction,
                    effective,
                    _policySequence,
                    detail);
                _sequence = next;
                if (state.Entries.Count == CapacityPerProvider)
                {
                    state.Entries.Dequeue();
                    state.Dropped = SaturatingIncrement(state.Dropped);
                }
                state.Entries.Enqueue(accepted);
                state.Accepted = SaturatingIncrement(state.Accepted);
                return true;
            }
        }

        private static bool IsActiveLocked(ProviderState state) => state.Token > 0L;

        private void Release(string moduleId, long token)
        {
            lock (_gate)
                if (_providers.TryGetValue(moduleId, out ProviderState state) && state.Token == token)
                {
                    state.Token = 0L;
                }
        }

        private bool IsLeaseActive(string moduleId, long token)
        {
            lock (_gate)
                return _providers.TryGetValue(moduleId, out ProviderState state) &&
                       state.Token == token && IsActiveLocked(state);
        }

        private void PruneInactiveLocked()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, ProviderState> pair in _providers)
                if (!IsActiveLocked(pair.Value)) stale.Add(pair.Key);
            for (int index = 0; index < stale.Count; index++) _providers.Remove(stale[index]);
        }

        private static bool BoundedSafe(string value, int maximum)
        {
            if (value == null || value.Length == 0 || value.Length > maximum) return false;
            for (int index = 0; index < value.Length; index++)
            {
                UnicodeCategory category = char.GetUnicodeCategory(value[index]);
                if (char.IsControl(value[index]) || category == UnicodeCategory.Format ||
                    category == UnicodeCategory.Surrogate) return false;
            }
            return true;
        }

        private static bool IsConfidence(FindingConfidence value) =>
            value >= FindingConfidence.Informational && value <= FindingConfidence.Conclusive;
        private static bool IsAction(EnforcementAction value) =>
            value >= EnforcementAction.Log && value <= EnforcementAction.Ban;
        private static long SaturatingIncrement(long value) =>
            value == long.MaxValue ? value : value + 1L;

        private sealed class ProviderState
        {
            internal ProviderState(string moduleId)
            {
                ModuleId = moduleId;
                Entries = new Queue<SecurityEvidence>(CapacityPerProvider);
            }

            internal string ModuleId { get; }
            internal Queue<SecurityEvidence> Entries { get; }
            internal long Token { get; set; }
            internal long Accepted { get; set; }
            internal long Dropped { get; set; }
        }

        private sealed class ProviderSink : ISentinelEvidenceSink
        {
            private readonly EvidenceLedger _owner;
            private readonly string _moduleId;
            private readonly long _token;

            internal ProviderSink(EvidenceLedger owner, string moduleId, long token)
            {
                _owner = owner;
                _moduleId = moduleId;
                _token = token;
            }

            public bool TryAppend(
                string actor,
                string rule,
                string correlationId,
                FindingConfidence confidence,
                EnforcementAction requestedAction,
                string detail,
                out SecurityEvidence accepted) =>
                _owner.TryAppend(
                    _moduleId,
                    _token,
                    actor,
                    rule,
                    correlationId,
                    confidence,
                    requestedAction,
                    detail,
                    out accepted);
        }

        private sealed class ProviderLease : ISentinelEvidenceProviderLease
        {
            private EvidenceLedger _owner;
            private readonly long _token;

            internal ProviderLease(
                EvidenceLedger owner,
                string moduleId,
                long token,
                ISentinelEvidenceSink sink)
            {
                _owner = owner;
                ProviderModuleId = moduleId;
                _token = token;
                Sink = sink;
            }

            public string ProviderModuleId { get; }
            public bool IsActive => _owner != null &&
                                    _owner.IsLeaseActive(ProviderModuleId, _token);
            public ISentinelEvidenceSink Sink { get; }

            public void Dispose()
            {
                EvidenceLedger owner = _owner;
                _owner = null;
                owner?.Release(ProviderModuleId, _token);
            }
        }
    }
}
