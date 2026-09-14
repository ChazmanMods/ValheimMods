using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicSentinel.Contracts;

namespace RunicSentinel.Core
{
    internal sealed class SentinelPolicyRule
    {
        internal SentinelPolicyRule(
            PluginClassification classification,
            string id,
            string version,
            string sha256)
        {
            Classification = classification;
            Id = id;
            Version = version;
            Sha256 = sha256;
        }

        internal PluginClassification Classification { get; }
        internal string Id { get; }
        internal string Version { get; }
        internal string Sha256 { get; }
    }

    internal enum SentinelModuleScope
    {
        Both = 0,
        Client = 1,
        Server = 2
    }

    internal sealed class SentinelModuleRule
    {
        internal SentinelModuleRule(
            SentinelModuleScope scope,
            string id,
            string version,
            int protocol,
            IReadOnlyList<string> capabilities)
        {
            Scope = scope;
            Id = id;
            Version = version;
            Protocol = protocol;
            Capabilities = capabilities;
        }

        internal SentinelModuleScope Scope { get; }
        internal string Id { get; }
        internal string Version { get; }
        internal int Protocol { get; }
        internal IReadOnlyList<string> Capabilities { get; }
    }

    internal sealed class SentinelAdministratorRole
    {
        internal SentinelAdministratorRole(string authority, string subject)
        {
            Authority = authority;
            Subject = subject;
        }

        internal string Authority { get; }
        internal string Subject { get; }
        internal string CanonicalKey => Authority + ":" + Uri.EscapeDataString(Subject);
    }

    internal sealed class SentinelPolicy
    {
        internal const int MaximumBytes = 1024 * 1024;
        internal const int MaximumRules = 2048;
        internal const int SignatureBytes = 384;
        internal const int MaximumSignatureFileBytes = 1024;

        internal SentinelPolicy(
            int formatVersion,
            string profile,
            long sequence,
            long issuedUnixSeconds,
            long expiresUnixSeconds,
            PluginClassification unknown,
            IReadOnlyList<SentinelPolicyRule> rules,
            IReadOnlyList<SentinelModuleRule> modules,
            IReadOnlyList<SentinelAdministratorRole> administrators,
            IReadOnlyList<SentinelAdministratorRole> bannedUsers,
            string payloadDigest)
        {
            FormatVersion = formatVersion;
            Profile = profile;
            Sequence = sequence;
            IssuedUnixSeconds = issuedUnixSeconds;
            ExpiresUnixSeconds = expiresUnixSeconds;
            Unknown = unknown;
            Rules = rules;
            Modules = modules;
            Administrators = administrators;
            BannedUsers = bannedUsers;
            PayloadDigest = payloadDigest;
        }

        internal int FormatVersion { get; }
        internal string Profile { get; }
        internal long Sequence { get; }
        internal long IssuedUnixSeconds { get; }
        internal long ExpiresUnixSeconds { get; }
        internal PluginClassification Unknown { get; }
        internal IReadOnlyList<SentinelPolicyRule> Rules { get; }
        internal IReadOnlyList<SentinelModuleRule> Modules { get; }
        internal IReadOnlyList<SentinelAdministratorRole> Administrators { get; }
        internal IReadOnlyList<SentinelAdministratorRole> BannedUsers { get; }
        internal string PayloadDigest { get; }

        internal static bool TryDecodeSignatureFile(
            byte[] file,
            out byte[] signature,
            out string failure)
        {
            signature = null;
            failure = string.Empty;
            if (file == null || file.Length == 0 || file.Length > MaximumSignatureFileBytes)
            { failure = "SignatureSize"; return false; }
            string text;
            try { text = new UTF8Encoding(false, true).GetString(file); }
            catch { failure = "SignatureUtf8"; return false; }
            if (text.IndexOf('\r') >= 0 || text.IndexOf('\0') >= 0 ||
                !text.EndsWith("\n", StringComparison.Ordinal) ||
                text.IndexOf('\n') != text.Length - 1)
            { failure = "SignatureCanonical"; return false; }
            string encoded = text.Substring(0, text.Length - 1);
            try { signature = Convert.FromBase64String(encoded); }
            catch { failure = "SignatureEncoding"; return false; }
            if (signature.Length != SignatureBytes || Convert.ToBase64String(signature) != encoded)
            { signature = null; failure = "SignatureShape"; return false; }
            return true;
        }

