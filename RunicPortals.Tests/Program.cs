using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;
using RunicPortals.Api;
using RunicPortals.Core;
using RunicPortals.Integration;

namespace RunicPortals.Tests
{
    internal static class Program
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

        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("ParserKeepsPublicPrivateAndGroupFormats", ParserKeepsPublicPrivateAndGroupFormats),
                ("PortalMetadataKeysRemainStable", PortalMetadataKeysRemainStable),
                ("GroupCatalogCodecRoundTrips", GroupCatalogCodecRoundTrips),
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
            Contains(manifest, "denikson-BepInExPack_Valheim-5.4.2333");
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
                "MaximumDirectoryRequestDistanceMeters = 16f",
                "GetPeer(sender)",
                "character.GetOwner() != sender",
                "PortalAccessAction.ViewDiscover",
                "PortalAccessAction.Arrive",
                "ForceSendZDO(sender, zdo.m_uid)",
                "DirectoryRequestTimeoutSeconds");
            Contains(picker,
                "BeginDirectorySync(_mapPicker, source, candidates, truncated)",
                "TickDirectoryPicker(session)");
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
