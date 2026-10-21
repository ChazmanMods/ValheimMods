namespace RunicPermissions.Contracts
{
    public enum PermissionOutcome
    {
        Denied = 0,
        Allowed = 1,
        AuditRequired = 2
    }

    /// <summary>Stable, localizable evaluator result codes.</summary>
    public enum PermissionReason
    {
        InvalidRequest = 1,
        IdentityMissing = 2,
        IdentityAmbiguous = 3,
        IdentityStale = 4,
        OwnershipMissing = 5,
        OwnershipAmbiguous = 6,
        OwnershipStale = 7,
        OwnershipInvalid = 8,
        ProfileMissing = 9,
        ProfileAmbiguous = 10,
        ProfileStale = 11,
        ProfileInvalid = 12,
        ScopeNotConfigured = 13,
        PolicyInvalid = 14,
        ServerExplicitDeny = 15,
        ObjectExplicitDeny = 16,
        WardContextMissing = 17,
        WardAmbiguous = 18,
        WardStale = 19,
        OverlappingHostileWards = 20,
        WardDenied = 21,
        ActiveWardRequired = 22,
        GroupProviderMissing = 23,
        GroupMissing = 24,
        GroupAmbiguous = 25,
        GroupStale = 26,
        GroupContextMismatch = 27,
        NotGroupMember = 28,
        NotApproved = 29,
        NotOwner = 30,
        NobodyPolicy = 31,
        AllowedEveryone = 32,
        AllowedApproved = 33,
        AllowedOwner = 34,
        AllowedWard = 35,
        AllowedGroup = 36,
        AllowedExplicit = 37,
        AdminBypassAuditRequired = 38,
        AllowedAdminBypass = 39,
        AdminAuditUnavailable = 40
    }

    public sealed class PermissionEvaluation
    {
        private PermissionEvaluation(
            PermissionOutcome outcome,
            PermissionReason reason,
            bool adminBypass,
            bool auditRecorded)
        {
            Outcome = outcome;
            Reason = reason;
            IsAdminBypass = adminBypass;
            AuditRecorded = auditRecorded;
        }

        public PermissionOutcome Outcome { get; }

        public PermissionReason Reason { get; }

        public bool IsAdminBypass { get; }

        public bool AuditRecorded { get; }

        public bool IsAllowed => Outcome == PermissionOutcome.Allowed;

        public static PermissionEvaluation Allow(PermissionReason reason) =>
            new PermissionEvaluation(PermissionOutcome.Allowed, reason, false, false);

        public static PermissionEvaluation Deny(PermissionReason reason) =>
            new PermissionEvaluation(PermissionOutcome.Denied, reason, false, false);

        public static PermissionEvaluation RequireAdminAudit() =>
            new PermissionEvaluation(
                PermissionOutcome.AuditRequired,
                PermissionReason.AdminBypassAuditRequired,
                true,
                false);

        internal static PermissionEvaluation AllowAuditedAdmin() =>
            new PermissionEvaluation(
                PermissionOutcome.Allowed,
                PermissionReason.AllowedAdminBypass,
                true,
                true);
    }

    public sealed class AdminBypassAuditRecord
    {
        public AdminBypassAuditRecord(PermissionEvaluationRequest request)
        {
            EvaluationId = request.EvaluationId;
            ResourceId = request.ResourceId;
            Subject = request.Subject.Identity;
            Action = request.Action;
            AdminSessionId = request.AdminBypass.AdminSessionId;
            Justification = request.AdminBypass.Justification;
        }

        public string EvaluationId { get; }

        public string ResourceId { get; }

        public StableIdentity Subject { get; }

        public PermissionAction Action { get; }

        public string AdminSessionId { get; }

        public string Justification { get; }
    }

    public interface IPermissionAuditSink
    {
        bool TryRecord(AdminBypassAuditRecord record, out string failureReason);
    }

    public interface IPermissionEvaluationService
    {
        PermissionEvaluation Evaluate(PermissionEvaluationRequest request);
    }

    public static class PermissionCapabilities
    {
        public const string Evaluate = "permissions.evaluate";
        public const int ProtocolMajor = 1;
        public const int ProtocolMinor = 0;
        public const string ProtocolVersion = "1.0";
    }
}
