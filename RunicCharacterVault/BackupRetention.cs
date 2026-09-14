using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RunicCharacterVault
{
    internal static class BackupRetention
    {
        private const int RecentBackupCount = 5;
        private const int DailyBackupCount = 10;
        private const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";

        internal static IReadOnlyList<string> Apply(string directory, string profileName)
        {
            List<BackupFile> backups = FindBackups(directory, profileName)
                .OrderByDescending(backup => backup.Timestamp)
                .ToList();
            HashSet<string> retained = new HashSet<string>(
                backups.Take(RecentBackupCount).Select(backup => backup.Path),
                StringComparer.Ordinal);
            DateTime dailyBoundary = backups.Take(RecentBackupCount)
                .Select(backup => backup.Timestamp.Date)
                .DefaultIfEmpty(DateTime.MinValue)
                .Last();
            IEnumerable<BackupFile> daily = backups.Skip(RecentBackupCount)
                .Where(backup => backup.Timestamp.Date < dailyBoundary)
                .GroupBy(backup => backup.Timestamp.Date)
                .OrderByDescending(group => group.Key)
                .Take(DailyBackupCount)
                .Select(group => group.OrderBy(backup => backup.Timestamp).First());
            retained.UnionWith(daily.Select(backup => backup.Path));

            List<string> deleted = new List<string>();
            foreach (BackupFile backup in backups.Where(backup => !retained.Contains(backup.Path)))
            {
                File.Delete(backup.Path);
                deleted.Add(Path.GetFileName(backup.Path));
            }
            return deleted;
        }

        private static IEnumerable<BackupFile> FindBackups(string directory, string profileName)
        {
            string prefix = profileName + "_";
            foreach (string path in Directory.GetFiles(directory, "*.fch"))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string value = fileName.Substring(prefix.Length);
                DateTime timestamp;
                if (DateTime.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out timestamp))
                {
                    yield return new BackupFile(path, timestamp);
                }
            }
        }

        private sealed class BackupFile
        {
            internal BackupFile(string path, DateTime timestamp)
            {
                Path = path;
                Timestamp = timestamp;
            }

            internal string Path { get; }
            internal DateTime Timestamp { get; }
        }
    }
}
