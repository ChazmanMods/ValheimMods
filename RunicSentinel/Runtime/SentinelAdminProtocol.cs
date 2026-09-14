using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RunicSentinel.Runtime
{
    internal sealed class SentinelAdminDocument
    {
        internal long Sequence;
        internal string Profile = "runic-suite";
        internal string ExpiresUnixSeconds = "0";
        internal string UnknownMods = "Forbidden";
        internal string RequiredMods = string.Empty;
        internal string OptionalMods = string.Empty;
        internal string GrayListMods = string.Empty;
        internal string ForbiddenMods = string.Empty;
        internal string Administrators = string.Empty;
        internal string BannedUsers = string.Empty;
        internal string Modules = string.Empty;
        internal string DetectedProfile = string.Empty;
        internal string Integrity = string.Empty;
        internal string LastDenial = string.Empty;
        internal string AdmissionMode = "Optional";
        internal string IntegritySeconds = "15";
        internal string VeryHighThreshold = "2";
        internal string HighThreshold = "3";
        internal string EnforcementWindowSeconds = "60";
        internal bool BackupTransitions = true;
        internal bool ManagedSigningKey;
        internal bool SetupAvailable;
        internal string AdministratorSource = string.Empty;
        internal string SigningKeyPin = string.Empty;
        internal string Status = string.Empty;
        internal bool CapacitySupported, CapacityAvailable, CapacitySavedEnabled, CapacityActiveEnabled, CapacityRestartRequired;
        internal string CapacityVersion = "", CapacityRevision = "", CapacitySavedPlayers = "", CapacityActivePlayers = "", CapacityStatus = "", CapacityCurrentPlayers = "";
    }

    internal static class SentinelAdminProtocol
    {
        internal const int MaximumWireBytes = 120 * 1024;
        private const string Header = "RUNIC-SENTINEL-ADMIN/1\n";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Encode(SentinelAdminDocument value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sequence"] = value.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["profile"] = value.Profile,
                ["expires"] = value.ExpiresUnixSeconds,
                ["unknown"] = value.UnknownMods,
                ["required"] = value.RequiredMods,
                ["optional"] = value.OptionalMods,
                ["gray"] = value.GrayListMods,
                ["forbidden"] = value.ForbiddenMods,
                ["admins"] = value.Administrators,
                ["bans"] = value.BannedUsers,
                ["modules"] = value.Modules,
                ["detected"] = value.DetectedProfile,
                ["integrity"] = value.Integrity,
                ["last-denial"] = value.LastDenial,
                ["admission"] = value.AdmissionMode,
                ["integrity-seconds"] = value.IntegritySeconds,
                ["very-high"] = value.VeryHighThreshold,
                ["high"] = value.HighThreshold,
                ["window"] = value.EnforcementWindowSeconds,
                ["backup"] = value.BackupTransitions ? "1" : "0",
                ["managed-key"] = value.ManagedSigningKey ? "1" : "0",
                ["setup-available"] = value.SetupAvailable ? "1" : "0",
                ["administrator-source"] = value.AdministratorSource,
                ["key-pin"] = value.SigningKeyPin,
                ["status"] = value.Status,
                ["cap-supported"] = value.CapacitySupported ? "1" : "0",
                ["cap-available"] = value.CapacityAvailable ? "1" : "0",
                ["cap-saved-enabled"] = value.CapacitySavedEnabled ? "1" : "0",
                ["cap-active-enabled"] = value.CapacityActiveEnabled ? "1" : "0",
                ["cap-restart"] = value.CapacityRestartRequired ? "1" : "0",
                ["cap-version"] = value.CapacityVersion,
                ["cap-revision"] = value.CapacityRevision,
                ["cap-saved-players"] = value.CapacitySavedPlayers,
                ["cap-active-players"] = value.CapacityActivePlayers,
                ["cap-status"] = value.CapacityStatus,
                ["cap-current-players"] = value.CapacityCurrentPlayers
            };
            var builder = new StringBuilder(Header);
            foreach (KeyValuePair<string, string> field in fields)
                builder.Append(field.Key).Append('=')
                    .Append(Convert.ToBase64String(StrictUtf8.GetBytes(field.Value ?? string.Empty)))
                    .Append('\n');
            byte[] bytes = StrictUtf8.GetBytes(builder.ToString());
            if (bytes.Length > MaximumWireBytes)
                throw new InvalidDataException("admin-document-too-large");
            return bytes;
        }

        internal static bool TryDecode(byte[] bytes, out SentinelAdminDocument value)
        {
            value = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumWireBytes) return false;
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch { return false; }
            if (!text.StartsWith(Header, StringComparison.Ordinal) ||
                text.IndexOf('\r') >= 0 || !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            string[] lines = text.Split('\n');
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < lines.Length - 1; index++)
            {
                int separator = lines[index].IndexOf('=');
                if (separator <= 0 || !fields.TryAdd(
                        lines[index].Substring(0, separator),
                        Decode(lines[index].Substring(separator + 1)))) return false;
            }
            if (!TryLong(fields, "sequence", out long sequence)) return false;
            value = new SentinelAdminDocument
            {
                Sequence = sequence,
                Profile = Get(fields, "profile"),
                ExpiresUnixSeconds = Get(fields, "expires"),
                UnknownMods = Get(fields, "unknown"),
                RequiredMods = Get(fields, "required"),
                OptionalMods = Get(fields, "optional"),
                GrayListMods = Get(fields, "gray"),
                ForbiddenMods = Get(fields, "forbidden"),
                Administrators = Get(fields, "admins"),
                BannedUsers = Get(fields, "bans"),
                Modules = Get(fields, "modules"),
                DetectedProfile = Get(fields, "detected"),
                Integrity = Get(fields, "integrity"),
                LastDenial = Get(fields, "last-denial"),
                AdmissionMode = Get(fields, "admission"),
                IntegritySeconds = Get(fields, "integrity-seconds"),
                VeryHighThreshold = Get(fields, "very-high"),
                HighThreshold = Get(fields, "high"),
                EnforcementWindowSeconds = Get(fields, "window"),
                BackupTransitions = Get(fields, "backup") == "1",
                ManagedSigningKey = Get(fields, "managed-key") == "1",
                SetupAvailable = Get(fields, "setup-available") == "1",
                AdministratorSource = Get(fields, "administrator-source"),
                SigningKeyPin = Get(fields, "key-pin"),
                Status = Get(fields, "status"),
                CapacitySupported = Get(fields, "cap-supported") == "1",
                CapacityAvailable = Get(fields, "cap-available") == "1",
                CapacitySavedEnabled = Get(fields, "cap-saved-enabled") == "1",
                CapacityActiveEnabled = Get(fields, "cap-active-enabled") == "1",
                CapacityRestartRequired = Get(fields, "cap-restart") == "1",
                CapacityVersion = Get(fields, "cap-version"),
                CapacityRevision = Get(fields, "cap-revision"),
                CapacitySavedPlayers = Get(fields, "cap-saved-players"),
                CapacityActivePlayers = Get(fields, "cap-active-players"),
                CapacityStatus = Get(fields, "cap-status"),
                CapacityCurrentPlayers = Get(fields, "cap-current-players")
            };
            return true;
        }

        internal static byte[] EncodeTool(string tool)
        {
            string exact = tool ?? string.Empty;
            if (exact != "report" && exact != "networks" && exact != "backup" && exact != "bootstrap")
                throw new ArgumentException("Unknown admin tool.", nameof(tool));
            return StrictUtf8.GetBytes("RUNIC-SENTINEL-ADMIN-TOOL/1\n" + exact + "\n");
        }

        internal static bool TryDecodeTool(byte[] bytes, out string tool)
        {
            tool = string.Empty;
            if (bytes == null || bytes.Length > 128) return false;
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch { return false; }
            const string prefix = "RUNIC-SENTINEL-ADMIN-TOOL/1\n";
            if (!text.StartsWith(prefix, StringComparison.Ordinal) ||
                !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            tool = text.Substring(prefix.Length, text.Length - prefix.Length - 1);
            return tool == "report" || tool == "networks" || tool == "backup" || tool == "bootstrap";
        }

        internal static byte[] EncodeMessage(string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > 4096) throw new InvalidDataException("admin-message-too-large");
            return bytes;
        }

        internal static string DecodeMessage(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 4096) return "invalid-response";
            try { return StrictUtf8.GetString(bytes); }
            catch { return "invalid-response"; }
        }

        private static string Decode(string value)
        {
            try { return StrictUtf8.GetString(Convert.FromBase64String(value)); }
            catch { return null; }
        }
        private static string Get(IDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string value) && value != null ? value : string.Empty;
        private static bool TryLong(IDictionary<string, string> values, string key, out long result) =>
            long.TryParse(Get(values, key), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out result) && result >= 0L;
    }
}
