using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;

namespace Runic.Foundation.Persistence.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static void Main()
        {
            Run("key normalization", KeyNormalization);
            Run("migration preserves unknown data", MigrationPreservesUnknownData);
            Run("migration is atomic on failure", MigrationIsAtomicOnFailure);
            Run("migration is idempotent", MigrationIsIdempotent);
            Run("protocol incompatibility is isolated", ProtocolIncompatibilityIsIsolated);
            Run("protocol selects a compatible provider", ProtocolSelectsCompatibleProvider);
            Run("idempotency rejects duplicate", IdempotencyRejectsDuplicate);
            Run("idempotency window fails closed at its bound", IdempotencyWindowIsBounded);
            Run("RPC payload is immutable and bounded", RpcPayloadIsImmutableAndBounded);
            Run("RPC peer identities reject invalid UTF-16", RpcPeerIdentityUtf16Bounds);
            Run("record snapshots do not leak mutable storage", RecordSnapshotDoesNotLeakStorage);
            Run("migrations reject foreign key mutation", MigrationRejectsForeignMutation);
            Run("migration compare-and-swap prevents concurrent overwrite", ConcurrentMigrationUsesCompareAndSwap);
            Run("stale migration leases cannot remove replacements", StaleMigrationLeaseIsSafe);
            Run("RPC handshake sends once when challenge arrives first", HandshakeChallengeFirst);
            Run("RPC handshake sends once when client handshake arrives first", HandshakeClientFirst);
            Run("RPC handshake rejects incompatible interleavings", HandshakeIncompatible);
            Run("RPC wire codec rejects truncation and trailing data", RpcWireBounds);
            Run("RPC replay capacity is isolated per peer", RpcReplayCapacityIsPerPeer);
            Run("RPC retryable replay reservations retain conflict and flood bounds", RpcRetryableReplayIsBounded);
            Run("stale endpoint owners fail closed", StaleEndpointOwnerFailsClosed);
            Run("endpoint protocol equals its owning module hello", EndpointProtocolMatchesModuleHello);
            Run("accepted operations are not reported complete", AcceptedOperationIsExplicit);
            Run("queued PeerInfo resumes once after a late challenge", QueuedPeerInfoResumesOnce);
            Run("ready PeerInfo sends once when challenge arrives first", ReadyPeerInfoSendsOnce);
            Run("queued PeerInfo is erased on disconnect", QueuedPeerInfoDisconnectRace);
            Run("claimed PeerInfo resume cannot survive disconnect", ClaimedPeerInfoDisconnectRace);
            Run("Steam backend identity requires authenticated exact transport agreement", SteamIdentityClassification);
            Run("server admin authority requires exact canonical Steam identity", ServerAdminIdentityClassification);
            Run("idempotency bounds canonical UTF-8 bytes before encode", IdempotencyUtf8Bound);
            Run("transport boundary isolates codec nonce send and reflection faults", TransportBoundaryIsolation);
            Run("disconnect failures remain latched through close fallback", DisconnectFailureRetries);
            Run("handshake claims reject duplicate keys", HandshakeClaimsRejectDuplicates);
            Run("handshake claims reject noncanonical edge whitespace", HandshakeClaimsRejectEdgeWhitespace);
            Run("handshake claims reject count and byte floods", HandshakeClaimsRejectFloods);
            Run("throwing claim provider fails the connection boundary", ThrowingClaimProviderFailsClosed);
            Run("missing handshake claim prevents PeerInfo admission", MissingClaimPreventsAdmission);
            Run("mismatched handshake claim prevents PeerInfo admission", MismatchedClaimPreventsAdmission);
            Run("provisional capacity bounds a 100 connection flood", ProvisionalCapacity100);
            Run("provisional capacity bounds a 1000 connection flood", ProvisionalCapacity1000);
            Run("direct wire idempotency keys are exact and canonical", DirectWireIdempotencyKeysAreCanonical);
            Run("direct ZRpc handshake mutation replay and response are end to end", DirectRpcEndToEnd);
            Run("direct ZRpc transient replay policy is exact and durability-aware", DirectRpcNotReadyRetriesExactRequest);
            Run("client exposes only its exact current server session", ClientServerSnapshotIsDirectSessionBound);
            Run("vanilla handshake routed reset preserves the provisional direct session", ProvisionalRoutedResetPreservesDirectSession);
            Run("dedicated actors resolve from exact transport-owned Player ZDO evidence", DedicatedActorUsesReplicatedPlayerZdoEvidence);
            Run("same direct session upgrades its transport identity exactly once", DirectSessionIdentityUpgradeIsMonotonic);
            Run("AccountBoundPlayer fails closed without persisted resolver", AccountBoundActorFailsClosed);
            Run("Harmony admission hooks order before compatibility libraries", HarmonyOrderingIsExplicit);
            Run("peer UID collision preserves the live session", PeerUidCollisionFailsClosed);
            Run("claim codec round trip is immutable and bounded", ClaimCodecRoundTrip);
            Run("binding resolution never auto-enrolls", PlayerIdentityBindingTests.ResolveNeverAutoEnrolls);
            Run("bindings persist as a reverse-unique bijection", PlayerIdentityBindingTests.EnrollmentPersistsReverseUniqueBijection);
            Run("binding enrollment rejects both conflict directions", PlayerIdentityBindingTests.EnrollmentRejectsBothConflictDirections);
            Run("binding revocation updates both directions", PlayerIdentityBindingTests.RevocationUpdatesBothDirections);
            Run("bindings are strictly isolated by world", PlayerIdentityBindingTests.WorldsAreStrictlyIsolated);
            Run("binding corruption latches until explicit reload", PlayerIdentityBindingTests.CorruptionLatchesUntilExplicitReload);
            Run("binding codec rejects world mismatch and duplicate records", PlayerIdentityBindingTests.CodecRejectsWorldMismatchAndDuplicateRecords);
            Run("failed binding writes cannot change live state", PlayerIdentityBindingTests.FailedWriteCannotChangeLiveBindingState);
            Run("binding file commit is atomic and backup is never auto-trusted", PlayerIdentityBindingTests.FileCommitIsAtomicAndBackupIsNeverAutoTrusted);
            Run("binding console enrollment uses exact peer and local-only flags", PlayerIdentityBindingTests.ConsoleEnrollmentUsesExactPeerAndLocalOnlyFlags);
            Run("remote binding enrollment requires bound Steam admin", PlayerIdentityBindingTests.RemoteAdminEnrollmentRequiresBoundSteamAdmin);
            Run("Steam admin can bind an exact PlayFab target bijectively", PlayerIdentityBindingTests.SteamAdminEnrollsPlayFabTargetEndToEnd);
            Run("remote binding enrollment rechecks disconnect and UID reuse", PlayerIdentityBindingTests.RemoteAdminEnrollmentRechecksDisconnectAndUidReuse);
            Run("remote binding enrollment is explicit and idempotent", PlayerIdentityBindingTests.RemoteAdminEnrollmentIsExplicitAndIdempotent);
            Run("client console dispatches only direct admin RPC", PlayerIdentityBindingTests.ClientConsoleDispatchesOnlyDirectAdminRpc);
            Run("admin binding query accepts its byte-backed query kind", PlayerIdentityBindingTests.AdminQueryAcceptsByteBackedKind);
            Run("direct admin binding RPC replays without a second commit", DirectAdminBindingRpcReplay);

            System.Console.WriteLine($"PASS — {_passed}/68 Runic Persistence foundation tests");
        }

        private static void KeyNormalization()
        {
            Equal("runic.storage.owner-id", RunicKey.Create("runic.storage", "owner-id").Value);
            Throws<ArgumentException>(() => RunicKey.Create("Runic Storage", "owner-id"));
            Throws<ArgumentException>(() => RunicKey.Create("runic.storage", "Owner ID"));
            Throws<ArgumentException>(() => RunicKey.Create("runic.storage.child", "owner-id"));
        }

        private static void MigrationPreservesUnknownData()
        {
            var store = new MemoryRecordStore("runic.storage", new RecordSetSnapshot(0, new Dictionary<string, byte[]>
            {
                ["thirdparty.unknown"] = Bytes("keep")
            }));
            var registry = new MigrationRegistry();
            registry.Register(new MigrationStep("runic.storage", 0, 1, snapshot => snapshot.Set("runic.storage.schema", Bytes("one"))));

            MigrationResult result = registry.Migrate("runic.storage", 1, store, new RecordingBackup());

            True(result.Success && result.Changed, "Migration should commit.");
            True(store.ReadSnapshot().TryGet("thirdparty.unknown", out byte[] unknown), "Unknown key should remain.");
            Equal("keep", Text(unknown));
        }

        private static void MigrationIsAtomicOnFailure()
        {
            var store = new MemoryRecordStore("runic.test", new RecordSetSnapshot(0, new Dictionary<string, byte[]>
            {
                ["runic.test.value"] = Bytes("before")
            }));
            var registry = new MigrationRegistry();
            registry.Register(new MigrationStep("runic.test", 0, 1, snapshot =>
            {
                snapshot.Set("runic.test.value", Bytes("after"));
                throw new InvalidOperationException("planned failure");
            }));

            MigrationResult result = registry.Migrate("runic.test", 1, store, new RecordingBackup());

            True(!result.Success, "Migration should fail.");
            Equal(0, store.ReadSnapshot().SchemaVersion);
            store.ReadSnapshot().TryGet("runic.test.value", out byte[] value);
            Equal("before", Text(value));
        }

        private static void MigrationIsIdempotent()
        {
            var store = new MemoryRecordStore("runic.test", new RecordSetSnapshot(1, new Dictionary<string, byte[]>()));
            var registry = new MigrationRegistry();
            MigrationResult result = registry.Migrate("runic.test", 1, store, new RecordingBackup());
            True(result.Success && !result.Changed, "Current schema should be a no-op.");
        }

        private static void ProtocolIncompatibilityIsIsolated()
        {
            var local = new ProtocolHello("local", Array.Empty<ModuleProtocolState>());
            var remote = new ProtocolHello("remote", new[]
            {
                new ModuleProtocolState("storage", "0.1.0", 1, new[] { "container.query" }),
                new ModuleProtocolState("future", "1.0.0", 4, new[] { "portal.directory" })
            });
            NegotiationResult result = ProtocolNegotiator.Negotiate(local, remote, new[]
            {
                new ProtocolRequirement("crafting", "container.query", 1, 1),
                new ProtocolRequirement("exploration", "portal.directory", 1, 2)
            });

            True(result.Decisions.Single(item => item.Requirement.CapabilityId == "container.query").Enabled,
                "Compatible capability should remain enabled.");
            True(!result.Decisions.Single(item => item.Requirement.CapabilityId == "portal.directory").Enabled,
                "Only incompatible capability should disable.");
        }

        private static void ProtocolSelectsCompatibleProvider()
        {
            var local = new ProtocolHello("local", Array.Empty<ModuleProtocolState>());
            var remote = new ProtocolHello("remote", new[]
            {
                new ModuleProtocolState("provider.a", "1.0.0", 4, new[] { "container.query" }),
                new ModuleProtocolState("provider.b", "1.0.0", 1, new[] { "container.query" })
            });

            NegotiationResult result = ProtocolNegotiator.Negotiate(local, remote, new[]
            {
                new ProtocolRequirement("runic.crafting", "container.query", 1, 1)
            });

            True(result.AllEnabled, "A compatible provider should be selected.");
            Equal("provider.b", result.Decisions[0].ProviderModuleId);
        }

        private static void IdempotencyRejectsDuplicate()
        {
            var window = new IdempotencyWindow();
            True(window.TryAccept("request-1", 100, 10), "First request should pass.");
            True(!window.TryAccept("request-1", 105, 10), "Duplicate inside window should fail.");
            True(window.TryAccept("request-1", 111, 10), "Expired key should pass again.");
        }

        private static void IdempotencyWindowIsBounded()
        {
            var window = new IdempotencyWindow(2);
            True(window.TryAccept("request-1", 100, 10), "First key should pass.");
            True(window.TryAccept("request-2", 100, 10), "Second key should pass.");
            True(!window.TryAccept("request-3", 100, 10), "A full live window must fail closed.");
            Equal(2, window.Count);
            True(window.TryAccept("request-3", 111, 10), "Expired entries should release capacity.");
        }

        private static void RpcPayloadIsImmutableAndBounded()
        {
            byte[] source = { 1, 2, 3 };
            var envelope = new RpcEnvelope(1, "runic.storage", "container.query", "correlation", "request", source, 1);
            source[0] = 9;
            byte[] firstRead = envelope.Payload;
            firstRead[1] = 9;
            byte[] secondRead = envelope.Payload;
            Equal((byte)1, secondRead[0]);
            Equal((byte)2, secondRead[1]);

            Throws<ArgumentOutOfRangeException>(() => new RpcEnvelope(
                1,
                "runic.storage",
                "container.query",
                "correlation",
                "request",
                new byte[RpcEnvelope.MaximumPayloadBytes + 1],
                1));
        }

        private static void RpcPeerIdentityUtf16Bounds()
        {
            var maximum = new RpcPeerIdentity(
                "test.account",
                new string('x', RpcPeerIdentity.MaximumSubjectLength),
                RpcIdentityAssurance.BackendAccount);
            Equal(RpcPeerIdentity.MaximumSubjectLength, maximum.SubjectId.Length);
            True(maximum.CanonicalKey.Length > 0, "Canonical identity key was unavailable.");
            Throws<ArgumentOutOfRangeException>(() => new RpcPeerIdentity(
                "test.account",
                new string('x', RpcPeerIdentity.MaximumSubjectLength + 1),
                RpcIdentityAssurance.BackendAccount));
            Throws<ArgumentException>(() => new RpcPeerIdentity(
                "test.account",
                "broken-\ud800",
                RpcIdentityAssurance.BackendAccount));
            Throws<ArgumentException>(() => new RpcPeerIdentity(
                "test.account",
                "broken-\udc00",
                RpcIdentityAssurance.BackendAccount));
            Throws<ArgumentException>(() => new RpcPeerIdentity(
                "Test.Account",
                "subject",
                RpcIdentityAssurance.BackendAccount));
            Throws<ArgumentException>(() => new RpcPeerIdentity(
                "test.account",
                " subject",
                RpcIdentityAssurance.BackendAccount));
            Throws<ArgumentException>(() => new RpcPeerIdentity(
                "test.account",
                "subject ",
                RpcIdentityAssurance.BackendAccount));
            var paired = new RpcPeerIdentity(
                "test.account",
                "valid-\ud83d\ude80",
                RpcIdentityAssurance.BackendAccount);
            True(paired.CanonicalKey.Length > 0, "Valid surrogate pair was rejected.");
        }

        private static void RecordSnapshotDoesNotLeakStorage()
        {
            var snapshot = new RecordSetSnapshot(0, new Dictionary<string, byte[]>
            {
                ["runic.test.value"] = new byte[] { 1, 2 }
            });
            IReadOnlyDictionary<string, byte[]> exposed = snapshot.Records;
            exposed["runic.test.value"][0] = 9;

            snapshot.TryGet("runic.test.value", out byte[] stored);
            Equal((byte)1, stored[0]);
        }

        private static void MigrationRejectsForeignMutation()
        {
            var store = new MemoryRecordStore("runic.test", new RecordSetSnapshot(0, new Dictionary<string, byte[]>
            {
                ["runic.test.value"] = Bytes("before"),
                ["thirdparty.value"] = Bytes("untouched")
            }));
            var registry = new MigrationRegistry();
            registry.Register(new MigrationStep("runic.test", 0, 1, snapshot =>
            {
                snapshot.Set("runic.test.value", Bytes("after"));
                snapshot.Remove("thirdparty.value");
            }));

            MigrationResult result = registry.Migrate("runic.test", 1, store, null);

            True(!result.Success, "A foreign-key mutation must fail.");
            Equal(0, store.ReadSnapshot().SchemaVersion);
            True(store.ReadSnapshot().TryGet("thirdparty.value", out byte[] foreign),
                "Foreign data must remain after rejection.");
            Equal("untouched", Text(foreign));
        }

        private static void ConcurrentMigrationUsesCompareAndSwap()
        {
            var store = new BarrierRecordStore(
                "runic.test",
                new RecordSetSnapshot(0, new Dictionary<string, byte[]>()));
            var registry = new MigrationRegistry();
            registry.Register(new MigrationStep("runic.test", 0, 1, snapshot =>
                snapshot.Set("runic.test.one", Bytes("one"))));
            registry.Register(new MigrationStep("runic.test", 1, 2, snapshot =>
                snapshot.Set("runic.test.two", Bytes("two"))));

            Task<MigrationResult> first = Task.Run(() => registry.Migrate("runic.test", 1, store, null));
            Task<MigrationResult> second = Task.Run(() => registry.Migrate("runic.test", 2, store, null));
            Task.WaitAll(first, second);

            MigrationResult[] results = { first.Result, second.Result };
            Equal(1, results.Count(item => item.Success));
            MigrationResult winner = results.Single(item => item.Success);
            Equal(winner.FinalVersion, store.ReadSnapshot().SchemaVersion);
            True(results.Single(item => !item.Success).Error.Contains("concurrently"),
                "Losing CAS must report a retryable concurrent change.");
        }

        private static void StaleMigrationLeaseIsSafe()
        {
            var registry = new MigrationRegistry();
            MigrationRegistration stale = registry.Register(new MigrationStep(
                "runic.test", 0, 1, snapshot => { }));
            Equal(1, registry.UnregisterModule("runic.test"));
            MigrationRegistration current = registry.Register(new MigrationStep(
                "runic.test", 0, 1, snapshot => { }));

            stale.Dispose();

            True(current.IsActive, "A stale lease removed its replacement.");
            current.Dispose();
        }

        private static void HandshakeChallengeFirst()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkChallenge(true);
            True(!gate.TryTakeOffer(), "Challenge alone must not send an offer.");
            gate.MarkClientHandshake();
            True(gate.TryTakeOffer(), "Second prerequisite must release the offer.");
            True(!gate.TryTakeOffer(), "Offer latch must be one-shot.");
        }

        private static void HandshakeClientFirst()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkClientHandshake();
            True(!gate.TryTakeOffer(), "Vanilla handshake alone must not send an offer.");
            gate.MarkChallenge(true);
            True(gate.TryTakeOffer(), "Late challenge must release the queued offer.");
            True(!gate.TryTakeOffer(), "Late-challenge path must still be one-shot.");
        }

        private static void HandshakeIncompatible()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkClientHandshake();
            gate.MarkChallenge(false);
            True(!gate.TryTakeOffer(), "An incompatible profile must never release an offer.");
        }

        private static void RpcWireBounds()
        {
            var frame = new RpcWireFrame
            {
                Kind = RpcFrameKind.Request,
                SessionId = Guid.NewGuid().ToString("N"),
                Sequence = 1,
                CorrelationId = Guid.NewGuid().ToString("N"),
                ModuleId = "runic.test",
                EndpointId = "runic.test.query",
                TimeoutMilliseconds = 1000,
                ResultCode = RpcResultCode.Success,
                Payload = new byte[] { 1, 2, 3 }
            };
            byte[] encoded = RpcWireCodec.Encode(frame);
            True(RpcWireCodec.TryDecode(encoded, out RpcWireFrame decoded, out _),
                "Valid bounded frame did not decode.");
            Equal(frame.CorrelationId, decoded.CorrelationId);

            byte[] truncated = encoded.Take(encoded.Length - 1).ToArray();
            True(!RpcWireCodec.TryDecode(truncated, out _, out _), "Truncated frame was accepted.");
            byte[] trailing = encoded.Concat(new byte[] { 9 }).ToArray();
            True(!RpcWireCodec.TryDecode(trailing, out _, out _), "Trailing bytes were accepted.");
        }

        private static void RpcReplayCapacityIsPerPeer()
        {
            var firstPeer = new RpcReplayLedger(1, 100);
            var secondPeer = new RpcReplayLedger(1, 100);
            Equal(
                RpcReplayAdmission.New,
                firstPeer.TryBegin("session-a|one", new string('a', 64), 10, out _));
            Equal(
                RpcReplayAdmission.CapacityReached,
                firstPeer.TryBegin("session-a|two", new string('b', 64), 10, out _));
            Equal(
                RpcReplayAdmission.New,
                secondPeer.TryBegin("session-b|one", new string('c', 64), 10, out _));
        }

        private static void RpcRetryableReplayIsBounded()
        {
            foreach (RpcResultCode code in new[]
                     {
                         RpcResultCode.NotReady,
                         RpcResultCode.RateLimited,
                         RpcResultCode.CapacityReached,
                         RpcResultCode.TimedOut
                     })
            {
                True(RpcMutationReplayPolicy.ReleasesReservation(
                    code, RpcReplayDurability.SessionOnly),
                    code + " was not retryable for SessionOnly.");
                True(RpcMutationReplayPolicy.ReleasesReservation(
                    code, RpcReplayDurability.HandlerDurable),
                    code + " was not retryable for HandlerDurable.");
            }
            True(!RpcMutationReplayPolicy.ReleasesReservation(
                RpcResultCode.HandlerFailed, RpcReplayDurability.SessionOnly),
                "HandlerFailed re-entered a SessionOnly handler.");
            True(RpcMutationReplayPolicy.ReleasesReservation(
                RpcResultCode.HandlerFailed, RpcReplayDurability.HandlerDurable),
                "HandlerFailed did not re-enter a HandlerDurable handler.");
            foreach (RpcResultCode terminal in new[]
                     {
                         RpcResultCode.Success,
                         RpcResultCode.InvalidRequest,
                         RpcResultCode.Unauthorized,
                         RpcResultCode.ReplayConflict,
                         RpcResultCode.Cancelled,
                         RpcResultCode.ConnectionClosed,
                         RpcResultCode.ProtocolViolation,
                         RpcResultCode.Accepted
                     })
                True(!RpcMutationReplayPolicy.ReleasesReservation(
                    terminal, RpcReplayDurability.HandlerDurable),
                    terminal + " incorrectly released a terminal reservation.");

            var ledger = new RpcReplayLedger(2, 100);
            string first = new string('a', 64);
            string conflict = new string('b', 64);
            Equal(RpcReplayAdmission.New,
                ledger.TryBegin("session|operation", first, 10, out _));
            True(!ledger.Abandon("session|operation", conflict),
                "An alternate fingerprint released the live reservation.");
            True(ledger.Abandon("session|operation", first),
                "Exact NotReady reservation did not become retryable.");
            Equal(RpcReplayAdmission.Conflict,
                ledger.TryBegin("session|operation", conflict, 11, out _));

            for (int index = 0; index < 1000; index++)
            {
                Equal(RpcReplayAdmission.New,
                    ledger.TryBegin("session|operation", first, 12 + index, out _));
                if (index == 0)
                    Equal(RpcReplayAdmission.InProgress,
                        ledger.TryBegin("session|operation", first, 12 + index, out _));
                True(ledger.Abandon("session|operation", first),
                    "Repeated NotReady could not release the exact reservation.");
            }
            Equal(1, ledger.Count);
            Equal(RpcReplayAdmission.New,
                ledger.TryBegin("session|second", conflict, 1012, out _));
            True(ledger.Abandon("session|second", conflict),
                "Second retryable reservation failed.");
            Equal(RpcReplayAdmission.CapacityReached,
                ledger.TryBegin("session|flood", new string('c', 64), 1012, out _));
            Equal(2, ledger.Count);
        }

        private static void StaleEndpointOwnerFailsClosed()
        {
            const string moduleId = "runic.rpcstaletest";
            var descriptor = new ModuleDescriptor(
                moduleId,
                "RPC stale test",
                "1.0.0",
                "1.0",
                new[] { "test.rpc" });
            ModuleRegistration module = RunicRegistry.Shared.RegisterModule(descriptor);
            var service = new RunicRpcService(new ManualLogSource("RunicPersistence.Tests"));
            IDisposable endpoint = service.RegisterEndpoint(
                module,
                new RpcEndpointDescriptor(
                    moduleId,
                    moduleId + ".query",
                    "test.rpc",
                    1,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.ReadOnly),
                _ => RpcHandlerResult.Ok());
            True(service.IsEndpointOwnerCurrent(moduleId + ".query"),
                "Active endpoint owner was not recognized.");

            module.Dispose();

            True(!service.IsEndpointOwnerCurrent(moduleId + ".query"),
                "Endpoint retained authority after its module lease became stale.");
            endpoint.Dispose();
            service.Dispose();
        }

        private static void AcceptedOperationIsExplicit()
        {
            RpcHandlerResult result = RpcHandlerResult.Accept(new byte[] { 4, 2 });
            True(result.OperationAccepted, "Accepted operation lost its explicit state.");
            True(!result.Success, "Accepted asynchronous work was mislabeled complete.");
            Throws<ArgumentException>(() => RpcHandlerResult.Accept(Array.Empty<byte>()));
        }

        private static void EndpointProtocolMatchesModuleHello()
        {
            const string moduleId = "runic.rpcprotocoltest";
            var descriptor = new ModuleDescriptor(
                moduleId,
                "RPC protocol test",
                "1.0.0",
                "2.0",
                new[] { "test.rpc-protocol" });
            ModuleRegistration module = RunicRegistry.Shared.RegisterModule(descriptor);
            var service = new RunicRpcService(new ManualLogSource("RunicPersistence.Tests"));
            try
            {
                Throws<InvalidOperationException>(() => service.RegisterEndpoint(
                    module,
                    new RpcEndpointDescriptor(
                        moduleId,
                        moduleId + ".wrong",
                        "test.rpc-protocol",
                        1,
                        RpcEndpointDirection.ClientToServer,
                        RpcOperationKind.ReadOnly),
                    _ => RpcHandlerResult.Ok()));
                using (service.RegisterEndpoint(
                    module,
                    new RpcEndpointDescriptor(
                        moduleId,
                        moduleId + ".exact",
                        "test.rpc-protocol",
                        2,
                        RpcEndpointDirection.ClientToServer,
                        RpcOperationKind.ReadOnly),
                    _ => RpcHandlerResult.Ok()))
                    True(service.IsEndpointOwnerCurrent(moduleId + ".exact"),
                        "Exact module-major endpoint was not registered.");
            }
            finally
            {
                service.Dispose();
                module.Dispose();
            }
        }

        private static void QueuedPeerInfoResumesOnce()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkClientHandshake();
            True(!gate.TryAllowOrQueuePeerInfo("secret"), "Early PeerInfo must be queued.");
            gate.MarkChallenge(true);
            True(gate.TryTakeOffer(), "Late challenge must release the offer.");
            True(gate.TryTakeQueuedPeerInfo(out string password), "Queued PeerInfo was not claimed.");
            Equal("secret", password);
            True(!gate.TryTakeQueuedPeerInfo(out _), "Queued PeerInfo was claimed twice.");
            True(gate.TryAllowOrQueuePeerInfo(password), "Reflected resume was not armed.");
            True(!gate.TryAllowOrQueuePeerInfo(password), "PeerInfo original ran twice.");
        }

        private static void ReadyPeerInfoSendsOnce()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkChallenge(true);
            gate.MarkClientHandshake();
            True(gate.TryTakeOffer(), "Challenge-first offer was not released.");
            True(gate.TryAllowOrQueuePeerInfo("secret"), "Ready PeerInfo should run immediately.");
            True(!gate.TryAllowOrQueuePeerInfo("secret"), "Ready PeerInfo ran twice.");
            True(!gate.HasPendingPeerInfo, "Immediate PeerInfo retained a password.");
        }

        private static void QueuedPeerInfoDisconnectRace()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkClientHandshake();
            True(!gate.TryAllowOrQueuePeerInfo("secret"), "PeerInfo should queue.");
            gate.Close();
            gate.MarkChallenge(true);
            True(!gate.TryTakeOffer(), "Closed gate released an offer.");
            True(!gate.TryTakeQueuedPeerInfo(out _), "Closed gate leaked queued PeerInfo.");
            True(!gate.HasPendingPeerInfo, "Disconnect did not erase the password.");
        }

        private static void ClaimedPeerInfoDisconnectRace()
        {
            var gate = new RpcHandshakeGate();
            gate.MarkClientHandshake();
            gate.TryAllowOrQueuePeerInfo("secret");
            gate.MarkChallenge(true);
            gate.TryTakeOffer();
            True(gate.TryTakeQueuedPeerInfo(out string password), "Resume was not claimed.");
            gate.Close();
            True(!gate.TryAllowOrQueuePeerInfo(password), "A disconnect race allowed reflection.");
            True(!gate.PeerInfoSent, "A closed pre-invoke resume was mislabeled sent.");
        }

        private static void SteamIdentityClassification()
        {
            True(RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "76561198000000001", "76561198000000001", "76561198000000001", 0, out string subject),
                "Matching authenticated transport identities were rejected.");
            Equal("76561198000000001", subject);
            True(!RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "", "76561198000000001", "76561198000000001", 0, out _), "Empty hostname elevated.");
            True(!RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "display name", "76561198000000001", "76561198000000001", 0, out _), "Display hostname elevated.");
            True(!RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "0", "0", "0", 0, out _), "Zero Steam identity elevated.");
            True(!RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "76561198000000001", "76561198000000002", "76561198000000001", 0, out _), "Mismatch elevated.");
            True(!RunicRpcService.TryClassifyAuthenticatedSteamIdentity(
                    "76561198000000001", "76561198000000001", "76561198000000001", 1, out _), "Unauthenticated flag elevated.");
        }

        private static void ServerAdminIdentityClassification()
        {
            var steam = new RpcPeerIdentity(
                "steam",
                "76561198000000001",
                RpcIdentityAssurance.BackendAccount);
            True(RunicRpcService.IsEligibleSteamAdminIdentity(steam),
                "Canonical backend-bound Steam identity was rejected.");
            True(RunicRpcService.SteamAdminIdentityMatchesTransport(
                    steam,
                    "76561198000000001"),
                "Exact Steam transport identity did not match.");
            True(!RunicRpcService.SteamAdminIdentityMatchesTransport(
                    steam,
                    "76561198000000002"),
                "Mismatched Steam transport identity elevated admin.");
            True(!RunicRpcService.IsEligibleSteamAdminIdentity(new RpcPeerIdentity(
                    "steam",
                    "76561198000000001",
                    RpcIdentityAssurance.ConnectionBound)),
                "Connection-bound identity elevated admin.");
            True(!RunicRpcService.IsEligibleSteamAdminIdentity(new RpcPeerIdentity(
                    "playfab.entity",
                    "76561198000000001",
                    RpcIdentityAssurance.BackendAccount)),
                "PlayFab subject shaped like Steam elevated admin.");
            True(!RunicRpcService.IsEligibleSteamAdminIdentity(new RpcPeerIdentity(
                    "steam",
                    "00076561198000000001",
                    RpcIdentityAssurance.BackendAccount)),
                "Noncanonical Steam subject elevated admin.");
        }

        private static void IdempotencyUtf8Bound()
        {
            string bounded = new string('a', 256);
            True(RunicRpcService.TryNormalizeIdempotencyKey(
                    bounded, true, out string exact, out _) &&
                 string.Equals(bounded, exact, StringComparison.Ordinal),
                "256 UTF-8 bytes should fit without normalization.");
            True(!RunicRpcService.TryNormalizeIdempotencyKey(
                    new string('\u00e9', 129), true, out _, out string tooLong) &&
                 tooLong == "idempotency-key-too-long", "Byte bound was not enforced.");
            True(!RunicRpcService.TryNormalizeIdempotencyKey(
                    "\ud800", true, out _, out string invalid) &&
                 invalid == "idempotency-key-invalid", "Invalid UTF-16 did not fail explicitly.");
            True(!RunicRpcService.TryNormalizeIdempotencyKey(
                    " key", true, out string leading, out string leadingFailure) &&
                 leading == " key" && leadingFailure == "idempotency-key-invalid",
                "Leading whitespace was normalized instead of rejected.");
            True(!RunicRpcService.TryNormalizeIdempotencyKey(
                    "key ", true, out string trailing, out string trailingFailure) &&
                 trailing == "key " && trailingFailure == "idempotency-key-invalid",
                "Trailing whitespace was normalized instead of rejected.");
            Throws<ArgumentException>(() => new RpcHandlerResult(
                RpcResultCode.InvalidRequest,
                " noncanonical-reason"));
            Throws<ArgumentException>(() => new RpcEnvelope(
                1,
                "runic.test",
                "request",
                "correlation",
                " key",
                Array.Empty<byte>(),
                1));
        }

        private static void TransportBoundaryIsolation()
        {
            Exception[] faults =
            {
                new InvalidOperationException("registry-cap"),
                new System.Security.Cryptography.CryptographicException("nonce"),
                new System.IO.IOException("send"),
                new TargetInvocationException(new MissingMethodException("reflection"))
            };
            int failures = 0;
            foreach (Exception fault in faults)
                RpcTransportBoundary.Run(
                    () => throw fault,
                    _ => failures++);
            Equal(4, failures);
            True(!RpcTransportBoundary.RunGate(
                    () => throw new InvalidOperationException("gate"),
                    _ => throw new InvalidOperationException("logger")),
                "A double-fault boundary did not return fail-closed false.");
        }

        private static void DisconnectFailureRetries()
        {
            var clock = new TestClock();
            int disconnectCalls = 0;
            var runtime = TestRuntime(
                false,
                clock,
                peer =>
                {
                    disconnectCalls++;
                    throw new InvalidOperationException("injected disconnect failure");
                });
            var service = new RunicRpcService(
                new ManualLogSource("disconnect-retry"),
                new RunicRegistry(),
                runtime);
            var socket = new TestSocket("disconnect-retry");
            var peer = new ZNetPeer(socket, true);
            True(service.OnConnectionStarted(peer), "Provisional session did not start.");
            service.FailClosed(peer.m_rpc, "injected-failure", null);
            for (int attempt = 0; attempt < RunicRpcService.MaximumDisconnectAttempts; attempt++)
            {
                service.Tick();
                True(socket.IsConnected(), "Transport closed before bounded retries completed.");
                clock.AdvanceMilliseconds(RunicRpcService.DisconnectRetryMilliseconds);
            }
            service.Tick();
            Equal(RunicRpcService.MaximumDisconnectAttempts, disconnectCalls);
            True(!socket.IsConnected(), "Close fallback did not terminate the rejected transport.");
            service.Dispose();
        }

        private static void HandshakeClaimsRejectDuplicates()
        {
            Throws<ArgumentException>(() => new RpcHandshakeClaims(new[]
            {
                new RpcHandshakeClaim("runic.test.hash", "a"),
                new RpcHandshakeClaim("runic.test.hash", "b")
            }));
        }

        private static void HandshakeClaimsRejectEdgeWhitespace()
        {
            Throws<ArgumentException>(() => new RpcHandshakeClaim("runic.test.hash", " value"));
            Throws<ArgumentException>(() => new RpcHandshakeClaim("runic.test.hash", "value "));
            Throws<ArgumentException>(() => new RpcHandshakeClaim("runic.test.hash", "\u00a0value"));
            Throws<ArgumentException>(() => new ProtocolHello(
                " nonce",
                Array.Empty<ModuleProtocolState>()));
            Throws<ArgumentException>(() => new ProtocolHello(
                "nonce ",
                Array.Empty<ModuleProtocolState>()));
            byte[] nonce = Enumerable.Repeat((byte)1, RpcHandshakeCodec.NonceBytes).ToArray();
            var hello = new ProtocolHello(
                RpcWireCodec.ToLowerHex(nonce),
                new[] { new ModuleProtocolState("runic.test", "1.0.0", 1, null) },
                new[] { new RpcHandshakeClaim("runic.test.hash", "valuex") });
            byte[] encoded = RpcHandshakeCodec.Encode(new RpcHandshakeProfile
            {
                Nonce = nonce,
                EchoNonce = Array.Empty<byte>(),
                Hello = hello
            });
            encoded[encoded.Length - 1] = (byte)' ';
            True(!RpcHandshakeCodec.TryDecode(encoded, out _),
                "The handshake codec normalized trailing claim whitespace.");
        }

        private static void HandshakeClaimsRejectFloods()
        {
            Throws<ArgumentOutOfRangeException>(() => new RpcHandshakeClaims(
                Enumerable.Range(0, RpcHandshakeClaims.MaximumClaims + 1)
                    .Select(index => new RpcHandshakeClaim("runic.test.c" + index, "x"))));
            Throws<ArgumentOutOfRangeException>(() => new RpcHandshakeClaims(
                Enumerable.Range(0, RpcHandshakeClaims.MaximumClaims)
                    .Select(index => new RpcHandshakeClaim(
                        "runic.test.large" + index,
                        new string('x', 300)))));
        }

        private static void ThrowingClaimProviderFailsClosed()
        {
            var registry = new RunicRegistry();
            ModuleRegistration module = RegisterRpcTestModule(registry);
            var clock = new TestClock();
            int disconnects = 0;
            var runtime = TestRuntime(true, clock, peer => { disconnects++; peer.Dispose(); });
            var service = new RunicRpcService(new ManualLogSource("claim-throw"), registry, runtime);
            IDisposable provider = service.RegisterHandshakeClaimProvider(
                module,
                () => throw new InvalidOperationException("provider threw"));
            var socket = new TestSocket("claim-throw");
            var peer = new ZNetPeer(socket, false);
            True(service.OnConnectionStarted(peer), "Server provisional session did not start.");
            RpcTransportBoundary.Run(
                () => service.OnServerHandshake(peer.m_rpc),
                exception => service.FailClosed(peer.m_rpc, "server-handshake-failed", exception));
            service.Tick();
            Equal(1, disconnects);
            True(!socket.IsConnected(), "Throwing provider did not fail closed.");
            provider.Dispose();
            service.Dispose();
            module.Dispose();
        }

        private static void MissingClaimPreventsAdmission()
        {
            Equal("claim-missing", ClaimAdmissionReason(null, "expected"));
        }

        private static void MismatchedClaimPreventsAdmission()
        {
            Equal("claim-mismatch", ClaimAdmissionReason("wrong", "expected"));
        }

        private static void ProvisionalCapacity100() => ProvisionalCapacityFlood(100);
        private static void ProvisionalCapacity1000() => ProvisionalCapacityFlood(1000);

        private static void ProvisionalCapacityFlood(int connectionCount)
        {
            var service = new RunicRpcService(
                new ManualLogSource("session-cap"),
                new RunicRegistry(),
                TestRuntime(false, new TestClock(), peer => peer.Dispose()));
            var accepted = new List<ZNetPeer>();
            int refused = 0;
            for (int index = 0; index < connectionCount; index++)
            {
                var peer = new ZNetPeer(new TestSocket("flood-" + index), true);
                if (service.OnConnectionStarted(peer)) accepted.Add(peer);
                else
                {
                    refused++;
                    True(!peer.m_socket.IsConnected(), "Refused provisional socket remained open.");
                }
            }
            Equal(RunicRpcService.MaximumSessions, accepted.Count);
            Equal(connectionCount - RunicRpcService.MaximumSessions, refused);
            foreach (ZNetPeer peer in accepted) service.OnConnectionClosed(peer);

            var replacement = new ZNetPeer(new TestSocket("replacement"), true);
            True(service.OnConnectionStarted(replacement), "Cleanup did not release provisional capacity.");
            service.OnConnectionClosed(replacement);
            service.Dispose();
        }

        private static void DirectWireIdempotencyKeysAreCanonical()
        {
            AssertMalformedDirectIdempotencyKey("xkey", " key");
            AssertMalformedDirectIdempotencyKey("keyx", "key ");

            using (var harness = new RpcPairHarness())
            {
                int handlerCalls = 0;
                using (harness.Server.RegisterEndpoint(
                           harness.ServerModule,
                           RpcPairHarness.Endpoint,
                           request =>
                           {
                               handlerCalls++;
                               Equal("key", request.IdempotencyKey);
                               return RpcHandlerResult.Ok();
                           }))
                using (harness.Client.RegisterEndpoint(
                           harness.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    RpcResponse first = null;
                    RpcSendResult firstSend = harness.Client.SendToServer(
                        harness.ClientModule,
                        RpcPairHarness.Endpoint.EndpointId,
                        Array.Empty<byte>(),
                        "key",
                        TimeSpan.FromSeconds(1),
                        response => first = response);
                    True(firstSend.Accepted, "Canonical direct key was rejected.");
                    harness.PumpRoundTrip();
                    True(first != null && first.Success && !first.Replayed,
                        "Canonical direct key did not reach the handler exactly once.");

                    RpcResponse replay = null;
                    RpcSendResult replaySend = harness.Client.SendToServer(
                        harness.ClientModule,
                        RpcPairHarness.Endpoint.EndpointId,
                        Array.Empty<byte>(),
                        "key",
                        TimeSpan.FromSeconds(1),
                        response => replay = response);
                    True(replaySend.Accepted, "Canonical replay was rejected before the ledger.");
                    harness.PumpRoundTrip();
                    Equal(1, handlerCalls);
                    True(replay != null && replay.Success && replay.Replayed,
                        "Exact canonical replay did not use the cached result.");
                }
            }
        }

        private static void AssertMalformedDirectIdempotencyKey(string safeKey, string malformedKey)
        {
            using (var harness = new RpcPairHarness())
            {
                int handlerCalls = 0;
                using (harness.Server.RegisterEndpoint(
                           harness.ServerModule,
                           RpcPairHarness.Endpoint,
                           _ =>
                           {
                               handlerCalls++;
                               return RpcHandlerResult.Ok();
                           }))
                using (harness.Client.RegisterEndpoint(
                           harness.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    RpcSendResult send = harness.Client.SendToServer(
                        harness.ClientModule,
                        RpcPairHarness.Endpoint.EndpointId,
                        Array.Empty<byte>(),
                        safeKey,
                        TimeSpan.FromSeconds(1),
                        _ => { });
                    True(send.Accepted, "Safe source frame was rejected before wire mutation.");
                    harness.ReplaceNextServerWireText(safeKey, malformedKey);
                    harness.PumpServerOnly();
                    Equal(0, handlerCalls);
                    True(harness.Server.TryGetPeer(RpcPairHarness.ClientUid, out RpcPeerSnapshot rejected) &&
                         !rejected.Ready,
                        "A noncanonical direct wire key did not fail the exact session closed.");
                }
            }
        }

        private static void DirectRpcEndToEnd()
        {
            using (var harness = new RpcPairHarness())
            {
                int handlerCalls = 0;
                IDisposable serverEndpoint = harness.Server.RegisterEndpoint(
                    harness.ServerModule,
                    RpcPairHarness.Endpoint,
                    request =>
                    {
                        handlerCalls++;
                        return RpcHandlerResult.Ok(request.Payload);
                    });
                IDisposable clientEndpoint = harness.Client.RegisterEndpoint(
                    harness.ClientModule,
                    RpcPairHarness.Endpoint,
                    _ => RpcHandlerResult.Deny("client-handler-unused"));

                RpcResponse first = null;
                RpcSendResult firstSend = harness.Client.SendToServer(
                    harness.ClientModule,
                    RpcPairHarness.Endpoint.EndpointId,
                    Bytes("payload"),
                    "same-operation",
                    TimeSpan.FromSeconds(1),
                    response => first = response);
                True(firstSend.Accepted, "First direct request was rejected: " + firstSend.ReasonCode);
                harness.PumpRoundTrip();
                True(first != null && first.Success && !first.Replayed, "First response failed.");
                Equal("payload", Text(first.Payload));

                RpcResponse second = null;
                RpcSendResult secondSend = harness.Client.SendToServer(
                    harness.ClientModule,
                    RpcPairHarness.Endpoint.EndpointId,
                    Bytes("payload"),
                    "same-operation",
                    TimeSpan.FromSeconds(1),
                    response => second = response);
                True(secondSend.Accepted, "Replay request was rejected before the peer ledger.");
                harness.PumpRoundTrip();
                Equal(1, handlerCalls);
                True(second != null && second.Success && second.Replayed,
                    "Mutation replay did not return the cached response.");
                serverEndpoint.Dispose();
                clientEndpoint.Dispose();
            }
        }

        private static void DirectRpcNotReadyRetriesExactRequest()
        {
            foreach (RpcResultCode transient in new[]
                     {
                         RpcResultCode.NotReady,
                         RpcResultCode.RateLimited,
                         RpcResultCode.CapacityReached,
                         RpcResultCode.TimedOut
                     })
                AssertDirectRetryPolicy(
                    transient,
                    RpcReplayDurability.SessionOnly,
                    shouldReenter: true);

            AssertDirectRetryPolicy(
                RpcResultCode.HandlerFailed,
                RpcReplayDurability.SessionOnly,
                shouldReenter: false);
            AssertDirectRetryPolicy(
                RpcResultCode.HandlerFailed,
                RpcReplayDurability.HandlerDurable,
                shouldReenter: true);

            using (var harness = new RpcPairHarness())
            {
                int calls = 0;
                bool succeed = false;
                using (harness.Server.RegisterEndpoint(
                           harness.ServerModule,
                           RpcPairHarness.Endpoint,
                           _ =>
                           {
                               calls++;
                               return succeed
                                   ? RpcHandlerResult.Ok(Bytes("ready"))
                                   : new RpcHandlerResult(
                                       RpcResultCode.NotReady, "test-not-ready");
                           }))
                using (harness.Client.RegisterEndpoint(
                           harness.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    for (int index = 0; index < 32; index++)
                    {
                        RpcResponse response = SendDirectMutation(
                            harness, RpcPairHarness.Endpoint,
                            "repeat-not-ready", Bytes("same"));
                        Equal(RpcResultCode.NotReady, response.Code);
                        True(!response.Replayed,
                            "A repeated NotReady response was replay-cached.");
                    }
                    Equal(32, calls);
                    succeed = true;
                    RpcResponse completed = SendDirectMutation(
                        harness, RpcPairHarness.Endpoint,
                        "repeat-not-ready", Bytes("same"));
                    True(completed.Success && !completed.Replayed,
                        "Exact request did not re-enter after repeated NotReady responses.");
                    Equal(33, calls);
                }
            }

            using (var harness = new RpcPairHarness())
            {
                int calls = 0;
                bool releaseFirst = false;
                using (harness.Server.RegisterEndpoint(
                           harness.ServerModule,
                           RpcPairHarness.Endpoint,
                           request =>
                           {
                               calls++;
                               return releaseFirst &&
                                      string.Equals(
                                          request.IdempotencyKey, "flood-000",
                                          StringComparison.Ordinal)
                                   ? RpcHandlerResult.Ok()
                                   : new RpcHandlerResult(
                                       RpcResultCode.NotReady, "test-not-ready");
                           }))
                using (harness.Client.RegisterEndpoint(
                           harness.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    for (int index = 0; index < 256; index++)
                    {
                        string key = "flood-" + index.ToString("000");
                        RpcResponse response = SendDirectMutation(
                            harness, RpcPairHarness.Endpoint, key, Bytes("same"));
                        Equal(RpcResultCode.NotReady, response.Code);
                        harness.AdvanceMilliseconds(20);
                    }
                    Equal(256, calls);
                    RpcResponse bounded = SendDirectMutation(
                        harness, RpcPairHarness.Endpoint,
                        "flood-overflow", Bytes("same"));
                    Equal(RpcResultCode.CapacityReached, bounded.Code);
                    Equal(256, calls);

                    RpcResponse conflict = SendDirectMutation(
                        harness, RpcPairHarness.Endpoint,
                        "flood-000", Bytes("different"));
                    Equal(RpcResultCode.ReplayConflict, conflict.Code);
                    Equal(256, calls);

                    releaseFirst = true;
                    RpcResponse exact = SendDirectMutation(
                        harness, RpcPairHarness.Endpoint,
                        "flood-000", Bytes("same"));
                    True(exact.Success && !exact.Replayed,
                        "An exact abandoned reservation could not re-enter at capacity.");
                    Equal(257, calls);
                }
            }

            using (var disconnected = new RpcPairHarness())
            {
                using (disconnected.Server.RegisterEndpoint(
                           disconnected.ServerModule,
                           RpcPairHarness.Endpoint,
                           _ => new RpcHandlerResult(
                               RpcResultCode.NotReady, "test-not-ready")))
                using (disconnected.Client.RegisterEndpoint(
                           disconnected.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                    Equal(
                        RpcResultCode.NotReady,
                        SendDirectMutation(
                            disconnected, RpcPairHarness.Endpoint,
                            "disconnect-retry", Bytes("same")).Code);
            }
            using (var reconnected = new RpcPairHarness())
            {
                int calls = 0;
                using (reconnected.Server.RegisterEndpoint(
                           reconnected.ServerModule,
                           RpcPairHarness.Endpoint,
                           _ => { calls++; return RpcHandlerResult.Ok(); }))
                using (reconnected.Client.RegisterEndpoint(
                           reconnected.ClientModule,
                           RpcPairHarness.Endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    RpcResponse response = SendDirectMutation(
                        reconnected, RpcPairHarness.Endpoint,
                        "disconnect-retry", Bytes("same"));
                    True(response.Success && !response.Replayed,
                        "A disconnected session retained a retryable reservation.");
                    Equal(1, calls);
                }
            }
        }

        private static void AssertDirectRetryPolicy(
            RpcResultCode firstCode,
            RpcReplayDurability durability,
            bool shouldReenter)
        {
            using (var harness = new RpcPairHarness())
            {
                RpcEndpointDescriptor endpoint = RpcPairHarness.EndpointWith(durability);
                int calls = 0;
                using (harness.Server.RegisterEndpoint(
                           harness.ServerModule,
                           endpoint,
                           _ =>
                           {
                               calls++;
                               return calls == 1
                                   ? new RpcHandlerResult(firstCode, "test-first-result")
                                   : RpcHandlerResult.Ok(Bytes("ready"));
                           }))
                using (harness.Client.RegisterEndpoint(
                           harness.ClientModule,
                           endpoint,
                           _ => RpcHandlerResult.Deny("client-handler-unused")))
                {
                    string key = "policy-" + ((int)firstCode).ToString() + "-" +
                                 ((int)durability).ToString();
                    RpcResponse first = SendDirectMutation(
                        harness, endpoint, key, Bytes("same"));
                    Equal(firstCode, first.Code);
                    True(!first.Replayed, "First response was incorrectly marked replayed.");

                    RpcResponse conflict = SendDirectMutation(
                        harness, endpoint, key, Bytes("different"));
                    Equal(RpcResultCode.ReplayConflict, conflict.Code);
                    Equal(1, calls);

                    RpcResponse exact = SendDirectMutation(
                        harness, endpoint, key, Bytes("same"));
                    if (shouldReenter)
                    {
                        True(exact.Success && !exact.Replayed,
                            firstCode + " did not release the exact reservation for " + durability + ".");
                        Equal(2, calls);
                    }
                    else
                    {
                        Equal(firstCode, exact.Code);
                        True(exact.Replayed,
                            firstCode + " was not terminally cached for " + durability + ".");
                        Equal(1, calls);
                    }
                }
            }
        }

        private static RpcResponse SendDirectMutation(
            RpcPairHarness harness,
            RpcEndpointDescriptor endpoint,
            string idempotencyKey,
            byte[] payload)
        {
            RpcResponse response = null;
            RpcSendResult sent = harness.Client.SendToServer(
                harness.ClientModule,
                endpoint.EndpointId,
                payload,
                idempotencyKey,
                TimeSpan.FromSeconds(1),
                value => response = value);
            True(sent.Accepted, "Direct retry-policy request was rejected: " + sent.ReasonCode);
            harness.PumpRoundTrip();
            True(response != null, "Direct retry-policy request produced no response.");
            return response;
        }

        private static void ClientServerSnapshotIsDirectSessionBound()
        {
            using (var harness = new RpcPairHarness())
            {
                True(harness.Client.TryGetPeer(
                        RpcPairHarness.ServerUid,
                        out RpcPeerSnapshot server),
                    "Client could not resolve its direct server session.");
                Equal("valheim.server", server.Identity.Authority);
                Equal(RpcIdentityAssurance.ConnectionBound, server.Identity.Assurance);
                True(server.Ready && server.Current, "Client server snapshot was not current.");
                True(!harness.Client.TryGetPeer(RpcPairHarness.ServerUid + 1, out _),
                    "Client exposed a peer other than its server connection.");
                Equal(1, harness.Client.GetPeers().Count);
                Equal(server.SessionId, harness.Client.GetPeers()[0].SessionId);
            }
        }

        private static void DirectAdminBindingRpcReplay()
        {
            using (var harness = new RpcPairHarness(true, true))
            {
                var serverStorage = new TestPlayerBindingStorage();
                var serverStore = new PlayerIdentityBindingStore(
                    () => "valheim.1111111111111111",
                    serverStorage);
                var clientStore = new PlayerIdentityBindingStore(
                    () => string.Empty,
                    new TestPlayerBindingStorage());
                serverStore.Tick();
                clientStore.Tick();
                IDisposable serverEndpoints = PlayerIdentityBindingCommands.ConfigureRpc(
                    harness.ServerModule,
                    harness.Server,
                    serverStore,
                    null);
                IDisposable clientEndpoints = PlayerIdentityBindingCommands.ConfigureRpc(
                    harness.ClientModule,
                    harness.Client,
                    clientStore,
                    null);
                try
                {
                    RpcResponse first = null;
                    RpcSendResult firstSend = harness.Client.SendToServer(
                        harness.ClientModule,
                        PlayerIdentityBindingCommands.EnrollEndpointId,
                        PlayerIdentityBindingCommands.EncodeInt64(RpcPairHarness.ClientUid),
                        "binding-operation",
                        TimeSpan.FromSeconds(1),
                        response => first = response);
                    True(firstSend.Accepted, "Direct admin enrollment was not sent: " + firstSend.ReasonCode);
                    harness.PumpRoundTrip();
                    True(first != null && first.Success && !first.Replayed,
                        "First direct admin enrollment failed.");
                    Equal(1, serverStorage.WriteAttempts);
                    True(harness.Server.TryGetPeer(
                            RpcPairHarness.ClientUid,
                            out RpcPeerSnapshot target),
                        "Direct admin target disappeared.");
                    Equal(
                        RpcPlayerBindingStatus.Verified,
                        serverStore.Resolve(target.Identity, 9001));

                    RpcResponse replay = null;
                    RpcSendResult replaySend = harness.Client.SendToServer(
                        harness.ClientModule,
                        PlayerIdentityBindingCommands.EnrollEndpointId,
                        PlayerIdentityBindingCommands.EncodeInt64(RpcPairHarness.ClientUid),
                        "binding-operation",
                        TimeSpan.FromSeconds(1),
                        response => replay = response);
                    True(replaySend.Accepted, "Replay did not reach the direct session ledger.");
                    harness.PumpRoundTrip();
                    True(replay != null && replay.Success && replay.Replayed,
                        "Direct admin replay was not returned from the session ledger.");
                    Equal(1, serverStorage.WriteAttempts);
                }
                finally
                {
                    clientEndpoints.Dispose();
                    serverEndpoints.Dispose();
                    clientStore.Dispose();
                    serverStore.Dispose();
                }
            }
        }

        private static void AccountBoundActorFailsClosed()
        {
            using (var harness = new RpcPairHarness())
            {
                True(harness.Server.TryGetPeer(RpcPairHarness.ClientUid, out RpcPeerSnapshot peer),
                    "Ready client snapshot missing.");
                True(!harness.Server.TryResolveActor(
                        peer,
                        RpcActorAssurance.AccountBoundPlayer,
                        out _,
                        out string reason),
                    "Legacy player ID was trusted without a persisted resolver.");
                Equal("actor-binding-unavailable", reason);
                True(harness.Server.TryResolveActor(
                        peer,
                        RpcActorAssurance.TransportOwnedCharacter,
                        out RpcActorSnapshot actor,
                        out _),
                    "Transport-owned actor was not resolved.");
                True(actor.IsServerAdmin, "Injected exact-session admin fact was lost.");
                True(!actor.HasVerifiedPlayerBinding, "Transport-only actor claimed a legacy binding.");
            }
        }

        private static void DedicatedActorUsesReplicatedPlayerZdoEvidence()
        {
            var announced = new ZDOID(1952735848L, 1U);
            True(RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced,
                    announced,
                    1952735848L,
                    1952735848L,
                    true,
                    true,
                    1595169003L,
                    out ZDOID characterId,
                    out long playerId,
                    out string reason),
                "Exact dedicated-server Player ZDO evidence was rejected: " + reason);
            Equal(announced, characterId);
            Equal(1595169003L, playerId);
            Equal("actor-transport-owned", reason);
            Equal(1875862075, "Player".GetStableHashCode());

            True(!RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced, new ZDOID(1952735848L, 2U), 1952735848L, 1952735848L,
                    true, true, 1595169003L, out _, out _, out reason),
                "A different character ZDO was accepted.");
            Equal("actor-character-id-mismatch", reason);
            True(!RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced, announced, 1952735848L, 44L,
                    true, true, 1595169003L, out _, out _, out reason),
                "A character ZDO owned by another peer was accepted.");
            Equal("actor-character-owner-mismatch", reason);
            True(!RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced, announced, 1952735848L, 1952735848L,
                    false, true, 1595169003L, out _, out _, out reason),
                "An invalid character ZDO was accepted.");
            Equal("actor-character-invalid", reason);
            True(!RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced, announced, 1952735848L, 1952735848L,
                    true, false, 1595169003L, out _, out _, out reason),
                "A non-Player prefab was accepted as an actor.");
            Equal("actor-character-prefab-mismatch", reason);
            True(!RunicRpcService.TryClassifyTransportOwnedCharacterEvidence(
                    announced, announced, 1952735848L, 1952735848L,
                    true, true, 0L, out _, out _, out reason),
                "A Player ZDO without a player ID was accepted.");
            Equal("actor-player-id-missing", reason);
        }

        private static void DirectSessionIdentityUpgradeIsMonotonic()
        {
            int phase = 0;
            RpcRuntimeIdentityResolver identity = delegate(
                ZNetPeer _,
                string sessionId,
                bool localIsServer,
                out RpcPeerIdentity result)
            {
                if (!localIsServer)
                    result = new RpcPeerIdentity(
                        "valheim.server", "server", RpcIdentityAssurance.ConnectionBound);
                else if (phase == 0)
                    result = new RpcPeerIdentity(
                        "valheim.transport",
                        "session-" + sessionId,
                        RpcIdentityAssurance.ConnectionBound);
                else
                    result = new RpcPeerIdentity(
                        "steam",
                        phase == 1 ? "76561198000000001" : "76561198000000002",
                        RpcIdentityAssurance.BackendAccount);
                return true;
            };
            var clock = new TestClock();
            var service = new RunicRpcService(
                new ManualLogSource("identity-upgrade"),
                new RunicRegistry(),
                new RunicRpcRuntimeContext(
                    () => true,
                    () => clock.UtcNowTicks,
                    peer => peer.Dispose(),
                    (_, __) => { },
                    identity));
            var peer = new ZNetPeer(new TestSocket("identity-upgrade"), false) { m_uid = 31337 };
            True(service.OnConnectionStarted(peer), "Provisional direct session did not start.");
            service.OnPeerAdmitted(peer);
            True(service.TryGetPeer(peer.m_uid, out RpcPeerSnapshot provisional),
                "Provisional admitted peer was not visible.");
            Equal(RpcIdentityAssurance.ConnectionBound, provisional.Identity.Assurance);

            phase = 1;
            True(service.TryGetPeer(peer.m_uid, out RpcPeerSnapshot upgraded),
                "The same live session did not refresh its authenticated transport identity.");
            Equal(RpcIdentityAssurance.BackendAccount, upgraded.Identity.Assurance);
            Equal("steam", upgraded.Identity.Authority);
            Equal("76561198000000001", upgraded.Identity.SubjectId);

            phase = 0;
            True(service.TryGetPeer(peer.m_uid, out RpcPeerSnapshot retained),
                "Temporary transport-proof unavailability downgraded a proven session.");
            Equal("76561198000000001", retained.Identity.SubjectId);

            phase = 2;
            True(!service.TryGetPeer(peer.m_uid, out _),
                "A different backend subject replaced the identity of a live session.");
            service.Tick();
            True(!peer.m_rpc.IsConnected(),
                "A conflicting backend identity did not fail the session closed.");
            service.Dispose();
            peer.Dispose();
        }

        private static void ProvisionalRoutedResetPreservesDirectSession()
        {
            var service = new RunicRpcService(
                new ManualLogSource("provisional-routed-reset"),
                new RunicRegistry(),
                TestRuntime(true, new TestClock(), peer => peer.Dispose()));
            var peer = new ZNetPeer(new TestSocket("provisional-routed-reset"), false)
            {
                m_uid = 3434
            };

            True(service.OnConnectionStarted(peer), "The direct provisional session did not start.");
            service.OnRoutedPeerRemoving(peer);
            True(!service.CanReceivePeerInfo(peer.m_rpc, out string provisionalReason),
                "An offer-free provisional connection was unexpectedly admissible.");
            Equal("runic-handshake-missing", provisionalReason);

            service.OnPeerAdmitted(peer);
            True(service.TryGetPeer(peer.m_uid, out RpcPeerSnapshot admitted) && admitted.Current,
                "The preserved direct session could not be admitted.");
            service.OnRoutedPeerRemoving(peer);
            True(!service.TryGetPeer(peer.m_uid, out _),
                "An admitted routed peer survived the real RemovePeer lifecycle signal.");
            True(!service.CanReceivePeerInfo(peer.m_rpc, out string closedReason),
                "A routed-removed direct session remained usable.");
            Equal("runic-rpc-handler-unavailable", closedReason);

            peer.Dispose();
            service.Dispose();
        }

        private static void HarmonyOrderingIsExplicit()
        {
            string[] methods =
            {
                "ZNetOnNewConnectionPrefix",
                "ZNetRpcServerHandshakePrefix",
                "ZNetRpcClientHandshakePrefix",
                "ZNetSendPeerInfoPrefix",
                "ZNetRpcPeerInfoPrefix"
            };
            foreach (string name in methods)
            {
                MethodInfo method = typeof(TransportPatches).GetMethod(
                    name,
                    BindingFlags.Static | BindingFlags.NonPublic);
                True(method != null, "Missing Harmony boundary " + name + ".");
                string text = string.Join("|", method.GetCustomAttributesData()
                    .Select(attribute => attribute.ToString()));
                True(text.Contains(typeof(HarmonyLib.HarmonyPriority).FullName),
                    name + " has no explicit priority.");
                True(text.Contains(typeof(HarmonyLib.HarmonyBefore).FullName),
                    name + " has no compatibility ordering.");
            }
            int runic = RunicRpcService.DirectRpcName.GetStableHashCode();
            True(runic != "RPC_Jotunn_ReceiveVersionData".GetStableHashCode(), "Jotunn RPC hash collision.");
            True(runic != "ServerSync VersionCheck".GetStableHashCode(), "ServerSync RPC hash collision.");
            True(runic != "ConditionalConfigSync VersionCheck".GetStableHashCode(), "CCS RPC hash collision.");
        }

        private static void PeerUidCollisionFailsClosed()
        {
            var registry = new RunicRegistry();
            var clock = new TestClock();
            var disconnected = new List<ZNetPeer>();
            var service = new RunicRpcService(
                new ManualLogSource("uid-collision"),
                registry,
                TestRuntime(true, clock, peer => { disconnected.Add(peer); peer.Dispose(); }));
            var first = new ZNetPeer(new TestSocket("first"), false) { m_uid = 42 };
            var second = new ZNetPeer(new TestSocket("second"), false) { m_uid = 42 };
            True(service.OnConnectionStarted(first), "First session did not start.");
            True(service.OnConnectionStarted(second), "Second provisional session did not start.");
            service.OnPeerAdmitted(first);
            True(service.TryGetPeer(42, out RpcPeerSnapshot original), "First UID was not indexed.");
            service.OnPeerAdmitted(second);
            True(service.TryGetPeer(42, out RpcPeerSnapshot after), "Live UID was removed.");
            Equal(original.SessionId, after.SessionId);
            service.Tick();
            Equal(1, disconnected.Count);
            True(ReferenceEquals(second, disconnected[0]), "UID collision disconnected the live session.");
            service.OnConnectionClosed(first);
            service.OnConnectionClosed(second);
            service.Dispose();
        }

        private static void ClaimCodecRoundTrip()
        {
            byte[] nonce = Enumerable.Range(0, RpcHandshakeCodec.NonceBytes).Select(value => (byte)value).ToArray();
            var hello = new ProtocolHello(
                RpcWireCodec.ToLowerHex(nonce),
                new[] { new ModuleProtocolState("runic.test", "1.0.0", 1, new[] { "test.rpc" }) },
                new[] { new RpcHandshakeClaim("runic.test.topology", "abc123") });
            byte[] encoded = RpcHandshakeCodec.Encode(new RpcHandshakeProfile
            {
                Nonce = nonce,
                EchoNonce = Array.Empty<byte>(),
                Hello = hello
            });
            True(RpcHandshakeCodec.TryDecode(encoded, out RpcHandshakeProfile decoded),
                "Claim handshake did not decode.");
            True(decoded.Hello.Claims.TryGetValue("runic.test.topology", out string value),
                "Decoded claim missing.");
            Equal("abc123", value);
            byte[] mismatchedHelloNonce = (byte[])encoded.Clone();
            mismatchedHelloNonce[36] = mismatchedHelloNonce[36] == (byte)'0' ? (byte)'1' : (byte)'0';
            True(!RpcHandshakeCodec.TryDecode(mismatchedHelloNonce, out _),
                "The evidence-visible hello nonce was not bound to the binary session nonce.");
            encoded[encoded.Length - 1] ^= 0x1;
            Equal("abc123", value);
        }

        private static string ClaimAdmissionReason(string clientValue, string expected)
        {
            var serverRegistry = new RunicRegistry();
            var clientRegistry = new RunicRegistry();
            ModuleRegistration serverModule = RegisterRpcTestModule(serverRegistry);
            ModuleRegistration clientModule = RegisterRpcTestModule(clientRegistry);
            var clock = new TestClock();
            var server = new RunicRpcService(
                new ManualLogSource("claim-server"),
                serverRegistry,
                TestRuntime(true, clock, peer => peer.Dispose()));
            var client = new RunicRpcService(
                new ManualLogSource("claim-client"),
                clientRegistry,
                TestRuntime(false, clock, peer => peer.Dispose()));
            IDisposable provider = null;
            if (clientValue != null)
            {
                provider = client.RegisterHandshakeClaimProvider(
                    clientModule,
                    () => new[] { new RpcHandshakeClaim("runic.test.topology", clientValue) });
            }
            IDisposable evaluator = server.RegisterHandshakeClaimEvaluator(
                serverModule,
                "runic.test.topology-check",
                claims => !claims.TryGetValue("runic.test.topology", out string value)
                    ? RpcHandshakeClaimEvaluation.Deny("claim-missing")
                    : string.Equals(value, expected, StringComparison.Ordinal)
                        ? RpcHandshakeClaimEvaluation.Allow()
                        : RpcHandshakeClaimEvaluation.Deny("claim-mismatch"));
            TestSocket.CreatePair("claim-server", "claim-client", out TestSocket serverSocket, out TestSocket clientSocket);
            var serverPeer = new ZNetPeer(serverSocket, false);
            var clientPeer = new ZNetPeer(clientSocket, true);
            True(server.OnConnectionStarted(serverPeer), "Claim server connection did not start.");
            True(client.OnConnectionStarted(clientPeer), "Claim client connection did not start.");
            server.OnServerHandshake(serverPeer.m_rpc);
            Pump(clientPeer.m_rpc, clientSocket);
            client.OnClientHandshake(clientPeer.m_rpc);
            client.CanSendPeerInfo(clientPeer.m_rpc, string.Empty, out _);
            Pump(serverPeer.m_rpc, serverSocket);
            True(!server.CanReceivePeerInfo(serverPeer.m_rpc, out string reason),
                "Rejected claim was admitted before RPC_PeerInfo.");
            evaluator.Dispose();
            provider?.Dispose();
            server.Dispose();
            client.Dispose();
            serverPeer.Dispose();
            clientPeer.Dispose();
            serverModule.Dispose();
            clientModule.Dispose();
            return reason;
        }

        private static ModuleRegistration RegisterRpcTestModule(RunicRegistry registry) =>
            registry.RegisterModule(new ModuleDescriptor(
                "runic.test",
                "RPC test",
                "1.0.0",
                "1.0",
                new[] { "test.rpc", RunicCapabilityIds.NetworkRpc }));

        private static RunicRpcRuntimeContext TestRuntime(
            bool server,
            TestClock clock,
            Action<ZNetPeer> disconnect,
            bool isAdmin = true)
        {
            RpcRuntimeIdentityResolver identity = delegate(
                ZNetPeer peer,
                string sessionId,
                bool localIsServer,
                out RpcPeerIdentity result)
            {
                result = localIsServer
                    ? new RpcPeerIdentity("steam", peer.m_socket.GetHostName(), RpcIdentityAssurance.BackendAccount)
                    : new RpcPeerIdentity("valheim.server", "server", RpcIdentityAssurance.ConnectionBound);
                return true;
            };
            RpcRuntimeActorResolver actor = delegate(
                ZNetPeer peer,
                long peerId,
                out ZDOID characterId,
                out long playerId,
                out string reason)
            {
                characterId = new ZDOID(peerId == 0 ? 1 : peerId, 1);
                playerId = 9001;
                reason = "actor-transport-owned";
                return true;
            };
            return new RunicRpcRuntimeContext(
                () => server,
                () => clock.UtcNowTicks,
                disconnect,
                (_, __) => { },
                identity,
                actor,
                _ => isAdmin);
        }

        private static void Pump(ZRpc rpc, TestSocket socket)
        {
            for (int iteration = 0; iteration < 32 && socket.PendingPackages > 0; iteration++)
                rpc.Update(0.01f);
            Equal(0, socket.PendingPackages);
        }

        private sealed class RpcPairHarness : IDisposable
        {
            internal const long ClientUid = 101;
            internal const long ServerUid = 202;
            internal static readonly RpcEndpointDescriptor Endpoint = new RpcEndpointDescriptor(
                "runic.test",
                "runic.test.mutate",
                "test.rpc",
                1,
                RpcEndpointDirection.ClientToServer,
                RpcOperationKind.Mutation,
                RpcReplayDurability.SessionOnly,
                RpcIdentityAssurance.ConnectionBound,
                1024);

            internal static RpcEndpointDescriptor EndpointWith(
                RpcReplayDurability durability) =>
                new RpcEndpointDescriptor(
                    "runic.test",
                    "runic.test.mutate",
                    "test.rpc",
                    1,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.Mutation,
                    durability,
                    RpcIdentityAssurance.ConnectionBound,
                    1024);

            private readonly TestSocket _serverSocket;
            private readonly TestSocket _clientSocket;
            private readonly ZNetPeer _serverPeer;
            private readonly ZNetPeer _clientPeer;
            private readonly TestClock _clock;

            internal RpcPairHarness(bool bindingProfile = false, bool serverAdmin = true)
            {
                var serverRegistry = new RunicRegistry();
                var clientRegistry = new RunicRegistry();
                ServerModule = bindingProfile
                    ? RegisterBindingTestModule(serverRegistry)
                    : RegisterRpcTestModule(serverRegistry);
                ClientModule = bindingProfile
                    ? RegisterBindingTestModule(clientRegistry)
                    : RegisterRpcTestModule(clientRegistry);
                _clock = new TestClock();
                Server = new RunicRpcService(
                    new ManualLogSource("rpc-pair-server"),
                    serverRegistry,
                    TestRuntime(true, _clock, peer => peer.Dispose(), serverAdmin));
                Client = new RunicRpcService(
                    new ManualLogSource("rpc-pair-client"),
                    clientRegistry,
                    TestRuntime(false, _clock, peer => peer.Dispose()));
                TestSocket.CreatePair("76561198000000001", "server-account", out _serverSocket, out _clientSocket);
                _serverPeer = new ZNetPeer(_serverSocket, false) { m_uid = ClientUid };
                _clientPeer = new ZNetPeer(_clientSocket, true) { m_uid = ServerUid };
                True(Server.OnConnectionStarted(_serverPeer), "Server direct handler was not registered.");
                True(Client.OnConnectionStarted(_clientPeer), "Client direct handler was not registered.");

                Server.OnServerHandshake(_serverPeer.m_rpc);
                // Stock 0.221.12 RPC_ServerHandshake calls ClearPlayerData, which calls
                // ZRoutedRpc.RemovePeer before the direct compatibility offer is admitted.
                Server.OnRoutedPeerRemoving(_serverPeer);
                Pump(_clientPeer.m_rpc, _clientSocket);
                Client.OnClientHandshake(_clientPeer.m_rpc);
                True(Client.CanSendPeerInfo(_clientPeer.m_rpc, string.Empty, out _),
                    "Client PeerInfo was not released after its offer.");
                Pump(_serverPeer.m_rpc, _serverSocket);
                True(Server.CanReceivePeerInfo(_serverPeer.m_rpc, out string reason),
                    "Server rejected compatible direct offer: " + reason);
                Pump(_clientPeer.m_rpc, _clientSocket);
                Server.OnPeerAdmitted(_serverPeer);
                Client.OnPeerAdmitted(_clientPeer);
                True(Client.IsServerConnectionReady, "Client server session is not ready.");
                True(Server.TryGetPeer(ClientUid, out RpcPeerSnapshot ready) && ready.Ready,
                    "Server client session is not ready.");
            }

            internal RunicRpcService Server { get; }
            internal RunicRpcService Client { get; }
            internal ModuleRegistration ServerModule { get; }
            internal ModuleRegistration ClientModule { get; }

            internal void AdvanceMilliseconds(int milliseconds) =>
                _clock.AdvanceMilliseconds(milliseconds);

            internal void PumpRoundTrip()
            {
                Pump(_serverPeer.m_rpc, _serverSocket);
                Pump(_clientPeer.m_rpc, _clientSocket);
            }

            internal void ReplaceNextServerWireText(string expected, string replacement) =>
                _serverSocket.ReplaceOnlyQueuedAscii(expected, replacement);

            internal void PumpServerOnly() => Pump(_serverPeer.m_rpc, _serverSocket);

            public void Dispose()
            {
                Server.OnConnectionClosed(_serverPeer);
                Client.OnConnectionClosed(_clientPeer);
                Server.Dispose();
                Client.Dispose();
                _serverPeer.Dispose();
                _clientPeer.Dispose();
                ServerModule.Dispose();
                ClientModule.Dispose();
            }
        }

        private static ModuleRegistration RegisterBindingTestModule(RunicRegistry registry) =>
            registry.RegisterModule(new ModuleDescriptor(
                Plugin.ModuleId,
                Plugin.PluginName,
                Plugin.PluginVersion,
                Plugin.ProtocolVersion,
                new[]
                {
                    RunicCapabilityIds.NetworkRpc,
                    RunicCapabilityIds.ActorIdentityBinding
                }));

        private sealed class TestClock
        {
            internal long UtcNowTicks { get; private set; } = DateTime.UtcNow.Ticks;
            internal void AdvanceMilliseconds(int milliseconds) =>
                UtcNowTicks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
        }

        private sealed class TestSocket : ISocket
        {
            private const int MaximumQueuedPackages = 256;
            private const int MaximumQueuedBytes = 2 * 1024 * 1024;
            private readonly object _gate = new object();
            private readonly Queue<ZPackage> _incoming = new Queue<ZPackage>();
            private readonly string _hostName;
            private TestSocket _remote;
            private bool _connected = true;
            private int _queuedBytes;
            private int _sent;
            private int _received;

            internal TestSocket(string hostName) { _hostName = hostName; }

            internal static void CreatePair(
                string firstName,
                string secondName,
                out TestSocket first,
                out TestSocket second)
            {
                first = new TestSocket(firstName);
                second = new TestSocket(secondName);
                first._remote = second;
                second._remote = first;
            }

            internal int PendingPackages
            {
                get { lock (_gate) return _incoming.Count; }
            }

            internal void ReplaceOnlyQueuedAscii(string expected, string replacement)
            {
                if (expected == null || replacement == null || expected.Length != replacement.Length)
                    throw new ArgumentException("Wire replacements must have equal, non-null lengths.");
                byte[] source = Encoding.ASCII.GetBytes(expected);
                byte[] target = Encoding.ASCII.GetBytes(replacement);
                lock (_gate)
                {
                    if (_incoming.Count != 1)
                        throw new InvalidOperationException("Expected exactly one queued wire package.");
                    ZPackage package = _incoming.Dequeue();
                    byte[] bytes = package.GetArray();
                    int match = -1;
                    for (int offset = 0; offset <= bytes.Length - source.Length; offset++)
                    {
                        bool equal = true;
                        for (int index = 0; index < source.Length; index++)
                        {
                            if (bytes[offset + index] == source[index]) continue;
                            equal = false;
                            break;
                        }
                        if (!equal) continue;
                        if (match >= 0)
                            throw new InvalidOperationException("Wire text was not unique in the queued package.");
                        match = offset;
                    }
                    if (match < 0) throw new InvalidOperationException("Wire text was not found.");
                    for (int index = 0; index < target.Length; index++)
                        bytes[match + index] = target[index];
                    _incoming.Enqueue(new ZPackage(bytes));
                }
            }

            public bool IsConnected() => _connected;

            public void Send(ZPackage package)
            {
                if (!_connected || package == null || _remote == null || !_remote._connected)
                    throw new InvalidOperationException("Test transport is closed.");
                byte[] bytes = package.GetArray();
                lock (_remote._gate)
                {
                    if (_remote._incoming.Count >= MaximumQueuedPackages ||
                        _remote._queuedBytes > MaximumQueuedBytes - bytes.Length)
                        throw new InvalidOperationException("Test transport queue is full.");
                    _remote._incoming.Enqueue(new ZPackage((byte[])bytes.Clone()));
                    _remote._queuedBytes += bytes.Length;
                }
                _sent += bytes.Length;
            }

            public ZPackage Recv()
            {
                lock (_gate)
                {
                    if (_incoming.Count == 0) return null;
                    ZPackage package = _incoming.Dequeue();
                    int size = package.Size();
                    _queuedBytes -= size;
                    _received += size;
                    return package;
                }
            }

            public int GetSendQueueSize() => _remote == null ? 0 : _remote._queuedBytes;
            public int GetCurrentSendRate() => 0;
            public bool IsHost() => false;
            public void Dispose() => Close();
            public bool GotNewData() => PendingPackages > 0;
            public void Close() => _connected = false;
            public string GetEndPointString() => _hostName;
            public string GetHostName() => _hostName;
            public ISocket Accept() => null;
            public int GetHostPort() => -1;
            public bool Flush() => true;
            public void VersionMatch() { }

            public void GetAndResetStats(out int totalSent, out int totalRecv)
            {
                totalSent = _sent;
                totalRecv = _received;
                _sent = 0;
                _received = 0;
            }

            public void GetConnectionQuality(
                out float localQuality,
                out float remoteQuality,
                out int ping,
                out float outByteSec,
                out float inByteSec)
            {
                localQuality = 1f;
                remoteQuality = 1f;
                ping = 0;
                outByteSec = 0f;
                inByteSec = 0f;
            }
        }

        private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

        private static string Text(byte[] value) => Encoding.UTF8.GetString(value);

        private static void Run(string name, Action test)
        {
            test();
            _passed++;
            System.Console.WriteLine($"PASS {name}");
        }

        private static void True(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
        }

        private sealed class RecordingBackup : IMigrationBackupSink
        {
            public void CreateBackup(string moduleId, RecordSetSnapshot snapshot)
            {
                if (string.IsNullOrWhiteSpace(moduleId) || snapshot == null)
                {
                    throw new InvalidOperationException("Backup input is invalid.");
                }
            }
        }

        private sealed class TestPlayerBindingStorage : IPlayerIdentityBindingStorage
        {
            private byte[] _bytes;
            internal int WriteAttempts { get; private set; }

            public bool TryRead(string worldScope, out bool exists, out byte[] bytes, out string reasonCode)
            {
                exists = _bytes != null;
                bytes = exists ? (byte[])_bytes.Clone() : Array.Empty<byte>();
                reasonCode = exists ? "binding-file-read" : "binding-file-missing";
                return true;
            }

            public bool TryWriteAtomic(string worldScope, byte[] bytes, out string reasonCode)
            {
                WriteAttempts++;
                _bytes = (byte[])bytes.Clone();
                reasonCode = "binding-file-committed";
                return true;
            }
        }

        private sealed class BarrierRecordStore : IRecordStore
        {
            private readonly object _gate = new object();
            private readonly Barrier _writers = new Barrier(2);
            private RecordSetSnapshot _snapshot;

            internal BarrierRecordStore(string moduleId, RecordSetSnapshot snapshot)
            {
                ModuleId = moduleId;
                _snapshot = snapshot.Clone();
            }

            public string ModuleId { get; }

            public RecordSetSnapshot ReadSnapshot()
            {
                lock (_gate) return _snapshot.Clone();
            }

            public bool TryReplace(RecordSetSnapshot expected, RecordSetSnapshot replacement)
            {
                if (!_writers.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Concurrent migration did not reach the CAS barrier.");
                lock (_gate)
                {
                    if (!SnapshotsEqual(_snapshot, expected)) return false;
                    _snapshot = replacement.Clone();
                    return true;
                }
            }

            private static bool SnapshotsEqual(RecordSetSnapshot left, RecordSetSnapshot right)
            {
                if (left.SchemaVersion != right.SchemaVersion || left.Records.Count != right.Records.Count)
                    return false;
                foreach (KeyValuePair<string, byte[]> pair in left.Records)
                {
                    if (!right.Records.TryGetValue(pair.Key, out byte[] other) ||
                        !pair.Value.SequenceEqual(other))
                        return false;
                }
                return true;
            }
        }
    }
}
