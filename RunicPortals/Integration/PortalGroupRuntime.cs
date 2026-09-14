using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;
using RunicPortals.Core;
using UnityEngine;

namespace RunicPortals.Integration
{
    internal readonly struct PortalGroupChoice
    {
        internal PortalGroupChoice(string groupId, string displayName)
        {
            if (!GroupIdentity.IsCanonicalId(groupId))
                throw new ArgumentException("A canonical Group UUID is required.", nameof(groupId));
            GroupId = groupId;
            DisplayName = GroupIdentity.RequireDisplayName(displayName);
        }

        internal string GroupId { get; }
        internal string DisplayName { get; }
    }

    internal sealed class PortalGroupRuntime : IDisposable, IPortalGroupMembershipResolver
    {
        private const string RequestRpc = "RunicPortals.Groups.Request.v2";
        private const string ResponseRpc = "RunicPortals.Groups.Response.v2";
        private const int WireSchema = 2;
        private const int TerminalMarker = 0x47525031;
        private const int MaximumPending = 32;
        private const int MaximumReplayEntries = 128;
        private const int MaximumEnvelopeBytes = 32768;
        private const float RefreshSeconds = 5f;
        private static readonly long RequestLifetimeTicks = TimeSpan.FromSeconds(6).Ticks;
        private static readonly long RetryTicks = TimeSpan.FromSeconds(2).Ticks;
        private static readonly long ReplayLifetimeTicks = TimeSpan.FromSeconds(30).Ticks;
        private static readonly long SnapshotLifetimeTicks = TimeSpan.FromSeconds(15).Ticks;
        private static readonly object CommandGate = new object();
        private static PortalGroupRuntime _commandRuntime;
        private static Terminal.ConsoleCommand _groupCommand;

        private sealed class Pending
        {
            internal string Id;
            internal byte[] Payload;
            internal Action<string> Output;
            internal Action<GroupFriendlyResponse> Completed;
            internal bool Silent;
            internal long Deadline;
            internal long NextAttempt;
            internal int Attempts;
        }

        private sealed class Replay
        {
            internal string Key;
            internal byte[] Request;
            internal ZPackage Response;
            internal long Expires;
        }

        private sealed class ConnectedPlayer
        {
            internal StableIdentity Identity;
            internal string Name;
        }

        private readonly ManualLogSource _log;
        private readonly CompatibleGroupWorldStore _store;
        private readonly GroupActiveSelectionService _activeGroups;
        private readonly GroupCommandProcessor _processor;
        private readonly Dictionary<string, Pending> _pending =
            new Dictionary<string, Pending>(StringComparer.Ordinal);
        private readonly Dictionary<string, Replay> _replays =
            new Dictionary<string, Replay>(StringComparer.Ordinal);
        private readonly Queue<string> _replayOrder = new Queue<string>();
        private readonly HashSet<string> _clientMemberships =
            new HashSet<string>(StringComparer.Ordinal);
        private PortalGroupChoice[] _clientGroupChoices = Array.Empty<PortalGroupChoice>();

        private ZRoutedRpc _registeredRpc;
        private ActiveGroupSelection _clientActive = ActiveGroupSelection.Stale;
        private long _clientSnapshotExpires;
        private float _nextRefresh;
        private bool _refreshPending;
        private bool _disposed;
        private bool _supportsInvitationUi;
        private GroupInvitationUi _invitations;

        internal bool SupportsInvitationUi => ZNet.instance != null &&
            (ZNet.instance.IsServer() || _supportsInvitationUi);

        internal PortalGroupRuntime(ManualLogSource log)
        {
            _log = log;
            string root = Path.Combine(Paths.ConfigPath, "RunicPermissions", "groups");
            _store = new CompatibleGroupWorldStore(root);
            _activeGroups = new GroupActiveSelectionService(
                _store,
                new FileGroupActiveSelectionStore(Path.Combine(root, "active")),
                CurrentWorldScope);
            _processor = new GroupCommandProcessor(_store, CurrentWorldScope);
        }

        internal void Initialize()
        {
            lock (CommandGate)
            {
                _commandRuntime = this;
                if (_groupCommand == null)
                    _groupCommand = new Terminal.ConsoleCommand(
                        "group",
                        "Runic Group management. Type /group help.",
                        (Terminal.ConsoleEvent)OnGroupCommand,
                        false, false, false, false, false, false, null, false, false, false);
            }
        }

