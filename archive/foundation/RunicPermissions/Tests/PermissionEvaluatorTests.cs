using System;
using System.Collections.Generic;
using RunicPermissions.Contracts;
using RunicPermissions.Evaluation;

namespace RunicPermissions.Tests
{
    internal static class PermissionEvaluatorTests
    {
        private const string BuildersGroup = "11111111111111111111111111111111";
        private const string RaidersGroup = "22222222222222222222222222222222";
        private static readonly StableIdentity Owner = new StableIdentity("steam", "100");
        private static readonly StableIdentity Player = new StableIdentity("steam", "200");

        internal static void Register()
        {
            TestRunner.Run("everyone permits verified player", EveryonePermitsVerifiedPlayer);
            TestRunner.Run("approved permits stable offline approval", ApprovedPermitsStableIdentity);
            TestRunner.Run("owner uses stable ID, not display name", OwnerUsesStableIdentity);
            TestRunner.Run("nobody denies", NobodyDenies);
            TestRunner.Run("missing scope denies", MissingScopeDenies);
            TestRunner.Run("server deny beats all allows and admin", ServerDenyHasHighestPrecedence);
            TestRunner.Run("object deny beats server allow and admin", ObjectDenyBeatsAllowAndAdmin);
            TestRunner.Run("ACL conflict denies", AccessListConflictDenies);
            TestRunner.Run("ward denial blocks public object", WardDenialBlocksEveryone);
            TestRunner.Run("ward-owner public exception enables public object", WardPublicExceptionEnablesEveryone);
            TestRunner.Run("ward policy requires active ward", WardPolicyRequiresWard);
            TestRunner.Run("ward policy accepts ward allow", WardPolicyAcceptsAllow);
            TestRunner.Run("plain ward policy ignores ACL exceptions", PlainWardIgnoresAclException);
            TestRunner.Run("ward plus exception allows listed identity", WardExceptionAllowsListedIdentity);
            TestRunner.Run("ward exception flag is required", WardExceptionRequiresWardOwnerFlag);
            TestRunner.Run("ward exception policy still requires a ward", WardExceptionRequiresActiveWard);
            TestRunner.Run("ambiguous identity fails closed", AmbiguousIdentityFailsClosed);
            TestRunner.Run("stale identity fails closed", StaleIdentityFailsClosed);
            TestRunner.Run("ambiguous ownership fails closed", AmbiguousOwnershipFailsClosed);
            TestRunner.Run("stale profile fails closed", StaleProfileFailsClosed);
            TestRunner.Run("undefined profile trust fails closed", UndefinedProfileTrustFailsClosed);
            TestRunner.Run("overlapping hostile wards fail closed", HostileWardsFailClosed);
            TestRunner.Run("stale ward fails closed", StaleWardFailsClosed);
            TestRunner.Run("missing group provider fails closed", MissingGroupProviderFailsClosed);
            TestRunner.Run("missing group fails closed", MissingGroupFailsClosed);
            TestRunner.Run("group context mismatch fails closed", GroupMismatchFailsClosed);
            TestRunner.Run("group member is allowed", GroupMemberIsAllowed);
            TestRunner.Run("group non-member is denied", GroupNonMemberIsDenied);
            TestRunner.Run("admin bypass is provisional until audit", AdminBypassRequiresAudit);
            TestRunner.Run("audited admin bypass is allowed", AuditedAdminBypassIsAllowed);
            TestRunner.Run("failed admin audit denies", FailedAdminAuditDenies);
            TestRunner.Run("admin mode must be deliberate", AdminModeMustBeDeliberate);
            TestRunner.Run("evaluation is deterministic", EvaluationIsDeterministic);
        }

        private static void EveryonePermitsVerifiedPlayer() =>
            AssertReason(PermissionReason.AllowedEveryone, Evaluate(Request(PermissionPolicyKind.Everyone)));

        private static void ApprovedPermitsStableIdentity()
        {
            PermissionEvaluationRequest request = Request(
                PermissionPolicyKind.Approved,
                acl: new AccessControlList(new[] { new StableIdentity("STEAM", "200") }));
            AssertReason(PermissionReason.AllowedApproved, Evaluate(request));
        }

        private static void OwnerUsesStableIdentity()
        {
            PermissionEvaluationRequest owner = Request(
                PermissionPolicyKind.Owner,
                subject: IdentityClaim.Verified(Owner, "Completely Different Display Name"));
            AssertReason(PermissionReason.AllowedOwner, Evaluate(owner));

            PermissionEvaluationRequest impersonator = Request(
                PermissionPolicyKind.Owner,
                subject: IdentityClaim.Verified(Player, "Owner's Display Name"));
            AssertReason(PermissionReason.NotOwner, Evaluate(impersonator));
        }

