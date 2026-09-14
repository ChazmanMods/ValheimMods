using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicSentinel.Admission;
using RunicSentinelClient.Runtime;

namespace RunicSentinelClient.Tests
{
    internal static class Program
    {
        private const long Now = 1_800_000_000L;
        private static readonly string HashA = new string('a', 64);
        private static readonly string HashB = new string('b', 64);

        private static int Main()
        {
            var tests = new Action[]
            {
                ProfileCanonicalizationIsStable,
                ChallengeReportAndDecisionRoundTrip,
                CodecRejectsTamperingTrailingDataAndCaps,
                FileCollectorHashesAStableBoundedProfile,
                ConnectionPatchHasDiscoverableClassMetadata,
                ClientAssemblyHasMinimalDependencySurface,
                ClientLifecycleIsStrictlyScoped,
                NativeResumeCompletionFollowsSuccessfulInvoke,
                TransportSourceHasNoServerAuthority,
                PackageManifestIsClientOnly
            };
            try
            {
                foreach (Action test in tests)
                {
                    test();
                    System.Console.WriteLine("PASS " + test.Method.Name);
                }
                System.Console.WriteLine("RunicSentinelClient tests passed: " + tests.Length + ".");
                return 0;
            }
            catch (Exception exception)
            {
                System.Console.Error.WriteLine("FAIL " + exception);
                return 1;
            }
        }

        private static void ProfileCanonicalizationIsStable()
        {
            var reversed = new[]
            {
                new AdmissionPluginEvidence("test.beta", "2.0.0", HashB),
                new AdmissionPluginEvidence("test.alpha", "1.0.0", HashA)
            };
            True(AdmissionProfileCanonicalizer.TryCreate(
                reversed, Now, out AdmissionClientProfile first, out string failure), failure);
            Equal("test.alpha", first.Plugins[0].Id);
            True(AdmissionProfileCanonicalizer.TryCreate(
                reversed.Reverse(), Now, out AdmissionClientProfile second, out failure), failure);
            Equal(first.Digest, second.Digest);

            False(AdmissionProfileCanonicalizer.TryCreate(
                new[]
                {
                    new AdmissionPluginEvidence("test.alpha", "1.0.0", HashA),
                    new AdmissionPluginEvidence("test.alpha", "2.0.0", HashB)
                },
                Now,
                out _,
                out failure));
            Equal("profile-plugin-duplicate", failure);
        }

        private static void ChallengeReportAndDecisionRoundTrip()
        {
            var nonce = Enumerable.Range(0, AdmissionProtocolV2.NonceBytes)
                .Select(value => (byte)value).ToArray();
            var challenge = new AdmissionChallenge(
                "0123456789abcdef0123456789abcdef", nonce, Now, Now + 60L);
            byte[] challengeBytes = AdmissionProtocolV2.EncodeChallenge(challenge);
            True(AdmissionProtocolV2.TryDecodeChallenge(
                challengeBytes, out AdmissionChallenge decodedChallenge, out string failure), failure);
            Equal(challenge.RequestId, decodedChallenge.RequestId);
            Sequence(nonce, decodedChallenge.Nonce);

            True(AdmissionProfileCanonicalizer.TryCreate(
                new[] { new AdmissionPluginEvidence("test.alpha", "1.0.0", HashA) },
                Now - 10L,
                out AdmissionClientProfile expected,
                out failure), failure);
            AdmissionReport report = AdmissionProtocolV2.CreateReport(
                decodedChallenge, expected, "1.0.0", Now + 1L);
            byte[] reportBytes = AdmissionProtocolV2.EncodeReport(report);
            True(reportBytes.Length <= AdmissionProtocolV2.MaximumFrameBytes);
            True(AdmissionProtocolV2.TryDecodeReport(
                reportBytes, out AdmissionReport decodedReport, out failure), failure);
            True(AdmissionProtocolV2.TryValidateReport(
                challenge, decodedReport, Now + 2L,
                out AdmissionClientProfile actual, out failure), failure);
            Equal(expected.Digest, actual.Digest);
            Equal("test.alpha", actual.Plugins.Single().Id);

            var decision = new AdmissionDecisionMessage(
                challenge.RequestId, true, true, "compatible", 42L, "runic-suite", Now + 2L);
            True(AdmissionProtocolV2.TryDecodeDecision(
                AdmissionProtocolV2.EncodeDecision(decision),
                out AdmissionDecisionMessage decodedDecision,
                out failure), failure);
            True(decodedDecision.Accepted);
            True(decodedDecision.ResumeHandshake);
            Equal(42L, decodedDecision.PolicySequence);

            var optional = new AdmissionDecisionMessage(
                challenge.RequestId, true, false, "optional-observed", 42L, "runic-suite", Now + 2L);
            True(AdmissionProtocolV2.TryDecodeDecision(
                AdmissionProtocolV2.EncodeDecision(optional), out decodedDecision, out failure), failure);
            False(decodedDecision.ResumeHandshake);
            Throws<ArgumentException>(() => new AdmissionDecisionMessage(
                challenge.RequestId, false, true, "denied", 42L, "runic-suite", Now));
        }

