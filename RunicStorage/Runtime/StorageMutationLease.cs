using System;
using System.Threading;

namespace RunicStorage.Runtime
{
    internal enum StorageMutationDenial { None, InvalidOwner, RecoveryRequired, OperationActive }
    internal sealed class StorageMutationLease : IDisposable
    {
        private readonly RunicAutomation.MutationLease _shared;
        private int _owned;

        private StorageMutationLease(Player player, RunicAutomation.MutationLease shared, bool trackPlayer)
        {
            _shared = shared;
            Player = player;
            Inventory = player.GetInventory();
            _owned = 1;
            if (trackPlayer) _shared.Track(Inventory);
        }

        internal Player Player { get; }
        internal Inventory Inventory { get; }

        internal static bool TryBegin(Player player, out StorageMutationLease lease)
            => TryBegin(player, out lease, out _);

        internal static bool TryBegin(Player player, out StorageMutationLease lease,
            out StorageMutationDenial denial, bool trackPlayer = true)
        {
            lease = null;
            denial = StorageMutationDenial.InvalidOwner;
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) ||
                !player.IsOwner()) return false;
            denial = StorageMutationDenial.RecoveryRequired;
            if (trackPlayer && RunicAutomation.MutationGate.IsBlocked(player.GetInventory())) return false;
            denial = StorageMutationDenial.OperationActive;
            if (!RunicAutomation.MutationGate.TryBegin("storage", out var shared)) return false;
            try { lease = new StorageMutationLease(player, shared, trackPlayer); }
            catch { shared.Dispose(); throw; }
            denial = StorageMutationDenial.None;
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