        internal void Tick()
        {
            if (_disposed) return;
            ZRoutedRpc routed = ZRoutedRpc.instance;
            ZNet network = ZNet.instance;
            if (routed == null || network == null)
            {
                _invitations?.Reset();
                return;
            }
            Register(routed);
            if (!Application.isBatchMode)
            {
                if (_invitations == null) _invitations = new GroupInvitationUi(this, _log);
                _invitations.Tick();
            }
            long now = DateTime.UtcNow.Ticks;
            Expire(now);
            if (network.IsServer())
            {
                _refreshPending = false;
                return;
            }

            foreach (Pending pending in _pending.Values.ToArray())
            {
                if (now >= pending.Deadline)
                {
                    _pending.Remove(pending.Id);
                    if (!pending.Silent) pending.Output?.Invoke("Runic Group: request timed out.");
                    pending.Completed?.Invoke(null);
                    if (pending.Silent) _refreshPending = false;
                    continue;
                }
                if (pending.Attempts < 2 && now >= pending.NextAttempt)
                    SendPending(routed, network, pending, now);
            }

            if (_refreshPending || Time.realtimeSinceStartup < _nextRefresh ||
                !TryLocalIdentity(out _)) return;
            _nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
            _refreshPending = true;
            Submit(new GroupFriendlyRequest(GroupFriendlyOperation.Active), null, true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _invitations?.Reset();
            _invitations = null;
            lock (CommandGate)
                if (ReferenceEquals(_commandRuntime, this)) _commandRuntime = null;
            _pending.Clear();
            _replays.Clear();
            _replayOrder.Clear();
            _clientMemberships.Clear();
            _clientGroupChoices = Array.Empty<PortalGroupChoice>();
            _clientActive = ActiveGroupSelection.Stale;
            _registeredRpc = null;
        }

        internal bool TryIsMember(string groupId, long playerId, out bool isMember)
        {
            isMember = false;
            if (_disposed || !GroupIdentity.IsCanonicalId(groupId) || playerId == 0L)
                return false;
            StableIdentity identity = PlayerIdentity(playerId);
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (!TryReadCatalog(out GroupCatalog catalog, out _)) return false;
                if (!Guid.TryParseExact(groupId, "N", out Guid id) ||
                    !catalog.TryGetGroup(id, out GroupRecord group))
                    return true;
                isMember = group.TryGetMember(identity, out _);
                return true;
            }
            Player local = Player.m_localPlayer;
            if (local == null || local.GetPlayerID() != playerId ||
                DateTime.UtcNow.Ticks >= _clientSnapshotExpires) return false;
            isMember = _clientMemberships.Contains(groupId);
            return true;
        }

        bool IPortalGroupMembershipResolver.TryIsMember(
            string groupId,
            long playerId,
            out bool isMember) => TryIsMember(groupId, playerId, out isMember);

        internal bool TryGetActive(long playerId, out string groupId, out string displayName)
        {
            groupId = string.Empty;
            displayName = string.Empty;
            if (_disposed || playerId == 0L) return false;
            ActiveGroupSelection active;
            if (ZNet.instance != null && ZNet.instance.IsServer())
                active = _activeGroups.Resolve(PlayerIdentity(playerId));
            else
            {
                Player local = Player.m_localPlayer;
                if (local == null || local.GetPlayerID() != playerId ||
                    DateTime.UtcNow.Ticks >= _clientSnapshotExpires) return false;
                active = _clientActive;
            }
            if (active == null || !active.IsAvailable) return false;
            groupId = active.GroupId;
            displayName = active.DisplayName;
            return true;
        }

        internal bool TryGetLocalGroupChoices(out IReadOnlyList<PortalGroupChoice> choices)
        {
            choices = Array.Empty<PortalGroupChoice>();
            if (_disposed) return false;
            Player local = Player.m_localPlayer;
            long playerId = local == null ? 0L : local.GetPlayerID();
            ZNet network = ZNet.instance;
            if (playerId == 0L || network == null) return false;
            if (network.IsServer())
            {
                if (!TryMembershipChoices(PlayerIdentity(playerId), out PortalGroupChoice[] current))
                    return false;
                choices = Array.AsReadOnly(current);
                return true;
            }
            if (DateTime.UtcNow.Ticks >= _clientSnapshotExpires) return false;
            choices = Array.AsReadOnly((PortalGroupChoice[])_clientGroupChoices.Clone());
            return true;
        }

        internal void RequestLocalGroupChoiceRefresh()
        {
            if (_disposed || ZNet.instance == null || ZNet.instance.IsServer()) return;
            _nextRefresh = 0f;
        }

        private void Register(ZRoutedRpc routed)
        {
            if (ReferenceEquals(_registeredRpc, routed)) return;
            routed.Register<ZPackage>(RequestRpc, ReceiveRequest);
            routed.Register<ZPackage>(ResponseRpc, ReceiveResponse);
            _registeredRpc = routed;
            _supportsInvitationUi = false;
            _invitations?.Reset();
            _pending.Clear();
            _refreshPending = false;
            _nextRefresh = 0f;
            ClearClientSnapshot();
        }

