using System;

namespace RunicTransactions.Contracts
{
    public enum TransactionOperation
    {
        QueryContainer = 0,
        TransferContainer = 1,
        ReserveMaterials = 2,
        ConsumeMaterials = 3
    }

    public enum AuthorizationPhase
    {
        Prepare = 0,
        Revalidate = 1
    }

    public sealed class TransactionAuthorizationContext
    {
        public TransactionAuthorizationContext(
            TransactionId transactionId,
            CorrelationId correlationId,
            PrincipalId principal,
            EndpointId endpoint,
            ResourceId resource,
            long amount,
            TransactionOperation operation,
            AuthorizationPhase phase)
        {
            TransactionId = transactionId;
            CorrelationId = correlationId;
            Principal = principal;
            Endpoint = endpoint;
            Resource = resource;
            Amount = amount;
            Operation = operation;
            Phase = phase;
        }

        public TransactionId TransactionId { get; }
        public CorrelationId CorrelationId { get; }
        public PrincipalId Principal { get; }
        public EndpointId Endpoint { get; }
        public ResourceId Resource { get; }
        public long Amount { get; }
        public TransactionOperation Operation { get; }
        public AuthorizationPhase Phase { get; }
    }

    public readonly struct AuthorizationDecision
    {
        private AuthorizationDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason ?? string.Empty;
        }

        public bool Allowed { get; }
        public string Reason { get; }
        public static AuthorizationDecision Allow() => new AuthorizationDecision(true, string.Empty);
        public static AuthorizationDecision Deny(string reason) =>
            new AuthorizationDecision(false, string.IsNullOrWhiteSpace(reason) ? "Permission denied." : reason);
    }

    public interface ITransactionAuthorizer
    {
        AuthorizationDecision Authorize(TransactionAuthorizationContext context);
    }

    public sealed class AllowAllTransactionAuthorizer : ITransactionAuthorizer
    {
        public static readonly AllowAllTransactionAuthorizer Instance = new AllowAllTransactionAuthorizer();
        private AllowAllTransactionAuthorizer() { }
        public AuthorizationDecision Authorize(TransactionAuthorizationContext context) => AuthorizationDecision.Allow();
    }

    /// <summary>Safe default used until an authoritative server adapter supplies a policy.</summary>
    public sealed class DenyAllTransactionAuthorizer : ITransactionAuthorizer
    {
        public static readonly DenyAllTransactionAuthorizer Instance = new DenyAllTransactionAuthorizer();
        private DenyAllTransactionAuthorizer() { }
        public AuthorizationDecision Authorize(TransactionAuthorizationContext context) =>
            AuthorizationDecision.Deny("No authoritative transaction policy is installed.");
    }
}
