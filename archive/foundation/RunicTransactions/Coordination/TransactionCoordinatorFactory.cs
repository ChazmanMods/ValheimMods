using System;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    /// <summary>
    /// Creates isolated coordinators only after a server adapter supplies an authoritative policy.
    /// </summary>
    public interface ITransactionCoordinatorFactory
    {
        ITransactionCoordinator Create(
            ITransactionAuthorizer authorizer,
            ContainerQueryPolicy queryPolicy = null,
            int maximumAuditRecords = TransactionCoordinatorOptions.DefaultMaximumAuditRecords,
            int maximumTrackedTransactions = TransactionCoordinatorOptions.DefaultMaximumTrackedTransactions,
            TimeSpan? reservationLeaseDuration = null,
            TimeSpan? terminalRetentionDuration = null,
            ITransactionClock clock = null);
    }

    public sealed class TransactionCoordinatorFactory : ITransactionCoordinatorFactory
    {
        public ITransactionCoordinator Create(
            ITransactionAuthorizer authorizer,
            ContainerQueryPolicy queryPolicy = null,
            int maximumAuditRecords = TransactionCoordinatorOptions.DefaultMaximumAuditRecords,
            int maximumTrackedTransactions = TransactionCoordinatorOptions.DefaultMaximumTrackedTransactions,
            TimeSpan? reservationLeaseDuration = null,
            TimeSpan? terminalRetentionDuration = null,
            ITransactionClock clock = null)
        {
            if (authorizer == null) throw new ArgumentNullException(nameof(authorizer));
            return new InMemoryTransactionCoordinator(new TransactionCoordinatorOptions(
                authorizer,
                clock,
                queryPolicy: queryPolicy,
                maximumAuditRecords: maximumAuditRecords,
                maximumTrackedTransactions: maximumTrackedTransactions,
                reservationLeaseDuration: reservationLeaseDuration,
                terminalRetentionDuration: terminalRetentionDuration));
        }
    }
}
