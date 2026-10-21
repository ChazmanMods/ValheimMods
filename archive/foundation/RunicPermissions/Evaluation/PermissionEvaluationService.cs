using RunicPermissions.Contracts;

namespace RunicPermissions.Evaluation
{
    /// <summary>
    /// Public capability implementation. Administrative access is only returned after
    /// its mandatory audit record has been accepted by the configured sink.
    /// </summary>
    public sealed class PermissionEvaluationService : IPermissionEvaluationService
    {
        private readonly PermissionEvaluator _evaluator;
        private readonly IPermissionAuditSink _auditSink;

        public PermissionEvaluationService(
            PermissionEvaluator evaluator,
            IPermissionAuditSink auditSink)
        {
            _evaluator = evaluator ?? throw new System.ArgumentNullException(nameof(evaluator));
            _auditSink = auditSink;
        }

        public PermissionEvaluation Evaluate(PermissionEvaluationRequest request)
        {
            PermissionEvaluation result = _evaluator.Evaluate(request);
            if (result.Outcome != PermissionOutcome.AuditRequired) return result;

            if (_auditSink == null ||
                !_auditSink.TryRecord(new AdminBypassAuditRecord(request), out _))
                return PermissionEvaluation.Deny(PermissionReason.AdminAuditUnavailable);

            return PermissionEvaluation.AllowAuditedAdmin();
        }
    }
}
