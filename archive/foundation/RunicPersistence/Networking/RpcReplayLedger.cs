using System;
using System.Collections.Generic;

namespace Runic.Foundation.Persistence
{
    internal enum RpcReplayAdmission
    {
        New = 0,
        Replay = 1,
        Conflict = 2,
        InProgress = 3,
        CapacityReached = 4
    }

    internal sealed class RpcReplayLedger
    {
        internal const int DefaultMaximumEntries = 10000;
        internal static readonly long DefaultRetentionTicks = TimeSpan.FromMinutes(10).Ticks;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int _maximumEntries;
        private readonly long _retentionTicks;

        internal RpcReplayLedger(
            int maximumEntries = DefaultMaximumEntries,
            long retentionTicks = 0)
        {
            if (maximumEntries < 1 || maximumEntries > 1000000)
                throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            if (retentionTicks == 0) retentionTicks = DefaultRetentionTicks;
            if (retentionTicks <= 0 || retentionTicks > TimeSpan.FromDays(7).Ticks)
                throw new ArgumentOutOfRangeException(nameof(retentionTicks));
            _maximumEntries = maximumEntries;
            _retentionTicks = retentionTicks;
        }

        internal int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        internal RpcReplayAdmission TryBegin(
            string scopedKey,
            string fingerprint,
            long nowUtcTicks,
            out RpcHandlerResult cached)
        {
            ValidateKey(scopedKey, nameof(scopedKey));
            ValidateFingerprint(fingerprint);
            ValidateTicks(nowUtcTicks, nameof(nowUtcTicks));
            cached = null;

            lock (_gate)
            {
                PruneLocked(nowUtcTicks);
                if (_entries.TryGetValue(scopedKey, out Entry existing))
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                        return RpcReplayAdmission.Conflict;
                    if (existing.Retryable)
                    {
                        existing.Retryable = false;
                        existing.ExpiresUtcTicks = SaturatingAdd(
                            nowUtcTicks, _retentionTicks);
                        return RpcReplayAdmission.New;
                    }
                    if (!existing.Completed) return RpcReplayAdmission.InProgress;
                    cached = existing.Result;
                    return RpcReplayAdmission.Replay;
                }

                if (_entries.Count >= _maximumEntries)
                    return RpcReplayAdmission.CapacityReached;
                _entries.Add(scopedKey, new Entry(fingerprint, SaturatingAdd(nowUtcTicks, _retentionTicks)));
                return RpcReplayAdmission.New;
            }
        }

        internal bool Complete(
            string scopedKey,
            string fingerprint,
            RpcHandlerResult result,
            long nowUtcTicks)
        {
            ValidateKey(scopedKey, nameof(scopedKey));
            ValidateFingerprint(fingerprint);
            if (result == null) throw new ArgumentNullException(nameof(result));
            ValidateTicks(nowUtcTicks, nameof(nowUtcTicks));

            lock (_gate)
            {
                if (!_entries.TryGetValue(scopedKey, out Entry entry) ||
                    !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal) ||
                    entry.Completed)
                    return false;
                entry.Result = new RpcHandlerResult(result.Code, result.ReasonCode, result.Payload);
                entry.Completed = true;
                entry.ExpiresUtcTicks = SaturatingAdd(nowUtcTicks, _retentionTicks);
                return true;
            }
        }

        /// <summary>
        /// Marks one exact in-progress reservation retryable after a handler reports a transient
        /// NotReady result. The scoped key and fingerprint remain retained, so an alternate
        /// fingerprint still conflicts and repeated retryable failures consume one bounded slot.
        /// </summary>
        internal bool Abandon(string scopedKey, string fingerprint)
        {
            ValidateKey(scopedKey, nameof(scopedKey));
            ValidateFingerprint(fingerprint);
            lock (_gate)
            {
                if (!_entries.TryGetValue(scopedKey, out Entry entry) || entry.Completed ||
                    !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return false;
                entry.Retryable = true;
                return true;
            }
        }

        internal int Prune(long nowUtcTicks)
        {
            ValidateTicks(nowUtcTicks, nameof(nowUtcTicks));
            lock (_gate) return PruneLocked(nowUtcTicks);
        }

        private int PruneLocked(long nowUtcTicks)
        {
            var expired = new List<string>();
            foreach (KeyValuePair<string, Entry> pair in _entries)
                if (pair.Value.ExpiresUtcTicks <= nowUtcTicks) expired.Add(pair.Key);
            foreach (string key in expired) _entries.Remove(key);
            return expired.Count;
        }

        private static long SaturatingAdd(long left, long right) =>
            left > DateTime.MaxValue.Ticks - right ? DateTime.MaxValue.Ticks : left + right;

        private static void ValidateKey(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void ValidateFingerprint(string value)
        {
            if (value == null || value.Length != 64) throw new ArgumentOutOfRangeException(nameof(value));
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    throw new ArgumentException("Fingerprint is not canonical lowercase SHA-256.", nameof(value));
            }
        }

        private static void ValidateTicks(long value, string name)
        {
            if (value < 0 || value > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException(name);
        }

        private sealed class Entry
        {
            internal Entry(string fingerprint, long expiresUtcTicks)
            {
                Fingerprint = fingerprint;
                ExpiresUtcTicks = expiresUtcTicks;
            }

            internal readonly string Fingerprint;
            internal long ExpiresUtcTicks;
            internal bool Completed;
            internal bool Retryable;
            internal RpcHandlerResult Result;
        }
    }
}