        private static void NobodyDenies() =>
            AssertReason(PermissionReason.NobodyPolicy, Evaluate(Request(PermissionPolicyKind.Nobody)));

        private static void MissingScopeDenies()
        {
            PermissionEvaluationRequest request = Request(PermissionPolicyKind.Everyone);
            var emptyProfile = new PermissionProfile(
                "empty", 1, 0, Array.Empty<KeyValuePair<PermissionAction, PermissionPolicy>>());
            AssertReason(PermissionReason.ScopeNotConfigured, Evaluate(Clone(request, profile: emptyProfile)));
        }

        private static void ServerDenyHasHighestPrecedence()
        {
            PermissionEvaluationRequest request = Request(
                PermissionPolicyKind.Everyone,
                acl: new AccessControlList(new[] { Player }),
                serverOverride: AccessOverride.Conflicting,
                ward: new WardContext(WardState.Ambiguous, wardId: "ward-1"),
                admin: DeliberateAdmin());
            AssertReason(PermissionReason.ServerExplicitDeny, Evaluate(request, true));
        }

        private static void ObjectDenyBeatsAllowAndAdmin()
        {
            var conflict = new AccessControlList(new[] { Player }, new[] { Player });
            PermissionEvaluationRequest request = Request(
                PermissionPolicyKind.Everyone,
                acl: conflict,
                serverOverride: AccessOverride.ExplicitAllow,
                admin: DeliberateAdmin());
            AssertReason(PermissionReason.ObjectExplicitDeny, Evaluate(request, true));
        }

        private static void AccessListConflictDenies()
        {
            var conflict = new AccessControlList(new[] { Player }, new[] { Player });
            AssertReason(
                PermissionReason.ObjectExplicitDeny,
                Evaluate(Request(PermissionPolicyKind.Approved, acl: conflict)));
        }

        private static void WardDenialBlocksEveryone()
        {
            AssertReason(
                PermissionReason.WardDenied,
                Evaluate(Request(
                    PermissionPolicyKind.Everyone,
                    ward: new WardContext(WardState.Denies, false, "ward-1"))));
        }

        private static void WardPublicExceptionEnablesEveryone()
        {
            AssertReason(
                PermissionReason.AllowedEveryone,
                Evaluate(Request(
                    PermissionPolicyKind.Everyone,
                    ward: new WardContext(WardState.Denies, true, "ward-1"))));
        }

        private static void WardPolicyRequiresWard() =>
            AssertReason(PermissionReason.ActiveWardRequired, Evaluate(Request(PermissionPolicyKind.Ward)));

        private static void WardPolicyAcceptsAllow() =>
            AssertReason(
                PermissionReason.AllowedWard,
                Evaluate(Request(
                    PermissionPolicyKind.Ward,
                    ward: new WardContext(WardState.Allows, wardId: "ward-1"))));

        private static void PlainWardIgnoresAclException()
        {
            AssertReason(
                PermissionReason.WardDenied,
                Evaluate(Request(
                    PermissionPolicyKind.Ward,
                    acl: new AccessControlList(new[] { Player }),
                    ward: new WardContext(WardState.Denies, true, "ward-1"))));
        }

        private static void WardExceptionAllowsListedIdentity()
        {
            AssertReason(
                PermissionReason.AllowedExplicit,
                Evaluate(Request(
                    PermissionPolicyKind.WardWithExceptions,
                    acl: new AccessControlList(new[] { Player }),
                    ward: new WardContext(WardState.Denies, true, "ward-1"))));
        }

        private static void WardExceptionRequiresWardOwnerFlag()
        {
            AssertReason(
                PermissionReason.WardDenied,
                Evaluate(Request(
                    PermissionPolicyKind.WardWithExceptions,
                    acl: new AccessControlList(new[] { Player }),
                    ward: new WardContext(WardState.Denies, false, "ward-1"))));
        }

        private static void WardExceptionRequiresActiveWard()
        {
            AssertReason(
                PermissionReason.ActiveWardRequired,
                Evaluate(Request(
                    PermissionPolicyKind.WardWithExceptions,
                    acl: new AccessControlList(new[] { Player }))));
        }

