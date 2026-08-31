using System;
using System.Security.Cryptography;
using System.Text;

namespace RunicSentinel.Core
{
    internal sealed class PinnedRsaPublicKey
    {
        internal const int ModulusBytes = 384;
        internal const int MaximumFileBytes = 1024;
        private static readonly byte[] CanonicalExponent = { 0x01, 0x00, 0x01 };
        private readonly byte[] _modulus;

        private PinnedRsaPublicKey(byte[] modulus, string fingerprint)
        {
            _modulus = (byte[])modulus.Clone();
            Fingerprint = fingerprint;
        }

        internal string Fingerprint { get; }

        internal RSAParameters CreateParameters() => new RSAParameters
        {
            Modulus = (byte[])_modulus.Clone(),
            Exponent = (byte[])CanonicalExponent.Clone()
        };

        internal static bool TryParse(
            byte[] payload,
            string pinnedFingerprint,
            out PinnedRsaPublicKey key,
            out string failure)
        {
            key = null;
            failure = string.Empty;
            if (payload == null || payload.Length == 0 || payload.Length > MaximumFileBytes)
            { failure = "PublicKeySize"; return false; }
            if (!SentinelPolicy.IsLowerHex(pinnedFingerprint, 64))
            { failure = "PublicKeyPin"; return false; }

            string text;
            try { text = new UTF8Encoding(false, true).GetString(payload); }
            catch { failure = "PublicKeyUtf8"; return false; }
            if (text.IndexOf('\r') >= 0 || text.IndexOf('\0') >= 0 ||
                !text.EndsWith("\n", StringComparison.Ordinal))
            { failure = "PublicKeyCanonical"; return false; }
            string[] lines = text.Split('\n');
            if (lines.Length != 4 || lines[0] != "RUNIC-RSA-PUBLIC/1" ||
                !lines[1].StartsWith("modulus=", StringComparison.Ordinal) ||
                lines[2] != "exponent=AQAB" || lines[3].Length != 0)
            { failure = "PublicKeyCanonical"; return false; }

            byte[] modulus;
            try { modulus = Convert.FromBase64String(lines[1].Substring(8)); }
            catch { failure = "PublicKeyEncoding"; return false; }
            if (modulus.Length != ModulusBytes || (modulus[0] & 0x80) == 0 ||
                Convert.ToBase64String(modulus) != lines[1].Substring(8))
            { failure = "PublicKeyShape"; return false; }

            string actualFingerprint;
            using (SHA256 sha = SHA256.Create())
                actualFingerprint = SentinelPolicy.Hex(sha.ComputeHash(payload));
            if (!SentinelPolicy.FixedTimeHexEquals(actualFingerprint, pinnedFingerprint))
            { failure = "PublicKeyPinMismatch"; return false; }
            key = new PinnedRsaPublicKey(modulus, actualFingerprint);
            return true;
        }
    }
}
