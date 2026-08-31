using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;

namespace Runic.Foundation.Persistence.Tests
{
    internal static class PlayerIdentityBindingTests
    {
        internal static void ResolveNeverAutoEnrolls()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000001", storage);
            store.Tick();
            Equal(
                RpcPlayerBindingStatus.Missing,
                store.Resolve(Account("100"), 9001));
            Equal(0, storage.WriteAttempts);
            Equal(0, store.List().Count);
            store.Dispose();
        }

        internal static void EnrollmentPersistsReverseUniqueBijection()
        {
            var storage = new MemoryBindingStorage();
            const string scope = "valheim.0000000000000002";
            var first = NewStore(scope, storage);
            first.Tick();
            PlayerIdentityBindingMutationResult enrolled = first.Enroll(Account("101"), 9001);
            True(enrolled.Success && enrolled.Changed, "Enrollment was not committed.");
            Equal(RpcPlayerBindingStatus.Verified, first.Resolve(Account("101"), 9001));
            first.Dispose();

            var reloaded = NewStore(scope, storage);
            reloaded.Tick();
            Equal(PlayerIdentityBindingStoreState.Ready, reloaded.State);
            Equal(RpcPlayerBindingStatus.Verified, reloaded.Resolve(Account("101"), 9001));
            Equal(RpcPlayerBindingStatus.Conflict, reloaded.Resolve(Account("101"), 9002));
            Equal(RpcPlayerBindingStatus.Conflict, reloaded.Resolve(Account("102"), 9001));
            reloaded.Dispose();
        }

