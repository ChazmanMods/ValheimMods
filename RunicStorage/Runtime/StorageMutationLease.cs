using System;
using System.Threading;

namespace RunicStorage.Runtime
{
    internal sealed class StorageMutationLease : IDisposable
    {
        private readonly RunicAutomation.MutationLease _shared;
        private int _owned;

        private StorageMutationLease(Player player, RunicAutomation.MutationLease shared)
        {
            _shared = shared;
            Player = player;
            Inventory = player.GetInventory();
            _owned = 1;
            _shared.Track(Inventory);
        }

        internal Player Player { get; }
        internal Inventory Inventory { get; }

        internal static bool TryBegin(Player player, out StorageMutationLease lease)
        {
            lease = null;
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !player.IsOwner() || RunicAutomation.MutationGate.IsBlocked(player.GetInventory()) ||
                !RunicAutomation.MutationGate.TryBegin("storage", out var shared))
                return false;
            lease = new StorageMutationLease(player, shared);
            return true;
        }

        internal bool Covers(Player player, Inventory inventory) =>
            Volatile.Read(ref _owned) == 1 && ReferenceEquals(Player, player) &&
            ReferenceEquals(Inventory, inventory);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owned, 0) != 1) return;
            _shared.Dispose();
        }
    }
}
