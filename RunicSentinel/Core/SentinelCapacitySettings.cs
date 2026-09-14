using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RunicSentinel.Core
{
    internal sealed class SentinelCapacitySettings
    {
        internal const int MaximumConfigBytes = 128 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal bool Enabled;
        internal int Players;
        internal string Revision;
        private string[] _lines;
        private int _enabledLine, _playersLine;
        private bool _bom;

        internal static SentinelCapacitySettings Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumConfigBytes)
                throw new InvalidDataException("World Engine config is missing or too large.");
            string text = Utf8.GetString(bytes);
            var value = new SentinelCapacitySettings { Revision = Hash(bytes), _bom = text[0] == '\ufeff', _enabledLine = -1, _playersLine = -1 };
            if (value._bom) text = text.Substring(1);
            value._lines = text.Split('\n');
            bool inCapacity = false;
            int sections = 0;
            for (int i = 0; i < value._lines.Length; i++)
            {
                string line = value._lines[i].Trim();
                if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inCapacity = line == "[Player Capacity]";
                    if (inCapacity) sections++;
                    continue;
                }
                if (!inCapacity) continue;
                int equal = line.IndexOf('=');
                if (equal < 0) continue;
                string key = line.Substring(0, equal).Trim(), setting = line.Substring(equal + 1).Trim();
                if (key == "Enabled")
                {
                    if (value._enabledLine >= 0 || !bool.TryParse(setting, out value.Enabled)) throw new InvalidDataException("Ambiguous or invalid capacity Enabled setting.");
                    value._enabledLine = i;
                }
                else if (key == "MaximumPlayers")
                {
                    if (value._playersLine >= 0 || !TryPlayers(setting, out value.Players)) throw new InvalidDataException("Invalid MaximumPlayers; expected 2–64.");
                    value._playersLine = i;
                }
            }
            if (sections != 1 || value._enabledLine < 0 || value._playersLine < 0)
                throw new InvalidDataException("Expected one complete Player Capacity section. Start World Engine 1.2.0 once to generate it.");
            return value;
        }

        internal byte[] Edit(bool enabled, int players)
        {
            if (players < 2 || players > 64) throw new ArgumentOutOfRangeException(nameof(players), "Player cap must be 2–64.");
            var lines = (string[])_lines.Clone();
            lines[_enabledLine] = "Enabled = " + (enabled ? "true" : "false") + (lines[_enabledLine].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
            lines[_playersLine] = "MaximumPlayers = " + players.ToString(CultureInfo.InvariantCulture) + (lines[_playersLine].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
            byte[] result = Utf8.GetBytes((_bom ? "\ufeff" : "") + string.Join("\n", lines));
            Parse(result); // Validate the replacement before any persistence.
            return result;
        }

        internal static bool TryPlayers(string value, out int players) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out players) && players >= 2 && players <= 64;
        internal static bool IsRevision(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }
        private static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }

        internal static SentinelCapacitySettings Read(string path)
        {
            if (new FileInfo(path).Length > MaximumConfigBytes) throw new InvalidDataException("World Engine config is too large.");
            return Parse(File.ReadAllBytes(path));
        }

        internal static void Save(string path, string revision, bool enabled, int players)
        {
            if (!IsRevision(revision)) throw new InvalidDataException("Refresh server cap settings before saving.");
            var current = Read(path);
            if (!string.Equals(current.Revision, revision, StringComparison.Ordinal))
                throw new InvalidOperationException("Server settings changed since you opened this tab. Refresh and try again.");
            byte[] edited = current.Edit(enabled, players);
            string temporary = path + ".sentinel-cap-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(edited, 0, edited.Length); stream.Flush(true); }
                if (Read(path).Revision != revision) throw new InvalidOperationException("Server settings changed while saving. Refresh and try again.");
                // Atomic same-filesystem replacement. Keep the previous config in one rotating backup.
                // Do not fall back to truncating the live file if atomic replacement is unsupported.
                File.Replace(temporary, path, path + ".sentinel-cap.bak");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
