using System;
using System.Globalization;
using System.Text;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal static class SentinelCapacityProtocol
    {
        private const string Header = "RUNIC-SENTINEL-CAPACITY/1";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static byte[] Encode(string revision, bool enabled, int players)
        {
            if (!SentinelCapacitySettings.IsRevision(revision) || players < 2 || players > 64) throw new ArgumentException("Refresh the server settings and enter a player cap from 2 to 64.");
            return Utf8.GetBytes(Header + "\n" + revision + "\n" + (enabled ? "1" : "0") + "\n" + players.ToString(CultureInfo.InvariantCulture) + "\n");
        }
        internal static bool TryDecode(byte[] bytes, out string revision, out bool enabled, out int players)
        {
            revision = null; enabled = false; players = 0;
            if (bytes == null || bytes.Length > 128) return false;
            try
            {
                string[] lines = Utf8.GetString(bytes).Split('\n');
                if (lines.Length != 5 || lines[0] != Header || lines[4] != "" || !SentinelCapacitySettings.IsRevision(lines[1]) ||
                    (lines[2] != "1" && lines[2] != "0") || !SentinelCapacitySettings.TryPlayers(lines[3], out players) ||
                    lines[3] != players.ToString(CultureInfo.InvariantCulture)) return false;
                revision = lines[1]; enabled = lines[2] == "1"; return true;
            }
            catch { return false; }
        }
    }
}
