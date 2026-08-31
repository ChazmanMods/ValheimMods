using RunicPermissions.Groups;

namespace RunicPermissions.Contracts
{
    public enum WardState
    {
        None = 0,
        Allows = 1,
        Denies = 2,
        Ambiguous = 3,
        Stale = 4,
        OverlappingHostile = 5
    }

    /// <summary>
    /// Server-resolved ward result at the target. PublicExceptionsAllowed must only be
    /// true when the ward owner explicitly enabled object-local public exceptions.
    /// </summary>
    public sealed class WardContext
    {
        public WardContext(WardState state, bool publicExceptionsAllowed = false, string wardId = "")
        {
            State = state;
            PublicExceptionsAllowed = publicExceptionsAllowed;
            WardId = (wardId ?? string.Empty).Trim();
        }

        public WardState State { get; }

        public bool PublicExceptionsAllowed { get; }

        public string WardId { get; }

        public static WardContext NoWard { get; } = new WardContext(WardState.None);
    }

    public enum GroupResolutionStatus
    {
        Available = 0,
        ProviderMissing = 1,
        GroupMissing = 2,
        Ambiguous = 3,
        Stale = 4
    }

    /// <summary>A server-resolved answer from an optional group provider.</summary>
    public sealed class GroupMembershipContext
    {
        public GroupMembershipContext(
            GroupResolutionStatus status,
            string groupId,
            bool isMember,
            string providerId = "")
        {
            Status = status;
            GroupId = groupId ?? string.Empty;
            IsMember = isMember;
            ProviderId = (providerId ?? string.Empty).Trim();
        }

        public GroupResolutionStatus Status { get; }

        public string GroupId { get; }

        public bool IsMember { get; }

        public string ProviderId { get; }
    }

    /// <summary>
    /// A server-verified administrative claim. Administrator status alone is insufficient:
    /// a session and a deliberate mode with a supplied justification are also required.
    /// </summary>
    public sealed class AdminBypassClaim
    {
        public AdminBypassClaim(
            bool isAdministrator,
            bool deliberateModeActive,
            string adminSessionId = "",
            string justification = "")
        {
            IsAdministrator = isAdministrator;
            DeliberateModeActive = deliberateModeActive;
            AdminSessionId = (adminSessionId ?? string.Empty).Trim();
            Justification = (justification ?? string.Empty).Trim();
        }

        public bool IsAdministrator { get; }

        public bool DeliberateModeActive { get; }

        public string AdminSessionId { get; }

        public string Justification { get; }

        public bool IsDeliberateAndAuditable =>
            IsAdministrator && DeliberateModeActive &&
            AdminSessionId.Length > 0 && Justification.Length > 0;

        public static AdminBypassClaim None { get; } = new AdminBypassClaim(false, false);
    }

    public sealed class PermissionEvaluationRequest
    {
        public PermissionEvaluationRequest(
            string evaluationId,
            string resourceId,
            IdentityClaim subject,
            OwnershipRecord ownership,
            PermissionProfile profile,
            PermissionAction action,
            AccessOverride serverOverride,
            WardContext ward,
            GroupMembershipContext group = null,
            AdminBypassClaim adminBypass = null)
        {
            EvaluationId = (evaluationId ?? string.Empty).Trim();
            ResourceId = (resourceId ?? string.Empty).Trim();
            Subject = subject;
            Ownership = ownership;
            Profile = profile;
            Action = action;
            ServerOverride = serverOverride;
            Ward = ward;
            Group = group;
            AdminBypass = adminBypass ?? AdminBypassClaim.None;
        }

        public string EvaluationId { get; }

        public string ResourceId { get; }

        public IdentityClaim Subject { get; }

        public OwnershipRecord Ownership { get; }

        public PermissionProfile Profile { get; }

        public PermissionAction Action { get; }

        public AccessOverride ServerOverride { get; }

        public WardContext Ward { get; }

        public GroupMembershipContext Group { get; }

        public AdminBypassClaim AdminBypass { get; }
    }
}
