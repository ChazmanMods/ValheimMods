using System;
using System.Linq;
using System.Reflection;
using RunicSentinel.Contracts;
using RunicSentinel.Core;

namespace RunicSentinel.Tests
{
    internal static class NetworkCompatibilityTests
    {
        private const long Now = 1_800_000_000L;
        private static readonly string Snapshot = new string('a', 64);
        private static readonly string PolicyDigest = new string('b', 64);

        internal static void CleanProfileIsCompatible()
        {
            SentinelAdmissionCheck result = SentinelNetworkCompatibility.Evaluate(
                Profile(), Request(), Now);
            True(result.Compatible);
            Equal("compatible", result.Reason);
        }

        internal static void VersionSnapshotAndPolicyMustMatchExactly()
        {
            SentinelAdmissionEnvelope version = Request();
            version.Version = "1.0.1";
            Equal("sentinel-request-malformed", Denied(version).Reason);

            SentinelAdmissionEnvelope snapshot = Request();
            snapshot.SnapshotDigest = new string('c', 64);
            Equal("sentinel-snapshot-mismatch", Denied(snapshot).Reason);

            SentinelAdmissionEnvelope policy = Request();
            policy.PolicyDigest = new string('d', 64);
            Equal("sentinel-policy-mismatch", Denied(policy).Reason);

            SentinelAdmissionEnvelope sequence = Request();
            sequence.PolicySequence++;
            Equal("sentinel-policy-mismatch", Denied(sequence).Reason);

            SentinelAdmissionEnvelope disposition = Request();
            disposition.Disposition = AdmissionDisposition.Quarantine;
            Equal("sentinel-policy-not-allow", Denied(disposition).Reason);
        }

        internal static void MissingPolicyAndTimestampBoundsFailClosed()
        {
            Equal("sentinel-server-policy-unavailable",
                SentinelNetworkCompatibility.Evaluate(null, Request(), Now).Reason);

            SentinelAdmissionEnvelope stale = Request();
            stale.IssuedUnixSeconds = Now - 301L;
            Equal("sentinel-request-stale", Denied(stale).Reason);

            SentinelAdmissionEnvelope future = Request();
            future.IssuedUnixSeconds = Now + 61L;
            Equal("sentinel-request-stale", Denied(future).Reason);

            SentinelAdmissionEnvelope futureSnapshot = Request();
            futureSnapshot.SnapshotCapturedUnixSeconds = Now + 61L;
            Equal("sentinel-request-stale", Denied(futureSnapshot).Reason);
        }

        internal static void RequestCodecIsExactAndRejectsTrailingBytes()
        {
            SentinelAdmissionEnvelope expected = Request();
            ZPackage encoded = SentinelNetworkCompatibility.WriteRequest(expected);
            True(encoded.Size() <= SentinelNetworkCompatibility.MaximumRequestBytes);
            True(SentinelNetworkCompatibility.TryReadRequest(
                new ZPackage(encoded.GetArray()), out SentinelAdmissionEnvelope actual));
            Equal(expected.RequestId, actual.RequestId);
            Equal(expected.Version, actual.Version);
            Equal(expected.PolicyDigest, actual.PolicyDigest);
            Equal(expected.PolicySequence, actual.PolicySequence);

            encoded.Write(1234);
            False(SentinelNetworkCompatibility.TryReadRequest(
                new ZPackage(encoded.GetArray()), out _));

            var oversized = new ZPackage();
            oversized.Write(new byte[SentinelNetworkCompatibility.MaximumRequestBytes + 1]);
            False(SentinelNetworkCompatibility.TryReadRequest(
                new ZPackage(oversized.GetArray()), out _));
        }

        internal static void RequestIdsAndProfilesUseCanonicalGrammar()
        {
            SentinelAdmissionEnvelope uppercase = Request();
            uppercase.RequestId = new string('A', 32);
            False(SentinelNetworkCompatibility.TryReadRequest(
                new ZPackage(SentinelNetworkCompatibility.WriteRequest(uppercase).GetArray()), out _));

            SentinelAdmissionEnvelope profile = Request();
            profile.PolicyProfile = "group ";
            False(SentinelNetworkCompatibility.TryReadRequest(
                new ZPackage(SentinelNetworkCompatibility.WriteRequest(profile).GetArray()), out _));
        }

        internal static void RpcSurfaceIsPrivateBoundedAndNonDurable()
        {
            Equal(256, SentinelNetworkCompatibility.MaximumCachedRequests);
            True(SentinelNetworkCompatibility.RequestRpcName.StartsWith(
                "runic.sentinel.", StringComparison.Ordinal));
            True(SentinelNetworkCompatibility.ResponseRpcName.StartsWith(
                "runic.sentinel.", StringComparison.Ordinal));

            string[] names = typeof(SentinelNetworkCompatibility)
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic)
                .Select(method => method.Name)
                .ToArray();
            False(names.Any(name =>
                name.Contains("Admin", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Recover", StringComparison.OrdinalIgnoreCase)));
        }

        private static SentinelAdmissionCheck Denied(SentinelAdmissionEnvelope remote)
        {
            SentinelAdmissionCheck result = SentinelNetworkCompatibility.Evaluate(
                Profile(), remote, Now);
            False(result.Compatible);
            return result;
        }

        private static SentinelNetworkProfile Profile() => new SentinelNetworkProfile(
            Snapshot,
            Now - 10L,
            PolicyDigest,
            42L,
            "group",
            AdmissionDisposition.Allow);

        private static SentinelAdmissionEnvelope Request() => new SentinelAdmissionEnvelope
        {
            RequestId = "0123456789abcdef0123456789abcdef",
            Version = SentinelNetworkCompatibility.PluginVersion,
            SnapshotDigest = Snapshot,
            SnapshotCapturedUnixSeconds = Now - 10L,
            PolicyDigest = PolicyDigest,
            PolicySequence = 42L,
            PolicyProfile = "group",
            Disposition = AdmissionDisposition.Allow,
            IssuedUnixSeconds = Now
        };

        private static void Equal<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException(
                    "Expected " + expected + "; actual " + actual + ".");
        }

        private static void True(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void False(bool value) => True(!value);
    }
}
