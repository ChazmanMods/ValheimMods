using System;

namespace RunicTransactions.Contracts
{
    public enum TransactionAuditAction
    {
        Reserve = 0,
        Commit = 1,
        Rollback = 2,
        InventoryReplace = 3,
        Expire = 4
    }

    public sealed class TransactionAuditRecord
    {
        public TransactionAuditRecord(
            long sequence,
            DateTimeOffset timestampUtc,
            TransactionId transactionId,
            CorrelationId correlationId,
            PrincipalId principal,
            TransactionAuditAction action,
            TransactionOutcome outcome,
            TransactionDenialCode denialCode,
            string detail)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            TransactionId = transactionId;
            CorrelationId = correlationId;
            Principal = principal;
            Action = action;
            Outcome = outcome;
            DenialCode = denialCode;
            Detail = detail ?? string.Empty;
        }

        public long Sequence { get; }
        public DateTimeOffset TimestampUtc { get; }
        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public PrincipalId Principal { get; }
        public TransactionAuditAction Action { get; }
        public TransactionOutcome Outcome { get; }
        public TransactionDenialCode DenialCode { get; }
        public string Detail { get; }
    }

    public interface ITransactionClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public sealed class SystemTransactionClock : ITransactionClock
    {
        public static readonly SystemTransactionClock Instance = new SystemTransactionClock();
        private SystemTransactionClock() { }
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
