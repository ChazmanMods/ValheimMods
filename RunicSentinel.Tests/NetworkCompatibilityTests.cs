using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RunicSentinel.Admission;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel.Tests
{
    internal static class NetworkCompatibilityTests
    {
        private const long Now = 1_800_000_000L;
        private static readonly string HashA = new string('a', 64);
        private static readonly string HashB = new string('b', 64);

        internal static void CleanProfileIsCompatible()
        {
            using var runtime = new SentinelRuntime();
            SentinelAdmissionCheck result = SentinelNetworkCompatibility.Evaluate(
                runtime,
                Program.Policy(),
                Profile(new AdmissionPluginEvidence("a.required", "1.0.0", HashA)),
                "player");
            True(result.Compatible);
            Equal("compatible", result.Reason);
        }

        internal static void VersionSnapshotAndPolicyMustMatchExactly()
        {
            using var runtime = new SentinelRuntime();
            SentinelPolicy policy = Program.Policy();
            SentinelAdmissionCheck mismatch = SentinelNetworkCompatibility.Evaluate(
                runtime,
                policy,
                Profile(new AdmissionPluginEvidence("a.required", "2.0.0", HashA)),
                "player");
            False(mismatch.Compatible);
            Equal("sentinel-policy-pluginmismatch", mismatch.Reason);

            SentinelAdmissionCheck forbidden = SentinelNetworkCompatibility.Evaluate(
                runtime,
                policy,
                Profile(
                    new AdmissionPluginEvidence("a.required", "1.0.0", HashA),
                    new AdmissionPluginEvidence("z.forbidden", "1.0.0", HashB)),
                "player");
            False(forbidden.Compatible);
            Equal("sentinel-policy-forbiddenpresent", forbidden.Reason);

            // The authoritative server evaluates only the reported client profile. Its own loaded
            // plugin digest is intentionally not part of this decision.
            SentinelAdmissionCheck distinctSets = SentinelNetworkCompatibility.Evaluate(
                runtime,
                policy,
                Profile(new AdmissionPluginEvidence("a.required", "1.0.0", HashB)),
                "player");
            True(distinctSets.Compatible);
        }

        internal static void MissingPolicyAndTimestampBoundsFailClosed()
        {
            using var runtime = new SentinelRuntime();
            Equal(
                "sentinel-server-policy-unavailable",
                SentinelNetworkCompatibility.Evaluate(
                    runtime, null, Profile(), "player").Reason);

            AdmissionChallenge challenge = Challenge();
            True(AdmissionProfileCanonicalizer.TryCreate(
                new[] { new AdmissionPluginEvidence("a.required", "1.0.0", HashA) },
                Now - 10L,
                out AdmissionClientProfile profile,
                out string failure), failure);
            AdmissionReport report = AdmissionProtocolV2.CreateReport(
                challenge, profile, "1.0.0", Now);
            False(AdmissionProtocolV2.TryValidateReport(
                challenge, report, Now + 61L, out _, out failure));
            Equal("challenge-stale", failure);
            False(AdmissionProtocolV2.IsChallengeCurrent(
                challenge, Now + 61L, out failure));
            Equal("challenge-stale", failure);
        }

        internal static void RequestCodecIsExactAndRejectsTrailingBytes()
        {
            AdmissionChallenge challenge = Challenge();
            byte[] encoded = AdmissionProtocolV2.EncodeChallenge(challenge);
            True(encoded.Length <= AdmissionProtocolV2.MaximumFrameBytes);
            True(AdmissionProtocolV2.TryDecodeChallenge(
                encoded, out AdmissionChallenge actual, out string failure), failure);
            Equal(challenge.RequestId, actual.RequestId);
            Sequence(challenge.Nonce, actual.Nonce);

            byte[] trailing = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, trailing, 0, encoded.Length);
            False(AdmissionProtocolV2.TryDecodeChallenge(trailing, out _, out _));
            False(AdmissionProtocolV2.TryGetKind(
                new byte[AdmissionProtocolV2.MaximumFrameBytes + 1], out _, out _));

            AdmissionClientProfile profile = Profile(
                new AdmissionPluginEvidence("a.required", "1.0.0", HashA));
            AdmissionReport report = AdmissionProtocolV2.CreateReport(
                challenge, profile, "1.0.0", Now);
            byte[] reportBytes = AdmissionProtocolV2.EncodeReport(report);
            True(AdmissionProtocolV2.TryDecodeReport(
                reportBytes, out AdmissionReport decoded, out failure), failure);
            True(AdmissionProtocolV2.TryValidateReport(
                challenge, decoded, Now, out AdmissionClientProfile rebuilt, out failure), failure);
            Equal(profile.Digest, rebuilt.Digest);
        }

        internal static void RequestIdsAndProfilesUseCanonicalGrammar()
        {
            Throws<ArgumentException>(() => new AdmissionChallenge(
                new string('A', 32), new byte[AdmissionProtocolV2.NonceBytes], Now, Now + 20L));
            Throws<ArgumentException>(() => new AdmissionPluginEvidence(
                "bad plugin", "1.0.0", HashA));

            var duplicate = new[]
            {
                new AdmissionPluginEvidence("a.required", "1.0.0", HashA),
                new AdmissionPluginEvidence("a.required", "1.0.0", HashA)
            };
            False(AdmissionProfileCanonicalizer.TryCreate(
                duplicate, Now, out _, out string failure));
            Equal("profile-plugin-duplicate", failure);

            AdmissionChallenge challenge = Challenge();
            AdmissionClientProfile profile = Profile(
                new AdmissionPluginEvidence("a.required", "1.0.0", HashA));
            AdmissionReport valid = AdmissionProtocolV2.CreateReport(
                challenge, profile, "1.0.0", Now);
            var tampered = new AdmissionReport(
                valid.RequestId,
                valid.ClientVersion,
                valid.CapturedUnixSeconds,
                valid.IssuedUnixSeconds,
                HashB,
                valid.NonceBinding,
                valid.Plugins.ToList());
            False(AdmissionProtocolV2.TryValidateReport(
                challenge, tampered, Now, out _, out failure));
            Equal("report-digest-mismatch", failure);
        }

        internal static void OptionalFailureStartsAFreshAdmissionExchange()
        {
            using var runtime = new SentinelRuntime();
            object transport = System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(SentinelNetworkCompatibility));
            SetField(transport, "_runtime", runtime);
            SetField(transport, "_mode", SentinelRemoteAdmissionMode.Optional);
            SentinelPolicy policy = Program.Policy();
            SetField(runtime, "_policy", policy);

            Type connectionType = typeof(SentinelNetworkCompatibility).GetNestedType(
                "ServerConnection",
                BindingFlags.NonPublic);
            True(connectionType != null);
            ConstructorInfo constructor = connectionType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
            object connection = constructor.Invoke(new object[] { null, null, 7L });
            var registeredHandler = new object();
            AdmissionChallenge expired = Challenge();
            SetField(connection, "RegisteredHandler", registeredHandler);
            SetField(connection, "StartedTicks", 100L);
            SetField(connection, "DeadlineTicks", 200L);
            SetField(connection, "NextChallengeTicks", 300L);
            SetField(connection, "NextDecisionTicks", 400L);
            SetField(connection, "ResumeDeadlineTicks", 500L);
            SetField(connection, "ChallengeAttempts", 3);
            SetField(connection, "DecisionAttempts", 2);
            SetField(connection, "Challenge", expired);
            SetField(connection, "Policy", policy);
            SetField(connection, "AcceptedReportDigest", HashA);
            SetField(connection, "Compliant", true);
            SetField(connection, "ApprovedAwaitingResume", true);
            SetField(connection, "ReleaseInProgress", true);
            SetField(connection, "NativeReleased", true);

            InvokePrivate(
                transport,
                "FailAdmissionLocked",
                connection,
                "sentinel-client-absent",
                600L);

            Equal(0L, GetField<long>(connection, "StartedTicks"));
            Equal(0L, GetField<long>(connection, "DeadlineTicks"));
            Equal(0L, GetField<long>(connection, "NextChallengeTicks"));
            Equal(0L, GetField<long>(connection, "NextDecisionTicks"));
            Equal(0L, GetField<long>(connection, "ResumeDeadlineTicks"));
            Equal(0, GetField<int>(connection, "ChallengeAttempts"));
            Equal(0, GetField<int>(connection, "DecisionAttempts"));
            Equal<AdmissionChallenge>(null, GetField<AdmissionChallenge>(connection, "Challenge"));
            Equal<SentinelPolicy>(null, GetField<SentinelPolicy>(connection, "Policy"));
            Equal(string.Empty, GetField<string>(connection, "AcceptedReportDigest"));
            False(GetField<bool>(connection, "Compliant"));
            False(GetField<bool>(connection, "ApprovedAwaitingResume"));
            False(GetField<bool>(connection, "ReleaseInProgress"));
            True(GetField<bool>(connection, "NativeReleased"));
            Equal(registeredHandler, GetField<object>(connection, "RegisteredHandler"));

            InvokePrivate(transport, "BeginAdmissionLocked", connection, 700L);

            AdmissionChallenge retry = GetField<AdmissionChallenge>(connection, "Challenge");
            True(retry != null);
            False(string.Equals(expired.RequestId, retry.RequestId, StringComparison.Ordinal));
            Equal(700L, GetField<long>(connection, "StartedTicks"));
            True(GetField<long>(connection, "DeadlineTicks") > 700L);
            Equal(policy, GetField<SentinelPolicy>(connection, "Policy"));
            Equal(0, GetField<int>(connection, "ChallengeAttempts"));
            True(GetField<bool>(connection, "NativeReleased"));
        }

        internal static void RpcSurfaceIsPrivateBoundedAndNonDurable()
        {
            Equal(64, SentinelNetworkCompatibility.MaximumTrackedConnections);
            Equal(3, SentinelNetworkCompatibility.MaximumChallengeAttempts);
            Equal(20L, SentinelNetworkCompatibility.AdmissionGraceSeconds);
            Equal(10L, SentinelNetworkCompatibility.ResumeGraceSeconds);
            Equal(120L, SentinelNetworkCompatibility.PeerInfoGraceSeconds);
            Equal("chazman.RunicSentinel.Admission.v2", AdmissionProtocolV2.DirectRpcName);
            True(SentinelNetworkCompatibility.DisconnectsForFailure(
                SentinelRemoteAdmissionMode.Required));
            False(SentinelNetworkCompatibility.DisconnectsForFailure(
                SentinelRemoteAdmissionMode.Optional));
            False(SentinelNetworkCompatibility.DisconnectsForFailure(
                SentinelRemoteAdmissionMode.Disabled));
            False(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                true,
                false,
                false,
                false));
            False(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                true,
                true,
                false,
                false));
            True(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                true,
                true,
                true,
                false));
            True(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Optional,
                false,
                false,
                false,
                true));

            string[] names = typeof(SentinelNetworkCompatibility)
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic)
                .Select(method => method.Name).ToArray();
            False(names.Any(name => name.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Recover", StringComparison.OrdinalIgnoreCase)));
            False(typeof(AdmissionReport).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(property => property.Name.Contains(
                    "Disposition", StringComparison.OrdinalIgnoreCase)));

            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                root, "RunicSentinel", "Core", "SentinelNetworkCompatibility.cs"));
            int tickStart = source.IndexOf(
                "private void TickClientLocked", StringComparison.Ordinal);
            int tickEnd = source.IndexOf(
                "private ServerConnection AddServerConnectionLocked",
                tickStart,
                StringComparison.Ordinal);
            True(tickStart >= 0 && tickEnd > tickStart);
            string tick = source.Substring(tickStart, tickEnd - tickStart);
            int retainedBinding = tick.IndexOf(
                "FindExactPeer(network, retained.Rpc)", StringComparison.Ordinal);
            int readyOnlyFallback = tick.IndexOf(
                "network.GetServerPeer()", StringComparison.Ordinal);
            True(retainedBinding >= 0 && readyOnlyFallback > retainedBinding);
        }

        private static AdmissionChallenge Challenge() => new AdmissionChallenge(
            "0123456789abcdef0123456789abcdef",
            Enumerable.Range(0, AdmissionProtocolV2.NonceBytes)
                .Select(value => (byte)value).ToArray(),
            Now,
            Now + 60L);

        private static AdmissionClientProfile Profile(
            params AdmissionPluginEvidence[] plugins)
        {
            True(AdmissionProfileCanonicalizer.TryCreate(
                plugins ?? Array.Empty<AdmissionPluginEvidence>(),
                Now - 10L,
                out AdmissionClientProfile profile,
                out string failure), failure);
            return profile;
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    "Expected " + expected + "; actual " + actual + ".");
        }

        private static void True(bool value, string detail = "")
        {
            if (!value) throw new InvalidOperationException(
                "Expected true." + (detail.Length == 0 ? string.Empty : " " + detail));
        }

        private static void False(bool value) => True(!value);

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            True(field != null, "Missing field " + name + ".");
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            True(field != null, "Missing field " + name + ".");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            True(method != null, "Missing method " + name + ".");
            method.Invoke(target, arguments);
        }

        private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException("Sequences differ.");
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