        private void ReceiveRequest(long sender, ZPackage package)
        {
            if (_disposed || ZNet.instance == null || !ZNet.instance.IsServer() ||
                ZRoutedRpc.instance == null) return;
            if (!TryReadRequest(package, out string requestId, out byte[] payload))
            {
                SentinelSecurityBridge.Report(
                    sender, "portal-group-envelope-invalid", "portal-group-envelope", 3,
                    "The server rejected a malformed bounded Group request envelope.");
                return;
            }
            if (!TryResolvePeerIdentity(sender, out StableIdentity actor, out string identityFailure))
            {
                // Character replication may not be ready yet. Reply without disclosing group
                // data; normal readiness failures are not evidence of cheating.
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                if (peer != null && peer.IsReady() && peer.m_uid == sender)
                    SendResponse(sender, requestId, false,
                        "Character identity is not ready: " + identityFailure + ". Wait until spawned and retry.",
                        null, null, DateTime.UtcNow.Ticks);
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            Expire(now);
            string key = sender.ToString(CultureInfo.InvariantCulture) + ":" + requestId;
            if (_replays.TryGetValue(key, out Replay replay))
            {
                if (Exact(replay.Request, payload))
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, Clone(replay.Response));
                else
                {
                    SentinelSecurityBridge.Report(
                        sender, "portal-group-replay-conflict", requestId, 4,
                        "One request identity was reused with different bytes.");
                    SendResponse(sender, requestId, false, "request-id-conflict", null, actor, now);
                }
                return;
            }

            if (!GroupFriendlyProtocol.TryDecodeRequest(
                    payload, out GroupFriendlyRequest request, out string failure))
            {
                SentinelSecurityBridge.Report(
                    sender, "portal-group-payload-invalid", requestId, 3,
                    "The server rejected malformed Group command bytes.");
                SendResponse(sender, requestId, false, failure, null, actor, now);
                return;
            }
            GroupFriendlyResponse response = request.IsQuery
                ? EvaluateQuery(actor, request)
                : EvaluateMutation(actor, request);
            SendResponse(sender, requestId, true, "ok-invites-v1", response, actor, now, payload, key);
        }

        private void ReceiveResponse(long sender, ZPackage package)
        {
            if (_disposed || ZNet.instance == null || ZNet.instance.IsServer()) return;
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_uid != sender ||
                !TryReadResponse(
                    package,
                    out string requestId,
                    out bool accepted,
                    out string reason,
                    out byte[] payload,
                    out PortalGroupChoice[] memberships) ||
                !_pending.TryGetValue(requestId, out Pending pending)) return;
            _pending.Remove(requestId);
            if (pending.Silent) _refreshPending = false;
            if (!accepted || !GroupFriendlyProtocol.TryDecodeResponse(
                    payload, out GroupFriendlyResponse response, out _))
            {
                ClearClientSnapshot();
                if (!pending.Silent)
                    pending.Output?.Invoke("Runic Group: request failed (" + reason + ").");
                pending.Completed?.Invoke(null);
                return;
            }
            if (response.Active.IsAvailable && !memberships.Any(value =>
                    string.Equals(value.GroupId, response.Active.GroupId, StringComparison.Ordinal) &&
                    string.Equals(value.DisplayName, response.Active.DisplayName, StringComparison.Ordinal)))
            {
                ClearClientSnapshot();
                if (!pending.Silent)
                    pending.Output?.Invoke("Runic Group: request failed (group-snapshot-invalid).");
                pending.Completed?.Invoke(null);
                return;
            }
            _clientMemberships.Clear();
            for (int index = 0; index < memberships.Length; index++)
                _clientMemberships.Add(memberships[index].GroupId);
            _clientGroupChoices = (PortalGroupChoice[])memberships.Clone();
            _clientActive = response.Active;
            _clientSnapshotExpires = DateTime.UtcNow.Ticks + SnapshotLifetimeTicks;
            _supportsInvitationUi = string.Equals(reason, "ok-invites-v1", StringComparison.Ordinal);
            if (!pending.Silent && response.Text.Length != 0)
                pending.Output?.Invoke(response.Text);
            pending.Completed?.Invoke(response);
        }

        private void SendResponse(
            long peerId,
            string requestId,
            bool accepted,
            string reason,
            GroupFriendlyResponse response,
            StableIdentity actor,
            long now,
            byte[] requestPayload = null,
            string replayKey = null)
        {
            PortalGroupChoice[] memberships = actor == null
                ? Array.Empty<PortalGroupChoice>() : MembershipChoices(actor);
            byte[] payload = response == null
                ? Array.Empty<byte>()
                : GroupFriendlyProtocol.EncodeResponse(response);
            ZPackage package = WriteResponse(requestId, accepted, reason, payload, memberships);
            if (package.Size() > MaximumEnvelopeBytes) return;
            if (replayKey != null && requestPayload != null)
            {
                while (_replays.Count >= MaximumReplayEntries && _replayOrder.Count != 0)
                    _replays.Remove(_replayOrder.Dequeue());
                _replays[replayKey] = new Replay
                {
                    Key = replayKey,
                    Request = (byte[])requestPayload.Clone(),
                    Response = Clone(package),
                    Expires = now + ReplayLifetimeTicks
                };
                _replayOrder.Enqueue(replayKey);
            }
            ZRoutedRpc.instance?.InvokeRoutedRPC(peerId, ResponseRpc, package);
        }

        internal void RequestInvitations(Action<GroupFriendlyResponse> completed) =>
            Submit(new GroupFriendlyRequest(GroupFriendlyOperation.Invitations), null, false, completed);

        internal void RespondToInvitation(GroupInvitationChoice invitation, bool accept,
            Action<GroupFriendlyResponse> completed) =>
            Submit(new GroupFriendlyRequest(accept ? GroupFriendlyOperation.Accept : GroupFriendlyOperation.Decline,
                invitation.GroupId, invitation.Revision.ToString(CultureInfo.InvariantCulture)), null, false, completed);