        internal static void EnrollmentRejectsBothConflictDirections()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000003", storage);
            store.Tick();
            True(store.Enroll(Account("201"), 8001).Success, "Initial enrollment failed.");
            PlayerIdentityBindingMutationResult same = store.Enroll(Account("201"), 8001);
            True(same.Success && !same.Changed, "Exact enrollment was not idempotent.");
            Equal("identity-already-bound", store.Enroll(Account("201"), 8002).ReasonCode);
            Equal("player-id-already-bound", store.Enroll(Account("202"), 8001).ReasonCode);
            Equal(1, store.List().Count);
            store.Dispose();
        }

        internal static void RevocationUpdatesBothDirections()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000004", storage);
            store.Tick();
            store.Enroll(Account("301"), 7001);
            store.Enroll(Account("302"), 7002);
            True(store.RevokeIdentity("steam", "301").Changed, "Account revoke did not commit.");
            Equal(RpcPlayerBindingStatus.Missing, store.Resolve(Account("301"), 7001));
            True(store.RevokePlayer(7002).Changed, "Player revoke did not commit.");
            Equal(RpcPlayerBindingStatus.Missing, store.Resolve(Account("302"), 7002));
            Equal(0, store.List().Count);
            store.Dispose();
        }

        internal static void WorldsAreStrictlyIsolated()
        {
            var storage = new MemoryBindingStorage();
            string scope = "valheim.0000000000000005";
            var store = new PlayerIdentityBindingStore(() => scope, storage);
            store.Tick();
            store.Enroll(Account("401"), 6001);
            scope = "valheim.0000000000000006";
            store.Tick();
            Equal(RpcPlayerBindingStatus.Missing, store.Resolve(Account("401"), 6001));
            Equal(0, store.List().Count);
            scope = "valheim.0000000000000005";
            store.Tick();
            Equal(RpcPlayerBindingStatus.Verified, store.Resolve(Account("401"), 6001));
            store.Dispose();
        }

        internal static void CorruptionLatchesUntilExplicitReload()
        {
            var storage = new MemoryBindingStorage();
            const string scope = "valheim.0000000000000007";
            byte[] valid = PlayerIdentityBindingCodec.Encode(
                scope,
                new[] { new PlayerIdentityBindingSnapshot("steam", "501", 5001) });
            byte[] corrupt = (byte[])valid.Clone();
            corrupt[corrupt.Length - 1] ^= 0x5a;
            storage.Set(scope, corrupt);
            var store = NewStore(scope, storage);
            store.Tick();
            Equal(PlayerIdentityBindingStoreState.Corrupt, store.State);
            Equal(RpcPlayerBindingStatus.Stale, store.Resolve(Account("501"), 5001));

            storage.Set(scope, valid);
            store.Tick();
            Equal(PlayerIdentityBindingStoreState.Corrupt, store.State);
            True(store.Reload(out string reason), "Explicit reload did not accept repaired data: " + reason);
            Equal(RpcPlayerBindingStatus.Verified, store.Resolve(Account("501"), 5001));
            store.Dispose();
        }

        internal static void CodecRejectsWorldMismatchAndDuplicateRecords()
        {
            const string scope = "valheim.0000000000000008";
            byte[] valid = PlayerIdentityBindingCodec.Encode(
                scope,
                new[] { new PlayerIdentityBindingSnapshot("steam", "601", 4001) });
            True(!PlayerIdentityBindingCodec.TryDecode(
                    valid,
                    "valheim.0000000000000009",
                    out _,
                    out string mismatch) && mismatch == "binding-world-mismatch",
                "Cross-world binding file was accepted.");

            byte[] duplicatePlayer = EncodeUnchecked(
                scope,
                new PlayerIdentityBindingSnapshot("steam", "601", 4001),
                new PlayerIdentityBindingSnapshot("steam", "602", 4001));
            True(!PlayerIdentityBindingCodec.TryDecode(
                    duplicatePlayer,
                    scope,
                    out _,
                    out string duplicateReason) && duplicateReason == "binding-bijection-invalid",
                "Duplicate reverse mapping was accepted.");
        }

        internal static void FailedWriteCannotChangeLiveBindingState()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.000000000000000a", storage);
            store.Tick();
            True(store.Enroll(Account("701"), 3001).Success, "Initial enrollment failed.");
            storage.FailWrites = true;
            PlayerIdentityBindingMutationResult failed = store.Enroll(Account("702"), 3002);
            True(!failed.Success, "Injected storage failure was reported successful.");
            Equal(RpcPlayerBindingStatus.Verified, store.Resolve(Account("701"), 3001));
            Equal(RpcPlayerBindingStatus.Missing, store.Resolve(Account("702"), 3002));
            Equal(1, store.List().Count);
            storage.FailWrites = false;
            storage.ThrowWrites = true;
            PlayerIdentityBindingMutationResult threw = store.Enroll(Account("703"), 3003);
            True(!threw.Success && threw.ReasonCode == "binding-write-failed",
                "Throwing storage adapter escaped or was reported successful.");
            Equal(RpcPlayerBindingStatus.Missing, store.Resolve(Account("703"), 3003));
            Equal(1, store.List().Count);
            store.Dispose();
        }

        internal static void FileCommitIsAtomicAndBackupIsNeverAutoTrusted()
        {
            True(!FilePlayerIdentityBindingStorage.HasRootPrefix(
                    "/srv/Runic/",
                    "/srv/runic/world.rpb",
                    false),
                "Linux-style path containment ignored case.");
            True(FilePlayerIdentityBindingStorage.HasRootPrefix(
                    "C:\\Runic\\",
                    "c:\\runic\\world.rpb",
                    true),
                "Windows-style path containment did not use platform case semantics.");
            string root = Path.Combine(
                Path.GetTempPath(),
                "runic-binding-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                const string scope = "valheim.000000000000000b";
                var storage = new FilePlayerIdentityBindingStorage(root);
                var store = NewStore(scope, storage);
                store.Tick();
                True(store.Enroll(Account("801"), 2001).Success, "First file commit failed.");
                True(store.Enroll(Account("802"), 2002).Success, "Replacement file commit failed.");
                string path = storage.GetPath(scope);
                True(File.Exists(path), "Atomic target file is missing.");
                True(File.Exists(path + ".bak"), "Atomic replacement backup is missing.");
                store.Dispose();

                byte[] corrupt = File.ReadAllBytes(path);
                corrupt[corrupt.Length - 1] ^= 0x33;
                File.WriteAllBytes(path, corrupt);
                var restarted = NewStore(scope, storage);
                restarted.Tick();
                Equal(PlayerIdentityBindingStoreState.Corrupt, restarted.State);
                Equal(0, restarted.List().Count);
                Equal(RpcPlayerBindingStatus.Stale, restarted.Resolve(Account("801"), 2001));
                True(File.Exists(path + ".bak"), "Diagnostic backup was removed.");
                restarted.Dispose();
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        internal static void ConsoleEnrollmentUsesExactPeerAndLocalOnlyFlags()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.000000000000000c", storage);
            store.Tick();
            RpcPeerIdentity identity = Account("901");
            var peer = new RpcPeerSnapshot(
                41,
                "session-41",
                identity,
                true,
                true,
                new ProtocolHello("peer", Array.Empty<ModuleProtocolState>()),
                DateTime.UtcNow.Ticks);
            var rpc = new FakeRpcService(peer, 1001);
            var registry = new RunicRegistry();
            ModuleRegistration module = registry.RegisterModule(new ModuleDescriptor(
                Plugin.ModuleId,
                Plugin.PluginName,
                Plugin.PluginVersion,
                Plugin.ProtocolVersion,
                new[] { RunicCapabilityIds.ActorIdentityBinding }));
            PlayerIdentityBindingCommands.Attach(store, rpc, module, null);
            var output = new List<string>();
            PlayerIdentityBindingCommands.Execute(
                store,
                rpc,
                module,
                new[] { PlayerIdentityBindingCommands.CommandName, "enroll", "41" },
                output.Add);
            Equal(RpcPlayerBindingStatus.Verified, store.Resolve(identity, 1001));
            True(output.Single().Contains("binding-enrolled", StringComparison.Ordinal),
                "Console enrollment result was not explicit.");

            FieldInfo commandField = typeof(PlayerIdentityBindingCommands).GetField(
                "_command",
                BindingFlags.Static | BindingFlags.NonPublic);
            var command = (Terminal.ConsoleCommand)commandField.GetValue(null);
            True(!command.OnlyServer,
                "Binding command is unavailable to remote clients that must use the direct admin RPC.");
            True(!command.IsNetwork, "Binding command can be forwarded over the network.");
            True(!command.RemoteCommand, "Binding command can be remotely invoked.");
            var exactConsole = new object();
            True(PlayerIdentityBindingCommands.IsExactProcessConsoleContext(exactConsole, exactConsole),
                "Exact process-console context was rejected.");
            True(!PlayerIdentityBindingCommands.IsExactProcessConsoleContext(new object(), exactConsole),
                "Foreign Terminal/chat context was accepted.");
            True(!PlayerIdentityBindingCommands.IsExactProcessConsoleContext(exactConsole, null),
                "Missing process-console identity was accepted.");
            PlayerIdentityBindingCommands.Detach(store);
            module.Dispose();
            store.Dispose();
        }

        internal static void RemoteAdminEnrollmentRequiresBoundSteamAdmin()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.000000000000000d", storage);
            store.Tick();
            RpcPeerSnapshot target = Peer(52, "target-session", Account("76561198000000052"));

            RpcPeerSnapshot nonAdmin = Peer(51, "admin-session", Account("76561198000000051"));
            var deniedRpc = new FakeAdminRpcService(nonAdmin, target, false);
            RpcHandlerResult denied = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(nonAdmin, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                deniedRpc,
                store,
                null);
            Equal(RpcResultCode.Unauthorized, denied.Code);
            Equal(0, storage.WriteAttempts);

            RpcPeerSnapshot playFab = Peer(
                51,
                "admin-session",
                new RpcPeerIdentity(
                    "playfab.entity",
                    "76561198000000051",
                    RpcIdentityAssurance.BackendAccount));
            var claimedAdminRpc = new FakeAdminRpcService(playFab, target, true);
            RpcHandlerResult claimed = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(playFab, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                claimedAdminRpc,
                store,
                null);
            Equal(RpcResultCode.Unauthorized, claimed.Code);
            Equal(0, storage.WriteAttempts);

            RpcPeerSnapshot connectionOnly = Peer(
                51,
                "admin-session",
                new RpcPeerIdentity(
                    "steam",
                    "76561198000000051",
                    RpcIdentityAssurance.ConnectionBound));
            var weakRpc = new FakeAdminRpcService(connectionOnly, target, true);
            RpcHandlerResult weak = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(connectionOnly, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                weakRpc,
                store,
                null);
            Equal(RpcResultCode.Unauthorized, weak.Code);
            Equal(0, storage.WriteAttempts);
            store.Dispose();
        }

        internal static void SteamAdminEnrollsPlayFabTargetEndToEnd()
        {
            var storage = new MemoryBindingStorage();
            const string scope = "valheim.0000000000000012";
            var store = NewStore(scope, storage);
            store.Tick();
            RpcPeerSnapshot admin = Peer(81, "admin-session", Account("76561198000000081"));
            var playFabIdentity = new RpcPeerIdentity(
                "playfab.entity",
                "29f46c8d3bfe4e2391d78667e98e5301",
                RpcIdentityAssurance.BackendAccount);
            RpcPeerSnapshot target = Peer(82, "playfab-target-session", playFabIdentity);
            var rpc = new FakeAdminRpcService(admin, target, true);

            RpcHandlerResult enrolled = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(admin, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                rpc,
                store,
                null);
            Equal(RpcResultCode.Success, enrolled.Code);
            Equal("binding-enrolled", enrolled.ReasonCode);
            Equal(1, storage.WriteAttempts);
            Equal(
                RpcPlayerBindingStatus.Verified,
                store.Resolve(playFabIdentity, FakeAdminRpcService.TargetPlayerId));
            store.Dispose();

            // Reload from the committed world-scoped bytes to prove the persisted reverse index,
            // not merely the pre-commit in-memory map, recognizes the PlayFab account exactly.
            var reloaded = NewStore(scope, storage);
            reloaded.Tick();
            Equal(
                RpcPlayerBindingStatus.Verified,
                reloaded.Resolve(playFabIdentity, FakeAdminRpcService.TargetPlayerId));
            var otherPlayFab = new RpcPeerIdentity(
                "playfab.entity",
                "e99cd4e35f2549f59bc2219f4a09717f",
                RpcIdentityAssurance.BackendAccount);
            PlayerIdentityBindingMutationResult playerConflict = reloaded.Enroll(
                otherPlayFab,
                FakeAdminRpcService.TargetPlayerId);
            True(!playerConflict.Success, "A second account reused the bound Valheim player ID.");
            Equal("player-id-already-bound", playerConflict.ReasonCode);
            PlayerIdentityBindingMutationResult accountConflict = reloaded.Enroll(
                playFabIdentity,
                FakeAdminRpcService.TargetPlayerId + 1);
            True(!accountConflict.Success, "The PlayFab account was rebound to another player ID.");
            Equal("identity-already-bound", accountConflict.ReasonCode);
            Equal(1, storage.WriteAttempts);
            reloaded.Dispose();
        }

        internal static void RemoteAdminEnrollmentRechecksDisconnectAndUidReuse()
        {
            RpcPeerSnapshot admin = Peer(61, "admin-session", Account("76561198000000061"));
            RpcPeerSnapshot target = Peer(62, "target-session-a", Account("76561198000000062"));

            var disconnectStorage = new MemoryBindingStorage();
            var disconnectStore = NewStore("valheim.000000000000000e", disconnectStorage);
            disconnectStore.Tick();
            var disconnectedRpc = new FakeAdminRpcService(admin, target, true)
            {
                DropTargetAfterGetCount = 2
            };
            RpcHandlerResult disconnected = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(admin, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                disconnectedRpc,
                disconnectStore,
                null);
            Equal(RpcResultCode.NotReady, disconnected.Code);
            Equal(0, disconnectStorage.WriteAttempts);
            disconnectStore.Dispose();

            var reuseStorage = new MemoryBindingStorage();
            var reuseStore = NewStore("valheim.000000000000000f", reuseStorage);
            reuseStore.Tick();
            var reusedRpc = new FakeAdminRpcService(admin, target, true)
            {
                ReplaceTargetAfterGetCount = 2,
                ReplacementTarget = Peer(
                    target.PeerId,
                    "target-session-b",
                    Account("76561198000000999"))
            };
            RpcHandlerResult reused = PlayerIdentityBindingCommands.HandleAdminEnroll(
                Request(admin, PlayerIdentityBindingCommands.EncodeInt64(target.PeerId)),
                reusedRpc,
                reuseStore,
                null);
            Equal(RpcResultCode.NotReady, reused.Code);
            Equal("binding-target-session-changed", reused.ReasonCode);
            Equal(0, reuseStorage.WriteAttempts);
            reuseStore.Dispose();
        }

        internal static void RemoteAdminEnrollmentIsExplicitAndIdempotent()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000010", storage);
            store.Tick();
            RpcPeerSnapshot admin = Peer(71, "admin-session", Account("76561198000000071"));
            RpcPeerSnapshot target = Peer(72, "target-session", Account("76561198000000072"));
            var rpc = new FakeAdminRpcService(admin, target, true);
            RpcRequestContext request = Request(
                admin,
                PlayerIdentityBindingCommands.EncodeInt64(target.PeerId));
            RpcHandlerResult first = PlayerIdentityBindingCommands.HandleAdminEnroll(
                request,
                rpc,
                store,
                null);
            RpcHandlerResult repeated = PlayerIdentityBindingCommands.HandleAdminEnroll(
                request,
                rpc,
                store,
                null);
            Equal(RpcResultCode.Success, first.Code);
            Equal(RpcResultCode.Success, repeated.Code);
            Equal("binding-already-present", repeated.ReasonCode);
            Equal(1, storage.WriteAttempts);
            Equal(
                RpcPlayerBindingStatus.Verified,
                store.Resolve(target.Identity, FakeAdminRpcService.TargetPlayerId));
            store.Dispose();
        }

        internal static void ClientConsoleDispatchesOnlyDirectAdminRpc()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000011", storage);
            store.Tick();
            var registry = new RunicRegistry();
            ModuleRegistration module = registry.RegisterModule(new ModuleDescriptor(
                Plugin.ModuleId,
                Plugin.PluginName,
                Plugin.PluginVersion,
                Plugin.ProtocolVersion,
                new[] { RunicCapabilityIds.ActorIdentityBinding }));
            var rpc = new FakeClientCommandRpc();
            var output = new List<string>();
            PlayerIdentityBindingCommands.Execute(
                store,
                rpc,
                module,
                new[] { PlayerIdentityBindingCommands.CommandName, "peers" },
                output.Add);
            Equal(PlayerIdentityBindingCommands.QueryEndpointId, rpc.LastEndpointId);
            Equal(5, rpc.LastPayload.Length);
            PlayerIdentityBindingCommands.Execute(
                store,
                rpc,
                module,
                new[] { PlayerIdentityBindingCommands.CommandName, "enroll", "123" },
                output.Add);
            Equal(PlayerIdentityBindingCommands.EnrollEndpointId, rpc.LastEndpointId);
            True(PlayerIdentityBindingCommands.EncodeInt64(123).SequenceEqual(rpc.LastPayload),
                "Client enrollment did not send only the target peer UID.");
            Equal(0, storage.WriteAttempts);
            True(output.All(line => !line.Contains("committed", StringComparison.Ordinal)),
                "Client console claimed a local server commit.");
            module.Dispose();
            store.Dispose();
        }

        internal static void AdminQueryAcceptsByteBackedKind()
        {
            var storage = new MemoryBindingStorage();
            var store = NewStore("valheim.0000000000000013", storage);
            store.Tick();
            RpcPeerSnapshot admin = Peer(91, "admin-session", Account("76561198000000091"));
            RpcPeerSnapshot target = Peer(92, "target-session", Account("76561198000000092"));
            var rpc = new FakeAdminRpcService(admin, target, true);
            var payload = new byte[5];
            payload[0] = (byte)PlayerIdentityBindingCommands.BindingQueryKind.Peers;

            RpcHandlerResult result = PlayerIdentityBindingCommands.HandleAdminQuery(
                Request(admin, payload),
                rpc,
                store,
                null);

            Equal(RpcResultCode.Success, result.Code);
            True(result.Payload.Length > 0, "Peer query returned no response payload.");
            payload[0] = byte.MaxValue;
            Equal(
                RpcResultCode.InvalidRequest,
                PlayerIdentityBindingCommands.HandleAdminQuery(
                    Request(admin, payload),
                    rpc,
                    store,
                    null).Code);
            store.Dispose();
        }

        private static PlayerIdentityBindingStore NewStore(
            string scope,
            IPlayerIdentityBindingStorage storage) =>
            new PlayerIdentityBindingStore(() => scope, storage);

        private static RpcPeerIdentity Account(string subject) =>
            new RpcPeerIdentity("steam", subject, RpcIdentityAssurance.BackendAccount);

        private static RpcPeerSnapshot Peer(
            long peerId,
            string sessionId,
            RpcPeerIdentity identity) =>
            new RpcPeerSnapshot(
                peerId,
                sessionId,
                identity,
                true,
                true,
                new ProtocolHello("peer", Array.Empty<ModuleProtocolState>()),
                DateTime.UtcNow.Ticks);

        private static RpcRequestContext Request(RpcPeerSnapshot requester, byte[] payload) =>
            new RpcRequestContext(
                requester,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    PlayerIdentityBindingCommands.EnrollEndpointId,
                    RunicCapabilityIds.ActorIdentityBinding,
                    1,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.BackendAccount,
                    8),
                "correlation",
                "idempotency",
                payload,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.AddSeconds(5).Ticks,
                true,
                () => true);

        private static byte[] EncodeUnchecked(
            string scope,
            params PlayerIdentityBindingSnapshot[] records)
        {
            var utf8 = new UTF8Encoding(false, true);
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, utf8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RUNICPBI"));
                writer.Write(PlayerIdentityBindingCodec.SchemaVersion);
                WriteString(writer, utf8, scope);
                writer.Write(records.Length);
                foreach (PlayerIdentityBindingSnapshot record in records)
                {
                    WriteString(writer, utf8, record.Authority);
                    WriteString(writer, utf8, record.SubjectId);
                    writer.Write(record.PlayerId);
                }
                writer.Flush();
                byte[] payload = body.ToArray();
                byte[] digest;
                using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(payload);
                return payload.Concat(digest).ToArray();
            }
        }

        private static void WriteString(BinaryWriter writer, Encoding encoding, string value)
        {
            byte[] bytes = encoding.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }

        private sealed class MemoryBindingStorage : IPlayerIdentityBindingStorage
        {
            private readonly Dictionary<string, byte[]> _files =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            internal int WriteAttempts { get; private set; }
            internal bool FailWrites { get; set; }
            internal bool ThrowWrites { get; set; }

            internal void Set(string scope, byte[] bytes) =>
                _files[scope] = (byte[])bytes.Clone();

            public bool TryRead(
                string worldScope,
                out bool exists,
                out byte[] bytes,
                out string reasonCode)
            {
                exists = _files.TryGetValue(worldScope, out byte[] stored);
                bytes = exists ? (byte[])stored.Clone() : Array.Empty<byte>();
                reasonCode = exists ? "binding-file-read" : "binding-file-missing";
                return true;
            }

            public bool TryWriteAtomic(
                string worldScope,
                byte[] bytes,
                out string reasonCode)
            {
                WriteAttempts++;
                if (ThrowWrites) throw new IOException("Injected writer exception.");
                if (FailWrites)
                {
                    reasonCode = "injected-write-failure";
                    return false;
                }
                _files[worldScope] = (byte[])bytes.Clone();
                reasonCode = "binding-file-committed";
                return true;
            }
        }

        private sealed class FakeRpcService : IRunicRpcService
        {
            private readonly RpcPeerSnapshot _peer;
            private readonly long _playerId;

            internal FakeRpcService(RpcPeerSnapshot peer, long playerId)
            {
                _peer = peer;
                _playerId = playerId;
            }

            public bool IsServer => true;
            public bool IsServerConnectionReady => false;
            public event EventHandler<RpcPeerEventArgs> PeerReady { add { } remove { } }
            public event EventHandler<RpcPeerEventArgs> PeerDisconnected { add { } remove { } }
            public IDisposable RegisterEndpoint(ModuleRegistration module, RpcEndpointDescriptor descriptor, RunicRpcHandler handler) => throw new NotSupportedException();
            public IDisposable RegisterPeerRequirement(ModuleRegistration module, RpcPeerRequirement requirement) => throw new NotSupportedException();
            public IDisposable RegisterPlayerBindingResolver(ModuleRegistration module, IRpcPlayerBindingResolver resolver) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimProvider(ModuleRegistration module, RunicHandshakeClaimProvider provider) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimEvaluator(ModuleRegistration module, string evaluatorId, RunicHandshakeClaimEvaluator evaluator) => throw new NotSupportedException();
            public RpcSendResult SendToServer(ModuleRegistration module, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public RpcSendResult SendToPeer(ModuleRegistration module, RpcPeerSnapshot expectedPeer, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public bool TryGetPeer(long peerId, out RpcPeerSnapshot peer)
            {
                peer = peerId == _peer.PeerId ? _peer : null;
                return peer != null;
            }
            public bool TryResolveActor(RpcPeerSnapshot expectedPeer, RpcActorAssurance minimumAssurance, out RpcActorSnapshot actor, out string reasonCode)
            {
                if (!ReferenceEquals(expectedPeer, _peer) || minimumAssurance != RpcActorAssurance.TransportOwnedCharacter)
                {
                    actor = null;
                    reasonCode = "stale-peer-session";
                    return false;
                }
                actor = new RpcActorSnapshot(
                    _peer,
                    new ZDOID(_peer.PeerId, 1),
                    _playerId,
                    RpcActorAssurance.TransportOwnedCharacter,
                    false);
                reasonCode = "actor-resolved";
                return true;
            }
            public IReadOnlyList<RpcPeerSnapshot> GetPeers() => new[] { _peer };
            public bool TryDisconnectPeer(long peerId, string reasonCode) => false;
        }

        private sealed class FakeAdminRpcService : IRunicRpcService
        {
            internal const long AdminPlayerId = 11001;
            internal const long TargetPlayerId = 11002;

            private readonly RpcPeerSnapshot _admin;
            private readonly RpcPeerSnapshot _target;
            private readonly bool _adminAllowed;
            private int _targetGetCount;

            internal FakeAdminRpcService(
                RpcPeerSnapshot admin,
                RpcPeerSnapshot target,
                bool adminAllowed)
            {
                _admin = admin;
                _target = target;
                _adminAllowed = adminAllowed;
            }

            internal int DropTargetAfterGetCount { get; set; } = int.MaxValue;
            internal int ReplaceTargetAfterGetCount { get; set; } = int.MaxValue;
            internal RpcPeerSnapshot ReplacementTarget { get; set; }

            public bool IsServer => true;
            public bool IsServerConnectionReady => false;
            public event EventHandler<RpcPeerEventArgs> PeerReady { add { } remove { } }
            public event EventHandler<RpcPeerEventArgs> PeerDisconnected { add { } remove { } }
            public IDisposable RegisterEndpoint(ModuleRegistration module, RpcEndpointDescriptor descriptor, RunicRpcHandler handler) => throw new NotSupportedException();
            public IDisposable RegisterPeerRequirement(ModuleRegistration module, RpcPeerRequirement requirement) => throw new NotSupportedException();
            public IDisposable RegisterPlayerBindingResolver(ModuleRegistration module, IRpcPlayerBindingResolver resolver) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimProvider(ModuleRegistration module, RunicHandshakeClaimProvider provider) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimEvaluator(ModuleRegistration module, string evaluatorId, RunicHandshakeClaimEvaluator evaluator) => throw new NotSupportedException();
            public RpcSendResult SendToServer(ModuleRegistration module, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public RpcSendResult SendToPeer(ModuleRegistration module, RpcPeerSnapshot expectedPeer, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();

            public bool TryGetPeer(long peerId, out RpcPeerSnapshot peer)
            {
                if (peerId == _admin.PeerId)
                {
                    peer = _admin;
                    return true;
                }
                if (peerId != _target.PeerId)
                {
                    peer = null;
                    return false;
                }
                _targetGetCount++;
                if (_targetGetCount > DropTargetAfterGetCount)
                {
                    peer = null;
                    return false;
                }
                peer = _targetGetCount > ReplaceTargetAfterGetCount
                    ? ReplacementTarget
                    : _target;
                return peer != null;
            }

            public bool TryResolveActor(
                RpcPeerSnapshot expectedPeer,
                RpcActorAssurance minimumAssurance,
                out RpcActorSnapshot actor,
                out string reasonCode)
            {
                if (expectedPeer == null || minimumAssurance != RpcActorAssurance.TransportOwnedCharacter)
                {
                    actor = null;
                    reasonCode = "actor-unavailable";
                    return false;
                }
                bool admin = expectedPeer.PeerId == _admin.PeerId;
                actor = new RpcActorSnapshot(
                    expectedPeer,
                    new ZDOID(expectedPeer.PeerId, 1),
                    admin ? AdminPlayerId : TargetPlayerId,
                    RpcActorAssurance.TransportOwnedCharacter,
                    admin && _adminAllowed);
                reasonCode = "actor-resolved";
                return true;
            }

            public IReadOnlyList<RpcPeerSnapshot> GetPeers() => new[] { _admin, _target };
            public bool TryDisconnectPeer(long peerId, string reasonCode) => false;
        }

        private sealed class FakeClientCommandRpc : IRunicRpcService
        {
            internal string LastEndpointId { get; private set; } = string.Empty;
            internal byte[] LastPayload { get; private set; } = Array.Empty<byte>();

            public bool IsServer => false;
            public bool IsServerConnectionReady => true;
            public event EventHandler<RpcPeerEventArgs> PeerReady { add { } remove { } }
            public event EventHandler<RpcPeerEventArgs> PeerDisconnected { add { } remove { } }
            public IDisposable RegisterEndpoint(ModuleRegistration module, RpcEndpointDescriptor descriptor, RunicRpcHandler handler) => throw new NotSupportedException();
            public IDisposable RegisterPeerRequirement(ModuleRegistration module, RpcPeerRequirement requirement) => throw new NotSupportedException();
            public IDisposable RegisterPlayerBindingResolver(ModuleRegistration module, IRpcPlayerBindingResolver resolver) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimProvider(ModuleRegistration module, RunicHandshakeClaimProvider provider) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimEvaluator(ModuleRegistration module, string evaluatorId, RunicHandshakeClaimEvaluator evaluator) => throw new NotSupportedException();
            public RpcSendResult SendToServer(ModuleRegistration module, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion)
            {
                LastEndpointId = endpointId;
                LastPayload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
                return new RpcSendResult(true, "accepted", null);
            }
            public RpcSendResult SendToPeer(ModuleRegistration module, RpcPeerSnapshot expectedPeer, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public bool TryGetPeer(long peerId, out RpcPeerSnapshot peer) { peer = null; return false; }
            public bool TryResolveActor(RpcPeerSnapshot expectedPeer, RpcActorAssurance minimumAssurance, out RpcActorSnapshot actor, out string reasonCode)
            {
                actor = null;
                reasonCode = "client";
                return false;
            }
            public IReadOnlyList<RpcPeerSnapshot> GetPeers() => Array.Empty<RpcPeerSnapshot>();
            public bool TryDisconnectPeer(long peerId, string reasonCode) => false;
        }
    }
}
