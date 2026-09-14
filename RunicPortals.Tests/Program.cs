using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;
using RunicPortals.Api;
using RunicPortals.Core;
using RunicPortals.Integration;

namespace RunicPortals.Tests
{
    internal static partial class Program
    {
        private sealed class AllowAll : IPortalAccessEvaluator
        {
            public bool Allows(
                PortalEndpoint endpoint,
                string travelerStableId,
                PortalAccessAction action) => true;
        }

        private sealed class GroupMemberships : IPortalGroupMembershipResolver
        {
            private readonly HashSet<string> _members = new HashSet<string>(StringComparer.Ordinal);

            internal bool Available { get; set; } = true;

            internal void Add(string groupId, long playerId) =>
                _members.Add(groupId + ":" + playerId);

            public bool TryIsMember(string groupId, long playerId, out bool isMember)
            {
                isMember = Available && _members.Contains(groupId + ":" + playerId);
                return Available;
            }
        }

        private static void VersionGateAcceptsOnlyAuditedBuilds()
        {
            if (!ValheimContracts.IsSupportedVersion("1.0.7") || !ValheimContracts.IsSupportedVersion("1.0.12"))
                throw new Exception("Audited version rejected.");
            foreach (string unsupported in new[] { "1.0.8", "1.0.13", "l-1.0.12", "1.0.120", "", null })
                if (ValheimContracts.IsSupportedVersion(unsupported))
                    throw new Exception("Unsupported version accepted.");
        }

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("InvitationSnapshotIsPrivateAndExpires", InvitationSnapshotIsPrivateAndExpires),
                ("InvitationSnapshotCodecIsBounded", InvitationSnapshotCodecIsBounded),
                ("InvitationResponsesPreserveLegacyCommands", InvitationResponsesPreserveLegacyCommands),
                ("DeclinePersistsOnlyInviteesOwnInvitation", DeclinePersistsOnlyInviteesOwnInvitation),
                ("StalePopupCannotAcceptReplacementInvitation", StalePopupCannotAcceptReplacementInvitation),
                ("NativeInvitationDialogContractsAndInputProtection", NativeInvitationDialogContractsAndInputProtection),
                ("VersionGateAcceptsOnlyAuditedBuilds", VersionGateAcceptsOnlyAuditedBuilds),
                ("ParserKeepsPublicPrivateAndGroupFormats", ParserKeepsPublicPrivateAndGroupFormats),
                ("InstalledValheimContractsInitialize", InstalledValheimContractsInitialize),
                ("InstalledMapMutationHooksExist", InstalledMapMutationHooksExist),
                ("TypedStandardEditorCommandsKeepVanillaTagsSeparate", TypedStandardEditorCommandsKeepVanillaTagsSeparate),
                ("TypedNetworkEditorCommandsCoverPoliciesAndDirections", TypedNetworkEditorCommandsCoverPoliciesAndDirections),
                ("TypedEditorCommandsNormalizeAndRejectUnsafeFields", TypedEditorCommandsNormalizeAndRejectUnsafeFields),
                ("EditorDraftPrefillsAndRoundTrips", EditorDraftPrefillsAndRoundTrips),
                ("EditorPanelUsesTypedCommandsOnly", EditorPanelUsesTypedCommandsOnly),
                ("GroupChoicesRoundTripVersionedResponse", GroupChoicesRoundTripVersionedResponse),
                ("GroupEditorAuthorizationUsesCurrentMembership", GroupEditorAuthorizationUsesCurrentMembership),
                ("PortalMetadataKeysRemainStable", PortalMetadataKeysRemainStable),
                ("GroupCatalogCodecRoundTrips", GroupCatalogCodecRoundTrips),
                ("SignedCharacterIdentitiesAreCanonical", SignedCharacterIdentitiesAreCanonical),
                ("SignedCharactersCreateInviteAcceptAndPersist", SignedCharactersCreateInviteAcceptAndPersist),
                ("SignedGroupMembersKeepPortalPermissionBoundaries", SignedGroupMembersKeepPortalPermissionBoundaries),
                ("GroupIdentityFailureRepliesWithoutMembershipDisclosure", GroupIdentityFailureRepliesWithoutMembershipDisclosure),
                ("CompatibleGroupStoreUsesExistingFormat", CompatibleGroupStoreUsesExistingFormat),
                ("DirectoryStaysInsideExactNetwork", DirectoryStaysInsideExactNetwork),
                ("RouteRejectsCrossNetwork", RouteRejectsCrossNetwork),
                ("OneWayRequiresAcknowledgement", OneWayRequiresAcknowledgement),
                ("PublicPolicyDefaultsToTravelAndOwnerEdit", PublicPolicyDefaultsToTravelAndOwnerEdit),
                ("PrivatePolicyRequiresRecordedOwner", PrivatePolicyRequiresRecordedOwner),
                ("GroupPolicyUsesCurrentAuthenticatedMembership", GroupPolicyUsesCurrentAuthenticatedMembership),
                ("WardEvidenceIsIndependentFromPortalPolicy", WardEvidenceIsIndependentFromPortalPolicy),
                ("RemoteDestinationWardPolicyIsConsistent", RemoteDestinationWardPolicyIsConsistent),
                ("AuthorityUsesNativeLocalEvidence", AuthorityUsesNativeLocalEvidence),
                ("AuthorityRequiresSourceOwnershipForEdit", AuthorityRequiresSourceOwnershipForEdit),
                ("ConfirmationIsBoundedRepeat", ConfirmationIsBoundedRepeat),
                ("SelectionStoreEvictsOldest", SelectionStoreEvictsOldest),
                ("MapOverlayRejectsDuplicateIds", MapOverlayRejectsDuplicateIds),
                ("ArrivalSuppressionExpires", ArrivalSuppressionExpires),
                ("GroupApiIsPublicAndOptional", GroupApiIsPublicAndOptional),
                ("ManifestHasOnlyBepInEx", ManifestHasOnlyBepInEx),
                ("ProjectHasNoRuntimeFoundationReference", ProjectHasNoRuntimeFoundationReference),
                ("ProductionHasNoDurableOrRegistryLayer", ProductionHasNoDurableOrRegistryLayer),
                ("GroupRpcIsBoundedAndPeerAuthenticated", GroupRpcIsBoundedAndPeerAuthenticated),
                ("DirectorySyncWarmsRemotePortalsOnFirstUse", DirectorySyncWarmsRemotePortalsOnFirstUse),
                ("LegacyDirectoryProtocolGetsCompatibilityRejection", LegacyDirectoryProtocolGetsCompatibilityRejection),
                ("NormalMapDirectoryWarmsFromAuthoritativeServer", NormalMapDirectoryWarmsFromAuthoritativeServer),
                ("PortalMutationsNeverClaimOwnership", PortalMutationsNeverClaimOwnership)
            };
            int passed = 0;
            foreach ((string name, Action run) in tests)
            {
                try
                {
                    run();
                    passed++;
                    System.Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    System.Console.WriteLine("FAIL " + name + ": " + exception.Message);
                    return 1;
                }
            }
            System.Console.WriteLine(passed + "/" + tests.Length + " tests passed.");
            return 0;
        }

        private static void InstalledValheimContractsInitialize()
        {
            True(ValheimContracts.Initialize(out string problem), problem);
        }

        private static void InstalledMapMutationHooksExist()
        {
            const BindingFlags instance = BindingFlags.Instance |
                                          BindingFlags.Public |
                                          BindingFlags.NonPublic;
            True(typeof(Minimap).GetMethod(
                "RemovePinUnderPointer", instance, null, Type.EmptyTypes, null) != null);
            True(typeof(Minimap).GetMethod(
                "OnMapLeftClick", instance, null, Type.EmptyTypes, null) != null);
            True(typeof(Minimap).GetMethod(
                "OnMapDblClick", instance, null, Type.EmptyTypes, null) != null);
            True(typeof(Minimap).GetMethod(
                "OnMapMiddleClick", instance, null,
                new[] { typeof(UIInputHandler) }, null) != null);
        }

        private static void ParserKeepsPublicPrivateAndGroupFormats()
        {
            Equal(PortalEditKind.PublicNetwork,
                PortalEditCommand.Parse("network|public|home|north|both").Kind);
            Equal(PortalNetworkKind.Personal,
                PortalEditCommand.Parse("network|private|home|north|arrive").NetworkKind);
            PortalEditCommand group = PortalEditCommand.Parse(
                "network|group|home|north|depart").BindGroup(Guid.NewGuid().ToString("N"));
            Equal(PortalNetworkKind.Group, group.NetworkKind);
            True(GroupIdentity.IsCanonicalId(group.GroupId));
        }

        private static void TypedStandardEditorCommandsKeepVanillaTagsSeparate()
        {
            PortalEditCommand empty = PortalEditCommand.CreateStandard(string.Empty);
            Equal(PortalEditKind.StandardPair, empty.Kind);
            True(empty.HasVanillaTag);
            Equal(string.Empty, empty.VanillaTag);
            Equal(string.Empty, empty.NetworkId);
            Equal(string.Empty, empty.DisplayName);

            string maximum = new string('v', PortalEditCommand.MaximumVanillaTagLength);
            PortalEditCommand bounded = PortalEditCommand.CreateStandard(maximum);
            Equal(PortalEditKind.StandardPair, bounded.Kind);
            True(bounded.HasVanillaTag);
            Equal(maximum, bounded.VanillaTag);

            PortalEditCommand tooLong = PortalEditCommand.CreateStandard(maximum + "x");
            Equal(PortalEditKind.Invalid, tooLong.Kind);
            False(tooLong.HasVanillaTag);
            Equal(string.Empty, tooLong.VanillaTag);

            PortalEditCommand legacy = PortalEditCommand.Parse("standard");
            Equal(PortalEditKind.StandardPair, legacy.Kind);
            False(legacy.HasVanillaTag);
            Equal(string.Empty, legacy.VanillaTag);
        }

        private static void TypedNetworkEditorCommandsCoverPoliciesAndDirections()
        {
            string groupId = Guid.NewGuid().ToString("N");
            var policies = new[]
            {
                PortalNetworkKind.Public,
                PortalNetworkKind.Personal,
                PortalNetworkKind.Group
            };
            var directions = new[]
            {
                (Arrives: true, Departs: true),
                (Arrives: true, Departs: false),
                (Arrives: false, Departs: true)
            };

            foreach (PortalNetworkKind policy in policies)
            foreach ((bool arrives, bool departs) in directions)
            {
                string selectedGroup = policy == PortalNetworkKind.Group ? groupId : string.Empty;
                PortalEditCommand command = PortalEditCommand.CreateNetwork(
                    "RealmCase",
                    "NorthGate",
                    policy,
                    selectedGroup,
                    arrives,
                    departs);
                Equal(PortalEditKind.PublicNetwork, command.Kind);
                Equal("RealmCase", command.NetworkId);
                Equal("NorthGate", command.DisplayName);
                Equal(policy, command.NetworkKind);
                Equal(selectedGroup, command.GroupId);
                Equal(arrives, command.AcceptsArrival);
                Equal(departs, command.PermitsDeparture);
                False(command.HasVanillaTag);
                Equal(string.Empty, command.VanillaTag);
            }
        }

        private static void TypedEditorCommandsNormalizeAndRejectUnsafeFields()
        {
            PortalEditCommand normalized = PortalEditCommand.CreateNetwork(
                "  RealmCase  ",
                "  NorthGate  ",
                PortalNetworkKind.Public,
                string.Empty,
                true,
                true);
            Equal(PortalEditKind.PublicNetwork, normalized.Kind);
            Equal("RealmCase", normalized.NetworkId);
            Equal("NorthGate", normalized.DisplayName);

            string tooLong = new string('x', PortalContractLimits.MaximumNetworkIdLength + 1);
            string malformed = new string(new[] { '\uD800' });
            foreach (PortalEditCommand invalid in new[]
                     {
                         PortalEditCommand.CreateNetwork(
                             string.Empty, "Gate", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "   ", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             tooLong, "Gate", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", tooLong, PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm\n", "Gate", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", malformed, PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm|Injected", "Gate", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "Gate|Injected", PortalNetworkKind.Public, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "Gate", PortalNetworkKind.Custom, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "Gate", PortalNetworkKind.Group, string.Empty, true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "Gate", PortalNetworkKind.Public, Guid.NewGuid().ToString("N"), true, true),
                         PortalEditCommand.CreateNetwork(
                             "Realm", "Gate", PortalNetworkKind.Public, string.Empty, false, false),
                         PortalEditCommand.CreateStandard("line\nbreak"),
                         PortalEditCommand.CreateStandard(malformed)
                     })
                Equal(PortalEditKind.Invalid, invalid.Kind);
        }

        private static void EditorDraftPrefillsAndRoundTrips()
        {
            PortalEditorDraft standard = PortalEditorDraft.ForStandard("HomeGate");
            True(standard.StandardPair);
            Equal("HomeGate", standard.VanillaTag);
            PortalEditCommand standardCommand = standard.BuildCommand();
            Equal(PortalEditKind.StandardPair, standardCommand.Kind);
            True(standardCommand.HasVanillaTag);
            Equal("HomeGate", standardCommand.VanillaTag);

            string groupId = Guid.NewGuid().ToString("N");
            var endpoint = new PortalEndpoint(
                "portal:group",
                PortalMode.Network,
                "MountainGate",
                "RealmCase",
                PortalNetworkKind.Group,
                "valheim.player:1",
                "Owner",
                PortalOnlineState.Online,
                true,
                false,
                PortalAccessProfile.ForGroup(groupId),
                7L);
            PortalEditorDraft group = PortalEditorDraft.ForNetwork("OldTag", endpoint);
            False(group.StandardPair);
            Equal("OldTag", group.VanillaTag);
            Equal("RealmCase", group.NetworkName);
            Equal("MountainGate", group.PortalName);
            Equal(PortalNetworkKind.Group, group.Access);
            Equal(groupId, group.GroupId);
            Equal(PortalEditorDirection.ArrivalsOnly, group.Direction);

            PortalEditCommand groupCommand = group.BuildCommand();
            Equal(PortalEditKind.PublicNetwork, groupCommand.Kind);
            Equal(endpoint.NetworkId, groupCommand.NetworkId);
            Equal(endpoint.DisplayName, groupCommand.DisplayName);
            Equal(endpoint.NetworkKind, groupCommand.NetworkKind);
            Equal(groupId, groupCommand.GroupId);
            Equal(endpoint.AcceptsArrival, groupCommand.AcceptsArrival);
            Equal(endpoint.PermitsDeparture, groupCommand.PermitsDeparture);
            False(groupCommand.HasVanillaTag);
        }

        private static void EditorPanelUsesTypedCommandsOnly()
        {
            string panelPath = Path.Combine(
                ProjectRoot(), "Integration", "PortalEditorPanel.cs");
            True(File.Exists(panelPath), "The typed portal editor panel source is missing.");
            string panel = File.ReadAllText(panelPath);
            Contains(panel,
                "class PortalEditorPanel",
                "_draft.BuildCommand()",
                "PortalEditCommand");
            Reject(panel, "network|");
        }

        private static void GroupChoicesRoundTripVersionedResponse()
        {
            string firstId = Guid.NewGuid().ToString("N");
            string secondId = Guid.NewGuid().ToString("N");
            var choices = new[]
            {
                new PortalGroupChoice(firstId, "Builders"),
                new PortalGroupChoice(secondId, "Raiders")
            };
            Type runtime = typeof(PortalGroupRuntime);
            MethodInfo writer = runtime.GetMethod(
                "WriteResponse",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo reader = runtime.GetMethod(
                "TryReadResponse",
                BindingFlags.NonPublic | BindingFlags.Static);
            True(writer != null && reader != null);

            string requestId = Guid.NewGuid().ToString("N");
            byte[] payload = { 1, 2, 3 };
            var package = (ZPackage)writer.Invoke(
                null,
                new object[] { requestId, true, "ok", payload, choices });
            var schema = new ZPackage(package.GetArray());
            Equal(2, schema.ReadInt());

            object[] arguments =
            {
                new ZPackage(package.GetArray()),
                string.Empty,
                false,
                string.Empty,
                null,
                null
            };
            True((bool)reader.Invoke(null, arguments));
            Equal(requestId, (string)arguments[1]);
            True((bool)arguments[2]);
            Equal("ok", (string)arguments[3]);
            True(payload.SequenceEqual((byte[])arguments[4]));
            var decoded = (PortalGroupChoice[])arguments[5];
            Equal(2, decoded.Length);
            Equal(firstId, decoded[0].GroupId);
            Equal("Builders", decoded[0].DisplayName);
            Equal(secondId, decoded[1].GroupId);
            Equal("Raiders", decoded[1].DisplayName);

            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalGroupRuntime.cs"));
            Contains(source,
                "RunicPortals.Groups.Request.v2",
                "RunicPortals.Groups.Response.v2",
                "memberships[index].GroupId",
                "memberships[index].DisplayName");
        }

        private static void GroupEditorAuthorizationUsesCurrentMembership()
        {
            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalRuntime.cs"));
            int start = source.IndexOf(
                "private bool TryAuthorizeBoundGroupEdit(",
                StringComparison.Ordinal);
            int end = source.IndexOf(
                "private void ConsumeEdit(",
                start,
                StringComparison.Ordinal);
            True(start >= 0 && end > start,
                "The bounded Group edit authorization method was not found.");
            string authorization = source.Substring(start, end - start);
            Contains(authorization,
                "_groups.TryIsMember(command.GroupId, playerId, out bool member)");
            Reject(authorization, "TryGetActive");
        }

        private static void PortalMetadataKeysRemainStable()
        {
            Equal(2, PortalZdoCodec.SchemaVersion);
            Equal("runic.portals.record", PortalZdoCodec.AuthoritativeRecordKey);
            Equal("runic.portals.group", PortalZdoCodec.GroupKey);
            Equal("runic.portals.network", PortalZdoCodec.NetworkKey);
            Equal("runic.portals.ownerIdentity", PortalZdoCodec.OwnerIdentityKey);
        }

        private static void GroupCatalogCodecRoundTrips()
        {
            const string scope = "valheim.0000000000000001";
            var owner = new StableIdentity("valheim.player", "123");
            GroupMutationResult created = GroupCatalog.Empty.Create(
                0L, Guid.NewGuid(), "Builders", owner);
            True(created.Success);
            byte[] encoded = GroupCatalogCodec.Encode(scope, created.Catalog);
            True(GroupCatalogCodec.TryDecode(encoded, scope, out GroupCatalog decoded, out _));
            Equal(created.Catalog.Revision, decoded.Revision);
            Equal("Builders", decoded.Groups[0].DisplayName);
        }

        private static void SignedCharacterIdentitiesAreCanonical()
        {
            foreach (long value in new[] { 1L, -1L, -691478230L, 1793603172L, long.MinValue, long.MaxValue })
            {
                string canonical = PortalPermissionAdapter.Identity(value);
                True(PortalPermissionAdapter.TryPlayerId(canonical, out long parsed));
                Equal(value, parsed);
                True(PortalPermissionAdapter.TryParseIdentity(canonical, out StableIdentity identity));
                Equal(canonical, identity.CanonicalKey);
            }
            foreach (string value in new[] { "0", "-0", "+1", "01", "-01", " 1", "1 ", "1.0", "--1",
                         "9223372036854775808", "-9223372036854775809", "", "Bulvye" })
                False(PortalPermissionAdapter.TryParseIdentity("valheim.player:" + value, out _));
            False(PortalPermissionAdapter.TryParseIdentity("steam:123", out _));
            False(PortalPermissionAdapter.TryParseIdentity(null, out _));
        }

        private static void SignedCharactersCreateInviteAcceptAndPersist()
        {
            foreach (long ownerId in new[] { 123L, -123L })
            foreach (long memberId in new[] { 456L, -456L })
            {
                string root = Path.Combine(Path.GetTempPath(), "runic-portals-signed-" + Guid.NewGuid().ToString("N"));
                try
                {
                    const string scope = "valheim.0000000000000003";
                    True(PortalPermissionAdapter.TryParseIdentity(PortalPermissionAdapter.Identity(ownerId), out StableIdentity owner));
                    True(PortalPermissionAdapter.TryParseIdentity(PortalPermissionAdapter.Identity(memberId), out StableIdentity member));
                    var stranger = new StableIdentity("valheim.player", "-789");
                    var store = new CompatibleGroupWorldStore(root);
                    var processor = new GroupCommandProcessor(store, () => scope);
                    Guid group = Guid.NewGuid();
                    True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Create, group, "Builders")).Success);
                    False(processor.Execute(stranger, new GroupCommand(GroupCommandKind.Invite, group,
                        target: member, invitationExpiresUtcTicks: DateTime.UtcNow.AddHours(1).Ticks)).Success);
                    True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Invite, group,
                        target: member, invitationExpiresUtcTicks: DateTime.UtcNow.AddHours(1).Ticks)).Success);
                    False(processor.Execute(stranger, new GroupCommand(GroupCommandKind.Accept, group)).Success);
                    True(processor.Execute(member, new GroupCommand(GroupCommandKind.Accept, group)).Success);
                    var reopened = new CompatibleGroupWorldStore(root);
                    GroupWorldReadResult read = reopened.Read(scope);
                    Equal(GroupWorldReadState.Ready, read.State);
                    True(read.Catalog.TryGetGroup(group, out GroupRecord saved));
                    True(saved.TryGetMember(member, out _));
                    True(saved.TryGetMember(owner, out _));
                    False(saved.TryGetMember(stranger, out _));
                    True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Remove, group, target: member)).Success);
                    True(store.Read(scope).Catalog.TryGetGroup(group, out GroupRecord updated));
                    False(updated.TryGetMember(member, out _));
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
        }

        private static void SignedGroupMembersKeepPortalPermissionBoundaries()
        {
            string groupId = Guid.NewGuid().ToString("N");
            var groups = new GroupMemberships();
            groups.Add(groupId, -456L);
            var permissions = new PortalPermissionAdapter(groups);
            PortalEndpoint endpoint = PolicyEndpoint(PortalNetworkKind.Group,
                "valheim.player:-123", PortalAccessProfile.ForGroup(groupId));
            True(permissions.Allows(endpoint, "valheim.player:-456", PortalAccessAction.Arrive));
            True(permissions.Allows(endpoint, "valheim.player:-456", PortalAccessAction.Depart));
            False(permissions.Allows(endpoint, "valheim.player:456", PortalAccessAction.Arrive));
            False(permissions.Allows(endpoint, "valheim.player:0", PortalAccessAction.Arrive));
            False(permissions.Allows(endpoint, "valheim.player:-456", PortalAccessAction.Edit));
            True(permissions.Allows(endpoint, "valheim.player:-123", PortalAccessAction.Edit));
            groups.Available = false;
            False(permissions.Allows(endpoint, "valheim.player:-456", PortalAccessAction.Arrive));
        }

        private static void GroupIdentityFailureRepliesWithoutMembershipDisclosure()
        {
            string source = File.ReadAllText(Path.Combine(ProjectRoot(), "Integration", "PortalGroupRuntime.cs"));
            Contains(source, "out string identityFailure", "SendResponse(sender, requestId, false,",
                "null, null, DateTime.UtcNow.Ticks", "actor == null",
                "? Array.Empty<PortalGroupChoice>() : MembershipChoices(actor)",
                "character.GetOwner() != sender", "peer.m_rpc == null || peer.m_socket == null",
                "PortalPermissionAdapter.TryParseIdentity(value, out identity)");
            Reject(source, "portal-group-identity-unbound", "playerId <= 0L", "GetPlayerID() <= 0L", "GetPlayerID() > 0L");
            foreach (string file in new[] { "PortalDirectorySync.cs", "PortalMapOverlayIntegration.cs", "PortalRuntime.cs", "ValheimContracts.cs" })
                Reject(File.ReadAllText(Path.Combine(ProjectRoot(), "Integration", file)),
                    "playerId <= 0L", "playerId > 0L", "GetPlayerID() <= 0L");
        }

        private static void CompatibleGroupStoreUsesExistingFormat()
        {
            string root = Path.Combine(Path.GetTempPath(), "runic-portals-groups-" + Guid.NewGuid().ToString("N"));
            try
            {
                const string scope = "valheim.0000000000000002";
                var store = new CompatibleGroupWorldStore(root);
                var processor = new GroupCommandProcessor(store, () => scope);
                var owner = new StableIdentity("valheim.player", "456");
                GroupCommandExecutionResult result = processor.Execute(
                    owner,
                    new GroupCommand(GroupCommandKind.Create, Guid.NewGuid(), "Raiders"));
                True(result.Success);
                GroupWorldReadResult read = store.Read(scope);
                Equal(GroupWorldReadState.Ready, read.State);
                Equal("Raiders", read.Catalog.Groups[0].DisplayName);
                string file = Directory.GetFiles(root, "*.groups").Single();
                True(GroupCatalogCodec.TryDecode(
                    File.ReadAllBytes(file), scope, out GroupCatalog decoded, out _));
                Equal(read.Catalog.Revision, decoded.Revision);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void DirectoryStaysInsideExactNetwork()
        {
            var graph = new PortalGraph(new[]
            {
                Endpoint("a", "Alpha", "home", true, true),
                Endpoint("b", "Beta", "home", true, true),
                Endpoint("c", "Elsewhere", "HOME", true, true)
            }, 8);
            PortalDirectoryResult result = graph.Query(
                new PortalDirectoryQuery("valheim.player:1", "a", "home"),
                new AllowAll());
            Equal(RouteStopCode.Ready, result.StopCode);
            Equal(1, result.Entries.Count);
            Equal("b", result.Entries[0].PortalId);
        }

        private static void RouteRejectsCrossNetwork()
        {
            var graph = new PortalGraph(new[]
            {
                Endpoint("a", "Alpha", "home", true, true),
                Endpoint("b", "Beta", "other", true, true)
            }, 8);
            RoutePlan plan = graph.Plan(new RoutePlanRequest(
                "valheim.player:1", "a", "b", TravelPolicyState.Allowed, true),
                new AllowAll());
            Equal(RouteStopCode.NetworkMismatch, plan.StopCode);
        }

        private static void OneWayRequiresAcknowledgement()
        {
            var graph = new PortalGraph(new[]
            {
                Endpoint("a", "Alpha", "home", true, true),
                Endpoint("b", "Beta", "home", true, false)
            }, 8);
            RoutePlan warning = graph.Plan(new RoutePlanRequest(
                "valheim.player:1", "a", "b", TravelPolicyState.Allowed, false),
                new AllowAll());
            Equal(RouteStopCode.OneWayWarningRequired, warning.StopCode);
            RoutePlan ready = graph.Plan(new RoutePlanRequest(
                "valheim.player:1", "a", "b", TravelPolicyState.Allowed, true),
                new AllowAll());
            True(ready.IsReady && ready.IsOneWay);
        }

        private static void PublicPolicyDefaultsToTravelAndOwnerEdit()
        {
            var permissions = new PortalPermissionAdapter(null);
            PortalEndpoint endpoint = PolicyEndpoint(
                PortalNetworkKind.Public,
                "valheim.player:1",
                PortalAccessProfile.PublicNetwork);
            const string owner = "valheim.player:1";
            const string visitor = "valheim.player:2";
            True(permissions.Allows(endpoint, visitor, PortalAccessAction.ViewDiscover));
            True(permissions.Allows(endpoint, visitor, PortalAccessAction.Arrive));
            True(permissions.Allows(endpoint, visitor, PortalAccessAction.Depart));
            False(permissions.Allows(endpoint, visitor, PortalAccessAction.Edit));
            True(permissions.Allows(endpoint, owner, PortalAccessAction.Edit));
            Equal(PortalNetworkKind.Public,
                PortalEditCommand.Parse("network|home|north|both").NetworkKind);
        }

        private static void PrivatePolicyRequiresRecordedOwner()
        {
            var permissions = new PortalPermissionAdapter(null);
            PortalEndpoint endpoint = PolicyEndpoint(
                PortalNetworkKind.Personal,
                "valheim.player:1",
                PortalAccessProfile.PrivateNetwork);
            foreach (PortalAccessAction action in new[]
                     {
                         PortalAccessAction.ViewDiscover,
                         PortalAccessAction.Arrive,
                         PortalAccessAction.Depart,
                         PortalAccessAction.Edit
                     })
            {
                True(permissions.Allows(endpoint, "valheim.player:1", action));
                False(permissions.Allows(endpoint, "valheim.player:2", action));
            }
        }

        private static void GroupPolicyUsesCurrentAuthenticatedMembership()
        {
            string groupId = Guid.NewGuid().ToString("N");
            var groups = new GroupMemberships();
            groups.Add(groupId, 2L);
            var permissions = new PortalPermissionAdapter(groups);
            PortalEndpoint endpoint = PolicyEndpoint(
                PortalNetworkKind.Group,
                "valheim.player:1",
                PortalAccessProfile.ForGroup(groupId));
            True(permissions.Allows(endpoint, "valheim.player:2", PortalAccessAction.ViewDiscover));
            True(permissions.Allows(endpoint, "valheim.player:2", PortalAccessAction.Arrive));
            True(permissions.Allows(endpoint, "valheim.player:2", PortalAccessAction.Depart));
            False(permissions.Allows(endpoint, "valheim.player:2", PortalAccessAction.Edit));
            True(permissions.Allows(endpoint, "valheim.player:1", PortalAccessAction.Edit));
            False(permissions.Allows(endpoint, "valheim.player:3", PortalAccessAction.Arrive));
            groups.Available = false;
            False(permissions.Allows(endpoint, "valheim.player:2", PortalAccessAction.Arrive));
        }

        private static void WardEvidenceIsIndependentFromPortalPolicy()
        {
            var permissions = new PortalPermissionAdapter(null);
            PortalEndpoint source = PolicyEndpoint(
                PortalNetworkKind.Public,
                "valheim.player:1",
                PortalAccessProfile.PublicNetwork,
                "source");
            PortalEndpoint destination = PolicyEndpoint(
                PortalNetworkKind.Public,
                "valheim.player:1",
                PortalAccessProfile.PublicNetwork,
                "destination");
            PortalRoutePermissionEvidence route = PortalRoutePermissionEvidence.Evaluate(
                permissions,
                source,
                destination,
                "valheim.player:2",
                false,
                true);
            True(route.PolicyAllowed);
            False(route.SourceWardAllowed);
            True(route.DestinationWardAllowed);
            Equal(
                AuthorityStopCode.SourceWardDenied,
                new PortalAuthorityGate().Evaluate(Evidence(
                    PortalMutationKind.CommitTravel,
                    isServer: false,
                    sourceOwned: false,
                    playerOwned: true,
                    ownerPermission: route.PolicyAllowed,
                    sourceWard: route.SourceWardAllowed,
                    destinationWard: route.DestinationWardAllowed)).StopCode);
        }

        private static void RemoteDestinationWardPolicyIsConsistent()
        {
            False(ValheimContracts.WardAllows(new WardContext(WardState.Ambiguous)));
            True(ValheimContracts.DestinationWardAllows(new WardContext(WardState.Ambiguous)));
            True(ValheimContracts.DestinationWardAllows(WardContext.NoWard));
            True(ValheimContracts.DestinationWardAllows(new WardContext(WardState.Allows)));
            False(ValheimContracts.DestinationWardAllows(new WardContext(WardState.Denies)));
            False(ValheimContracts.DestinationWardAllows(
                new WardContext(WardState.OverlappingHostile)));

            string runtime = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalRuntime.cs"));
            string picker = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalMapPicker.cs"));
            string overlay = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalMapOverlayIntegration.cs"));
            Contains(runtime,
                "ValheimContracts.WardAllows(sourceWard)",
                "ValheimContracts.DestinationWardAllows(destinationWard)",
                "ValheimContracts.DestinationWardAllows(ValheimContracts.ResolveWard(");
            Contains(picker,
                "ValheimContracts.WardAllows(sourceWard)",
                "ValheimContracts.DestinationWardAllows(ward)");
            Contains(overlay, "ValheimContracts.DestinationWardAllows(ward)");
            Equal(2, Count(runtime, "ValheimContracts.WardAllows(sourceWard)"));
        }

        private static void AuthorityUsesNativeLocalEvidence()
        {
            var gate = new PortalAuthorityGate();
            PortalAuthorityDecision decision = gate.Evaluate(Evidence(
                PortalMutationKind.CommitTravel,
                isServer: false,
                sourceOwned: false,
                playerOwned: true));
            True(decision.IsAllowed);
        }

        private static void AuthorityRequiresSourceOwnershipForEdit()
        {
            var gate = new PortalAuthorityGate();
            PortalAuthorityDecision decision = gate.Evaluate(Evidence(
                PortalMutationKind.ConfigureMetadata,
                isServer: false,
                sourceOwned: false,
                playerOwned: false));
            Equal(AuthorityStopCode.SourceObjectNotOwned, decision.StopCode);
        }

        private static void ConfirmationIsBoundedRepeat()
        {
            var gate = new PortalOverwriteConfirmationGate();
            Equal(PortalConfirmationAdmission.Pending, gate.Request(true, "portal:a", "one"));
            Equal(PortalConfirmationAdmission.Approved, gate.Request(true, "portal:a", "one"));
            Equal(PortalConfirmationAdmission.Pending, gate.Request(true, "portal:a", "two"));
            gate.Cancel("portal:a");
            Equal(PortalConfirmationAdmission.Pending, gate.Request(true, "portal:a", "two"));
        }

        private static void SelectionStoreEvictsOldest()
        {
            var store = new PortalSelectionStore(1);
            store.Set(new PortalSelection("one", "a", "b", 1, 1, false, 1));
            store.Set(new PortalSelection("two", "a", "c", 1, 1, false, 2));
            False(store.TryGet("one", "a", out _));
            True(store.TryGet("two", "a", out _));
        }

        private static void MapOverlayRejectsDuplicateIds()
        {
            var model = new PortalMapOverlayModel();
            model.SetContext("world:player");
            var candidate = new PortalMapCandidate(
                "a", "home", "Alpha", 1f, 2f, 3f, true, true, true, true, true);
            False(model.TryReplace(new[] { candidate, candidate }));
        }

        private static void ArrivalSuppressionExpires()
        {
            var suppression = new PortalArrivalSuppression();
            suppression.Arm(1L, "portal", 100L, 20L);
            True(suppression.Blocks(1L, "portal", 119L));
            False(suppression.Blocks(1L, "portal", 121L));
        }

        private static void GroupApiIsPublicAndOptional()
        {
            var method = typeof(GroupIntegrationApi).GetMethod("TryIsMember");
            True(method != null && method.IsPublic && method.IsStatic);
            False(GroupIntegrationApi.TryIsMember(Guid.NewGuid().ToString("N"), 1L, out _));
        }

        private static void ManifestHasOnlyBepInEx()
        {
            string manifest = File.ReadAllText(Path.Combine(ProjectRoot(), "manifest.json"));
            Contains(manifest, "\"version_number\": \"1.2.4\"");
            Equal("1.2.4", Plugin.Version);
            Contains(manifest, "denikson-BepInExPack_Valheim-5.4.2350");
            Reject(manifest, "RunicCore", "RunicPersistence", "RunicPermissions", "RunicTransactions");
        }

        private static void ProjectHasNoRuntimeFoundationReference()
        {
            string project = File.ReadAllText(Path.Combine(ProjectRoot(), "RunicPortals.csproj"));
            Reject(project, "ProjectReference", "RunicCore.csproj", "RunicPersistence.csproj",
                "RunicPermissions.csproj", "RunicTransactions.csproj");
            Contains(project, "Link=\"Embedded\\GroupCatalog.cs\"");
        }

        private static void ProductionHasNoDurableOrRegistryLayer()
        {
            string source = ProductionSource();
            Reject(source, "Runic.Foundation", "RunicRegistry", "IRunicRpcService",
                "PortalRemote", "PortalPickerProtection", "DurableComposite", "quarantine");
        }

        private static void GroupRpcIsBoundedAndPeerAuthenticated()
        {
            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalGroupRuntime.cs"));
            Contains(source, "MaximumPending = 32", "MaximumReplayEntries = 128",
                "RequestLifetimeTicks", "GetPeer(sender)", "peer.m_characterID",
                "character.GetOwner() != sender", "MaximumEnvelopeBytes");
            Reject(source, "File.WriteAllText", "journal", "quarantine");
        }

        private static void DirectorySyncWarmsRemotePortalsOnFirstUse()
        {
            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalDirectorySync.cs"));
            string picker = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalMapPicker.cs"));
            Contains(source,
                "MaximumDirectoryEnvelopeBytes = 2048",
                "MaximumDirectoryEndpointsSent = 512",
                "MaximumDirectoryAttempts = 8",
                "MaximumDirectoryRequestDistanceMeters = 16f",
                "sourceZdoId",
                "ZDOMan.instance?.GetZDO(sourceZdoId)",
                "GetPeer(sender)",
                "character.GetOwner() != sender",
                "PortalAccessAction.ViewDiscover",
                "PortalAccessAction.Arrive",
                "ForceSendZDO(sender, zdo.m_uid)",
                "DirectoryRequestTimeoutSeconds = 8f",
                "source-ward-unavailable");
            string contracts = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "ValheimContracts.cs"));
            Contains(contracts,
                "SourceWardAllows",
                "PokeLocalZone",
                "ZoneSystem.GetZone(position)",
                "WardState.Ambiguous");
            Contains(picker,
                "BeginDirectorySync(_mapPicker, source, candidates, truncated)",
                "TickDirectoryPicker(session)");
            Reject(source, "SetOwner(", "ClaimOwnership", "RequestOwnership");
        }

        private static void LegacyDirectoryProtocolGetsCompatibilityRejection()
        {
            string requestId = Guid.NewGuid().ToString("N");
            var request = new ZPackage();
            request.Write(1);
            request.Write(requestId);
            request.Write("3227159871:3");
            request.Write(1L);
            request.Write("home");
            request.Write(0x50524431);

            Type runtime = typeof(Plugin).Assembly.GetType(
                "RunicPortals.Integration.PortalRuntime", true);
            MethodInfo reader = runtime.GetMethod(
                "TryReadLegacyDirectoryRequest",
                BindingFlags.NonPublic | BindingFlags.Static);
            True(reader != null);
            object[] arguments = { new ZPackage(request.GetArray()), string.Empty };
            True((bool)reader.Invoke(null, arguments));
            Equal(requestId, (string)arguments[1]);

            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalDirectorySync.cs"));
            Contains(source,
                "portal-directory-protocol-outdated",
                "client-update-required",
                "legacyRequestId, 2");
            string groups = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalGroupRuntime.cs"));
            Contains(groups, "Character identity is not ready: ", "SendResponse(sender, requestId, false,");
        }

        private static void NormalMapDirectoryWarmsFromAuthoritativeServer()
        {
            string source = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalDirectorySync.cs"));
            string overlay = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Integration", "PortalMapOverlayIntegration.cs"));
            Contains(source,
                "MapDirectoryRequestRpc",
                "MapDirectoryResponseRpc",
                "TickMapDirectorySync",
                "PortalAccessAction.ViewDiscover",
                "ForceSendZDO(sender, zdo.m_uid)",
                "MapDirectoryRefreshSeconds = 15f");
            Contains(overlay, "TickMapDirectorySync(context, realtime)");
            Reject(source, "SetOwner(", "ClaimOwnership", "RequestOwnership");
        }

        private static void PortalMutationsNeverClaimOwnership()
        {
            string source = ProductionSource();
            Reject(source, ".SetOwner(", "ClaimOwnership", "RequestOwnership");
            Contains(source, "view.IsOwner()", "playerView.IsOwner()");
        }

        private static PortalEndpoint Endpoint(
            string id,
            string name,
            string network,
            bool arrives,
            bool departs) => new PortalEndpoint(
            id,
            PortalMode.Network,
            name,
            network,
            PortalNetworkKind.Public,
            "valheim.player:1",
            "Owner",
            PortalOnlineState.Online,
            arrives,
            departs,
            PortalAccessProfile.PublicNetwork,
            1L);

        private static PortalEndpoint PolicyEndpoint(
            PortalNetworkKind kind,
            string owner,
            PortalAccessProfile access,
            string id = "policy") => new PortalEndpoint(
            id,
            PortalMode.Network,
            "Policy",
            "home",
            kind,
            owner,
            "Owner",
            PortalOnlineState.Online,
            true,
            true,
            access,
            1L);

        private static PortalAuthorityEvidence Evidence(
            PortalMutationKind kind,
            bool isServer,
            bool sourceOwned,
            bool playerOwned,
            bool ownerPermission = true,
            bool sourceWard = true,
            bool destinationWard = true) => new PortalAuthorityEvidence(
            kind, true, isServer, false, false, true, true, true, true,
            sourceOwned, playerOwned, ownerPermission, sourceWard, destinationWard, true, true, true);

        private static string ProjectRoot() => Path.Combine(RepositoryRoot(), "RunicPortals");

        private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));

        private static string ProductionSource() => string.Join("\n",
            Directory.GetFiles(ProjectRoot(), "*.cs", SearchOption.AllDirectories)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        private static void Contains(string value, params string[] expected)
        {
            foreach (string item in expected)
                if (value.IndexOf(item, StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Missing expected text: " + item);
        }

        private static void Reject(string value, params string[] forbidden)
        {
            foreach (string item in forbidden)
                if (value.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Found forbidden text: " + item);
        }

        private static int Count(string value, string expected)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(expected, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += expected.Length;
            }
            return count;
        }

        private static void True(bool value, string message = "Expected true.")
        { if (!value) throw new InvalidOperationException(message); }

        private static void False(bool value, string message = "Expected false.")
        { if (value) throw new InvalidOperationException(message); }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }
    }
}
