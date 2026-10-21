using System;

namespace Runic.Foundation.Persistence
{
    internal enum RpcDisconnectAction
    {
        None = 0,
        Disconnect = 1,
        CloseTransport = 2
    }

    /// <summary>
    /// Keeps a rejected session latched until closure is observed. A throwing or ineffective
    /// ZNet.Disconnect cannot accidentally make that session usable again.
    /// </summary>
    internal sealed class RpcDisconnectRetryGate
    {
        private readonly int _maximumAttempts;
        private readonly long _retryTicks;
        private int _attempts;
        private long _nextAttemptUtcTicks;

        internal RpcDisconnectRetryGate(int maximumAttempts, long retryTicks)
        {
            if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            if (retryTicks < 1) throw new ArgumentOutOfRangeException(nameof(retryTicks));
            _maximumAttempts = maximumAttempts;
            _retryTicks = retryTicks;
        }

        internal bool Pending { get; private set; }
        internal int Attempts => _attempts;

        internal void MarkPending(long nowUtcTicks)
        {
            Pending = true;
            if (_nextAttemptUtcTicks == 0 || _attempts == 0)
                _nextAttemptUtcTicks = nowUtcTicks;
        }

        internal RpcDisconnectAction TakeAction(long nowUtcTicks)
        {
            if (!Pending || nowUtcTicks < _nextAttemptUtcTicks) return RpcDisconnectAction.None;
            if (_attempts >= _maximumAttempts) return RpcDisconnectAction.CloseTransport;
            _attempts++;
            _nextAttemptUtcTicks = SaturatingAdd(nowUtcTicks, _retryTicks);
            return RpcDisconnectAction.Disconnect;
        }

        internal void MarkClosed()
        {
            Pending = false;
            _nextAttemptUtcTicks = long.MaxValue;
        }

        private static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
