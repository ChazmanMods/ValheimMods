using System;

namespace RunicSentinel.Runtime
{
    internal enum SentinelIntegrityState
    {
        Unavailable = 0,
        MonitorOnly = 1,
        Ready = 2,
        Compromised = 3
    }

    internal sealed class SentinelIntegritySnapshot
    {
        internal SentinelIntegritySnapshot(
            SentinelIntegrityState state,
            long checkedUnixSeconds,
            string reasonCode,
            string policyDigest)
        {
            if (!Enum.IsDefined(typeof(SentinelIntegrityState), state) || checkedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            CheckedUnixSeconds = checkedUnixSeconds;
            ReasonCode = string.IsNullOrEmpty(reasonCode) ? "unavailable" : reasonCode;
            PolicyDigest = policyDigest ?? string.Empty;
        }

        internal SentinelIntegrityState State { get; }
        internal long CheckedUnixSeconds { get; }
        internal string ReasonCode { get; }
        internal string PolicyDigest { get; }
    }
}
