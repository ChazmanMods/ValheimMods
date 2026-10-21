using System;
using System.IO;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupActiveSelectionTests
    {
        private const string World = "valheim.0000000000000042";
        private static readonly StableIdentity Owner =
            new StableIdentity("valheim.player", "1001");
        private static readonly StableIdentity Other =
            new StableIdentity("valheim.player", "1002");
        private static readonly Guid GroupId =
            Guid.ParseExact("42424242424242424242424242424242", "N");

        internal static void Register()
        {
            TestRunner.Run("active Group selection persists by world and stable identity", PersistsExactly);
            TestRunner.Run("active Group selection can be explicitly cleared", ClearsSelection);
            TestRunner.Run("active Group selection corruption fails closed", CorruptionFailsClosed);
            TestRunner.Run("active Group service rejoins selection to current membership", RevalidatesMembership);
            TestRunner.Run("active Group client cache is identity-bound", ClientCacheIsIdentityBound);
        }

        private static void PersistsExactly()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TrySet(World, Owner, GroupId, out string reason));
                TestAssert.Equal("group-active-selected", reason);
                GroupActiveSelectionReadResult owner = store.Read(World, Owner);
                GroupActiveSelectionReadResult other = store.Read(World, Other);
                TestAssert.True(owner.HasSelection);
                TestAssert.Equal(GroupId, owner.GroupId);
                TestAssert.Equal(GroupActiveSelectionReadState.Missing, other.State);
                TestAssert.Equal(
                    GroupActiveSelectionReadState.Missing,
                    store.Read("valheim.0000000000000043", Owner).State);
            });
        }

        private static void ClearsSelection()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TrySet(World, Owner, GroupId, out _));
                TestAssert.True(store.TrySet(World, Owner, Guid.Empty, out string reason));
                TestAssert.Equal("group-active-cleared", reason);
                GroupActiveSelectionReadResult read = store.Read(World, Owner);
                TestAssert.Equal(GroupActiveSelectionReadState.Ready, read.State);
                TestAssert.False(read.HasSelection);
            });
        }

        private static void CorruptionFailsClosed()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TrySet(World, Owner, GroupId, out _));
                File.WriteAllBytes(store.GetPrimaryPath(World, Owner), new byte[] { 1, 2, 3, 4 });
                GroupActiveSelectionReadResult read = store.Read(World, Owner);
                TestAssert.Equal(GroupActiveSelectionReadState.Corrupt, read.State);
                TestAssert.False(read.HasSelection);
            });
        }

        private static void RevalidatesMembership()
        {
            WithStore((store, _) =>
            {
                GroupMutationResult created = GroupCatalog.Empty.Create(
                    0, GroupId, "Builders", Owner);
                TestAssert.True(created.Success);
                var catalogs = new FixedCatalogStore(new GroupWorldReadResult(
                    GroupWorldReadState.Ready,
                    "group-store-ready",
                    created.Catalog,
                    "hash"));
                var service = new GroupActiveSelectionService(catalogs, store, () => World);
                TestAssert.True(service.TrySetAuthoritative(
                    Owner, GroupId, out ActiveGroupSelection selected, out _));
                TestAssert.True(selected.IsAvailable);
                TestAssert.Equal("Builders", selected.DisplayName);

                TestAssert.False(service.TrySetAuthoritative(
                    Other, GroupId, out ActiveGroupSelection denied, out _));
                TestAssert.Equal(ActiveGroupSelectionStatus.NotMember, denied.Status);
            });
        }

        private static void ClientCacheIsIdentityBound()
        {
            var catalogs = new FixedCatalogStore(new GroupWorldReadResult(
                GroupWorldReadState.Missing,
                "group-store-missing",
                GroupCatalog.Empty,
                string.Empty));
            WithStore((store, _) =>
            {
                var service = new GroupActiveSelectionService(catalogs, store, () => string.Empty);
                service.SetClientCache(Owner, new ActiveGroupSelection(
                    ActiveGroupSelectionStatus.Available,
                    GroupId.ToString("N"),
                    "Builders"));
                TestAssert.True(service.Resolve(Owner).IsAvailable);
                TestAssert.Equal(ActiveGroupSelectionStatus.Stale, service.Resolve(Other).Status);
                service.ClearClientCache();
                TestAssert.Equal(ActiveGroupSelectionStatus.Stale, service.Resolve(Owner).Status);
            });
        }

        private static void WithStore(Action<FileGroupActiveSelectionStore, string> test)
        {
            string root = Path.Combine(
                Path.GetTempPath(), "runic-permissions-active-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { test(new FileGroupActiveSelectionStore(root), root); }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private sealed class FixedCatalogStore : IGroupWorldStore
        {
            private readonly GroupWorldReadResult _read;
            internal FixedCatalogStore(GroupWorldReadResult read) { _read = read; }
            public GroupWorldReadResult Read(string worldScope) => _read;
            public GroupWorldCommitResult TryCommit(
                string worldScope,
                long expectedCatalogRevision,
                GroupCatalog replacement) => throw new NotSupportedException();
        }
    }
}