        private static void AmbiguousIdentityFailsClosed()
        {
            var subject = new IdentityClaim(Player, "Player", IdentityResolutionStatus.Ambiguous);
            AssertReason(
                PermissionReason.IdentityAmbiguous,
                Evaluate(Request(PermissionPolicyKind.Everyone, subject: subject)));
        }

        private static void StaleIdentityFailsClosed()
        {
            var subject = new IdentityClaim(Player, "Player", IdentityResolutionStatus.Stale);
            AssertReason(
                PermissionReason.IdentityStale,
                Evaluate(Request(PermissionPolicyKind.Everyone, subject: subject)));
        }

        private static void AmbiguousOwnershipFailsClosed()
        {
            var ownership = new OwnershipRecord(Owner, Owner, 1, 0, RecordTrust.Ambiguous);
            AssertReason(
                PermissionReason.OwnershipAmbiguous,
                Evaluate(Request(PermissionPolicyKind.Everyone, ownership: ownership)));
        }

        private static void StaleProfileFailsClosed()
        {
            PermissionEvaluationRequest request = Request(PermissionPolicyKind.Everyone);
            PermissionProfile stale = Profile(PermissionPolicyKind.Everyone, trust: RecordTrust.Stale);
            AssertReason(PermissionReason.ProfileStale, Evaluate(Clone(request, profile: stale)));
        }

        private static void UndefinedProfileTrustFailsClosed()
        {
            PermissionEvaluationRequest request = Request(PermissionPolicyKind.Everyone);
            PermissionProfile invalid = Profile(
                PermissionPolicyKind.Everyone,
                trust: (RecordTrust)999);
            AssertReason(PermissionReason.ProfileInvalid, Evaluate(Clone(request, profile: invalid)));
        }

        private static void HostileWardsFailClosed() =>
            AssertReason(
                PermissionReason.OverlappingHostileWards,
                Evaluate(Request(
                    PermissionPolicyKind.Everyone,
                    ward: new WardContext(WardState.OverlappingHostile, true, "wards-a-b"),
                    admin: DeliberateAdmin()), true));

        private static void StaleWardFailsClosed() =>
            AssertReason(
                PermissionReason.WardStale,
                Evaluate(Request(
                    PermissionPolicyKind.Everyone,
                    ward: new WardContext(WardState.Stale, true, "ward-1"))));

        private static void MissingGroupProviderFailsClosed()
        {
            var group = new GroupMembershipContext(GroupResolutionStatus.ProviderMissing, BuildersGroup, true);
            AssertReason(
                PermissionReason.GroupProviderMissing,
                Evaluate(Request(PermissionPolicyKind.Group, groupId: BuildersGroup, group: group)));
        }

        private static void MissingGroupFailsClosed()
        {
            var group = new GroupMembershipContext(
                GroupResolutionStatus.GroupMissing, BuildersGroup, false, "groups.mod");
            AssertReason(
                PermissionReason.GroupMissing,
                Evaluate(Request(PermissionPolicyKind.Group, groupId: BuildersGroup, group: group)));
        }

        private static void GroupMismatchFailsClosed()
        {
            var group = new GroupMembershipContext(
                GroupResolutionStatus.Available, RaidersGroup, true, "groups.mod");
            AssertReason(
                PermissionReason.GroupContextMismatch,
                Evaluate(Request(PermissionPolicyKind.Group, groupId: BuildersGroup, group: group)));
        }

        private static void GroupMemberIsAllowed()
        {
            var group = new GroupMembershipContext(
                GroupResolutionStatus.Available, BuildersGroup, true, "groups.mod");
            AssertReason(
                PermissionReason.AllowedGroup,
                Evaluate(Request(PermissionPolicyKind.Group, groupId: BuildersGroup, group: group)));
        }

        private static void GroupNonMemberIsDenied()
        {
            var group = new GroupMembershipContext(
                GroupResolutionStatus.Available, BuildersGroup, false, "groups.mod");
            AssertReason(
                PermissionReason.NotGroupMember,
                Evaluate(Request(PermissionPolicyKind.Group, groupId: BuildersGroup, group: group)));
        }

        private static void AdminBypassRequiresAudit()
        {
            PermissionEvaluation result = Evaluate(
                Request(PermissionPolicyKind.Nobody, admin: DeliberateAdmin()), true);
            TestAssert.Equal(PermissionOutcome.AuditRequired, result.Outcome);
            TestAssert.False(result.IsAllowed);
            TestAssert.True(result.IsAdminBypass);
        }

