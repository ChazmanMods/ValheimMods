using System;
using System.Collections.Generic;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicTransactions.Valheim;

namespace RunicTransactions.Tests
{
    internal static class OwnershipReturnRpcTests
    {
        internal static void ReturnRequestCacheIsRateLimitedPrunedAndBounded()
        {
            int[] profiles = { 100, 1_000, 10_000 };
            foreach (int size in profiles)
            {
                var cache = new BoundedReturnRequestCache();
                for (int index = 0; index < size; index++)
                    TestAssert.True(cache.TryAdmit("container-" + index, 10d, 1d),
                        "Unique request was rejected at profile " + size + ".");
                TestAssert.True(cache.Count <= BoundedReturnRequestCache.MaximumEntries,
                    "Return-request cache exceeded its hard capacity.");
                TestAssert.Equal(
                    Math.Min(size, BoundedReturnRequestCache.MaximumEntries),
                    cache.Count,
                    "Return-request cache profile count mismatch.");
                TestAssert.True(cache.Contains("container-" + (size - 1)),
                    "Deterministic newest entry was evicted.");
                if (size > BoundedReturnRequestCache.MaximumEntries)
                    TestAssert.False(cache.Contains("container-0"),
                        "Deterministic oldest entry survived capacity eviction.");
                TestAssert.Equal(cache.Count, cache.Prune(11d, 1d),
                    "Expired retry entries were not pruned.");
                TestAssert.Equal(0, cache.Count, "Prune left expired entries.");
            }

            var retry = new BoundedReturnRequestCache();
            TestAssert.True(retry.TryAdmit("same", 20d, 1d), "Initial request was rejected.");
            TestAssert.False(retry.TryAdmit("same", 20.999d, 1d),
                "Retry-window request was admitted.");
            TestAssert.True(retry.TryAdmit("same", 21d, 1d),
                "Expired retry-window request was not admitted.");
            TestAssert.False(retry.TryAdmit("same", 19d, 1d),
                "Backward clock movement bypassed the retry gate.");
        }

        internal static void ClientAcceptsOnlyExactCurrentDirectServerSession()
        {
            RpcPeerSnapshot server = Peer(
                201,
                "server-session-a",
                new RpcPeerIdentity(
                    "valheim.server",
                    "session-server-session-a",
                    RpcIdentityAssurance.ConnectionBound));
            var rpc = new FakeClientRpc(server);
            TestAssert.True(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(server, () => true),
                    rpc),
                "Exact direct server session was rejected.");

            RpcPeerSnapshot stale = Peer(
                server.PeerId,
                "server-session-b",
                server.Identity);
            TestAssert.False(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(stale, () => true),
                    rpc),
                "Stale/reused server session was accepted.");
            TestAssert.False(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(server, () => false),
                    rpc),
                "Disconnected request context was accepted.");
            rpc.ServerReady = false;
            TestAssert.False(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(server, () => true),
                    rpc),
                "Unready server connection was accepted.");
            rpc.ServerReady = true;

            RpcPeerSnapshot claimed = Peer(
                server.PeerId,
                server.SessionId,
                new RpcPeerIdentity(
                    "steam",
                    "76561198000000001",
                    RpcIdentityAssurance.BackendAccount));
            TestAssert.False(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(claimed, () => true),
                    rpc),
                "Client-claimed backend identity was accepted as the server.");
            rpc.IsServerValue = true;
            TestAssert.False(DurableContainerSafety.IsExactCurrentServerRequest(
                    Request(server, () => true),
                    rpc),
                "Server receiver accepted a server-to-client request.");
        }

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

        private static RpcRequestContext Request(
            RpcPeerSnapshot peer,
            Func<bool> current) =>
            new RpcRequestContext(
                peer,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    DurableContainerSafety.ReturnRequestEndpoint,
                    RunicCapabilityIds.ContainerOwnershipReturn,
                    1,
                    RpcEndpointDirection.ServerToClient,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.ConnectionBound,
                    64),
                "correlation",
                "idempotency",
                Array.Empty<byte>(),
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.AddSeconds(3).Ticks,
                false,
                current);

        private sealed class FakeClientRpc : IRunicRpcService
        {
            private readonly RpcPeerSnapshot _server;

            internal FakeClientRpc(RpcPeerSnapshot server)
            {
                _server = server;
            }

            internal bool IsServerValue { get; set; }
            internal bool ServerReady { get; set; } = true;
            public bool IsServer => IsServerValue;
            public bool IsServerConnectionReady => ServerReady;
            public event EventHandler<RpcPeerEventArgs> PeerReady { add { } remove { } }
            public event EventHandler<RpcPeerEventArgs> PeerDisconnected { add { } remove { } }
            public event EventHandler<RpcAdmissionRejectedEventArgs> AdmissionRejected { add { } remove { } }
            public IDisposable RegisterEndpoint(ModuleRegistration module, RpcEndpointDescriptor descriptor, RunicRpcHandler handler) => throw new NotSupportedException();
            public IDisposable RegisterPeerRequirement(ModuleRegistration module, RpcPeerRequirement requirement) => throw new NotSupportedException();
            public IDisposable RegisterPlayerBindingResolver(ModuleRegistration module, IRpcPlayerBindingResolver resolver) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimProvider(ModuleRegistration module, RunicHandshakeClaimProvider provider) => throw new NotSupportedException();
            public IDisposable RegisterHandshakeClaimEvaluator(ModuleRegistration module, string evaluatorId, RunicHandshakeClaimEvaluator evaluator) => throw new NotSupportedException();
            public RpcSendResult SendToServer(ModuleRegistration module, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public RpcSendResult SendToPeer(ModuleRegistration module, RpcPeerSnapshot expectedPeer, string endpointId, byte[] payload, string idempotencyKey, TimeSpan timeout, RunicRpcCompletion completion) => throw new NotSupportedException();
            public bool TryGetPeer(long peerId, out RpcPeerSnapshot peer)
            {
                peer = peerId == _server.PeerId ? _server : null;
                return peer != null;
            }
            public bool TryResolveActor(RpcPeerSnapshot expectedPeer, RpcActorAssurance minimumAssurance, out RpcActorSnapshot actor, out string reasonCode)
            {
                actor = null;
                reasonCode = "not-supported";
                return false;
            }
            public IReadOnlyList<RpcPeerSnapshot> GetPeers() => new[] { _server };
            public bool TryDisconnectPeer(long peerId, string reasonCode) => false;
        }
    }
}
