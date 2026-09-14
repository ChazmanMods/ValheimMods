using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using RunicSafety.Api;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    [HarmonyPatch(typeof(ZNet), "ServerLoadWorld")]
    internal static class SentinelTransitionBackup
    {
        private const int MaximumWorldBackupFiles = 1024;
        private static readonly object Gate = new object();
        private static SentinelRuntime _runtime;
        private static ManualLogSource _log;
        private static string _stateRoot;

        internal static void Attach(
            SentinelRuntime runtime,
            ManualLogSource log,
            string configRoot)
        {
            lock (Gate)
            {
                _runtime = runtime;
                _log = log;
                _stateRoot = Path.Combine(
                    Path.GetFullPath(configRoot),
                    "RunicSentinel",
                    "transitions");
            }
        }

        internal static void Detach()
        {
            lock (Gate)
            {
                _runtime = null;
                _log = null;
                _stateRoot = null;
            }
        }

        internal static string CreateVerifiedBackupNow(string reason)
        {
            SentinelRuntime runtime;
            string root;
            lock (Gate)
            {
                runtime = _runtime;
                root = _stateRoot;
            }
            ZNet network = ZNet.instance;
            World world = ZNet.World;
            if (runtime == null || network == null || !network.IsServer() || world == null)
                return "no-world-loaded";
            IMigrationBackupService backups = SafetyIntegrationApi.Backups;
            if (backups == null) throw new InvalidOperationException("runic-safety-backup-unavailable");
            var sources = new List<MigrationBackupSource>();
            var staged = new List<string>();
            string staging = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
            try
            {
                AddWorldBackupSources(world, staging, sources, staged);
                if (sources.Count == 0) throw new FileNotFoundException("world-backup-sources-missing");
                MigrationBackupRequest request = CreateWorldBackupRequest(
                    backups,
                    string.IsNullOrWhiteSpace(reason) ? "runic-sentinel-admin-backup" : reason,
                    sources);
                MigrationBackupResult result = backups.CreateBackup(request, CancellationToken.None);
                string validationFailure = string.Empty;
                if (result == null || !result.Succeeded ||
                    !backups.ValidateBackup(result.BackupDirectory, out validationFailure))
                    throw new IOException("verified-world-backup-failed-" +
                        (result?.FailureCode ?? validationFailure ?? "unknown"));
                return result.CorrelationId;
            }
            finally
            {
                foreach (string path in staged)
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                try
                {
                    if (Directory.Exists(staging) && Directory.GetFileSystemEntries(staging).Length == 0)
                        Directory.Delete(staging, false);
                }
                catch { }
            }
        }

        [HarmonyPrefix]
        private static void BeforeWorldLoad(ZNet __instance)
        {
            if (!(SentinelConfig.BackupBeforeTransitions?.Value ?? true) ||
                __instance == null || !__instance.IsServer()) return;
            SentinelRuntime runtime;
            ManualLogSource log;
            string root;
            lock (Gate)
            {
                runtime = _runtime;
                log = _log;
                root = _stateRoot;
            }
            World world = ZNet.World;
            if (runtime == null || world == null) return;
            if (!runtime.TryGetTransitionFingerprint(out string current))
            {
                // Optional is the safe first-run/profile-authoring mode: there may not be a signed
                // passport yet, so there is no authenticated transition fingerprint to compare.
                // Required mode must never load a world under an unverifiable profile.
                if (SentinelConfig.RemoteAdmissionMode != SentinelRemoteAdmissionMode.Required)
                    return;
                runtime.RecordAdmissionFailure("sentinel-transition-profile-unverified");
                throw new InvalidOperationException(
                    "Raven's Gate cannot verify the active profile before world load.");
            }
            string marker = Path.Combine(root, __instance.GetWorldUID().ToString() + ".state");
            string previous = ReadMarker(marker);
            if (string.Equals(previous, current, StringComparison.Ordinal)) return;

            IMigrationBackupService backups = SafetyIntegrationApi.Backups;
            if (backups == null)
            {
                runtime.RecordAdmissionFailure("sentinel-transition-backup-unavailable");
                throw new InvalidOperationException(
                    "Runic Safety backup service is required before this profile transition.");
            }
            var sources = new List<MigrationBackupSource>();
            var staged = new List<string>();
            string staging = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
            try
            {
                AddWorldBackupSources(world, staging, sources, staged);
                if (sources.Count == 0)
                    throw new FileNotFoundException(
                        "No existing world files were available for the required transition backup.");
                MigrationBackupRequest request = CreateWorldBackupRequest(
                    backups, "runic-sentinel-profile-transition", sources);
                MigrationBackupResult result = backups.CreateBackup(request, CancellationToken.None);
                string validationFailure = string.Empty;
                if (result == null || !result.Succeeded ||
                    !backups.ValidateBackup(result.BackupDirectory, out validationFailure))
                {
                    runtime.RecordAdmissionFailure("sentinel-transition-backup-failed");
                    throw new IOException(
                        "Required profile-transition backup failed: " +
                        (result?.FailureCode ?? validationFailure ?? "unknown"));
                }
                WriteMarker(marker, current);
                log?.LogWarning(
                    "Raven's Gate created and verified a world backup before applying a new " +
                    "modpack or policy profile. Correlation: " + result.CorrelationId + ".");
            }
            finally
            {
                foreach (string path in staged)
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                try
                {
                    if (Directory.Exists(staging) &&
                        Directory.GetFileSystemEntries(staging).Length == 0)
                        Directory.Delete(staging, false);
                }
                catch { }
            }

        }

        private static string ReadMarker(string path)
        {
            try
            {
                if (!File.Exists(path)) return string.Empty;
                string value = File.ReadAllText(path, Encoding.ASCII).Trim();
                return SentinelPolicy.IsLowerHex(value, 64) ? value : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static void AddWorldBackupSources(
            World world,
            string staging,
            ICollection<MigrationBackupSource> sources,
            ICollection<string> staged)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            IList<string> savePaths = world.GetSavePaths();
            if (savePaths == null) return;
            int sourceIndex = 0;
            foreach (string savePath in savePaths)
            {
                if (string.IsNullOrWhiteSpace(savePath)) continue;
                if (world.m_fileSource == FileHelpers.FileSource.Cloud)
                {
                    if (FileHelpers.Exists(savePath, FileHelpers.FileSource.Cloud))
                    {
                        AddCloudFile(savePath, savePath);
                        continue;
                    }
                    string[] cloudFiles = FileHelpers.GetFiles(
                        FileHelpers.FileSource.Cloud, savePath) ?? Array.Empty<string>();
                    foreach (string cloudFile in cloudFiles)
                        if (IsCloudChild(savePath, cloudFile)) AddCloudFile(cloudFile, savePath);
                    continue;
                }
                if (File.Exists(savePath))
                {
                    AddLocalFile(savePath, Path.GetFileName(savePath));
                    continue;
                }
                if (!Directory.Exists(savePath)) continue;
                sources.Add(new MigrationBackupSource(
                    Path.GetFullPath(savePath),
                    "world-save/" + NormalizeLogicalName(
                        new DirectoryInfo(savePath).Name, sourceIndex++)));
            }

            void AddLocalFile(string path, string relativeName)
            {
                sources.Add(new MigrationBackupSource(
                    path,
                    "world-save/" + NormalizeLogicalName(relativeName, sourceIndex++)));
            }

            void AddCloudFile(string cloudPath, string saveRoot)
            {
                Directory.CreateDirectory(staging);
                string extension = Path.GetExtension(cloudPath);
                if (extension.Length > 16) extension = ".bin";
                string local = Path.Combine(
                    staging,
                    "world-" + sourceIndex.ToString("D4") + extension);
                FileHelpers.FileCopyOutFromCloud(cloudPath, local, false);
                if (!File.Exists(local)) throw new IOException("cloud-world-staging-failed");
                staged.Add(local);
                string relative = cloudPath.StartsWith(saveRoot, StringComparison.Ordinal)
                    ? cloudPath.Substring(saveRoot.Length)
                    : Path.GetFileName(cloudPath);
                sources.Add(new MigrationBackupSource(
                    local,
                    "world-save/" + NormalizeLogicalName(relative, sourceIndex++)));
            }
        }

        private static MigrationBackupRequest CreateWorldBackupRequest(
            IMigrationBackupService backups,
            string migrationId,
            IEnumerable<MigrationBackupSource> sources) =>
            new MigrationBackupRequest(
                migrationId,
                sources,
                backups.DefaultDestinationRoot,
                backups.DefaultRetentionCount,
                backups.DefaultMaximumTotalBytes,
                MaximumWorldBackupFiles);

        private static bool IsCloudChild(string root, string candidate)
        {
            string prefix = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/') + "/";
            string path = (candidate ?? string.Empty).Replace('\\', '/');
            return path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length;
        }

        private static string NormalizeLogicalName(string value, int index)
        {
            string normalized = (value ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return string.IsNullOrWhiteSpace(normalized)
                ? "source-" + index.ToString("D4")
                : normalized;
        }

        private static void WriteMarker(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            byte[] bytes = Encoding.ASCII.GetBytes(value + "\n");
            using (var stream = new FileStream(
                       temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