        private static void AuditedAdminBypassIsAllowed()
        {
            var sink = new RecordingAuditSink(true);
            var service = new PermissionEvaluationService(
                new PermissionEvaluator(new PermissionEvaluatorOptions(true)), sink);
            PermissionEvaluation result = service.Evaluate(
                Request(PermissionPolicyKind.Nobody, admin: DeliberateAdmin()));
            AssertReason(PermissionReason.AllowedAdminBypass, result);
            TestAssert.True(result.IsAllowed);
            TestAssert.True(result.AuditRecorded);
            TestAssert.Equal(1, sink.Count);
            TestAssert.Equal("eval-1", sink.Last.EvaluationId);
        }

        private static void FailedAdminAuditDenies()
        {
            var service = new PermissionEvaluationService(
                new PermissionEvaluator(new PermissionEvaluatorOptions(true)),
                new RecordingAuditSink(false));
            AssertReason(
                PermissionReason.AdminAuditUnavailable,
                service.Evaluate(Request(PermissionPolicyKind.Nobody, admin: DeliberateAdmin())));
        }

        private static void AdminModeMustBeDeliberate()
        {
            var passiveAdmin = new AdminBypassClaim(true, false, "session-1", "maintenance");
            AssertReason(
                PermissionReason.NobodyPolicy,
                Evaluate(Request(PermissionPolicyKind.Nobody, admin: passiveAdmin), true));
        }

        private static void EvaluationIsDeterministic()
        {
            PermissionEvaluationRequest request = Request(PermissionPolicyKind.Owner);
            PermissionEvaluation first = Evaluate(request);
            for (int i = 0; i < 100; i++)
            {
                PermissionEvaluation next = Evaluate(request);
                TestAssert.Equal(first.Outcome, next.Outcome);
                TestAssert.Equal(first.Reason, next.Reason);
            }
        }

        private static PermissionEvaluation Evaluate(
            PermissionEvaluationRequest request,
            bool adminBypassEnabled = false) =>
            new PermissionEvaluator(new PermissionEvaluatorOptions(adminBypassEnabled)).Evaluate(request);

        private static PermissionEvaluationRequest Request(
            PermissionPolicyKind kind,
            AccessControlList acl = null,
            string groupId = "",
            IdentityClaim subject = null,
            OwnershipRecord ownership = null,
            AccessOverride? serverOverride = null,
            WardContext ward = null,
            GroupMembershipContext group = null,
            AdminBypassClaim admin = null)
        {
            return new PermissionEvaluationRequest(
                "eval-1",
                "object-1",
                subject ?? IdentityClaim.Verified(Player, "Player"),
                ownership ?? new OwnershipRecord(Owner, Owner, 1, 0),
                Profile(kind, acl, groupId),
                PermissionAction.OpenContainer,
                serverOverride ?? AccessOverride.None,
                ward ?? WardContext.NoWard,
                group,
                admin);
        }

        private static PermissionProfile Profile(
            PermissionPolicyKind kind,
            AccessControlList acl = null,
            string groupId = "",
            RecordTrust trust = RecordTrust.Current) =>
            new PermissionProfile(
                "profile-1",
                1,
                0,
                new[]
                {
                    new KeyValuePair<PermissionAction, PermissionPolicy>(
                        PermissionAction.OpenContainer,
                        new PermissionPolicy(kind, acl, groupId))
                },
                trust);

        private static PermissionEvaluationRequest Clone(
            PermissionEvaluationRequest source,
            PermissionProfile profile = null) =>
            new PermissionEvaluationRequest(
                source.EvaluationId,
                source.ResourceId,
                source.Subject,
                source.Ownership,
                profile ?? source.Profile,
                source.Action,
                source.ServerOverride,
                source.Ward,
                source.Group,
                source.AdminBypass);

        private static AdminBypassClaim DeliberateAdmin() =>
            new AdminBypassClaim(true, true, "admin-session-1", "deliberate maintenance");

        private static void AssertReason(PermissionReason expected, PermissionEvaluation actual) =>
            TestAssert.Equal(expected, actual.Reason);

        private sealed class RecordingAuditSink : IPermissionAuditSink
        {
            private readonly bool _succeeds;

            internal RecordingAuditSink(bool succeeds)
            {
                _succeeds = succeeds;
            }

            internal int Count { get; private set; }
            internal AdminBypassAuditRecord Last { get; private set; }

            public bool TryRecord(AdminBypassAuditRecord record, out string failureReason)
            {
                Count++;
                Last = record;
                failureReason = _succeeds ? string.Empty : "simulated audit failure";
                return _succeeds;
            }
        }
    }
}