        private static void CodecRejectsTamperingTrailingDataAndCaps()
        {
            AdmissionChallenge challenge = AdmissionProtocolV2.CreateChallenge(Now, 60L);
            byte[] encoded = AdmissionProtocolV2.EncodeChallenge(challenge);
            byte[] trailing = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, trailing, 0, encoded.Length);
            False(AdmissionProtocolV2.TryDecodeChallenge(trailing, out _, out _));

            byte[] oversized = new byte[AdmissionProtocolV2.MaximumFrameBytes + 1];
            False(AdmissionProtocolV2.TryGetKind(oversized, out _, out _));

            var tooMany = new List<AdmissionPluginEvidence>();
            for (int index = 0; index <= AdmissionProtocolV2.MaximumPlugins; index++)
                tooMany.Add(new AdmissionPluginEvidence(
                    "test.mod" + index.ToString("D3"), "1.0.0", HashA));
            False(AdmissionProfileCanonicalizer.TryCreate(
                tooMany, Now, out _, out string failure));
            Equal("profile-plugin-cap", failure);

            True(AdmissionProfileCanonicalizer.TryCreate(
                new[] { new AdmissionPluginEvidence("test.alpha", "1.0.0", HashA) },
                Now,
                out AdmissionClientProfile profile,
                out failure), failure);
            AdmissionReport valid = AdmissionProtocolV2.CreateReport(challenge, profile, "1.0.0", Now);
            var tampered = new AdmissionReport(
                valid.RequestId,
                valid.ClientVersion,
                valid.CapturedUnixSeconds,
                valid.IssuedUnixSeconds,
                new string('c', 64),
                valid.NonceBinding,
                valid.Plugins.ToList());
            False(AdmissionProtocolV2.TryValidateReport(
                challenge, tampered, Now + 1L, out _, out failure));
            Equal("report-digest-mismatch", failure);
        }

        private static void FileCollectorHashesAStableBoundedProfile()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "RunicSentinelClientTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "shared.dll");
                byte[] payload = { 1, 3, 3, 7, 9 };
                File.WriteAllBytes(path, payload);
                var descriptors = new[]
                {
                    new ClientPluginFile("test.beta", "2.0.0", path),
                    new ClientPluginFile("test.alpha", "1.0.0", path)
                };
                True(ClientProfileBuilder.TryBuild(
                    descriptors,
                    () => Now,
                    CancellationToken.None,
                    out AdmissionClientProfile profile,
                    out string failure), failure);
                Equal(2, profile.Plugins.Count);
                string expected;
                using (SHA256 sha = SHA256.Create())
                    expected = AdmissionProfileCanonicalizer.Hex(sha.ComputeHash(payload));
                True(profile.Plugins.All(plugin => plugin.Sha256 == expected));
                Equal(Now, profile.CapturedUnixSeconds);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void ClientAssemblyHasMinimalDependencySurface()
        {
            string assemblyPath = typeof(RunicSentinelClient.Plugin).Assembly.Location;
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            string[] references = assembly.MainModule.AssemblyReferences
                .Select(value => value.Name).ToArray();
            string[] forbiddenReferences =
            {
                "RunicSentinel", "RunicSafety", "RunicCore", "RunicPersistence",
                "assembly_utils", "com.rlabrecque.steamworks.net",
                "UnityEngine.IMGUIModule", "UnityEngine.TextRenderingModule"
            };
            foreach (string forbidden in forbiddenReferences)
                False(references.Contains(forbidden, StringComparer.Ordinal));
            True(references.Contains("0Harmony", StringComparer.Ordinal));

            string[] forbiddenTypeWords =
            {
                "Admin", "PrivateKey", "Backup", "Enforcement", "FlightRecorder",
                "SupportReport", "Panel"
            };
            foreach (TypeDefinition type in assembly.MainModule.Types.SelectMany(AllTypes))
                foreach (string word in forbiddenTypeWords)
                    False(type.FullName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);

            int nativeResumeLiterals = assembly.MainModule.Types.SelectMany(AllTypes)
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Count(instruction => instruction.OpCode == OpCodes.Ldstr &&
                                      string.Equals(instruction.Operand as string,
                                          "ServerHandshake", StringComparison.Ordinal));
            Equal(1, nativeResumeLiterals);
            Equal("chazman.RunicSentinelClient", RunicSentinelClient.Plugin.Guid);
            Equal("chazman.RunicSentinel.Admission.v2", AdmissionProtocolV2.DirectRpcName);
            MethodInfo stableHash = typeof(ClientAdmissionTransport).GetMethod(
                "StableHash", BindingFlags.Static | BindingFlags.NonPublic);
            True(stableHash != null);
            int rpcHash = (int)stableHash.Invoke(null, new object[]
                { AdmissionProtocolV2.DirectRpcName });
            Equal(1958020084, rpcHash);
            False(new[] { 243851712, -517879146, -1939829389, 1656310437 }
                .Contains(rpcHash));
        }

