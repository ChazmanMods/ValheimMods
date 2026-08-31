using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace RunicPermissions.Groups
{
    public enum GroupWorldReadState
    {
        Missing = 0,
        Ready = 1,
        Corrupt = 2,
        EvidenceConflict = 3,
        Unavailable = 4
    }

    public enum GroupWorldCommitState
    {
        Committed = 0,
        RevisionConflict = 1,
        Corrupt = 2,
        EvidenceConflict = 3,
        Unavailable = 4,
        InvalidReplacement = 5
    }

    public sealed class GroupWorldReadResult
    {
        internal GroupWorldReadResult(
            GroupWorldReadState state,
            string reasonCode,
            GroupCatalog catalog,
            string exactSha256)
        {
            State = state;
            ReasonCode = reasonCode ?? string.Empty;
            Catalog = catalog;
            ExactSha256 = exactSha256 ?? string.Empty;
        }

        public GroupWorldReadState State { get; }
        public string ReasonCode { get; }
        public GroupCatalog Catalog { get; }
        public string ExactSha256 { get; }
    }

    public sealed class GroupWorldCommitResult
    {
        internal GroupWorldCommitResult(
            GroupWorldCommitState state,
            string reasonCode,
            GroupWorldReadResult current)
        {
            State = state;
            ReasonCode = reasonCode ?? string.Empty;
            Current = current;
        }

        public GroupWorldCommitState State { get; }
        public string ReasonCode { get; }
        public GroupWorldReadResult Current { get; }
        public bool Success => State == GroupWorldCommitState.Committed;
    }

    public interface IGroupWorldStore
    {
        GroupWorldReadResult Read(string worldScope);
        GroupWorldCommitResult TryCommit(
            string worldScope,
            long expectedCatalogRevision,
            GroupCatalog replacement);
    }

    internal sealed class CompatibleGroupWorldStore : IGroupWorldStore
    {
        private readonly string _root;

        internal CompatibleGroupWorldStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A storage root is required.", nameof(root));
            _root = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (string.Equals(_root, Path.GetPathRoot(_root), PathComparison()))
                throw new ArgumentException("A filesystem root cannot be used.", nameof(root));
        }

        public GroupWorldReadResult Read(string worldScope)
        {
            try
            {
                Paths paths = Resolve(worldScope);
                if (!Directory.Exists(_root)) return Missing();
                using (Acquire(paths.Lock)) return ReadLocked(paths, worldScope);
            }
            catch (ArgumentException) { return Unavailable("group-world-scope-invalid"); }
            catch (Exception exception) when (StorageFailure(exception))
            {
                return Unavailable("group-store-read-unavailable");
            }
        }

        public GroupWorldCommitResult TryCommit(
            string worldScope,
            long expectedCatalogRevision,
            GroupCatalog replacement)
        {
            if (expectedCatalogRevision < 0L || replacement == null ||
                replacement.Revision != expectedCatalogRevision + 1L)
                return Commit(
                    GroupWorldCommitState.InvalidReplacement,
                    "group-store-replacement-invalid",
                    null);
            try
            {
                Paths paths = Resolve(worldScope);
                Directory.CreateDirectory(_root);
                using (Acquire(paths.Lock))
                {
                    GroupWorldReadResult current = ReadLocked(paths, worldScope);
                    if (current.State == GroupWorldReadState.Corrupt)
                        return Commit(GroupWorldCommitState.Corrupt, current.ReasonCode, current);
                    if (current.State == GroupWorldReadState.EvidenceConflict)
                        return Commit(GroupWorldCommitState.EvidenceConflict, current.ReasonCode, current);
                    if (current.State == GroupWorldReadState.Unavailable)
                        return Commit(GroupWorldCommitState.Unavailable, current.ReasonCode, current);
                    long revision = current.State == GroupWorldReadState.Missing
                        ? 0L
                        : current.Catalog.Revision;
                    if (revision != expectedCatalogRevision)
                        return Commit(
                            GroupWorldCommitState.RevisionConflict,
                            "group-store-revision-conflict",
                            current);
                    GroupCommandLedger ledger = current.State == GroupWorldReadState.Missing
                        ? GroupCommandLedger.Empty
                        : current.Catalog.CommandLedger;
                    if (!SameLedger(ledger, replacement.CommandLedger))
                        return Commit(
                            GroupWorldCommitState.InvalidReplacement,
                            "group-store-command-ledger-mismatch",
                            current);
                    return Publish(paths, worldScope, replacement, current);
                }
            }
            catch (ArgumentException)
            {
                return Commit(
                    GroupWorldCommitState.InvalidReplacement,
                    "group-store-replacement-invalid",
                    null);
            }
            catch (Exception exception) when (StorageFailure(exception))
            {
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-commit-unavailable",
                    null);
            }
        }

        private GroupWorldCommitResult Publish(
            Paths paths,
            string worldScope,
            GroupCatalog replacement,
            GroupWorldReadResult current)
        {
            if (File.Exists(paths.Temporary) || File.Exists(paths.Backup))
                return Commit(
                    GroupWorldCommitState.EvidenceConflict,
                    "group-store-nonprimary-evidence",
                    current);
            byte[] exact = GroupCatalogCodec.Encode(worldScope, replacement);
            using (var stream = new FileStream(
                       paths.Temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(exact, 0, exact.Length);
                stream.Flush(true);
            }
            byte[] staged = ReadBounded(paths.Temporary);
            if (!Exact(exact, staged) || !GroupCatalogCodec.TryDecode(
                    staged, worldScope, out GroupCatalog decoded, out _) ||
                decoded.Revision != replacement.Revision)
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-staged-readback-failed",
                    current);
            if (File.Exists(paths.Primary)) File.Replace(paths.Temporary, paths.Primary, null, true);
            else File.Move(paths.Temporary, paths.Primary);
            GroupWorldReadResult committed = ReadPrimary(paths.Primary, worldScope);
            return committed.State == GroupWorldReadState.Ready &&
                   committed.Catalog.Revision == replacement.Revision
                ? Commit(GroupWorldCommitState.Committed, "group-store-committed", committed)
                : Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-commit-readback-failed",
                    committed);
        }

        private GroupWorldReadResult ReadLocked(Paths paths, string worldScope)
        {
            if (File.Exists(paths.Temporary) || File.Exists(paths.Backup))
                return new GroupWorldReadResult(
                    GroupWorldReadState.EvidenceConflict,
                    "group-store-nonprimary-evidence",
                    null,
                    string.Empty);
            return File.Exists(paths.Primary)
                ? ReadPrimary(paths.Primary, worldScope)
                : Missing();
        }

        private static GroupWorldReadResult ReadPrimary(string path, string worldScope)
        {
            byte[] exact = ReadBounded(path);
            if (!GroupCatalogCodec.TryDecode(
                    exact, worldScope, out GroupCatalog catalog, out string reason))
                return new GroupWorldReadResult(
                    GroupWorldReadState.Corrupt,
                    reason,
                    null,
                    GroupCatalogCodec.ComputeSha256(exact));
            return new GroupWorldReadResult(
                GroupWorldReadState.Ready,
                "group-store-ready",
                catalog,
                GroupCatalogCodec.ComputeSha256(exact));
        }

        private Paths Resolve(string worldScope)
        {
            string scope = GroupIdentity.RequireWorldScope(worldScope);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(scope));
            var key = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++) key.Append(digest[index].ToString("x2"));
            string primary = Path.Combine(_root, key + ".groups");
            if (!string.Equals(Path.GetDirectoryName(primary), _root, PathComparison()))
                throw new ArgumentException("The group path escaped its root.", nameof(worldScope));
            return new Paths(primary, primary + ".tmp", primary + ".bak", primary + ".lock");
        }

        private static FileStream Acquire(string path) => new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.WriteThrough);

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.None,
                       4096,
                       FileOptions.SequentialScan))
            {
                if (stream.Length < 1L || stream.Length > GroupLimits.MaximumCatalogBytes)
                    throw new InvalidDataException("The group catalog length is invalid.");
                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new EndOfStreamException();
                    offset += read;
                }
                return bytes;
            }
        }

        private static bool SameLedger(GroupCommandLedger left, GroupCommandLedger right) =>
            left != null && right != null && left.Epoch == right.Epoch &&
            left.NextSequence == right.NextSequence &&
            left.MinimumAcceptedSequence == right.MinimumAcceptedSequence &&
            left.Issues.Count == right.Issues.Count && left.Receipts.Count == right.Receipts.Count;

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool StorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException ||
            exception is NotSupportedException || exception is System.Security.SecurityException;

        private static StringComparison PathComparison() =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static GroupWorldReadResult Missing() => new GroupWorldReadResult(
            GroupWorldReadState.Missing,
            "group-store-missing",
            GroupCatalog.Empty,
            string.Empty);

        private static GroupWorldReadResult Unavailable(string reason) => new GroupWorldReadResult(
            GroupWorldReadState.Unavailable,
            reason,
            null,
            string.Empty);

        private static GroupWorldCommitResult Commit(
            GroupWorldCommitState state,
            string reason,
            GroupWorldReadResult current) => new GroupWorldCommitResult(state, reason, current);

        private readonly struct Paths
        {
            internal Paths(string primary, string temporary, string backup, string @lock)
            {
                Primary = primary;
                Temporary = temporary;
                Backup = backup;
                Lock = @lock;
            }

            internal string Primary { get; }
            internal string Temporary { get; }
            internal string Backup { get; }
            internal string Lock { get; }
        }
    }
}
