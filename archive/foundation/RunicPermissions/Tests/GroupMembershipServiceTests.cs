using System;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupMembershipServiceTests
    {
        private const string World = "valheim.0000000000000001";
        private static readonly Guid GroupId =
            Guid.ParseExact("11111111111111111111111111111111", "N");
        private static readonly StableIdentity Owner = new StableIdentity("steam", "owner");
        private static readonly StableIdentity Other = new StableIdentity("steam", "other");

        internal static void Register()
        {
            TestRunner.Run("built-in group resolver returns current membership", ResolvesMembership);
            TestRunner.Run("built-in group resolver reports a missing group", ReportsMissingGroup);
            TestRunner.Run("built-in group resolver denies corrupt evidence", DeniesCorruptEvidence);
            TestRunner.Run("built-in group resolver denies a missing world scope", DeniesMissingWorld);
            TestRunner.Run("built-in group membership lists are bounded snapshots", ListsMemberships);
        }

        private static void ResolvesMembership()
        {
            FileBackedGroupMembershipService service = Service(ReadyCatalog());
            GroupMembershipContext owner = service.Resolve(GroupId.ToString("N"), Owner);
            GroupMembershipContext other = service.Resolve(GroupId.ToString("N"), Other);

            TestAssert.Equal(GroupResolutionStatus.Available, owner.Status);
            TestAssert.True(owner.IsMember);
            TestAssert.Equal(GroupCapabilities.ProviderId, owner.ProviderId);
            TestAssert.Equal(GroupResolutionStatus.Available, other.Status);
            TestAssert.False(other.IsMember);
        }

        private static void ReportsMissingGroup()
        {
            GroupMembershipContext result = Service(ReadyCatalog()).Resolve(
                "22222222222222222222222222222222",
                Owner);
            TestAssert.Equal(GroupResolutionStatus.GroupMissing, result.Status);
            TestAssert.False(result.IsMember);
        }

        private static void DeniesCorruptEvidence()
        {
            var read = new GroupWorldReadResult(
                GroupWorldReadState.Corrupt,
                "group-store-corrupt",
                null,
                string.Empty);
            GroupMembershipContext result = Service(read).Resolve(GroupId.ToString("N"), Owner);
            TestAssert.Equal(GroupResolutionStatus.Ambiguous, result.Status);
            TestAssert.False(result.IsMember);
        }

        private static void DeniesMissingWorld()
        {
            var service = new FileBackedGroupMembershipService(
                new FixedStore(ReadyCatalog()),
                () => string.Empty);
            GroupMembershipContext result = service.Resolve(GroupId.ToString("N"), Owner);
            TestAssert.Equal(GroupResolutionStatus.Stale, result.Status);
            TestAssert.False(result.IsMember);
        }

        private static void ListsMemberships()
        {
            FileBackedGroupMembershipService service = Service(ReadyCatalog());
            var memberships = service.GetMemberships(Owner, out GroupResolutionStatus status);
            TestAssert.Equal(GroupResolutionStatus.Available, status);
            TestAssert.Equal(1, memberships.Count);
            TestAssert.Equal(GroupId.ToString("N"), memberships[0].GroupIdText);
            TestAssert.Equal(GroupRole.Owner, memberships[0].Role);
        }

        private static FileBackedGroupMembershipService Service(GroupWorldReadResult read) =>
            new FileBackedGroupMembershipService(new FixedStore(read), () => World);

        private static GroupWorldReadResult ReadyCatalog()
        {
            GroupMutationResult created = GroupCatalog.Empty.Create(0, GroupId, "Builders", Owner);
            TestAssert.True(created.Success);
            return new GroupWorldReadResult(
                GroupWorldReadState.Ready,
                "group-store-ready",
                created.Catalog,
                "hash");
        }

        private sealed class FixedStore : IGroupWorldStore
        {
            private readonly GroupWorldReadResult _read;

            internal FixedStore(GroupWorldReadResult read) { _read = read; }

            public GroupWorldReadResult Read(string worldScope) => _read;

            public GroupWorldCommitResult TryCommit(
                string worldScope,
                long expectedCatalogRevision,
                GroupCatalog replacement) => throw new NotSupportedException();
        }
    }
}
