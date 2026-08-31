using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RunicVelocity.Contracts;

namespace RunicVelocity.Core
{
    internal static class ManifestCachePolicy
    {
        internal const int MaximumFiles = 4096;
        internal const int MaximumDirectories = 4096;
        internal const long MaximumFileBytes = 536870912L;
        internal const long MaximumScanBytes = 4294967296L;
        internal const long MaximumMetadataFileBytes = 67108864L;
        internal const int MaximumCacheBytes = 8388608;
        internal const int MaximumPathCharacters = 1024;

        internal static bool UsesCaseInsensitivePaths =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static StringComparer PathComparer =>
            PathComparerFor(UsesCaseInsensitivePaths);

        internal static StringComparison PathComparison =>
            UsesCaseInsensitivePaths
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        internal static StringComparer PathComparerFor(bool caseInsensitive) =>
            caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal static bool CanReuse(
            PluginManifestEntry cached,
            long currentLength,
            long currentLastWriteUtcTicks) =>
            cached != null &&
            cached.Length == currentLength &&
            cached.LastWriteUtcTicks == currentLastWriteUtcTicks &&
            IsSha256(cached.Sha256);

        internal static bool CanAdmitScanBytes(long alreadyAdmitted, long nextFileBytes) =>
            alreadyAdmitted >= 0L && nextFileBytes >= 0L &&
            nextFileBytes <= MaximumFileBytes &&
            alreadyAdmitted <= MaximumScanBytes - nextFileBytes;

        internal static bool IsSafeRelativePath(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Length <= MaximumPathCharacters &&
            !System.IO.Path.IsPathRooted(value) &&
            value.IndexOf('\0') < 0 &&
            !HasParentSegment(value);

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        internal static string Classification(string pluginId, string relativePath)
        {
            if (!string.IsNullOrEmpty(pluginId) && pluginId.StartsWith("chazman.Runic", StringComparison.Ordinal))
                return "runic-plugin";
            return string.IsNullOrEmpty(pluginId) ? "library" : "third-party-plugin";
        }

        private static bool HasParentSegment(string value)
        {
            string normalized = value.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
                if (segment == "..") return true;
            return false;
        }
    }
}
