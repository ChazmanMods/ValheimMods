using System;

namespace RunicSafety.Api
{
    public enum SafetyActionKind
    {
        RareItemSacrifice = 0,
        OccupiedContainerDestruction = 1,
        VehicleDestruction = 2,
        PortalOverwrite = 3,
        ProtectedDestination = 4,
        ConfiguredHighImpactAction = 5
    }

    public enum ConfirmationOutcome
    {
        Proceed = 0,
        ConfirmAgain = 1,
        Invalid = 2
    }

    public sealed class ConfirmationRequest
    {
        public ConfirmationRequest(
            SafetyActionKind action,
            string contextKey,
            string stateFingerprint,
            TimeSpan window,
            bool enabled = true)
        {
            Action = action;
            ContextKey = contextKey ?? string.Empty;
            StateFingerprint = stateFingerprint ?? string.Empty;
            Window = window;
            Enabled = enabled;
        }

        public SafetyActionKind Action { get; }
        public string ContextKey { get; }
        public string StateFingerprint { get; }
        public TimeSpan Window { get; }
        public bool Enabled { get; }
    }

    public sealed class ConfirmationDecision
    {
        internal ConfirmationDecision(ConfirmationOutcome outcome, string correlationId)
        {
            Outcome = outcome;
            CorrelationId = correlationId ?? string.Empty;
        }

        public ConfirmationOutcome Outcome { get; }
        public string CorrelationId { get; }
        public bool MayProceed => Outcome == ConfirmationOutcome.Proceed;
    }

    public interface IContextualConfirmationService
    {
        ConfirmationDecision Evaluate(ConfirmationRequest request, DateTime utcNow);
        void Cancel(string contextKey);
        void Clear();
        int PendingCount { get; }
        int Capacity { get; }
    }
}
