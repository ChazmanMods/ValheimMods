using System;
using System.Threading;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    /// <summary>
    /// Process-wide, non-reentrant guard for short synchronous mutations of external game state.
    /// Valheim inventory notifications run inline; all Runic adapters share this gate so a
    /// callback cannot enter another module while a source/destination pair is only half-applied.
    /// It fails closed instead of waiting or permitting same-thread recursion.
    /// </summary>
    public static class RunicMutationGate
    {
        private static readonly object MetadataGate = new object();
        private static int _held;
        private static string _ownerId = string.Empty;

        public static bool IsHeld => Volatile.Read(ref _held) != 0;

        public static string CurrentOwnerId
        {
            get
            {
                lock (MetadataGate) return _ownerId;
            }
        }

        public static bool TryEnter(string ownerId, out IDisposable lease)
        {
            string owner = StableIdentifier.Require(ownerId, nameof(ownerId), 160);
            if (Interlocked.CompareExchange(ref _held, 1, 0) != 0)
            {
                lease = null;
                return false;
            }

            lock (MetadataGate) _ownerId = owner;
            lease = new MutationLease();
            return true;
        }

        private static void Release()
        {
            lock (MetadataGate) _ownerId = string.Empty;
            Volatile.Write(ref _held, 0);
        }

        private sealed class MutationLease : IDisposable
        {
            private int _active = 1;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _active, 0) == 1) Release();
            }
        }
    }
}
