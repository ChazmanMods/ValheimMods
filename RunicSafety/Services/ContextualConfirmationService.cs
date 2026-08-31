using System;
using System.Collections.Generic;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class ContextualConfirmationService : IContextualConfirmationService
    {
        public const int DefaultCapacity = 128;
        private const int MaximumContextLength = 160;
        private const int MaximumFingerprintLength = 128;

        private readonly object _sync = new object();
        private readonly Dictionary<string, PendingEntry> _pending;
        private readonly ISafetyDiagnosticService _diagnostics;

        public ContextualConfirmationService(
            ISafetyDiagnosticService diagnostics,
            int capacity = DefaultCapacity)
        {
            if (capacity < 8 || capacity > 1024)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Capacity = capacity;
            _pending = new Dictionary<string, PendingEntry>(StringComparer.Ordinal);
        }

        public int Capacity { get; }

        public int PendingCount
        {
            get { lock (_sync) return _pending.Count; }
        }

        public ConfirmationDecision Evaluate(ConfirmationRequest request, DateTime utcNow)
        {
            string correlation = _diagnostics.NewCorrelationId("confirm");
            if (request == null ||
                string.IsNullOrWhiteSpace(request.ContextKey) ||
                request.ContextKey.Length > MaximumContextLength ||
                request.StateFingerprint.Length > MaximumFingerprintLength ||
                request.Window < TimeSpan.FromMilliseconds(250) ||
                request.Window > TimeSpan.FromSeconds(30))
            {
                _diagnostics.Record(correlation, "confirmation", "invalid-request", SafetyDiagnosticSeverity.Warning);
                return new ConfirmationDecision(ConfirmationOutcome.Invalid, correlation);
            }

            if (!request.Enabled)
            {
                _diagnostics.Record(correlation, "confirmation", "disabled-proceed");
                return new ConfirmationDecision(ConfirmationOutcome.Proceed, correlation);
            }

            DateTime now = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            string key = ((int)request.Action).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                         request.ContextKey;
            lock (_sync)
            {
                RemoveExpiredLocked(now);
                if (_pending.TryGetValue(key, out PendingEntry existing) &&
                    existing.ExpiresUtc >= now &&
                    string.Equals(existing.Fingerprint, request.StateFingerprint, StringComparison.Ordinal))
                {
                    _pending.Remove(key);
                    _diagnostics.Record(correlation, "confirmation", "confirmed");
                    return new ConfirmationDecision(ConfirmationOutcome.Proceed, correlation);
                }

                if (_pending.Count >= Capacity) EvictOldestLocked();
                _pending[key] = new PendingEntry(
                    request.StateFingerprint,
                    now + request.Window,
                    now);
            }

            _diagnostics.Record(correlation, "confirmation", "repeat-required");
            return new ConfirmationDecision(ConfirmationOutcome.ConfirmAgain, correlation);
        }

        public void Cancel(string contextKey)
        {
            if (string.IsNullOrEmpty(contextKey)) return;
            lock (_sync)
            {
                var keys = new List<string>();
                foreach (string key in _pending.Keys)
                    if (key.EndsWith(":" + contextKey, StringComparison.Ordinal)) keys.Add(key);
                foreach (string key in keys) _pending.Remove(key);
            }
        }

        public void Clear()
        {
            lock (_sync) _pending.Clear();
        }

        private void RemoveExpiredLocked(DateTime now)
        {
            if (_pending.Count == 0) return;
            var expired = new List<string>();
            foreach (KeyValuePair<string, PendingEntry> pair in _pending)
                if (pair.Value.ExpiresUtc < now) expired.Add(pair.Key);
            foreach (string key in expired) _pending.Remove(key);
        }

        private void EvictOldestLocked()
        {
            string oldestKey = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, PendingEntry> pair in _pending)
            {
                if (pair.Value.CreatedUtc >= oldest) continue;
                oldestKey = pair.Key;
                oldest = pair.Value.CreatedUtc;
            }
            if (oldestKey != null) _pending.Remove(oldestKey);
        }

        private readonly struct PendingEntry
        {
            internal PendingEntry(string fingerprint, DateTime expiresUtc, DateTime createdUtc)
            {
                Fingerprint = fingerprint;
                ExpiresUtc = expiresUtc;
                CreatedUtc = createdUtc;
            }

            internal string Fingerprint { get; }
            internal DateTime ExpiresUtc { get; }
            internal DateTime CreatedUtc { get; }
        }
    }
}