        private static void ConnectionPatchHasDiscoverableClassMetadata()
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                typeof(RunicSentinelClient.Plugin).Assembly.Location);
            TypeDefinition patch = assembly.MainModule.Types.Single(type =>
                type.FullName == typeof(ClientTransportPatches).FullName);
            True(patch.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"));
            MethodDefinition prefix = patch.Methods.Single(method =>
                method.Name == "ZNetOnNewConnectionPrefix");
            True(prefix.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib.HarmonyPrefix"));
            True(prefix.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib.HarmonyPriority"));
            False(prefix.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"));
            Equal(2, prefix.Parameters.Count);

            TypeDefinition transport = assembly.MainModule.Types.Single(type =>
                type.FullName == typeof(ClientAdmissionTransport).FullName);
            MethodDefinition attach = transport.Methods.Single(method =>
                method.Name == "AttachServerPeer");
            False(attach.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference called &&
                called.Name == "IsConnected"));
            True(transport.Methods.Any(method =>
                method.Name == "IsTrackedServerPeer"));
            True(transport.Methods.Any(method =>
                method.Name == "RpcConnected"));
            True(transport.Methods.Any(method =>
                method.Name == "TryReadInviteSecretKey"));

            string gamePath = Path.Combine(
                Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ??
                @"E:\SteamLibrary\steamapps\common\Valheim",
                "valheim_Data", "Managed", "assembly_valheim.dll");
            using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(gamePath))
            {
                TypeDefinition znet = game.MainModule.Types.Single(type => type.FullName == "ZNet");
                MethodDefinition[] handshakes = znet.Methods.Where(method =>
                    method.Name == "RPC_ServerHandshake" &&
                    method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(new[] { "ZRpc", "System.String" })).ToArray();
                Equal(1, handshakes.Length);
                Equal("System.Void", handshakes[0].ReturnType.FullName);
                FieldDefinition inviteSecret = znet.Fields.Single(field =>
                    field.Name == "m_inviteSecretKey");
                True(inviteSecret.IsStatic &&
                     inviteSecret.FieldType.FullName == "System.String");
            }

            string root = FindRepositoryRoot();
            string plugin = File.ReadAllText(Path.Combine(
                root, "RunicSentinelClient", "Plugin.cs"));
            True(plugin.Contains(
                "if (!ClientTransportPatches.IsInstalled(Guid))",
                StringComparison.Ordinal));
            string transportSource = File.ReadAllText(Path.Combine(
                root, "RunicSentinelClient", "Runtime", "ClientAdmissionTransport.cs"));
            int attachStart = transportSource.IndexOf(
                "internal bool AttachServerPeer", StringComparison.Ordinal);
            int tickStart = transportSource.IndexOf(
                "internal void Tick", attachStart, StringComparison.Ordinal);
            string attachSource = transportSource.Substring(
                attachStart, tickStart - attachStart);
            int blockedCheck = attachSource.IndexOf(
                "ReferenceEquals(_blockedNetwork, network)", StringComparison.Ordinal);
            int collisionNotice = attachSource.IndexOf(
                "left the direct RPC with its existing Sentinel responder",
                StringComparison.Ordinal);
            True(blockedCheck >= 0 && collisionNotice > blockedCheck);
            True(transportSource.Contains(
                "!IsTrackedServerPeer(network, _serverPeer)",
                StringComparison.Ordinal));
            int connectedCheck = transportSource.IndexOf(
                "if (!RpcConnected(rpc))", StringComparison.Ordinal);
            int reportInvoke = transportSource.IndexOf(
                "rpc.Invoke(",
                connectedCheck,
                StringComparison.Ordinal);
            True(connectedCheck >= 0 && reportInvoke > connectedCheck);
        }

        private static void PackageManifestIsClientOnly()
        {
            string root = FindRepositoryRoot();
            using JsonDocument manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "RunicSentinelClient", "manifest.json")));
            Equal("RunicSentinelClient",
                manifest.RootElement.GetProperty("name").GetString());
            Equal("1.0.1",
                manifest.RootElement.GetProperty("version_number").GetString());
            string[] dependencies = manifest.RootElement.GetProperty("dependencies")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            Sequence(
                new[] { "denikson-BepInExPack_Valheim-5.4.2350" },
                dependencies);
            string iconPath = Path.Combine(root, "RunicSentinelClient", "icon.png");
            byte[] icon = File.ReadAllBytes(iconPath);
            True(icon.Length > 24);
            Sequence(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, icon.Take(8));
            Equal(256, ReadBigEndianInt32(icon, 16));
            Equal(256, ReadBigEndianInt32(icon, 20));
            True(File.Exists(Path.Combine(root, "RunicSentinelClient", "README.md")));
            True(File.Exists(Path.Combine(root, "RunicSentinelClient", "CHANGELOG.md")));
        }

        private static void ClientLifecycleIsStrictlyScoped()
        {
            string root = FindRepositoryRoot();
            string plugin = File.ReadAllText(Path.Combine(
                root, "RunicSentinelClient", "Plugin.cs"));
            True(plugin.Contains("Application.isBatchMode", StringComparison.Ordinal));
            False(plugin.Contains("Chainloader.PluginInfos", StringComparison.Ordinal));
            string runtime = File.ReadAllText(Path.Combine(
                root, "RunicSentinelClient", "Runtime", "ClientAdmissionRuntime.cs"));
            True(runtime.Contains("network.IsServer()", StringComparison.Ordinal));
            True(runtime.Contains("if (network == null)", StringComparison.Ordinal));
            True(runtime.Contains("StopClientServices", StringComparison.Ordinal));
        }

        private static void TransportSourceHasNoServerAuthority()
        {
            string root = FindRepositoryRoot();
            string source = string.Join("\n", Directory.GetFiles(
                    Path.Combine(root, "RunicSentinelClient"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => path.IndexOf(
                    Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) < 0)
                .Select(File.ReadAllText));
            False(source.Contains("ZRoutedRpc", StringComparison.Ordinal));
            False(source.Contains("Disconnect(", StringComparison.Ordinal));
            False(source.Contains("SentinelPolicy", StringComparison.Ordinal));
            False(source.Contains("PrivateKey", StringComparison.Ordinal));
            True(source.Contains("ReferenceEquals(_serverRpc, rpc)", StringComparison.Ordinal));
            True(source.Contains("rpc.Invoke(\"ServerHandshake\"", StringComparison.Ordinal));
        }

        private static void NativeResumeCompletionFollowsSuccessfulInvoke()
        {
            string root = FindRepositoryRoot();
            string source = File.ReadAllText(Path.Combine(
                root, "RunicSentinelClient", "Runtime", "ClientAdmissionTransport.cs"));
            True(source.Contains(
                "MaximumNativeResumeAttempts = 3",
                StringComparison.Ordinal));
            int methodStart = source.IndexOf(
                "private void ReceiveDecision",
                StringComparison.Ordinal);
            int methodEnd = source.IndexOf(
                "private void ClearDisconnected",
                methodStart,
                StringComparison.Ordinal);
            True(methodStart >= 0 && methodEnd > methodStart);
            string method = source.Substring(methodStart, methodEnd - methodStart);
            int invoke = method.IndexOf(
                "rpc.Invoke(\"ServerHandshake\"",
                StringComparison.Ordinal);
            int resumed = method.IndexOf(
                "_nativeHandshakeResumed = true;",
                StringComparison.Ordinal);
            int completedAfterInvoke = method.IndexOf(
                "_completedRequestId = decision.RequestId;",
                invoke,
                StringComparison.Ordinal);
            True(invoke >= 0 && resumed > invoke && completedAfterInvoke > invoke);
            True(method.Contains(
                "_nativeResumeAttempts >= MaximumNativeResumeAttempts",
                StringComparison.Ordinal));
            True(method.Contains("_nativeResumeAttempts++;", StringComparison.Ordinal));
            True(method.Contains(
                "rpc.Invoke(\"ServerHandshake\", nativeHandshakeSecret);",
                StringComparison.Ordinal));
            False(method.Contains("Array.Empty<object>()", StringComparison.Ordinal));
        }

        private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes)
                foreach (TypeDefinition value in AllTypes(nested)) yield return value;
        }

        private static string FindRepositoryRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "RunicSentinelClient")) &&
                    Directory.Exists(Path.Combine(current, "RunicSentinelAdmission.Shared")))
                    return current;
                current = Directory.GetParent(current)?.FullName;
            }
            throw new DirectoryNotFoundException("Repository root was not found.");
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 | bytes[offset + 3];

        private static void Equal<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException(
                    "Expected " + expected + "; actual " + actual + ".");
        }

        private static void True(bool value, string detail = "")
        {
            if (!value) throw new InvalidOperationException(
                "Expected true." + (detail.Length == 0 ? string.Empty : " " + detail));
        }

        private static void False(bool value) => True(!value);

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
