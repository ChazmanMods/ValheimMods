using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using RunicSentinel.Core;

namespace SentinelAuthenticationTests
{
    internal static class SteamSessionTests
    {
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        internal static void ValidTicket()
        {
            var evidence = new SteamSessionEvidence(); var socket = new object();
            var entry = evidence.Begin(42, socket, 7);
            Check(!evidence.TryValidate(42, socket, 7, out _), "A connection alone is not authentication");
            evidence.Complete(entry, true);
            Check(!evidence.TryValidate(42, socket, 7, out _), "BeginAuthSession OK is not final authentication");
            evidence.Validate(42, true);
            Check(evidence.TryValidate(42, socket, 7, out _), "Accepted and Steam-validated ticket should work without a certificate");
        }

        internal static void RejectedTicket()
        {
            foreach (bool beginAccepted in new[] { false, true })
            {
                var evidence = new SteamSessionEvidence(); var socket = new object();
                var entry = evidence.Begin(42, socket, 7);
                evidence.Complete(entry, beginAccepted);
                evidence.Validate(42, false);
                evidence.Validate(42, true);
                Check(evidence.IsRejected(42), "Failure must not be replaced by late success");
                Check(!evidence.TryValidate(42, socket, 7, out _), "Rejected/revoked session must deny");
            }
        }

        internal static void ExactConnection()
        {
            var evidence = new SteamSessionEvidence(); var socket = new object();
            var entry = evidence.Begin(42, socket, 7);
            evidence.Complete(entry, true); evidence.Validate(42, true);
            Check(!evidence.TryValidate(42, new object(), 7, out _), "Same account/handle on another socket must deny");
            Check(!evidence.TryValidate(42, socket, 8, out _), "Reused socket with a different handle must deny");
            Check(!evidence.TryValidate(43, socket, 7, out _), "Another account must deny");
            Check(evidence.TryValidate(42, socket, 7, out _), "Original binding stays valid");
        }

        internal static void DisconnectAndWorldChange()
        {
            var evidence = new SteamSessionEvidence(); var socket = new object();
            var old = evidence.Begin(42, socket, 7);
            evidence.Complete(old, true); evidence.Validate(42, true);
            evidence.RemoveConnection(socket);
            evidence.Validate(42, true);
            Check(!evidence.TryValidate(42, socket, 7, out _), "Disconnected callbacks cannot recreate a session");
            var nextSocket = new object(); var next = evidence.Begin(42, nextSocket, 7);
            evidence.Complete(old, true);
            Check(!evidence.TryValidate(42, nextSocket, 7, out _), "Stale completion cannot approve a reconnect");
            evidence.Complete(next, true);
            Check(!evidence.TryValidate(42, nextSocket, 7, out _), "Reconnect needs a new Steam validation event");
            evidence.Validate(42, true);
            Check(evidence.TryValidate(42, nextSocket, 7, out _), "Fresh validated reconnect works");
            evidence.Remove(42);
            Check(!evidence.TryValidate(42, nextSocket, 7, out _), "EndAuthSession clears proof");
            var last = evidence.Begin(42, socket, 9); evidence.Complete(last, true); evidence.Validate(42, true);
            evidence.Clear(); evidence.Complete(last, true); evidence.Validate(42, true);
            Check(!evidence.TryValidate(42, socket, 9, out _), "New world cannot inherit proof");
        }

        internal static void DuplicateAndCapacity()
        {
            var evidence = new SteamSessionEvidence(); var socket = new object();
            var first = evidence.Begin(42, socket, 7); evidence.Complete(first, true); evidence.Validate(42, true);
            Check(evidence.Begin(42, new object(), 8) == null, "Duplicate account must not inherit authentication");
            Check(!evidence.TryValidate(42, socket, 7, out _), "Ambiguous account authentication fails closed");
            evidence.Clear();
            Check(evidence.Begin(0, socket, 7) == null && evidence.Begin(1, null, 7) == null &&
                evidence.Begin(1, socket, 0) == null, "Invalid bindings rejected");
            for (ulong id = 1; id <= 64; id++) Check(evidence.Begin(id, new object(), (uint)id) != null, "Bounded capacity");
            Check(evidence.Begin(65, socket, 65) == null, "Overflow rejected");
            evidence.Remove(1);
            Check(evidence.Begin(65, socket, 65) != null, "Disconnect frees capacity");
        }

