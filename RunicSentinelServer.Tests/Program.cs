using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicSentinel;
using RunicSentinel.Admission;
using RunicSentinel.Core;

namespace RunicSentinelServer.Tests
{
    internal static class Program
    {
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
                ("package identity and version are exact", PackageIdentityIsExact),
                ("manifest dependencies describe the authority package", ManifestIsExact),
                ("authority implementation is source-linked without a full-client dependency", AuthorityCodeIsSourceLinked),
                ("server binary excludes the client admission responder", ClientAdmissionResponderIsAbsent),
                ("server binary excludes administrator GUI and input patches", AdministratorGuiIsAbsent),
                ("server binary cannot invoke the native client handshake resume", NativeClientResumeIsAbsent),
                ("server admission challenge and gate surfaces remain present", ServerAdmissionSurfaceIsPresent),
                ("required admission policy remains fail closed", RequiredAdmissionIsFailClosed),
                ("non-authoritative role gate precedes authority startup", NonAuthoritativeRoleIsInert),
                ("role bootstrap covers creation first connection and destruction", RoleBootstrapCoversLifecycle),
                ("direct admission wire identity remains client compatible", AdmissionWireIdentityIsStable),
                ("full and server authority packages are mutually incompatible", AuthorityPackagesAreMutuallyIncompatible),
                ("optional Portal telemetry discovers the server assembly", PortalBridgeDiscoversServerAssembly),
                ("server documentation and configuration expose no client UI", DocumentationIsServerOnly),
                ("Thunderstore icon is exactly 256 by 256", IconIsCanonical),
                ("assembly dependencies contain no client or GUI module", AssemblyDependenciesAreServerOnly)
            };

            int failed = 0;
            foreach ((string name, Action run) in tests)
            {
                try
                {
                    run();
                    System.Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    failed++;
                    System.Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                }
            }
            System.Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void PackageIdentityIsExact()
        {
            Equal("chazman.RunicSentinelServer", Plugin.Guid);
            Equal("Runic Sentinel Server", Plugin.Name);
            Equal("1.2.0", Plugin.Version);
            Equal("runic.sentinel.server", Plugin.ModuleId);
            Equal("RunicSentinelServer", typeof(Plugin).Assembly.GetName().Name);
            Equal(new System.Version(1, 2, 0, 0), typeof(Plugin).Assembly.GetName().Version);
            True(HasAttributeArgument(ServerAssembly(), "BepInEx.BepInPlugin", Plugin.Guid));
            True(HasAttributeArgument(ServerAssembly(), "BepInEx.BepInIncompatibility", "chazman.RunicSentinel"));
        }

