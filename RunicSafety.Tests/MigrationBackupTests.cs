using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class MigrationBackupTests
    {
        internal static void Register()
        {
            TestRunner.Run("migration backup commits and validates", CommitsAndValidates);
            TestRunner.Run("migration backup default request exposes bounded defaults", DefaultRequestBounded);
            TestRunner.Run("migration backup preserves source bytes", PreservesSourceBytes);
            TestRunner.Run("migration backup records SHA-256", RecordsSha256);
            TestRunner.Run("migration backup writes restore instructions", WritesRestoreInstructions);
            TestRunner.Run("migration backup manifest does not expose raw source path", ManifestEncodesPath);
            TestRunner.Run("migration backup missing source aborts", MissingSourceAborts);
            TestRunner.Run("migration backup duplicate source aborts", DuplicateSourceAborts);
            TestRunner.Run("migration backup expands a chunked world directory recursively", DirectorySourceBacksUpTree);
            TestRunner.Run("migration backup applies the file cap to expanded directory members", DirectorySourceHonorsFileLimit);
            TestRunner.Run("migration backup rejects a chunk directory that changes during copy", DirectorySourceChangeAborts);
            TestRunner.Run("migration backup rejects an earlier chunk changed while later chunks copy", DirectoryMemberChangesAfterOwnCopyAborts);
            TestRunner.Run("migration backup refuses reparse chunk directories", DirectorySourceReparseAborts);
            TestRunner.Run("migration backup file limit aborts", FileLimitAborts);
            TestRunner.Run("migration backup size limit aborts", SizeLimitAborts);
            TestRunner.Run("migration backup cancellation aborts", CancellationAborts);
            TestRunner.Run("migration backup source change aborts", SourceChangeAborts);
            TestRunner.Run("migration backup write fault aborts", WriteFaultAborts);
            TestRunner.Run("migration backup atomic move fault leaves no commit", MoveFaultLeavesNoCommit);
            TestRunner.Run("migration backup post-commit validation failure aborts mutation", PostCommitValidationFailureAborts);
            TestRunner.Run("migration backup reparse root fails closed", ReparseRootFailsClosed);
            TestRunner.Run("migration backup failure never invokes mutation", FailureNeverInvokesMutation);
            TestRunner.Run("migration backup success invokes mutation once", SuccessInvokesMutationOnce);
            TestRunner.Run("migration backup rejects a conflicting active root before mutation", ConcurrentRootRejectedBeforeMutation);
            TestRunner.Run("migration backup revalidates committed bytes immediately before mutation", PreMutationRevalidationAborts);
            TestRunner.Run("migration backup rejects a tampered manifest subset before mutation", ManifestSubsetAbortsMutation);
            TestRunner.Run("migration backup files remain write-protected through mutation callback", BackupProtectedThroughMutation);
            TestRunner.Run("migration backup cancellation after commit still aborts mutation", CancellationAfterCommitAbortsMutation);
            TestRunner.Run("migration mutation exception preserves backup", MutationExceptionPreservesBackup);
            TestRunner.Run("migration backup validation catches corrupted file", CorruptFileFailsValidation);
            TestRunner.Run("migration backup validation catches corrupt manifest", CorruptManifestFailsValidation);
            TestRunner.Run("migration backup validation rejects incomplete directory", IncompleteBackupRejected);
            TestRunner.Run("migration backup retention is bounded", RetentionBounded);
            TestRunner.Run("migration backup retention ignores unmarked directories", RetentionIgnoresUnmarked);
            TestRunner.Run("migration backup retention never deletes a source backup", RetentionNeverDeletesSource);
            TestRunner.Run("migration backup partial directories are cleaned on failure", PartialsCleaned);
            TestRunner.Run("migration backup diagnostics do not contain source paths", DiagnosticsHidePaths);
            TestRunner.Run("migration backup rejects invalid retention", InvalidRetentionRejected);
            TestRunner.Run("migration backup rejects empty source list", EmptySourcesRejected);
        }

        private static MigrationBackupRequest Request(
            TempScope temp,
            IEnumerable<MigrationBackupSource> sources = null,
            int retention = 5,
            int maxFiles = 32,
            long maxBytes = 1024 * 1024) =>
            new MigrationBackupRequest(
                "inventory-topology-1",
                sources ?? new[] { new MigrationBackupSource(temp.Source, "character") },
                temp.Backups,
                retention,
                maxBytes,
                maxFiles);

        private static MigrationBackupService Service(
            ISafetyDiagnosticService diagnostics = null,
            IBackupStorage storage = null,
            Func<DateTime> clock = null) =>
            new MigrationBackupService(
                storage ?? new PhysicalBackupStorage(),
                diagnostics ?? new CorrelatedDiagnosticBuffer(),
                clock ?? (() => new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc)));

        private static void CommitsAndValidates()
        {
            using var temp = new TempScope();
            MigrationBackupService service = Service();
            MigrationBackupResult result = service.CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.True(result.Succeeded, result.FailureCode);
            TestAssert.True(Directory.Exists(result.BackupDirectory));
            TestAssert.True(service.ValidateBackup(result.BackupDirectory, out string failure), failure);
        }

        private static void DefaultRequestBounded()
        {
            using var temp = new TempScope();
            var defaults = new MigrationBackupDefaults(temp.Backups, 7, 9, 123456);
            MigrationBackupService service = new MigrationBackupService(
                new PhysicalBackupStorage(),
                new CorrelatedDiagnosticBuffer(),
                () => DateTime.UtcNow,
                () => defaults);
            MigrationBackupRequest request = service.CreateDefaultRequest(
                "default-test", new[] { new MigrationBackupSource(temp.Source, "character") });
            TestAssert.Equal(temp.Backups, service.DefaultDestinationRoot);
            TestAssert.Equal(7, request.RetentionCount);
            TestAssert.Equal(9, request.MaximumFiles);
            TestAssert.Equal(123456L, request.MaximumTotalBytes);
        }

        private static void PreservesSourceBytes()
        {
            using var temp = new TempScope();
            byte[] before = File.ReadAllBytes(temp.Source);
            DateTime write = File.GetLastWriteTimeUtc(temp.Source);
            MigrationBackupResult result = Service().CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.True(result.Succeeded);
            TestAssert.SequenceEqual(before, File.ReadAllBytes(temp.Source));
            TestAssert.Equal(write, File.GetLastWriteTimeUtc(temp.Source));
        }

        private static void RecordsSha256()
        {
            using var temp = new TempScope();
            MigrationBackupResult result = Service().CreateBackup(Request(temp), CancellationToken.None);
            using SHA256 sha = SHA256.Create();
            string expected = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(temp.Source))).ToLowerInvariant();
            TestAssert.Equal(expected, result.Files[0].Sha256);
            TestAssert.Equal(64, result.Files[0].Sha256.Length);
        }

        private static void WritesRestoreInstructions()
        {
            using var temp = new TempScope();
            MigrationBackupResult result = Service().CreateBackup(Request(temp), CancellationToken.None);
            string instructions = File.ReadAllText(Path.Combine(result.BackupDirectory,
                MigrationBackupService.RestoreFileName));
            TestAssert.Contains("Stop Valheim", instructions);
            TestAssert.Contains("does not auto-restore", instructions);
        }

        private static void ManifestEncodesPath()
        {
            using var temp = new TempScope();
            MigrationBackupResult result = Service().CreateBackup(Request(temp), CancellationToken.None);
            string manifest = File.ReadAllText(Path.Combine(result.BackupDirectory,
                MigrationBackupService.ManifestFileName));
            TestAssert.False(manifest.Contains(temp.Source, StringComparison.OrdinalIgnoreCase));
            TestAssert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(temp.Source)), manifest);
        }

        private static void MissingSourceAborts()
        {
            using var temp = new TempScope();
            var sources = new[] { new MigrationBackupSource(Path.Combine(temp.Root, "missing.fch"), "missing") };
            MigrationBackupResult result = Service().CreateBackup(Request(temp, sources), CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.SourceMissing, result.Outcome);
            TestAssert.False(result.Succeeded);
        }

        private static void DuplicateSourceAborts()
        {
            using var temp = new TempScope();
            var sources = new[]
            {
                new MigrationBackupSource(temp.Source, "one"),
                new MigrationBackupSource(temp.Source, "two")
            };
            TestAssert.Equal(MigrationBackupOutcome.InvalidRequest,
                Service().CreateBackup(Request(temp, sources), CancellationToken.None).Outcome);
        }

        private static void DirectorySourceBacksUpTree()
        {
            using var temp = new TempScope();
            string world = Path.Combine(temp.Root, "worlds_local", "RunicWorld");
            string chunks = Path.Combine(world, "chunks");
            Directory.CreateDirectory(chunks);
            string metadata = Path.Combine(world, "meta.fwl");
            string chunk = Path.Combine(chunks, "0_0.chunk");
            File.WriteAllBytes(metadata, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(chunk, new byte[] { 4, 5, 6, 7 });

            MigrationBackupResult result = Service().CreateBackup(
                Request(temp, new[] { new MigrationBackupSource(world, "world") }, maxFiles: 8),
                CancellationToken.None);

            TestAssert.True(result.Succeeded, result.FailureCode);
            TestAssert.Equal(2, result.Files.Count);
            MigrationBackupFile metadataBackup = result.Files.Single(file =>
                string.Equals(file.OriginalPath, Path.GetFullPath(metadata), StringComparison.OrdinalIgnoreCase));
            MigrationBackupFile chunkBackup = result.Files.Single(file =>
                string.Equals(file.OriginalPath, Path.GetFullPath(chunk), StringComparison.OrdinalIgnoreCase));
            TestAssert.True(metadataBackup.LogicalName.StartsWith("world/", StringComparison.Ordinal));
            TestAssert.True(chunkBackup.LogicalName.StartsWith("world/", StringComparison.Ordinal));
            TestAssert.SequenceEqual(File.ReadAllBytes(metadata),
                File.ReadAllBytes(Path.Combine(result.BackupDirectory, metadataBackup.BackupFileName)));
            TestAssert.SequenceEqual(File.ReadAllBytes(chunk),
                File.ReadAllBytes(Path.Combine(result.BackupDirectory, chunkBackup.BackupFileName)));
        }

        private static void DirectorySourceHonorsFileLimit()
        {
            using var temp = new TempScope();
            string world = Path.Combine(temp.Root, "world");
            Directory.CreateDirectory(world);
            File.WriteAllText(Path.Combine(world, "one.chunk"), "one");
            File.WriteAllText(Path.Combine(world, "two.chunk"), "two");
            MigrationBackupResult result = Service().CreateBackup(
                Request(temp, new[] { new MigrationBackupSource(world, "world") }, maxFiles: 1),
                CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.FileLimitExceeded, result.Outcome);
            TestAssert.Equal("configured-file-limit", result.FailureCode);
        }

        private static void DirectorySourceChangeAborts()
        {
            using var temp = new TempScope();
            string world = Path.Combine(temp.Root, "world");
            Directory.CreateDirectory(world);
            string first = Path.Combine(world, "one.chunk");
            File.WriteAllText(first, "one");
            var storage = new FaultStorage(first);
            bool changed = false;
            storage.OnTrackedSourceOpen = () =>
            {
                if (changed) return;
                changed = true;
                File.WriteAllText(Path.Combine(world, "two.chunk"), "two");
            };
            MigrationBackupResult result = Service(storage: storage).CreateBackup(
                Request(temp, new[] { new MigrationBackupSource(world, "world") }, maxFiles: 8),
                CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.SourceChangedDuringCopy, result.Outcome);
            TestAssert.Equal("source-directory-changed-during-copy", result.FailureCode);
        }

        private static void DirectorySourceReparseAborts()
        {
            using var temp = new TempScope();
            string world = Path.Combine(temp.Root, "world");
            Directory.CreateDirectory(world);
            File.WriteAllText(Path.Combine(world, "one.chunk"), "one");
            var storage = new FaultStorage(temp.Source) { PretendBackupRootIsReparse = world };
            MigrationBackupResult result = Service(storage: storage).CreateBackup(
                Request(temp, new[] { new MigrationBackupSource(world, "world") }),
                CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.InvalidRequest, result.Outcome);
            TestAssert.Equal("reparse-source-directory-refused", result.FailureCode);
        }

        private static void DirectoryMemberChangesAfterOwnCopyAborts()
        {
            using var temp = new TempScope();
            string world = Path.Combine(temp.Root, "world");
            Directory.CreateDirectory(world);
            string first = Path.Combine(world, "one.chunk");
            string second = Path.Combine(world, "two.chunk");
            File.WriteAllText(first, "one");
            File.WriteAllText(second, "two");
            var storage = new FaultStorage(second);
            bool changed = false;
            storage.OnTrackedSourceOpen = () =>
            {
                if (changed) return;
                changed = true;
                File.AppendAllText(first, "-changed-after-copy");
            };

            MigrationBackupResult result = Service(storage: storage).CreateBackup(
                Request(temp, new[] { new MigrationBackupSource(world, "world") }, maxFiles: 8),
                CancellationToken.None);

            TestAssert.Equal(MigrationBackupOutcome.SourceChangedDuringCopy, result.Outcome);
            TestAssert.Equal("source-changed-during-copy", result.FailureCode);
        }

        private static void FileLimitAborts()
        {
            using var temp = new TempScope();
            string second = Path.Combine(temp.Root, "world.db");
            File.WriteAllText(second, "world");
            var sources = new[]
            {
                new MigrationBackupSource(temp.Source, "one"),
                new MigrationBackupSource(second, "two")
            };
            TestAssert.Equal(MigrationBackupOutcome.FileLimitExceeded,
                Service().CreateBackup(Request(temp, sources, maxFiles: 1), CancellationToken.None).Outcome);
        }

        private static void SizeLimitAborts()
        {
            using var temp = new TempScope();
            TestAssert.Equal(MigrationBackupOutcome.SizeLimitExceeded,
                Service().CreateBackup(Request(temp, maxBytes: 1), CancellationToken.None).Outcome);
        }

        private static void CancellationAborts()
        {
            using var temp = new TempScope();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            TestAssert.Equal(MigrationBackupOutcome.Cancelled,
                Service().CreateBackup(Request(temp), cancellation.Token).Outcome);
        }

        private static void SourceChangeAborts()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ChangeSourceMetadataAfterFirstRead = true };
            MigrationBackupResult result = Service(storage: storage)
                .CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.SourceChangedDuringCopy, result.Outcome);
        }

        private static void WriteFaultAborts()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ThrowOnCreateNew = true };
            TestAssert.Equal(MigrationBackupOutcome.IoFailure,
                Service(storage: storage).CreateBackup(Request(temp), CancellationToken.None).Outcome);
        }

        private static void MoveFaultLeavesNoCommit()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ThrowOnMove = true };
            MigrationBackupResult result = Service(storage: storage)
                .CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.IoFailure, result.Outcome);
            TestAssert.Equal(0, Directory.Exists(temp.Backups)
                ? Directory.GetDirectories(temp.Backups, "backup-*").Length
                : 0);
        }

        private static void PostCommitValidationFailureAborts()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ThrowOnManifestRead = true };
            bool invoked = false;
            MigrationExecutionResult result = Service(storage: storage).ExecuteAfterBackup(
                Request(temp), () => invoked = true, CancellationToken.None);
            TestAssert.False(invoked);
            TestAssert.False(result.MutationInvoked);
            TestAssert.Equal(MigrationBackupOutcome.IoFailure, result.Backup.Outcome);
            TestAssert.Contains("post-commit-validation-failed", result.Backup.FailureCode);
            TestAssert.True(Directory.Exists(result.Backup.BackupDirectory));
        }

        private static void ReparseRootFailsClosed()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { PretendBackupRootIsReparse = temp.Backups };
            MigrationBackupResult result = Service(storage: storage)
                .CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.Equal(MigrationBackupOutcome.InvalidRequest, result.Outcome);
            TestAssert.Equal("reparse-backup-root-refused", result.FailureCode);
        }

        private static void FailureNeverInvokesMutation()
        {
            using var temp = new TempScope();
            bool invoked = false;
            var missing = new[] { new MigrationBackupSource(Path.Combine(temp.Root, "missing"), "missing") };
            MigrationExecutionResult result = Service().ExecuteAfterBackup(
                Request(temp, missing), () => invoked = true, CancellationToken.None);
            TestAssert.False(invoked);
            TestAssert.False(result.MutationInvoked);
        }

        private static void SuccessInvokesMutationOnce()
        {
            using var temp = new TempScope();
            int calls = 0;
            MigrationExecutionResult result = Service().ExecuteAfterBackup(
                Request(temp), () => calls++, CancellationToken.None);
            TestAssert.True(result.Succeeded);
            TestAssert.Equal(1, calls);
        }

        private static void ConcurrentRootRejectedBeforeMutation()
        {
            using var temp = new TempScope();
            using var mutationEntered = new ManualResetEventSlim(false);
            using var releaseMutation = new ManualResetEventSlim(false);
            MigrationBackupService service = Service();
            Task<MigrationExecutionResult> first = Task.Run(() => service.ExecuteAfterBackup(
                Request(temp, retention: 1),
                () =>
                {
                    mutationEntered.Set();
                    if (!releaseMutation.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("test release was not signaled");
                },
                CancellationToken.None));
            TestAssert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)),
                "The first migration never reached its protected callback.");

            bool secondInvoked = false;
            MigrationExecutionResult second = service.ExecuteAfterBackup(
                Request(temp, retention: 1), () => secondInvoked = true, CancellationToken.None);
            TestAssert.False(secondInvoked);
            TestAssert.False(second.MutationInvoked);
            TestAssert.Equal(MigrationBackupOutcome.IoFailure, second.Backup.Outcome);
            TestAssert.Equal("backup-root-busy", second.Backup.FailureCode);
            TestAssert.Equal(1, Directory.GetDirectories(temp.Backups, "backup-*").Length);

            releaseMutation.Set();
            TestAssert.True(first.GetAwaiter().GetResult().Succeeded);
        }

        private static void PreMutationRevalidationAborts()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ThrowOnManifestReadNumber = 2 };
            bool invoked = false;
            MigrationExecutionResult result = Service(storage: storage).ExecuteAfterBackup(
                Request(temp), () => invoked = true, CancellationToken.None);
            TestAssert.False(invoked);
            TestAssert.False(result.MutationInvoked);
            TestAssert.Equal(MigrationBackupOutcome.IoFailure, result.Backup.Outcome);
            TestAssert.Contains("pre-mutation-validation", result.Backup.FailureCode);
            TestAssert.True(Directory.Exists(result.Backup.BackupDirectory));
        }

        private static void ManifestSubsetAbortsMutation()
        {
            using var temp = new TempScope();
            string second = Path.Combine(temp.Root, "world.db");
            File.WriteAllText(second, "world-state");
            var sources = new[]
            {
                new MigrationBackupSource(temp.Source, "character"),
                new MigrationBackupSource(second, "world")
            };
            var storage = new FaultStorage(temp.Source)
            {
                OnManifestOpen = (openNumber, path) =>
                {
                    if (openNumber != 2) return;
                    string[] lines = File.ReadAllLines(path);
                    File.WriteAllLines(path, lines.Where(line =>
                        !line.StartsWith("FILE\t", StringComparison.Ordinal) ||
                        line.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("0000.fch")),
                            StringComparison.Ordinal)));
                }
            };
            bool invoked = false;
            MigrationExecutionResult result = Service(storage: storage).ExecuteAfterBackup(
                Request(temp, sources), () => invoked = true, CancellationToken.None);
            TestAssert.False(invoked);
            TestAssert.False(result.MutationInvoked);
            TestAssert.Equal(MigrationBackupOutcome.IoFailure, result.Backup.Outcome);
            TestAssert.Contains("backup-file-set-changed", result.Backup.FailureCode);
            TestAssert.Equal(2, result.Backup.Files.Count);
        }

        private static void BackupProtectedThroughMutation()
        {
            using var temp = new TempScope();
            bool writeBlocked = false;
            MigrationExecutionResult result = Service().ExecuteAfterBackup(
                Request(temp),
                () =>
                {
                    string backup = Directory.GetDirectories(temp.Backups, "backup-*").Single();
                    string data = Directory.GetFiles(backup, "0000.*").Single();
                    try { File.AppendAllText(data, "tamper"); }
                    catch (IOException) { writeBlocked = true; }
                },
                CancellationToken.None);
            TestAssert.True(result.Succeeded);
            TestAssert.True(writeBlocked, "The committed backup data file was writable during mutation.");
            TestAssert.True(Service().ValidateBackup(result.Backup.BackupDirectory, out string failure), failure);
        }

        private static void CancellationAfterCommitAbortsMutation()
        {
            using var temp = new TempScope();
            using var cancellation = new CancellationTokenSource();
            var storage = new FaultStorage(temp.Source)
            {
                OnMarkerProtectionOpen = cancellation.Cancel
            };
            bool invoked = false;
            MigrationExecutionResult result = Service(storage: storage).ExecuteAfterBackup(
                Request(temp), () => invoked = true, cancellation.Token);
            TestAssert.False(invoked);
            TestAssert.False(result.MutationInvoked);
            TestAssert.Equal(MigrationBackupOutcome.Cancelled, result.Backup.Outcome);
            TestAssert.Equal("cancelled-before-mutation", result.Backup.FailureCode);
            TestAssert.True(Directory.Exists(result.Backup.BackupDirectory));
        }

        private static void MutationExceptionPreservesBackup()
        {
            using var temp = new TempScope();
            MigrationBackupService service = Service();
            MigrationExecutionResult result = service.ExecuteAfterBackup(
                Request(temp), () => throw new InvalidOperationException("migration fault"), CancellationToken.None);
            TestAssert.True(result.MutationInvoked);
            TestAssert.NotNull(result.MutationFailure);
            TestAssert.True(service.ValidateBackup(result.Backup.BackupDirectory, out string failure), failure);
        }

        private static void CorruptFileFailsValidation()
        {
            using var temp = new TempScope();
            MigrationBackupService service = Service();
            MigrationBackupResult result = service.CreateBackup(Request(temp), CancellationToken.None);
            File.AppendAllText(Path.Combine(result.BackupDirectory, result.Files[0].BackupFileName), "corrupt");
            TestAssert.False(service.ValidateBackup(result.BackupDirectory, out string failure));
            TestAssert.Equal("backup-file-missing-or-sized-wrong", failure);
        }

        private static void CorruptManifestFailsValidation()
        {
            using var temp = new TempScope();
            MigrationBackupService service = Service();
            MigrationBackupResult result = service.CreateBackup(Request(temp), CancellationToken.None);
            File.WriteAllText(Path.Combine(result.BackupDirectory, MigrationBackupService.ManifestFileName), "bad");
            TestAssert.False(service.ValidateBackup(result.BackupDirectory, out string failure));
            TestAssert.Equal("manifest-invalid", failure);
        }

        private static void IncompleteBackupRejected()
        {
            using var temp = new TempScope();
            Directory.CreateDirectory(temp.Backups);
            string incomplete = Path.Combine(temp.Backups, "backup-incomplete");
            Directory.CreateDirectory(incomplete);
            TestAssert.False(Service().ValidateBackup(incomplete, out string failure));
            TestAssert.Equal("backup-incomplete", failure);
        }

        private static void RetentionBounded()
        {
            using var temp = new TempScope();
            int second = 0;
            MigrationBackupService service = Service(clock: () =>
                new DateTime(2026, 8, 22, 12, 0, second++, DateTimeKind.Utc));
            for (int index = 0; index < 5; index++)
                TestAssert.True(service.CreateBackup(Request(temp, retention: 2), CancellationToken.None).Succeeded);
            TestAssert.Equal(2, Directory.GetDirectories(temp.Backups, "backup-*")
                .Count(path => File.Exists(Path.Combine(path, MigrationBackupService.MarkerFileName))));
        }

        private static void RetentionIgnoresUnmarked()
        {
            using var temp = new TempScope();
            Directory.CreateDirectory(temp.Backups);
            string userDirectory = Path.Combine(temp.Backups, "backup-do-not-delete");
            Directory.CreateDirectory(userDirectory);
            File.WriteAllText(Path.Combine(userDirectory, "user.txt"), "keep");
            MigrationBackupService service = Service();
            TestAssert.True(service.CreateBackup(Request(temp, retention: 1), CancellationToken.None).Succeeded);
            TestAssert.True(Directory.Exists(userDirectory));
        }

        private static void RetentionNeverDeletesSource()
        {
            using var temp = new TempScope();
            Directory.CreateDirectory(temp.Backups);
            string sourceBackup = Path.Combine(temp.Backups, "backup-source");
            Directory.CreateDirectory(sourceBackup);
            string nestedSource = Path.Combine(sourceBackup, "original.fch");
            File.WriteAllText(nestedSource, "only-source-copy");
            File.WriteAllText(Path.Combine(sourceBackup, MigrationBackupService.MarkerFileName),
                "RUNIC_SAFETY_BACKUP_V1\n");
            Directory.SetCreationTimeUtc(sourceBackup, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var sources = new[] { new MigrationBackupSource(nestedSource, "nested-source") };
            MigrationBackupResult result = Service().CreateBackup(
                Request(temp, sources, retention: 1), CancellationToken.None);
            TestAssert.True(result.Succeeded, result.FailureCode);
            TestAssert.True(File.Exists(nestedSource), "Retention deleted a source file.");
            TestAssert.Equal("only-source-copy", File.ReadAllText(nestedSource));
        }

        private static void PartialsCleaned()
        {
            using var temp = new TempScope();
            var storage = new FaultStorage(temp.Source) { ThrowOnMove = true };
            Service(storage: storage).CreateBackup(Request(temp), CancellationToken.None);
            TestAssert.Equal(0, Directory.Exists(temp.Backups)
                ? Directory.GetDirectories(temp.Backups, ".partial-*").Length
                : 0);
        }

        private static void DiagnosticsHidePaths()
        {
            using var temp = new TempScope();
            var diagnostics = new CorrelatedDiagnosticBuffer();
            var missing = new[] { new MigrationBackupSource(Path.Combine(temp.Root, "secret-world.db"), "secret") };
            Service(diagnostics).CreateBackup(Request(temp, missing), CancellationToken.None);
            foreach (SafetyDiagnosticEvent entry in diagnostics.Snapshot())
            {
                TestAssert.False(entry.Code.Contains(temp.Root, StringComparison.OrdinalIgnoreCase));
                TestAssert.False(entry.Category.Contains(temp.Root, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static void InvalidRetentionRejected()
        {
            using var temp = new TempScope();
            TestAssert.Equal(MigrationBackupOutcome.InvalidRequest,
                Service().CreateBackup(Request(temp, retention: 0), CancellationToken.None).Outcome);
        }

        private static void EmptySourcesRejected()
        {
            using var temp = new TempScope();
            TestAssert.Equal(MigrationBackupOutcome.InvalidRequest,
                Service().CreateBackup(Request(temp, Array.Empty<MigrationBackupSource>()), CancellationToken.None).Outcome);
        }

        private sealed class TempScope : IDisposable
        {
            internal TempScope()
            {
                Root = Path.Combine(Path.GetTempPath(), "RunicSafetyTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                Source = Path.Combine(Root, "character.fch");
                Backups = Path.Combine(Root, "backups");
                File.WriteAllBytes(Source, Encoding.UTF8.GetBytes("character-state-αβγ"));
            }
            internal string Root { get; }
            internal string Source { get; }
            internal string Backups { get; }
            public void Dispose()
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }

        private sealed class FaultStorage : IBackupStorage
        {
            private readonly PhysicalBackupStorage _inner = new PhysicalBackupStorage();
            private readonly string _trackedSource;
            private int _sourceMetadataReads;
            internal FaultStorage(string trackedSource) { _trackedSource = Path.GetFullPath(trackedSource); }
            internal bool ChangeSourceMetadataAfterFirstRead { get; set; }
            internal bool ThrowOnCreateNew { get; set; }
            internal bool ThrowOnMove { get; set; }
            internal bool ThrowOnManifestRead { get; set; }
            internal int ThrowOnManifestReadNumber { get; set; }
            internal Action OnMarkerProtectionOpen { get; set; }
            internal Action<int, string> OnManifestOpen { get; set; }
            internal Action OnTrackedSourceOpen { get; set; }
            internal string PretendBackupRootIsReparse { get; set; }
            public string GetFullPath(string path) => _inner.GetFullPath(path);
            public string Combine(string left, string right) => _inner.Combine(left, right);
            public string GetFileName(string path) => _inner.GetFileName(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public BackupFileMetadata GetFileMetadata(string path)
            {
                BackupFileMetadata metadata = _inner.GetFileMetadata(path);
                if (ChangeSourceMetadataAfterFirstRead &&
                    string.Equals(Path.GetFullPath(path), _trackedSource, StringComparison.OrdinalIgnoreCase) &&
                    ++_sourceMetadataReads > 1)
                    return new BackupFileMetadata(metadata.Length, metadata.LastWriteUtc.AddTicks(1), metadata.IsReparsePoint);
                return metadata;
            }
            public Stream OpenRead(string path)
            {
                if (string.Equals(Path.GetFullPath(path), _trackedSource, StringComparison.OrdinalIgnoreCase))
                    OnTrackedSourceOpen?.Invoke();
                if (string.Equals(Path.GetFileName(path), MigrationBackupService.MarkerFileName,
                        StringComparison.Ordinal))
                    OnMarkerProtectionOpen?.Invoke();
                if (string.Equals(Path.GetFileName(path), MigrationBackupService.ManifestFileName,
                        StringComparison.Ordinal))
                {
                    int read = Interlocked.Increment(ref _manifestReads);
                    OnManifestOpen?.Invoke(read, path);
                    if (ThrowOnManifestRead || ThrowOnManifestReadNumber == read)
                        throw new IOException("fault injection");
                }
                return _inner.OpenRead(path);
            }
            public Stream CreateNew(string path)
            {
                if (ThrowOnCreateNew) throw new IOException("fault injection");
                return _inner.CreateNew(path);
            }
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public void MoveDirectory(string source, string destination)
            {
                if (ThrowOnMove) throw new IOException("fault injection");
                _inner.MoveDirectory(source, destination);
            }
            public void DeleteDirectory(string path, bool recursive) => _inner.DeleteDirectory(path, recursive);
            public string[] GetDirectories(string path, string pattern) => _inner.GetDirectories(path, pattern);
            public string[] GetFiles(string path, string pattern) => _inner.GetFiles(path, pattern);
            public DateTime GetDirectoryCreationUtc(string path) => _inner.GetDirectoryCreationUtc(path);
            public bool IsDirectoryReparsePoint(string path) =>
                !string.IsNullOrEmpty(PretendBackupRootIsReparse) &&
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(PretendBackupRootIsReparse),
                    StringComparison.OrdinalIgnoreCase) || _inner.IsDirectoryReparsePoint(path);
            public void WriteAllTextNew(string path, string contents) => _inner.WriteAllTextNew(path, contents);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public string[] ReadAllLines(string path)
                => _inner.ReadAllLines(path);

            private int _manifestReads;
        }
    }
}