        private void Submit(GroupFriendlyRequest request, Action<string> output, bool silent,
            Action<GroupFriendlyResponse> completed = null)
        {
            if (_disposed || request == null) { completed?.Invoke(null); return; }
            ZNet network = ZNet.instance;
            if (network == null)
            {
                if (!silent) output?.Invoke("Runic Group: network is unavailable.");
                completed?.Invoke(null);
                return;
            }
            if (network.IsServer())
            {
                if (!TryLocalIdentity(out StableIdentity actor))
                {
                    if (!silent) output?.Invoke("Runic Group: local player identity is unavailable.");
                    completed?.Invoke(null);
                    return;
                }
                GroupFriendlyResponse response = request.IsQuery
                    ? EvaluateQuery(actor, request)
                    : EvaluateMutation(actor, request);
                if (!silent && response.Text.Length != 0) output?.Invoke(response.Text);
                completed?.Invoke(response);
                return;
            }
            if ((request.Operation == GroupFriendlyOperation.Invitations || request.Operation == GroupFriendlyOperation.Decline) &&
                !_supportsInvitationUi)
            {
                if (!silent) output?.Invoke("Group invitation buttons and decline require RunicPortals 1.2.4 on the server. Use /group accept <group name> on older servers.");
                completed?.Invoke(null);
                return;
            }
            if (_pending.Count >= MaximumPending)
            {
                if (!silent) output?.Invoke("Runic Group: too many requests are pending.");
                completed?.Invoke(null);
                return;
            }
            string id = Guid.NewGuid().ToString("N");
            long now = DateTime.UtcNow.Ticks;
            var pending = new Pending
            {
                Id = id,
                Payload = GroupFriendlyProtocol.EncodeRequest(request),
                Output = output,
                Completed = completed,
                Silent = silent,
                Deadline = now + RequestLifetimeTicks,
                NextAttempt = now
            };
            _pending.Add(id, pending);
            if (_registeredRpc != null) SendPending(_registeredRpc, network, pending, now);
        }

        private static void SendPending(
            ZRoutedRpc routed,
            ZNet network,
            Pending pending,
            long now)
        {
            ZNetPeer server = network.GetServerPeer();
            if (server == null || !server.IsReady()) return;
            ZPackage package = WriteRequest(pending.Id, pending.Payload);
            if (package.Size() > MaximumEnvelopeBytes) return;
            routed.InvokeRoutedRPC(server.m_uid, RequestRpc, package);
            pending.Attempts++;
            pending.NextAttempt = now + RetryTicks;
        }

        private GroupFriendlyResponse EvaluateQuery(StableIdentity actor, GroupFriendlyRequest request)
        {
            ActiveGroupSelection active = _activeGroups.Resolve(actor);
            if (!TryReadCatalog(out GroupCatalog catalog, out string failure))
                return Friendly("Group information is unavailable (" + failure + ").", active);
            switch (request.Operation)
            {
                case GroupFriendlyOperation.Invitations:
                    return Friendly(GroupInvitationSnapshot.Encode(GroupInvitationSnapshot.Select(
                        catalog, actor, DateTime.UtcNow.Ticks,
                        identity => ConnectedLabel(ConnectedPlayers(), identity))), active);
                case GroupFriendlyOperation.List:
                {
                    IReadOnlyList<GroupMembership> groups = catalog.GetMemberships(actor);
                    if (groups.Count == 0)
                        return Friendly("You are not in a Group. Use /group create <name>.", active);
                    var lines = new List<string> { "Your Groups:" };
                    foreach (GroupMembership group in groups)
                        lines.Add("- " + group.DisplayName + " (" +
                                  group.Role.ToString().ToLowerInvariant() + ")" +
                                  (active.IsAvailable && active.GroupId == group.GroupIdText
                                      ? " [active]"
                                      : string.Empty));
                    return Friendly(string.Join("\n", lines), active);
                }
                case GroupFriendlyOperation.Active:
                    return Friendly(ActiveLabel(active), active);
                case GroupFriendlyOperation.WhoAmI:
                    return Friendly("Your exact Group identity is " + actor.CanonicalKey + ".", active);
                case GroupFriendlyOperation.Members:
                {
                    if (!TryActiveRecord(actor, catalog, active, out GroupRecord group, out failure))
                        return Friendly(failure, active);
                    List<ConnectedPlayer> connected = ConnectedPlayers();
                    var lines = new List<string> { "Members of " + group.DisplayName + ":" };
                    foreach (GroupMember member in group.Members)
                        lines.Add("- " + ConnectedLabel(connected, member.Identity) + " (" +
                                  member.Role.ToString().ToLowerInvariant() + ")");
                    return Friendly(string.Join("\n", lines), active);
                }
                default:
                    return Friendly("That Group query is unsupported.", active);
            }
        }

