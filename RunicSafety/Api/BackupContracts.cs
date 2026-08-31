using System;
using System.Collections.Generic;
using System.Threading;

namespace RunicSafety.Api
{
    public sealed class MigrationBackupSource
    {
        public MigrationBackupSource(string sourcePath, string logicalName)
        {
            SourcePath = sourcePath ?? string.Empty;
            LogicalName = logicalName ?? string.Empty;
        }

        public string SourcePath { get; }
        public string LogicalName { get; }
    }

    public sealed class MigrationBackupRequest
    {
        public MigrationBackupRequest(
            string migrationId,
            IEnumerable<MigrationBackupSource> sources,
            string destinationRoot,
            int retentionCount,
            long maximumTotalBytes,
            int maximumFiles)
        {
            MigrationId = migrationId ?? string.Empty;
            Sources = sources == null
                ? Array.Empty<MigrationBackupSource>()
                : new List<MigrationBackupSource>(sources).AsReadOnly();
            DestinationRoot = destinationRoot ?? string.Empty;
            RetentionCount = retentionCount;
            MaximumTotalBytes = maximumTotalBytes;
            MaximumFiles = maximumFiles;
        }

        public string MigrationId { get; }
        public IReadOnlyList<MigrationBackupSource> Sources { get; }
        public string DestinationRoot { get; }
        public int RetentionCount { get; }
        public long MaximumTotalBytes { get; }
        public int MaximumFiles { get; }
    }

    public enum MigrationBackupOutcome
    {
        Succeeded = 0,
        InvalidRequest = 1,
        SourceMissing = 2,
        SourceChangedDuringCopy = 3,
        SizeLimitExceeded = 4,
        FileLimitExceeded = 5,
        IoFailure = 6,
        Cancelled = 7
    }

    public sealed class MigrationBackupFile
    {
        internal MigrationBackupFile(
            string logicalName,
            string originalPath,
            string backupFileName,
            long length,
            string sha256)
        {
            LogicalName = logicalName;
            OriginalPath = originalPath;
            BackupFileName = backupFileName;
            Length = length;
            Sha256 = sha256;
        }

        public string LogicalName { get; }
        public string OriginalPath { get; }
        public string BackupFileName { get; }
        public long Length { get; }
        public string Sha256 { get; }
    }

    public sealed class MigrationBackupResult
    {
        internal MigrationBackupResult(
            MigrationBackupOutcome outcome,
            string backupDirectory,
            string correlationId,
            string failureCode,
            IReadOnlyList<MigrationBackupFile> files)
        {
            Outcome = outcome;
            BackupDirectory = backupDirectory ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            FailureCode = failureCode ?? string.Empty;
            Files = files ?? Array.Empty<MigrationBackupFile>();
        }

        public MigrationBackupOutcome Outcome { get; }
        public string BackupDirectory { get; }
        public string CorrelationId { get; }
        public string FailureCode { get; }
        public IReadOnlyList<MigrationBackupFile> Files { get; }
        public bool Succeeded => Outcome == MigrationBackupOutcome.Succeeded;
    }

    public sealed class MigrationExecutionResult
    {
        internal MigrationExecutionResult(MigrationBackupResult backup, bool mutationInvoked, Exception mutationFailure)
        {
            Backup = backup;
            MutationInvoked = mutationInvoked;
            MutationFailure = mutationFailure;
        }

        public MigrationBackupResult Backup { get; }
        public bool MutationInvoked { get; }
        public Exception MutationFailure { get; }
        public bool Succeeded => Backup != null && Backup.Succeeded && MutationInvoked && MutationFailure == null;
    }

    public interface IMigrationBackupService
    {
        string DefaultDestinationRoot { get; }
        int DefaultRetentionCount { get; }
        int DefaultMaximumFiles { get; }
        long DefaultMaximumTotalBytes { get; }
        MigrationBackupRequest CreateDefaultRequest(
            string migrationId,
            IEnumerable<MigrationBackupSource> sources);
        MigrationBackupResult CreateBackup(MigrationBackupRequest request, CancellationToken cancellationToken);
        MigrationExecutionResult ExecuteAfterBackup(
            MigrationBackupRequest request,
            Action mutation,
            CancellationToken cancellationToken);
        bool ValidateBackup(string backupDirectory, out string failureCode);
    }
}
