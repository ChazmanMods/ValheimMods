using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using RunicSentinel.Contracts;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private static readonly RSA SigningKey = CreateSigningKey();

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("Steam ticket authentication requires native acceptance and asynchronous validation", SentinelAuthenticationTests.SteamSessionTests.ValidTicket),
                ("Steam rejected and revoked sessions remain denied", SentinelAuthenticationTests.SteamSessionTests.RejectedTicket),
                ("Steam authentication is bound to exact account socket and handle", SentinelAuthenticationTests.SteamSessionTests.ExactConnection),
                ("Steam disconnect end-session and world changes discard evidence", SentinelAuthenticationTests.SteamSessionTests.DisconnectAndWorldChange),
                ("Steam duplicate sessions and capacity fail closed", SentinelAuthenticationTests.SteamSessionTests.DuplicateAndCapacity),
                ("Steam compiled hooks match Windows Linux and administrator boundaries", SentinelAuthenticationTests.SteamSessionTests.CompiledHooks),
                ("capacity request grammar is strict and bounded", CapacityRequestIsStrict),
                ("capacity status extends old documents without breaking policy fields", CapacityStatusIsCompatible),
                ("capacity edits preserve unrelated configuration and encoding", CapacityEditsAreScoped),
                ("invalid or ambiguous capacity configs reject edits", CapacityConfigsRejectAmbiguity),
                ("capacity saves are atomic backed up and reject stale revisions", CapacityPersistenceIsSafe),
                ("capacity endpoint remains server-authorized and restart-only", CapacityAuthorityIsServerOwned),
                ("World Engine optional adapter matches the compiled capacity contract", CapacityAdapterMatchesWorldEngine),
                ("RSA-3072 policy verifies exact canonical v2 bytes", RsaPolicyRoundTrip),
                ("tampered policy wrong keys and noncanonical files fail closed", RsaPolicyRejectsForgery),
                ("policy sequence grammar and enum grammar are strict", PolicyGrammarIsStrict),
                ("v3 passport lists modules roles and bans with strict ordering", PassportV3IsStrict),
                ("installed Valheim crypto surface supports exact RSA verifier", InstalledCryptoSurfaceIsExact),
                ("attestation is sorted immutable and inspection bounded", AttestationIsCanonical),
                ("nonce binding is explicitly non-authenticating", NonceBindingIsHonest),
                ("required forbidden and unknown policy is deterministic", AdmissionIsDeterministic),
                ("untrusted attestation is revalidated before admission", AdmissionRevalidatesEvidence),
                ("evidence provider leases are exact and locally scoped", EvidenceIdentityIsAuthenticated),
                ("evidence provider registry has a hard active capacity", EvidenceProviderCapacityIsBounded),
                ("evidence fairness preserves quiet providers under a flood", EvidenceIsFair),
                ("evidence records requested effective policy and drop state", EvidenceCapturesPolicyState),
                ("evidence enum cursor and sequence overflow fail closed", EvidenceOverflowIsSafe),
                ("flight recorder rotates within a hard two-file bound", FlightRecorderIsBounded),
                ("administrator protocol is bounded and round trips exactly", AdminProtocolIsBounded),
                ("administrator panel operations are server re-authorized", AdminControlIsServerAuthorized),
                ("server administrator ID formats match only authenticated Steam subjects", ServerAdministratorIdFormats),
                ("signed roles native roles revocation and bans use exact authorization rules", ServerAdministratorAuthorization),
                ("first-time setup requires a native administrator and pristine ready state", FirstTimeSetupRules),
                ("setup request carries no identity and authorization precedes replay cache", FirstTimeSetupBoundary),
                ("managed RSA-3072 generation and import use validated provider fallbacks", ManagedRsaProviderIsExact),
                ("direct Sentinel admission accepts a server-evaluated client profile", NetworkCompatibilityTests.CleanProfileIsCompatible),
                ("client and server plugin sets are evaluated asymmetrically", NetworkCompatibilityTests.VersionSnapshotAndPolicyMustMatchExactly),
                ("missing server policy and stale challenges fail closed", NetworkCompatibilityTests.MissingPolicyAndTimestampBoundsFailClosed),
                ("v2 admission codec rejects trailing and oversized data", NetworkCompatibilityTests.RequestCodecIsExactAndRejectsTrailingBytes),
                ("v2 request profiles and bindings use canonical grammar", NetworkCompatibilityTests.RequestIdsAndProfilesUseCanonicalGrammar),
                ("optional admission failure starts a fresh exchange", NetworkCompatibilityTests.OptionalFailureStartsAFreshAdmissionExchange),
                ("direct pre-handshake admission is bounded and non-durable", NetworkCompatibilityTests.RpcSurfaceIsPrivateBoundedAndNonDurable),
                ("runtime workers are generation safe bounded and deduplicated", RuntimeIsBounded),
                ("admission evaluation has bounded low-frequency cost", AdmissionPerformanceIsBounded),
                ("disabled mode is startup inert", DisabledModeIsInert),
                ("dedicated console input is bounded and main-thread executed", DedicatedConsoleIsBounded),
                ("release surface is canonical and standalone", ReleaseIsAligned)
            };
            int failed = 0;
            foreach ((string name, Action run) in tests)
            {
                try { run(); System.Console.WriteLine("PASS " + name); }
                catch (Exception exception)
                {
                    failed++;
                    System.Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            System.Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void RsaPolicyRoundTrip()
        {
            byte[] policyBytes = PolicyBytes();
            PinnedRsaPublicKey key = PublicKey(SigningKey);
            byte[] signature = SigningKey.SignData(
                policyBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            Equal(384, signature.Length);
            True(SentinelPolicy.TryParseAndVerify(
                policyBytes,
                signature,
                key,
                out SentinelPolicy policy,
                out string failure), failure);
            Equal("2026.08.22", policy.Profile);
            Equal(42L, policy.Sequence);
            Equal(2, policy.Rules.Count);
            Equal(PluginClassification.Quarantined, policy.Unknown);
            Equal(64, policy.PayloadDigest.Length);

            byte[] signatureFile = Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n");
            True(SentinelPolicy.TryDecodeSignatureFile(signatureFile, out byte[] decoded, out failure), failure);
            Sequence(signature, decoded);
        }

        private static void RsaPolicyRejectsForgery()
        {
            byte[] bytes = PolicyBytes();
            byte[] signature = SigningKey.SignData(
                bytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            PinnedRsaPublicKey key = PublicKey(SigningKey);
            bytes[bytes.Length - 10] ^= 1;
            False(SentinelPolicy.TryParseAndVerify(bytes, signature, key, out _, out _));
            byte[] pssSignature = SigningKey.SignData(
                PolicyBytes(),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            False(SentinelPolicy.TryParseAndVerify(
                PolicyBytes(),
                pssSignature,
                key,
                out _,
                out _));

            using RSA wrong = CreateSigningKey();
            False(SentinelPolicy.TryParseAndVerify(
                PolicyBytes(),
                signature,
                PublicKey(wrong),
                out _,
                out _));

            byte[] publicFile = PublicKeyFile(SigningKey);
            False(PinnedRsaPublicKey.TryParse(
                publicFile,
                new string('0', 64),
                out _,
                out _));
            RSAParameters weakShape = SigningKey.ExportParameters(false);
            weakShape.Modulus[0] &= 0x7F;
            byte[] weakFile = Encoding.ASCII.GetBytes(
                "RUNIC-RSA-PUBLIC/1\nmodulus=" +
                Convert.ToBase64String(weakShape.Modulus) +
                "\nexponent=AQAB\n");
            string weakPin;
            using (SHA256 sha = SHA256.Create())
                weakPin = SentinelPolicy.Hex(sha.ComputeHash(weakFile));
            False(PinnedRsaPublicKey.TryParse(weakFile, weakPin, out _, out _));
            byte[] signatureFile = Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + " \n");
            False(SentinelPolicy.TryDecodeSignatureFile(signatureFile, out _, out _));

            string crlf = Encoding.UTF8.GetString(PolicyBytes()).Replace("\n", "\r\n");
            byte[] crlfBytes = Encoding.UTF8.GetBytes(crlf);
            byte[] crlfSignature = SigningKey.SignData(
                crlfBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            False(SentinelPolicy.TryParseAndVerify(
                crlfBytes,
                crlfSignature,
                key,
                out _,
                out _));
        }

        private static void PolicyGrammarIsStrict()
        {
            False(TryPolicy(PolicyText(sequence: "01"), out _));
            False(TryPolicy(PolicyText(sequence: "0"), out _));
            False(TryPolicy(PolicyText(unknown: "1"), out _));
            False(TryPolicy(PolicyText(firstClassification: "1"), out _));
            False(TryPolicy(PolicyText(reverseRules: true), out _));
            True(TryPolicy(PolicyText(sequence: long.MaxValue.ToString()), out SentinelPolicy maximum));
            Equal(long.MaxValue, maximum.Sequence);
        }

        private static void PassportV3IsStrict()
        {
            True(TryPolicy(PassportV3Text(), out SentinelPolicy policy));
            Equal(3, policy.FormatVersion);
            Equal(3, policy.Modules.Count);
            Equal(1, policy.Administrators.Count);
            Equal("steam", policy.Administrators[0].Authority);
            Equal("76561198000000000", policy.Administrators[0].Subject);
            Equal(1, policy.BannedUsers.Count);
            Equal("76561198999999999", policy.BannedUsers[0].Subject);
            True(policy.Modules.Single(value => value.Id == "runic.sentinel")
                .Capabilities.Contains("security.enforcement"));
            False(TryPolicy(PassportV3Text().Replace(
                "security.enforcement,security.evidence",
                "security.evidence,security.enforcement"), out _));
            False(TryPolicy(PassportV3Text().Replace(
                "unknown-capability=Forbidden",
                "unknown-capability=Unmanaged"), out _));
            False(TryPolicy(PassportV3Text().Replace(
                "role=steam|76561198000000000\n",
                "role=steam|76561198000000000\nrole=steam|76561198000000000\n"), out _));
        }

        private static void InstalledCryptoSurfaceIsExact()
        {
            string game = Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ??
                          @"E:\SteamLibrary\steamapps\common\Valheim";
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(game, "valheim_Data", "Managed", "mscorlib.dll"));
            TypeDefinition rsa = assembly.MainModule.Types.Single(type =>
                type.FullName == "System.Security.Cryptography.RSA");
            Method(rsa, "Create");
            Method(rsa, "ImportParameters", "System.Security.Cryptography.RSAParameters");
            Method(
                rsa,
                "VerifyData",
                "System.Byte[]",
                "System.Byte[]",
                "System.Security.Cryptography.HashAlgorithmName",
                "System.Security.Cryptography.RSASignaturePadding");
            TypeDefinition padding = assembly.MainModule.Types.Single(type =>
                type.FullName == "System.Security.Cryptography.RSASignaturePadding");
            Method(padding, "get_Pkcs1");
        }

        private static void AttestationIsCanonical()
        {
            True(AttestationPolicy.TryCanonicalize(
                Enumerable.Range(0, 100).Select(index => Plugin("normal" + index)),
                out _,
                out _,
                out _));
            False(AttestationPolicy.TryCanonicalize(
                Enumerable.Range(0, 1_000).Select(index => Plugin("bounded" + index)),
                out _,
                out _,
                out string thousandFailure));
            Equal("PluginCap", thousandFailure);
            True(AttestationPolicy.TryCanonicalize(
                new[] { Plugin("z"), Plugin("a") },
                out IReadOnlyList<AttestedPlugin> plugins,
                out string canonical,
                out _));
            Equal("a", plugins[0].Id);
            True(canonical.IndexOf("a|", StringComparison.Ordinal) <
                 canonical.IndexOf("z|", StringComparison.Ordinal));
            Throws<NotSupportedException>(() =>
                ((IList<AttestedPlugin>)plugins).Add(Plugin("x")));
            False(AttestationPolicy.TryCanonicalize(
                new[] { Plugin("a"), Plugin("a") },
                out _,
                out _,
                out _));
            False(AttestationPolicy.TryCanonicalize(
                Enumerable.Range(0, 10_000).Select(index => Plugin("p" + index)),
                out _,
                out _,
                out string capFailure));
            Equal("PluginCap", capFailure);
            Throws<ArgumentException>(() => new AttestedPlugin(
                "a",
                "1.0.0",
                new string('a', 64),
                new[] { "same", "same" },
                null));
        }

        private static void NonceBindingIsHonest()
        {
            string digest = new string('a', 64);
            True(AttestationPolicy.TryComputeNonceBinding(
                new string('b', 64),
                digest,
                out string first));
            True(AttestationPolicy.TryComputeNonceBinding(
                new string('c', 64),
                digest,
                out string second));
            False(first == second);
            False(AttestationPolicy.TryComputeNonceBinding("short", digest, out _));
            string contract = Read("RunicSentinel", "Contracts", "SecurityContracts.cs");
            Contains(contract, "ProvidesClientAuthenticityProof");
            Contains(contract, "TryComputeNonceBinding");
            False(contract.Contains("TryAnswerChallenge", StringComparison.Ordinal));
        }

        private static void AdmissionIsDeterministic()
        {
            AttestedPlugin[] source = { Plugin("z.forbidden"), Plugin("unknown") };
            True(AttestationPolicy.TryCanonicalize(
                source,
                out IReadOnlyList<AttestedPlugin> plugins,
                out string canonical,
                out _));
            var snapshot = new AttestationSnapshot(
                AttestationPolicy.Digest(canonical),
                plugins,
                1L);
            AdmissionDecision result = AdmissionPolicy.Evaluate(Policy(), snapshot, "player");
            Equal(AdmissionDisposition.Deny, result.Disposition);
            Equal(42L, result.PolicySequence);
            Equal("2026.08.22", result.PolicyProfile);
            True(result.Findings.Any(value => value.Rule == "RequiredMissing"));
            True(result.Findings.Any(value => value.Rule == "ForbiddenPresent"));

            True(TryPolicy(PolicyText(
                unknown: "Unmanaged",
                firstClassification: "Unmanaged"), out SentinelPolicy grayPolicy));
            True(AttestationPolicy.TryCanonicalize(
                new[] { Plugin("a.required") }, out IReadOnlyList<AttestedPlugin> grayPlugins,
                out string grayCanonical, out _));
            AdmissionDecision gray = AdmissionPolicy.Evaluate(
                grayPolicy,
                new AttestationSnapshot(AttestationPolicy.Digest(grayCanonical), grayPlugins, 1L),
                "player");
            Equal(AdmissionDisposition.Allow, gray.Disposition);
            True(gray.Findings.Any(value => value.Rule == "GrayListPresent"));
        }

        private static void AdmissionRevalidatesEvidence()
        {
            var duplicate = new AttestationSnapshot(
                new string('a', 64),
                new[] { Plugin("a"), Plugin("a") },
                1L);
            AdmissionDecision invalid = AdmissionPolicy.Evaluate(Policy(), duplicate, "player");
            Equal(AdmissionDisposition.Deny, invalid.Disposition);
            Equal("InvalidAttestation", invalid.Findings.Single().Rule);

            True(AttestationPolicy.TryCanonicalize(
                new[] { Plugin("a") },
                out IReadOnlyList<AttestedPlugin> plugins,
                out _,
                out _));
            var wrongDigest = new AttestationSnapshot(new string('b', 64), plugins, 1L);
            Equal(AdmissionDisposition.Deny,
                AdmissionPolicy.Evaluate(Policy(), wrongDigest, "player").Disposition);
        }

        private static void AdmissionPerformanceIsBounded()
        {
            SentinelPolicy policy = Policy();
            True(AttestationPolicy.TryCanonicalize(
                new[] { Plugin("a.required") },
                out IReadOnlyList<AttestedPlugin> plugins,
                out string canonical,
                out _));
            var snapshot = new AttestationSnapshot(
                AttestationPolicy.Digest(canonical), plugins, 1L);
            const int iterations = 10000;
            var stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < iterations; index++)
            {
                AdmissionDecision decision = AdmissionPolicy.Evaluate(policy, snapshot, "player");
                Equal(AdmissionDisposition.Allow, decision.Disposition);
            }
            stopwatch.Stop();
            System.Console.WriteLine(
                "     admission x" + iterations + " ms=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F2"));
            True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                "Admission evaluation exceeded its generous release performance ceiling.");
        }

        private static void EvidenceIdentityIsAuthenticated()
        {
            var ledger = new EvidenceLedger();
            using ISentinelEvidenceProviderLease lease = ledger.RegisterProvider("runic.owner");
            True(lease.IsActive);
            True(lease.Sink.TryAppend(
                "actor",
                "rule",
                "correlation",
                FindingConfidence.High,
                EnforcementAction.Warn,
                "bounded detail",
                out SecurityEvidence accepted));
            Equal("runic.owner", accepted.ProviderModuleId);
            Throws<InvalidOperationException>(() => ledger.RegisterProvider("runic.owner"));
            lease.Dispose();
            False(lease.IsActive);
            False(lease.Sink.TryAppend(
                "actor",
                "rule",
                "correlation",
                FindingConfidence.High,
                EnforcementAction.Warn,
                "bounded detail",
                out _));
            using ISentinelEvidenceProviderLease replacement =
                ledger.RegisterProvider("runic.owner");
            True(replacement.IsActive);
        }

        private static void EvidenceIsFair()
        {
            var ledger = new EvidenceLedger();
            using ISentinelEvidenceProviderLease noisyLease = ledger.RegisterProvider("runic.noisy");
            using ISentinelEvidenceProviderLease quietLease = ledger.RegisterProvider("runic.quiet");
            for (int index = 0; index < 300; index++)
                True(noisyLease.Sink.TryAppend(
                    "actor", "rule", "noisy-" + index,
                    FindingConfidence.High, EnforcementAction.Warn, "detail", out _));
            for (int index = 0; index < 2; index++)
                True(quietLease.Sink.TryAppend(
                    "actor", "rule", "quiet-" + index,
                    FindingConfidence.High, EnforcementAction.Warn, "detail", out _));
            EvidenceReadSnapshot snapshot = ledger.ReadAfter(0L, 1000);
            Equal(EvidenceLedger.CapacityPerProvider + 2, snapshot.Entries.Count);
            Equal(2, snapshot.Entries.Count(value => value.ProviderModuleId == "runic.quiet"));
            EvidenceProviderStatus noisyStatus = snapshot.Providers.Single(
                value => value.ProviderModuleId == "runic.noisy");
            Equal(300L, noisyStatus.AcceptedEntries);
            Equal(300L - EvidenceLedger.CapacityPerProvider, noisyStatus.DroppedEntries);
            Throws<NotSupportedException>(() =>
                ((IList<SecurityEvidence>)snapshot.Entries).Add(snapshot.Entries[0]));
        }

        private static void EvidenceProviderCapacityIsBounded()
        {
            var ledger = new EvidenceLedger();
            var leases = new List<ISentinelEvidenceProviderLease>();
            for (int index = 0; index < EvidenceLedger.MaximumProviders; index++)
                leases.Add(ledger.RegisterProvider("runic.provider-" + index));
            Throws<InvalidOperationException>(() =>
                ledger.RegisterProvider("runic.provider-overflow"));
            Equal(EvidenceLedger.MaximumProviders, ledger.ReadAfter(0L, 0).Providers.Count);
            foreach (ISentinelEvidenceProviderLease lease in leases) lease.Dispose();
        }

        private static void EvidenceCapturesPolicyState()
        {
            var ledger = new EvidenceLedger();
            ledger.SetPolicySequence(42L);
            using ISentinelEvidenceProviderLease lease = ledger.RegisterProvider("runic.provider");
            True(lease.Sink.TryAppend(
                "actor",
                "rule",
                "correlation",
                FindingConfidence.Low,
                EnforcementAction.Ban,
                "privacy bounded",
                out SecurityEvidence value));
            Equal(EnforcementAction.Ban, value.RequestedAction);
            Equal(EnforcementAction.Warn, value.EffectiveAction);
            Equal(42L, value.PolicySequence);
            False(lease.Sink.TryAppend(
                "actor",
                "rule",
                "correlation",
                FindingConfidence.High,
                EnforcementAction.Warn,
                "control\ntext",
                out _));
            EvidenceReadSnapshot snapshot = ledger.ReadAfter(0L, 256);
            Equal(42L, snapshot.PolicySequence);
            Equal(1L, snapshot.Providers.Single().DroppedEntries);
        }

        private static void EvidenceOverflowIsSafe()
        {
            var ledger = new EvidenceLedger();
            using ISentinelEvidenceProviderLease lease = ledger.RegisterProvider("runic.provider");
            False(lease.Sink.TryAppend(
                "actor", "rule", "correlation",
                (FindingConfidence)999,
                EnforcementAction.Warn,
                "detail",
                out _));
            False(lease.Sink.TryAppend(
                "actor", "rule", "correlation",
                FindingConfidence.High,
                (EnforcementAction)999,
                "detail",
                out _));
            Equal(0, ledger.ReadAfter(long.MaxValue, 256).Entries.Count);
            typeof(EvidenceLedger).GetField("_sequence",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ledger, long.MaxValue);
            False(lease.Sink.TryAppend(
                "actor", "rule", "correlation",
                FindingConfidence.High,
                EnforcementAction.Warn,
                "detail",
                out _));
            Equal(long.MaxValue, ledger.ReadAfter(long.MaxValue, 256).NewestSequence);

            var overflowLedger = new EvidenceLedger();
            typeof(EvidenceLedger).GetField("_token",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(overflowLedger, long.MaxValue);
            Throws<InvalidOperationException>(() =>
                overflowLedger.RegisterProvider("runic.provider"));
            Equal(0, overflowLedger.ReadAfter(0L, 256).Providers.Count);
        }

        private static void FlightRecorderIsBounded()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "RunicSentinelFlightTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var ledger = new EvidenceLedger();
                using (var recorder = new SentinelFlightRecorder(ledger, null, root))
                using (ISentinelEvidenceProviderLease lease = ledger.RegisterProvider("runic.recorder"))
                    for (int index = 0; index < 1000; index++)
                        True(lease.Sink.TryAppend(
                            "steam:76561198000000000",
                            "bounded-flight-test",
                            "record-" + index,
                            FindingConfidence.High,
                            EnforcementAction.Warn,
                            new string('x', 512),
                            out _));
                string directory = Path.Combine(root, "RunicSentinel", "flight-recorder");
                string current = Path.Combine(directory, "security-current.log");
                string previous = Path.Combine(directory, "security-previous.log");
                True(File.Exists(current));
                True(File.Exists(previous));
                True(new FileInfo(current).Length <= SentinelFlightRecorder.MaximumFileBytes);
                True(new FileInfo(previous).Length <= SentinelFlightRecorder.MaximumFileBytes);
                Equal(2, Directory.GetFiles(directory, "*.log").Length);
                True(File.ReadAllText(current).StartsWith(
                    "RUNIC-SENTINEL-FLIGHT/1\n",
                    StringComparison.Ordinal));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void RuntimeIsBounded()
        {
            string source = Read("RunicSentinel", "Runtime", "SentinelRuntime.cs");
            Contains(source, "Task.Run(");
            Contains(source, "generation != _generation");
            Contains(source, "token.IsCancellationRequested");
            Contains(source, "AttestationPolicy.MaximumPlugins");
            Contains(source, "MaximumTotalPluginBytes");
            Contains(source, "new Dictionary<string, FileEvidence>(PathComparer)");
            Contains(source, "RuntimeInformation.IsOSPlatform(OSPlatform.Windows)");
            Contains(source, "token.ThrowIfCancellationRequested()");
            Contains(source, "HashStablePlugin(");
            Contains(source, "long remaining = expectedLength");
            Contains(source, "TryReadStableBounded(");
            Contains(source, "stream.ReadByte() != -1");
            Contains(source, "PolicyRollbackOrEquivocation");
            False(source.Contains("File.ReadAllBytes", StringComparison.Ordinal));
            False(source.Contains("File.ReadAllText", StringComparison.Ordinal));
            False(source.Contains("FileShare.ReadWrite", StringComparison.Ordinal));
            False(source.Contains("Assembly.Load", StringComparison.Ordinal));
            False(source.Contains("Process.GetProcesses", StringComparison.Ordinal));
            False(source.Contains("UnityEngine", StringComparison.Ordinal));
            int capture = source.IndexOf("descriptors = CaptureDescriptors()", StringComparison.Ordinal);
            int worker = source.IndexOf("Task.Run(", StringComparison.Ordinal);
            True(capture >= 0 && capture < worker);
        }

        private static void DisabledModeIsInert()
        {
            string plugin = Read("RunicSentinel", "Plugin.cs");
            int enabled = plugin.IndexOf("if (!(SentinelConfig.Enabled?.Value ?? true))", StringComparison.Ordinal);
            int runtime = plugin.IndexOf("_runtime = new SentinelRuntime()", StringComparison.Ordinal);
            True(enabled >= 0 && enabled < runtime);
            Contains(plugin, "no worker or network handlers were created");
        }

        private static void DedicatedConsoleIsBounded()
        {
            string commands = Read(
                "RunicSentinel", "Runtime", "SentinelOperatorCommands.cs");
            Contains(commands, "Application.isBatchMode");
            Contains(commands, "System.Console.ReadLine()");
            Contains(commands, "ConcurrentQueue<string>");
            Contains(commands, "TickDedicatedConsole()");
            Contains(commands, "handled++ < 8");
            Contains(commands, "line.Length > 1024");
            Contains(commands, "_queuedDedicatedLines) > 32");
            Contains(Read("RunicSentinel", "Plugin.cs"),
                "_operatorCommands?.TickDedicatedConsole()");
        }

        private static void AdminProtocolIsBounded()
        {
            var input = new SentinelAdminDocument
            {
                Sequence = 77,
                Profile = "strict-runic",
                RequiredMods = "a.mod|1.2.3|*\nb.mod|2.0.0|" + new string('a', 64),
                Administrators = "steam|76561198000000000",
                BannedUsers = "steam|76561198999999999",
                AdmissionMode = "Required",
                VeryHighThreshold = "2",
                HighThreshold = "3",
                EnforcementWindowSeconds = "90",
                ManagedSigningKey = true
            };
            byte[] bytes = SentinelAdminProtocol.Encode(input);
            True(bytes.Length <= SentinelAdminProtocol.MaximumWireBytes);
            True(SentinelAdminProtocol.TryDecode(bytes, out SentinelAdminDocument output));
            Equal(input.Sequence, output.Sequence);
            Equal(input.Profile, output.Profile);
            Equal(input.RequiredMods, output.RequiredMods);
            Equal(input.Administrators, output.Administrators);
            Equal(input.AdmissionMode, output.AdmissionMode);
            True(output.ManagedSigningKey);
            False(SentinelAdminProtocol.TryDecode(
                new byte[SentinelAdminProtocol.MaximumWireBytes + 1], out _));
            Sequence(SentinelAdminProtocol.EncodeTool("backup"),
                Encoding.UTF8.GetBytes("RUNIC-SENTINEL-ADMIN-TOOL/1\nbackup\n"));
            Throws<ArgumentException>(() => SentinelAdminProtocol.EncodeTool("shell"));
        }

        private static void AdminControlIsServerAuthorized()
        {
            string control = Read("RunicSentinel", "Runtime", "SentinelAdminControl.cs");
            Contains(control, "ReceiveRequest(ZRpc rpc, ZPackage package)");
            Contains(control, "_serverConnections.TryGetValue(rpc");
            Contains(control, "FindExactReadyPeer(network, rpc)");
            Contains(control, "connection.Rpc.Invoke(ResponseRpc, package)");
            Contains(control, "MaximumTrackedPeers = 64");
            False(control.Contains("ZRoutedRpc", StringComparison.Ordinal));
            False(control.Contains("GetPeer(sender)", StringComparison.Ordinal));
            Contains(control, "SentinelTransportIdentity.TryResolvePeer");
            Contains(control, "_runtime.IsAdministrator(authority, subject)");
            Contains(control, "MaximumReplayEntries = 256");
            Contains(control, "RequestLifetimeTicks");
            Contains(control, "Fixed(cached.RequestDigest, digest)");
            string panel = Read("RunicSentinel", "Runtime", "SentinelAdminPanel.cs");
            Contains(panel, "KeyCode.F3");
            Contains(panel, "SentinelAdminPanel.IsOpen");
            Contains(panel, "__result = false");
            Contains(panel, "BlocksLocalPlayer");
            Contains(panel, "PlayerAttackInput");
            Contains(panel, "Character.StartAttack");
            Contains(panel, "UpdatePlacement");
            Contains(panel, "HarmonyBefore(\"chazman.RunicBuildCamera\")");
            Contains(panel, "UpdateBuildGuiInput");
            Contains(panel, "private static bool Prefix() => !SentinelAdminPanel.IsOpen;");
            Contains(panel, "RunicSentinelValheimSkin");
            Contains(panel, "CreateWoodTexture");
            Contains(panel, "CreateInsetTexture");
            Contains(panel, "RUNIC SENTINEL FORGE");
            Contains(panel, "GUIContent.none");
            Contains(panel,
                "Every operation is independently re-authorized by the server.");
            string managed = Read("RunicSentinel", "Runtime", "SentinelManagedPolicyService.cs");
            Contains(managed, "server-private");
            Contains(managed, "CreateManagedRsa3072()");
            Contains(managed, "parameters.Modulus.Length == 384");
            Contains(managed, "RSASignaturePadding.Pkcs1");
            Contains(Read("RunicSentinel", "Plugin.cs"), "CreateVerifiedBackupNow");
        }

        private static void ServerAdministratorIdFormats()
        {
            const string id = "76561198000000001";
            foreach (string entry in new[] { id, "Steam_" + id, "V_" + id })
                True(SentinelAdministratorRules.MatchesSteamList("steam", id, value => value == entry));
            False(SentinelAdministratorRules.MatchesSteamList("steam", id, value => value == "V_76561198000000002"));
            foreach (string bad in new[] { "", "0", "01", " " + id, id + " ", "Steam_" + id, "V_" + id, "PlayerName", "18446744073709551616" })
                False(SentinelAdministratorRules.MatchesSteamList("steam", bad, _ => true));
            False(SentinelAdministratorRules.MatchesSteamList("playfab.entity", id, _ => true));
            False(SentinelAdministratorRules.MatchesSteamList("Steam", id, _ => true));
            False(SentinelAdministratorRules.MatchesSteamList("steam", id, null));
            var entries = new HashSet<string> { "V_" + id };
            True(SentinelAdministratorRules.MatchesSteamList("steam", id, entries.Contains));
            entries.Clear();
            False(SentinelAdministratorRules.MatchesSteamList("steam", id, entries.Contains));
        }

        private static void ServerAdministratorAuthorization()
        {
            foreach (bool signed in new[] { false, true })
            foreach (bool native in new[] { false, true })
            foreach (bool banned in new[] { false, true })
                Equal(!banned && (signed || native), SentinelAdministratorRules.Allows(signed, native, banned));
        }

        private static void FirstTimeSetupRules()
        {
            foreach (bool native in new[] { false, true })
            foreach (bool trustMaterial in new[] { false, true })
            foreach (bool ready in new[] { false, true })
            foreach (bool banned in new[] { false, true })
                Equal(native && !trustMaterial && ready && !banned,
                    SentinelAdministratorRules.CanInitialize(native, trustMaterial, ready, banned));
        }

        private static void FirstTimeSetupBoundary()
        {
            True(SentinelAdminProtocol.TryDecodeTool(SentinelAdminProtocol.EncodeTool("bootstrap"), out string tool));
            Equal("bootstrap", tool);
            False(SentinelAdminProtocol.TryDecodeTool(Encoding.UTF8.GetBytes(
                "RUNIC-SENTINEL-ADMIN-TOOL/1\nbootstrap steam somebody-else\n"), out _));
            var input = new SentinelAdminDocument { SetupAvailable = true, AdministratorSource = "Server administrator / local host" };
            True(SentinelAdminProtocol.TryDecode(SentinelAdminProtocol.Encode(input), out SentinelAdminDocument output));
            True(output.SetupAvailable);
            Equal(input.AdministratorSource, output.AdministratorSource);
            string legacy = Encoding.UTF8.GetString(SentinelAdminProtocol.Encode(input));
            legacy = string.Join("\n", legacy.Split('\n').Where(line => !line.StartsWith("setup-available=") && !line.StartsWith("administrator-source=")));
            True(SentinelAdminProtocol.TryDecode(Encoding.UTF8.GetBytes(legacy), out output));
            False(output.SetupAvailable);
            Equal(string.Empty, output.AdministratorSource);
            string control = Read("RunicSentinel", "Runtime", "SentinelAdminControl.cs");
            string receive = control.Substring(control.IndexOf("private void ReceiveRequest", StringComparison.Ordinal));
            True(receive.IndexOf("SentinelServerAdministrator.IsAdministrator", StringComparison.Ordinal) < receive.IndexOf("_cache.TryGetValue", StringComparison.Ordinal));
            True(receive.IndexOf("_runtime.IsBanned", StringComparison.Ordinal) < receive.IndexOf("_cache.TryGetValue", StringComparison.Ordinal));
            Contains(control, "if (!serverAdministrator)");
            Contains(control, "InitializeFromServerAdministrator(authority, subject)");
            string bridge = Read("RunicSentinel", "Runtime", "SentinelServerAdministrator.cs");
            Contains(bridge, "!network.IsServer()");
            Contains(bridge, "UseServerAdminList");
            Contains(bridge, "!network.IsDedicated() && !UnityEngine.Application.isBatchMode");
            Contains(bridge, "network.IsAdmin(peer.m_socket.GetHostName())");
            Contains(bridge, "as SyncedList");
            string managed = Read("RunicSentinel", "Runtime", "SentinelManagedPolicyService.cs");
            Contains(managed, "PathExists(_privatePath)");
            Contains(managed, "PathExists(Resolve(SentinelConfig.PolicyFile?.Value))");
            Contains(managed, "PathExists(Resolve(SentinelConfig.SignatureFile?.Value))");
            Contains(managed, "PathExists(Resolve(SentinelConfig.PublicKeyFile?.Value))");
            Contains(managed, "lock (_gate)");
            Contains(managed, "if (!CanInitialize)");
            Contains(managed, "return Bootstrap(authority, subject)");
            Contains(managed, "UnknownMods = \"Unmanaged\"");
            Contains(Read("RunicSentinel", "Runtime", "SentinelAdminPanel.cs"), "Set Up Sentinel");
        }

        private static void ManagedRsaProviderIsExact()
        {
            using RSA generated = SentinelManagedPolicyService.CreateManagedRsa3072();
            Equal(3072, generated.KeySize);
            RSAParameters privateParameters = generated.ExportParameters(true);
            Equal(384, privateParameters.Modulus.Length);
            Sequence(new byte[] { 1, 0, 1 }, privateParameters.Exponent);
            byte[] payload = Encoding.UTF8.GetBytes("sentinel-managed-rsa-provider-test");
            byte[] signature;
            using (RSA imported = SentinelManagedPolicyService.ImportManagedRsa3072(
                       privateParameters))
            {
                Equal(3072, imported.KeySize);
                signature = imported.SignData(
                    payload,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                True(imported.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));
            }
            Equal(384, signature.Length);

            string source = Read(
                "RunicSentinel", "Runtime", "SentinelManagedPolicyService.cs");
            Contains(source, "new RSACryptoServiceProvider(3072)");
            Contains(source, "PersistKeyInCsp = false");
            Contains(source, "parameters.Modulus.Length == 384");
        }

        private static void ReleaseIsAligned()
        {
            string plugin = Read("RunicSentinel", "Plugin.cs");
            Contains(plugin, "public const string Version = SentinelVersion.Current");
            Contains(plugin, "standalone plugin");
            Contains(Read("RunicSentinel", "README.md"), "compatibility evidence");
            Equal("security.attest", SentinelCapabilityIds.Attestation);
            Equal("security.enforcement", SentinelCapabilityIds.Enforcement);
            Equal("security.roles", SentinelCapabilityIds.Roles);
            Equal("security.runtime-integrity", SentinelCapabilityIds.RuntimeIntegrity);
            string runtimeSource = Read("RunicSentinel", "Runtime", "SentinelRuntime.cs");
            Contains(runtimeSource, "PolicyCurrentLocked");
            Contains(runtimeSource, "sentinel-passport-expired");
            string backupSource = Read("RunicSentinel", "Runtime", "SentinelTransitionBackup.cs");
            Contains(backupSource, "SentinelRemoteAdmissionMode.Required");
            Contains(backupSource, "sentinel-transition-profile-unverified");
            string recorderSource = Read("RunicSentinel", "Runtime", "SentinelFlightRecorder.cs");
            Contains(recorderSource, "MaximumFileBytes = 512L * 1024L");
            Contains(recorderSource, "security-previous.log");
            Contains(recorderSource, "enforcement remains active");
            Equal("3.0", SentinelCapabilityIds.ProtocolVersion);

            string network = Read("RunicSentinel", "Core", "SentinelNetworkCompatibility.cs");
            Contains(network, "MaximumTrackedConnections = 64");
            Contains(network, "RPC_ServerHandshake");
            Contains(network, "typeof(ZRpc), typeof(string)");
            Contains(network, "m_inviteSecretKey");
            Contains(network, "state.NativeHandshakeSecret");
            False(network.Contains("Array.Empty<object>()", StringComparison.Ordinal));
            Contains(network, "RPC_PeerInfo");
            Contains(network, "AfterPeerInfo");
            Contains(network, "state.Peer.IsReady()");
            Contains(network, "sentinel-peer-info-before-admission");
            Contains(network, "sentinel-native-handshake-resume-timeout");
            Contains(network, "sentinel-peer-info-timeout");
            Contains(network, "PolicyIsCurrentLocked");
            Contains(network, "Stopwatch.GetTimestamp()");
            False(network.Contains("DateTime.UtcNow.Ticks", StringComparison.Ordinal));
            Contains(network, "UnregisterOwnedHandler");
            Contains(network, ".Rpc.Invoke(\"ServerHandshake\"");
            Contains(network, "AdmissionProtocolV2.DirectRpcName");
            False(network.Contains("ZRoutedRpc", StringComparison.Ordinal));

            string enforcement = Read("RunicSentinel", "Runtime", "SentinelEnforcementRuntime.cs");
            Contains(enforcement, "MaximumTrackedPeers = 256");
            Contains(enforcement, "Prune(now)");
            string maps = Read("RunicSentinel", "Runtime", "SentinelNetworkMapWriter.cs");
            Contains(maps, "MaximumZdos = 16384");
            Contains(maps, "MaximumEdges = 2048");
            Contains(maps, "!network.IsServer()");
            string transition = Read("RunicSentinel", "Runtime", "SentinelTransitionBackup.cs");
            Contains(transition, "BackupBeforeTransitions");
            Contains(transition, "ValidateBackup");
            Contains(transition, "[HarmonyPatch(typeof(ZNet), \"ServerLoadWorld\")]");
            Contains(transition, "world.GetSavePaths()");
            Contains(transition, "MaximumWorldBackupFiles = 1024");
            Contains(transition, "new MigrationBackupRequest(");
            False(transition.Contains("world.GetMetaPath()", StringComparison.Ordinal));
            Contains(transition, "FileCopyOutFromCloud");
            Contains(transition, "world.m_fileSource == FileHelpers.FileSource.Cloud");
            Contains(transition, "throw new InvalidOperationException");

            string allSource = string.Join("\n", Directory.GetFiles(
                PathOf("RunicSentinel"),
                "*.cs",
                SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
            False(allSource.Contains("HMAC", StringComparison.OrdinalIgnoreCase));
            False(allSource.Contains("security.attestation", StringComparison.Ordinal));
            Contains(allSource, "RSASignaturePadding.Pkcs1");
            Contains(allSource, "HashAlgorithmName.SHA256");

            string config = Read("RunicSentinel", "RunicSentinel.cfg.example");
            Contains(config, "PublicKeyFile");
            Contains(config, "TrustedPublicKeySha256");
            Contains(config, "server-private");
            Contains(config, "OpenPanel");
            string pluginLifecycle = Read("RunicSentinel", "Plugin.cs");
            int unityStart = pluginLifecycle.IndexOf("private void Start()", StringComparison.Ordinal);
            int initialSnapshot = pluginLifecycle.IndexOf(
                "_runtime.Start(Paths.ConfigPath)", StringComparison.Ordinal);
            True(unityStart >= 0 && initialSnapshot > unityStart);
            string configurationSource = Read("RunicSentinel", "Configuration.cs");
            Contains(configurationSource, "Sampled at startup; changing it requires a restart");
            False(configurationSource.Contains(
                "RemoteAdmissionPolicy.SettingChanged += Notify", StringComparison.Ordinal));
            Contains(Read("RunicSentinel", "Runtime", "SentinelManagedPolicyService.cs"),
                "admission-mode-restart-required");
            string readme = Read("RunicSentinel", "README.md");
            Contains(readme, "F3");
            Contains(readme, "server-managed");
            Contains(readme, "not unforgeable proof");
            using JsonDocument manifest = JsonDocument.Parse(Read("RunicSentinel", "manifest.json"));
            Equal("RunicSentinel", manifest.RootElement.GetProperty("name").GetString());
            Equal("1.4.2", manifest.RootElement.GetProperty("version_number").GetString());
            Sequence(
                new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2350"
                    ,"Chazman-RunicSafety-1.0.2"
                },
                manifest.RootElement.GetProperty("dependencies").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());

            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(BuiltPlugin());
            False(assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "RunicCore"));
            False(assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "RunicPersistence"));
            True(assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "RunicSafety"));
            True(assembly.MainModule.Types.Any(type =>
                type.FullName == "RunicSentinel.Contracts.AttestationSnapshot"));
            AssertInstalledValheim10Contracts();
        }

        private static void AssertInstalledValheim10Contracts()
        {
            string clientRoot = Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ??
                                @"E:\SteamLibrary\steamapps\common\Valheim";
            string serverRoot = Environment.GetEnvironmentVariable("VALHEIM_SERVER_INSTALL") ??
                                @"E:\SteamLibrary\steamapps\common\Valheim dedicated server";
            foreach (string path in new[]
                     {
                         Path.Combine(clientRoot, "valheim_Data", "Managed", "assembly_valheim.dll"),
                         Path.Combine(serverRoot, "valheim_server_Data", "Managed", "assembly_valheim.dll")
                     })
            {
                True(File.Exists(path), "Missing installed Valheim 1.0 assembly: " + path);
                using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(path);
                TypeDefinition znet = game.MainModule.Types.Single(type => type.FullName == "ZNet");
                Equal("System.Void", Method(
                    znet, "RPC_ServerHandshake", "ZRpc", "System.String").ReturnType.FullName);
                Equal("System.Void", Method(znet, "ServerLoadWorld").ReturnType.FullName);
                TypeDefinition world = game.MainModule.Types.Single(type => type.FullName == "World");
                MethodDefinition savePaths = Method(world, "GetSavePaths");
                Equal("System.Collections.Generic.List`1<System.String>",
                    savePaths.ReturnType.FullName);
                True(savePaths.IsPublic && !savePaths.IsStatic);
                False(world.Methods.Any(method =>
                    method.Name == "GetMetaPath" && method.IsPublic));
                FieldDefinition secret = znet.Fields.Single(field =>
                    field.Name == "m_inviteSecretKey");
                True(secret.IsStatic && secret.FieldType.FullName == "System.String");
            }
        }

        internal static SentinelPolicy Policy()
        {
            True(TryPolicy(PolicyText(), out SentinelPolicy policy));
            return policy;
        }

        private static bool TryPolicy(string text, out SentinelPolicy policy)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] signature = SigningKey.SignData(
                bytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return SentinelPolicy.TryParseAndVerify(
                bytes,
                signature,
                PublicKey(SigningKey),
                out policy,
                out _);
        }

        private static byte[] PolicyBytes() => Encoding.UTF8.GetBytes(PolicyText());

        private static string PolicyText(
            string sequence = "42",
            string unknown = "Quarantined",
            string firstClassification = "Required",
            bool reverseRules = false)
        {
            string first = "rule=" + firstClassification + "|a.required|1.0.0|*\n";
            string second = "rule=Forbidden|z.forbidden|*|*\n";
            return "RUNIC-SENTINEL/2\n" +
                   "profile=2026.08.22\n" +
                   "sequence=" + sequence + "\n" +
                   "issued=1\n" +
                   "expires=0\n" +
                   "unknown=" + unknown + "\n" +
                   (reverseRules ? second + first : first + second);
        }

        private static string PassportV3Text() =>
            "RUNIC-SENTINEL/3\n" +
            "profile=runic-suite\n" +
            "sequence=43\n" +
            "issued=1\n" +
            "expires=0\n" +
            "unknown=Forbidden\n" +
            "unknown-capability=Forbidden\n" +
            "rule=Required|a.required|1.0.0|*\n" +
            "module=Both|runic.core|1.1.0|1|foundation.modules,foundation.services,keybindings.registry,notification.publish\n" +
            "module=Both|runic.persistence|1.1.0|1|network.actor-identity-binding,network.peer-admission,network.protocol,network.rpc,persistence.migrate\n" +
            "module=Both|runic.sentinel|1.1.0|3|security.admission,security.attest,security.enforcement,security.evidence,security.roles,security.runtime-integrity\n" +
            "role=steam|76561198000000000\n" +
            "ban=steam|76561198999999999\n";

        private static AttestedPlugin Plugin(string id, string version = "1.0.0") =>
            new AttestedPlugin(
                id,
                version,
                new string('a', 64),
                Array.Empty<string>(),
                Array.Empty<string>());

        private static RSA CreateSigningKey()
        {
            RSA rsa = RSA.Create();
            rsa.KeySize = 3072;
            Equal(3072, rsa.KeySize);
            return rsa;
        }

        private static byte[] PublicKeyFile(RSA rsa)
        {
            RSAParameters parameters = rsa.ExportParameters(false);
            Equal(384, parameters.Modulus.Length);
            Sequence(new byte[] { 1, 0, 1 }, parameters.Exponent);
            return Encoding.ASCII.GetBytes(
                "RUNIC-RSA-PUBLIC/1\nmodulus=" +
                Convert.ToBase64String(parameters.Modulus) +
                "\nexponent=AQAB\n");
        }

        private static PinnedRsaPublicKey PublicKey(RSA rsa)
        {
            byte[] file = PublicKeyFile(rsa);
            string fingerprint;
            using (SHA256 sha = SHA256.Create())
                fingerprint = SentinelPolicy.Hex(sha.ComputeHash(file));
            True(PinnedRsaPublicKey.TryParse(
                file,
                fingerprint,
                out PinnedRsaPublicKey key,
                out string failure), failure);
            return key;
        }

        private static MethodDefinition Method(
            TypeDefinition type,
            string name,
            params string[] parameters)
        {
            MethodDefinition[] matches = type.Methods.Where(method =>
                method.Name == name && method.Parameters
                    .Select(value => value.ParameterType.FullName)
                    .SequenceEqual(parameters)).ToArray();
            Equal(1, matches.Length);
            return matches[0];
        }

        private static string BuiltPlugin() => PathOf(
            "RunicSentinel",
            "bin",
            "Release",
            "netstandard2.1",
            "RunicSentinel.dll");

        private static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));
        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
        private static void Contains(string value, string token) =>
            True(value.Contains(token, StringComparison.Ordinal), "Missing " + token);
        private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
            True(expected.SequenceEqual(actual), "Sequences differ.");
        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
        }
        private static void True(bool value, string message = "Expected true.")
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private static void False(bool value) => True(!value, "Expected false.");
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