        internal static bool TryParseAndVerify(
            byte[] payload,
            byte[] signature,
            PinnedRsaPublicKey publicKey,
            out SentinelPolicy policy,
            out string failure)
        {
            policy = null;
            failure = string.Empty;
            if (payload == null || payload.Length == 0 || payload.Length > MaximumBytes)
            { failure = "PolicySize"; return false; }
            if (signature == null || signature.Length != SignatureBytes || publicKey == null)
            { failure = "SignatureShape"; return false; }

            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(publicKey.CreateParameters());
                    if (rsa.KeySize != 3072 || !rsa.VerifyData(
                            payload,
                            signature,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1))
                    { failure = "SignatureMismatch"; return false; }
                }
            }
            catch
            { failure = "SignatureVerification"; return false; }

            string text;
            try { text = new UTF8Encoding(false, true).GetString(payload); }
            catch { failure = "PolicyUtf8"; return false; }
            if (text.IndexOf('\r') >= 0 || text.IndexOf('\0') >= 0 ||
                !text.EndsWith("\n", StringComparison.Ordinal))
            { failure = "PolicyCanonical"; return false; }
            string[] lines = text.Split('\n');
            bool version2 = lines.Length >= 7 && lines[0] == "RUNIC-SENTINEL/2";
            bool version3 = lines.Length >= 8 && lines[0] == "RUNIC-SENTINEL/3";
            if ((!version2 && !version3) ||
                lines[lines.Length - 1].Length != 0)
            { failure = "PolicyHeader"; return false; }
            if (!TryRequired(lines[1], "profile=", 64, out string profile) ||
                !TryCanonicalLong(lines[2], "sequence=", false, out long sequence) ||
                !TryCanonicalLong(lines[3], "issued=", true, out long issued) ||
                !TryCanonicalLong(lines[4], "expires=", true, out long expires) ||
                expires != 0L && expires <= issued ||
                !TryUnknown(lines[5], out PluginClassification unknown))
            { failure = "PolicyPreamble"; return false; }

            int firstEntry = 6;
            if (version3)
            {
                if (lines[6] != "unknown-capability=Forbidden")
                { failure = "CapabilityDefault"; return false; }
                firstEntry = 7;
            }

