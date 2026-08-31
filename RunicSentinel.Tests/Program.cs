using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Runic.Foundation.Core;
using RunicSentinel.Contracts;
using RunicSentinel.Core;

namespace RunicSentinel.Tests
{
    internal static class Program
    {
        private static readonly RSA SigningKey = CreateSigningKey();

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("RSA-3072 policy verifies exact canonical v2 bytes", RsaPolicyRoundTrip),
                ("tampered policy wrong keys and noncanonical files fail closed", RsaPolicyRejectsForgery),
                ("policy sequence grammar and enum grammar are strict", PolicyGrammarIsStrict),
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
                ("private Sentinel compatibility accepts an exact profile", NetworkCompatibilityTests.CleanProfileIsCompatible),
                ("Sentinel version snapshot and policy match exactly", NetworkCompatibilityTests.VersionSnapshotAndPolicyMustMatchExactly),
                ("Sentinel missing policy and timestamp bounds fail closed", NetworkCompatibilityTests.MissingPolicyAndTimestampBoundsFailClosed),
                ("Sentinel request codec rejects trailing and oversized data", NetworkCompatibilityTests.RequestCodecIsExactAndRejectsTrailingBytes),
                ("Sentinel request IDs and profiles use canonical grammar", NetworkCompatibilityTests.RequestIdsAndProfilesUseCanonicalGrammar),
                ("Sentinel private RPC is bounded and non-durable", NetworkCompatibilityTests.RpcSurfaceIsPrivateBoundedAndNonDurable),
                ("runtime workers are generation safe bounded and deduplicated", RuntimeIsBounded),
                ("disabled mode is startup inert", DisabledModeIsInert),
                ("release surface is canonical and private-key free", ReleaseIsAligned)
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
            string contract = Read("RunicCore", "Api", "SecurityContracts.cs");
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

        private static void ReleaseIsAligned()
        {
            string plugin = Read("RunicSentinel", "Plugin.cs");
            Contains(plugin, "public const string Version = SentinelNetworkCompatibility.PluginVersion");
            Contains(plugin, "Private post-connect compatibility admission");
            Contains(plugin, "self-reported compatibility evidence");
            Equal("security.attest", SentinelCapabilityIds.Attestation);
            Equal("2.0", SentinelCapabilityIds.ProtocolVersion);

            string network = Read("RunicSentinel", "Core", "SentinelNetworkCompatibility.cs");
            Contains(network, "runic.sentinel.admission.request.v1");
            Contains(network, "GetPeer(sender)");
            Contains(network, "peer.IsReady()");
            Contains(network, "MaximumCachedRequests = 256");
            False(network.Contains("IRunicRpcService", StringComparison.Ordinal));
            False(network.Contains("Journal", StringComparison.Ordinal));

            string allSource = string.Join("\n", Directory.GetFiles(
                PathOf("RunicSentinel"),
                "*.cs",
                SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
            False(allSource.Contains("HMAC", StringComparison.OrdinalIgnoreCase));
            False(allSource.Contains("SigningKey", StringComparison.Ordinal));
            False(allSource.Contains("security.attestation", StringComparison.Ordinal));
            Contains(allSource, "RSASignaturePadding.Pkcs1");
            Contains(allSource, "HashAlgorithmName.SHA256");

            string config = Read("RunicSentinel", "RunicSentinel.cfg.example");
            Contains(config, "PublicKeyFile");
            Contains(config, "TrustedPublicKeySha256");
            Contains(config, "private signer remains external");
            Contains(config, "Sentinel never loads it");
            False(config.Contains("SigningKey", StringComparison.Ordinal));
            string readme = Read("RunicSentinel", "README.md");
            Contains(readme, "external Server");
            Contains(readme, "public verification only");
            Contains(readme, "no private key or");
            using JsonDocument manifest = JsonDocument.Parse(Read("RunicSentinel", "manifest.json"));
            Equal("RunicSentinel", manifest.RootElement.GetProperty("name").GetString());
            Equal("1.0.0", manifest.RootElement.GetProperty("version_number").GetString());
            Sequence(
                new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2333"
                },
                manifest.RootElement.GetProperty("dependencies").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());

            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(BuiltPlugin());
            False(assembly.MainModule.AssemblyReferences.Any(reference =>
                reference.Name == "RunicCore" || reference.Name == "RunicPersistence" ||
                reference.Name == "RunicPermissions" || reference.Name == "RunicTransactions"));
            True(assembly.MainModule.Types.Any(type =>
                type.FullName == "RunicSentinel.Contracts.AttestationSnapshot"));
        }

        private static SentinelPolicy Policy()
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
