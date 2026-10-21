using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RunicPermissions.Contracts;

namespace RunicPermissions.Groups
{
    public enum ActiveGroupSelectionStatus : byte
    {
        Available = 1,
        NoneSelected = 2,
        Stale = 3,
        Ambiguous = 4,
        GroupMissing = 5,
        NotMember = 6
    }

    public sealed class ActiveGroupSelection
    {
        internal ActiveGroupSelection(
            ActiveGroupSelectionStatus status,
            string groupId = "",
            string displayName = "")
        {
            if (!Enum.IsDefined(typeof(ActiveGroupSelectionStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (status == ActiveGroupSelectionStatus.Available &&
                !GroupIdentity.IsCanonicalId(groupId))
                throw new ArgumentException(
                    "An available active Group requires an exact Group UUID.", nameof(groupId));
            if (status != ActiveGroupSelectionStatus.Available &&
                !string.IsNullOrEmpty(groupId))
                throw new ArgumentException(
                    "An unavailable active Group cannot expose an ID.", nameof(groupId));
            Status = status;
            GroupId = groupId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public ActiveGroupSelectionStatus Status { get; }
        public string GroupId { get; }
        public string DisplayName { get; }
        public bool IsAvailable => Status == ActiveGroupSelectionStatus.Available;

        internal static ActiveGroupSelection None { get; } =
            new ActiveGroupSelection(ActiveGroupSelectionStatus.NoneSelected);
        internal static ActiveGroupSelection Stale { get; } =
            new ActiveGroupSelection(ActiveGroupSelectionStatus.Stale);
        internal static ActiveGroupSelection Ambiguous { get; } =
            new ActiveGroupSelection(ActiveGroupSelectionStatus.Ambiguous);
    }

    /// <summary>
    /// Resolves the exact currently selected Group. A client answer is a convenience cache only;
    /// an authoritative consumer must resolve and validate the selection again on the server.
    /// </summary>
    public interface IActiveGroupSelectionService
    {
        ActiveGroupSelection Resolve(StableIdentity identity);
    }

    public enum GroupActiveSelectionReadState
    {
        Missing = 0,
        Ready = 1,
        Corrupt = 2,
        Ambiguous = 3,
        Unavailable = 4
    }

    public sealed class GroupActiveSelectionReadResult
    {
        internal GroupActiveSelectionReadResult(
            GroupActiveSelectionReadState state,
            string reasonCode,
            Guid groupId)
        {
            State = state;
            ReasonCode = reasonCode ?? string.Empty;
            GroupId = groupId;
        }

        public GroupActiveSelectionReadState State { get; }
        public string ReasonCode { get; }
        public Guid GroupId { get; }
        public bool HasSelection => State == GroupActiveSelectionReadState.Ready &&
                                    GroupId != Guid.Empty;
    }

    public interface IGroupActiveSelectionStore
    {
        GroupActiveSelectionReadResult Read(string worldScope, StableIdentity identity);
        bool TrySet(
            string worldScope,
            StableIdentity identity,
            Guid groupId,
            out string reasonCode);
    }

    /// <summary>
    /// Server-owned per-world preference storage. The preference never grants membership: every
    /// read is joined against the current primary Group catalog before it is returned as available.
    /// </summary>
    public sealed class FileGroupActiveSelectionStore : IGroupActiveSelectionStore
    {
        private const uint Magic = 0x31414752; // "RGA1"
        private const ushort Schema = 1;
        private const int MaximumFileBytes = 2048;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly string _root;

        public FileGroupActiveSelectionStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A storage root is required.", nameof(root));
            _root = TrimTrailingSeparators(Path.GetFullPath(root));
            if (string.Equals(_root, Path.GetPathRoot(_root), PathComparison()))
                throw new ArgumentException(
                    "A filesystem root cannot be the active Group storage root.", nameof(root));
        }

        public GroupActiveSelectionReadResult Read(string worldScope, StableIdentity identity)
        {
            if (identity == null) return Unavailable("group-active-identity-missing");
            try
            {
                Paths paths = Resolve(worldScope, identity);
                if (!Directory.Exists(_root)) return Missing();
                using (Acquire(paths.Lock))
                {
                    if (File.Exists(paths.Temporary))
                        return Ambiguous("group-active-temporary-evidence");
                    if (!File.Exists(paths.Primary)) return Missing();
                    byte[] exact = ReadBounded(paths.Primary);
                    return TryDecode(exact, worldScope, identity, out Guid groupId)
                        ? new GroupActiveSelectionReadResult(
                            GroupActiveSelectionReadState.Ready,
                            "group-active-ready",
                            groupId)
                        : Corrupt("group-active-corrupt");
                }
            }
            catch (ArgumentException) { return Unavailable("group-active-request-invalid"); }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                return Unavailable("group-active-read-unavailable");
            }
        }

        public bool TrySet(
            string worldScope,
            StableIdentity identity,
            Guid groupId,
            out string reasonCode)
        {
            reasonCode = "group-active-write-unavailable";
            if (identity == null)
            {
                reasonCode = "group-active-identity-missing";
                return false;
            }
            try
            {
                Paths paths = Resolve(worldScope, identity);
                Directory.CreateDirectory(_root);
                using (Acquire(paths.Lock))
                {
                    // Selecting a Group is an explicit repair action for an interrupted or corrupt
                    // preference. It cannot grant access because membership is checked separately.
                    byte[] exact = Encode(worldScope, identity, groupId);
                    using (var stream = new FileStream(
                               paths.Temporary,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None,
                               4096,
                               FileOptions.WriteThrough))
                    {
                        stream.Write(exact, 0, exact.Length);
                        stream.Flush(true);
                    }
                    byte[] staged = ReadBounded(paths.Temporary);
                    if (!ExactEquals(exact, staged) ||
                        !TryDecode(staged, worldScope, identity, out Guid stagedId) ||
                        stagedId != groupId)
                    {
                        reasonCode = "group-active-staged-readback-failed";
                        return false;
                    }
                    if (File.Exists(paths.Primary))
                        File.Replace(paths.Temporary, paths.Primary, null, true);
                    else
                        File.Move(paths.Temporary, paths.Primary);
                    byte[] committed = ReadBounded(paths.Primary);
                    if (!TryDecode(committed, worldScope, identity, out Guid committedId) ||
                        committedId != groupId || File.Exists(paths.Temporary))
                    {
                        reasonCode = "group-active-commit-readback-failed";
                        return false;
                    }
                    reasonCode = groupId == Guid.Empty
                        ? "group-active-cleared"
                        : "group-active-selected";
                    return true;
                }
            }
            catch (ArgumentException)
            {
                reasonCode = "group-active-request-invalid";
                return false;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                reasonCode = "group-active-write-unavailable";
                return false;
            }
        }

        internal string GetPrimaryPath(string worldScope, StableIdentity identity) =>
            Resolve(worldScope, identity).Primary;

        private Paths Resolve(string worldScope, StableIdentity identity)
        {
            worldScope = GroupIdentity.RequireWorldScope(worldScope);
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            byte[] material = StrictUtf8.GetBytes(worldScope + "\n" + identity.CanonicalKey);
            string key;
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(material);
                var text = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    text.Append(digest[index].ToString("x2"));
                key = text.ToString();
            }
            string primary = Path.Combine(_root, key + ".active");
            if (!IsExactChild(primary, _root))
                throw new ArgumentException("The active Group path escaped its trusted root.");
            return new Paths(primary, primary + ".tmp", primary + ".lock");
        }

        private static byte[] Encode(
            string worldScope,
            StableIdentity identity,
            Guid groupId)
        {
            worldScope = GroupIdentity.RequireWorldScope(worldScope);
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            byte[] body;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write(Schema);
                WriteText(writer, worldScope, GroupLimits.MaximumWorldScopeUtf8Bytes);
                WriteText(writer, identity.Authority, StableIdentity.MaximumAuthorityLength);
                WriteText(writer, identity.SubjectId, StableIdentity.MaximumSubjectIdLength * 4);
                writer.Write(groupId.ToByteArray());
                writer.Flush();
                body = stream.ToArray();
            }
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create()) digest = algorithm.ComputeHash(body);
            var exact = new byte[body.Length + digest.Length];
            Buffer.BlockCopy(body, 0, exact, 0, body.Length);
            Buffer.BlockCopy(digest, 0, exact, body.Length, digest.Length);
            if (exact.Length > MaximumFileBytes) throw new InvalidDataException();
            return exact;
        }

