using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupDomainTests
    {
        private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly StableIdentity Owner = Identity("owner");
        private static readonly StableIdentity Officer = Identity("officer");
        private static readonly StableIdentity Member = Identity("member");
        private static readonly StableIdentity Other = Identity("other");
        private const long Now = 638914176000000000L;

        internal static void Register()
        {
            TestRunner.Run("group IDs are canonical immutable UUIDs", GroupIdsAreCanonical);
            TestRunner.Run("group display names are bounded exact presentation", DisplayNamesAreExactPresentation);
            TestRunner.Run("group display names are unique by normalized ordinal case", DisplayNamesAreUnique);
            TestRunner.Run("one account may belong to multiple groups", AccountMayJoinMultipleGroups);
            TestRunner.Run("invitations bind exact invitee and expiry", InvitationsBindIdentityAndExpiry);
            TestRunner.Run("officers invite and remove only members", OfficerAuthorityIsBounded);
            TestRunner.Run("invitation audit survives inviter departure", InvitationSurvivesInviterDeparture);
            TestRunner.Run("last Owner requires explicit transfer", LastOwnerRequiresTransfer);
            TestRunner.Run("role changes cannot create a second Owner", RoleChangesPreserveOneOwner);
            TestRunner.Run("delete retires UUID and blocks stale reuse", DeleteRetiresIdentity);
            TestRunner.Run("catalog and group revisions are deterministic CAS", RevisionsAreDeterministic);
            TestRunner.Run("expired invitation pruning is deterministic", ExpiredInvitationsPrune);
            TestRunner.Run("group constructors reject malformed invariants", ConstructorsRejectMalformedState);
        }

        private static void GroupIdsAreCanonical()
        {
            TestAssert.Equal("11111111111111111111111111111111", GroupIdentity.ToCanonicalId(FirstId));
            TestAssert.True(GroupIdentity.TryParseCanonicalId(
                "11111111111111111111111111111111", out Guid parsed));
            TestAssert.Equal(FirstId, parsed);
            TestAssert.False(GroupIdentity.TryParseCanonicalId(
                "11111111-1111-1111-1111-111111111111", out _));
            TestAssert.False(GroupIdentity.TryParseCanonicalId(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", out _));
            TestAssert.Throws<ArgumentException>(() => GroupIdentity.ToCanonicalId(Guid.Empty));
        }

        private static void DisplayNamesAreExactPresentation()
        {
            GroupCatalog catalog = Create("Ravens");
            TestAssert.Equal("Ravens", catalog.Groups[0].DisplayName);
            TestAssert.Equal(
                GroupMutationCode.InvalidRequest,
                GroupCatalog.Empty.Create(0, FirstId, " Ravens", Owner).Code);
            string decomposed = "Cafe\u0301";
            TestAssert.False(decomposed.IsNormalized(NormalizationForm.FormC));
            TestAssert.Equal(
                GroupMutationCode.InvalidRequest,
                GroupCatalog.Empty.Create(0, FirstId, decomposed, Owner).Code);
            TestAssert.Equal(
                GroupMutationCode.InvalidRequest,
                GroupCatalog.Empty.Create(
                    0, FirstId, new string('x', GroupLimits.MaximumDisplayNameUtf8Bytes + 1), Owner).Code);
        }

        private static void DisplayNamesAreUnique()
        {
            GroupCatalog first = Create("Ravens");
            GroupMutationResult collision = first.Create(first.Revision, SecondId, "ravens", Other);
            TestAssert.Equal(GroupMutationCode.NameConflict, collision.Code);
            TestAssert.Equal(1, collision.Catalog.Groups.Count);

            GroupMutationResult second = first.Create(first.Revision, SecondId, "Wolves", Other);
            TestAssert.True(second.Success);
            GroupMutationResult renamed = second.Catalog.Rename(
                second.Catalog.Revision, SecondId, second.Group.Revision, Other, "RAVENS");
            TestAssert.Equal(GroupMutationCode.NameConflict, renamed.Code);
        }

        private static void AccountMayJoinMultipleGroups()
        {
            GroupCatalog catalog = Create("Ravens");
            GroupMutationResult second = catalog.Create(catalog.Revision, SecondId, "Wolves", Other);
            catalog = second.Catalog;
            catalog = InviteAndAccept(catalog, FirstId, Owner, Member, Now + TimeSpan.FromHours(1).Ticks);
            catalog = InviteAndAccept(catalog, SecondId, Other, Member, Now + TimeSpan.FromHours(1).Ticks);
            IReadOnlyList<GroupMembership> memberships = catalog.GetMemberships(Member);
            TestAssert.Equal(2, memberships.Count);
            TestAssert.Equal(FirstId, memberships[0].GroupId);
            TestAssert.Equal(SecondId, memberships[1].GroupId);
        }

        private static void InvitationsBindIdentityAndExpiry()
        {
            GroupCatalog catalog = Create("Ravens");
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult invite = catalog.Invite(
                catalog.Revision,
                FirstId,
                group.Revision,
                Owner,
                Member,
                Now,
                Now + TimeSpan.FromHours(1).Ticks);
            TestAssert.Equal(GroupMutationCode.Invited, invite.Code);
            catalog = invite.Catalog;
            group = invite.Group;

            GroupMutationResult wrong = catalog.Accept(
                catalog.Revision, FirstId, group.Revision, Other, Now + 1);
            TestAssert.Equal(GroupMutationCode.InvitationMissing, wrong.Code);
            GroupMutationResult expired = catalog.Accept(
                catalog.Revision,
                FirstId,
                group.Revision,
                Member,
                Now + TimeSpan.FromHours(1).Ticks);
            TestAssert.Equal(GroupMutationCode.InvitationExpired, expired.Code);
            GroupMutationResult accepted = catalog.Accept(
                catalog.Revision, FirstId, group.Revision, Member, Now + 1);
            TestAssert.Equal(GroupMutationCode.Accepted, accepted.Code);
            TestAssert.True(accepted.Group.TryGetMember(Member, out GroupMember member));
            TestAssert.Equal(GroupRole.Member, member.Role);
            TestAssert.False(accepted.Group.TryGetInvitation(Member, out _));
        }

        private static void OfficerAuthorityIsBounded()
        {
            GroupCatalog catalog = CreateWithMembers();
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult invite = catalog.Invite(
                catalog.Revision,
                FirstId,
                group.Revision,
                Officer,
                Other,
                Now,
                Now + 1000);
            TestAssert.True(invite.Success);
            catalog = invite.Catalog;
            group = invite.Group;
            GroupMutationResult removeMember = catalog.Remove(
                catalog.Revision, FirstId, group.Revision, Officer, Member);
            TestAssert.Equal(GroupMutationCode.Removed, removeMember.Code);
            catalog = removeMember.Catalog;
            group = removeMember.Group;
            TestAssert.Equal(
                GroupMutationCode.Unauthorized,
                catalog.Remove(catalog.Revision, FirstId, group.Revision, Officer, Owner).Code);
            TestAssert.Equal(
                GroupMutationCode.Unauthorized,
                catalog.Remove(catalog.Revision, FirstId, group.Revision, Officer, Officer).Code);
        }

        private static void LastOwnerRequiresTransfer()
        {
            GroupCatalog catalog = CreateWithMembers();
            GroupRecord group = catalog.Groups[0];
            TestAssert.Equal(
                "group-owner-transfer-required",
                catalog.Leave(catalog.Revision, FirstId, group.Revision, Owner).ReasonCode);
            TestAssert.Equal(
                GroupMutationCode.Unauthorized,
                catalog.Remove(catalog.Revision, FirstId, group.Revision, Officer, Owner).Code);
            GroupMutationResult transfer = catalog.TransferOwnership(
                catalog.Revision, FirstId, group.Revision, Owner, Member);
            TestAssert.Equal(GroupMutationCode.OwnershipTransferred, transfer.Code);
            TestAssert.True(transfer.Group.TryGetMember(Owner, out GroupMember oldOwner));
            TestAssert.Equal(GroupRole.Officer, oldOwner.Role);
            TestAssert.True(transfer.Group.TryGetMember(Member, out GroupMember newOwner));
            TestAssert.Equal(GroupRole.Owner, newOwner.Role);

            GroupMutationResult leave = transfer.Catalog.Leave(
                transfer.Catalog.Revision, FirstId, transfer.Group.Revision, Owner);
            TestAssert.Equal(GroupMutationCode.Left, leave.Code);
        }

        private static void InvitationSurvivesInviterDeparture()
        {
            GroupCatalog catalog = CreateWithMembers();
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult invite = catalog.Invite(
                catalog.Revision,
                FirstId,
                group.Revision,
                Officer,
                Other,
                Now,
                Now + 1000);
            GroupMutationResult leave = invite.Catalog.Leave(
                invite.Catalog.Revision,
                FirstId,
                invite.Group.Revision,
                Officer);
            TestAssert.Equal(GroupMutationCode.Left, leave.Code);
            TestAssert.True(leave.Group.TryGetInvitation(Other, out GroupInvitation evidence));
            TestAssert.Equal(Officer, evidence.InvitedBy);
            GroupMutationResult accept = leave.Catalog.Accept(
                leave.Catalog.Revision,
                FirstId,
                leave.Group.Revision,
                Other,
                Now + 1);
            TestAssert.Equal(GroupMutationCode.Accepted, accept.Code);
        }

        private static void RoleChangesPreserveOneOwner()
        {
            GroupCatalog catalog = CreateWithMembers();
            GroupRecord group = catalog.Groups[0];
            TestAssert.Equal(
                GroupMutationCode.Unauthorized,
                catalog.SetRole(
                    catalog.Revision, FirstId, group.Revision, Owner, Member, GroupRole.Owner).Code);
            TestAssert.Equal(
                GroupMutationCode.Unauthorized,
                catalog.SetRole(
                    catalog.Revision, FirstId, group.Revision, Owner, Owner, GroupRole.Member).Code);
            GroupMutationResult promote = catalog.SetRole(
                catalog.Revision, FirstId, group.Revision, Owner, Member, GroupRole.Officer);
            TestAssert.Equal(GroupMutationCode.RoleChanged, promote.Code);
            TestAssert.Equal(1, promote.Group.Members.Count(value => value.Role == GroupRole.Owner));
        }

        private static void DeleteRetiresIdentity()
        {
            GroupCatalog catalog = Create("Ravens");
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult deleted = catalog.Delete(
                catalog.Revision, FirstId, group.Revision, Owner);
            TestAssert.Equal(GroupMutationCode.Deleted, deleted.Code);
            TestAssert.Equal(0, deleted.Catalog.Groups.Count);
            TestAssert.Equal(1, deleted.Catalog.RetiredGroupIds.Count);
            TestAssert.Equal(
                GroupMutationCode.RetiredIdentity,
                deleted.Catalog.Create(
                    deleted.Catalog.Revision, FirstId, "New Ravens", Owner).Code);
        }

        private static void RevisionsAreDeterministic()
        {
            GroupCatalog catalog = Create("Ravens");
            TestAssert.Equal(1L, catalog.Revision);
            TestAssert.Equal(1L, catalog.Groups[0].Revision);
            GroupMutationResult invite = catalog.Invite(
                1, FirstId, 1, Owner, Member, Now, Now + 1000);
            TestAssert.Equal(2L, invite.Catalog.Revision);
            TestAssert.Equal(2L, invite.Group.Revision);
            GroupMutationResult staleCatalog = invite.Catalog.Accept(1, FirstId, 2, Member, Now + 1);
            TestAssert.Equal(GroupMutationCode.RevisionConflict, staleCatalog.Code);
            GroupMutationResult staleGroup = invite.Catalog.Accept(2, FirstId, 1, Member, Now + 1);
            TestAssert.Equal(GroupMutationCode.RevisionConflict, staleGroup.Code);
            TestAssert.Equal(2L, staleGroup.Catalog.Revision);
        }

        private static void ExpiredInvitationsPrune()
        {
            GroupCatalog catalog = Create("Ravens");
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult invite = catalog.Invite(
                catalog.Revision, FirstId, group.Revision, Owner, Member, Now, Now + 10);
            GroupMutationResult pruned = invite.Catalog.PruneExpiredInvitations(
                invite.Catalog.Revision, FirstId, invite.Group.Revision, Now + 10);
            TestAssert.Equal(GroupMutationCode.ExpiredInvitationsPruned, pruned.Code);
            TestAssert.Equal(0, pruned.Group.Invitations.Count);
            GroupMutationResult noChange = pruned.Catalog.PruneExpiredInvitations(
                pruned.Catalog.Revision, FirstId, pruned.Group.Revision, Now + 11);
            TestAssert.Equal(GroupMutationCode.NoChange, noChange.Code);
            TestAssert.Equal(pruned.Catalog.Revision, noChange.Catalog.Revision);
        }

        private static void ConstructorsRejectMalformedState()
        {
            TestAssert.Throws<ArgumentException>(() => new GroupRecord(
                FirstId,
                "Ravens",
                1,
                new[]
                {
                    new GroupMember(Owner, GroupRole.Owner, 1),
                    new GroupMember(Other, GroupRole.Owner, 1)
                }));
            TestAssert.Throws<ArgumentException>(() => new GroupRecord(
                FirstId,
                "Ravens",
                1,
                new[] { new GroupMember(Owner, GroupRole.Owner, 1) },
                new[] { new GroupInvitation(Owner, Other, 1, Now + 1) }));
            GroupRecord group = Create("Ravens").Groups[0];
            TestAssert.Throws<ArgumentException>(() => new GroupCatalog(
                1,
                new[]
                {
                    group,
                    new GroupRecord(
                        SecondId,
                        "RAVENS",
                        1,
                        new[] { new GroupMember(Other, GroupRole.Owner, 1) })
                }));
        }

        private static GroupCatalog Create(string name)
        {
            GroupMutationResult created = GroupCatalog.Empty.Create(0, FirstId, name, Owner);
            TestAssert.True(created.Success, created.ReasonCode);
            return created.Catalog;
        }

        private static GroupCatalog CreateWithMembers()
        {
            GroupCatalog catalog = Create("Ravens");
            catalog = InviteAndAccept(catalog, FirstId, Owner, Officer, Now + 1000);
            GroupRecord group = catalog.Groups[0];
            GroupMutationResult promote = catalog.SetRole(
                catalog.Revision, FirstId, group.Revision, Owner, Officer, GroupRole.Officer);
            catalog = promote.Catalog;
            catalog = InviteAndAccept(catalog, FirstId, Owner, Member, Now + 1000);
            return catalog;
        }

        private static GroupCatalog InviteAndAccept(
            GroupCatalog catalog,
            Guid id,
            StableIdentity inviter,
            StableIdentity invitee,
            long expiry)
        {
            TestAssert.True(catalog.TryGetGroup(id, out GroupRecord group));
            GroupMutationResult invite = catalog.Invite(
                catalog.Revision, id, group.Revision, inviter, invitee, Now, expiry);
            TestAssert.True(invite.Success, invite.ReasonCode);
            GroupMutationResult accept = invite.Catalog.Accept(
                invite.Catalog.Revision, id, invite.Group.Revision, invitee, Now + 1);
            TestAssert.True(accept.Success, accept.ReasonCode);
            return accept.Catalog;
        }

        private static StableIdentity Identity(string value) => new StableIdentity("steam", value);
    }
}
