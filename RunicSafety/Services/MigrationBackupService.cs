using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class MigrationBackupService : IMigrationBackupService
    {
        internal const string MarkerFileName = ".runicsafety-backup";
        internal const string ManifestFileName = "manifest.runic";
        internal const string RestoreFileName = "RESTORE.txt";
        private const string MarkerText = "RUNIC_SAFETY_BACKUP_V1";
        private const int BufferSize = 81920;
        private const int AbsoluteMaximumFiles = 1024;
        private const long AbsoluteMaximumBytes = 1024L * 1024L * 1024L * 64L;
        private const long MaximumManifestBytes = 16L * 1024L * 1024L;
        private const long MaximumRestoreBytes = 1024L * 1024L;
        private const long MaximumMarkerBytes = 128L;
        private const int MaximumConcurrentBackupRoots = 128;
        private const int AbsoluteMaximumSourceDirectories = 4096;

        private static readonly object RootGateSync = new object();
        private static readonly StringComparer RootPathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        private static readonly StringComparison RootPathComparison =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly HashSet<string> ActiveBackupRoots =
            new HashSet<string>(RootPathComparer);

        private readonly IBackupStorage _storage;
        private readonly ISafetyDiagnosticService _diagnostics;
        private readonly Func<DateTime> _clock;
        private readonly Func<MigrationBackupDefaults> _defaults;

        public MigrationBackupService(ISafetyDiagnosticService diagnostics)
            : this(
                new PhysicalBackupStorage(),
                diagnostics,
                () => DateTime.UtcNow,
                () => new MigrationBackupDefaults(
                    SafetyConfig.BackupRoot?.Value ?? Path.Combine(BepInEx.Paths.ConfigPath, "RunicSafety", "backups"),
                    SafetyConfig.BackupRetention?.Value ?? 5,
                    SafetyConfig.BackupMaximumFiles?.Value ?? 32,
                    checked((long)(SafetyConfig.BackupMaximumMiB?.Value ?? 2048) * 1024L * 1024L)))
        {
        }

        internal MigrationBackupService(
            IBackupStorage storage,
            ISafetyDiagnosticService diagnostics,
            Func<DateTime> clock,
            Func<MigrationBackupDefaults> defaults = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _defaults = defaults ?? (() => new MigrationBackupDefaults(
                Path.Combine(Path.GetTempPath(), "RunicSafety", "backups"), 5, 32, 2L * 1024L * 1024L * 1024L));
        }

        public string DefaultDestinationRoot => _defaults().DestinationRoot;
        public int DefaultRetentionCount => _defaults().RetentionCount;
        public int DefaultMaximumFiles => _defaults().MaximumFiles;
        public long DefaultMaximumTotalBytes => _defaults().MaximumTotalBytes;

        public MigrationBackupRequest CreateDefaultRequest(
            string migrationId,
            IEnumerable<MigrationBackupSource> sources)
        {
            MigrationBackupDefaults defaults = _defaults();
            return new MigrationBackupRequest(
                migrationId,
                sources,
                defaults.DestinationRoot,
                defaults.RetentionCount,
                defaults.MaximumTotalBytes,
                defaults.MaximumFiles);
        }

        public MigrationBackupResult CreateBackup(
            MigrationBackupRequest request,
            CancellationToken cancellationToken)
        {
            string correlation = _diagnostics.NewCorrelationId("backup");
            if (!TryAcquireRootLease(request, correlation, out string canonicalRoot,
                    out IDisposable rootLease, out MigrationBackupResult gateFailure))
                return gateFailure;
            using (rootLease)
                return CreateBackupUnderLease(request, cancellationToken, correlation, canonicalRoot);
        }

        private MigrationBackupResult CreateBackupUnderLease(
            MigrationBackupRequest request,
            CancellationToken cancellationToken,
            string correlation,
            string canonicalRoot)
        {
            string root = string.Empty;
            string partial = string.Empty;
            var files = new List<MigrationBackupFile>();
            try
            {
                if (!TryValidateRequest(request, out root, out MigrationBackupOutcome validationOutcome,
                        out string validationCode))
                    return Failure(validationOutcome, correlation, validationCode, files);
                if (!SameRoot(root, canonicalRoot))
                    return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                        "backup-root-changed", files);

                cancellationToken.ThrowIfCancellationRequested();
                var sources = new List<SourceState>(request.Sources.Count);
                var directorySources = new List<DirectorySourceState>();
                long totalBytes = 0;
                var sourcePaths = new HashSet<string>(RootPathComparer);
                for (int index = 0; index < request.Sources.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MigrationBackupSource source = request.Sources[index];
                    if (source == null || string.IsNullOrWhiteSpace(source.SourcePath))
                        return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                            "source-path-required", files);
                    string fullSource = _storage.GetFullPath(source.SourcePath);
                    if (fullSource.Length > 4096)
                        return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                            "source-path-too-long", files);
                    string logicalName = BoundLogicalName(source.LogicalName, index);
                    if (_storage.FileExists(fullSource))
                    {
                        if (!TryAddSourceFile(
                                fullSource,
                                logicalName,
                                request,
                                sourcePaths,
                                sources,
                                ref totalBytes,
                                out MigrationBackupOutcome sourceOutcome,
                                out string sourceFailure))
                            return Failure(sourceOutcome, correlation, sourceFailure, files);
                    }
                    else if (_storage.DirectoryExists(fullSource))
                    {
                        if (_storage.IsDirectoryReparsePoint(fullSource))
                            return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                                "reparse-source-directory-refused", files);
                        if (PathsOverlap(root, fullSource))
                            return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                                "source-directory-overlaps-backup-root", files);
                        if (!TryEnumerateDirectory(
                                fullSource,
                                request.MaximumFiles,
                                cancellationToken,
                                out IReadOnlyList<string> members,
                                out MigrationBackupOutcome directoryOutcome,
                                out string directoryFailure))
                            return Failure(directoryOutcome, correlation, directoryFailure, files);
                        if (members.Count == 0)
                            return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                                "source-directory-empty", files);
                        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
                        {
                            string member = members[memberIndex];
                            string relative = Path.GetRelativePath(fullSource, member)
                                .Replace(Path.DirectorySeparatorChar, '/');
                            string memberLogicalName = BoundLogicalName(
                                logicalName + "/" + relative,
                                index);
                            if (!TryAddSourceFile(
                                    member,
                                    memberLogicalName,
                                    request,
                                    sourcePaths,
                                    sources,
                                    ref totalBytes,
                                    out MigrationBackupOutcome sourceOutcome,
                                    out string sourceFailure))
                                return Failure(sourceOutcome, correlation, sourceFailure, files);
                        }
                        directorySources.Add(new DirectorySourceState(fullSource, members));
                    }
                    else
                    {
                        return Failure(MigrationBackupOutcome.SourceMissing, correlation,
                            "source-missing", files);
                    }
                }

                _storage.CreateDirectory(root);
                if (_storage.IsDirectoryReparsePoint(root))
                    return Failure(MigrationBackupOutcome.InvalidRequest, correlation,
                        "reparse-backup-root-refused", files);
                string suffix = _clock().ToUniversalTime().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) +
                                "-" + ShortCorrelation(correlation);
                partial = _storage.Combine(root, ".partial-" + suffix);
                string committed = _storage.Combine(root, "backup-" + suffix);
                if (_storage.DirectoryExists(partial) || _storage.DirectoryExists(committed))
                    return Failure(MigrationBackupOutcome.IoFailure, correlation,
                        "backup-name-collision", files);
                _storage.CreateDirectory(partial);

                for (int index = 0; index < sources.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SourceState source = sources[index];
                    string backupName = CreateBackupName(index, source.Path);
                    string destination = _storage.Combine(partial, backupName);
                    string sha;
                    try
                    {
                        sha = CopyAndHash(
                            source.Path,
                            destination,
                            source.Metadata.Length,
                            cancellationToken);
                    }
                    catch (BackupSourceChangedException)
                    {
                        return FailureWithCleanup(
                            MigrationBackupOutcome.SourceChangedDuringCopy,
                            correlation,
                            "source-grew-during-copy",
                            files,
                            root,
                            partial);
                    }
                    BackupFileMetadata after = _storage.GetFileMetadata(source.Path);
                    BackupFileMetadata copied = _storage.GetFileMetadata(destination);
                    if (!source.Metadata.StableEquals(after) || copied.Length != source.Metadata.Length)
                        return FailureWithCleanup(
                            MigrationBackupOutcome.SourceChangedDuringCopy,
                            correlation,
                            "source-changed-during-copy",
                            files,
                            root,
                            partial);
                    files.Add(new MigrationBackupFile(
                        source.LogicalName,
                        source.Path,
                        backupName,
                        source.Metadata.Length,
                        sha));
                }

                for (int index = 0; index < directorySources.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DirectorySourceState directory = directorySources[index];
                    if (!TryEnumerateDirectory(
                            directory.Path,
                            request.MaximumFiles,
                            cancellationToken,
                            out IReadOnlyList<string> currentMembers,
                            out _,
                            out _) ||
                        !SamePaths(directory.Members, currentMembers))
                        return FailureWithCleanup(
                            MigrationBackupOutcome.SourceChangedDuringCopy,
                            correlation,
                            "source-directory-changed-during-copy",
                            files,
                            root,
                            partial);
                }

                // A directory-backed save can update an earlier chunk while a later
                // chunk is still being copied. Recheck every member as one snapshot
                // after the membership check so that such cross-file churn cannot
                // produce a successfully committed mixed-generation backup.
                for (int index = 0; index < sources.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SourceState source = sources[index];
                    if (!source.Metadata.StableEquals(_storage.GetFileMetadata(source.Path)))
                        return FailureWithCleanup(
                            MigrationBackupOutcome.SourceChangedDuringCopy,
                            correlation,
                            "source-changed-during-copy",
                            files,
                            root,
                            partial);
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriteManifest(partial, request.MigrationId, correlation, files);
                WriteRestoreInstructions(partial);
                _storage.WriteAllTextNew(
                    _storage.Combine(partial, MarkerFileName),
                    MarkerText + Environment.NewLine);
                _storage.MoveDirectory(partial, committed);
                partial = string.Empty;

                if (!ValidateBackupInternal(
                        committed,
                        request.MigrationId,
                        correlation,
                        files,
                        out string validationFailure))
                {
                    _diagnostics.Record(correlation, "migration-backup", "post-commit-validation-failed",
                        SafetyDiagnosticSeverity.Error);
                    return new MigrationBackupResult(
                        MigrationBackupOutcome.IoFailure,
                        committed,
                        correlation,
                        "post-commit-validation-failed-" + BoundCode(validationFailure),
                        files.AsReadOnly());
                }

                try { ApplyRetention(root, committed, request.RetentionCount, sources); }
                catch (Exception)
                {
                    _diagnostics.Record(correlation, "migration-backup", "retention-failed",
                        SafetyDiagnosticSeverity.Warning);
                }

                var result = new MigrationBackupResult(
                    MigrationBackupOutcome.Succeeded,
                    committed,
                    correlation,
                    string.Empty,
                    files.AsReadOnly());
                _diagnostics.Record(correlation, "migration-backup", "committed");
                return result;
            }
            catch (OperationCanceledException)
            {
                CleanupPartial(root, partial);
                return Failure(MigrationBackupOutcome.Cancelled, correlation, "cancelled", files);
            }
            catch (Exception)
            {
                CleanupPartial(root, partial);
                return Failure(MigrationBackupOutcome.IoFailure, correlation, "io-failure", files);
            }
        }

        public MigrationExecutionResult ExecuteAfterBackup(
            MigrationBackupRequest request,
            Action mutation,
            CancellationToken cancellationToken)
        {
            string correlation = _diagnostics.NewCorrelationId("backup");
            if (mutation == null)
            {
                MigrationBackupResult invalid = Failure(
                    MigrationBackupOutcome.InvalidRequest,
                    correlation,
                    "mutation-required",
                    new List<MigrationBackupFile>());
                return new MigrationExecutionResult(invalid, false, null);
            }

            if (!TryAcquireRootLease(request, correlation, out string canonicalRoot,
                    out IDisposable rootLease, out MigrationBackupResult gateFailure))
                return new MigrationExecutionResult(gateFailure, false, null);
            using (rootLease)
            {
                MigrationBackupResult backup = CreateBackupUnderLease(
                    request, cancellationToken, correlation, canonicalRoot);
                if (!backup.Succeeded) return new MigrationExecutionResult(backup, false, null);

                if (cancellationToken.IsCancellationRequested)
                    return new MigrationExecutionResult(
                        CommittedFailure(backup, MigrationBackupOutcome.Cancelled,
                            "cancelled-before-mutation"), false, null);

                var backupProtection = new List<Stream>(backup.Files.Count + 3);
                try
                {
                    string committed = _storage.GetFullPath(backup.BackupDirectory);
                    if (!IsDirectChild(canonicalRoot, committed))
                        return new MigrationExecutionResult(
                            CommittedFailure(backup, MigrationBackupOutcome.IoFailure,
                                "backup-root-proof-failed"), false, null);
                    ProtectFile(backupProtection, _storage.Combine(committed, MarkerFileName));
                    ProtectFile(backupProtection, _storage.Combine(committed, ManifestFileName));
                    ProtectFile(backupProtection, _storage.Combine(committed, RestoreFileName));
                    for (int index = 0; index < backup.Files.Count; index++)
                    {
                        MigrationBackupFile file = backup.Files[index];
                        if (file == null || !IsSimpleFileName(file.BackupFileName))
                            throw new InvalidDataException("Backup result contains an unsafe filename.");
                        ProtectFile(
                            backupProtection,
                            _storage.Combine(committed, file.BackupFileName));
                    }
                    string validationFailure = "marker-unavailable";
                    if (!ValidateBackupInternal(
                            committed,
                            request.MigrationId,
                            backup.CorrelationId,
                            backup.Files,
                            out validationFailure))
                        return new MigrationExecutionResult(
                            CommittedFailure(backup, MigrationBackupOutcome.IoFailure,
                                "pre-mutation-validation-failed-" + BoundCode(validationFailure)),
                            false,
                            null);
                    if (cancellationToken.IsCancellationRequested)
                        return new MigrationExecutionResult(
                            CommittedFailure(backup, MigrationBackupOutcome.Cancelled,
                                "cancelled-before-mutation"), false, null);

                    try
                    {
                        mutation();
                        _diagnostics.Record(backup.CorrelationId, "migration", "mutation-completed");
                        return new MigrationExecutionResult(backup, true, null);
                    }
                    catch (Exception exception)
                    {
                        _diagnostics.Record(backup.CorrelationId, "migration", "mutation-threw",
                            SafetyDiagnosticSeverity.Error);
                        return new MigrationExecutionResult(backup, true, exception);
                    }
                }
                catch (Exception)
                {
                    return new MigrationExecutionResult(
                        CommittedFailure(backup, MigrationBackupOutcome.IoFailure,
                            "pre-mutation-validation-io-failure"), false, null);
                }
                finally
                {
                    for (int index = backupProtection.Count - 1; index >= 0; index--)
                        backupProtection[index]?.Dispose();
                }
            }
        }

        public bool ValidateBackup(string backupDirectory, out string failureCode)
            => ValidateBackupInternal(
                backupDirectory,
                null,
                null,
                null,
                out failureCode);

        private bool ValidateBackupInternal(
            string backupDirectory,
            string expectedMigrationId,
            string expectedCorrelationId,
            IReadOnlyList<MigrationBackupFile> expectedFiles,
            out string failureCode)
        {
            failureCode = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(backupDirectory))
                {
                    failureCode = "backup-directory-required";
                    return false;
                }
                string directory = _storage.GetFullPath(backupDirectory);
                if (!_storage.DirectoryExists(directory) || _storage.IsDirectoryReparsePoint(directory) ||
                    !_storage.FileExists(_storage.Combine(directory, MarkerFileName)) ||
                    !_storage.FileExists(_storage.Combine(directory, ManifestFileName)) ||
                    !_storage.FileExists(_storage.Combine(directory, RestoreFileName)))
                {
                    failureCode = "backup-incomplete";
                    return false;
                }
                BackupFileMetadata markerMetadata = _storage.GetFileMetadata(
                    _storage.Combine(directory, MarkerFileName));
                BackupFileMetadata manifestMetadata = _storage.GetFileMetadata(
                    _storage.Combine(directory, ManifestFileName));
                BackupFileMetadata restoreMetadata = _storage.GetFileMetadata(
                    _storage.Combine(directory, RestoreFileName));
                if (markerMetadata.IsReparsePoint || manifestMetadata.IsReparsePoint ||
                    restoreMetadata.IsReparsePoint || markerMetadata.Length > MaximumMarkerBytes ||
                    manifestMetadata.Length > MaximumManifestBytes ||
                    restoreMetadata.Length > MaximumRestoreBytes)
                {
                    failureCode = "backup-metadata-file-limit";
                    return false;
                }
                if (!string.Equals(
                        ReadBoundedUtf8(
                            _storage.Combine(directory, MarkerFileName),
                            MaximumMarkerBytes).Trim(),
                        MarkerText,
                        StringComparison.Ordinal))
                {
                    failureCode = "marker-invalid";
                    return false;
                }
                string restoreText = ReadBoundedUtf8(
                    _storage.Combine(directory, RestoreFileName), MaximumRestoreBytes);
                if (!restoreText.StartsWith("Runic Safety migration backup", StringComparison.Ordinal))
                {
                    failureCode = "restore-instructions-invalid";
                    return false;
                }
                string[] lines = ReadBoundedUtf8Lines(
                    _storage.Combine(directory, ManifestFileName),
                    MaximumManifestBytes,
                    AbsoluteMaximumFiles + 16);
                if (lines.Length < 4 || lines.Length > AbsoluteMaximumFiles + 16 ||
                    !string.Equals(lines[0], MarkerText, StringComparison.Ordinal) ||
                    !lines[1].StartsWith("MIGRATION\t", StringComparison.Ordinal) ||
                    !lines[2].StartsWith("CORRELATION\t", StringComparison.Ordinal) ||
                    !lines[3].StartsWith("CREATED_UTC\t", StringComparison.Ordinal))
                {
                    failureCode = "manifest-invalid";
                    return false;
                }
                string[] migrationFields = lines[1].Split('\t');
                string[] correlationFields = lines[2].Split('\t');
                string[] createdFields = lines[3].Split('\t');
                if (migrationFields.Length != 2 || correlationFields.Length != 2 || createdFields.Length != 2 ||
                    migrationFields[1].Length > 256 || correlationFields[1].Length > 256 ||
                    createdFields[1].Length > 64)
                {
                    failureCode = "manifest-metadata-invalid";
                    return false;
                }
                string migrationId = Decode(migrationFields[1]);
                string correlationId = Decode(correlationFields[1]);
                if (string.IsNullOrWhiteSpace(migrationId) || migrationId.Length > 96 ||
                    string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128 ||
                    expectedMigrationId != null && !string.Equals(
                        migrationId, expectedMigrationId, StringComparison.Ordinal) ||
                    expectedCorrelationId != null && !string.Equals(
                        correlationId, expectedCorrelationId, StringComparison.Ordinal) ||
                    !DateTime.TryParseExact(createdFields[1], "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out _))
                {
                    failureCode = "manifest-metadata-invalid";
                    return false;
                }
                int fileLines = 0;
                long totalLength = 0;
                var backupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, MigrationBackupFile> expectedByName = null;
                if (expectedFiles != null)
                {
                    if (expectedFiles.Count < 1 || expectedFiles.Count > AbsoluteMaximumFiles)
                    {
                        failureCode = "expected-file-set-invalid";
                        return false;
                    }
                    expectedByName = new Dictionary<string, MigrationBackupFile>(
                        expectedFiles.Count, StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < expectedFiles.Count; index++)
                    {
                        MigrationBackupFile expected = expectedFiles[index];
                        if (expected == null || !IsSimpleFileName(expected.BackupFileName) ||
                            expected.Length < 0 || !IsSha256(expected.Sha256) ||
                            !expectedByName.TryAdd(expected.BackupFileName, expected))
                        {
                            failureCode = "expected-file-set-invalid";
                            return false;
                        }
                    }
                }
                for (int lineIndex = 4; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (!line.StartsWith("FILE\t", StringComparison.Ordinal))
                    {
                        failureCode = "manifest-entry-invalid";
                        return false;
                    }
                    if (++fileLines > AbsoluteMaximumFiles)
                    {
                        failureCode = "manifest-file-limit";
                        return false;
                    }
                    string[] fields = line.Split('\t');
                    if (fields.Length != 6 || !long.TryParse(fields[4], NumberStyles.None,
                            CultureInfo.InvariantCulture, out long expectedLength) ||
                        expectedLength < 0 || fields[1].Length > 256 || fields[2].Length > 65536 ||
                        fields[3].Length > 256 || fields[5].Length != 64)
                    {
                        failureCode = "manifest-entry-invalid";
                        return false;
                    }
                    string logicalName = Decode(fields[1]);
                    string originalPath = Decode(fields[2]);
                    string fileName = Decode(fields[3]);
                    if (string.IsNullOrWhiteSpace(logicalName) || logicalName.Length > 96 ||
                        string.IsNullOrWhiteSpace(originalPath) || originalPath.Length > 4096 ||
                        !Path.IsPathRooted(originalPath) || !IsSimpleFileName(fileName) ||
                        !backupNames.Add(fileName) || !IsSha256(fields[5]))
                    {
                        failureCode = "manifest-path-invalid";
                        return false;
                    }
                    if (expectedByName != null)
                    {
                        if (!expectedByName.TryGetValue(fileName, out MigrationBackupFile expected) ||
                            !string.Equals(expected.LogicalName, logicalName, StringComparison.Ordinal) ||
                            !string.Equals(expected.OriginalPath, originalPath, StringComparison.Ordinal) ||
                            expected.Length != expectedLength ||
                            !string.Equals(expected.Sha256, fields[5], StringComparison.OrdinalIgnoreCase))
                        {
                            failureCode = "backup-file-set-changed";
                            return false;
                        }
                    }
                    string path = _storage.Combine(directory, fileName);
                    if (!_storage.FileExists(path))
                    {
                        failureCode = "backup-file-missing-or-sized-wrong";
                        return false;
                    }
                    BackupFileMetadata fileMetadata = _storage.GetFileMetadata(path);
                    if (fileMetadata.IsReparsePoint || fileMetadata.Length != expectedLength)
                    {
                        failureCode = "backup-file-missing-or-sized-wrong";
                        return false;
                    }
                    try { totalLength = checked(totalLength + expectedLength); }
                    catch (OverflowException)
                    {
                        failureCode = "manifest-size-overflow";
                        return false;
                    }
                    if (totalLength > AbsoluteMaximumBytes)
                    {
                        failureCode = "manifest-size-limit";
                        return false;
                    }
                    string actual = HashFile(path, expectedLength, CancellationToken.None);
                    if (!string.Equals(actual, fields[5], StringComparison.OrdinalIgnoreCase))
                    {
                        failureCode = "backup-hash-mismatch";
                        return false;
                    }
                }
                if (fileLines == 0 || expectedByName != null && fileLines != expectedByName.Count)
                {
                    failureCode = fileLines == 0
                        ? "manifest-has-no-files"
                        : "backup-file-set-changed";
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                failureCode = "backup-validation-io-failure";
                return false;
            }
        }

        private bool TryValidateRequest(
            MigrationBackupRequest request,
            out string root,
            out MigrationBackupOutcome outcome,
            out string failureCode)
        {
            root = string.Empty;
            outcome = MigrationBackupOutcome.InvalidRequest;
            failureCode = "invalid-request";
            if (request == null || string.IsNullOrWhiteSpace(request.MigrationId) ||
                request.MigrationId.Length > 96 || request.Sources == null ||
                request.Sources.Count == 0 || string.IsNullOrWhiteSpace(request.DestinationRoot) ||
                request.RetentionCount < 1 || request.RetentionCount > 128 ||
                request.MaximumFiles < 1 || request.MaximumFiles > AbsoluteMaximumFiles ||
                request.MaximumTotalBytes < 1 || request.MaximumTotalBytes > AbsoluteMaximumBytes)
                return false;
            if (request.Sources.Count > request.MaximumFiles)
            {
                outcome = MigrationBackupOutcome.FileLimitExceeded;
                failureCode = "configured-file-limit";
                return false;
            }
            root = _storage.GetFullPath(request.DestinationRoot);
            if (string.IsNullOrWhiteSpace(root)) return false;
            return true;
        }

        private bool TryAddSourceFile(
            string path,
            string logicalName,
            MigrationBackupRequest request,
            HashSet<string> sourcePaths,
            List<SourceState> sources,
            ref long totalBytes,
            out MigrationBackupOutcome outcome,
            out string failureCode)
        {
            outcome = MigrationBackupOutcome.InvalidRequest;
            failureCode = "invalid-source";
            string fullPath = _storage.GetFullPath(path);
            if (fullPath.Length > 4096)
            {
                failureCode = "source-path-too-long";
                return false;
            }
            if (!_storage.FileExists(fullPath))
            {
                outcome = MigrationBackupOutcome.SourceMissing;
                failureCode = "source-missing";
                return false;
            }
            if (!sourcePaths.Add(fullPath))
            {
                failureCode = "duplicate-source";
                return false;
            }
            if (sources.Count >= request.MaximumFiles)
            {
                outcome = MigrationBackupOutcome.FileLimitExceeded;
                failureCode = "configured-file-limit";
                return false;
            }
            BackupFileMetadata metadata = _storage.GetFileMetadata(fullPath);
            if (metadata.IsReparsePoint)
            {
                failureCode = "reparse-source-refused";
                return false;
            }
            try { totalBytes = checked(totalBytes + metadata.Length); }
            catch (OverflowException)
            {
                outcome = MigrationBackupOutcome.SizeLimitExceeded;
                failureCode = "source-size-overflow";
                return false;
            }
            if (totalBytes > request.MaximumTotalBytes)
            {
                outcome = MigrationBackupOutcome.SizeLimitExceeded;
                failureCode = "configured-size-limit";
                return false;
            }
            sources.Add(new SourceState(fullPath, logicalName, metadata));
            failureCode = string.Empty;
            return true;
        }

        private bool TryEnumerateDirectory(
            string sourceRoot,
            int maximumFiles,
            CancellationToken cancellationToken,
            out IReadOnlyList<string> members,
            out MigrationBackupOutcome outcome,
            out string failureCode)
        {
            members = Array.Empty<string>();
            outcome = MigrationBackupOutcome.InvalidRequest;
            failureCode = "source-directory-invalid";
            string root = _storage.GetFullPath(sourceRoot);
            if (!_storage.DirectoryExists(root))
            {
                outcome = MigrationBackupOutcome.SourceMissing;
                failureCode = "source-directory-missing";
                return false;
            }
            if (_storage.IsDirectoryReparsePoint(root))
            {
                failureCode = "reparse-source-directory-refused";
                return false;
            }

            var pending = new List<string> { root };
            var seenDirectories = new HashSet<string>(RootPathComparer) { root };
            var result = new List<string>();
            for (int directoryIndex = 0; directoryIndex < pending.Count; directoryIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending[directoryIndex];
                if (!_storage.DirectoryExists(directory) ||
                    _storage.IsDirectoryReparsePoint(directory) ||
                    !IsSameOrNested(root, directory))
                {
                    failureCode = "source-directory-tree-invalid";
                    return false;
                }

                string[] files = _storage.GetFiles(directory, "*") ?? Array.Empty<string>();
                Array.Sort(files, RootPathComparer);
                for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string file = _storage.GetFullPath(files[fileIndex]);
                    if (file.Length > 4096 || !IsNested(root, file) || !_storage.FileExists(file))
                    {
                        failureCode = "source-directory-member-invalid";
                        return false;
                    }
                    if (result.Count >= maximumFiles || result.Count >= AbsoluteMaximumFiles)
                    {
                        outcome = MigrationBackupOutcome.FileLimitExceeded;
                        failureCode = "configured-file-limit";
                        return false;
                    }
                    result.Add(file);
                }

                string[] directories = _storage.GetDirectories(directory, "*") ?? Array.Empty<string>();
                Array.Sort(directories, RootPathComparer);
                for (int childIndex = 0; childIndex < directories.Length; childIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string child = _storage.GetFullPath(directories[childIndex]);
                    if (child.Length > 4096 || !IsNested(root, child) ||
                        !_storage.DirectoryExists(child) ||
                        _storage.IsDirectoryReparsePoint(child) ||
                        !seenDirectories.Add(child))
                    {
                        failureCode = "source-directory-tree-invalid";
                        return false;
                    }
                    if (pending.Count >= AbsoluteMaximumSourceDirectories)
                    {
                        outcome = MigrationBackupOutcome.FileLimitExceeded;
                        failureCode = "source-directory-count-limit";
                        return false;
                    }
                    pending.Add(child);
                }
            }
            result.Sort(RootPathComparer);
            members = result.AsReadOnly();
            failureCode = string.Empty;
            return true;
        }

        private static bool SamePaths(
            IReadOnlyList<string> expected,
            IReadOnlyList<string> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count) return false;
            for (int index = 0; index < expected.Count; index++)
                if (!RootPathComparer.Equals(expected[index], actual[index])) return false;
            return true;
        }

        private static bool PathsOverlap(string left, string right) =>
            SameRoot(left, right) || IsNested(left, right) || IsNested(right, left);

        private static bool IsSameOrNested(string parent, string candidate) =>
            SameRoot(parent, candidate) || IsNested(parent, candidate);

        private static bool IsNested(string parent, string candidate)
        {
            string prefix = EnsureTrailingSeparator(Path.GetFullPath(parent));
            string fullCandidate = Path.GetFullPath(candidate);
            return fullCandidate.StartsWith(prefix, RootPathComparison);
        }

        private bool TryAcquireRootLease(
            MigrationBackupRequest request,
            string correlation,
            out string canonicalRoot,
            out IDisposable lease,
            out MigrationBackupResult failure)
        {
            canonicalRoot = string.Empty;
            lease = null;
            failure = null;
            var files = new List<MigrationBackupFile>();
            try
            {
                if (!TryValidateRequest(request, out canonicalRoot,
                        out MigrationBackupOutcome outcome, out string code))
                {
                    failure = Failure(outcome, correlation, code, files);
                    return false;
                }
            }
            catch (Exception)
            {
                failure = Failure(MigrationBackupOutcome.IoFailure, correlation,
                    "backup-root-canonicalization-failed", files);
                return false;
            }

            lock (RootGateSync)
            {
                if (ActiveBackupRoots.Contains(canonicalRoot))
                {
                    failure = Failure(MigrationBackupOutcome.IoFailure, correlation,
                        "backup-root-busy", files);
                    return false;
                }
                if (ActiveBackupRoots.Count >= MaximumConcurrentBackupRoots)
                {
                    failure = Failure(MigrationBackupOutcome.IoFailure, correlation,
                        "backup-root-gate-capacity", files);
                    return false;
                }
                ActiveBackupRoots.Add(canonicalRoot);
            }
            lease = new RootLease(canonicalRoot);
            return true;
        }

        private static bool SameRoot(string left, string right) =>
            !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) &&
            RootPathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

        private static void ReleaseRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            lock (RootGateSync) ActiveBackupRoots.Remove(root);
        }

        private string CopyAndHash(
            string source,
            string destination,
            long expectedLength,
            CancellationToken cancellationToken)
        {
            using (Stream input = _storage.OpenRead(source))
            using (Stream output = _storage.CreateNew(destination))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[BufferSize];
                int read;
                long copied = 0L;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    copied = checked(copied + read);
                    if (copied > expectedLength) throw new BackupSourceChangedException();
                    output.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (copied != expectedLength) throw new BackupSourceChangedException();
                output.Flush();
                if (output is FileStream fileStream) fileStream.Flush(true);
                return ToHex(sha.Hash);
            }
        }

        private string HashFile(
            string path,
            long expectedLength,
            CancellationToken cancellationToken)
        {
            using (Stream input = _storage.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[BufferSize];
                int read;
                long hashed = 0L;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hashed = checked(hashed + read);
                    if (hashed > expectedLength)
                        throw new InvalidDataException("Backup file grew during validation.");
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (hashed != expectedLength)
                    throw new InvalidDataException("Backup file shrank during validation.");
                return ToHex(sha.Hash);
            }
        }

        private void ProtectFile(ICollection<Stream> protection, string path)
        {
            Stream stream = _storage.OpenRead(path);
            if (stream == null || !stream.CanRead)
            {
                stream?.Dispose();
                throw new IOException("A committed backup file could not be protected.");
            }
            protection.Add(stream);
        }

        private string ReadBoundedUtf8(string path, long maximumBytes)
        {
            if (maximumBytes < 0 || maximumBytes > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            using (Stream input = _storage.OpenRead(path))
            using (var memory = new MemoryStream((int)Math.Min(maximumBytes, BufferSize)))
            {
                int bufferLength = maximumBytes >= BufferSize
                    ? BufferSize
                    : checked((int)maximumBytes + 1);
                byte[] buffer = new byte[bufferLength];
                long total = 0L;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    total = checked(total + read);
                    if (total > maximumBytes)
                        throw new InvalidDataException("Bounded UTF-8 file exceeded its limit.");
                    memory.Write(buffer, 0, read);
                }
                return StrictUtf8.GetString(memory.ToArray());
            }
        }

        private string[] ReadBoundedUtf8Lines(
            string path,
            long maximumBytes,
            int maximumLines)
        {
            string text = ReadBoundedUtf8(path, maximumBytes);
            var lines = new List<string>(Math.Min(maximumLines, 64));
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (lines.Count >= maximumLines)
                        throw new InvalidDataException("Bounded manifest exceeded its line limit.");
                    lines.Add(line);
                }
            }
            return lines.ToArray();
        }

        private void WriteManifest(
            string partial,
            string migrationId,
            string correlation,
            IReadOnlyList<MigrationBackupFile> files)
        {
            var builder = new StringBuilder();
            builder.AppendLine(MarkerText);
            builder.Append(global::Runic.Localization.RunicText.Get("text_591ed5228a15")).AppendLine(Encode(migrationId));
            builder.Append(global::Runic.Localization.RunicText.Get("text_9e4ee92280d6")).AppendLine(Encode(correlation));
            builder.Append(global::Runic.Localization.RunicText.Get("text_3194eac00899")).AppendLine(
                _clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            foreach (MigrationBackupFile file in files)
            {
                builder.Append("FILE\t")
                    .Append(Encode(file.LogicalName)).Append('\t')
                    .Append(Encode(file.OriginalPath)).Append('\t')
                    .Append(Encode(file.BackupFileName)).Append('\t')
                    .Append(file.Length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(file.Sha256).AppendLine();
            }
            _storage.WriteAllTextNew(_storage.Combine(partial, ManifestFileName), builder.ToString());
        }

        private void WriteRestoreInstructions(string partial)
        {
            const string instructions =
                "Runic Safety migration backup\n\n" +
                "1. Stop Valheim and the dedicated server.\n" +
                "2. Validate this backup with Runic Safety before restoring.\n" +
                "3. Read manifest.runic; each FILE row contains Base64 UTF-8 logical name, original path, " +
                "backup filename, byte length, and SHA-256.\n" +
                "4. Copy each backup file to its decoded original path only after preserving the current file.\n" +
                "5. Keep world and character file families from the same timestamp together.\n" +
                "Runic Safety intentionally does not auto-restore multi-file saves because that is not atomic.\n";
            _storage.WriteAllTextNew(_storage.Combine(partial, RestoreFileName), instructions);
        }

        private void ApplyRetention(
            string root,
            string current,
            int retentionCount,
            IReadOnlyList<SourceState> sources)
        {
            var committed = new List<BackupDirectory>();
            foreach (string directory in _storage.GetDirectories(root, "backup-*"))
            {
                string full = _storage.GetFullPath(directory);
                string marker = _storage.Combine(full, MarkerFileName);
                if (!IsDirectChild(root, full) || _storage.IsDirectoryReparsePoint(full) ||
                    !_storage.FileExists(marker) || _storage.GetFileMetadata(marker).Length > MaximumMarkerBytes ||
                    !string.Equals(
                        ReadBoundedUtf8(marker, MaximumMarkerBytes).Trim(),
                        MarkerText,
                        StringComparison.Ordinal))
                    continue;
                committed.Add(new BackupDirectory(full, _storage.GetDirectoryCreationUtc(full)));
            }
            committed.Sort((left, right) =>
            {
                int date = right.CreatedUtc.CompareTo(left.CreatedUtc);
                return date != 0 ? date : RootPathComparer.Compare(right.Path, left.Path);
            });
            for (int index = retentionCount; index < committed.Count; index++)
            {
                if (string.Equals(committed[index].Path, current, RootPathComparison)) continue;
                if (ContainsSource(committed[index].Path, sources)) continue;
                _storage.DeleteDirectory(committed[index].Path, true);
            }
        }

        private static bool ContainsSource(string directory, IReadOnlyList<SourceState> sources)
        {
            string prefix = EnsureTrailingSeparator(Path.GetFullPath(directory));
            for (int index = 0; index < sources.Count; index++)
            {
                string source = Path.GetFullPath(sources[index].Path);
                if (source.StartsWith(prefix, RootPathComparison)) return true;
            }
            return false;
        }

        private MigrationBackupResult FailureWithCleanup(
            MigrationBackupOutcome outcome,
            string correlation,
            string code,
            List<MigrationBackupFile> files,
            string root,
            string partial)
        {
            CleanupPartial(root, partial);
            return Failure(outcome, correlation, code, files);
        }

        private MigrationBackupResult Failure(
            MigrationBackupOutcome outcome,
            string correlation,
            string code,
            List<MigrationBackupFile> files)
        {
            _diagnostics.Record(correlation, "migration-backup", code, SafetyDiagnosticSeverity.Warning);
            return new MigrationBackupResult(
                outcome,
                string.Empty,
                correlation,
                code,
                files.AsReadOnly());
        }

        private MigrationBackupResult CommittedFailure(
            MigrationBackupResult backup,
            MigrationBackupOutcome outcome,
            string code)
        {
            _diagnostics.Record(backup.CorrelationId, "migration-backup", code,
                SafetyDiagnosticSeverity.Error);
            return new MigrationBackupResult(
                outcome,
                backup.BackupDirectory,
                backup.CorrelationId,
                code,
                backup.Files);
        }

        private void CleanupPartial(string root, string partial)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(partial)) return;
            try
            {
                string fullRoot = _storage.GetFullPath(root);
                string fullPartial = _storage.GetFullPath(partial);
                string name = _storage.GetFileName(fullPartial);
                if (IsDirectChild(fullRoot, fullPartial) &&
                    name.StartsWith(".partial-", StringComparison.Ordinal) &&
                    !_storage.IsDirectoryReparsePoint(fullPartial) &&
                    _storage.DirectoryExists(fullPartial))
                    _storage.DeleteDirectory(fullPartial, true);
            }
            catch (Exception) { }
        }

        private static bool IsDirectChild(string parent, string child)
        {
            string parentFull = EnsureTrailingSeparator(Path.GetFullPath(parent));
            string childFull = Path.GetFullPath(child);
            if (!childFull.StartsWith(parentFull, RootPathComparison)) return false;
            string relative = childFull.Substring(parentFull.Length);
            return relative.Length != 0 && relative.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                   relative.IndexOf(Path.AltDirectorySeparatorChar) < 0;
        }

        private static string EnsureTrailingSeparator(string value) =>
            value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? value
                : value + Path.DirectorySeparatorChar;

        private static string CreateBackupName(int index, string path)
        {
            string extension = Path.GetExtension(path);
            if (extension.Length > 16 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                extension = ".bin";
            return index.ToString("D4", CultureInfo.InvariantCulture) + extension.ToLowerInvariant();
        }

        private static string BoundLogicalName(string value, int index)
        {
            string fallback = "source-" + index.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length <= 96 ? trimmed : trimmed.Substring(0, 96);
        }

        private static string BoundCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            string trimmed = value.Trim();
            var builder = new StringBuilder(Math.Min(trimmed.Length, 48));
            for (int index = 0; index < trimmed.Length && builder.Length < 48; index++)
            {
                char current = trimmed[index];
                builder.Append(char.IsLetterOrDigit(current) || current == '-' || current == '_'
                    ? current
                    : '_');
            }
            return builder.ToString();
        }

        private static string ShortCorrelation(string correlation)
        {
            string value = correlation ?? string.Empty;
            int dash = value.LastIndexOf('-');
            if (dash >= 0 && dash + 1 < value.Length) value = value.Substring(dash + 1);
            return value.Length <= 16 ? value : value.Substring(value.Length - 16);
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string Decode(string value) =>
            StrictUtf8.GetString(Convert.FromBase64String(value));

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool IsSimpleFileName(string value) =>
            !string.IsNullOrWhiteSpace(value) && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!(current >= '0' && current <= '9') &&
                    !(current >= 'a' && current <= 'f') &&
                    !(current >= 'A' && current <= 'F')) return false;
            }
            return true;
        }

        private readonly struct SourceState
        {
            internal SourceState(string path, string logicalName, BackupFileMetadata metadata)
            {
                Path = path;
                LogicalName = logicalName;
                Metadata = metadata;
            }
            internal string Path { get; }
            internal string LogicalName { get; }
            internal BackupFileMetadata Metadata { get; }
        }

        private readonly struct DirectorySourceState
        {
            internal DirectorySourceState(string path, IReadOnlyList<string> members)
            {
                Path = path;
                Members = members;
            }
            internal string Path { get; }
            internal IReadOnlyList<string> Members { get; }
        }

        private sealed class BackupSourceChangedException : IOException
        {
        }

        private readonly struct BackupDirectory
        {
            internal BackupDirectory(string path, DateTime createdUtc)
            {
                Path = path;
                CreatedUtc = createdUtc;
            }
            internal string Path { get; }
            internal DateTime CreatedUtc { get; }
        }

        private sealed class RootLease : IDisposable
        {
            private string _root;

            internal RootLease(string root)
            {
                _root = root;
            }

            public void Dispose()
            {
                string root = Interlocked.Exchange(ref _root, null);
                ReleaseRoot(root);
            }
        }
    }

    internal readonly struct BackupFileMetadata
    {
        internal BackupFileMetadata(long length, DateTime lastWriteUtc, bool isReparsePoint)
        {
            Length = length;
            LastWriteUtc = lastWriteUtc;
            IsReparsePoint = isReparsePoint;
        }
        internal long Length { get; }
        internal DateTime LastWriteUtc { get; }
        internal bool IsReparsePoint { get; }
        internal bool StableEquals(BackupFileMetadata other) =>
            Length == other.Length && LastWriteUtc == other.LastWriteUtc &&
            IsReparsePoint == other.IsReparsePoint;
    }

    internal readonly struct MigrationBackupDefaults
    {
        internal MigrationBackupDefaults(
            string destinationRoot,
            int retentionCount,
            int maximumFiles,
            long maximumTotalBytes)
        {
            DestinationRoot = destinationRoot;
            RetentionCount = retentionCount;
            MaximumFiles = maximumFiles;
            MaximumTotalBytes = maximumTotalBytes;
        }
        internal string DestinationRoot { get; }
        internal int RetentionCount { get; }
        internal int MaximumFiles { get; }
        internal long MaximumTotalBytes { get; }
    }

    internal interface IBackupStorage
    {
        string GetFullPath(string path);
        string Combine(string left, string right);
        string GetFileName(string path);
        bool FileExists(string path);
        bool DirectoryExists(string path);
        BackupFileMetadata GetFileMetadata(string path);
        Stream OpenRead(string path);
        Stream CreateNew(string path);
        void CreateDirectory(string path);
        void MoveDirectory(string source, string destination);
        void DeleteDirectory(string path, bool recursive);
        string[] GetDirectories(string path, string pattern);
        string[] GetFiles(string path, string pattern);
        DateTime GetDirectoryCreationUtc(string path);
        bool IsDirectoryReparsePoint(string path);
        void WriteAllTextNew(string path, string contents);
        string ReadAllText(string path);
        string[] ReadAllLines(string path);
    }

    internal sealed class PhysicalBackupStorage : IBackupStorage
    {
        public string GetFullPath(string path) => Path.GetFullPath(path);
        public string Combine(string left, string right) => Path.Combine(left, right);
        public string GetFileName(string path) => Path.GetFileName(path);
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public BackupFileMetadata GetFileMetadata(string path)
        {
            var info = new FileInfo(path);
            return new BackupFileMetadata(
                info.Length,
                info.LastWriteTimeUtc,
                (info.Attributes & FileAttributes.ReparsePoint) != 0);
        }
        public Stream OpenRead(string path) => new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        public Stream CreateNew(string path) => new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public string[] GetDirectories(string path, string pattern) => Directory.GetDirectories(path, pattern);
        public string[] GetFiles(string path, string pattern) => Directory.GetFiles(path, pattern);
        public DateTime GetDirectoryCreationUtc(string path) => Directory.GetCreationTimeUtc(path);
        public bool IsDirectoryReparsePoint(string path) =>
            (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        public void WriteAllTextNew(string path, string contents)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(contents ?? string.Empty);
            using (var stream = new FileStream(
                       path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);
        public string[] ReadAllLines(string path) => File.ReadAllLines(path, Encoding.UTF8);
    }
}