        internal static void CompiledHooks()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(SteamSessionEvidence).Assembly.Location);
            string ns = "RunicSentinel.Runtime.";
            foreach (string patch in new[] { "SentinelSteamTicketPatch", "SentinelSteamClosePatch",
                "SentinelSteamServerEndAuthPatch", "SentinelSteamUserEndAuthPatch", "SentinelSteamWorldEndPatch" })
                Check(assembly.MainModule.GetType(ns + patch)?.CustomAttributes.Any(a =>
                    a.AttributeType.FullName == "HarmonyLib.HarmonyPatch") == true, "Missing lifecycle hook " + patch);
            var runtime = assembly.MainModule.GetType(ns + "SentinelSteamSessions");
            var calls = runtime.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as MethodReference).Where(m => m != null).ToList();
            Check(calls.Any(m => m.Name == "CreateGameServer") && calls.Any(m => m.Name == "Create"), "Both Steam callback backends required");
            Check(!calls.Any(m => m.Name == "BeginAuthSession" || m.Name == "IsAdmin"), "Tracker never starts duplicate auth or grants roles");
            var verify = assembly.MainModule.GetType(ns + "SentinelTransportIdentity").Methods.Single(m => m.Name == "TrySteam");
            var verificationCalls = verify.Body.Instructions.Select(i => i.Operand as MethodReference).Where(m => m != null).ToList();
            Check(verificationCalls.Any(m => m.Name == "TryValidate") && verificationCalls.Any(m => m.Name == "IsRejected"), "Certificate fallback and revocation required");
            var admin = assembly.MainModule.GetType(ns + "SentinelAdminControl").Methods.Single(m => m.Name == "ReceiveRequest");
            var adminCalls = admin.Body.Instructions.Select(i => i.Operand as MethodReference).Where(m => m != null).ToList();
            int identity = adminCalls.FindIndex(m => m.Name == "TryResolvePeer");
            int authorization = adminCalls.FindIndex(m => m.DeclaringType.Name == "SentinelAdministratorRules" && m.Name == "Allows");
            Check(identity >= 0 && authorization > identity, "Administrator and ban checks must follow authentication");
            var resolve = assembly.MainModule.GetType(ns + "SentinelSocketTransport");
            Check(resolve != null && resolve.Methods.Any(m => m.Name == "TryUnwrap"), "Shared wrapper resolver missing");
            foreach (string owner in new[] { "SentinelSteamSessions", "SentinelTransportIdentity" })
                Check(assembly.MainModule.GetType(ns + owner).Methods.Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference call &&
                        call.DeclaringType.FullName == ns + "SentinelSocketTransport" && call.Name == "TryResolve"),
                    "Both ticket observation and identity resolution must use exact unwrapped transport: " + owner);
            using (var ccs = AssemblyDefinition.ReadAssembly(@"C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\plugins\shudnal-ConditionalConfigSync\ConditionalConfigSync.dll"))
            {
                var wrapper = ccs.MainModule.GetType("ConditionalConfigSync.ConditionalConfigSync")
                    .NestedTypes.Single(t => t.Name == "ZNetRpcPeerInfoSyncPatch")
                    .NestedTypes.Single(t => t.Name == "BufferingSocket");
                Check(wrapper.BaseType.FullName == "ZPlayFabSocket" && wrapper.Fields.Any(f =>
                    f.Name == "Original" && f.IsPublic && f.IsInitOnly && f.FieldType.FullName == "ISocket"),
                    "Installed ConditionalConfigSync wrapper contract changed");
            }

            // Audit the installed Windows and saved Linux 1.0.12 contracts without loading Unity.
            foreach (string path in new[] {
                @"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll",
                @"E:\SteamLibrary\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed\assembly_valheim.dll",
                @"E:\Valheim Mods\Server Operations\deploy-20260911-valheim-1.0.12\assembly_valheim-linux.dll" })
            {
                using var game = AssemblyDefinition.ReadAssembly(path);
                var method = game.MainModule.GetType("ZSteamMatchmaking").Methods.Single(m => m.Name == "VerifySessionTicket");
                Check(method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 2 &&
                    method.Parameters[1].ParameterType.FullName == "Steamworks.CSteamID", "Ticket hook signature changed: " + path);
                Check(game.MainModule.GetType("ZSteamSocket").Fields.Any(f => f.Name == "m_con" &&
                    f.FieldType.FullName == "Steamworks.HSteamNetConnection"), "Socket handle contract changed: " + path);
                var native = game.MainModule.GetType("ZNet").Methods.Single(m => m.Name == "RPC_PeerInfo");
                Check(native.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "VerifySessionTicket"), "Native handshake must still verify the ticket");
            }
        }
    }
}
