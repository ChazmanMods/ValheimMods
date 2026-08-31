using System;
using System.Threading;

namespace RunicStorage.Runtime
{
    internal sealed class StorageMutationLease : IDisposable
    {
        private static int _active;
        private int _owned;

        private StorageMutationLease(Player player)
        {
            Player = player;
            Inventory = player.GetInventory();
            _owned = 1;
        }

        internal Player Player { get; }
        internal Inventory Inventory { get; }

        internal static bool TryBegin(Player player, out StorageMutationLease lease)
        {
            lease = null;
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !player.IsOwner() || Interlocked.CompareExchange(ref _active, 1, 0) != 0)
                return false;
            lease = new StorageMutationLease(player);
            return true;
        }

        internal bool Covers(Player player, Inventory inventory) =>
            Volatile.Read(ref _owned) == 1 && ReferenceEquals(Player, player) &&
            ReferenceEquals(Inventory, inventory);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owned, 0) != 1) return;
            Volatile.Write(ref _active, 0);
        }
    }
}