        private GroupFriendlyResponse EvaluateMutation(
            StableIdentity actor,
            GroupFriendlyRequest request)
        {
            ActiveGroupSelection active = _activeGroups.Resolve(actor);
            if (!TryReadCatalog(out GroupCatalog catalog, out string failure))
                return Friendly("Group command unavailable (" + failure + ").", active);
            if (request.Operation == GroupFriendlyOperation.Select)
            {
                if (!TryResolveMemberGroup(catalog, actor, request.Primary, out GroupRecord selected, out failure))
                    return Friendly(failure, active);
                if (!_activeGroups.TrySetAuthoritative(
                        actor, selected.Id, out active, out string setFailure))
                    return Friendly("Could not select that Group (" + setFailure + ").", active);
                return Friendly("Active Group: " + selected.DisplayName + ".", active);
            }

            GroupCommand command;
            GroupRecord target = null;
            if (request.Operation == GroupFriendlyOperation.Create)
            {
                command = new GroupCommand(
                    GroupCommandKind.Create, request.ProposedGroupId, request.Primary);
            }
            else if (request.Operation == GroupFriendlyOperation.Accept || request.Operation == GroupFriendlyOperation.Decline)
            {
                if (!TryResolveInvitation(catalog, actor, request.Primary, out target, out failure))
                    return Friendly(failure, active);
                target.TryGetInvitation(actor, out GroupInvitation invitation);
                if (!GroupInvitationSnapshot.Matches(invitation, request.Secondary, DateTime.UtcNow.Ticks))
                    return Friendly("That invitation changed or expired. Wait for the current invitation.", active);
                command = new GroupCommand(request.Operation == GroupFriendlyOperation.Accept
                    ? GroupCommandKind.Accept : GroupCommandKind.Decline, target.Id);
            }
            else
            {
                if (!TryActiveRecord(actor, catalog, active, out target, out failure))
                    return Friendly(failure, active);
                command = BuildActiveCommand(request, target, out failure);
                if (command == null) return Friendly(failure, active);
            }

            long expectedInvitationRevision = 0;
            if ((request.Operation == GroupFriendlyOperation.Accept || request.Operation == GroupFriendlyOperation.Decline) &&
                request.Secondary.Length != 0)
                long.TryParse(request.Secondary, NumberStyles.None, CultureInfo.InvariantCulture, out expectedInvitationRevision);
            GroupCommandExecutionResult result = _processor.Execute(actor, command, expectedInvitationRevision);
            if (!result.Success)
                return Friendly("Group command denied: " + ReasonLabel(result.ReasonCode) + ".", active);
            bool select = request.Operation == GroupFriendlyOperation.Create ||
                          request.Operation == GroupFriendlyOperation.Accept;
            bool clear = request.Operation == GroupFriendlyOperation.Leave ||
                         request.Operation == GroupFriendlyOperation.Delete;
            if (select)
                _activeGroups.TrySetAuthoritative(actor, command.GroupId, out active, out _);
            else if (clear)
                _activeGroups.TrySetAuthoritative(actor, Guid.Empty, out active, out _);
            else
                active = _activeGroups.Resolve(actor);
            return Friendly(SuccessLabel(request.Operation, result.Group ?? target), active);
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
                case GroupFriendlyOperation.Remove:
                case GroupFriendlyOperation.SetRole:
                case GroupFriendlyOperation.TransferOwnership:
                    if (!TryResolveConnectedPlayer(request.Primary, out StableIdentity target, out failure))
                        return null;
                    if (request.Operation == GroupFriendlyOperation.CancelInvitation)
                        return new GroupCommand(GroupCommandKind.CancelInvitation, group.Id, target: target);
                    if (request.Operation == GroupFriendlyOperation.Remove)
                        return new GroupCommand(GroupCommandKind.Remove, group.Id, target: target);
                    if (request.Operation == GroupFriendlyOperation.TransferOwnership)
                        return new GroupCommand(GroupCommandKind.TransferOwnership, group.Id, target: target);
                    return new GroupCommand(
                        GroupCommandKind.SetRole,
                        group.Id,
                        target: target,
                        role: request.Secondary == "officer" ? GroupRole.Officer : GroupRole.Member);
                case GroupFriendlyOperation.Delete:
                    return new GroupCommand(GroupCommandKind.Delete, group.Id);
                default:
                    failure = "That Group command is unsupported.";
                    return null;
            }
        }

        private bool TryReadCatalog(out GroupCatalog catalog, out string failure)
        {
            catalog = null;
            failure = "group-world-unavailable";
            string scope = CurrentWorldScope();
            if (scope.Length == 0) return false;
            GroupWorldReadResult read = _store.Read(scope);
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
            catalog = read.State == GroupWorldReadState.Missing ? GroupCatalog.Empty : read.Catalog;
            failure = catalog == null ? "group-store-invalid" : string.Empty;
            return catalog != null;
        }

        private PortalGroupChoice[] MembershipChoices(StableIdentity actor)
        {
            return TryMembershipChoices(actor, out PortalGroupChoice[] memberships)
                ? memberships
                : Array.Empty<PortalGroupChoice>();
        }

        private bool TryMembershipChoices(
            StableIdentity actor,
            out PortalGroupChoice[] memberships)
        {
            memberships = Array.Empty<PortalGroupChoice>();
            if (actor == null || !TryReadCatalog(out GroupCatalog catalog, out _)) return false;
            IReadOnlyList<GroupMembership> current = catalog.GetMemberships(actor);
            int count = Math.Min(current.Count, GroupLimits.MaximumGroupsPerIdentity);
            var snapshot = new PortalGroupChoice[count];
            for (int index = 0; index < count; index++)
                snapshot[index] = new PortalGroupChoice(
                    current[index].GroupIdText,
                    current[index].DisplayName);
            memberships = snapshot;
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
            IEnumerable<GroupRecord> matches = catalog.Groups.Where(value =>
                value.TryGetMember(actor, out _) &&
                (GroupIdentity.TryParseCanonicalId(selector, out Guid exact)
                    ? value.Id == exact
                    : string.Equals(value.DisplayName, selector, StringComparison.OrdinalIgnoreCase)));
            GroupRecord[] bounded = matches.Take(2).ToArray();
            if (bounded.Length == 1)
            {
                group = bounded[0];
                failure = string.Empty;
                return true;
            }
            if (bounded.Length > 1) failure = "That Group name is ambiguous.";
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
            failure = "No current invitation from that Group was found.";
            IEnumerable<GroupRecord> matches = catalog.Groups.Where(value =>
                value.TryGetInvitation(actor, out GroupInvitation invitation) &&
                !invitation.IsExpired(DateTime.UtcNow.Ticks) &&
                (GroupIdentity.TryParseCanonicalId(selector, out Guid exact)
                    ? value.Id == exact
                    : string.Equals(value.DisplayName, selector, StringComparison.OrdinalIgnoreCase)));
            GroupRecord[] bounded = matches.Take(2).ToArray();
            if (bounded.Length != 1) return false;
            group = bounded[0];
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
            failure = "No active Group. Use /group use <group name> first.";
            return active != null && active.IsAvailable &&
                   Guid.TryParseExact(active.GroupId, "N", out Guid id) &&
                   catalog.TryGetGroup(id, out group) && group.TryGetMember(actor, out _);
        }

        private bool TryResolveConnectedPlayer(
            string selector,
            out StableIdentity identity,
            out string failure)
        {
            identity = null;
            failure = "No connected player has that exact name.";
            if (TryCanonicalPlayerIdentity(selector, out identity))
            {
                failure = string.Empty;
                return true;
            }
            ConnectedPlayer[] matches = ConnectedPlayers()
                .Where(value => string.Equals(value.Name, selector, StringComparison.OrdinalIgnoreCase))
                .GroupBy(value => value.Identity.CanonicalKey, StringComparer.Ordinal)
                .Select(value => value.First())
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
            {
                identity = matches[0].Identity;
                failure = string.Empty;
                return true;
            }
            if (matches.Length > 1) failure = "More than one connected player has that name.";
            else if (ZNet.instance != null && ZNet.instance.IsServer() &&
                     ZNet.instance.GetPeers().Take(64).Any(peer => peer != null && peer.IsReady() &&
                         string.Equals(peer.m_playerName, selector, StringComparison.OrdinalIgnoreCase)))
                failure = "That player is connected, but their character identity is not ready. Ask them to finish spawning and retry /group whoami.";
            return false;
        }

        private static List<ConnectedPlayer> ConnectedPlayers()
        {
            var values = new List<ConnectedPlayer>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ZNet network = ZNet.instance;
            if (network == null || !network.IsServer()) return values;
            Player host = Player.m_localPlayer;
            if (host != null && host.IsOwner() && host.GetPlayerID() != 0L)
                AddConnected(values, seen, PlayerIdentity(host.GetPlayerID()), host.GetPlayerName());
            foreach (ZNetPeer peer in network.GetPeers().Take(64))
                if (peer != null && peer.IsReady() &&
                    TryResolvePeerIdentity(peer.m_uid, out StableIdentity identity))
                    AddConnected(values, seen, identity, peer.m_playerName);
            return values;
        }

        private static void AddConnected(
            ICollection<ConnectedPlayer> values,
            ISet<string> seen,
            StableIdentity identity,
            string name)
        {
            string safe = SafeName(name);
            if (identity != null && safe.Length != 0 && seen.Add(identity.CanonicalKey))
                values.Add(new ConnectedPlayer { Identity = identity, Name = safe });
        }

        private static string ConnectedLabel(
            IEnumerable<ConnectedPlayer> values,
            StableIdentity identity) =>
            values.FirstOrDefault(value => value.Identity.Equals(identity))?.Name ??
            "Player " + identity.SubjectId;

        private static bool TryResolvePeerIdentity(long sender, out StableIdentity identity)
            => TryResolvePeerIdentity(sender, out identity, out _);

        private static bool TryResolvePeerIdentity(long sender, out StableIdentity identity, out string failure)
        {
            identity = null;
            failure = "connection unavailable";
            ZNet network = ZNet.instance;
            ZNetPeer peer = network?.GetPeer(sender);
            if (network == null || !network.IsServer() || peer == null || peer.m_uid != sender ||
                !peer.IsReady() || peer.m_rpc == null || peer.m_socket == null)
                return false;
            failure = "character not spawned";
            if (peer.m_characterID.IsNone() || ZDOMan.instance == null) return false;
            ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
            failure = "character data not synchronized";
            if (character == null || !character.IsValid() || character.GetOwner() != sender ||
                ZDOMan.instance.GetZDO(peer.m_characterID) != character) return false;
            GameObject prefab = ZNetScene.instance?.GetPrefab(character.GetPrefab());
            long playerId = character.GetLong(ZDOVars.s_playerID, 0L);
            failure = "character ID or prefab unavailable";
            if (prefab == null || prefab.GetComponent<Player>() == null || playerId == 0L)
                return false;
            identity = PlayerIdentity(playerId);
            failure = string.Empty;
            return true;
        }

        private static bool TryLocalIdentity(out StableIdentity identity)
        {
            identity = null;
            Player player = Player.m_localPlayer;
            if (player == null || !player.IsOwner() || player.GetPlayerID() == 0L) return false;
            identity = PlayerIdentity(player.GetPlayerID());
            return true;
        }

        private static StableIdentity PlayerIdentity(long playerId) =>
            new StableIdentity(
                "valheim.player",
                playerId.ToString(CultureInfo.InvariantCulture));

        private static bool TryCanonicalPlayerIdentity(string value, out StableIdentity identity)
            => PortalPermissionAdapter.TryParseIdentity(value, out identity);

        private static string CurrentWorldScope()
        {
            try
            {
                ZNet network = ZNet.instance;
                if (network == null || !network.IsServer()) return string.Empty;
                long uid = network.GetWorldUID();
                return uid == 0L
                    ? string.Empty
                    : "valheim." + unchecked((ulong)uid).ToString("x16", CultureInfo.InvariantCulture);
            }
            catch { return string.Empty; }
        }

        private void Expire(long now)
        {
            while (_replayOrder.Count != 0)
            {
                string key = _replayOrder.Peek();
                if (_replays.TryGetValue(key, out Replay value) && value.Expires > now) break;
                _replayOrder.Dequeue();
                _replays.Remove(key);
            }
            if (_clientSnapshotExpires != 0L && now >= _clientSnapshotExpires)
                ClearClientSnapshot();
        }

        private void ClearClientSnapshot()
        {
            _clientMemberships.Clear();
            _clientGroupChoices = Array.Empty<PortalGroupChoice>();
            _clientActive = ActiveGroupSelection.Stale;
            _clientSnapshotExpires = 0L;
        }

        private static ZPackage WriteRequest(string id, byte[] payload)
        {
            var package = new ZPackage();
            package.Write(WireSchema);
            package.Write(id);
            package.Write(payload ?? Array.Empty<byte>());
            package.Write(TerminalMarker);
            return package;
        }

        private static bool TryReadRequest(
            ZPackage package,
            out string id,
            out byte[] payload)
        {
            id = string.Empty;
            payload = null;
            try
            {
                if (package == null || package.Size() < 1 || package.Size() > MaximumEnvelopeBytes ||
                    package.ReadInt() != WireSchema) return false;
                id = package.ReadString();
                payload = package.ReadByteArray();
                return CanonicalRequestId(id) && payload != null &&
                       payload.Length <= GroupFriendlyProtocol.MaximumRequestBytes &&
                       package.ReadInt() == TerminalMarker && package.GetPos() == package.Size();
            }
            catch { return false; }
        }

        private static ZPackage WriteResponse(
            string id,
            bool accepted,
            string reason,
            byte[] payload,
            IReadOnlyList<PortalGroupChoice> memberships)
        {
            var package = new ZPackage();
            package.Write(WireSchema);
            package.Write(id ?? string.Empty);
            package.Write(accepted);
            package.Write(BoundedReason(reason));
            package.Write(payload ?? Array.Empty<byte>());
            package.Write(memberships?.Count ?? 0);
            if (memberships != null)
                for (int index = 0; index < memberships.Count; index++)
                {
                    package.Write(memberships[index].GroupId);
                    package.Write(memberships[index].DisplayName);
                }
            package.Write(TerminalMarker);
            return package;
        }

        private static bool TryReadResponse(
            ZPackage package,
            out string id,
            out bool accepted,
            out string reason,
            out byte[] payload,
            out PortalGroupChoice[] memberships)
        {
            id = string.Empty;
            accepted = false;
            reason = string.Empty;
            payload = null;
            memberships = Array.Empty<PortalGroupChoice>();
            try
            {
                if (package == null || package.Size() < 1 || package.Size() > MaximumEnvelopeBytes ||
                    package.ReadInt() != WireSchema) return false;
                id = package.ReadString();
                accepted = package.ReadBool();
                reason = package.ReadString();
                payload = package.ReadByteArray();
                int count = package.ReadInt();
                if (!CanonicalRequestId(id) || !CanonicalReason(reason) || payload == null ||
                    payload.Length > GroupFriendlyProtocol.MaximumResponseBytes || count < 0 ||
                    count > GroupLimits.MaximumGroupsPerIdentity) return false;
                memberships = new PortalGroupChoice[count];
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < count; index++)
                {
                    string groupId = package.ReadString();
                    string displayName = package.ReadString();
                    var choice = new PortalGroupChoice(groupId, displayName);
                    if (!ids.Add(choice.GroupId) || !names.Add(choice.DisplayName)) return false;
                    memberships[index] = choice;
                }
                return package.ReadInt() == TerminalMarker && package.GetPos() == package.Size();
            }
            catch
            {
                memberships = Array.Empty<PortalGroupChoice>();
                return false;
            }
        }

        private static ZPackage Clone(ZPackage source) => new ZPackage(source.GetArray());

        private static bool Exact(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static bool CanonicalRequestId(string value) =>
            value != null && value.Length == 32 &&
            Guid.TryParseExact(value, "N", out Guid id) && id != Guid.Empty &&
            value == id.ToString("N");

        private static string BoundedReason(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "unavailable" : value.Trim();
            if (text.Length > 96) text = text.Substring(0, 96);
            var builder = new StringBuilder(text.Length);
            foreach (char current in text)
            {
                bool valid = current >= 'a' && current <= 'z' ||
                             current >= '0' && current <= '9' || current == '-' ||
                             current == '_' || current == '.';
                builder.Append(valid ? current : '-');
            }
            return builder.ToString();
        }

        private static bool CanonicalReason(string value) =>
            value != null && value.Length > 0 && value.Length <= 96 &&
            value == BoundedReason(value);

        private static GroupFriendlyResponse Friendly(
            string text,
            ActiveGroupSelection active) => new GroupFriendlyResponse(text, active);

        private static string ActiveLabel(ActiveGroupSelection active) =>
            active != null && active.IsAvailable
                ? "Active Group: " + active.DisplayName + "."
                : "Active Group: none. Use /group use <group name>.";

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
                case GroupFriendlyOperation.Decline: return "Declined invitation to " + name + ".";
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

        private static string ReasonLabel(string reason) =>
            string.IsNullOrEmpty(reason) ? "unavailable" : reason.Replace('-', ' ');

        private static string SafeName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0 || !name.IsNormalized(NormalizationForm.FormC) ||
                Encoding.UTF8.GetByteCount(name) > 128) return string.Empty;
            for (int index = 0; index < name.Length; index++)
                if (char.IsControl(name[index])) return string.Empty;
            return name;
        }

        private static void OnGroupCommand(Terminal.ConsoleEventArgs args)
        {
            PortalGroupRuntime runtime;
            lock (CommandGate) runtime = _commandRuntime;
            if (runtime == null || runtime._disposed)
            {
                args?.Context?.AddString("Runic Group: service unavailable.");
                return;
            }
            if (!TryParseCommand(args?.Args, out GroupFriendlyRequest request, out string message))
            {
                args?.Context?.AddString(message);
                return;
            }
            runtime.Submit(request, args.Context.AddString, false);
        }

        private static bool TryParseCommand(
            string[] args,
            out GroupFriendlyRequest request,
            out string message)
        {
            request = null;
            args = args ?? Array.Empty<string>();
            string verb = args.Length > 1 ? (args[1] ?? string.Empty).Trim().ToLowerInvariant() : "help";
            try
            {
                switch (verb)
                {
                    case "help":
                        message = "Group commands: create, list, use, active, members, whoami, invite, accept, decline, leave, rename, cancel, remove, role, transfer, delete.";
                        return false;
                    case "list": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.List); break;
                    case "active": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.Active); break;
                    case "members": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.Members); break;
                    case "whoami": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.WhoAmI); break;
                    case "create": request = new GroupFriendlyRequest(GroupFriendlyOperation.Create, Join(args, 2), proposedGroupId: Guid.NewGuid()); break;
                    case "use":
                    case "select": request = new GroupFriendlyRequest(GroupFriendlyOperation.Select, Join(args, 2)); break;
                    case "invite": request = new GroupFriendlyRequest(GroupFriendlyOperation.Invite, Join(args, 2), number: 168); break;
                    case "accept": request = new GroupFriendlyRequest(GroupFriendlyOperation.Accept, Join(args, 2)); break;
                    case "decline": request = new GroupFriendlyRequest(GroupFriendlyOperation.Decline, Join(args, 2)); break;
                    case "leave": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.Leave); break;
                    case "rename": request = new GroupFriendlyRequest(GroupFriendlyOperation.Rename, Join(args, 2)); break;
                    case "cancel": request = new GroupFriendlyRequest(GroupFriendlyOperation.CancelInvitation, Join(args, 2)); break;
                    case "remove": request = new GroupFriendlyRequest(GroupFriendlyOperation.Remove, Join(args, 2)); break;
                    case "role":
                        if (args.Length < 4) throw new ArgumentException();
                        request = new GroupFriendlyRequest(
                            GroupFriendlyOperation.SetRole,
                            Join(args, 2, args.Length - 1),
                            (args[args.Length - 1] ?? string.Empty).Trim().ToLowerInvariant());
                        break;
                    case "transfer": request = new GroupFriendlyRequest(GroupFriendlyOperation.TransferOwnership, Join(args, 2)); break;
                    case "delete": RequireCount(args, 2); request = new GroupFriendlyRequest(GroupFriendlyOperation.Delete); break;
                    default:
                        message = "Unknown Group command. Type /group help.";
                        return false;
                }
                message = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is OverflowException)
            {
                request = null;
                message = "That Group command is incomplete or invalid. Type /group help.";
                return false;
            }
        }

        private static void RequireCount(string[] args, int count)
        {
            if (args.Length != count) throw new ArgumentException();
        }

        private static string Join(string[] args, int start, int end = -1)
        {
            if (end < 0) end = args.Length;
            if (start >= end) throw new ArgumentException();
            string value = string.Join(" ", args, start, end - start).Trim();
            if (value.Length == 0) throw new ArgumentException();
            return value;
        }
    }
}