        private static void ManifestIsExact()
        {
            string root = RepositoryRoot();
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                root, "RunicSentinelServer", "manifest.json")));
            JsonElement document = manifest.RootElement;
            Equal("RunicSentinelServer", document.GetProperty("name").GetString());
            Equal("1.2.0", document.GetProperty("version_number").GetString());
            string description = document.GetProperty("description").GetString();
            True(!string.IsNullOrWhiteSpace(description) && description.Length <= 250);
            Sequence(
                new[]
                {
                    "denikson-BepInExPack_Valheim-5.4.2350",
                    "Chazman-RunicSafety-1.0.2"
                },
                document.GetProperty("dependencies").EnumerateArray()
                    .Select(value => value.GetString()));
        }

        private static void AuthorityCodeIsSourceLinked()
        {
            string project = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "RunicSentinelServer", "RunicSentinelServer.csproj"));
            Contains(project, "RUNIC_SENTINEL_SERVER_ONLY");
            Contains(project, "..\\RunicSentinel\\Core\\SentinelNetworkCompatibility.cs");
            Contains(project, "..\\RunicSentinel\\Runtime\\SentinelAdminControl.cs");
            Contains(project, "..\\RunicSentinel\\Runtime\\SentinelServerAdministrator.cs");
            Contains(project, "..\\RunicSentinel\\Core\\SentinelAdministratorRules.cs");
            Contains(project, "..\\RunicSentinel\\Runtime\\SentinelRuntime.cs");
            Contains(project, "..\\RunicSentinelAdmission.Shared\\AdmissionProtocolV2.cs");
            False(project.Contains("**\\*.cs", StringComparison.Ordinal));
            False(project.Contains("SentinelAdminPanel.cs", StringComparison.Ordinal));

            using AssemblyDefinition assembly = ServerAssembly();
            True(AllTypes(assembly).Any(type => type.Name == "SentinelServerAdministrator"));
            True(AllTypes(assembly).Any(type => type.Name == "SentinelAdministratorRules"));
            True(AllTypes(assembly).Any(type => type.Name == "SentinelCapacityBridge"));
            True(AllTypes(assembly).Any(type => type.Name == "SentinelCapacitySettings"));
            True(AllTypes(assembly).Any(type => type.Name == "SentinelCapacityProtocol"));
            string[] references = assembly.MainModule.AssemblyReferences
                .Select(value => value.Name).ToArray();
            False(references.Contains("RunicSentinel", StringComparer.Ordinal));
            False(references.Contains("RunicSentinelClient", StringComparer.Ordinal));
            True(references.Contains("RunicSafety", StringComparer.Ordinal));
        }

        private static void ClientAdmissionResponderIsAbsent()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            TypeDefinition[] types = AllTypes(assembly).ToArray();
            string[] forbiddenMethods =
            {
                "TickClientLocked", "RegisterClientConnectionLocked",
                "ReceiveClientChallengeLocked", "SendClientReportLocked",
                "ReceiveClientDecisionLocked", "TryGetAdmissionClientProfile",
                "CreateReport", "EncodeReport", "TryDecodeChallenge",
                "TryDecodeDecision", "IsDecisionFresh"
            };
            foreach (string name in forbiddenMethods)
                False(types.SelectMany(type => type.Methods).Any(method => method.Name == name));
            False(types.Any(type => type.Name == "ClientConnection"));
            False(types.Any(type => type.FullName == "RunicSentinelClient.Plugin"));
        }

        private static void AdministratorGuiIsAbsent()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            TypeDefinition[] types = AllTypes(assembly).ToArray();
            False(types.Any(type => type.Name == "SentinelAdminPanel"));
            False(types.Any(type => type.Name.StartsWith("SentinelAdmin", StringComparison.Ordinal) &&
                                    type.Name.EndsWith("Patch", StringComparison.Ordinal)));
            False(types.SelectMany(type => type.Methods).Any(method => method.Name == "OnGUI"));
            False(types.SelectMany(type => type.Fields).Any(field => field.Name == "AdminPanelKey"));
            string[] references = assembly.MainModule.AssemblyReferences
                .Select(value => value.Name).ToArray();
            False(references.Contains("UnityEngine.IMGUIModule", StringComparer.Ordinal));
            False(references.Contains("UnityEngine.TextRenderingModule", StringComparer.Ordinal));
        }

        private static void NativeClientResumeIsAbsent()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            foreach (MethodDefinition method in AllTypes(assembly).SelectMany(type => type.Methods)
                         .Where(method => method.HasBody))
                False(method.Body.Instructions.Any(instruction =>
                    instruction.OpCode == OpCodes.Ldstr &&
                    string.Equals(instruction.Operand as string, "ServerHandshake", StringComparison.Ordinal)));
        }

        private static void ServerAdmissionSurfaceIsPresent()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            TypeDefinition transport = AllTypes(assembly).Single(type =>
                type.FullName == "RunicSentinel.Core.SentinelNetworkCompatibility");
            foreach (string name in new[]
                     {
                         "TickServerLocked", "AddServerConnectionLocked", "SendChallengeLocked",
                         "ReceiveServerReportLocked", "SendDecisionLocked", "FailAdmissionLocked",
                         "BeforeServerHandshake", "BeforePeerInfo"
                     })
                True(transport.Methods.Any(method => method.Name == name), name);
            foreach (string patch in new[]
                     {
                         "SentinelServerHandshakePatch", "SentinelPeerInfoPatch",
                         "SentinelDisconnectPatch", "SentinelNetworkDestroyPatch"
                     })
                True(AllTypes(assembly).Any(type => type.Name == patch), patch);

            string gamePath = Path.Combine(
                Environment.GetEnvironmentVariable("VALHEIM_INSTALL") ??
                @"E:\SteamLibrary\steamapps\common\Valheim",
                "valheim_Data", "Managed", "assembly_valheim.dll");
            using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(gamePath);
            TypeDefinition znet = game.MainModule.Types.Single(type => type.FullName == "ZNet");
            MethodDefinition[] handshakes = znet.Methods.Where(method =>
                method.Name == "RPC_ServerHandshake" &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(new[] { "ZRpc", "System.String" })).ToArray();
            Equal(1, handshakes.Length);
            Equal("System.Void", handshakes[0].ReturnType.FullName);
            MethodDefinition[] worldLoads = znet.Methods.Where(method =>
                method.Name == "ServerLoadWorld" && method.Parameters.Count == 0).ToArray();
            Equal(1, worldLoads.Length);
            Equal("System.Void", worldLoads[0].ReturnType.FullName);
            string source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "RunicSentinelServer", "Plugin.cs"));
            Contains(source, "\"RPC_ServerHandshake\", typeof(ZRpc), typeof(string)");
            Contains(source, "RequireInstanceVoid(\"ServerLoadWorld\")");
        }

        private static void RequiredAdmissionIsFailClosed()
        {
            True(SentinelNetworkCompatibility.DisconnectsForFailure(
                SentinelRemoteAdmissionMode.Required));
            False(SentinelNetworkCompatibility.DisconnectsForFailure(
                SentinelRemoteAdmissionMode.Optional));
            False(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                exactConnection: false,
                compliant: true,
                nativeHandshakeReleased: true,
                denied: false));
            False(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                exactConnection: true,
                compliant: false,
                nativeHandshakeReleased: true,
                denied: false));
            True(SentinelNetworkCompatibility.AllowsPeerInfo(
                SentinelRemoteAdmissionMode.Required,
                exactConnection: true,
                compliant: true,
                nativeHandshakeReleased: true,
                denied: false));
            True(Plugin.UnavailableAuthorityFailsClosed(
                SentinelRemoteAdmissionMode.Required,
                authoritative: true,
                authorityStarted: false));
            False(Plugin.UnavailableAuthorityFailsClosed(
                SentinelRemoteAdmissionMode.Optional,
                authoritative: true,
                authorityStarted: false));
            False(Plugin.UnavailableAuthorityFailsClosed(
                SentinelRemoteAdmissionMode.Required,
                authoritative: false,
                authorityStarted: false));
            False(Plugin.UnavailableAuthorityFailsClosed(
                SentinelRemoteAdmissionMode.Required,
                authoritative: true,
                authorityStarted: true));
        }

        private static void NonAuthoritativeRoleIsInert()
        {
            string source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "RunicSentinelServer", "Plugin.cs"));
            int gate = source.IndexOf("if (!authoritative)", StringComparison.Ordinal);
            int runtime = source.IndexOf("_runtime = new SentinelRuntime();", StringComparison.Ordinal);
            True(gate >= 0 && runtime > gate);
            Contains(source, "remains fully inert");
            Contains(source, "if (_authorityStarted && network != null && !network.IsServer())");
            False(source.Contains(
                "if (_authorityStarted && (network == null || !network.IsServer()))",
                StringComparison.Ordinal));
            False(source.Contains("SentinelAdminPanel", StringComparison.Ordinal));
            False(source.Contains("OnGUI", StringComparison.Ordinal));
        }

        private static void RoleBootstrapCoversLifecycle()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            TypeDefinition bootstrap = AllTypes(assembly).Single(type =>
                type.FullName == "RunicSentinel.SentinelServerRoleBootstrap");
            foreach (string name in new[]
                     {
                         "AfterSetServer", "AfterZNetAwake", "AfterNewConnection",
                         "AfterZNetDestroy", "BeforeUnavailableServerHandshake",
                         "BeforeUnavailableWorldLoad"
                     })
                True(bootstrap.Methods.Any(method => method.Name == name), name);
            TypeDefinition plugin = AllTypes(assembly).Single(type =>
                type.FullName == "RunicSentinel.Plugin");
            True(plugin.Methods.Any(method => method.Name == "ObserveConfiguredRole"));
            True(plugin.Methods.Any(method => method.Name == "ObserveConnection"));
            True(plugin.Methods.Any(method => method.Name == "ObserveNetworkDestroyed"));
            False(AllTypes(assembly).Any(type => type.Name == "SentinelNewConnectionPatch"));

            string source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "RunicSentinelServer", "Plugin.cs"));
            int safetyGate = source.IndexOf(
                "InstallPermanentFailClosedGate();", StringComparison.Ordinal);
            int roleObservers = source.IndexOf("InstallRoleObservers();", StringComparison.Ordinal);
            True(safetyGate >= 0 && roleObservers > safetyGate);
            Contains(source, "ReferenceEquals(network, instance._authorityNetwork)");
            Contains(source, "network.Disconnect(exact)");
            Contains(source, "world load was blocked");
        }

        private static void AdmissionWireIdentityIsStable()
        {
            Equal("chazman.RunicSentinel.Admission.v2", AdmissionProtocolV2.DirectRpcName);
            using AssemblyDefinition assembly = ServerAssembly();
            TypeDefinition protocol = AllTypes(assembly).Single(type =>
                type.FullName == "RunicSentinel.Admission.AdmissionProtocolV2");
            foreach (string name in new[]
                     { "CreateChallenge", "EncodeChallenge", "TryDecodeReport", "EncodeDecision" })
                True(protocol.Methods.Any(method => method.Name == name), name);
        }

        private static void AuthorityPackagesAreMutuallyIncompatible()
        {
            using AssemblyDefinition server = ServerAssembly();
            True(HasAttributeArgument(
                server, "BepInEx.BepInIncompatibility", "chazman.RunicSentinel"));

            string fullPath = Path.Combine(
                RepositoryRoot(), "RunicSentinel", "bin", "Release", "netstandard2.1",
                "RunicSentinel.dll");
            True(File.Exists(fullPath));
            using AssemblyDefinition full = AssemblyDefinition.ReadAssembly(fullPath);
            True(HasAttributeArgument(
                full, "BepInEx.BepInIncompatibility", "chazman.RunicSentinelServer"));
        }

        private static void PortalBridgeDiscoversServerAssembly()
        {
            string bridge = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "RunicPortals", "Integration", "SentinelSecurityBridge.cs"));
            Contains(bridge, "RunicSentinel.Api.SentinelIntegrationApi, RunicSentinel");
            Contains(bridge, "RunicSentinel.Api.SentinelIntegrationApi, RunicSentinelServer");
            Contains(bridge, "?? Type.GetType");
        }

        private static void DocumentationIsServerOnly()
        {
            string root = Path.Combine(RepositoryRoot(), "RunicSentinelServer");
            string readme = File.ReadAllText(Path.Combine(root, "README.md"));
            string changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
            string config = File.ReadAllText(Path.Combine(root, "RunicSentinelServer.cfg.example"));
            Contains(readme, "contains no client profile reporter");
            Contains(readme, "remains inert");
            Contains(readme, "intentionally incompatible");
            Contains(changelog, "Compiled out client profile reporting");
            False(config.Contains("[Administrator Panel]", StringComparison.Ordinal));
            False(config.Contains("OpenPanel", StringComparison.Ordinal));
            foreach (string file in new[]
                     { "manifest.json", "README.md", "CHANGELOG.md", "icon.png",
                         "RunicSentinelServer.cfg.example" })
                True(File.Exists(Path.Combine(root, file)), file);
        }

        private static void IconIsCanonical()
        {
            byte[] icon = File.ReadAllBytes(Path.Combine(
                RepositoryRoot(), "RunicSentinelServer", "icon.png"));
            True(icon.Length > 24);
            Sequence(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, icon.Take(8));
            Equal(256, ReadBigEndianInt32(icon, 16));
            Equal(256, ReadBigEndianInt32(icon, 20));
        }

        private static void AssemblyDependenciesAreServerOnly()
        {
            using AssemblyDefinition assembly = ServerAssembly();
            Sequence(
                new[]
                {
                    "0Harmony", "assembly_utils", "assembly_valheim", "BepInEx",
                    "com.rlabrecque.steamworks.net", "netstandard", "RunicSafety",
                    "UnityEngine.CoreModule", "Newtonsoft.Json", "assembly_guiutils",
                    "SoftReferenceableAssets", "Splatform", "UnityEngine.PhysicsModule"
                }.OrderBy(value => value, StringComparer.Ordinal),
                assembly.MainModule.AssemblyReferences.Select(value => value.Name)
                    .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static AssemblyDefinition ServerAssembly() =>
            AssemblyDefinition.ReadAssembly(typeof(Plugin).Assembly.Location);

        private static IEnumerable<TypeDefinition> AllTypes(AssemblyDefinition assembly) =>
            assembly.MainModule.Types.SelectMany(AllTypes);

        private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (TypeDefinition nested in type.NestedTypes)
                foreach (TypeDefinition value in AllTypes(nested)) yield return value;
        }

        private static bool HasAttributeArgument(
            AssemblyDefinition assembly,
            string attributeName,
            string expected)
        {
            TypeDefinition plugin = AllTypes(assembly).Single(type =>
                type.FullName == "RunicSentinel.Plugin");
            return plugin.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == attributeName &&
                attribute.ConstructorArguments.Any(argument =>
                    string.Equals(argument.Value as string, expected, StringComparison.Ordinal)));
        }

        private static string RepositoryRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "RunicSentinelServer")) &&
                    Directory.Exists(Path.Combine(current, "RunicSentinel"))) return current;
                current = Directory.GetParent(current)?.FullName;
            }
            throw new DirectoryNotFoundException("Repository root was not found.");
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 | bytes[offset + 3];

        private static void Contains(string text, string value) =>
            True(text != null && text.Contains(value, StringComparison.Ordinal), value);

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
                throw new InvalidOperationException("Sequences differ: expected [" +
                    string.Join(",", expected) + "]; actual [" + string.Join(",", actual) + "].");
        }
    }
}
