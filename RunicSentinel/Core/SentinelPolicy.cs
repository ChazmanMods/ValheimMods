using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
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

    internal sealed class SentinelPolicy
    {
        internal const int MaximumBytes = 1024 * 1024;
        internal const int MaximumRules = 2048;
        internal const int SignatureBytes = 384;
        internal const int MaximumSignatureFileBytes = 1024;

        internal SentinelPolicy(
            string profile,
            long sequence,
            long issuedUnixSeconds,
            long expiresUnixSeconds,
            PluginClassification unknown,
            IReadOnlyList<SentinelPolicyRule> rules,
            string payloadDigest)
        {
            Profile = profile;
            Sequence = sequence;
            IssuedUnixSeconds = issuedUnixSeconds;
            ExpiresUnixSeconds = expiresUnixSeconds;
            Unknown = unknown;
            Rules = rules;
            PayloadDigest = payloadDigest;
        }

        internal string Profile { get; }
        internal long Sequence { get; }
        internal long IssuedUnixSeconds { get; }
        internal long ExpiresUnixSeconds { get; }
        internal PluginClassification Unknown { get; }
        internal IReadOnlyList<SentinelPolicyRule> Rules { get; }
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
            if (lines.Length < 7 || lines[0] != "RUNIC-SENTINEL/2" ||
                lines[lines.Length - 1].Length != 0)
            { failure = "PolicyHeader"; return false; }
            if (!TryRequired(lines[1], "profile=", 64, out string profile) ||
                !TryCanonicalLong(lines[2], "sequence=", false, out long sequence) ||
                !TryCanonicalLong(lines[3], "issued=", true, out long issued) ||
                !TryCanonicalLong(lines[4], "expires=", true, out long expires) ||
                expires != 0L && expires <= issued ||
                !TryUnknown(lines[5], out PluginClassification unknown))
            { failure = "PolicyPreamble"; return false; }

            var rules = new List<SentinelPolicyRule>();
            string previous = null;
            for (int index = 6; index < lines.Length - 1; index++)
            {
                string line = lines[index];
                if (!line.StartsWith("rule=", StringComparison.Ordinal) ||
                    rules.Count >= MaximumRules)
                { failure = "PolicyRule"; return false; }
                string[] fields = line.Substring(5).Split('|');
                if (fields.Length != 4 ||
                    !TryClassification(fields[0], out PluginClassification kind) ||
                    !CanonicalPluginId(fields[1]) ||
                    !CanonicalVersionOrWildcard(fields[2]) ||
                    !CanonicalHashOrWildcard(fields[3]))
                { failure = "PolicyRule"; return false; }
                if (previous != null && string.CompareOrdinal(previous, fields[1]) >= 0)
                { failure = "RuleOrder"; return false; }
                previous = fields[1];
                rules.Add(new SentinelPolicyRule(kind, fields[1], fields[2], fields[3]));
            }

            string payloadDigest;
            using (SHA256 sha = SHA256.Create()) payloadDigest = Hex(sha.ComputeHash(payload));
            policy = new SentinelPolicy(
                profile,
                sequence,
                issued,
                expires,
                unknown,
                rules.AsReadOnly(),
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
    }
}
