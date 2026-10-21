using System.Collections.Generic;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    /// <summary>
    /// Process-local all-or-nothing resource coordinator. A server adapter owns endpoint discovery
    /// and synchronizes authoritative Valheim inventories into this service.
    /// </summary>
    public interface ITransactionCoordinator
    {
        ContainerQueryPolicy QueryPolicy { get; }
        int TrackedTransactionCount { get; }
        void RegisterOrReplaceEndpoint(EndpointId endpoint, IEnumerable<ResourceBalance> balances);
        bool TryGetEndpointSnapshot(EndpointId endpoint, out EndpointInventorySnapshot snapshot);
        TransactionResult ReserveExact(ReservationRequest request);
        TransactionResult CommitOnce(TransactionId transactionId, CorrelationId correlationId);
        TransactionResult Rollback(TransactionId transactionId, CorrelationId correlationId, string reason);
        bool TryGetTransaction(TransactionId transactionId, out TransactionSnapshot snapshot);
        IReadOnlyList<TransactionAuditRecord> GetAuditTrail(TransactionId transactionId);
        TransactionPruneResult PruneExpired();
    }

    public readonly struct TransactionPruneResult
    {
        public TransactionPruneResult(int expiredReservations, int removedTerminalTransactions, int trackedTransactions)
        {
            ExpiredReservations = expiredReservations;
            RemovedTerminalTransactions = removedTerminalTransactions;
            TrackedTransactions = trackedTransactions;
        }

        public int ExpiredReservations { get; }
        public int RemovedTerminalTransactions { get; }
        public int TrackedTransactions { get; }
    }

    public sealed class EndpointInventorySnapshot
    {
        private readonly ResourceInventorySnapshot[] _resources;

        internal EndpointInventorySnapshot(EndpointId endpoint, long version, ResourceInventorySnapshot[] resources)
        {
            Endpoint = endpoint;
            Version = version;
            _resources = resources;
        }

        public EndpointId Endpoint { get; }
        public long Version { get; }
        public IReadOnlyList<ResourceInventorySnapshot> Resources => _resources;

        public long OnHand(ResourceId resource)
        {
            for (int i = 0; i < _resources.Length; i++)
                if (_resources[i].Resource.Equals(resource)) return _resources[i].OnHand;
            return 0;
        }

        public long Reserved(ResourceId resource)
        {
            for (int i = 0; i < _resources.Length; i++)
                if (_resources[i].Resource.Equals(resource)) return _resources[i].Reserved;
            return 0;
        }
    }

    public readonly struct ResourceInventorySnapshot
    {
        internal ResourceInventorySnapshot(ResourceId resource, long onHand, long reserved)
        {
            Resource = resource;
            OnHand = onHand;
            Reserved = reserved;
        }

        public ResourceId Resource { get; }
        public long OnHand { get; }
        public long Reserved { get; }
        public long Available => OnHand > Reserved ? OnHand - Reserved : 0;
    }
}
