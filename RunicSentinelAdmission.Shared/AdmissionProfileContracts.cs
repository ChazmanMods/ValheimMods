using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicSentinel.Admission
{
    internal sealed class AdmissionPluginEvidence
    {
        internal AdmissionPluginEvidence(string id, string version, string sha256)
        {
            Id = AdmissionValidation.RequireAtom(id, 1, 128, nameof(id));
            Version = AdmissionValidation.RequireAtom(version, 1, 64, nameof(version));
            Sha256 = AdmissionValidation.RequireLowerHex(sha256, 64, nameof(sha256));
        }

        internal string Id { get; }
        internal string Version { get; }
        internal string Sha256 { get; }
    }

    internal sealed class AdmissionClientProfile
    {
        internal AdmissionClientProfile(
            long capturedUnixSeconds,
            string digest,
            IList<AdmissionPluginEvidence> plugins)
        {
            if (capturedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(capturedUnixSeconds));
            CapturedUnixSeconds = capturedUnixSeconds;
            Digest = AdmissionValidation.RequireLowerHex(digest, 64, nameof(digest));
            if (plugins == null) throw new ArgumentNullException(nameof(plugins));
            Plugins = new ReadOnlyCollection<AdmissionPluginEvidence>(
                new List<AdmissionPluginEvidence>(plugins));
        }

        internal long CapturedUnixSeconds { get; }
        internal string Digest { get; }
        internal IReadOnlyList<AdmissionPluginEvidence> Plugins { get; }
    }

    internal sealed class AdmissionChallenge
    {
        internal AdmissionChallenge(
            string requestId,
            byte[] nonce,
            long issuedUnixSeconds,
            long deadlineUnixSeconds)
        {
            RequestId = AdmissionValidation.RequireLowerHex(
                requestId, AdmissionProtocolV2.RequestIdHexLength, nameof(requestId));
            if (nonce == null || nonce.Length != AdmissionProtocolV2.NonceBytes)
                throw new ArgumentOutOfRangeException(nameof(nonce));
            if (issuedUnixSeconds < 0L || deadlineUnixSeconds < issuedUnixSeconds)
                throw new ArgumentOutOfRangeException(nameof(issuedUnixSeconds));
            _nonce = (byte[])nonce.Clone();
            IssuedUnixSeconds = issuedUnixSeconds;
            DeadlineUnixSeconds = deadlineUnixSeconds;
        }

        private readonly byte[] _nonce;
        internal string RequestId { get; }
        internal byte[] Nonce => (byte[])_nonce.Clone();
        internal long IssuedUnixSeconds { get; }
        internal long DeadlineUnixSeconds { get; }
    }

    internal sealed class AdmissionReport
    {
        internal AdmissionReport(
            string requestId,
            string clientVersion,
            long capturedUnixSeconds,
            long issuedUnixSeconds,
            string profileDigest,
            string nonceBinding,
            IList<AdmissionPluginEvidence> plugins)
        {
            RequestId = AdmissionValidation.RequireLowerHex(
                requestId, AdmissionProtocolV2.RequestIdHexLength, nameof(requestId));
            ClientVersion = AdmissionValidation.RequireAtom(
                clientVersion, 1, 32, nameof(clientVersion));
            if (capturedUnixSeconds < 0L || issuedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(capturedUnixSeconds));
            ProfileDigest = AdmissionValidation.RequireLowerHex(
                profileDigest, 64, nameof(profileDigest));
            NonceBinding = AdmissionValidation.RequireLowerHex(
                nonceBinding, 64, nameof(nonceBinding));
            if (plugins == null || plugins.Count > AdmissionProtocolV2.MaximumPlugins)
                throw new ArgumentOutOfRangeException(nameof(plugins));
            CapturedUnixSeconds = capturedUnixSeconds;
            IssuedUnixSeconds = issuedUnixSeconds;
            Plugins = new ReadOnlyCollection<AdmissionPluginEvidence>(
                new List<AdmissionPluginEvidence>(plugins));
        }

        internal string RequestId { get; }
        internal string ClientVersion { get; }
        internal long CapturedUnixSeconds { get; }
        internal long IssuedUnixSeconds { get; }
        internal string ProfileDigest { get; }
        internal string NonceBinding { get; }
        internal IReadOnlyList<AdmissionPluginEvidence> Plugins { get; }
    }

    internal sealed class AdmissionDecisionMessage
    {
        internal AdmissionDecisionMessage(
            string requestId,
            bool accepted,
            bool resumeHandshake,
            string reasonCode,
            long policySequence,
            string policyProfile,
            long issuedUnixSeconds)
        {
            RequestId = AdmissionValidation.RequireLowerHex(
                requestId, AdmissionProtocolV2.RequestIdHexLength, nameof(requestId));
            ReasonCode = AdmissionValidation.RequireReason(reasonCode, nameof(reasonCode));
            if (policySequence < 0L || issuedUnixSeconds < 0L)
                throw new ArgumentOutOfRangeException(nameof(policySequence));
            if (!string.IsNullOrEmpty(policyProfile))
                AdmissionValidation.RequireAtom(policyProfile, 1, 64, nameof(policyProfile));
            if (resumeHandshake && !accepted)
                throw new ArgumentException(
                    "Only an accepted decision may resume the native handshake.",
                    nameof(resumeHandshake));
            Accepted = accepted;
            ResumeHandshake = resumeHandshake;
            PolicySequence = policySequence;
            PolicyProfile = policyProfile ?? string.Empty;
            IssuedUnixSeconds = issuedUnixSeconds;
        }

        internal string RequestId { get; }
        internal bool Accepted { get; }
        internal bool ResumeHandshake { get; }
        internal string ReasonCode { get; }
        internal long PolicySequence { get; }
        internal string PolicyProfile { get; }
        internal long IssuedUnixSeconds { get; }
    }

    internal static class AdmissionValidation
    {
        internal static string RequireAtom(
            string value,
            int minimum,
            int maximum,
            string parameterName)
        {
            if (!IsAtom(value, minimum, maximum))
                throw new ArgumentException("Value is not a canonical ASCII atom.", parameterName);
            return value;
        }

        internal static bool IsAtom(string value, int minimum, int maximum)
        {
            if (value == null || value.Length < minimum || value.Length > maximum) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool safe = character >= 'a' && character <= 'z' ||
                            character >= 'A' && character <= 'Z' ||
                            character >= '0' && character <= '9' ||
                            character == '.' || character == '-' || character == '_';
                if (!safe) return false;
            }
            return true;
        }

        internal static string RequireLowerHex(
            string value,
            int length,
            string parameterName)
        {
            if (!IsLowerHex(value, length))
                throw new ArgumentException(
                    "Value is not canonical lowercase hexadecimal.", parameterName);
            return value;
        }

        internal static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        internal static string RequireReason(string value, string parameterName)
        {
            if (!IsReason(value))
                throw new ArgumentException("Reason code is not canonical.", parameterName);
            return value;
        }

        internal static bool IsReason(string value)
        {
            if (value == null || value.Length < 1 || value.Length > 96) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '-' || character == '.')) return false;
            }
            return true;
        }
    }
}
