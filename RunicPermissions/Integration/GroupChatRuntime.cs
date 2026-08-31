using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Runic.Foundation.Persistence;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;
using UnityEngine;

namespace RunicPermissions.Integration
{
    internal sealed partial class GroupRuntime
    {
        internal const string FriendlyQueryEndpointId = "runic.permissions.groups.friendly-query";
        internal const string FriendlyMutationEndpointId = "runic.permissions.groups.friendly-command";
        private static Terminal.ConsoleCommand _friendlyCommand;
        private bool _activeRefreshInFlight;
        private bool _activeCacheCurrent;
        private bool _serverConnectionWasReady;
        private float _nextActiveRefresh;

        private sealed class ConnectedPlayer
        {
            internal StableIdentity Identity;
            internal string DisplayName;
        }

        private sealed class CommandOutcome
        {
            internal bool Success;
            internal string ReasonCode;
            internal GroupRecord Group;
        }

        private void RegisterFriendlyEndpoints()
        {
            _leases.Add(_rpc.RegisterEndpoint(
                _module,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    FriendlyQueryEndpointId,
                    GroupCapabilities.ActiveSelection,
                    GroupCapabilities.ProtocolVersion,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.ReadOnly,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.BackendAccount,
                    GroupFriendlyProtocol.MaximumRequestBytes),
                HandleFriendlyQuery));
            _leases.Add(_rpc.RegisterEndpoint(
                _module,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    FriendlyMutationEndpointId,
                    GroupCapabilities.ActiveSelection,
                    GroupCapabilities.ProtocolVersion,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.HandlerDurable,
                    RpcIdentityAssurance.BackendAccount,
                    GroupFriendlyProtocol.MaximumRequestBytes),
                HandleFriendlyMutation));
        }

        private void AttachFriendlyRuntime()
        {
            _activeRefreshInFlight = false;
            _activeCacheCurrent = _rpc.IsServer;
            _serverConnectionWasReady = _rpc.IsServerConnectionReady;
            _nextActiveRefresh = 0f;
        }

        private void DetachFriendlyRuntime()
        {
            _activeRefreshInFlight = false;
            _activeCacheCurrent = false;
            _serverConnectionWasReady = false;
            _activeGroups.ClearClientCache();
        }

        internal void Tick()
        {
            if (_disposed || _rpc.IsServer) return;
            bool ready = _rpc.IsServerConnectionReady;
            if (!ready)
            {
                if (_serverConnectionWasReady || _activeCacheCurrent)
                    _activeGroups.ClearClientCache();
                _serverConnectionWasReady = false;
                _activeRefreshInFlight = false;
                _activeCacheCurrent = false;
                return;
            }
            _serverConnectionWasReady = true;
            if (_activeCacheCurrent || _activeRefreshInFlight ||
                Time.realtimeSinceStartup < _nextActiveRefresh ||
                !TryLocalPlayerIdentity(out _)) return;
            RequestActiveRefresh();
        }

        private void RequestActiveRefresh()
        {
            var request = new GroupFriendlyRequest(GroupFriendlyOperation.Active);
            _activeRefreshInFlight = true;
            RpcSendResult sent = _rpc.SendToServer(
                _module,
                FriendlyQueryEndpointId,
                GroupFriendlyProtocol.EncodeRequest(request),
                string.Empty,
                TimeSpan.FromSeconds(5),
                response => CompleteFriendlyResponse(response, null, true));
            if (!sent.Accepted)
            {
                _activeRefreshInFlight = false;
                _nextActiveRefresh = Time.realtimeSinceStartup + 2f;
            }
        }

        private static void AttachFriendlyCommand()
        {
            if (_friendlyCommand != null) return;
            _friendlyCommand = new Terminal.ConsoleCommand(
                "group",
                "Runic Group chat management. Type /group help.",
                (Terminal.ConsoleEvent)OnFriendlyCommand,
                false, false, false, false, false, null, false, false, false);
        }

        private static void OnFriendlyCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (args?.Context == null ||
                    !ReferenceEquals(args.Context, Chat.instance) &&
                    !ReferenceEquals(args.Context, Console.instance))
                {
                    args?.Context?.AddString("Runic Group: use the local chat panel.");
                    return;
                }
                GroupRuntime runtime;
                lock (ConsoleGate) runtime = _consoleRuntime;
                if (runtime == null || runtime._disposed)
                {
                    args.Context.AddString("Runic Group: service unavailable.");
                    return;
                }
                if (!TryParseFriendly(
                        args.Args,
                        out GroupFriendlyRequest request,
                        out string localText))
                {
                    args.Context.AddString(localText);
                    return;
                }
                runtime.ExecuteFriendly(request, args.Context.AddString);
            }
            catch (Exception exception)
            {
                try { args?.Context?.AddString("Runic Group: command failed safely."); }
                catch { }
                try { _consoleRuntime?._log?.LogWarning(
                    "Friendly Group command failed closed: " + exception.GetType().Name); }
                catch { }
            }
        }

        private static bool TryParseFriendly(
            string[] args,
            out GroupFriendlyRequest request,
            out string localText)
        {
            request = null;
            args = args ?? Array.Empty<string>();
            string verb = args.Length > 1
                ? (args[1] ?? string.Empty).Trim().ToLowerInvariant()
                : "help";
            if (verb == "help")
            {
                localText = FriendlyHelp();
                return false;
            }
            try
            {
                switch (verb)
                {
                    case "list":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.List);
                        break;
                    case "active":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.Active);
                        break;
                    case "members":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.Members);
                        break;
                    case "whoami":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.WhoAmI);
                        break;
                    case "create":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Create,
                            Join(args, 2),
                            proposedGroupId: Guid.NewGuid());
                        break;
                    case "use":
                    case "select":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Select, Join(args, 2));
                        break;
                    case "accept":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Accept, Join(args, 2));
                        break;
                    case "leave":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.Leave);
                        break;
                    case "rename":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Rename, Join(args, 2));
                        break;
                    case "invite":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Invite, Join(args, 2), number: 168);
                        break;
                    case "cancel":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.CancelInvitation, Join(args, 2));
                        break;
                    case "remove":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.Remove, Join(args, 2));
                        break;
                    case "role":
                        if (args.Length < 4) throw new ArgumentException();
                        string role = (args[args.Length - 1] ?? string.Empty).Trim().ToLowerInvariant();
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.SetRole,
                            Join(args, 2, args.Length - 1),
                            role);
                        break;
                    case "transfer":
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.TransferOwnership, Join(args, 2));
                        break;
                    case "delete":
                        RequireCount(args, 2);
                        request = new GroupFriendlyRequest(GroupFriendlyOperation.Delete);
                        break;
                    default:
                        localText = "Unknown Group command. Type /group help.";
                        return false;
                }
                localText = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is OverflowException)
            {
                localText = "That Group command is incomplete or invalid. Type /group help.";
                request = null;
                return false;
            }
        }

        private void ExecuteFriendly(GroupFriendlyRequest request, Action<string> output)
        {
            if (request == null || output == null) return;
            if (_rpc.IsServer)
            {
                if (!TryLocalActor(out StableIdentity actor))
                {
                    output("Runic Group: local server player identity is unavailable.");
                    return;
                }
                GroupFriendlyResponse local = request.IsQuery
                    ? EvaluateFriendlyQuery(actor, request)
                    : EvaluateFriendlyMutation(actor, request, false);
                ApplyFriendlyResponse(local, output);
                return;
            }
            string endpoint = request.IsQuery
                ? FriendlyQueryEndpointId
                : FriendlyMutationEndpointId;
            RpcSendResult sent = _rpc.SendToServer(
                _module,
                endpoint,
                GroupFriendlyProtocol.EncodeRequest(request),
                request.IsQuery ? string.Empty : "group-friendly-" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromSeconds(5),
                response => CompleteFriendlyResponse(response, output, false));
            if (!sent.Accepted)
                output("Runic Group: request could not be sent (" + sent.ReasonCode + ").");
        }

        private RpcHandlerResult HandleFriendlyQuery(RpcRequestContext context)
        {
            if (!GroupFriendlyProtocol.TryDecodeRequest(
                    context?.Payload, out GroupFriendlyRequest request, out string failure) ||
                !request.IsQuery)
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, failure);
            if (!TryRequestActor(context, out StableIdentity actor, out string denial))
                return RpcHandlerResult.Deny(denial);
            return RpcHandlerResult.Ok(GroupFriendlyProtocol.EncodeResponse(
                EvaluateFriendlyQuery(actor, request)));
        }

        private RpcHandlerResult HandleFriendlyMutation(RpcRequestContext context)
        {
            if (!GroupFriendlyProtocol.TryDecodeRequest(
                    context?.Payload, out GroupFriendlyRequest request, out string failure) ||
                request.IsQuery)
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, failure);
            if (!TryRequestActor(context, out StableIdentity actor, out string denial))
                return RpcHandlerResult.Deny(denial);
            GroupFriendlyResponse response = EvaluateFriendlyMutation(actor, request, true);
            return RpcHandlerResult.Ok(GroupFriendlyProtocol.EncodeResponse(response));
        }

        private GroupFriendlyResponse EvaluateFriendlyQuery(
            StableIdentity actor,
            GroupFriendlyRequest request)
        {
            ActiveGroupSelection active = _activeGroups.Resolve(actor);
            if (!TryReadCatalog(out GroupCatalog catalog, out string failure))
                return Friendly("Group information is unavailable (" + failure + ").", active);
            switch (request.Operation)
            {
                case GroupFriendlyOperation.List:
                {
                    IReadOnlyList<GroupMembership> memberships = catalog.GetMemberships(actor);
                    if (memberships.Count == 0)
                        return Friendly("You are not in a Group. Use /group create <name>.", active);
                    var lines = new List<string> { "Your Groups:" };
                    for (int index = 0; index < memberships.Count; index++)
                    {
                        GroupMembership membership = memberships[index];
                        bool selected = active.IsAvailable && string.Equals(
                            active.GroupId, membership.GroupIdText, StringComparison.Ordinal);
                        lines.Add("- " + membership.DisplayName + " (" +
                                  membership.Role.ToString().ToLowerInvariant() + ")" +
                                  (selected ? " [active]" : string.Empty));
                    }
                    if (!active.IsAvailable)
                        lines.Add("Use /group use <group name> to choose your active Group.");
                    return Friendly(string.Join("\n", lines), active);
                }
                case GroupFriendlyOperation.Active:
                    return Friendly(ActiveLabel(active), active);
                case GroupFriendlyOperation.Members:
                {
                    if (!TryActiveRecord(actor, catalog, active, out GroupRecord group, out failure))
                        return Friendly(failure, active);
                    List<ConnectedPlayer> connected = ConnectedPlayers();
                    var text = new StringBuilder("Members of " + group.DisplayName + ":");
                    for (int index = 0; index < group.Members.Count; index++)
                    {
                        GroupMember member = group.Members[index];
                        string label = ConnectedLabel(connected, member.Identity);
                        AppendBounded(text, "\n- " + label + " (" +
                            member.Role.ToString().ToLowerInvariant() + ")");
                    }
                    return Friendly(text.ToString(), active);
                }
                case GroupFriendlyOperation.WhoAmI:
                    return Friendly("Your exact Group identity is " + actor.CanonicalKey + ".", active);
                default:
                    return Friendly("That Group query is unsupported.", active);
            }
        }

        private GroupFriendlyResponse EvaluateFriendlyMutation(
            StableIdentity actor,
            GroupFriendlyRequest request,
            bool durable)
        {
            ActiveGroupSelection active = _activeGroups.Resolve(actor);
            if (!TryReadCatalog(out GroupCatalog catalog, out string failure))
                return Friendly("Group command unavailable (" + failure + ").", active);
            if (request.Operation == GroupFriendlyOperation.Select)
            {
                if (!TryResolveMemberGroup(
                        catalog, actor, request.Primary, out GroupRecord selected, out failure))
                    return Friendly(failure, active);
                if (!_activeGroups.TrySetAuthoritative(
                        actor, selected.Id, out active, out string setFailure))
                    return Friendly("Could not select that Group (" + setFailure + ").", active);
                return Friendly("Active Group: " + selected.DisplayName + ".", active);
            }

            GroupCommand command;
            GroupRecord targetGroup = null;
            switch (request.Operation)
            {
                case GroupFriendlyOperation.Create:
                    command = new GroupCommand(
                        GroupCommandKind.Create, request.ProposedGroupId, request.Primary);
                    break;
                case GroupFriendlyOperation.Accept:
                    if (!TryResolveInvitation(
                            catalog, actor, request.Primary, out targetGroup, out failure))
                        return Friendly(failure, active);
                    command = new GroupCommand(GroupCommandKind.Accept, targetGroup.Id);
                    break;
                default:
                    if (!TryActiveRecord(actor, catalog, active, out targetGroup, out failure))
                        return Friendly(failure, active);
                    command = BuildActiveCommand(request, targetGroup, out failure);
                    if (command == null) return Friendly(failure, active);
                    break;
            }

            CommandOutcome outcome = durable
                ? ExecuteDurable(actor, command)
                : ExecuteLocal(actor, command);
            if (!outcome.Success)
                return Friendly("Group command denied: " + ReasonLabel(outcome.ReasonCode) + ".", active);

            bool shouldSelect = request.Operation == GroupFriendlyOperation.Create ||
                                request.Operation == GroupFriendlyOperation.Accept;
            bool shouldClear = request.Operation == GroupFriendlyOperation.Leave ||
                               request.Operation == GroupFriendlyOperation.Delete;
            if (shouldSelect)
            {
                Guid id = command.GroupId;
                if (!_activeGroups.TrySetAuthoritative(
                        actor, id, out active, out string selectFailure))
                    return Friendly(SuccessLabel(request.Operation, outcome.Group) +
                                    " It was saved, but could not become active (" +
                                    selectFailure + ").", active);
            }
            else if (shouldClear)
            {
                if (!_activeGroups.TrySetAuthoritative(
                        actor, Guid.Empty, out active, out string clearFailure))
                    return Friendly(SuccessLabel(request.Operation, targetGroup) +
                                    " The old active preference could not be cleared (" +
                                    clearFailure + ").", active);
            }
            else
            {
                active = _activeGroups.Resolve(actor);
            }
            return Friendly(SuccessLabel(request.Operation, outcome.Group ?? targetGroup), active);
        }

        private GroupCommand BuildActiveCommand(
            GroupFriendlyRequest request,
            GroupRecord group,
            out string failure)
        {
            failure = string.Empty;
            switch (request.Operation)
            {
                case GroupFriendlyOperation.Invite:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity invitee, out failure))
                        return null;
                    return new GroupCommand(
                        GroupCommandKind.Invite,
                        group.Id,
                        target: invitee,
                        invitationExpiresUtcTicks: DateTime.UtcNow.AddHours(request.Number).Ticks);
                case GroupFriendlyOperation.Leave:
                    return new GroupCommand(GroupCommandKind.Leave, group.Id);
                case GroupFriendlyOperation.Rename:
                    return new GroupCommand(GroupCommandKind.Rename, group.Id, request.Primary);
                case GroupFriendlyOperation.CancelInvitation:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity cancelled, out failure))
                        return null;
                    return new GroupCommand(
                        GroupCommandKind.CancelInvitation, group.Id, target: cancelled);
                case GroupFriendlyOperation.Remove:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity removed, out failure))
                        return null;
                    return new GroupCommand(GroupCommandKind.Remove, group.Id, target: removed);
                case GroupFriendlyOperation.SetRole:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity roleTarget, out failure))
                        return null;
                    GroupRole role = string.Equals(
                        request.Secondary, "officer", StringComparison.Ordinal)
                        ? GroupRole.Officer
                        : GroupRole.Member;
                    return new GroupCommand(
                        GroupCommandKind.SetRole, group.Id, target: roleTarget, role: role);
                case GroupFriendlyOperation.TransferOwnership:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity successor, out failure))
                        return null;
                    return new GroupCommand(
                        GroupCommandKind.TransferOwnership, group.Id, target: successor);
                case GroupFriendlyOperation.Delete:
                    return new GroupCommand(GroupCommandKind.Delete, group.Id);
                default:
                    failure = "That Group command is unsupported.";
                    return null;
            }
        }

        private CommandOutcome ExecuteDurable(StableIdentity actor, GroupCommand command)
        {
            string scope = CurrentWorldScope();
            long now = DateTime.UtcNow.Ticks;
            if (scope.Length == 0)
                return new CommandOutcome { ReasonCode = "group-world-unavailable" };
            if (!_store.TryIssueCommand(
                    scope, actor, command, now, out GroupCommandIssue issue, out string issueFailure))
                return new CommandOutcome { ReasonCode = issueFailure ?? "group-world-unavailable" };
            var execution = new GroupCommandExecution(
                issue.Token,
                issue.ExpectedCatalogRevision,
                issue.ExpectedGroupRevision,
                command);
            if (!_store.TryExecuteIssuedCommand(
                    scope, actor, execution, now, out GroupCommandReceipt receipt,
                    out string executionFailure) || receipt == null)
                return new CommandOutcome { ReasonCode = executionFailure ?? "group-command-failed" };
            TryReadCatalog(out GroupCatalog catalog, out _);
            GroupRecord group = null;
            catalog?.TryGetGroup(command.GroupId, out group);
            if (receipt.Success) Audit(actor, command, receipt);
            return new CommandOutcome
            {
                Success = receipt.Success,
                ReasonCode = receipt.ReasonCode,
                Group = group
            };
        }

        private CommandOutcome ExecuteLocal(StableIdentity actor, GroupCommand command)
        {
            GroupCommandExecutionResult result = _processor.Execute(actor, command);
            return new CommandOutcome
            {
                Success = result.Success,
                ReasonCode = result.ReasonCode,
                Group = result.Group
            };
        }

        private bool TryReadCatalog(out GroupCatalog catalog, out string failure)
        {
            catalog = null;
            failure = "group-world-unavailable";
            string scope = CurrentWorldScope();
            if (scope.Length == 0) return false;
            GroupWorldReadResult read;
            try { read = _store.Read(scope); }
            catch { return false; }
            if (read == null || read.State == GroupWorldReadState.Unavailable)
            {
                failure = "group-store-unavailable";
                return false;
            }
            if (read.State == GroupWorldReadState.Corrupt ||
                read.State == GroupWorldReadState.EvidenceConflict)
            {
                failure = "group-store-evidence-conflict";
                return false;
            }
            catalog = read.State == GroupWorldReadState.Missing
                ? GroupCatalog.Empty
                : read.Catalog;
            if (catalog == null)
            {
                failure = "group-store-invalid";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private static bool TryResolveMemberGroup(
            GroupCatalog catalog,
            StableIdentity actor,
            string selector,
            out GroupRecord group,
            out string failure)
        {
            group = null;
            failure = "No Group with that name is available to you.";
            if (catalog == null || actor == null) return false;
            if (GroupIdentity.TryParseCanonicalId(selector, out Guid exact))
            {
                if (catalog.TryGetGroup(exact, out GroupRecord found) &&
                    found.TryGetMember(actor, out _))
                {
                    group = found;
                    failure = string.Empty;
                    return true;
                }
                return false;
            }
            List<GroupRecord> matches = catalog.Groups.Where(value =>
                    string.Equals(value.DisplayName, selector, StringComparison.OrdinalIgnoreCase) &&
                    value.TryGetMember(actor, out _))
                .Take(2)
                .ToList();
            if (matches.Count == 1)
            {
                group = matches[0];
                failure = string.Empty;
                return true;
            }
            failure = matches.Count > 1
                ? "That Group name is ambiguous; no Group was selected."
                : failure;
            return false;
        }

        private static bool TryResolveInvitation(
            GroupCatalog catalog,
            StableIdentity actor,
            string selector,
            out GroupRecord group,
            out string failure)
        {
            group = null;
            failure = "No invitation from that Group was found.";
            if (catalog == null || actor == null) return false;
            IEnumerable<GroupRecord> candidates = catalog.Groups.Where(value =>
                value.TryGetInvitation(actor, out GroupInvitation invitation) &&
                !invitation.IsExpired(DateTime.UtcNow.Ticks));
            if (GroupIdentity.TryParseCanonicalId(selector, out Guid exact))
                candidates = candidates.Where(value => value.Id == exact);
            else
                candidates = candidates.Where(value =>
                    string.Equals(value.DisplayName, selector, StringComparison.OrdinalIgnoreCase));
            List<GroupRecord> matches = candidates.Take(2).ToList();
            if (matches.Count != 1)
            {
                if (matches.Count > 1)
                    failure = "That invitation name is ambiguous; nothing was accepted.";
                return false;
            }
            group = matches[0];
            failure = string.Empty;
            return true;
        }

        private static bool TryActiveRecord(
            StableIdentity actor,
            GroupCatalog catalog,
            ActiveGroupSelection active,
            out GroupRecord group,
            out string failure)
        {
            group = null;
            failure = active.Status == ActiveGroupSelectionStatus.NoneSelected
                ? "No active Group. Use /group use <group name> first."
                : "Your active Group is unavailable. Use /group list, then /group use <group name>.";
            if (!active.IsAvailable ||
                !GroupIdentity.TryParseCanonicalId(active.GroupId, out Guid id) ||
                !catalog.TryGetGroup(id, out GroupRecord found) ||
                !found.TryGetMember(actor, out _)) return false;
            group = found;
            failure = string.Empty;
            return true;
        }

        private bool TryResolveConnectedPlayer(
            string selector,
            out StableIdentity identity,
            out string failure)
        {
            identity = null;
            failure = "No connected player has that exact name.";
            if (TryCanonicalWorldIdentity(selector, out StableIdentity exact))
            {
                identity = exact;
                failure = string.Empty;
                return true;
            }
            List<ConnectedPlayer> matches = ConnectedPlayers()
                .Where(value => string.Equals(
                    value.DisplayName, selector, StringComparison.OrdinalIgnoreCase))
                .GroupBy(value => value.Identity.CanonicalKey, StringComparer.Ordinal)
                .Select(value => value.First())
                .Take(2)
                .ToList();
            if (matches.Count == 1)
            {
                identity = matches[0].Identity;
                failure = string.Empty;
                return true;
            }
            if (matches.Count > 1)
                failure = "More than one connected player has that name; no player was chosen.";
            return false;
        }

        private List<ConnectedPlayer> ConnectedPlayers()
        {
            var result = new List<ConnectedPlayer>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                ZNet network = ZNet.instance;
                Player host = Player.m_localPlayer;
                if (network != null && network.IsServer() && host != null && host.IsOwner())
                {
                    long id = host.GetPlayerID();
                    string name = SafePlayerName(host.GetPlayerName());
                    if (id > 0 && name.Length != 0)
                    {
                        var identity = new StableIdentity(
                            "valheim.player", id.ToString(CultureInfo.InvariantCulture));
                        identities.Add(identity.CanonicalKey);
                        result.Add(new ConnectedPlayer { Identity = identity, DisplayName = name });
                    }
                }
                IReadOnlyList<RpcPeerSnapshot> peers = _rpc.GetPeers();
                for (int index = 0; index < peers.Count; index++)
                {
                    RpcPeerSnapshot peer = peers[index];
                    if (peer == null || !peer.Current || !peer.Ready ||
                        !_rpc.TryResolveActor(
                            peer,
                            RpcActorAssurance.AccountBoundPlayer,
                            out RpcActorSnapshot actor,
                            out _) || actor == null || !actor.HasVerifiedPlayerBinding ||
                        actor.ClaimedPlayerId <= 0) continue;
                    ZNetPeer networkPeer = network?.GetPeer(peer.PeerId);
                    if (networkPeer == null || networkPeer.m_uid != peer.PeerId ||
                        networkPeer.m_characterID != actor.CharacterId) continue;
                    string name = SafePlayerName(networkPeer.m_playerName);
                    if (name.Length == 0) continue;
                    var identity = new StableIdentity(
                        "valheim.player",
                        actor.ClaimedPlayerId.ToString(CultureInfo.InvariantCulture));
                    if (!identities.Add(identity.CanonicalKey)) continue;
                    result.Add(new ConnectedPlayer { Identity = identity, DisplayName = name });
                }
            }
            catch
            {
                // Partial or uncertain player evidence is omitted. Resolution requires one exact match.
            }
            return result;
        }

        private static string ConnectedLabel(
            IReadOnlyList<ConnectedPlayer> players,
            StableIdentity identity)
        {
            for (int index = 0; index < players.Count; index++)
                if (players[index].Identity.Equals(identity)) return players[index].DisplayName;
            return identity.Authority == "valheim.player"
                ? "Player " + identity.SubjectId
                : identity.CanonicalKey;
        }

        private static string SafePlayerName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0 || !name.IsNormalized(NormalizationForm.FormC) ||
                Encoding.UTF8.GetByteCount(name) > 128) return string.Empty;
            for (int index = 0; index < name.Length; index++)
                if (char.IsControl(name[index])) return string.Empty;
            return name;
        }

        private static bool TryCanonicalWorldIdentity(string value, out StableIdentity identity)
        {
            identity = null;
            const string prefix = "valheim.player:";
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string subject = value.Substring(prefix.Length);
            return long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out long id) &&
                   id > 0 && string.Equals(
                       subject, id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
                   StableIdentity.TryCreate("valheim.player", subject, out identity);
        }

        private void CompleteFriendlyResponse(
            RpcResponse response,
            Action<string> output,
            bool refreshOnly)
        {
            _activeRefreshInFlight = false;
            if (response == null || !response.Success ||
                !GroupFriendlyProtocol.TryDecodeResponse(
                    response.Payload, out GroupFriendlyResponse friendly, out _))
            {
                _activeCacheCurrent = false;
                _nextActiveRefresh = Time.realtimeSinceStartup + 2f;
                if (!refreshOnly)
                    output?.Invoke("Runic Group: request failed (" +
                                   (response?.ReasonCode ?? "no-response") + ").");
                return;
            }
            ApplyFriendlyResponse(friendly, refreshOnly ? null : output);
            _activeCacheCurrent = friendly.Active.Status != ActiveGroupSelectionStatus.Stale;
            if (!_activeCacheCurrent)
                _nextActiveRefresh = Time.realtimeSinceStartup + 2f;
        }

        private void ApplyFriendlyResponse(GroupFriendlyResponse response, Action<string> output)
        {
            if (response == null) return;
            if (TryLocalPlayerIdentity(out StableIdentity local))
                _activeGroups.SetClientCache(local, response.Active);
            if (output != null && response.Text.Length != 0) output(response.Text);
        }

        private static bool TryLocalPlayerIdentity(out StableIdentity identity)
        {
            identity = null;
            try
            {
                Player player = Player.m_localPlayer;
                long id = player == null ? 0L : player.GetPlayerID();
                return id > 0 && StableIdentity.TryCreate(
                    "valheim.player", id.ToString(CultureInfo.InvariantCulture), out identity);
            }
            catch { return false; }
        }

        private static GroupFriendlyResponse Friendly(
            string text,
            ActiveGroupSelection active) => new GroupFriendlyResponse(text, active);

        private static string ActiveLabel(ActiveGroupSelection active)
        {
            if (active.IsAvailable) return "Active Group: " + active.DisplayName + ".";
            if (active.Status == ActiveGroupSelectionStatus.NoneSelected)
                return "Active Group: none. Use /group use <group name>.";
            return "Active Group: unavailable. Use /group list, then choose it again.";
        }

        private static string SuccessLabel(
            GroupFriendlyOperation operation,
            GroupRecord group)
        {
            string name = group?.DisplayName ?? "the Group";
            switch (operation)
            {
                case GroupFriendlyOperation.Create: return "Group created: " + name + ". It is now active.";
                case GroupFriendlyOperation.Invite: return "Invitation sent for " + name + ".";
                case GroupFriendlyOperation.Accept: return "Joined " + name + ". It is now active.";
                case GroupFriendlyOperation.Leave: return "You left " + name + ".";
                case GroupFriendlyOperation.Rename: return "Group renamed to " + name + ".";
                case GroupFriendlyOperation.CancelInvitation: return "Invitation cancelled.";
                case GroupFriendlyOperation.Remove: return "Player removed from " + name + ".";
                case GroupFriendlyOperation.SetRole: return "Player role updated in " + name + ".";
                case GroupFriendlyOperation.TransferOwnership: return "Ownership of " + name + " transferred.";
                case GroupFriendlyOperation.Delete: return "Group deleted: " + name + ".";
                default: return "Group command completed.";
            }
        }

        private static string ReasonLabel(string reason)
        {
            switch (reason ?? string.Empty)
            {
                case "group-unauthorized":
                case "group-actor-unauthorized": return "you do not have permission";
                case "group-name-conflict": return "that Group name is already used";
                case "group-already-member": return "that player is already a member";
                case "group-invitation-missing": return "no matching invitation exists";
                case "group-invitation-expired": return "that invitation expired";
                case "group-capacity-reached": return "the Group capacity limit was reached";
                case "group-missing": return "the Group no longer exists";
                case "group-revision-conflict":
                case "group-catalog-revision-conflict": return "the Group changed; try again";
                default: return (reason ?? "request failed").Replace('-', ' ');
            }
        }

        private static string FriendlyHelp() =>
            "Runic Groups (private chat commands):\n" +
            "/group create <group name>\n" +
            "/group list\n" +
            "/group use <group name>\n" +
            "/group invite <connected player name>\n" +
            "/group accept <group name>\n" +
            "/group members\n" +
            "/group leave\n" +
            "/group rename <new name>\n" +
            "/group role <connected player name> <member|officer>\n" +
            "/group remove <connected player name>\n" +
            "/group transfer <connected player name>\n" +
            "/group delete";

        private static string Join(string[] args, int start, int end = -1)
        {
            if (args == null) throw new ArgumentException();
            if (end < 0) end = args.Length;
            if (start < 0 || end <= start || end > args.Length) throw new ArgumentException();
            string text = string.Join(" ", args.Skip(start).Take(end - start)).Trim();
            if (text.Length == 0) throw new ArgumentException();
            return text;
        }

        private static void RequireCount(string[] args, int count)
        {
            if (args == null || args.Length != count) throw new ArgumentException();
        }

        private static void AppendBounded(StringBuilder builder, string value)
        {
            if (builder.Length + value.Length > GroupFriendlyProtocol.MaximumResponseBytes - 512)
            {
                if (!builder.ToString().EndsWith("\n- more members omitted", StringComparison.Ordinal))
                    builder.Append("\n- more members omitted");
                return;
            }
            builder.Append(value);
        }
    }
}
