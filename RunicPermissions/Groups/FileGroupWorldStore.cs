using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    internal enum GroupWorldStoreStage
    {
        DirectoryCreated = 1,
        LockAcquired = 2,
        TemporaryFlushed = 3,
        AtomicCommitCompleted = 4
    }

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

    /// <summary>
    /// Primary-only catalog storage. A matching temporary or backup file is conflicting evidence,
    /// never an automatic recovery source. Publication uses a same-directory rename/replace after
    /// a WriteThrough + Flush(true) temporary-file write and exact canonical readback.
    /// </summary>
    public sealed class FileGroupWorldStore : IGroupWorldStore
    {
        private readonly string _root;
        private readonly Action<GroupWorldStoreStage> _testHook;

        public FileGroupWorldStore(string root) : this(root, null)
        {
        }

        internal FileGroupWorldStore(string root, Action<GroupWorldStoreStage> testHook)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A storage root is required.", nameof(root));
            _root = TrimTrailingSeparators(Path.GetFullPath(root));
            if (string.Equals(_root, Path.GetPathRoot(_root), PathComparison()))
                throw new ArgumentException("A filesystem root cannot be the group storage root.", nameof(root));
            _testHook = testHook;
        }

        public GroupWorldReadResult Read(string worldScope)
        {
            try
            {
                Paths paths = Resolve(worldScope);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                    return Unavailable("group-store-path-unsafe");
                if (!Directory.Exists(_root)) return Missing();
                using (Acquire(paths.Lock))
                {
                    _testHook?.Invoke(GroupWorldStoreStage.LockAcquired);
                    if (!PathsAreFreeOfReparsePoints(
                            _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                        return Unavailable("group-store-path-changed");
                    return ReadLocked(paths, worldScope);
                }
            }
            catch (ArgumentException) { return Unavailable("group-world-scope-invalid"); }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                return Unavailable("group-store-read-unavailable");
            }
        }

        public GroupWorldCommitResult TryCommit(
            string worldScope,
            long expectedCatalogRevision,
            GroupCatalog replacement)
        {
            if (expectedCatalogRevision < 0 || replacement == null ||
                replacement.Revision != expectedCatalogRevision + 1)
                return Commit(
                    GroupWorldCommitState.InvalidReplacement,
                    "group-store-replacement-invalid",
                    null);
            try
            {
                Paths paths = Resolve(worldScope);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                    return Commit(
                        GroupWorldCommitState.Unavailable,
                        "group-store-path-unsafe",
                        null);
                Directory.CreateDirectory(_root);
                _testHook?.Invoke(GroupWorldStoreStage.DirectoryCreated);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                    return Commit(
                        GroupWorldCommitState.Unavailable,
                        "group-store-path-changed",
                        null);
                using (Acquire(paths.Lock))
                {
                    _testHook?.Invoke(GroupWorldStoreStage.LockAcquired);
                    if (!PathsAreFreeOfReparsePoints(
                            _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                        return Commit(
                            GroupWorldCommitState.Unavailable,
                            "group-store-path-changed",
                            null);
                    GroupWorldReadResult current = ReadLocked(paths, worldScope);
                    if (current.State == GroupWorldReadState.EvidenceConflict)
                        return Commit(GroupWorldCommitState.EvidenceConflict, current.ReasonCode, current);
                    if (current.State == GroupWorldReadState.Corrupt)
                        return Commit(GroupWorldCommitState.Corrupt, current.ReasonCode, current);
                    if (current.State == GroupWorldReadState.Unavailable)
                        return Commit(GroupWorldCommitState.Unavailable, current.ReasonCode, current);
                    long observedRevision = current.State == GroupWorldReadState.Missing
                        ? 0
                        : current.Catalog.Revision;
                    if (observedRevision != expectedCatalogRevision)
                        return Commit(
                            GroupWorldCommitState.RevisionConflict,
                            "group-store-revision-conflict",
                            current);
                    GroupCommandLedger observedLedger = current.State == GroupWorldReadState.Missing
                        ? GroupCommandLedger.Empty
                        : current.Catalog.CommandLedger;
                    if (!SameLedger(observedLedger, replacement.CommandLedger))
                        return Commit(
                            GroupWorldCommitState.InvalidReplacement,
                            "group-store-command-ledger-mismatch",
                            current);

                    return PublishLocked(paths, worldScope, current, replacement);
                }
            }
            catch (ArgumentException)
            {
                return Commit(
                    GroupWorldCommitState.InvalidReplacement,
                    "group-store-replacement-invalid",
                    null);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-commit-unavailable",
                    null);
            }
        }

        internal bool TryIssueCommand(
            string worldScope,
            StableIdentity actor,
            GroupCommand command,
            long nowUtcTicks,
            out GroupCommandIssue issue,
            out string reasonCode)
        {
            issue = null;
            reasonCode = "group-command-issue-failed";
            if (actor == null || command == null || nowUtcTicks <= 0) return false;
            try
            {
                Paths paths = Resolve(worldScope);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                {
                    reasonCode = "group-store-path-unsafe";
                    return false;
                }
                Directory.CreateDirectory(_root);
                _testHook?.Invoke(GroupWorldStoreStage.DirectoryCreated);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                {
                    reasonCode = "group-store-path-changed";
                    return false;
                }
                using (Acquire(paths.Lock))
                {
                    _testHook?.Invoke(GroupWorldStoreStage.LockAcquired);
                    GroupWorldReadResult current = ReadLocked(paths, worldScope);
                    if (current.State != GroupWorldReadState.Missing &&
                        current.State != GroupWorldReadState.Ready)
                    {
                        reasonCode = current.ReasonCode;
                        return false;
                    }
                    GroupCatalog catalog = current.State == GroupWorldReadState.Missing
                        ? GroupCatalog.Empty
                        : current.Catalog;
                    long groupRevision = catalog.TryGetGroup(
                        command.GroupId, out GroupRecord group) ? group.Revision : -1L;
                    if (command.Kind == GroupCommandKind.Create && groupRevision >= 0)
                    {
                        reasonCode = "group-id-exists";
                        return false;
                    }
                    if (command.Kind != GroupCommandKind.Create && groupRevision < 1)
                    {
                        reasonCode = "group-missing";
                        return false;
                    }
                    GroupCommandLedger ledger = catalog.CommandLedger.Issue(
                        actor,
                        GroupCommandDurableCodec.ComputeRequestSha256(command),
                        command.GroupId,
                        catalog.Revision,
                        groupRevision,
                        nowUtcTicks,
                        checked(nowUtcTicks +
                                GroupCommandDurabilityLimits.MaximumIssueLifetime.Ticks),
                        out issue);
                    if (ReferenceEquals(ledger, catalog.CommandLedger))
                    {
                        reasonCode = "group-command-issue-replayed";
                        return true;
                    }
                    GroupWorldCommitResult published = PublishLocked(
                        paths, worldScope, current, catalog.WithCommandLedger(ledger));
                    if (!published.Success)
                    {
                        issue = null;
                        reasonCode = published.ReasonCode;
                        return false;
                    }
                    reasonCode = "group-command-issued";
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is UnauthorizedAccessException || exception is InvalidOperationException ||
                exception is OverflowException)
            {
                issue = null;
                if (exception is InvalidOperationException &&
                    string.Equals(exception.Message, "group-command-issue-capacity", StringComparison.Ordinal))
                    reasonCode = "group-command-issue-capacity";
                return false;
            }
        }

        internal bool TryExecuteIssuedCommand(
            string worldScope,
            StableIdentity actor,
            GroupCommandExecution execution,
            long nowUtcTicks,
            out GroupCommandReceipt receipt,
            out string reasonCode)
        {
            receipt = null;
            reasonCode = "group-command-execution-failed";
            if (actor == null || execution == null || nowUtcTicks <= 0) return false;
            string requestHash = GroupCommandDurableCodec.ComputeRequestSha256(execution.Command);
            try
            {
                Paths paths = Resolve(worldScope);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock) ||
                    !Directory.Exists(_root))
                {
                    reasonCode = "group-store-unavailable";
                    return false;
                }
                using (Acquire(paths.Lock))
                {
                    _testHook?.Invoke(GroupWorldStoreStage.LockAcquired);
                    GroupWorldReadResult current = ReadLocked(paths, worldScope);
                    if (current.State != GroupWorldReadState.Ready || current.Catalog == null)
                    {
                        reasonCode = current.ReasonCode;
                        return false;
                    }
                    GroupCatalog catalog = current.Catalog;
                    GroupCommandLedger ledger = catalog.CommandLedger.PruneExpired(nowUtcTicks);
                    if (ledger.TryGetReceipt(execution.Token, out GroupCommandReceipt replay))
                    {
                        if (!replay.Matches(actor, requestHash) ||
                            replay.ExpectedCatalogRevision != execution.ExpectedCatalogRevision ||
                            replay.ExpectedGroupRevision != execution.ExpectedGroupRevision)
                        {
                            reasonCode = "group-command-token-conflict";
                            return false;
                        }
                        if (!ReferenceEquals(ledger, catalog.CommandLedger))
                        {
                            GroupWorldCommitResult pruned = PublishLocked(
                                paths, worldScope, current, catalog.WithCommandLedger(ledger));
                            if (!pruned.Success) { reasonCode = pruned.ReasonCode; return false; }
                        }
                        receipt = replay;
                        reasonCode = "group-command-replayed";
                        return true;
                    }
                    if (!ledger.TryGetIssue(execution.Token, out GroupCommandIssue issue))
                    {
                        reasonCode = "group-command-token-expired-or-unknown";
                        return false;
                    }
                    if (!issue.Matches(actor, requestHash) || issue.GroupId != execution.Command.GroupId ||
                        issue.ExpectedCatalogRevision != execution.ExpectedCatalogRevision ||
                        issue.ExpectedGroupRevision != execution.ExpectedGroupRevision)
                    {
                        reasonCode = "group-command-token-conflict";
                        return false;
                    }

                    GroupCommandExecutionResult evaluated = GroupCommandProcessor.EvaluateIssued(
                        catalog,
                        actor,
                        execution.Command,
                        issue.ExpectedCatalogRevision,
                        issue.ExpectedGroupRevision,
                        nowUtcTicks);
                    GroupCatalog resultCatalog = evaluated.Catalog ?? catalog;
                    long resultGroupRevision = resultCatalog.TryGetGroup(
                        execution.Command.GroupId, out GroupRecord resultGroup)
                        ? resultGroup.Revision
                        : -1L;
                    GroupCommandLedger completed = ledger.Complete(
                        issue,
                        evaluated.Code,
                        evaluated.ReasonCode,
                        resultCatalog.Revision,
                        resultGroupRevision,
                        out receipt);
                    GroupCatalog replacement = resultCatalog.WithCommandLedger(completed);
                    GroupWorldCommitResult published = PublishLocked(
                        paths, worldScope, current, replacement);
                    if (!published.Success)
                    {
                        receipt = null;
                        reasonCode = published.ReasonCode;
                        return false;
                    }
                    reasonCode = evaluated.ReasonCode;
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is UnauthorizedAccessException || exception is InvalidOperationException ||
                exception is OverflowException)
            {
                receipt = null;
                if (exception is InvalidOperationException &&
                    string.Equals(exception.Message, "group-command-receipt-capacity", StringComparison.Ordinal))
                    reasonCode = "group-command-receipt-capacity";
                return false;
            }
        }

        private GroupWorldCommitResult PublishLocked(
            Paths paths,
            string worldScope,
            GroupWorldReadResult current,
            GroupCatalog replacement)
        {
            byte[] exact = GroupCatalogCodec.Encode(worldScope, replacement);
            if (File.Exists(paths.Temporary) || File.Exists(paths.Backup))
                return Commit(
                    GroupWorldCommitState.EvidenceConflict,
                    "group-store-nonprimary-evidence",
                    current);
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
            _testHook?.Invoke(GroupWorldStoreStage.TemporaryFlushed);
            if (!PathsAreFreeOfReparsePoints(
                    _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock) ||
                File.Exists(paths.Backup))
                return Commit(
                    GroupWorldCommitState.EvidenceConflict,
                    "group-store-path-or-evidence-changed",
                    current);
            byte[] staged = ReadBounded(paths.Temporary);
            if (!ExactEquals(exact, staged) ||
                !GroupCatalogCodec.TryDecode(
                    staged, worldScope, out GroupCatalog stagedCatalog, out _) ||
                stagedCatalog.Revision != replacement.Revision)
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-staged-readback-failed",
                    current);

            GroupWorldReadResult barrier = File.Exists(paths.Primary)
                ? ReadPrimary(paths, worldScope, true)
                : Missing();
            if (!SameCurrent(current, barrier))
                return Commit(
                    GroupWorldCommitState.EvidenceConflict,
                    "group-store-primary-raced",
                    barrier);

            if (File.Exists(paths.Primary))
                File.Replace(paths.Temporary, paths.Primary, null, true);
            else
                File.Move(paths.Temporary, paths.Primary);

            _testHook?.Invoke(GroupWorldStoreStage.AtomicCommitCompleted);
            if (!PathsAreFreeOfReparsePoints(
                    _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock) ||
                File.Exists(paths.Temporary) || File.Exists(paths.Backup))
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-path-changed-after-commit",
                    current);
            GroupWorldReadResult committed = ReadPrimary(paths, worldScope);
            if (committed.State != GroupWorldReadState.Ready ||
                committed.Catalog.Revision != replacement.Revision ||
                !string.Equals(
                    committed.ExactSha256,
                    GroupCatalogCodec.ComputeSha256(exact),
                    StringComparison.Ordinal))
                return Commit(
                    GroupWorldCommitState.Unavailable,
                    "group-store-commit-readback-failed",
                    committed);
            return Commit(
                GroupWorldCommitState.Committed,
                "group-store-committed",
                committed);
        }

        internal string GetPrimaryPath(string worldScope) => Resolve(worldScope).Primary;
        internal string GetTemporaryPath(string worldScope) => Resolve(worldScope).Temporary;
        internal string GetBackupPath(string worldScope) => Resolve(worldScope).Backup;

        private GroupWorldReadResult ReadLocked(Paths paths, string worldScope)
        {
            if (!PathsAreFreeOfReparsePoints(
                    _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                return Unavailable("group-store-path-unsafe");
            if (File.Exists(paths.Temporary) || File.Exists(paths.Backup))
                return new GroupWorldReadResult(
                    GroupWorldReadState.EvidenceConflict,
                    "group-store-nonprimary-evidence",
                    null,
                    string.Empty);
            if (!File.Exists(paths.Primary)) return Missing();
            return ReadPrimary(paths, worldScope);
        }

        private GroupWorldReadResult ReadPrimary(
            Paths paths,
            string worldScope,
            bool allowKnownTemporary = false)
        {
            try
            {
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock))
                    return Unavailable("group-store-path-unsafe");
                byte[] exact = ReadBounded(paths.Primary);
                if (!PathsAreFreeOfReparsePoints(
                        _root, paths.Primary, paths.Temporary, paths.Backup, paths.Lock) ||
                    !allowKnownTemporary && File.Exists(paths.Temporary) ||
                    File.Exists(paths.Backup))
                    return Unavailable("group-store-path-changed");
                if (!GroupCatalogCodec.TryDecode(exact, worldScope, out GroupCatalog catalog, out string reason))
                    return new GroupWorldReadResult(
                        GroupWorldReadState.Corrupt, reason, null, GroupCatalogCodec.ComputeSha256(exact));
                return new GroupWorldReadResult(
                    GroupWorldReadState.Ready,
                    "group-store-ready",
                    catalog,
                    GroupCatalogCodec.ComputeSha256(exact));
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                return Unavailable("group-store-primary-unavailable");
            }
        }

        private FileStream Acquire(string path) => new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.WriteThrough);

        private Paths Resolve(string worldScope)
        {
            worldScope = GroupIdentity.RequireWorldScope(worldScope);
            string key;
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(worldScope));
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                key = text.ToString();
            }
            string primary = Path.Combine(_root, key + ".groups");
            if (!IsExactChild(primary, _root))
                throw new ArgumentException("The group store path escaped its trusted root.", nameof(worldScope));
            return new Paths(
                primary,
                primary + ".tmp",
                primary + ".bak",
                primary + ".lock");
        }

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                if (stream.Length < 1 || stream.Length > GroupLimits.MaximumCatalogBytes)
                    throw new InvalidDataException("The group catalog length is invalid.");
                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new EndOfStreamException();
                    offset += read;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException();
                return bytes;
            }
        }

        private static bool ExactEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool SameCurrent(GroupWorldReadResult left, GroupWorldReadResult right)
        {
            if (left == null || right == null || left.State != right.State) return false;
            if (left.State == GroupWorldReadState.Missing) return true;
            return left.State == GroupWorldReadState.Ready &&
                   left.Catalog != null && right.Catalog != null &&
                   left.Catalog.Revision == right.Catalog.Revision &&
                   string.Equals(left.ExactSha256, right.ExactSha256, StringComparison.Ordinal);
        }

        private static bool SameLedger(GroupCommandLedger left, GroupCommandLedger right)
        {
            if (left == null || right == null || left.Epoch != right.Epoch ||
                left.NextSequence != right.NextSequence ||
                left.MinimumAcceptedSequence != right.MinimumAcceptedSequence ||
                left.Issues.Count != right.Issues.Count ||
                left.Receipts.Count != right.Receipts.Count) return false;
            for (int index = 0; index < left.Issues.Count; index++)
            {
                GroupCommandIssue first = left.Issues[index];
                GroupCommandIssue second = right.Issues[index];
                if (first.Sequence != second.Sequence || !first.Actor.Equals(second.Actor) ||
                    !string.Equals(first.RequestSha256, second.RequestSha256, StringComparison.Ordinal) ||
                    first.GroupId != second.GroupId ||
                    first.ExpectedCatalogRevision != second.ExpectedCatalogRevision ||
                    first.ExpectedGroupRevision != second.ExpectedGroupRevision ||
                    first.ExpiresUtcTicks != second.ExpiresUtcTicks) return false;
            }
            for (int index = 0; index < left.Receipts.Count; index++)
            {
                GroupCommandReceipt first = left.Receipts[index];
                GroupCommandReceipt second = right.Receipts[index];
                if (first.Sequence != second.Sequence || !first.Actor.Equals(second.Actor) ||
                    !string.Equals(first.RequestSha256, second.RequestSha256, StringComparison.Ordinal) ||
                    first.Code != second.Code ||
                    !string.Equals(first.ReasonCode, second.ReasonCode, StringComparison.Ordinal) ||
                    first.ExpectedCatalogRevision != second.ExpectedCatalogRevision ||
                    first.ExpectedGroupRevision != second.ExpectedGroupRevision ||
                    first.CatalogRevision != second.CatalogRevision ||
                    first.GroupRevision != second.GroupRevision) return false;
            }
            return true;
        }

        private static bool IsExactChild(string fullPath, string fullRoot)
        {
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string fileName = Path.GetFileName(fullPath);
            return Path.IsPathRooted(fullPath) && Path.IsPathRooted(fullRoot) &&
                   fullRoot.Length != 0 && fileName.Length != 0 &&
                   fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   string.Equals(directory, fullRoot, PathComparison());
        }

        private static bool PathsAreFreeOfReparsePoints(params string[] paths)
        {
            if (paths == null || paths.Length == 0) return false;
            for (int index = 0; index < paths.Length; index++)
                if (string.IsNullOrEmpty(paths[index]) || HasReparseTraversal(paths[index]))
                    return false;
            return true;
        }

        private static bool HasReparseTraversal(string fullPath)
        {
            try
            {
                string path = Path.GetFullPath(fullPath);
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || HasReparseAttribute(root)) return true;
                string current = root;
                string remainder = path.Substring(root.Length);
                string[] components = remainder.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int index = 0; index < components.Length; index++)
                {
                    current = Path.Combine(current, components[index]);
                    try
                    {
                        if (HasReparseAttribute(current)) return true;
                    }
                    catch (FileNotFoundException) { break; }
                    catch (DirectoryNotFoundException) { break; }
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool HasReparseAttribute(string path) =>
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        private static string TrimTrailingSeparators(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            int length = path.Length;
            while (length > root.Length &&
                   (path[length - 1] == Path.DirectorySeparatorChar ||
                    path[length - 1] == Path.AltDirectorySeparatorChar))
                length--;
            return length == path.Length ? path : path.Substring(0, length);
        }

        private static StringComparison PathComparison() => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException ||
            exception is NotSupportedException || exception is System.Security.SecurityException;

        private static GroupWorldReadResult Missing() => new GroupWorldReadResult(
            GroupWorldReadState.Missing, "group-store-missing", GroupCatalog.Empty, string.Empty);

        private static GroupWorldReadResult Unavailable(string reason) => new GroupWorldReadResult(
            GroupWorldReadState.Unavailable, reason, null, string.Empty);

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