        private static bool TryDecode(
            byte[] exact,
            string expectedWorld,
            StableIdentity expectedIdentity,
            out Guid groupId)
        {
            groupId = Guid.Empty;
            if (exact == null || exact.Length < 64 || exact.Length > MaximumFileBytes ||
                expectedIdentity == null) return false;
            int bodyLength = exact.Length - 32;
            var body = new byte[bodyLength];
            var supplied = new byte[32];
            Buffer.BlockCopy(exact, 0, body, 0, bodyLength);
            Buffer.BlockCopy(exact, bodyLength, supplied, 0, supplied.Length);
            byte[] computed;
            using (SHA256 algorithm = SHA256.Create()) computed = algorithm.ComputeHash(body);
            if (!ExactEquals(supplied, computed)) return false;
            try
            {
                using (var stream = new MemoryStream(body, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, true))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Schema) return false;
                    string world = ReadText(reader, GroupLimits.MaximumWorldScopeUtf8Bytes);
                    string authority = ReadText(reader, StableIdentity.MaximumAuthorityLength);
                    string subject = ReadText(reader, StableIdentity.MaximumSubjectIdLength * 4);
                    byte[] id = reader.ReadBytes(16);
                    if (id.Length != 16 || stream.Position != stream.Length) return false;
                    var identity = new StableIdentity(authority, subject);
                    if (!string.Equals(world, GroupIdentity.RequireWorldScope(expectedWorld),
                            StringComparison.Ordinal) || !identity.Equals(expectedIdentity)) return false;
                    groupId = new Guid(id);
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is IOException ||
                exception is DecoderFallbackException)
            {
                return false;
            }
        }

        private static void WriteText(BinaryWriter writer, string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length < 1 || bytes.Length > maximumBytes || bytes.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadUInt16();
            if (length < 1 || length > maximumBytes ||
                length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length);
            string value = StrictUtf8.GetString(bytes);
            if (!ExactEquals(bytes, StrictUtf8.GetBytes(value))) throw new InvalidDataException();
            return value;
        }

        private static FileStream Acquire(string path) => new FileStream(
            path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.WriteThrough);

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.None, 4096,
                       FileOptions.SequentialScan))
            {
                if (stream.Length < 1 || stream.Length > MaximumFileBytes)
                    throw new InvalidDataException();
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

        private static bool ExactEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool IsExactChild(string fullPath, string fullRoot) =>
            Path.IsPathRooted(fullPath) && Path.IsPathRooted(fullRoot) &&
            string.Equals(Path.GetDirectoryName(fullPath), fullRoot, PathComparison()) &&
            !string.IsNullOrEmpty(Path.GetFileName(fullPath));

        private static string TrimTrailingSeparators(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            int length = path.Length;
            while (length > root.Length &&
                   (path[length - 1] == Path.DirectorySeparatorChar ||
                    path[length - 1] == Path.AltDirectorySeparatorChar)) length--;
            return length == path.Length ? path : path.Substring(0, length);
        }

        private static StringComparison PathComparison() => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException ||
            exception is NotSupportedException || exception is System.Security.SecurityException;

        private static GroupActiveSelectionReadResult Missing() =>
            new GroupActiveSelectionReadResult(
                GroupActiveSelectionReadState.Missing, "group-active-missing", Guid.Empty);
        private static GroupActiveSelectionReadResult Corrupt(string reason) =>
            new GroupActiveSelectionReadResult(
                GroupActiveSelectionReadState.Corrupt, reason, Guid.Empty);
        private static GroupActiveSelectionReadResult Ambiguous(string reason) =>
            new GroupActiveSelectionReadResult(
                GroupActiveSelectionReadState.Ambiguous, reason, Guid.Empty);
        private static GroupActiveSelectionReadResult Unavailable(string reason) =>
            new GroupActiveSelectionReadResult(
                GroupActiveSelectionReadState.Unavailable, reason, Guid.Empty);

        private readonly struct Paths
        {
            internal Paths(string primary, string temporary, string @lock)
            {
                Primary = primary;
                Temporary = temporary;
                Lock = @lock;
            }

            internal string Primary { get; }
            internal string Temporary { get; }
            internal string Lock { get; }
        }
    }

    public sealed class GroupActiveSelectionService : IActiveGroupSelectionService
    {
        private readonly object _gate = new object();
        private readonly IGroupWorldStore _catalogs;
        private readonly IGroupActiveSelectionStore _selections;
        private readonly Func<string> _worldScopeProvider;
        private StableIdentity _cachedIdentity;
        private ActiveGroupSelection _cached = ActiveGroupSelection.Stale;

        public GroupActiveSelectionService(
            IGroupWorldStore catalogs,
            IGroupActiveSelectionStore selections,
            Func<string> worldScopeProvider)
        {
            _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
            _selections = selections ?? throw new ArgumentNullException(nameof(selections));
            _worldScopeProvider = worldScopeProvider ??
                                  throw new ArgumentNullException(nameof(worldScopeProvider));
        }

        public ActiveGroupSelection Resolve(StableIdentity identity)
        {
            if (identity == null) return ActiveGroupSelection.Ambiguous;
            string world;
            try { world = _worldScopeProvider() ?? string.Empty; }
            catch { return ActiveGroupSelection.Stale; }
            if (world.Length == 0)
            {
                lock (_gate)
                    return _cachedIdentity != null && _cachedIdentity.Equals(identity)
                        ? _cached
                        : ActiveGroupSelection.Stale;
            }
            GroupActiveSelectionReadResult selected = _selections.Read(world, identity);
            if (selected.State == GroupActiveSelectionReadState.Missing ||
                selected.State == GroupActiveSelectionReadState.Ready && !selected.HasSelection)
                return ActiveGroupSelection.None;
            if (selected.State == GroupActiveSelectionReadState.Corrupt ||
                selected.State == GroupActiveSelectionReadState.Ambiguous)
                return ActiveGroupSelection.Ambiguous;
            if (selected.State != GroupActiveSelectionReadState.Ready)
                return ActiveGroupSelection.Stale;
            GroupWorldReadResult catalog = _catalogs.Read(world);
            if (catalog == null || catalog.State == GroupWorldReadState.Unavailable)
                return ActiveGroupSelection.Stale;
            if (catalog.State == GroupWorldReadState.Corrupt ||
                catalog.State == GroupWorldReadState.EvidenceConflict)
                return ActiveGroupSelection.Ambiguous;
            if (catalog.State != GroupWorldReadState.Ready || catalog.Catalog == null ||
                !catalog.Catalog.TryGetGroup(selected.GroupId, out GroupRecord group))
                return new ActiveGroupSelection(ActiveGroupSelectionStatus.GroupMissing);
            if (!group.TryGetMember(identity, out _))
                return new ActiveGroupSelection(ActiveGroupSelectionStatus.NotMember);
            return new ActiveGroupSelection(
                ActiveGroupSelectionStatus.Available, group.IdText, group.DisplayName);
        }

        internal bool TrySetAuthoritative(
            StableIdentity identity,
            Guid groupId,
            out ActiveGroupSelection selection,
            out string reasonCode)
        {
            selection = ActiveGroupSelection.Stale;
            reasonCode = "group-world-unavailable";
            string world;
            try { world = _worldScopeProvider() ?? string.Empty; }
            catch
            {
                reasonCode = "group-world-unavailable";
                return false;
            }
            if (world.Length == 0 || !_selections.TrySet(world, identity, groupId, out reasonCode))
                return false;
            selection = Resolve(identity);
            return groupId == Guid.Empty
                ? selection.Status == ActiveGroupSelectionStatus.NoneSelected
                : selection.IsAvailable &&
                  string.Equals(selection.GroupId, groupId.ToString("N"), StringComparison.Ordinal);
        }

        internal void SetClientCache(StableIdentity identity, ActiveGroupSelection selection)
        {
            lock (_gate)
            {
                _cachedIdentity = identity;
                _cached = selection ?? ActiveGroupSelection.Stale;
            }
        }

        internal void ClearClientCache()
        {
            lock (_gate)
            {
                _cachedIdentity = null;
                _cached = ActiveGroupSelection.Stale;
            }
        }
    }
}
