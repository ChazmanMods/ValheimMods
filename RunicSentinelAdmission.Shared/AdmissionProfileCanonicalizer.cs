using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace RunicSentinel.Admission
{
    internal static class AdmissionProfileCanonicalizer
    {
        internal const int MaximumCanonicalBytes = 256 * 1024;
        private const string Header = "RUNIC-SENTINEL-CLIENT-PROFILE/1\n";

        internal static bool TryCreate(
            IEnumerable<AdmissionPluginEvidence> source,
            long capturedUnixSeconds,
            out AdmissionClientProfile profile,
            out string failure)
        {
            profile = null;
            failure = string.Empty;
            if (source == null || capturedUnixSeconds < 0L)
            {
                failure = "profile-missing";
                return false;
            }

            var values = new List<AdmissionPluginEvidence>();
            foreach (AdmissionPluginEvidence value in source)
            {
                if (value == null)
                {
                    failure = "profile-entry-null";
                    return false;
                }
                if (values.Count >= AdmissionProtocolV2.MaximumPlugins)
                {
                    failure = "profile-plugin-cap";
                    return false;
                }
                values.Add(value);
            }
            values.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

            string previous = null;
            var builder = new StringBuilder(Header, Math.Min(32768, 64 + values.Count * 160));
            foreach (AdmissionPluginEvidence value in values)
            {
                if (string.Equals(previous, value.Id, StringComparison.Ordinal))
                {
                    failure = "profile-plugin-duplicate";
                    return false;
                }
                previous = value.Id;
                builder.Append(value.Id).Append('|')
                    .Append(value.Version).Append('|')
                    .Append(value.Sha256).Append('\n');
                if (builder.Length > MaximumCanonicalBytes)
                {
                    failure = "profile-size";
                    return false;
                }
            }

            byte[] canonical = Encoding.UTF8.GetBytes(builder.ToString());
            if (canonical.Length > MaximumCanonicalBytes)
            {
                failure = "profile-size";
                return false;
            }
            string digest;
            using (SHA256 sha = SHA256.Create())
                digest = Hex(sha.ComputeHash(canonical));
            profile = new AdmissionClientProfile(capturedUnixSeconds, digest, values);
            return true;
        }

        internal static string Hex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2"));
            return builder.ToString();
        }

        internal static bool FixedTimeEquals(string left, string right)
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
    }
}
