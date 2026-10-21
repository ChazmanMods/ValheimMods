using System;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    public sealed class TransactionCoordinatorOptions
    {
        public const int DefaultMaximumAuditRecords = 10000;
        public const int DefaultMaximumTrackedTransactions = 10000;
        public static readonly TimeSpan DefaultReservationLeaseDuration = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan DefaultTerminalRetentionDuration = TimeSpan.FromMinutes(10);

        public TransactionCoordinatorOptions(
            ITransactionAuthorizer authorizer = null,
            ITransactionClock clock = null,
            ContainerQueryPolicy queryPolicy = null,
            int maximumAuditRecords = DefaultMaximumAuditRecords,
            int maximumTrackedTransactions = DefaultMaximumTrackedTransactions,
            TimeSpan? reservationLeaseDuration = null,
            TimeSpan? terminalRetentionDuration = null)
        {
            if (maximumAuditRecords < 100 || maximumAuditRecords > 1000000)
                throw new ArgumentOutOfRangeException(nameof(maximumAuditRecords));
            if (maximumTrackedTransactions < 1 || maximumTrackedTransactions > 1000000)
                throw new ArgumentOutOfRangeException(nameof(maximumTrackedTransactions));

            TimeSpan lease = reservationLeaseDuration ?? DefaultReservationLeaseDuration;
            if (lease <= TimeSpan.Zero || lease > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(reservationLeaseDuration));

            TimeSpan retention = terminalRetentionDuration ?? DefaultTerminalRetentionDuration;
            if (retention <= TimeSpan.Zero || retention > TimeSpan.FromDays(7))
                throw new ArgumentOutOfRangeException(nameof(terminalRetentionDuration));

            Authorizer = authorizer ?? DenyAllTransactionAuthorizer.Instance;
            Clock = clock ?? SystemTransactionClock.Instance;
            QueryPolicy = queryPolicy ?? ContainerQueryPolicy.ConservativeDefault;
            MaximumAuditRecords = maximumAuditRecords;
            MaximumTrackedTransactions = maximumTrackedTransactions;
            ReservationLeaseDuration = lease;
            TerminalRetentionDuration = retention;
        }

        public ITransactionAuthorizer Authorizer { get; }
        public ITransactionClock Clock { get; }
        public ContainerQueryPolicy QueryPolicy { get; }
        public int MaximumAuditRecords { get; }
        public int MaximumTrackedTransactions { get; }
        public TimeSpan ReservationLeaseDuration { get; }
        public TimeSpan TerminalRetentionDuration { get; }
    }
}
