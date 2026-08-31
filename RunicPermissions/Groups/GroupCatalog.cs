using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    public enum GroupMutationCode
    {
        Created = 0,
        Renamed = 1,
        Invited = 2,
        InvitationCancelled = 3,
        Accepted = 4,
        Left = 5,
        Removed = 6,
        RoleChanged = 7,
        OwnershipTransferred = 8,
        Deleted = 9,
        ExpiredInvitationsPruned = 10,
        NoChange = 11,
        RevisionConflict = 12,
        InvalidRequest = 13,
        GroupMissing = 14,
        NameConflict = 15,
        Unauthorized = 16,
        AlreadyMember = 17,
        InvitationMissing = 18,
        InvitationExpired = 19,
        CapacityReached = 20,
        RetiredIdentity = 21
    }

    public sealed class GroupMutationResult
    {
        internal GroupMutationResult(
            GroupMutationCode code,
            string reasonCode,
            GroupCatalog catalog,
            GroupRecord group)
        {
            Code = code;
            ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Group = group;
        }

        public GroupMutationCode Code { get; }
        public string ReasonCode { get; }
        public GroupCatalog Catalog { get; }
        public GroupRecord Group { get; }
        public bool Success => Code >= GroupMutationCode.Created &&
                               Code <= GroupMutationCode.NoChange;
    }

    public sealed class GroupMembership
    {
        internal GroupMembership(Guid groupId, string displayName, GroupRole role, long groupRevision)
        {
            GroupId = groupId;
            GroupIdText = GroupIdentity.ToCanonicalId(groupId);
            DisplayName = displayName;
            Role = role;
            GroupRevision = groupRevision;
        }

        public Guid GroupId { get; }
        public string GroupIdText { get; }
        public string DisplayName { get; }
        public GroupRole Role { get; }
        public long GroupRevision { get; }
    }

    public sealed class GroupCatalog
    {
        private readonly GroupRecord[] _groups;
        private readonly RetiredGroupId[] _retired;
        private readonly ReadOnlyCollection<GroupRecord> _groupView;
        private readonly ReadOnlyCollection<RetiredGroupId> _retiredView;

        public GroupCatalog(
            long revision,
            IEnumerable<GroupRecord> groups = null,
            IEnumerable<RetiredGroupId> retiredGroupIds = null)
            : this(revision, groups, retiredGroupIds, GroupCommandLedger.Empty)
        {
        }

        internal GroupCatalog(
            long revision,
            IEnumerable<GroupRecord> groups,
            IEnumerable<RetiredGroupId> retiredGroupIds,
            GroupCommandLedger commandLedger)
        {
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            Revision = revision;
            CommandLedger = commandLedger ?? throw new ArgumentNullException(nameof(commandLedger));
            _groups = CopyGroups(groups, revision);
            _retired = CopyRetired(retiredGroupIds, revision, _groups);
            ValidateMembershipBounds(_groups);
            _groupView = Array.AsReadOnly(_groups);
            _retiredView = Array.AsReadOnly(_retired);
        }

        public long Revision { get; }
        internal GroupCommandLedger CommandLedger { get; }
        public IReadOnlyList<GroupRecord> Groups => _groupView;
        public IReadOnlyList<RetiredGroupId> RetiredGroupIds => _retiredView;
        public static GroupCatalog Empty { get; } = new GroupCatalog(0);

        internal GroupCatalog WithCommandLedger(GroupCommandLedger ledger) =>
            new GroupCatalog(Revision, _groups, _retired, ledger);

        public bool TryGetGroup(Guid id, out GroupRecord group)
        {
            group = null;
            if (id == Guid.Empty) return false;
            int index = FindGroup(_groups, GroupIdentity.ToCanonicalId(id));
            if (index < 0) return false;
            group = _groups[index];
            return true;
        }

        public IReadOnlyList<GroupMembership> GetMemberships(StableIdentity identity)
        {
            if (identity == null) return Array.Empty<GroupMembership>();
            var values = new List<GroupMembership>();
            foreach (GroupRecord group in _groups)
                if (group.TryGetMember(identity, out GroupMember member))
                    values.Add(new GroupMembership(
                        group.Id, group.DisplayName, member.Role, group.Revision));
            return values.AsReadOnly();
        }

        public GroupMutationResult Create(
            long expectedCatalogRevision,
            Guid id,
            string displayName,
            StableIdentity owner)
        {
            if (!Expected(expectedCatalogRevision)) return Conflict();
            if (id == Guid.Empty || owner == null || !TryDisplayName(displayName)) return Invalid();
            string idText = GroupIdentity.ToCanonicalId(id);
            if (FindGroup(_groups, idText) >= 0) return Fail(GroupMutationCode.InvalidRequest, "group-id-exists");
            if (FindRetired(_retired, idText) >= 0) return Fail(GroupMutationCode.RetiredIdentity, "group-id-retired");
            if (_groups.Length >= GroupLimits.MaximumGroups ||
                CountMemberships(owner) >= GroupLimits.MaximumGroupsPerIdentity)
                return Fail(GroupMutationCode.CapacityReached, "group-capacity-reached");
            if (NameExists(displayName, null)) return Fail(GroupMutationCode.NameConflict, "group-name-conflict");
            var group = new GroupRecord(
                id,
                displayName,
                1,
                new[] { new GroupMember(owner, GroupRole.Owner, 1) });
            return Replace(null, group, GroupMutationCode.Created, "group-created");
        }

        public GroupMutationResult Rename(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            string displayName)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (!TryDisplayName(displayName) || actor == null) return Invalid(group);
            if (!IsOwner(group, actor)) return Unauthorized(group);
            if (NameExists(displayName, group.IdText)) return Fail(GroupMutationCode.NameConflict, "group-name-conflict", group);
            if (string.Equals(group.DisplayName, displayName, StringComparison.Ordinal))
                return Ok(GroupMutationCode.NoChange, "group-name-unchanged", group);
            return Replace(group, group.Rename(displayName), GroupMutationCode.Renamed, "group-renamed");
        }

        public GroupMutationResult Invite(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            StableIdentity invitee,
            long nowUtcTicks,
            long expiresUtcTicks)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || invitee == null || nowUtcTicks <= 0 || expiresUtcTicks <= nowUtcTicks ||
                expiresUtcTicks - nowUtcTicks > GroupLimits.MaximumInvitationLifetime.Ticks)
                return Invalid(group);
            if (!IsOfficerOrOwner(group, actor)) return Unauthorized(group);
            if (group.TryGetMember(invitee, out _)) return Fail(GroupMutationCode.AlreadyMember, "group-already-member", group);
            if (CountMemberships(invitee) >= GroupLimits.MaximumGroupsPerIdentity)
                return Fail(GroupMutationCode.CapacityReached, "group-membership-capacity", group);
            if (group.TryGetInvitation(invitee, out GroupInvitation existing))
            {
                if (existing.InvitedBy.Equals(actor) && existing.ExpiresUtcTicks == expiresUtcTicks)
                    return Ok(GroupMutationCode.NoChange, "group-invitation-unchanged", group);
                return Fail(GroupMutationCode.InvalidRequest, "group-invitation-exists", group);
            }
            if (group.Invitations.Count >= GroupLimits.MaximumInvitationsPerGroup)
                return Fail(GroupMutationCode.CapacityReached, "group-invitation-capacity", group);
            return Replace(group, group.Invite(actor, invitee, expiresUtcTicks), GroupMutationCode.Invited, "group-invited");
        }

        public GroupMutationResult CancelInvitation(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            StableIdentity invitee)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || invitee == null) return Invalid(group);
            if (!IsOfficerOrOwner(group, actor)) return Unauthorized(group);
            if (!group.TryGetInvitation(invitee, out _))
                return Fail(GroupMutationCode.InvitationMissing, "group-invitation-missing", group);
            return Replace(
                group,
                group.CancelInvitation(invitee),
                GroupMutationCode.InvitationCancelled,
                "group-invitation-cancelled");
        }

        public GroupMutationResult Accept(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity invitee,
            long nowUtcTicks)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (invitee == null || nowUtcTicks <= 0) return Invalid(group);
            if (group.TryGetMember(invitee, out _)) return Fail(GroupMutationCode.AlreadyMember, "group-already-member", group);
            if (!group.TryGetInvitation(invitee, out GroupInvitation invitation))
                return Fail(GroupMutationCode.InvitationMissing, "group-invitation-missing", group);
            if (!invitation.Invitee.Equals(invitee))
                return Fail(GroupMutationCode.Unauthorized, "group-invitation-identity-mismatch", group);
            if (invitation.IsExpired(nowUtcTicks))
                return Fail(GroupMutationCode.InvitationExpired, "group-invitation-expired", group);
            if (group.Members.Count >= GroupLimits.MaximumMembersPerGroup ||
                CountMemberships(invitee) >= GroupLimits.MaximumGroupsPerIdentity)
                return Fail(GroupMutationCode.CapacityReached, "group-membership-capacity", group);
            return Replace(group, group.Accept(invitee), GroupMutationCode.Accepted, "group-invitation-accepted");
        }

        public GroupMutationResult Leave(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || !group.TryGetMember(actor, out GroupMember member)) return Unauthorized(group);
            if (member.Role == GroupRole.Owner)
                return Fail(GroupMutationCode.Unauthorized, "group-owner-transfer-required", group);
            return Replace(group, group.RemoveMember(actor), GroupMutationCode.Left, "group-left");
        }

        public GroupMutationResult Remove(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            StableIdentity target)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || target == null || actor.Equals(target) ||
                !group.TryGetMember(actor, out GroupMember actorMember) ||
                !group.TryGetMember(target, out GroupMember targetMember))
                return Unauthorized(group);
            if (targetMember.Role == GroupRole.Owner ||
                actorMember.Role == GroupRole.Member ||
                actorMember.Role == GroupRole.Officer && targetMember.Role != GroupRole.Member)
                return Unauthorized(group);
            return Replace(group, group.RemoveMember(target), GroupMutationCode.Removed, "group-member-removed");
        }

        public GroupMutationResult SetRole(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            StableIdentity target,
            GroupRole role)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || target == null || role == GroupRole.Owner ||
                !Enum.IsDefined(typeof(GroupRole), role) || !IsOwner(group, actor) ||
                !group.TryGetMember(target, out GroupMember member) || member.Role == GroupRole.Owner)
                return Unauthorized(group);
            if (member.Role == role) return Ok(GroupMutationCode.NoChange, "group-role-unchanged", group);
            return Replace(group, group.SetRole(target, role), GroupMutationCode.RoleChanged, "group-role-changed");
        }

        public GroupMutationResult TransferOwnership(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor,
            StableIdentity successor)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || successor == null || actor.Equals(successor) || !IsOwner(group, actor) ||
                !group.TryGetMember(successor, out GroupMember target) || target.Role == GroupRole.Owner)
                return Unauthorized(group);
            return Replace(
                group,
                group.TransferOwnership(actor, successor),
                GroupMutationCode.OwnershipTransferred,
                "group-ownership-transferred");
        }

        public GroupMutationResult Delete(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            StableIdentity actor)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (actor == null || !IsOwner(group, actor)) return Unauthorized(group);
            if (_retired.Length >= GroupLimits.MaximumRetiredGroupIds)
                return Fail(GroupMutationCode.CapacityReached, "group-retired-id-capacity", group);
            long nextRevision = checked(Revision + 1);
            GroupRecord[] groups = _groups.Where(value => value.Id != id).ToArray();
            var retired = new List<RetiredGroupId>(_retired)
            {
                new RetiredGroupId(id, nextRevision)
            };
            var replacement = new GroupCatalog(nextRevision, groups, retired, CommandLedger);
            return new GroupMutationResult(GroupMutationCode.Deleted, "group-deleted", replacement, null);
        }

        public GroupMutationResult PruneExpiredInvitations(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            long nowUtcTicks)
        {
            if (!TryMutation(expectedCatalogRevision, id, expectedGroupRevision, out GroupRecord group, out GroupMutationResult failure))
                return failure;
            if (nowUtcTicks <= 0) return Invalid(group);
            GroupRecord replacement = group.PruneExpired(nowUtcTicks);
            return ReferenceEquals(group, replacement)
                ? Ok(GroupMutationCode.NoChange, "group-no-expired-invitations", group)
                : Replace(
                    group,
                    replacement,
                    GroupMutationCode.ExpiredInvitationsPruned,
                    "group-expired-invitations-pruned");
        }

        private bool TryMutation(
            long expectedCatalogRevision,
            Guid id,
            long expectedGroupRevision,
            out GroupRecord group,
            out GroupMutationResult failure)
        {
            group = null;
            failure = null;
            if (!Expected(expectedCatalogRevision)) { failure = Conflict(); return false; }
            if (id == Guid.Empty || expectedGroupRevision < 1)
            {
                failure = Invalid();
                return false;
            }
            if (!TryGetGroup(id, out group))
            {
                failure = Fail(GroupMutationCode.GroupMissing, "group-missing");
                return false;
            }
            if (group.Revision != expectedGroupRevision)
            {
                failure = Fail(GroupMutationCode.RevisionConflict, "group-revision-conflict", group);
                return false;
            }
            return true;
        }

        private GroupMutationResult Replace(
            GroupRecord current,
            GroupRecord replacement,
            GroupMutationCode code,
            string reason)
        {
            var groups = new List<GroupRecord>(_groups.Length + (current == null ? 1 : 0));
            foreach (GroupRecord group in _groups)
                if (current == null || group.Id != current.Id) groups.Add(group);
            groups.Add(replacement);
            var catalog = new GroupCatalog(
                checked(Revision + 1), groups, _retired, CommandLedger);
            return new GroupMutationResult(code, reason, catalog, replacement);
        }

        private bool NameExists(string name, string exceptId)
        {
            foreach (GroupRecord group in _groups)
                if (!string.Equals(group.IdText, exceptId, StringComparison.Ordinal) &&
                    string.Equals(group.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private int CountMemberships(StableIdentity identity)
        {
            int count = 0;
            foreach (GroupRecord group in _groups)
                if (group.TryGetMember(identity, out _)) count++;
            return count;
        }

        private static bool IsOwner(GroupRecord group, StableIdentity identity) =>
            group.TryGetMember(identity, out GroupMember member) && member.Role == GroupRole.Owner;

        private static bool IsOfficerOrOwner(GroupRecord group, StableIdentity identity) =>
            group.TryGetMember(identity, out GroupMember member) && member.Role >= GroupRole.Officer;

        private bool Expected(long revision) => revision >= 0 && revision == Revision;
        private GroupMutationResult Conflict() =>
            Fail(GroupMutationCode.RevisionConflict, "group-catalog-revision-conflict");
        private GroupMutationResult Invalid(GroupRecord group = null) =>
            Fail(GroupMutationCode.InvalidRequest, "group-request-invalid", group);
        private GroupMutationResult Unauthorized(GroupRecord group) =>
            Fail(GroupMutationCode.Unauthorized, "group-actor-unauthorized", group);
        private GroupMutationResult Ok(GroupMutationCode code, string reason, GroupRecord group) =>
            new GroupMutationResult(code, reason, this, group);
        private GroupMutationResult Fail(GroupMutationCode code, string reason, GroupRecord group = null) =>
            new GroupMutationResult(code, reason, this, group);

        private static bool TryDisplayName(string value)
        {
            try { GroupIdentity.RequireDisplayName(value); return true; }
            catch (ArgumentException) { return false; }
        }

        private static GroupRecord[] CopyGroups(IEnumerable<GroupRecord> source, long revision)
        {
            GroupRecord[] copy = (source ?? Array.Empty<GroupRecord>()).ToArray();
            if (copy.Length > GroupLimits.MaximumGroups ||
                copy.Any(value => value == null || value.Revision > revision))
                throw new ArgumentOutOfRangeException(nameof(source));
            Array.Sort(copy, (left, right) => string.Compare(left.IdText, right.IdText, StringComparison.Ordinal));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < copy.Length; index++)
            {
                if (index > 0 && string.Equals(copy[index - 1].IdText, copy[index].IdText, StringComparison.Ordinal))
                    throw new ArgumentException("Group UUIDs must be unique.", nameof(source));
                if (!names.Add(copy[index].DisplayName))
                    throw new ArgumentException("Group display names must be unique.", nameof(source));
            }
            return copy;
        }

        private static RetiredGroupId[] CopyRetired(
            IEnumerable<RetiredGroupId> source,
            long revision,
            IReadOnlyList<GroupRecord> groups)
        {
            RetiredGroupId[] copy = (source ?? Array.Empty<RetiredGroupId>()).ToArray();
            if (copy.Length > GroupLimits.MaximumRetiredGroupIds ||
                copy.Any(value => value == null || value.DeletedCatalogRevision > revision))
                throw new ArgumentOutOfRangeException(nameof(source));
            Array.Sort(copy, (left, right) => string.Compare(left.IdText, right.IdText, StringComparison.Ordinal));
            for (int index = 0; index < copy.Length; index++)
            {
                if (index > 0 && string.Equals(copy[index - 1].IdText, copy[index].IdText, StringComparison.Ordinal))
                    throw new ArgumentException("Retired group UUIDs must be unique.", nameof(source));
                if (FindGroup(groups, copy[index].IdText) >= 0)
                    throw new ArgumentException("A current group UUID cannot also be retired.", nameof(source));
            }
            return copy;
        }

        private static void ValidateMembershipBounds(IEnumerable<GroupRecord> groups)
        {
            var counts = new Dictionary<StableIdentity, int>();
            foreach (GroupRecord group in groups)
                foreach (GroupMember member in group.Members)
                {
                    counts.TryGetValue(member.Identity, out int count);
                    count++;
                    if (count > GroupLimits.MaximumGroupsPerIdentity)
                        throw new ArgumentOutOfRangeException(nameof(groups));
                    counts[member.Identity] = count;
                }
        }

        private static int FindGroup(IReadOnlyList<GroupRecord> groups, string idText)
        {
            int low = 0;
            int high = groups.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = string.Compare(groups[middle].IdText, idText, StringComparison.Ordinal);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }

        private static int FindRetired(IReadOnlyList<RetiredGroupId> retired, string idText)
        {
            int low = 0;
            int high = retired.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = string.Compare(retired[middle].IdText, idText, StringComparison.Ordinal);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }
    }
}
