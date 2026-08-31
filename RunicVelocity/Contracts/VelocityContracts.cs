using System;
using System.Collections.Generic;

namespace RunicVelocity.Contracts
{
    public sealed class StartupStageSample
    {
        public StartupStageSample(string stageId, double millisecondsFromAssemblyLoad)
        {
            if (string.IsNullOrWhiteSpace(stageId) || stageId.Length > 96 ||
                millisecondsFromAssemblyLoad < 0d || double.IsNaN(millisecondsFromAssemblyLoad) ||
                double.IsInfinity(millisecondsFromAssemblyLoad))
                throw new ArgumentOutOfRangeException(nameof(stageId));
            StageId = stageId;
            MillisecondsFromAssemblyLoad = millisecondsFromAssemblyLoad;
        }
        public string StageId { get; }
        public double MillisecondsFromAssemblyLoad { get; }
    }

    public sealed class StartupTimelineSnapshot
    {
        public StartupTimelineSnapshot(IEnumerable<StartupStageSample> stages)
        {
            Stages = new List<StartupStageSample>(stages ?? Array.Empty<StartupStageSample>()).AsReadOnly();
        }
        public IReadOnlyList<StartupStageSample> Stages { get; }
    }

    public sealed class PluginManifestEntry
    {
        public PluginManifestEntry(
            string relativePath,
            long length,
            long lastWriteUtcTicks,
            string sha256,
            string pluginId,
            string pluginVersion,
            IEnumerable<string> dependencies,
            string classification,
            long lastManifestScanUtcTicks = 0L)
        {
            RelativePath = relativePath ?? string.Empty;
            Length = length;
            LastWriteUtcTicks = lastWriteUtcTicks;
            Sha256 = sha256 ?? string.Empty;
            PluginId = pluginId ?? string.Empty;
            PluginVersion = pluginVersion ?? string.Empty;
            Dependencies = new List<string>(dependencies ?? Array.Empty<string>()).AsReadOnly();
            Classification = classification ?? string.Empty;
            LastManifestScanUtcTicks = Math.Max(0L, lastManifestScanUtcTicks);
        }
        public string RelativePath { get; }
        public long Length { get; }
        public long LastWriteUtcTicks { get; }
        public string Sha256 { get; }
        public string PluginId { get; }
        public string PluginVersion { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public string Classification { get; }
        public long LastManifestScanUtcTicks { get; }
    }

    public sealed class PluginManifestSnapshot
    {
        public PluginManifestSnapshot(
            IEnumerable<PluginManifestEntry> entries,
            int hashedFiles,
            int reusedFiles,
            bool truncated,
            string status)
        {
            Entries = new List<PluginManifestEntry>(entries ?? Array.Empty<PluginManifestEntry>()).AsReadOnly();
            HashedFiles = Math.Max(0, hashedFiles);
            ReusedFiles = Math.Max(0, reusedFiles);
            Truncated = truncated;
            Status = status ?? string.Empty;
        }
        public IReadOnlyList<PluginManifestEntry> Entries { get; }
        public int HashedFiles { get; }
        public int ReusedFiles { get; }
        public bool Truncated { get; }
        public string Status { get; }
        public static PluginManifestSnapshot Empty { get; } =
            new PluginManifestSnapshot(Array.Empty<PluginManifestEntry>(), 0, 0, false, "not-started");
    }

}
