using System;
using System.IO;
using System.Text;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupRuntimeContractTests
    {
        internal static void Register()
        {
            TestRunner.Run(
                "remote Group actor requires AccountBoundPlayer before world identity",
                RemoteActorBinding);
            TestRunner.Run(
                "Group RPC never uses routed sender identity",
                NoRoutedSenderTrust);
            TestRunner.Run(
                "Group targets use canonical Valheim player identities",
                WorldPlayerTargetsOnly);
            TestRunner.Run(
                "Group mutations use issued revisions and durable receipts",
                DurableCommandBoundary);
            TestRunner.Run(
                "Group endpoint protocol matches the advertised Permissions module",
                ProtocolMatchesModuleHello);
            TestRunner.Run(
                "Group list and show responses use the bounded 64KiB query wire cap",
                BoundedLongQueryResponses);
            TestRunner.Run(
                "malformed Group queries fail before catalog access",
                MalformedQueriesFailBeforeCatalogAccess);
            TestRunner.Run(
                "Group query responses enforce the exact maximum wire bound",
                QueryResponseMaximumIsEnforced);
            TestRunner.Run(
                "native Chat is a private Group command surface",
                NativeChatCommandSurface);
            TestRunner.Run(
                "friendly player names resolve only after exact connected actor proof",
                FriendlyTargetResolutionIsBound);
            TestRunner.Run(
                "active Group selection is refreshed to the client and revalidated on server",
                ActiveSelectionBoundary);
        }

        private static void RemoteActorBinding()
        {
            string source = Source();
            int backend = source.IndexOf(
                "identity.Assurance != RpcIdentityAssurance.BackendAccount",
                StringComparison.Ordinal);
            int resolve = source.IndexOf("_rpc.TryResolveActor(", StringComparison.Ordinal);
            int assurance = source.IndexOf(
                "RpcActorAssurance.AccountBoundPlayer",
                resolve,
                StringComparison.Ordinal);
            int verified = source.IndexOf(
                "verified.HasVerifiedPlayerBinding",
                assurance,
                StringComparison.Ordinal);
            int worldIdentity = source.IndexOf(
                "\"valheim.player\"",
                verified,
                StringComparison.Ordinal);
            TestAssert.True(backend >= 0 && resolve > backend && assurance > resolve &&
                            verified > assurance && worldIdentity > verified,
                "The direct backend session must be resolved to an exact persisted player binding " +
                "before creating the portable Group identity.");
            TestAssert.False(source.Contains(
                "StableIdentity.TryCreate(identity.Authority, identity.SubjectId, out actor)",
                StringComparison.Ordinal));
        }

        private static void NoRoutedSenderTrust()
        {
            string source = Source();
            TestAssert.False(source.Contains("ZRoutedRpc", StringComparison.Ordinal));
            TestAssert.False(source.Contains("InvokeRoutedRPC", StringComparison.Ordinal));
            TestAssert.True(source.Contains("request.IsConnectionCurrent", StringComparison.Ordinal));
            TestAssert.True(source.Contains("request.Peer", StringComparison.Ordinal));
        }

        private static void WorldPlayerTargetsOnly()
        {
            string source = Source();
            TestAssert.True(source.Contains(
                "group-target-player-identity-required", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "TryWorldPlayerIdentity(args[3], args[4]", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "identity.Authority, \"valheim.player\"", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "long.TryParse(identity.SubjectId, NumberStyles.None", StringComparison.Ordinal));
        }

        private static void DurableCommandBoundary()
        {
            string source = Source();
            TestAssert.True(source.Contains("PrepareEndpointId", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "RpcReplayDurability.HandlerDurable", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "GroupCommandDurableCodec.TryDecodeExecution", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "request.IdempotencyKey, execution.Token", StringComparison.Ordinal));
            TestAssert.True(source.Contains(
                "_store.TryExecuteIssuedCommand", StringComparison.Ordinal));
            TestAssert.False(source.Contains(
                "ComputePayloadKey(payload)", StringComparison.Ordinal));
        }

        private static void ProtocolMatchesModuleHello()
        {
            TestAssert.Equal(
                RunicPermissions.Contracts.PermissionCapabilities.ProtocolMajor,
                RunicPermissions.Groups.GroupCapabilities.ProtocolVersion);
        }

        private static void BoundedLongQueryResponses()
        {
            string id = Guid.Parse("b03b1a34-d2ed-4a93-9480-9f9560d4cf88").ToString("N");
            string list = id + " | Runic Portal Harness | owner | revision=1";
            string show = id + " | Runic Portal Harness | revision=1\n" +
                          "member valheim.player:2664509785 | owner";
            byte[] listBytes = GroupQueryProtocol.EncodeResponse(list);
            byte[] showBytes = GroupQueryProtocol.EncodeResponse(show);
            TestAssert.True(listBytes.Length > 32 && showBytes.Length > 32,
                "A legitimate list/show response must exercise the former 32-byte failure.");
            TestAssert.Equal(list, Encoding.UTF8.GetString(listBytes));
            TestAssert.Equal(show, Encoding.UTF8.GetString(showBytes));

            string source = Source();
            int cap = source.IndexOf(
                "GroupQueryProtocol.MaximumWireBytes", StringComparison.Ordinal);
            int handler = source.IndexOf("HandleQuery));", cap, StringComparison.Ordinal);
            TestAssert.True(cap >= 0 && handler > cap,
                "The direct RPC descriptor must admit the same bounded response produced by the query codec.");
        }

        private static void MalformedQueriesFailBeforeCatalogAccess()
        {
            foreach (byte[] payload in new[]
                     {
                         null,
                         Array.Empty<byte>(),
                         new byte[2],
                         new byte[16],
                         new byte[18],
                         new byte[GroupQueryProtocol.MaximumWireBytes],
                         new byte[GroupQueryProtocol.MaximumWireBytes + 1],
                         new byte[] { 0 },
                         new byte[] { 2 },
                         new byte[17]
                     })
                TestAssert.False(GroupQueryProtocol.TryDecodeRequest(
                    payload, out _, out _, out _));

            byte[] validShow = new byte[17];
            validShow[0] = 2;
            Buffer.BlockCopy(Guid.NewGuid().ToByteArray(), 0, validShow, 1, 16);
            TestAssert.True(GroupQueryProtocol.TryDecodeRequest(
                validShow, out byte kind, out Guid id, out string reason));
            TestAssert.Equal((byte)2, kind);
            TestAssert.True(id != Guid.Empty);
            TestAssert.Equal("ok", reason);

            string source = Source();
            int decode = source.IndexOf("GroupQueryProtocol.TryDecodeRequest(", StringComparison.Ordinal);
            int scope = source.IndexOf("string scope = CurrentWorldScope();", StringComparison.Ordinal);
            int store = source.IndexOf("_store.Read(scope)", StringComparison.Ordinal);
            TestAssert.True(decode >= 0 && scope > decode && store > scope,
                "Request shape and kind must be rejected before world scope or catalog access.");
        }

        private static void QueryResponseMaximumIsEnforced()
        {
            byte[] exact = GroupQueryProtocol.EncodeResponse(
                new string('a', GroupQueryProtocol.MaximumWireBytes));
            TestAssert.Equal(GroupQueryProtocol.MaximumWireBytes, exact.Length);
            TestAssert.Throws<InvalidOperationException>(() =>
                GroupQueryProtocol.EncodeResponse(
                    new string('a', GroupQueryProtocol.MaximumWireBytes + 1)));
        }

        private static void NativeChatCommandSurface()
        {
            string source = Source();
            TestAssert.True(source.Contains(
                "new Terminal.ConsoleCommand(\n                \"group\"", StringComparison.Ordinal));
            TestAssert.True(source.Contains("ReferenceEquals(args.Context, Chat.instance)",
                StringComparison.Ordinal));
            TestAssert.True(source.Contains("args.Context.AddString", StringComparison.Ordinal));
            TestAssert.False(source.Contains(
                "Runic Group: use the exact local F5 console.", StringComparison.Ordinal));
        }

        private static void FriendlyTargetResolutionIsBound()
        {
            string source = Source();
            int peers = source.IndexOf("_rpc.GetPeers()", StringComparison.Ordinal);
            int account = source.IndexOf(
                "RpcActorAssurance.AccountBoundPlayer", peers, StringComparison.Ordinal);
            int verified = source.IndexOf("actor.HasVerifiedPlayerBinding", account,
                StringComparison.Ordinal);
            int character = source.IndexOf(
                "networkPeer.m_characterID != actor.CharacterId", verified,
                StringComparison.Ordinal);
            int name = source.IndexOf("networkPeer.m_playerName", character,
                StringComparison.Ordinal);
            TestAssert.True(peers >= 0 && account > peers && verified > account &&
                            character > verified && name > character,
                "A display name must be presentation over an exact current account-bound player.");
            TestAssert.True(source.Contains(
                "More than one connected player has that name; no player was chosen.",
                StringComparison.Ordinal));
        }

        private static void ActiveSelectionBoundary()
        {
            string source = Source();
            TestAssert.True(source.Contains("RequestActiveRefresh()", StringComparison.Ordinal));
            TestAssert.True(source.Contains("_activeGroups.SetClientCache", StringComparison.Ordinal));
            TestAssert.True(source.Contains("_activeGroups.TrySetAuthoritative", StringComparison.Ordinal));
            TestAssert.True(source.Contains("TryActiveRecord(actor, catalog, active",
                StringComparison.Ordinal));
        }

        private static string Source()
        {
            foreach (string seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(seed);
                while (directory != null)
                {
                    foreach (string relative in new[]
                             {
                                 Path.Combine("RunicPermissions", "Integration", "GroupRuntime.cs"),
                                 Path.Combine("Integration", "GroupRuntime.cs")
                             })
                    {
                        string candidate = Path.Combine(directory.FullName, relative);
                        if (File.Exists(candidate))
                        {
                            string source = File.ReadAllText(candidate);
                            string chat = Path.Combine(
                                Path.GetDirectoryName(candidate) ?? string.Empty,
                                "GroupChatRuntime.cs");
                            return File.Exists(chat)
                                ? source + "\n" + File.ReadAllText(chat)
                                : source;
                        }
                    }
                    directory = directory.Parent;
                }
            }
            throw new FileNotFoundException("Could not locate RunicPermissions/Integration/GroupRuntime.cs.");
        }
    }
}