            var rules = new List<SentinelPolicyRule>();
            var modules = new List<SentinelModuleRule>();
            var administrators = new List<SentinelAdministratorRole>();
            var bannedUsers = new List<SentinelAdministratorRole>();
            string previousRule = null;
            string previousModule = null;
            string previousRole = null;
            string previousBan = null;
            int phase = 0;
            for (int index = firstEntry; index < lines.Length - 1; index++)
            {
                string line = lines[index];
                if (line.StartsWith("rule=", StringComparison.Ordinal) && phase <= 0)
                {
                    if (rules.Count >= MaximumRules)
                    { failure = "PolicyRule"; return false; }
                    string[] fields = line.Substring(5).Split('|');
                    if (fields.Length != 4 ||
                        !TryClassification(fields[0], out PluginClassification kind) ||
                        !CanonicalPluginId(fields[1]) ||
                        !CanonicalVersionOrWildcard(fields[2]) ||
                        !CanonicalHashOrWildcard(fields[3]))
                    { failure = "PolicyRule"; return false; }
                    if (previousRule != null && string.CompareOrdinal(previousRule, fields[1]) >= 0)
                    { failure = "RuleOrder"; return false; }
                    previousRule = fields[1];
                    rules.Add(new SentinelPolicyRule(kind, fields[1], fields[2], fields[3]));
                    continue;
                }
                if (version3 && line.StartsWith("module=", StringComparison.Ordinal) && phase <= 1)
                {
                    phase = 1;
                    if (modules.Count >= 128)
                    { failure = "ModuleCap"; return false; }
                    string[] fields = line.Substring(7).Split('|');
                    if (fields.Length != 5 ||
                        !TryModuleScope(fields[0], out SentinelModuleScope scope) ||
                        !CanonicalPluginId(fields[1]) ||
                        !CanonicalVersionOrWildcard(fields[2]) ||
                        !TryCanonicalInt(fields[3], out int protocol) ||
                        !TryCapabilities(fields[4], out IReadOnlyList<string> capabilities))
                    { failure = "ModuleRule"; return false; }
                    if (previousModule != null && string.CompareOrdinal(previousModule, fields[1]) >= 0)
                    { failure = "ModuleOrder"; return false; }
                    previousModule = fields[1];
                    modules.Add(new SentinelModuleRule(
                        scope, fields[1], fields[2], protocol, capabilities));
                    continue;
                }
                if (version3 && line.StartsWith("role=", StringComparison.Ordinal) && phase <= 2)
                {
                    phase = 2;
                    if (administrators.Count >= 256)
                    { failure = "RoleCap"; return false; }
                    string[] fields = line.Substring(5).Split('|');
                    if (fields.Length != 2 || !CanonicalAuthority(fields[0]) ||
                        !TryCanonicalSubject(fields[1], out string subject))
                    { failure = "RoleRule"; return false; }
                    string key = fields[0] + ":" + fields[1];
                    if (previousRole != null && string.CompareOrdinal(previousRole, key) >= 0)
                    { failure = "RoleOrder"; return false; }
                    previousRole = key;
                    administrators.Add(new SentinelAdministratorRole(fields[0], subject));
                    continue;
                }
                if (version3 && line.StartsWith("ban=", StringComparison.Ordinal) && phase <= 3)
                {
                    phase = 3;
                    if (bannedUsers.Count >= 4096)
                    { failure = "BanCap"; return false; }
                    string[] fields = line.Substring(4).Split('|');
                    if (fields.Length != 2 || !CanonicalAuthority(fields[0]) ||
                        !TryCanonicalSubject(fields[1], out string subject))
                    { failure = "BanRule"; return false; }
                    string key = fields[0] + ":" + fields[1];
                    if (previousBan != null && string.CompareOrdinal(previousBan, key) >= 0)
                    { failure = "BanOrder"; return false; }
                    previousBan = key;
                    bannedUsers.Add(new SentinelAdministratorRole(fields[0], subject));
                    continue;
                }
                failure = version3 ? "PolicyEntry" : "PolicyRule";
                return false;
            }

