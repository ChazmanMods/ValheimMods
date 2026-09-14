using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RunicSentinel.Admission
{
    internal enum AdmissionMessageKind : byte
    {
        Challenge = 1,
        Report = 2,
        Decision = 3
    }

    /// <summary>
    /// Pure, bounded v2 admission protocol compiled from source into the Sentinel variants.
    /// It has no BepInEx, Valheim, Unity, policy, administrator, or persistence dependency.
    /// </summary>
    internal static class AdmissionProtocolV2
    {
        internal const string DirectRpcName = "chazman.RunicSentinel.Admission.v2";
        internal const int WireSchema = 2;
        internal const int MaximumPlugins = 512;
        internal const int MaximumFrameBytes = 256 * 1024;
        internal const int NonceBytes = 32;
        internal const int RequestIdHexLength = 32;
        internal const long MaximumClockSkewSeconds = 60L;
        internal const long MaximumMessageAgeSeconds = 300L;
        internal const long MaximumChallengeLifetimeSeconds = 120L;

        private const int Magic = 0x32415352;
        private const int Terminal = 0x32444E45;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static AdmissionChallenge CreateChallenge(
            long nowUnixSeconds,
            long lifetimeSeconds)
        {
            if (nowUnixSeconds < 0L || lifetimeSeconds < 1L ||
                lifetimeSeconds > MaximumChallengeLifetimeSeconds ||
                nowUnixSeconds > long.MaxValue - lifetimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            var nonce = new byte[NonceBytes];
            var request = new byte[RequestIdHexLength / 2];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
                random.GetBytes(request);
            }
            return new AdmissionChallenge(
                AdmissionProfileCanonicalizer.Hex(request),
                nonce,
                nowUnixSeconds,
                nowUnixSeconds + lifetimeSeconds);
        }

        internal static bool IsChallengeCurrent(
            AdmissionChallenge challenge,
            long nowUnixSeconds,
            out string failure)
        {
            failure = string.Empty;
            if (challenge == null || nowUnixSeconds < 0L)
            {
                failure = "challenge-missing";
                return false;
            }
            if (challenge.DeadlineUnixSeconds < challenge.IssuedUnixSeconds ||
                challenge.DeadlineUnixSeconds - challenge.IssuedUnixSeconds >
                MaximumChallengeLifetimeSeconds)
            {
                failure = "challenge-lifetime";
                return false;
            }
            if (challenge.IssuedUnixSeconds < nowUnixSeconds - MaximumMessageAgeSeconds ||
                challenge.IssuedUnixSeconds > nowUnixSeconds + MaximumClockSkewSeconds ||
                nowUnixSeconds > challenge.DeadlineUnixSeconds)
            {
                failure = "challenge-stale";
                return false;
            }
            return true;
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        internal static AdmissionReport CreateReport(
            AdmissionChallenge challenge,
            AdmissionClientProfile profile,
            string clientVersion,
            long nowUnixSeconds)
        {
            if (!IsChallengeCurrent(challenge, nowUnixSeconds, out string failure))
                throw new InvalidOperationException(failure);
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!TryComputeNonceBinding(
                    challenge.RequestId,
                    challenge.Nonce,
                    profile.Digest,
                    out string binding))
                throw new InvalidOperationException("nonce-binding-failed");
            return new AdmissionReport(
                challenge.RequestId,
                clientVersion,
                profile.CapturedUnixSeconds,
                nowUnixSeconds,
                profile.Digest,
                binding,
                new List<AdmissionPluginEvidence>(profile.Plugins));
        }
#endif

        /// <summary>
        /// Authoritative server seam: validates freshness and challenge binding, reconstructs the
        /// canonical client profile from reported entries, and never trusts a client disposition.
        /// </summary>
        internal static bool TryValidateReport(
            AdmissionChallenge challenge,
            AdmissionReport report,
            long receivedUnixSeconds,
            out AdmissionClientProfile profile,
            out string failure)
        {
            profile = null;
            failure = string.Empty;
            if (!IsChallengeCurrent(challenge, receivedUnixSeconds, out failure)) return false;
            if (report == null ||
                !AdmissionProfileCanonicalizer.FixedTimeEquals(
                    challenge.RequestId, report.RequestId))
            {
                failure = "report-request-mismatch";
                return false;
            }
            if (report.IssuedUnixSeconds < receivedUnixSeconds - MaximumMessageAgeSeconds ||
                report.IssuedUnixSeconds > receivedUnixSeconds + MaximumClockSkewSeconds ||
                report.CapturedUnixSeconds > receivedUnixSeconds + MaximumClockSkewSeconds)
            {
                failure = "report-stale";
                return false;
            }
            if (!AdmissionProfileCanonicalizer.TryCreate(
                    report.Plugins,
                    report.CapturedUnixSeconds,
                    out profile,
                    out failure)) return false;
            if (!AdmissionProfileCanonicalizer.FixedTimeEquals(
                    profile.Digest, report.ProfileDigest))
            {
                profile = null;
                failure = "report-digest-mismatch";
                return false;
            }
            if (!TryComputeNonceBinding(
                    challenge.RequestId,
                    challenge.Nonce,
                    profile.Digest,
                    out string expected) ||
                !AdmissionProfileCanonicalizer.FixedTimeEquals(expected, report.NonceBinding))
            {
                profile = null;
                failure = "report-binding-mismatch";
                return false;
            }
            return true;
        }

        internal static bool TryComputeNonceBinding(
            string requestId,
            byte[] nonce,
            string profileDigest,
            out string binding)
        {
            binding = string.Empty;
            if (!AdmissionValidation.IsLowerHex(requestId, RequestIdHexLength) ||
                nonce == null || nonce.Length != NonceBytes ||
                !AdmissionValidation.IsLowerHex(profileDigest, 64)) return false;
            string input = "RUNIC-SENTINEL-ADMISSION-BINDING/2\n" + requestId + "\n" +
                           AdmissionProfileCanonicalizer.Hex(nonce) + "\n" + profileDigest + "\n";
            using (SHA256 sha = SHA256.Create())
                binding = AdmissionProfileCanonicalizer.Hex(
                    sha.ComputeHash(Encoding.ASCII.GetBytes(input)));
            return true;
        }

        internal static byte[] EncodeChallenge(AdmissionChallenge value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Encode(AdmissionMessageKind.Challenge, writer =>
            {
                WriteString(writer, value.RequestId, RequestIdHexLength);
                byte[] nonce = value.Nonce;
                writer.Write(nonce.Length);
                writer.Write(nonce);
                writer.Write(value.IssuedUnixSeconds);
                writer.Write(value.DeadlineUnixSeconds);
            });
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        internal static byte[] EncodeReport(AdmissionReport value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Plugins.Count > MaximumPlugins)
                throw new ArgumentOutOfRangeException(nameof(value));
            string previous = null;
            return Encode(AdmissionMessageKind.Report, writer =>
            {
                WriteString(writer, value.RequestId, RequestIdHexLength);
                WriteString(writer, value.ClientVersion, 32);
                writer.Write(value.CapturedUnixSeconds);
                writer.Write(value.IssuedUnixSeconds);
                WriteString(writer, value.ProfileDigest, 64);
                WriteString(writer, value.NonceBinding, 64);
                writer.Write(value.Plugins.Count);
                foreach (AdmissionPluginEvidence plugin in value.Plugins)
                {
                    if (plugin == null || previous != null &&
                        string.CompareOrdinal(previous, plugin.Id) >= 0)
                        throw new InvalidDataException("Profile entries must be strictly ordered.");
                    previous = plugin.Id;
                    WriteString(writer, plugin.Id, 128);
                    WriteString(writer, plugin.Version, 64);
                    WriteString(writer, plugin.Sha256, 64);
                }
            });
        }
#endif

        internal static byte[] EncodeDecision(AdmissionDecisionMessage value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Encode(AdmissionMessageKind.Decision, writer =>
            {
                WriteString(writer, value.RequestId, RequestIdHexLength);
                writer.Write(value.Accepted ? (byte)1 : (byte)0);
                writer.Write(value.ResumeHandshake ? (byte)1 : (byte)0);
                WriteString(writer, value.ReasonCode, 96);
                writer.Write(value.PolicySequence);
                WriteString(writer, value.PolicyProfile, 64);
                writer.Write(value.IssuedUnixSeconds);
            });
        }

        internal static bool TryGetKind(
            byte[] bytes,
            out AdmissionMessageKind kind,
            out string failure)
        {
            kind = 0;
            failure = string.Empty;
            try
            {
                using (var stream = NewReadStream(bytes))
                using (var reader = new BinaryReader(stream, StrictUtf8))
                {
                    if (!ReadHeader(reader, out kind, out failure)) return false;
                    return true;
                }
            }
            catch
            {
                kind = 0;
                failure = "frame-malformed";
                return false;
            }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        internal static bool TryDecodeChallenge(
            byte[] bytes,
            out AdmissionChallenge value,
            out string failure)
        {
            value = null;
            failure = string.Empty;
            try
            {
                using (var stream = NewReadStream(bytes))
                using (var reader = new BinaryReader(stream, StrictUtf8))
                {
                    if (!ReadExpectedHeader(reader, AdmissionMessageKind.Challenge, out failure) ||
                        !TryReadString(reader, stream, RequestIdHexLength, out string requestId) ||
                        !AdmissionValidation.IsLowerHex(requestId, RequestIdHexLength))
                    { failure = "challenge-shape"; return false; }
                    int nonceLength = reader.ReadInt32();
                    if (nonceLength != NonceBytes || Remaining(stream) < nonceLength + 20L)
                    { failure = "challenge-nonce"; return false; }
                    byte[] nonce = reader.ReadBytes(nonceLength);
                    long issued = reader.ReadInt64();
                    long deadline = reader.ReadInt64();
                    if (issued < 0L || deadline < issued || deadline - issued > MaximumChallengeLifetimeSeconds ||
                        !ReadTerminal(reader, stream))
                    { failure = "challenge-shape"; return false; }
                    value = new AdmissionChallenge(requestId, nonce, issued, deadline);
                    return true;
                }
            }
            catch { value = null; failure = "challenge-malformed"; return false; }
        }
#endif

        internal static bool TryDecodeReport(
            byte[] bytes,
            out AdmissionReport value,
            out string failure)
        {
            value = null;
            failure = string.Empty;
            try
            {
                using (var stream = NewReadStream(bytes))
                using (var reader = new BinaryReader(stream, StrictUtf8))
                {
                    if (!ReadExpectedHeader(reader, AdmissionMessageKind.Report, out failure) ||
                        !TryReadString(reader, stream, RequestIdHexLength, out string requestId) ||
                        !TryReadString(reader, stream, 32, out string clientVersion))
                    { failure = "report-shape"; return false; }
                    long captured = reader.ReadInt64();
                    long issued = reader.ReadInt64();
                    if (!TryReadString(reader, stream, 64, out string digest) ||
                        !TryReadString(reader, stream, 64, out string binding))
                    { failure = "report-shape"; return false; }
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaximumPlugins)
                    { failure = "report-plugin-cap"; return false; }
                    var plugins = new List<AdmissionPluginEvidence>(count);
                    string previous = null;
                    for (int index = 0; index < count; index++)
                    {
                        if (!TryReadString(reader, stream, 128, out string id) ||
                            !TryReadString(reader, stream, 64, out string version) ||
                            !TryReadString(reader, stream, 64, out string sha256) ||
                            !AdmissionValidation.IsAtom(id, 1, 128) ||
                            !AdmissionValidation.IsAtom(version, 1, 64) ||
                            !AdmissionValidation.IsLowerHex(sha256, 64) ||
                            previous != null && string.CompareOrdinal(previous, id) >= 0)
                        { failure = "report-plugin-shape"; return false; }
                        previous = id;
                        plugins.Add(new AdmissionPluginEvidence(id, version, sha256));
                    }
                    if (!AdmissionValidation.IsLowerHex(requestId, RequestIdHexLength) ||
                        !AdmissionValidation.IsAtom(clientVersion, 1, 32) ||
                        captured < 0L || issued < 0L ||
                        !AdmissionValidation.IsLowerHex(digest, 64) ||
                        !AdmissionValidation.IsLowerHex(binding, 64) ||
                        !ReadTerminal(reader, stream))
                    { failure = "report-shape"; return false; }
                    value = new AdmissionReport(
                        requestId, clientVersion, captured, issued, digest, binding, plugins);
                    return true;
                }
            }
            catch { value = null; failure = "report-malformed"; return false; }
        }

#if !RUNIC_SENTINEL_SERVER_ONLY
        internal static bool TryDecodeDecision(
            byte[] bytes,
            out AdmissionDecisionMessage value,
            out string failure)
        {
            value = null;
            failure = string.Empty;
            try
            {
                using (var stream = NewReadStream(bytes))
                using (var reader = new BinaryReader(stream, StrictUtf8))
                {
                    if (!ReadExpectedHeader(reader, AdmissionMessageKind.Decision, out failure) ||
                        !TryReadString(reader, stream, RequestIdHexLength, out string requestId))
                    { failure = "decision-shape"; return false; }
                    byte acceptedRaw = reader.ReadByte();
                    byte resumeRaw = reader.ReadByte();
                    if (!TryReadString(reader, stream, 96, out string reason))
                    { failure = "decision-shape"; return false; }
                    long sequence = reader.ReadInt64();
                    if (!TryReadString(reader, stream, 64, out string profile))
                    { failure = "decision-shape"; return false; }
                    long issued = reader.ReadInt64();
                    if (!AdmissionValidation.IsLowerHex(requestId, RequestIdHexLength) ||
                        acceptedRaw > 1 || resumeRaw > 1 ||
                        resumeRaw == 1 && acceptedRaw != 1 ||
                        !AdmissionValidation.IsReason(reason) || sequence < 0L || issued < 0L ||
                        profile.Length > 0 && !AdmissionValidation.IsAtom(profile, 1, 64) ||
                        !ReadTerminal(reader, stream))
                    { failure = "decision-shape"; return false; }
                    value = new AdmissionDecisionMessage(
                        requestId,
                        acceptedRaw == 1,
                        resumeRaw == 1,
                        reason,
                        sequence,
                        profile,
                        issued);
                    return true;
                }
            }
            catch { value = null; failure = "decision-malformed"; return false; }
        }

        internal static bool IsDecisionFresh(
            AdmissionDecisionMessage value,
            long nowUnixSeconds)
        {
            return value != null && nowUnixSeconds >= 0L &&
                   value.IssuedUnixSeconds >= nowUnixSeconds - MaximumMessageAgeSeconds &&
                   value.IssuedUnixSeconds <= nowUnixSeconds + MaximumClockSkewSeconds;
        }
#endif

        private static byte[] Encode(AdmissionMessageKind kind, Action<BinaryWriter> body)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8))
            {
                writer.Write(Magic);
                writer.Write(WireSchema);
                writer.Write((byte)kind);
                body(writer);
                writer.Write(Terminal);
                writer.Flush();
                if (stream.Length <= 0L || stream.Length > MaximumFrameBytes)
                    throw new InvalidDataException("Admission frame exceeds its wire bound.");
                return stream.ToArray();
            }
        }

        private static MemoryStream NewReadStream(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 0 || bytes.Length > MaximumFrameBytes)
                throw new InvalidDataException("Admission frame size is invalid.");
            return new MemoryStream(bytes, false);
        }

        private static bool ReadHeader(
            BinaryReader reader,
            out AdmissionMessageKind kind,
            out string failure)
        {
            kind = 0;
            failure = string.Empty;
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != WireSchema)
            { failure = "frame-header"; return false; }
            kind = (AdmissionMessageKind)reader.ReadByte();
            if (kind < AdmissionMessageKind.Challenge || kind > AdmissionMessageKind.Decision)
            { kind = 0; failure = "frame-kind"; return false; }
            return true;
        }

        private static bool ReadExpectedHeader(
            BinaryReader reader,
            AdmissionMessageKind expected,
            out string failure)
        {
            if (!ReadHeader(reader, out AdmissionMessageKind actual, out failure)) return false;
            if (actual == expected) return true;
            failure = "frame-kind";
            return false;
        }

        private static void WriteString(BinaryWriter writer, string value, int maximumCharacters)
        {
            if (value == null || value.Length > maximumCharacters)
                throw new InvalidDataException("Admission string exceeds its bound.");
            byte[] bytes = StrictUtf8.GetBytes(value);
            if (bytes.Length > maximumCharacters)
                throw new InvalidDataException("Admission string exceeds its byte bound.");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static bool TryReadString(
            BinaryReader reader,
            MemoryStream stream,
            int maximumBytes,
            out string value)
        {
            value = string.Empty;
            int length = reader.ReadInt32();
            if (length < 0 || length > maximumBytes || Remaining(stream) < length) return false;
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) return false;
            value = StrictUtf8.GetString(bytes);
            return value.Length <= maximumBytes;
        }

        private static bool ReadTerminal(BinaryReader reader, MemoryStream stream) =>
            Remaining(stream) == 4L && reader.ReadInt32() == Terminal && Remaining(stream) == 0L;

        private static long Remaining(MemoryStream stream) => stream.Length - stream.Position;
    }
}
