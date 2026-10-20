using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RunicSentinel.Core
{
    internal sealed class BuildingSettings
    {
        internal string Revision;
        internal bool Enabled;
        internal BuildingLimits Limits;
        private string[] _lines;
        private readonly int[] _indices = { -1, -1, -1, -1 };
        private bool _bom;
        internal static readonly string[] Keys = { "Enabled", "VerticalSupportMultiplier", "MaximumTerrainRaise", "MaximumDigDepth" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        internal static bool TryLimits(string support, string raise, string dig, out BuildingLimits limits)
        {
            limits = null;
            if (!float.TryParse(support, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float s) ||
                !float.TryParse(raise, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float r) ||
                !float.TryParse(dig, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float d)) return false;
            try { limits = new BuildingLimits(s, r, d); return true; } catch (ArgumentException) { return false; }
        }
        internal byte[] Encode() => Utf8.GetBytes("BUILDING/1\n" + Revision + "\n" + (Enabled ? "1" : "0") + "\n" + Number(Limits.SupportMultiplier) + "\n" + Number(Limits.RaiseHeight) + "\n" + Number(Limits.DigDepth) + "\n");
        internal static BuildingSettings Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 180) throw new InvalidDataException("Invalid building settings request.");
            var lines = Utf8.GetString(bytes).Split('\n');
            if (lines.Length != 7 || lines[0] != "BUILDING/1" || lines[6] != "" || !SentinelCapacitySettings.IsRevision(lines[1]) ||
                (lines[2] != "0" && lines[2] != "1") || !TryLimits(lines[3], lines[4], lines[5], out var limits))
                throw new InvalidDataException("Refresh settings and enter support 1–10, raise/depth 1–64.");
            var result = new BuildingSettings { Revision = lines[1], Enabled = lines[2] == "1", Limits = limits };
            if (!result.Encode().SequenceEqual(bytes)) throw new InvalidDataException("Noncanonical building settings request.");
            return result;
        }
        internal static BuildingSettings Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 128 * 1024) throw new InvalidDataException("Invalid Sentinel config size.");
            var result = new BuildingSettings();
            using (var hash = SHA256.Create()) result.Revision = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
            string text = Utf8.GetString(bytes);
            result._bom = text.StartsWith("\ufeff", StringComparison.Ordinal);
            result._lines = (result._bom ? text.Substring(1) : text).Split('\n');
            bool section = false; int count = 0; var values = new string[4];
            for (int i = 0; i < result._lines.Length; i++)
            {
                string line = result._lines[i].Trim();
                if (line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[")) { section = line == "[Building and Terrain]"; if (section) count++; continue; }
                int equals = line.IndexOf('=');
                if (!section || equals < 0) continue;
                int key = Array.IndexOf(Keys, line.Substring(0, equals).Trim());
                if (key < 0) continue;
                if (result._indices[key] >= 0) throw new InvalidDataException("Duplicate building setting.");
                result._indices[key] = i; values[key] = line.Substring(equals + 1).Trim();
            }
            if (count != 1 || result._indices.Any(i => i < 0) || !bool.TryParse(values[0], out result.Enabled) ||
                !TryLimits(values[1], values[2], values[3], out result.Limits)) throw new InvalidDataException("Start the updated full Sentinel host once to generate valid Building and Terrain settings.");
            return result;
        }
        internal static BuildingSettings Read(string path)
        {
            if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Sentinel config too large.");
            return Parse(File.ReadAllBytes(path));
        }
        internal static void Save(string path, BuildingSettings request)
        {
            request = Decode(request.Encode());
            var current = Read(path);
            if (current.Revision != request.Revision) throw new InvalidOperationException("Settings changed. Refresh the World tab before saving.");
            string[] values = { request.Enabled ? "true" : "false", Number(request.Limits.SupportMultiplier), Number(request.Limits.RaiseHeight), Number(request.Limits.DigDepth) };
            for (int i = 0; i < 4; i++)
            {
                int index = current._indices[i];
                current._lines[index] = Keys[i] + " = " + values[i] + (current._lines[index].EndsWith("\r") ? "\r" : "");
            }
            byte[] edited = Utf8.GetBytes((current._bom ? "\ufeff" : "") + string.Join("\n", current._lines));
            Parse(edited);
            string temp = path + ".building-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(edited, 0, edited.Length); stream.Flush(true); }
                if (Read(path).Revision != request.Revision) throw new InvalidOperationException("Settings changed while saving. Refresh and retry.");
                File.Replace(temp, path, path + ".building.bak");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
