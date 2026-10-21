using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;

namespace Runic.Foundation.Persistence
{
    internal enum PlayerIdentityBindingStoreState
    {
        NoWorld = 0,
        Ready = 1,
        Corrupt = 2,
        StorageFailed = 3
    }

    internal sealed class PlayerIdentityBindingSnapshot
    {
        internal PlayerIdentityBindingSnapshot(string authority, string subjectId, long playerId)
        {
            Authority = authority ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            PlayerId = playerId;
        }

        internal string Authority { get; }
        internal string SubjectId { get; }
        internal long PlayerId { get; }
    }

    internal sealed class PlayerIdentityBindingMutationResult
    {
        internal PlayerIdentityBindingMutationResult(bool success, bool changed, string reasonCode)
        {
            Success = success;
            Changed = changed;
            ReasonCode = reasonCode ?? string.Empty;
        }

        internal bool Success { get; }
        internal bool Changed { get; }
        internal string ReasonCode { get; }

        internal static PlayerIdentityBindingMutationResult Applied(string reasonCode) =>
            new PlayerIdentityBindingMutationResult(true, true, reasonCode);

        internal static PlayerIdentityBindingMutationResult Unchanged(string reasonCode) =>
            new PlayerIdentityBindingMutationResult(true, false, reasonCode);

        internal static PlayerIdentityBindingMutationResult Failed(string reasonCode) =>
            new PlayerIdentityBindingMutationResult(false, false, reasonCode);
    }

    internal interface IPlayerIdentityBindingStorage
    {
        bool TryRead(
            string worldScope,
            out bool exists,
            out byte[] bytes,
            out string reasonCode);

        bool TryWriteAtomic(
            string worldScope,
            byte[] bytes,
            out string reasonCode);
    }

    /// <summary>
    /// Server-owned, world-scoped bijection between an authenticated backend account and a
    /// Valheim profile player ID. Resolve is read-only: enrollment can happen only
    /// through an explicit administrative operation.
    /// </summary>
    internal sealed class PlayerIdentityBindingStore : IRpcPlayerBindingResolver, IDisposable
    {
        internal const int MaximumBindings = 10_000;

        private readonly object _gate = new object();
        private readonly Func<string> _worldScopeProvider;
        private readonly IPlayerIdentityBindingStorage _storage;
        private readonly ManualLogSource _log;
        private Dictionary<string, BindingEntry> _byIdentity =
            new Dictionary<string, BindingEntry>(StringComparer.Ordinal);
        private Dictionary<long, BindingEntry> _byPlayer =
            new Dictionary<long, BindingEntry>();
        private string _worldScope = string.Empty;
        private string _stateReason = "world-unavailable";
        private PlayerIdentityBindingStoreState _state = PlayerIdentityBindingStoreState.NoWorld;
        private bool _scopeObserved;
        private bool _disposed;

        internal PlayerIdentityBindingStore(
            Func<string> worldScopeProvider,
            IPlayerIdentityBindingStorage storage,
            ManualLogSource log = null)
        {
            _worldScopeProvider = worldScopeProvider ??
                throw new ArgumentNullException(nameof(worldScopeProvider));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _log = log;
        }

        internal PlayerIdentityBindingStoreState State
        {
            get { lock (_gate) return _state; }
        }

        internal string WorldScope
        {
            get { lock (_gate) return _worldScope; }
        }

        internal string StateReason
        {
            get { lock (_gate) return _stateReason; }
        }

        /// <summary>
        /// Observes world changes and loads a bounded snapshot. Calling this method never creates
        /// a binding. A corrupt snapshot remains unavailable until an administrator repairs the
        /// file and explicitly reloads it, or the active world changes.
        /// </summary>
        internal void Tick()
        {
            string scope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (_disposed) return;
                if (_scopeObserved && string.Equals(scope, _worldScope, StringComparison.Ordinal))
                    return;
                LoadWorldLocked(scope);
            }
        }

