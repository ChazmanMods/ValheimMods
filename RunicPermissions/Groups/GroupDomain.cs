using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    public enum GroupRole : byte
    {
        Member = 1,
        Officer = 2,
        Owner = 3
    }

    public static class GroupLimits
    {
        public const int MaximumGroups = 256;
        public const int MaximumMembersPerGroup = 256;
        public const int MaximumInvitationsPerGroup = 256;
        public const int MaximumGroupsPerIdentity = 64;
        public const int MaximumRetiredGroupIds = 4096;
        public const int MaximumDisplayNameUtf8Bytes = 64;
        public const int MaximumWorldScopeUtf8Bytes = 128;
        public const int MaximumCatalogBytes = 8 * 1024 * 1024;
        public static readonly TimeSpan MaximumInvitationLifetime = TimeSpan.FromDays(30);
    }

    public static class GroupIdentity
    {
        public static string ToCanonicalId(Guid groupId)
        {
            if (groupId == Guid.Empty)
                throw new ArgumentException("A nonempty group UUID is required.", nameof(groupId));
            return groupId.ToString("N");
        }

        public static bool TryParseCanonicalId(string value, out Guid groupId)
        {
            groupId = Guid.Empty;
            return value != null && value.Length == 32 &&
                   Guid.TryParseExact(value, "N", out groupId) && groupId != Guid.Empty &&
                   string.Equals(value, groupId.ToString("N"), StringComparison.Ordinal);
        }

        public static bool IsCanonicalId(string value) =>
            TryParseCanonicalId(value, out _);

        internal static string RequireDisplayName(string value)
        {
            if (value == null || value.Length == 0 ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                !value.IsNormalized(NormalizationForm.FormC) ||
                Encoding.UTF8.GetByteCount(value) > GroupLimits.MaximumDisplayNameUtf8Bytes)
                throw new ArgumentException(
                    "A nonempty, trimmed, NFC group display name within 64 UTF-8 bytes is required.",
                    nameof(value));
            for (int index = 0; index < value.Length; index++)
                if (char.IsControl(value[index]))
                    throw new ArgumentException(
                        "Group display names cannot contain control characters.", nameof(value));
            return value;
        }

        internal static string RequireWorldScope(string value)
        {
            if (value == null || value.Length == 0 || value.Length > GroupLimits.MaximumWorldScopeUtf8Bytes ||
                Encoding.UTF8.GetByteCount(value) > GroupLimits.MaximumWorldScopeUtf8Bytes)
                throw new ArgumentException("A bounded canonical world scope is required.", nameof(value));
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                bool valid = current >= 'a' && current <= 'z' ||
                             current >= '0' && current <= '9' ||
                             current == '.' || current == '_' || current == '-';
                if (!valid)
                    throw new ArgumentException("The world scope is not canonical.", nameof(value));
            }
            return value;
        }
    }

    public sealed class GroupMember
    {
        public GroupMember(StableIdentity identity, GroupRole role, long joinedRevision)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (!Enum.IsDefined(typeof(GroupRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            if (joinedRevision < 1) throw new ArgumentOutOfRangeException(nameof(joinedRevision));
            Role = role;
            JoinedRevision = joinedRevision;
        }

        public StableIdentity Identity { get; }
        public GroupRole Role { get; }
        public long JoinedRevision { get; }
    }

    public sealed class GroupInvitation
    {
        public GroupInvitation(
            StableIdentity invitee,
            StableIdentity invitedBy,
            long issuedRevision,
            long expiresUtcTicks)
        {
            Invitee = invitee ?? throw new ArgumentNullException(nameof(invitee));
            InvitedBy = invitedBy ?? throw new ArgumentNullException(nameof(invitedBy));
            if (issuedRevision < 1) throw new ArgumentOutOfRangeException(nameof(issuedRevision));
            if (expiresUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(expiresUtcTicks));
            IssuedRevision = issuedRevision;
            ExpiresUtcTicks = expiresUtcTicks;
        }

        public StableIdentity Invitee { get; }
        public StableIdentity InvitedBy { get; }
        public long IssuedRevision { get; }
        public long ExpiresUtcTicks { get; }
        public bool IsExpired(long nowUtcTicks) => nowUtcTicks <= 0 || nowUtcTicks >= ExpiresUtcTicks;
    }

    public sealed class GroupRecord
    {
        private readonly GroupMember[] _members;
        private readonly GroupInvitation[] _invitations;
        private readonly ReadOnlyCollection<GroupMember> _memberView;
        private readonly ReadOnlyCollection<GroupInvitation> _invitationView;

        public GroupRecord(
            Guid id,
            string displayName,
            long revision,
            IEnumerable<GroupMember> members,
            IEnumerable<GroupInvitation> invitations = null)
        {
            Id = id;
            IdText = GroupIdentity.ToCanonicalId(id);
            DisplayName = GroupIdentity.RequireDisplayName(displayName);
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            Revision = revision;
            _members = CopyMembers(members, revision);
            _invitations = CopyInvitations(invitations, revision, _members);
            _memberView = Array.AsReadOnly(_members);
            _invitationView = Array.AsReadOnly(_invitations);
        }

        public Guid Id { get; }
        public string IdText { get; }
        public string DisplayName { get; }
        public long Revision { get; }
        public IReadOnlyList<GroupMember> Members => _memberView;
        public IReadOnlyList<GroupInvitation> Invitations => _invitationView;

        public bool TryGetMember(StableIdentity identity, out GroupMember member)
        {
            member = null;
            if (identity == null) return false;
            int index = FindMember(_members, identity);
            if (index < 0) return false;
            member = _members[index];
            return true;
        }

        public bool TryGetInvitation(StableIdentity identity, out GroupInvitation invitation)
        {
            invitation = null;
            if (identity == null) return false;
            int index = FindInvitation(_invitations, identity);
            if (index < 0) return false;
            invitation = _invitations[index];
            return true;
        }

        internal GroupRecord Rename(string displayName) =>
            new GroupRecord(Id, displayName, checked(Revision + 1), _members, _invitations);

        internal GroupRecord Invite(StableIdentity actor, StableIdentity invitee, long expiresUtcTicks)
        {
            var invitations = new List<GroupInvitation>(_invitations)
            {
                new GroupInvitation(invitee, actor, checked(Revision + 1), expiresUtcTicks)
            };
            return new GroupRecord(Id, DisplayName, checked(Revision + 1), _members, invitations);
        }

        internal GroupRecord CancelInvitation(StableIdentity invitee)
        {
            var invitations = _invitations.Where(value => !value.Invitee.Equals(invitee)).ToArray();
            return new GroupRecord(Id, DisplayName, checked(Revision + 1), _members, invitations);
        }

        internal GroupRecord Accept(StableIdentity invitee)
        {
            long next = checked(Revision + 1);
            var members = new List<GroupMember>(_members)
            {
                new GroupMember(invitee, GroupRole.Member, next)
            };
            var invitations = _invitations.Where(value => !value.Invitee.Equals(invitee)).ToArray();
            return new GroupRecord(Id, DisplayName, next, members, invitations);
        }

        internal GroupRecord RemoveMember(StableIdentity identity)
        {
            var members = _members.Where(value => !value.Identity.Equals(identity)).ToArray();
            return new GroupRecord(Id, DisplayName, checked(Revision + 1), members, _invitations);
        }

        internal GroupRecord SetRole(StableIdentity identity, GroupRole role)
        {
            var members = new GroupMember[_members.Length];
            for (int index = 0; index < _members.Length; index++)
            {
                GroupMember current = _members[index];
                members[index] = current.Identity.Equals(identity)
                    ? new GroupMember(current.Identity, role, current.JoinedRevision)
                    : current;
            }
            return new GroupRecord(Id, DisplayName, checked(Revision + 1), members, _invitations);
        }

        internal GroupRecord TransferOwnership(StableIdentity owner, StableIdentity successor)
        {
            var members = new GroupMember[_members.Length];
            for (int index = 0; index < _members.Length; index++)
            {
                GroupMember current = _members[index];
                GroupRole role = current.Identity.Equals(owner)
                    ? GroupRole.Officer
                    : current.Identity.Equals(successor) ? GroupRole.Owner : current.Role;
                members[index] = role == current.Role
                    ? current
                    : new GroupMember(current.Identity, role, current.JoinedRevision);
            }
            return new GroupRecord(Id, DisplayName, checked(Revision + 1), members, _invitations);
        }

        internal GroupRecord PruneExpired(long nowUtcTicks)
        {
            GroupInvitation[] invitations = _invitations
                .Where(value => !value.IsExpired(nowUtcTicks))
                .ToArray();
            return invitations.Length == _invitations.Length
                ? this
                : new GroupRecord(Id, DisplayName, checked(Revision + 1), _members, invitations);
        }

        private static GroupMember[] CopyMembers(IEnumerable<GroupMember> source, long revision)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            GroupMember[] copy = source.ToArray();
            if (copy.Length < 1 || copy.Length > GroupLimits.MaximumMembersPerGroup ||
                copy.Any(value => value == null || value.JoinedRevision > revision))
                throw new ArgumentOutOfRangeException(nameof(source));
            Array.Sort(copy, (left, right) => left.Identity.CompareTo(right.Identity));
            int ownerCount = 0;
            for (int index = 0; index < copy.Length; index++)
            {
                if (index > 0 && copy[index - 1].Identity.Equals(copy[index].Identity))
                    throw new ArgumentException("Group member identities must be unique.", nameof(source));
                if (copy[index].Role == GroupRole.Owner) ownerCount++;
            }
            if (ownerCount != 1)
                throw new ArgumentException("A group requires exactly one Owner.", nameof(source));
            return copy;
        }

        private static GroupInvitation[] CopyInvitations(
            IEnumerable<GroupInvitation> source,
            long revision,
            IReadOnlyList<GroupMember> members)
        {
            GroupInvitation[] copy = (source ?? Array.Empty<GroupInvitation>()).ToArray();
            if (copy.Length > GroupLimits.MaximumInvitationsPerGroup ||
                copy.Any(value => value == null || value.IssuedRevision > revision))
                throw new ArgumentOutOfRangeException(nameof(source));
            Array.Sort(copy, (left, right) => left.Invitee.CompareTo(right.Invitee));
            for (int index = 0; index < copy.Length; index++)
            {
                if (index > 0 && copy[index - 1].Invitee.Equals(copy[index].Invitee))
                    throw new ArgumentException("Group invitation identities must be unique.", nameof(source));
                // InvitedBy is immutable audit history. The inviter may legitimately leave or be
                // removed before acceptance, so only the invitee's current nonmembership is a
                // snapshot invariant; authority is checked when Invite creates the record.
                if (FindMember(members, copy[index].Invitee) >= 0)
                    throw new ArgumentException("Group invitation membership evidence is invalid.", nameof(source));
            }
            return copy;
        }

        internal static int FindMember(IReadOnlyList<GroupMember> values, StableIdentity identity)
        {
            int low = 0;
            int high = values.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = values[middle].Identity.CompareTo(identity);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }

        private static int FindInvitation(
            IReadOnlyList<GroupInvitation> values,
            StableIdentity identity)
        {
            int low = 0;
            int high = values.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = values[middle].Invitee.CompareTo(identity);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }
    }

    public sealed class RetiredGroupId
    {
        public RetiredGroupId(Guid id, long deletedCatalogRevision)
        {
            Id = id;
            IdText = GroupIdentity.ToCanonicalId(id);
            if (deletedCatalogRevision < 1)
                throw new ArgumentOutOfRangeException(nameof(deletedCatalogRevision));
            DeletedCatalogRevision = deletedCatalogRevision;
        }

        public Guid Id { get; }
        public string IdText { get; }
        public long DeletedCatalogRevision { get; }
    }
}
