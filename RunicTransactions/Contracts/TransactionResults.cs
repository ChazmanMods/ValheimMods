using System;
using System.Collections.Generic;

namespace RunicTransactions.Contracts
{
    public enum TransactionState
    {
        Unknown = 0,
        Reserved = 1,
        Committed = 2,
        RolledBack = 3,
        Failed = 4,
        Preparing = 5
    }

    public enum TransactionOutcome
    {
        Reserved = 0,
        Committed = 1,
        RolledBack = 2,
        Denied = 3,
        Failed = 4
    }

    public enum TransactionDenialCode
    {
        None = 0,
        InvalidRequest = 1,
        EndpointNotFound = 2,
        PermissionDenied = 3,
        InsufficientResources = 4,
        IdempotencyConflict = 5,
        TransactionConflict = 6,
        TransactionNotFound = 7,
        RevalidationFailed = 8,
        InvalidState = 9,
        InternalFailure = 10,
        CoordinatorCapacityReached = 11,
        ReservationLeaseExpired = 12,
        TransactionInProgress = 13
    }

    public sealed class TransactionDenial
    {
        public TransactionDenial(
            TransactionDenialCode code,
            string reason,
            EndpointId? endpoint = null,
            ResourceId? resource = null)
        {
            if (code == TransactionDenialCode.None) throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            Reason = string.IsNullOrWhiteSpace(reason) ? code.ToString() : reason;
            Endpoint = endpoint;
            Resource = resource;
        }

        public TransactionDenialCode Code { get; }
        public string Reason { get; }
        public EndpointId? Endpoint { get; }
        public ResourceId? Resource { get; }
    }

    public sealed class TransactionResult
    {
        private readonly ReservationLine[] _lines;

        internal TransactionResult(
            TransactionId transactionId,
            CorrelationId correlationId,
            TransactionOutcome outcome,
            TransactionState state,
            IEnumerable<ReservationLine> lines,
            TransactionDenial denial,
            bool isReplay,
            long auditSequence)
        {
            TransactionId = transactionId;
            CorrelationId = correlationId;
            Outcome = outcome;
            State = state;
            _lines = lines == null ? Array.Empty<ReservationLine>() : new List<ReservationLine>(lines).ToArray();
            Denial = denial;
            IsReplay = isReplay;
            AuditSequence = auditSequence;
        }

        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public TransactionOutcome Outcome { get; }
        public TransactionState State { get; }
        public IReadOnlyList<ReservationLine> Lines => _lines;
        public TransactionDenial Denial { get; }
        public bool IsReplay { get; }
        public long AuditSequence { get; }
        public bool IsSuccess => Outcome == TransactionOutcome.Reserved ||
                                 Outcome == TransactionOutcome.Committed ||
                                 Outcome == TransactionOutcome.RolledBack;

        internal TransactionResult AsReplay(CorrelationId correlationId) => new TransactionResult(
            TransactionId,
            correlationId,
            Outcome,
            State,
            _lines,
            Denial,
            true,
            AuditSequence);
    }

    public sealed class TransactionSnapshot
    {
        private readonly ReservationLine[] _lines;

        internal TransactionSnapshot(
            TransactionId transactionId,
            CorrelationId correlationId,
            IdempotencyKey idempotencyKey,
            PrincipalId principal,
            TransactionState state,
            IEnumerable<ReservationLine> lines,
            DateTimeOffset? reservationExpiresAtUtc,
            DateTimeOffset? terminalAtUtc)
        {
            TransactionId = transactionId;
            CorrelationId = correlationId;
            IdempotencyKey = idempotencyKey;
            Principal = principal;
            State = state;
            _lines = new List<ReservationLine>(lines).ToArray();
            ReservationExpiresAtUtc = reservationExpiresAtUtc;
            TerminalAtUtc = terminalAtUtc;
        }

        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public IdempotencyKey IdempotencyKey { get; }
        public PrincipalId Principal { get; }
        public TransactionState State { get; }
        public IReadOnlyList<ReservationLine> Lines => _lines;
        public DateTimeOffset? ReservationExpiresAtUtc { get; }
        public DateTimeOffset? TerminalAtUtc { get; }
    }
}