        internal bool Reload(out string reasonCode)
        {
            string scope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (_disposed)
                {
                    reasonCode = "binding-store-disposed";
                    return false;
                }
                LoadWorldLocked(scope);
                reasonCode = _stateReason;
                return _state == PlayerIdentityBindingStoreState.Ready;
            }
        }

        public RpcPlayerBindingStatus Resolve(RpcPeerIdentity identity, long claimedPlayerId)
        {
            if (identity == null ||
                identity.Assurance < RpcIdentityAssurance.BackendAccount ||
                claimedPlayerId == 0)
                return RpcPlayerBindingStatus.Missing;

            string currentScope = GetCurrentWorldScope();
            string identityKey = IdentityKey(identity.Authority, identity.SubjectId);
            lock (_gate)
            {
                if (_disposed || _state != PlayerIdentityBindingStoreState.Ready ||
                    !string.Equals(currentScope, _worldScope, StringComparison.Ordinal))
                    return RpcPlayerBindingStatus.Stale;

                bool hasIdentity = _byIdentity.TryGetValue(identityKey, out BindingEntry forward);
                bool hasPlayer = _byPlayer.TryGetValue(claimedPlayerId, out BindingEntry reverse);
                if (!hasIdentity && !hasPlayer) return RpcPlayerBindingStatus.Missing;
                if (!hasIdentity || !hasPlayer) return RpcPlayerBindingStatus.Conflict;
                if (forward.PlayerId != claimedPlayerId ||
                    !string.Equals(forward.IdentityKey, reverse.IdentityKey, StringComparison.Ordinal))
                    return RpcPlayerBindingStatus.Conflict;
                return RpcPlayerBindingStatus.Verified;
            }
        }

        internal PlayerIdentityBindingMutationResult Enroll(
            RpcPeerIdentity identity,
            long playerId)
        {
            if (identity == null || identity.Assurance < RpcIdentityAssurance.BackendAccount)
                return PlayerIdentityBindingMutationResult.Failed("backend-account-required");
            if (playerId == 0)
                return PlayerIdentityBindingMutationResult.Failed("player-id-required");

            string currentScope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (!CanMutateLocked(currentScope, out string reason))
                    return PlayerIdentityBindingMutationResult.Failed(reason);
                string key = IdentityKey(identity.Authority, identity.SubjectId);
                if (_byIdentity.TryGetValue(key, out BindingEntry existingIdentity))
                {
                    return existingIdentity.PlayerId == playerId
                        ? PlayerIdentityBindingMutationResult.Unchanged("binding-already-present")
                        : PlayerIdentityBindingMutationResult.Failed("identity-already-bound");
                }
                if (_byPlayer.ContainsKey(playerId))
                    return PlayerIdentityBindingMutationResult.Failed("player-id-already-bound");
                if (_byIdentity.Count >= MaximumBindings)
                    return PlayerIdentityBindingMutationResult.Failed("binding-capacity-reached");

                var replacement = new Dictionary<string, BindingEntry>(_byIdentity, StringComparer.Ordinal)
                {
                    [key] = new BindingEntry(identity.Authority, identity.SubjectId, playerId)
                };
                return CommitLocked(replacement, "binding-enrolled");
            }
        }

        internal PlayerIdentityBindingMutationResult RevokeIdentity(
            string authority,
            string subjectId)
        {
            string key;
            try
            {
                var canonical = new RpcPeerIdentity(
                    authority,
                    subjectId,
                    RpcIdentityAssurance.BackendAccount);
                key = IdentityKey(canonical.Authority, canonical.SubjectId);
            }
            catch (ArgumentException)
            {
                return PlayerIdentityBindingMutationResult.Failed("identity-invalid");
            }

            string currentScope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (!CanMutateLocked(currentScope, out string reason))
                    return PlayerIdentityBindingMutationResult.Failed(reason);
                if (!_byIdentity.ContainsKey(key))
                    return PlayerIdentityBindingMutationResult.Unchanged("binding-not-found");
                var replacement = new Dictionary<string, BindingEntry>(_byIdentity, StringComparer.Ordinal);
                replacement.Remove(key);
                return CommitLocked(replacement, "binding-revoked");
            }
        }

        internal PlayerIdentityBindingMutationResult RevokePlayer(long playerId)
        {
            if (playerId == 0)
                return PlayerIdentityBindingMutationResult.Failed("player-id-required");
            string currentScope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (!CanMutateLocked(currentScope, out string reason))
                    return PlayerIdentityBindingMutationResult.Failed(reason);
                if (!_byPlayer.TryGetValue(playerId, out BindingEntry entry))
                    return PlayerIdentityBindingMutationResult.Unchanged("binding-not-found");
                var replacement = new Dictionary<string, BindingEntry>(_byIdentity, StringComparer.Ordinal);
                replacement.Remove(entry.IdentityKey);
                return CommitLocked(replacement, "binding-revoked");
            }
        }

        internal IReadOnlyList<PlayerIdentityBindingSnapshot> List()
        {
            string currentScope = GetCurrentWorldScope();
            lock (_gate)
            {
                if (_disposed || _state != PlayerIdentityBindingStoreState.Ready ||
                    !string.Equals(currentScope, _worldScope, StringComparison.Ordinal))
                    return Array.Empty<PlayerIdentityBindingSnapshot>();
                return _byIdentity.Values
                    .OrderBy(value => value.Authority, StringComparer.Ordinal)
                    .ThenBy(value => value.SubjectId, StringComparer.Ordinal)
                    .Select(value => new PlayerIdentityBindingSnapshot(
                        value.Authority,
                        value.SubjectId,
                        value.PlayerId))
                    .ToArray();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                ClearLocked(PlayerIdentityBindingStoreState.NoWorld, string.Empty, "binding-store-disposed");
            }
        }

        private string GetCurrentWorldScope()
        {
            try
            {
                string value = (_worldScopeProvider() ?? string.Empty).Trim().ToLowerInvariant();
                return PlayerIdentityBindingCodec.IsValidWorldScope(value) ? value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void LoadWorldLocked(string scope)
        {
            _scopeObserved = true;
            if (scope.Length == 0)
            {
                ClearLocked(PlayerIdentityBindingStoreState.NoWorld, string.Empty, "world-unavailable");
                return;
            }
            bool readSucceeded;
            bool exists;
            byte[] bytes;
            string readReason;
            try
            {
                readSucceeded = _storage.TryRead(
                    scope,
                    out exists,
                    out bytes,
                    out readReason);
            }
            catch
            {
                readSucceeded = false;
                exists = false;
                bytes = Array.Empty<byte>();
                readReason = "binding-read-failed";
            }
            if (!readSucceeded)
            {
                if (string.IsNullOrWhiteSpace(readReason)) readReason = "binding-read-failed";
                ClearLocked(PlayerIdentityBindingStoreState.StorageFailed, scope, readReason);
                LogFailure(scope, readReason);
                return;
            }
            if (!exists)
            {
                ClearLocked(PlayerIdentityBindingStoreState.Ready, scope, "binding-store-empty");
                return;
            }
            bool decodedSuccessfully;
            IReadOnlyList<PlayerIdentityBindingSnapshot> decoded;
            string decodeReason;
            try
            {
                decodedSuccessfully = PlayerIdentityBindingCodec.TryDecode(
                    bytes,
                    scope,
                    out decoded,
                    out decodeReason);
            }
            catch
            {
                decodedSuccessfully = false;
                decoded = Array.Empty<PlayerIdentityBindingSnapshot>();
                decodeReason = "binding-decode-failed";
            }
            if (!decodedSuccessfully)
            {
                ClearLocked(PlayerIdentityBindingStoreState.Corrupt, scope, decodeReason);
                LogFailure(scope, decodeReason);
                return;
            }

            var byIdentity = new Dictionary<string, BindingEntry>(StringComparer.Ordinal);
            var byPlayer = new Dictionary<long, BindingEntry>();
            foreach (PlayerIdentityBindingSnapshot item in decoded)
            {
                var entry = new BindingEntry(item.Authority, item.SubjectId, item.PlayerId);
                byIdentity.Add(entry.IdentityKey, entry);
                byPlayer.Add(entry.PlayerId, entry);
            }
            _byIdentity = byIdentity;
            _byPlayer = byPlayer;
            _worldScope = scope;
            _state = PlayerIdentityBindingStoreState.Ready;
            _stateReason = "binding-store-loaded";
        }

        private bool CanMutateLocked(string currentScope, out string reason)
        {
            if (_disposed)
            {
                reason = "binding-store-disposed";
                return false;
            }
            if (_state != PlayerIdentityBindingStoreState.Ready ||
                currentScope.Length == 0 ||
                !string.Equals(currentScope, _worldScope, StringComparison.Ordinal))
            {
                reason = _stateReason.Length == 0 ? "binding-store-unavailable" : _stateReason;
                return false;
            }
            reason = "binding-store-ready";
            return true;
        }

        private PlayerIdentityBindingMutationResult CommitLocked(
            Dictionary<string, BindingEntry> replacement,
            string successReason)
        {
            IReadOnlyList<PlayerIdentityBindingSnapshot> records = replacement.Values
                .OrderBy(value => value.Authority, StringComparer.Ordinal)
                .ThenBy(value => value.SubjectId, StringComparer.Ordinal)
                .Select(value => new PlayerIdentityBindingSnapshot(
                    value.Authority,
                    value.SubjectId,
                    value.PlayerId))
                .ToArray();
            byte[] encoded;
            try { encoded = PlayerIdentityBindingCodec.Encode(_worldScope, records); }
            catch
            {
                return PlayerIdentityBindingMutationResult.Failed("binding-encode-failed");
            }
            bool writeSucceeded;
            string writeReason;
            try
            {
                writeSucceeded = _storage.TryWriteAtomic(
                    _worldScope,
                    encoded,
                    out writeReason);
            }
            catch
            {
                writeSucceeded = false;
                writeReason = "binding-write-failed";
            }
            if (!writeSucceeded)
            {
                if (string.IsNullOrWhiteSpace(writeReason)) writeReason = "binding-write-failed";
                return PlayerIdentityBindingMutationResult.Failed(writeReason);
            }

            var byPlayer = new Dictionary<long, BindingEntry>();
            foreach (BindingEntry entry in replacement.Values) byPlayer.Add(entry.PlayerId, entry);
            _byIdentity = replacement;
            _byPlayer = byPlayer;
            _stateReason = successReason;
            return PlayerIdentityBindingMutationResult.Applied(successReason);
        }

        private void ClearLocked(
            PlayerIdentityBindingStoreState state,
            string worldScope,
            string reason)
        {
            _byIdentity = new Dictionary<string, BindingEntry>(StringComparer.Ordinal);
            _byPlayer = new Dictionary<long, BindingEntry>();
            _worldScope = worldScope ?? string.Empty;
            _state = state;
            _stateReason = reason ?? string.Empty;
        }

        private void LogFailure(string scope, string reason)
        {
            try
            {
                _log?.LogError(
                    "Runic player-identity bindings failed closed for world " + scope +
                    " (" + reason + "). The .bak file is retained for manual recovery but is " +
                    "never trusted automatically.");
            }
            catch { }
        }

        private static string IdentityKey(string authority, string subjectId) =>
            authority + "\0" + subjectId;

        private sealed class BindingEntry
        {
            internal BindingEntry(string authority, string subjectId, long playerId)
            {
                Authority = authority;
                SubjectId = subjectId;
                PlayerId = playerId;
                IdentityKey = PlayerIdentityBindingStore.IdentityKey(authority, subjectId);
            }

            internal string Authority { get; }
            internal string SubjectId { get; }
            internal long PlayerId { get; }
            internal string IdentityKey { get; }
        }
    }

    internal sealed class FilePlayerIdentityBindingStorage : IPlayerIdentityBindingStorage
    {
        internal const int MaximumFileBytes = 16 * 1024 * 1024;

        private readonly string _rootPath;

        internal FilePlayerIdentityBindingStorage(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            _rootPath = Path.GetFullPath(rootPath);
        }

        internal string GetPath(string worldScope)
        {
            if (!PlayerIdentityBindingCodec.IsValidWorldScope(worldScope))
                throw new ArgumentException("World scope is not canonical.", nameof(worldScope));
            string candidate = Path.GetFullPath(Path.Combine(_rootPath, worldScope + ".rpb"));
            string prefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            if (!HasRootPrefix(
                    prefix,
                    candidate,
                    Path.DirectorySeparatorChar == '\\'))
                throw new InvalidOperationException("Binding path escaped its configured root.");
            return candidate;
        }

        internal static bool HasRootPrefix(
            string normalizedRootPrefix,
            string normalizedCandidate,
            bool windowsCaseInsensitive)
        {
            if (normalizedRootPrefix == null || normalizedCandidate == null) return false;
            return normalizedCandidate.StartsWith(
                normalizedRootPrefix,
                windowsCaseInsensitive
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        public bool TryRead(
            string worldScope,
            out bool exists,
            out byte[] bytes,
            out string reasonCode)
        {
            exists = false;
            bytes = Array.Empty<byte>();
            reasonCode = "binding-read-failed";
            try
            {
                string path = GetPath(worldScope);
                if (!File.Exists(path))
                {
                    reasonCode = "binding-file-missing";
                    return true;
                }
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           4096,
                           FileOptions.SequentialScan))
                {
                    if (stream.Length < PlayerIdentityBindingCodec.MinimumEncodedBytes ||
                        stream.Length > MaximumFileBytes || stream.Length > int.MaxValue)
                    {
                        reasonCode = "binding-file-size-invalid";
                        return false;
                    }
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                        {
                            reasonCode = "binding-file-truncated";
                            return false;
                        }
                        offset += read;
                    }
                    if (stream.ReadByte() != -1)
                    {
                        reasonCode = "binding-file-grew-during-read";
                        return false;
                    }
                }
                exists = true;
                reasonCode = "binding-file-read";
                return true;
            }
            catch
            {
                exists = false;
                bytes = Array.Empty<byte>();
                reasonCode = "binding-read-failed";
                return false;
            }
        }

        public bool TryWriteAtomic(
            string worldScope,
            byte[] bytes,
            out string reasonCode)
        {
            reasonCode = "binding-write-failed";
            string temporaryPath = string.Empty;
            try
            {
                if (bytes == null ||
                    bytes.Length < PlayerIdentityBindingCodec.MinimumEncodedBytes ||
                    bytes.Length > MaximumFileBytes)
                {
                    reasonCode = "binding-file-size-invalid";
                    return false;
                }
                string path = GetPath(worldScope);
                Directory.CreateDirectory(_rootPath);
                temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporaryPath, path, path + ".bak", true);
                else
                    File.Move(temporaryPath, path);
                temporaryPath = string.Empty;
                reasonCode = "binding-file-committed";
                return true;
            }
            catch
            {
                reasonCode = "binding-write-failed";
                return false;
            }
            finally
            {
                if (temporaryPath.Length != 0)
                {
                    try { File.Delete(temporaryPath); }
                    catch { }
                }
            }
        }
    }

    internal static class PlayerIdentityBindingCodec
    {
        internal const int SchemaVersion = 1;
        internal const int ChecksumBytes = 32;
        internal const int MinimumEncodedBytes = 8 + 4 + 4 + 4 + ChecksumBytes;
        internal const int MaximumWorldScopeBytes = 128;
        internal const int MaximumAuthorityBytes = RpcPeerIdentity.MaximumAuthorityLength;
        internal const int MaximumSubjectBytes = RpcPeerIdentity.MaximumSubjectLength * 4;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RUNICPBI");
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static bool IsValidWorldScope(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumWorldScopeBytes)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                             character >= '0' && character <= '9' ||
                             character == '.' || character == '-' || character == '_';
                if (!valid) return false;
            }
            return StrictUtf8.GetByteCount(value) <= MaximumWorldScopeBytes;
        }

        internal static byte[] Encode(
            string worldScope,
            IReadOnlyList<PlayerIdentityBindingSnapshot> records)
        {
            if (!IsValidWorldScope(worldScope))
                throw new ArgumentException("World scope is not canonical.", nameof(worldScope));
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count > PlayerIdentityBindingStore.MaximumBindings)
                throw new ArgumentOutOfRangeException(nameof(records));

            var identityKeys = new HashSet<string>(StringComparer.Ordinal);
            var playerIds = new HashSet<long>();
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, StrictUtf8, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                WriteString(writer, worldScope, MaximumWorldScopeBytes);
                writer.Write(records.Count);
                foreach (PlayerIdentityBindingSnapshot record in records)
                {
                    if (record == null || record.PlayerId == 0)
                        throw new ArgumentException("Binding record is invalid.", nameof(records));
                    var identity = new RpcPeerIdentity(
                        record.Authority,
                        record.SubjectId,
                        RpcIdentityAssurance.BackendAccount);
                    string key = identity.Authority + "\0" + identity.SubjectId;
                    if (!identityKeys.Add(key) || !playerIds.Add(record.PlayerId))
                        throw new ArgumentException("Bindings are not reverse-unique.", nameof(records));
                    WriteString(writer, identity.Authority, MaximumAuthorityBytes);
                    WriteString(writer, identity.SubjectId, MaximumSubjectBytes);
                    writer.Write(record.PlayerId);
                }
                writer.Flush();
                byte[] bodyBytes = body.ToArray();
                if (bodyBytes.Length > FilePlayerIdentityBindingStorage.MaximumFileBytes - ChecksumBytes)
                    throw new ArgumentOutOfRangeException(nameof(records));
                byte[] digest;
                using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(bodyBytes);
                var result = new byte[bodyBytes.Length + digest.Length];
                Buffer.BlockCopy(bodyBytes, 0, result, 0, bodyBytes.Length);
                Buffer.BlockCopy(digest, 0, result, bodyBytes.Length, digest.Length);
                return result;
            }
        }

        internal static bool TryDecode(
            byte[] encoded,
            string expectedWorldScope,
            out IReadOnlyList<PlayerIdentityBindingSnapshot> records,
            out string reasonCode)
        {
            records = Array.Empty<PlayerIdentityBindingSnapshot>();
            reasonCode = "binding-decode-failed";
            if (encoded == null ||
                encoded.Length < MinimumEncodedBytes ||
                encoded.Length > FilePlayerIdentityBindingStorage.MaximumFileBytes ||
                !IsValidWorldScope(expectedWorldScope))
            {
                reasonCode = "binding-file-size-invalid";
                return false;
            }

            int bodyLength = encoded.Length - ChecksumBytes;
            byte[] expectedDigest;
            using (SHA256 sha = SHA256.Create())
                expectedDigest = sha.ComputeHash(encoded, 0, bodyLength);
            int difference = 0;
            for (int index = 0; index < ChecksumBytes; index++)
                difference |= expectedDigest[index] ^ encoded[bodyLength + index];
            if (difference != 0)
            {
                reasonCode = "binding-checksum-invalid";
                return false;
            }

            try
            {
                using (var body = new MemoryStream(encoded, 0, bodyLength, false))
                using (var reader = new BinaryReader(body, StrictUtf8, true))
                {
                    byte[] magic = reader.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
                    {
                        reasonCode = "binding-magic-invalid";
                        return false;
                    }
                    if (reader.ReadInt32() != SchemaVersion)
                    {
                        reasonCode = "binding-schema-unsupported";
                        return false;
                    }
                    string worldScope = ReadString(reader, MaximumWorldScopeBytes);
                    if (!string.Equals(worldScope, expectedWorldScope, StringComparison.Ordinal))
                    {
                        reasonCode = "binding-world-mismatch";
                        return false;
                    }
                    int count = reader.ReadInt32();
                    if (count < 0 || count > PlayerIdentityBindingStore.MaximumBindings)
                    {
                        reasonCode = "binding-count-invalid";
                        return false;
                    }
                    var result = new List<PlayerIdentityBindingSnapshot>(count);
                    var identityKeys = new HashSet<string>(StringComparer.Ordinal);
                    var playerIds = new HashSet<long>();
                    for (int index = 0; index < count; index++)
                    {
                        string authority = ReadString(reader, MaximumAuthorityBytes);
                        string subject = ReadString(reader, MaximumSubjectBytes);
                        long playerId = reader.ReadInt64();
                        if (playerId == 0)
                        {
                            reasonCode = "binding-player-id-invalid";
                            return false;
                        }
                        var identity = new RpcPeerIdentity(
                            authority,
                            subject,
                            RpcIdentityAssurance.BackendAccount);
                        if (!string.Equals(identity.Authority, authority, StringComparison.Ordinal) ||
                            !string.Equals(identity.SubjectId, subject, StringComparison.Ordinal))
                        {
                            reasonCode = "binding-identity-noncanonical";
                            return false;
                        }
                        string key = authority + "\0" + subject;
                        if (!identityKeys.Add(key) || !playerIds.Add(playerId))
                        {
                            reasonCode = "binding-bijection-invalid";
                            return false;
                        }
                        result.Add(new PlayerIdentityBindingSnapshot(authority, subject, playerId));
                    }
                    if (body.Position != body.Length)
                    {
                        reasonCode = "binding-trailing-data";
                        return false;
                    }
                    records = result;
                    reasonCode = "binding-decoded";
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is EndOfStreamException ||
                exception is ArgumentException ||
                exception is DecoderFallbackException)
            {
                records = Array.Empty<PlayerIdentityBindingSnapshot>();
                reasonCode = "binding-decode-failed";
                return false;
            }
        }

        private static void WriteString(BinaryWriter writer, string value, int maximumBytes)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length == 0 || bytes.Length > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader, int maximumBytes)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > maximumBytes)
                throw new InvalidDataException("Encoded string length is outside its bound.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }
    }
}
