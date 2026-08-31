using System;
using System.Collections.Generic;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    /// <summary>
    /// Server-owned resolver for the built-in Runic Group catalog. Consumers use this service
    /// immediately before an authoritative action; cached client answers are presentation only.
    /// </summary>
    public interface IGroupMembershipService
    {
        GroupMembershipContext Resolve(string groupId, StableIdentity identity);

        IReadOnlyList<GroupMembership> GetMemberships(
            StableIdentity identity,
            out GroupResolutionStatus status);
    }

    public static class GroupCapabilities
    {
        public const string Membership = "permissions.groups";
        public const string ActiveSelection = "permissions.groups.active";
        public const string ProviderId = "runic.permissions.groups";
        // Persistence advertises one protocol major for the complete Permissions module.
        // Group management first ships with the durable prepare/execute exchange, so it
        // uses that same release protocol rather than inventing an undiscoverable per-capability
        // major that could not match the module hello.
        public const int ProtocolVersion = 1;
    }

    /// <summary>
    /// Reads the exact current primary catalog for every authorization query. This     /// favors fail-closed correctness over an unversioned in-memory cache.
    /// </summary>
    public sealed class FileBackedGroupMembershipService : IGroupMembershipService
    {
        private readonly IGroupWorldStore _store;
        private readonly Func<string> _worldScopeProvider;

        public FileBackedGroupMembershipService(
            IGroupWorldStore store,
            Func<string> worldScopeProvider)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _worldScopeProvider = worldScopeProvider ??
                                  throw new ArgumentNullException(nameof(worldScopeProvider));
        }

        public GroupMembershipContext Resolve(string groupId, StableIdentity identity)
        {
            if (!GroupIdentity.IsCanonicalId(groupId) || identity == null)
                return Context(GroupResolutionStatus.Ambiguous, groupId, false);

            GroupWorldReadResult read = ReadCurrent(out GroupResolutionStatus status);
            if (status != GroupResolutionStatus.Available)
                return Context(status, groupId, false);

            if (!Guid.TryParseExact(groupId, "N", out Guid id) ||
                !read.Catalog.TryGetGroup(id, out GroupRecord group))
                return Context(GroupResolutionStatus.GroupMissing, groupId, false);

            return Context(
                GroupResolutionStatus.Available,
                group.IdText,
                group.TryGetMember(identity, out _));
        }

        public IReadOnlyList<GroupMembership> GetMemberships(
            StableIdentity identity,
            out GroupResolutionStatus status)
        {
            if (identity == null)
            {
                status = GroupResolutionStatus.Ambiguous;
                return Array.Empty<GroupMembership>();
            }

            GroupWorldReadResult read = ReadCurrent(out status);
            return status == GroupResolutionStatus.Available
                ? read.Catalog.GetMemberships(identity)
                : Array.Empty<GroupMembership>();
        }

        private GroupWorldReadResult ReadCurrent(out GroupResolutionStatus status)
        {
            string worldScope;
            try { worldScope = _worldScopeProvider() ?? string.Empty; }
            catch
            {
                status = GroupResolutionStatus.Stale;
                return null;
            }

            if (worldScope.Length == 0)
            {
                status = GroupResolutionStatus.Stale;
                return null;
            }

            GroupWorldReadResult read;
            try { read = _store.Read(worldScope); }
            catch
            {
                status = GroupResolutionStatus.Stale;
                return null;
            }

            if (read == null)
            {
                status = GroupResolutionStatus.Ambiguous;
                return null;
            }

            switch (read.State)
            {
                case GroupWorldReadState.Ready:
                case GroupWorldReadState.Missing:
                    status = GroupResolutionStatus.Available;
                    return read;
                case GroupWorldReadState.Corrupt:
                case GroupWorldReadState.EvidenceConflict:
                    status = GroupResolutionStatus.Ambiguous;
                    return read;
                default:
                    status = GroupResolutionStatus.Stale;
                    return read;
            }
        }

        private static GroupMembershipContext Context(
            GroupResolutionStatus status,
            string groupId,
            bool isMember) => new GroupMembershipContext(
                status,
                groupId ?? string.Empty,
                isMember,
                GroupCapabilities.ProviderId);
    }
}
