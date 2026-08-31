using System;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Evaluation
{
    public sealed class PermissionEvaluatorOptions
    {
        public PermissionEvaluatorOptions(bool adminBypassEnabled = false)
        {
            AdminBypassEnabled = adminBypassEnabled;
        }

        public bool AdminBypassEnabled { get; }
    }

    /// <summary>
    /// Pure, deterministic policy evaluator. An administrative result is     /// returned as AuditRequired, never Allowed; PermissionEvaluationService completes
    /// the audit before converting it to an allow.
    /// </summary>
    public sealed class PermissionEvaluator
    {
        private readonly PermissionEvaluatorOptions _options;

        public PermissionEvaluator(PermissionEvaluatorOptions options = null)
        {
            _options = options ?? new PermissionEvaluatorOptions();
        }

        public PermissionEvaluation Evaluate(PermissionEvaluationRequest request)
        {
            if (request == null) return PermissionEvaluation.Deny(PermissionReason.InvalidRequest);

            // The server's explicit denial is the highest policy-level authority.
            if (request.ServerOverride.Deny)
                return PermissionEvaluation.Deny(PermissionReason.ServerExplicitDeny);

            if (!Enum.IsDefined(typeof(PermissionAction), request.Action) ||
                request.EvaluationId.Length == 0 || request.ResourceId.Length == 0)
                return PermissionEvaluation.Deny(PermissionReason.InvalidRequest);

            PermissionEvaluation identityFailure = ValidateIdentity(request.Subject);
            if (identityFailure != null) return identityFailure;

            PermissionEvaluation ownershipFailure = ValidateOwnership(request.Ownership);
            if (ownershipFailure != null) return ownershipFailure;

            PermissionEvaluation profileFailure = ValidateProfile(request.Profile);
            if (profileFailure != null) return profileFailure;

            if (!request.Profile.TryGetPolicy(request.Action, out PermissionPolicy policy))
                return PermissionEvaluation.Deny(PermissionReason.ScopeNotConfigured);
            if (policy == null || !policy.IsWellFormed)
                return PermissionEvaluation.Deny(PermissionReason.PolicyInvalid);

            AccessOverride objectOverride = policy.AccessList.Resolve(request.Subject.Identity);

            // Any object-local explicit denial wins over server/object allows and bypass.
            if (objectOverride.Deny)
                return PermissionEvaluation.Deny(PermissionReason.ObjectExplicitDeny);

            PermissionEvaluation wardUncertainty = ValidateWardCertainty(request.Ward);
            if (wardUncertainty != null) return wardUncertainty;

            GroupMembershipContext group = null;
            if (policy.Kind == PermissionPolicyKind.Group)
            {
                PermissionEvaluation groupFailure = ValidateGroup(policy, request.Group);
                if (groupFailure != null) return groupFailure;
                group = request.Group;
            }

            // Deliberate admin mode can bypass ordinary policy/ward denial, but cannot
            // bypass explicit denial or uncertain security inputs. The caller still has
            // no allow until the mandatory audit succeeds.
            if (_options.AdminBypassEnabled && request.AdminBypass.IsDeliberateAndAuditable)
                return PermissionEvaluation.RequireAdminAudit();

            // A ward denial is evaluated before all public/object-local access. Only an
            // explicit ward-owner public-exception setting allows evaluation to continue.
            if (request.Ward.State == WardState.Denies && !request.Ward.PublicExceptionsAllowed)
                return PermissionEvaluation.Deny(PermissionReason.WardDenied);

            // Deny has already won. Explicit server/object allow may now grant access.
            if (request.ServerOverride.Allow)
                return PermissionEvaluation.Allow(PermissionReason.AllowedExplicit);
            if (objectOverride.Allow &&
                policy.Kind != PermissionPolicyKind.Ward &&
                policy.Kind != PermissionPolicyKind.WardWithExceptions)
                return PermissionEvaluation.Allow(
                    policy.Kind == PermissionPolicyKind.Approved
                        ? PermissionReason.AllowedApproved
                        : PermissionReason.AllowedExplicit);

            switch (policy.Kind)
            {
                case PermissionPolicyKind.Everyone:
                    return PermissionEvaluation.Allow(PermissionReason.AllowedEveryone);

                case PermissionPolicyKind.Approved:
                    return PermissionEvaluation.Deny(PermissionReason.NotApproved);

                case PermissionPolicyKind.Owner:
                    return request.Subject.Identity.Equals(request.Ownership.Owner)
                        ? PermissionEvaluation.Allow(PermissionReason.AllowedOwner)
                        : PermissionEvaluation.Deny(PermissionReason.NotOwner);

                case PermissionPolicyKind.Nobody:
                    return PermissionEvaluation.Deny(PermissionReason.NobodyPolicy);

                case PermissionPolicyKind.Ward:
                    return EvaluateWardOnly(request.Ward);

                case PermissionPolicyKind.WardWithExceptions:
                    if (objectOverride.Allow && request.Ward.State != WardState.None &&
                        (request.Ward.State != WardState.Denies || request.Ward.PublicExceptionsAllowed))
                        return PermissionEvaluation.Allow(PermissionReason.AllowedExplicit);
                    return EvaluateWardOnly(request.Ward);

                case PermissionPolicyKind.Group:
                    return group.IsMember
                        ? PermissionEvaluation.Allow(PermissionReason.AllowedGroup)
                        : PermissionEvaluation.Deny(PermissionReason.NotGroupMember);

                default:
                    return PermissionEvaluation.Deny(PermissionReason.PolicyInvalid);
            }
        }

        private static PermissionEvaluation EvaluateWardOnly(WardContext ward)
        {
            if (ward.State == WardState.Allows)
                return PermissionEvaluation.Allow(PermissionReason.AllowedWard);
            if (ward.State == WardState.None)
                return PermissionEvaluation.Deny(PermissionReason.ActiveWardRequired);
            return PermissionEvaluation.Deny(PermissionReason.WardDenied);
        }

        private static PermissionEvaluation ValidateIdentity(IdentityClaim subject)
        {
            if (subject == null || subject.Status == IdentityResolutionStatus.Missing || subject.Identity == null)
                return PermissionEvaluation.Deny(PermissionReason.IdentityMissing);
            if (subject.Status == IdentityResolutionStatus.Ambiguous)
                return PermissionEvaluation.Deny(PermissionReason.IdentityAmbiguous);
            if (subject.Status == IdentityResolutionStatus.Stale)
                return PermissionEvaluation.Deny(PermissionReason.IdentityStale);
            if (subject.Status != IdentityResolutionStatus.Verified)
                return PermissionEvaluation.Deny(PermissionReason.IdentityAmbiguous);
            return null;
        }

        private static PermissionEvaluation ValidateOwnership(OwnershipRecord ownership)
        {
            if (ownership == null || ownership.Trust == RecordTrust.Missing)
                return PermissionEvaluation.Deny(PermissionReason.OwnershipMissing);
            if (ownership.Trust == RecordTrust.Ambiguous)
                return PermissionEvaluation.Deny(PermissionReason.OwnershipAmbiguous);
            if (ownership.Trust == RecordTrust.Stale)
                return PermissionEvaluation.Deny(PermissionReason.OwnershipStale);
            if (!ownership.IsCurrent)
                return PermissionEvaluation.Deny(PermissionReason.OwnershipInvalid);
            return null;
        }

        private static PermissionEvaluation ValidateProfile(PermissionProfile profile)
        {
            if (profile == null || profile.Trust == RecordTrust.Missing)
                return PermissionEvaluation.Deny(PermissionReason.ProfileMissing);
            if (profile.Trust == RecordTrust.Ambiguous)
                return PermissionEvaluation.Deny(PermissionReason.ProfileAmbiguous);
            if (profile.Trust == RecordTrust.Stale)
                return PermissionEvaluation.Deny(PermissionReason.ProfileStale);
            if (!Enum.IsDefined(typeof(RecordTrust), profile.Trust))
                return PermissionEvaluation.Deny(PermissionReason.ProfileInvalid);
            if (!profile.IsWellFormed)
                return PermissionEvaluation.Deny(PermissionReason.ProfileInvalid);
            return null;
        }

        private static PermissionEvaluation ValidateWardCertainty(WardContext ward)
        {
            if (ward == null)
                return PermissionEvaluation.Deny(PermissionReason.WardContextMissing);
            if (!Enum.IsDefined(typeof(WardState), ward.State))
                return PermissionEvaluation.Deny(PermissionReason.WardAmbiguous);
            if (ward.State == WardState.Ambiguous)
                return PermissionEvaluation.Deny(PermissionReason.WardAmbiguous);
            if (ward.State == WardState.Stale)
                return PermissionEvaluation.Deny(PermissionReason.WardStale);
            if (ward.State == WardState.OverlappingHostile)
                return PermissionEvaluation.Deny(PermissionReason.OverlappingHostileWards);
            if (ward.State != WardState.None && ward.WardId.Length == 0)
                return PermissionEvaluation.Deny(PermissionReason.WardAmbiguous);
            return null;
        }

        private static PermissionEvaluation ValidateGroup(
            PermissionPolicy policy,
            GroupMembershipContext group)
        {
            if (group == null || group.Status == GroupResolutionStatus.ProviderMissing ||
                group.ProviderId.Length == 0)
                return PermissionEvaluation.Deny(PermissionReason.GroupProviderMissing);
            if (group.Status == GroupResolutionStatus.GroupMissing)
                return PermissionEvaluation.Deny(PermissionReason.GroupMissing);
            if (group.Status == GroupResolutionStatus.Ambiguous)
                return PermissionEvaluation.Deny(PermissionReason.GroupAmbiguous);
            if (group.Status == GroupResolutionStatus.Stale)
                return PermissionEvaluation.Deny(PermissionReason.GroupStale);
            if (group.Status != GroupResolutionStatus.Available)
                return PermissionEvaluation.Deny(PermissionReason.GroupAmbiguous);
            if (!GroupIdentity.IsCanonicalId(group.GroupId))
                return PermissionEvaluation.Deny(PermissionReason.GroupAmbiguous);
            if (!string.Equals(policy.GroupId, group.GroupId, StringComparison.Ordinal))
                return PermissionEvaluation.Deny(PermissionReason.GroupContextMismatch);
            return null;
        }
    }
}