            string payloadDigest;
            using (SHA256 sha = SHA256.Create()) payloadDigest = Hex(sha.ComputeHash(payload));
            policy = new SentinelPolicy(
                version3 ? 3 : 2,
                profile,
                sequence,
                issued,
                expires,
                unknown,
                rules.AsReadOnly(),
                modules.AsReadOnly(),
                administrators.AsReadOnly(),
                bannedUsers.AsReadOnly(),
                payloadDigest);
            return true;
        }

        internal static bool CanonicalPluginId(string value) => CanonicalAtom(value, 1, 128);
        internal static bool CanonicalVersionOrWildcard(string value) =>
            value == "*" || CanonicalAtom(value, 1, 64);
        internal static bool CanonicalHashOrWildcard(string value) =>
            value == "*" || IsLowerHex(value, 64);

        internal static bool CanonicalAtom(string value, int minimum, int maximum)
        {
            if (value == null || value.Length < minimum || value.Length > maximum) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '.' && character != '-' && character != '_') return false;
            }
            return true;
        }

        internal static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
                if (!((value[index] >= '0' && value[index] <= '9') ||
                      (value[index] >= 'a' && value[index] <= 'f'))) return false;
            return true;
        }

        internal static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal static bool FixedTimeHexEquals(string left, string right)
        {
            if (left == null || right == null) return false;
            int difference = left.Length ^ right.Length;
            int maximum = Math.Max(left.Length, right.Length);
            for (int index = 0; index < maximum; index++)
            {
                char a = index < left.Length ? left[index] : '\0';
                char b = index < right.Length ? right[index] : '\0';
                difference |= a ^ b;
            }
            return difference == 0;
        }

        private static bool TryRequired(string line, string prefix, int maximum, out string value)
        {
            value = string.Empty;
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            value = line.Substring(prefix.Length);
            return CanonicalAtom(value, 1, maximum);
        }

        private static bool TryCanonicalLong(
            string line,
            string prefix,
            bool allowZero,
            out long value)
        {
            value = 0L;
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string text = line.Substring(prefix.Length);
            if (text.Length == 0 || text.Length > 19 || text.Length > 1 && text[0] == '0' ||
                !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return false;
            return allowZero ? value >= 0L : value > 0L;
        }

        private static bool TryUnknown(string line, out PluginClassification value)
        {
            value = PluginClassification.Unknown;
            if (!line.StartsWith("unknown=", StringComparison.Ordinal)) return false;
            string text = line.Substring(8);
            if (text == "Quarantined") value = PluginClassification.Quarantined;
            else if (text == "Unmanaged") value = PluginClassification.Unmanaged;
            else if (text == "Forbidden") value = PluginClassification.Forbidden;
            else return false;
            return true;
        }

        private static bool TryClassification(string text, out PluginClassification value)
        {
            value = PluginClassification.Unknown;
            switch (text)
            {
                case "Required": value = PluginClassification.Required; return true;
                case "ApprovedOptional": value = PluginClassification.ApprovedOptional; return true;
                case "ServerOnly": value = PluginClassification.ServerOnly; return true;
                case "Forbidden": value = PluginClassification.Forbidden; return true;
                case "Unmanaged": value = PluginClassification.Unmanaged; return true;
                case "AdministratorOnly": value = PluginClassification.AdministratorOnly; return true;
                case "Quarantined": value = PluginClassification.Quarantined; return true;
                default: return false;
            }
        }

        private static bool TryModuleScope(string text, out SentinelModuleScope scope)
        {
            scope = SentinelModuleScope.Both;
            if (text == "Both") return true;
            if (text == "Client") { scope = SentinelModuleScope.Client; return true; }
            if (text == "Server") { scope = SentinelModuleScope.Server; return true; }
            return false;
        }

        private static bool TryCanonicalInt(string text, out int value)
        {
            value = 0;
            return text != null && text.Length > 0 && text.Length <= 10 &&
                   (text.Length == 1 || text[0] != '0') &&
                   int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                   value > 0;
        }

        private static bool TryCapabilities(string text, out IReadOnlyList<string> capabilities)
        {
            capabilities = Array.Empty<string>();
            if (string.IsNullOrEmpty(text)) return true;
            string[] values = text.Split(',');
            if (values.Length > 64) return false;
            string previous = null;
            for (int index = 0; index < values.Length; index++)
            {
                if (!CanonicalAtom(values[index], 1, 128) ||
                    previous != null && string.CompareOrdinal(previous, values[index]) >= 0)
                    return false;
                previous = values[index];
            }
            capabilities = Array.AsReadOnly(values);
            return true;
        }

        private static bool CanonicalAuthority(string value)
        {
            if (!CanonicalAtom(value, 1, 64)) return false;
            for (int index = 0; index < value.Length; index++)
                if (value[index] >= 'A' && value[index] <= 'Z') return false;
            return true;
        }

        private static bool TryCanonicalSubject(string encoded, out string subject)
        {
            subject = string.Empty;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > 768) return false;
            try
            {
                subject = Uri.UnescapeDataString(encoded);
                if (string.IsNullOrEmpty(subject) || subject.Length > 256 ||
                    Uri.EscapeDataString(subject) != encoded) return false;
                for (int index = 0; index < subject.Length; index++)
                    if (char.IsControl(subject[index])) return false;
                return true;
            }
            catch { subject = string.Empty; return false; }
        }
    }
}
