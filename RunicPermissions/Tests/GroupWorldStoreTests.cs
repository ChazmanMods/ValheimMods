using System;
using System.IO;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupWorldStoreTests
    {
        private const string World = "valheim.0123456789abcdef";
        private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly StableIdentity Owner = new StableIdentity("steam", "owner");

        internal static void Register()
        {
            TestRunner.Run("group catalog codec is canonical and exact", CodecIsCanonical);
            TestRunner.Run("group catalog codec rejects malformed trailing and wrong world", CodecRejectsMalformedEvidence);
            TestRunner.Run("group world store atomically creates replaces and CAS checks", StoreCommitsAndChecksRevision);
            TestRunner.Run("group world store fails closed on corrupt primary", CorruptPrimaryFailsClosed);
            TestRunner.Run("group world store never promotes temporary evidence", TemporaryEvidenceFailsClosed);
            TestRunner.Run("group world store never promotes backup evidence", BackupEvidenceFailsClosed);
            TestRunner.Run("group world store rejects wrong-world primary", WrongWorldPrimaryFailsClosed);
            TestRunner.Run("group world store rejects rewrite-capable primary handle", RewriteCapableHandleFailsClosed);
            TestRunner.Run("group world store preserves raced forensic evidence", RacedEvidenceIsPreserved);
            TestRunner.Run("group world store rejects reparse traversal and file", ReparseTraversalFailsClosed);
        }

        private static void CodecIsCanonical()
        {
            GroupCatalog catalog = Catalog();
            byte[] first = GroupCatalogCodec.Encode(World, catalog);
            byte[] second = GroupCatalogCodec.Encode(World, catalog);
            TestAssert.Equal(Convert.ToBase64String(first), Convert.ToBase64String(second));
            TestAssert.True(GroupCatalogCodec.TryDecode(first, World, out GroupCatalog decoded, out string reason), reason);
            TestAssert.Equal(catalog.Revision, decoded.Revision);
            TestAssert.Equal(catalog.Groups[0].Id, decoded.Groups[0].Id);
            TestAssert.Equal(catalog.Groups[0].DisplayName, decoded.Groups[0].DisplayName);
        }

        private static void CodecRejectsMalformedEvidence()
        {
            byte[] exact = GroupCatalogCodec.Encode(World, Catalog());
            byte[] corrupt = (byte[])exact.Clone();
            corrupt[12] ^= 0x40;
            TestAssert.False(GroupCatalogCodec.TryDecode(corrupt, World, out _, out _));
            var trailing = new byte[exact.Length + 1];
            Buffer.BlockCopy(exact, 0, trailing, 0, exact.Length);
            TestAssert.False(GroupCatalogCodec.TryDecode(trailing, World, out _, out _));
            TestAssert.False(GroupCatalogCodec.TryDecode(exact, "valheim.ffffffffffffffff", out _, out string reason));
            TestAssert.Equal("group-catalog-world-mismatch", reason);
        }

        private static void StoreCommitsAndChecksRevision()
        {
            WithStore((store, _) =>
            {
                GroupWorldReadResult missing = store.Read(World);
                TestAssert.Equal(GroupWorldReadState.Missing, missing.State);
                GroupCatalog first = Catalog();
                GroupWorldCommitResult committed = store.TryCommit(World, 0, first);
                TestAssert.Equal(GroupWorldCommitState.Committed, committed.State);
                TestAssert.Equal(1L, committed.Current.Catalog.Revision);

                GroupMutationResult renamed = first.Rename(
                    first.Revision, Id, first.Groups[0].Revision, Owner, "Ravens Two");
                TestAssert.True(renamed.Success);
                GroupWorldCommitResult stale = store.TryCommit(World, 0, renamed.Catalog);
                TestAssert.Equal(GroupWorldCommitState.InvalidReplacement, stale.State);
                GroupWorldCommitResult replaced = store.TryCommit(World, 1, renamed.Catalog);
                TestAssert.Equal(GroupWorldCommitState.Committed, replaced.State);
                TestAssert.Equal("Ravens Two", store.Read(World).Catalog.Groups[0].DisplayName);

                GroupCatalog competing = new GroupCatalog(
                    2,
                    new[]
                    {
                        new GroupRecord(
                            Id,
                            "Competing",
                            2,
                            new[] { new GroupMember(Owner, GroupRole.Owner, 1) })
                    });
                GroupWorldCommitResult conflict = store.TryCommit(World, 1, competing);
                TestAssert.Equal(GroupWorldCommitState.RevisionConflict, conflict.State);
                TestAssert.Equal(2L, conflict.Current.Catalog.Revision);
            });
        }

        private static void CorruptPrimaryFailsClosed()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TryCommit(World, 0, Catalog()).Success);
                string primary = store.GetPrimaryPath(World);
                byte[] bytes = File.ReadAllBytes(primary);
                bytes[0] ^= 0xff;
                File.WriteAllBytes(primary, bytes);
                GroupWorldReadResult read = store.Read(World);
                TestAssert.Equal(GroupWorldReadState.Corrupt, read.State);
                TestAssert.Equal(
                    GroupWorldCommitState.Corrupt,
                    store.TryCommit(World, 1, NextCatalog()).State);
            });
        }

        private static void TemporaryEvidenceFailsClosed()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TryCommit(World, 0, Catalog()).Success);
                byte[] authoritative = File.ReadAllBytes(store.GetPrimaryPath(World));
                File.WriteAllBytes(store.GetTemporaryPath(World), authoritative);
                TestAssert.Equal(GroupWorldReadState.EvidenceConflict, store.Read(World).State);
                TestAssert.Equal(
                    GroupWorldCommitState.EvidenceConflict,
                    store.TryCommit(World, 1, NextCatalog()).State);
                TestAssert.True(File.Exists(store.GetTemporaryPath(World)));
            });
        }

        private static void BackupEvidenceFailsClosed()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TryCommit(World, 0, Catalog()).Success);
                File.Copy(store.GetPrimaryPath(World), store.GetBackupPath(World));
                TestAssert.Equal(GroupWorldReadState.EvidenceConflict, store.Read(World).State);
                TestAssert.Equal(
                    GroupWorldCommitState.EvidenceConflict,
                    store.TryCommit(World, 1, NextCatalog()).State);
                TestAssert.True(File.Exists(store.GetBackupPath(World)));
            });
        }

        private static void WrongWorldPrimaryFailsClosed()
        {
            WithStore((store, _) =>
            {
                string primary = store.GetPrimaryPath(World);
                Directory.CreateDirectory(Path.GetDirectoryName(primary));
                File.WriteAllBytes(
                    primary,
                    GroupCatalogCodec.Encode("valheim.ffffffffffffffff", Catalog()));
                GroupWorldReadResult read = store.Read(World);
                TestAssert.Equal(GroupWorldReadState.Corrupt, read.State);
                TestAssert.Equal("group-catalog-world-mismatch", read.ReasonCode);
            });
        }

        private static void RewriteCapableHandleFailsClosed()
        {
            WithStore((store, _) =>
            {
                TestAssert.True(store.TryCommit(World, 0, Catalog()).Success);
                using (var rewriteCapable = new FileStream(
                           store.GetPrimaryPath(World),
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    TestAssert.Equal(GroupWorldReadState.Unavailable, store.Read(World).State);
                    TestAssert.Equal(
                        GroupWorldCommitState.Unavailable,
                        store.TryCommit(World, 1, NextCatalog()).State);
                }
                TestAssert.Equal(GroupWorldReadState.Ready, store.Read(World).State);
            });
        }

        private static void RacedEvidenceIsPreserved()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "RunicPermissionsGroupTests", Guid.NewGuid().ToString("N"));
            FileGroupWorldStore store = null;
            byte[] forensic = { 9, 8, 7 };
            try
            {
                store = new FileGroupWorldStore(
                    root,
                    stage =>
                    {
                        if (stage == GroupWorldStoreStage.DirectoryCreated)
                            File.WriteAllBytes(store.GetTemporaryPath(World), forensic);
                    });
                GroupWorldCommitResult result = store.TryCommit(World, 0, Catalog());
                TestAssert.Equal(GroupWorldCommitState.EvidenceConflict, result.State);
                TestAssert.Equal(
                    Convert.ToBase64String(forensic),
                    Convert.ToBase64String(File.ReadAllBytes(store.GetTemporaryPath(World))));

                File.Delete(store.GetTemporaryPath(World));
                FileStream rewrite = null;
                store = new FileGroupWorldStore(
                    root,
                    stage =>
                    {
                        if (stage == GroupWorldStoreStage.AtomicCommitCompleted)
                            rewrite = new FileStream(
                                store.GetPrimaryPath(World),
                                FileMode.Open,
                                FileAccess.ReadWrite,
                                FileShare.ReadWrite | FileShare.Delete);
                    });
                try
                {
                    result = store.TryCommit(World, 0, Catalog());
                    TestAssert.Equal(GroupWorldCommitState.Unavailable, result.State);
                    TestAssert.True(File.Exists(store.GetPrimaryPath(World)));
                }
                finally
                {
                    rewrite?.Dispose();
                }
                TestAssert.Equal(GroupWorldReadState.Ready, store.Read(World).State);
            }
            finally
            {
                DeleteGeneratedTree(root);
            }
        }

        private static void ReparseTraversalFailsClosed()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(), "RunicPermissionsGroupTests", Guid.NewGuid().ToString("N"));
            string targetRoot = Path.Combine(testRoot, "target");
            string linkedRoot = Path.Combine(testRoot, "linked");
            Directory.CreateDirectory(targetRoot);
            bool fixtureAvailable = false;
            try
            {
                Directory.CreateSymbolicLink(linkedRoot, targetRoot);
                fixtureAvailable = true;
                var traversalStore = new FileGroupWorldStore(linkedRoot);
                TestAssert.Equal(GroupWorldReadState.Unavailable, traversalStore.Read(World).State);
                TestAssert.Equal(
                    GroupWorldCommitState.Unavailable,
                    traversalStore.TryCommit(World, 0, Catalog()).State);

                string regularRoot = Path.Combine(testRoot, "regular");
                var regularStore = new FileGroupWorldStore(regularRoot);
                Directory.CreateDirectory(regularRoot);
                string outside = Path.Combine(testRoot, "outside.groups");
                File.WriteAllBytes(outside, GroupCatalogCodec.Encode(World, Catalog()));
                File.CreateSymbolicLink(regularStore.GetPrimaryPath(World), outside);
                TestAssert.Equal(GroupWorldReadState.Unavailable, regularStore.Read(World).State);
                TestAssert.Equal(
                    Convert.ToBase64String(GroupCatalogCodec.Encode(World, Catalog())),
                    Convert.ToBase64String(File.ReadAllBytes(outside)));
            }
            catch (Exception exception) when (
                !fixtureAvailable &&
                (exception is UnauthorizedAccessException ||
                 exception is PlatformNotSupportedException || exception is IOException))
            {
                // Some hosts prohibit unprivileged symlink creation. Other race fixtures still
                // exercise exclusive handles; production performs the explicit ReparsePoint walk.
            }
            finally
            {
                try { if (Directory.Exists(linkedRoot)) Directory.Delete(linkedRoot); }
                catch { }
                DeleteGeneratedTree(testRoot);
            }
        }

        private static GroupCatalog Catalog()
        {
            GroupMutationResult created = GroupCatalog.Empty.Create(0, Id, "Ravens", Owner);
            TestAssert.True(created.Success);
            return created.Catalog;
        }

        private static GroupCatalog NextCatalog()
        {
            GroupCatalog catalog = Catalog();
            return catalog.Rename(1, Id, 1, Owner, "Ravens Two").Catalog;
        }

        private static void WithStore(Action<FileGroupWorldStore, string> test)
        {
            string root = Path.Combine(
                Path.GetTempPath(), "RunicPermissionsGroupTests", Guid.NewGuid().ToString("N"));
            try
            {
                test(new FileGroupWorldStore(root), root);
            }
            finally
            {
                DeleteGeneratedTree(root);
            }
        }

        private static void DeleteGeneratedTree(string root)
        {
            try
            {
                string full = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath());
                string leaf = Path.GetFileName(full);
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                    (leaf.IndexOf("RunicPermissionsGroupTests", StringComparison.Ordinal) < 0 &&
                     Directory.GetParent(full)?.Name.IndexOf(
                         "RunicPermissionsGroupTests", StringComparison.Ordinal) < 0))
                    return;
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch
            {
                // Failed race fixtures retain forensic evidence; cleanup is best effort.
            }
        }
    }
}
