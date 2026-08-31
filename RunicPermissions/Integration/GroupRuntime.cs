using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;

namespace RunicPermissions.Integration
{
    internal sealed partial class GroupRuntime : IDisposable
    {
        internal const string QueryEndpointId = "runic.permissions.groups.query";
        internal const string PrepareEndpointId = "runic.permissions.groups.prepare";
        internal const string CommandEndpointId = "runic.permissions.groups.command";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly object ConsoleGate = new object();
        private static GroupRuntime _consoleRuntime;
        private static Terminal.ConsoleCommand _consoleCommand;

        private readonly ModuleRegistration _module;
        private readonly IRunicRpcService _rpc;
        private readonly FileGroupWorldStore _store;
        private readonly GroupActiveSelectionService _activeGroups;
        private readonly GroupCommandProcessor _processor;
        private readonly ManualLogSource _log;
        private readonly List<IDisposable> _leases = new List<IDisposable>();
        private bool _disposed;

        internal GroupRuntime(
            ModuleRegistration module,
            IRunicRpcService rpc,
            FileGroupWorldStore store,
            GroupActiveSelectionService activeGroups,
            ManualLogSource log)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _activeGroups = activeGroups ?? throw new ArgumentNullException(nameof(activeGroups));
            _log = log;
            if (!_module.IsActive)
                throw new InvalidOperationException("Group RPC requires an active module lease.");
            _processor = new GroupCommandProcessor(_store, CurrentWorldScope);

            _leases.Add(_rpc.RegisterEndpoint(
                _module,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    QueryEndpointId,
                    GroupCapabilities.Membership,
                    GroupCapabilities.ProtocolVersion,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.ReadOnly,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.BackendAccount,
                    GroupQueryProtocol.MaximumWireBytes),
                HandleQuery));
            _leases.Add(_rpc.RegisterEndpoint(
                _module,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    PrepareEndpointId,
                    GroupCapabilities.Membership,
                    GroupCapabilities.ProtocolVersion,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.SessionOnly,
                    RpcIdentityAssurance.BackendAccount,
                    GroupCommandCodec.MaximumPayloadBytes),
                HandlePrepare));
            _leases.Add(_rpc.RegisterEndpoint(
                _module,
                new RpcEndpointDescriptor(
                    Plugin.ModuleId,
                    CommandEndpointId,
                    GroupCapabilities.Membership,
                    GroupCapabilities.ProtocolVersion,
                    RpcEndpointDirection.ClientToServer,
                    RpcOperationKind.Mutation,
                    RpcReplayDurability.HandlerDurable,
                    RpcIdentityAssurance.BackendAccount,
                    GroupCommandDurableCodec.MaximumExecutionBytes),
                HandleCommand));
            RegisterFriendlyEndpoints();
            _leases.Add(_rpc.RegisterPeerRequirement(
                _module,
                new RpcPeerRequirement(
                    Plugin.ModuleId,
                    Plugin.Version,
                    Plugin.Version,
                    GroupCapabilities.ProtocolVersion,
                    GroupCapabilities.ProtocolVersion)));
            AttachFriendlyRuntime();
            AttachConsole(this);
        }

        internal static string CurrentWorldScope()
        {
            try
            {
                ZNet network = ZNet.instance;
                if (network == null || !network.IsServer()) return string.Empty;
                long uid = network.GetWorldUID();
                return uid == 0
                    ? string.Empty
                    : "valheim." + unchecked((ulong)uid).ToString("x16", CultureInfo.InvariantCulture);
            }
            catch { return string.Empty; }
        }

        internal static FileGroupWorldStore CreateStore() => new FileGroupWorldStore(
            Path.Combine(Paths.ConfigPath, "RunicPermissions", "groups"));

        internal static FileGroupActiveSelectionStore CreateActiveStore() =>
            new FileGroupActiveSelectionStore(
                Path.Combine(Paths.ConfigPath, "RunicPermissions", "groups", "active"));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachConsole(this);
            DetachFriendlyRuntime();
            for (int index = _leases.Count - 1; index >= 0; index--)
            {
                try { _leases[index]?.Dispose(); }
                catch { }
            }
            _leases.Clear();
        }

        private RpcHandlerResult HandleQuery(RpcRequestContext request)
        {
            if (!TryRequestActor(request, out StableIdentity actor, out string denial))
                return RpcHandlerResult.Deny(denial);
            if (!GroupQueryProtocol.TryDecodeRequest(
                    request.Payload, out byte kind, out Guid queryGroupId, out string queryFailure))
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, queryFailure);
            string scope = CurrentWorldScope();
            if (scope.Length == 0)
                return new RpcHandlerResult(RpcResultCode.NotReady, "group-world-unavailable");
            GroupWorldReadResult read;
            try { read = _store.Read(scope); }
            catch { return new RpcHandlerResult(RpcResultCode.NotReady, "group-store-unavailable"); }
            if (read == null || read.State == GroupWorldReadState.Corrupt ||
                read.State == GroupWorldReadState.EvidenceConflict)
                return new RpcHandlerResult(RpcResultCode.NotReady, "group-store-evidence-conflict");
            if (read.State == GroupWorldReadState.Unavailable)
                return new RpcHandlerResult(RpcResultCode.NotReady, "group-store-unavailable");
            GroupCatalog catalog = read.State == GroupWorldReadState.Missing
                ? GroupCatalog.Empty
                : read.Catalog;
            if (catalog == null)
                return new RpcHandlerResult(RpcResultCode.HandlerFailed, "group-store-invalid");

            string text;
            if (kind == 3)
            {
                text = actor.CanonicalKey;
            }
            else if (kind == 1)
            {
                IReadOnlyList<GroupMembership> memberships = catalog.GetMemberships(actor);
                text = memberships.Count == 0
                    ? "No Group memberships."
                    : string.Join("\n", memberships.Select(value =>
                        value.GroupIdText + " | " + value.DisplayName + " | " +
                        value.Role.ToString().ToLowerInvariant() + " | revision=" +
                        value.GroupRevision.ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                Guid id = queryGroupId;
                if (id == Guid.Empty || !catalog.TryGetGroup(id, out GroupRecord group))
                    return new RpcHandlerResult(RpcResultCode.NotFound, "group-missing");
                if (!group.TryGetMember(actor, out GroupMember viewer))
                    return RpcHandlerResult.Deny("group-viewer-not-member");
                var lines = new List<string>
                {
                    group.IdText + " | " + group.DisplayName + " | revision=" +
                    group.Revision.ToString(CultureInfo.InvariantCulture)
                };
                lines.AddRange(group.Members.Select(value =>
                    "member " + value.Identity.CanonicalKey + " | " +
                    value.Role.ToString().ToLowerInvariant()));
                if (viewer.Role >= GroupRole.Officer)
                    lines.AddRange(group.Invitations.Select(value =>
                        "invite " + value.Invitee.CanonicalKey + " | expires=" +
                        new DateTime(value.ExpiresUtcTicks, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)));
                text = string.Join("\n", lines);
            }
            return EncodeResponse(text);
        }

        private RpcHandlerResult HandlePrepare(RpcRequestContext request)
        {
            if (!TryRequestActor(request, out StableIdentity actor, out string denial))
                return RpcHandlerResult.Deny(denial);
            if (!GroupCommandCodec.TryDecode(
                    request.Payload, out GroupCommand command, out string failure))
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, failure);
            if (command.Target != null && !IsWorldPlayerIdentity(command.Target))
                return new RpcHandlerResult(
                    RpcResultCode.InvalidRequest, "group-target-player-identity-required");
            string scope = CurrentWorldScope();
            if (scope.Length == 0)
                return new RpcHandlerResult(RpcResultCode.NotReady, "group-world-unavailable");
            if (!_store.TryIssueCommand(
                    scope,
                    actor,
                    command,
                    DateTime.UtcNow.Ticks,
                    out GroupCommandIssue issue,
                    out string issueFailure))
                return new RpcHandlerResult(
                    string.Equals(issueFailure, "group-command-issue-capacity", StringComparison.Ordinal)
                        ? RpcResultCode.CapacityReached
                        : RpcResultCode.NotReady,
                    issueFailure);
            return RpcHandlerResult.Ok(GroupCommandDurableCodec.EncodeIssue(
                new GroupCommandIssueResponse(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision)));
        }

        private RpcHandlerResult HandleCommand(RpcRequestContext request)
        {
            if (!TryRequestActor(request, out StableIdentity actor, out string denial))
                return RpcHandlerResult.Deny(denial);
            if (!GroupCommandDurableCodec.TryDecodeExecution(
                    request.Payload, out GroupCommandExecution execution, out string failure))
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, failure);
            if (!string.Equals(request.IdempotencyKey, execution.Token, StringComparison.Ordinal))
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, "group-idempotency-mismatch");
            if (execution.Command.Target != null &&
                !IsWorldPlayerIdentity(execution.Command.Target))
                return new RpcHandlerResult(
                    RpcResultCode.InvalidRequest, "group-target-player-identity-required");
            string scope = CurrentWorldScope();
            if (scope.Length == 0)
                return new RpcHandlerResult(RpcResultCode.NotReady, "group-world-unavailable");
            if (!_store.TryExecuteIssuedCommand(
                    scope,
                    actor,
                    execution,
                    DateTime.UtcNow.Ticks,
                    out GroupCommandReceipt receipt,
                    out string executionFailure) || receipt == null)
                return new RpcHandlerResult(
                    string.Equals(executionFailure, "group-command-token-conflict", StringComparison.Ordinal)
                        ? RpcResultCode.ReplayConflict
                        : string.Equals(executionFailure, "group-command-receipt-capacity", StringComparison.Ordinal)
                            ? RpcResultCode.CapacityReached
                            : RpcResultCode.NotReady,
                    executionFailure);
            string response = receipt.ReasonCode + " | catalog=" +
                              receipt.CatalogRevision.ToString(CultureInfo.InvariantCulture) +
                              " | group-revision=" +
                              receipt.GroupRevision.ToString(CultureInfo.InvariantCulture);
            if (!receipt.Success)
            {
                RpcResultCode code = receipt.Code == GroupMutationCode.CapacityReached
                    ? RpcResultCode.CapacityReached
                    : receipt.Code == GroupMutationCode.RevisionConflict
                        ? RpcResultCode.NotReady
                        : receipt.Code == GroupMutationCode.GroupMissing
                            ? RpcResultCode.NotFound
                            : receipt.Code == GroupMutationCode.Unauthorized
                                ? RpcResultCode.Unauthorized
                                : RpcResultCode.InvalidRequest;
                return new RpcHandlerResult(code, receipt.ReasonCode, EncodeText(response));
            }
            Audit(actor, execution.Command, receipt);
            return RpcHandlerResult.Ok(EncodeText(response));
        }

        private bool TryRequestActor(
            RpcRequestContext request,
            out StableIdentity actor,
            out string denial)
        {
            actor = null;
            denial = "group-request-invalid";
            if (request == null || !request.ReceiverIsServer || !request.IsConnectionCurrent)
            {
                denial = "group-stale-session";
                return false;
            }
            if (DateTime.UtcNow.Ticks > request.DeadlineUtcTicks)
            {
                denial = "group-request-expired";
                return false;
            }
            RpcPeerIdentity identity = request.Peer?.Identity;
            if (identity == null || identity.Assurance != RpcIdentityAssurance.BackendAccount)
            {
                denial = "group-backend-identity-required";
                return false;
            }
            if (!_rpc.TryResolveActor(
                    request.Peer,
                    RpcActorAssurance.AccountBoundPlayer,
                    out RpcActorSnapshot verified,
                    out denial) ||
                verified == null || !verified.HasVerifiedPlayerBinding ||
                verified.ClaimedPlayerId == 0 ||
                !StableIdentity.TryCreate(
                    "valheim.player",
                    verified.ClaimedPlayerId.ToString(CultureInfo.InvariantCulture),
                    out actor))
            {
                if (string.IsNullOrEmpty(denial)) denial = "group-player-binding-required";
                return false;
            }
            denial = "ok";
            return true;
        }

        private static void AttachConsole(GroupRuntime runtime)
        {
            lock (ConsoleGate)
            {
                _consoleRuntime = runtime;
                if (_consoleCommand != null) return;
                _consoleCommand = new Terminal.ConsoleCommand(
                    "runic_group",
                    "Runic Group: whoami | list | show | create | rename | invite | cancel | accept | leave | remove | role | transfer | delete",
                    (Terminal.ConsoleEvent)OnConsoleCommand,
                    false, false, false, false, false, null, false, false, false);
                AttachFriendlyCommand();
            }
        }

        private static void DetachConsole(GroupRuntime runtime)
        {
            lock (ConsoleGate)
                if (ReferenceEquals(_consoleRuntime, runtime)) _consoleRuntime = null;
        }

        private static void OnConsoleCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (args?.Context == null ||
                    !ReferenceEquals(args.Context, Console.instance) &&
                    !ReferenceEquals(args.Context, Chat.instance))
                {
                    args?.Context?.AddString("Runic Group: use the local chat or F5 panel.");
                    return;
                }
                GroupRuntime runtime;
                lock (ConsoleGate) runtime = _consoleRuntime;
                if (runtime == null || runtime._disposed)
                {
                    args.Context.AddString("Runic Group: service unavailable.");
                    return;
                }
                runtime.ExecuteConsole(args.Args, args.Context.AddString);
            }
            catch (Exception exception)
            {
                try { args?.Context?.AddString("Runic Group: command failed closed."); }
                catch { }
                try { _consoleRuntime?._log?.LogWarning("Group command failed closed: " + exception.GetType().Name); }
                catch { }
            }
        }

        private void ExecuteConsole(string[] args, Action<string> output)
        {
            args = args ?? Array.Empty<string>();
            string verb = args.Length > 1 ? (args[1] ?? string.Empty).Trim().ToLowerInvariant() : "list";
            if (verb == "whoami")
            {
                SendOrExecuteQuery(new byte[] { 3 }, output);
                return;
            }
            if (verb == "list")
            {
                SendOrExecuteQuery(new byte[] { 1 }, output);
                return;
            }
            if (verb == "show")
            {
                if (args.Length != 3 || !GroupIdentity.TryParseCanonicalId(args[2], out Guid showId))
                {
                    output("Usage: runic_group show <groupId>");
                    return;
                }
                byte[] query = new byte[17];
                query[0] = 2;
                Buffer.BlockCopy(showId.ToByteArray(), 0, query, 1, 16);
                SendOrExecuteQuery(query, output);
                return;
            }
            if (!TryParseCommand(args, out GroupCommand command, out string usage))
            {
                output(usage);
                return;
            }
            if (_rpc.IsServer)
            {
                if (!TryLocalActor(out StableIdentity actor))
                {
                    output("Runic Group: local server player identity is unavailable.");
                    return;
                }
                ReportLocal(_processor.Execute(actor, command), output);
                return;
            }
            byte[] payload = GroupCommandCodec.Encode(command);
            RpcSendResult sent = _rpc.SendToServer(
                _module,
                PrepareEndpointId,
                payload,
                "group-prepare-" + Guid.NewGuid().ToString("N"),
                TimeSpan.FromSeconds(5),
                response => CompletePrepare(command, response, output));
            ReportSend(sent, output);
        }

        private void CompletePrepare(
            GroupCommand command,
            RpcResponse response,
            Action<string> output)
        {
            if (response == null || !response.Success ||
                !GroupCommandDurableCodec.TryDecodeIssue(
                    response.Payload, out GroupCommandIssueResponse issue, out _))
            {
                ReportResponse(response, output);
                return;
            }
            byte[] execution = GroupCommandDurableCodec.EncodeExecution(
                new GroupCommandExecution(
                    issue.Token,
                    issue.ExpectedCatalogRevision,
                    issue.ExpectedGroupRevision,
                    command));
            RpcSendResult sent = _rpc.SendToServer(
                _module,
                CommandEndpointId,
                execution,
                issue.Token,
                TimeSpan.FromSeconds(5),
                final => ReportResponse(final, output));
            ReportSend(sent, output);
        }

        private void SendOrExecuteQuery(byte[] payload, Action<string> output)
        {
            if (_rpc.IsServer)
            {
                if (!TryLocalActor(out StableIdentity actor))
                {
                    output("Runic Group: local server player identity is unavailable.");
                    return;
                }
                ReportLocalQuery(payload, actor, output);
                return;
            }
            RpcSendResult sent = _rpc.SendToServer(
                _module,
                QueryEndpointId,
                payload,
                string.Empty,
                TimeSpan.FromSeconds(5),
                response => ReportResponse(response, output));
            ReportSend(sent, output);
        }

        private void ReportLocalQuery(byte[] payload, StableIdentity actor, Action<string> output)
        {
            string scope = CurrentWorldScope();
            GroupWorldReadResult read = scope.Length == 0 ? null : _store.Read(scope);
            GroupCatalog catalog = read?.State == GroupWorldReadState.Ready
                ? read.Catalog
                : read?.State == GroupWorldReadState.Missing ? GroupCatalog.Empty : null;
            if (catalog == null)
            {
                output("Runic Group: catalog unavailable or conflicting.");
                return;
            }
            if (payload[0] == 3)
            {
                output(actor.CanonicalKey);
                return;
            }
            if (payload[0] == 1)
            {
                IReadOnlyList<GroupMembership> values = catalog.GetMemberships(actor);
                output(values.Count == 0
                    ? "No Group memberships."
                    : string.Join("\n", values.Select(value =>
                        value.GroupIdText + " | " + value.DisplayName + " | " +
                        value.Role.ToString().ToLowerInvariant())));
                return;
            }
            var id = new Guid(payload.Skip(1).Take(16).ToArray());
            if (!catalog.TryGetGroup(id, out GroupRecord group) || !group.TryGetMember(actor, out _))
            {
                output("Runic Group: group missing or access denied.");
                return;
            }
            output(group.IdText + " | " + group.DisplayName + "\n" + string.Join("\n",
                group.Members.Select(value =>
                    "member " + value.Identity.CanonicalKey + " | " +
                    value.Role.ToString().ToLowerInvariant())));
        }

        private static bool TryParseCommand(
            string[] args,
            out GroupCommand command,
            out string usage)
        {
            command = null;
            usage = "Runic Group: invalid command.";
            if (args == null || args.Length < 2) return false;
            string verb = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
            try
            {
                if (verb == "create" && args.Length >= 3)
                    command = new GroupCommand(
                        GroupCommandKind.Create, Guid.NewGuid(), string.Join(" ", args.Skip(2)));
                else if (verb == "rename" && args.Length >= 4 && ParseId(args[2], out Guid renameId))
                    command = new GroupCommand(
                        GroupCommandKind.Rename, renameId, string.Join(" ", args.Skip(3)));
                else if (verb == "invite" && (args.Length == 5 || args.Length == 6) &&
                         ParseId(args[2], out Guid inviteId) &&
                         TryWorldPlayerIdentity(args[3], args[4], out StableIdentity invitee))
                {
                    int hours = 168;
                    if (args.Length == 6 && (!int.TryParse(args[5], NumberStyles.None,
                            CultureInfo.InvariantCulture, out hours) || hours < 1 || hours > 720))
                        throw new ArgumentException();
                    command = new GroupCommand(
                        GroupCommandKind.Invite, inviteId, target: invitee,
                        invitationExpiresUtcTicks: DateTime.UtcNow.AddHours(hours).Ticks);
                }
                else if (verb == "cancel" && args.Length == 5 &&
                         ParseId(args[2], out Guid cancelId) &&
                         TryWorldPlayerIdentity(args[3], args[4], out StableIdentity cancelTarget))
                    command = new GroupCommand(
                        GroupCommandKind.CancelInvitation, cancelId, target: cancelTarget);
                else if (verb == "accept" && args.Length == 3 && ParseId(args[2], out Guid acceptId))
                    command = new GroupCommand(GroupCommandKind.Accept, acceptId);
                else if (verb == "leave" && args.Length == 3 && ParseId(args[2], out Guid leaveId))
                    command = new GroupCommand(GroupCommandKind.Leave, leaveId);
                else if ((verb == "remove" || verb == "transfer") && args.Length == 5 &&
                         ParseId(args[2], out Guid targetGroup) &&
                         TryWorldPlayerIdentity(args[3], args[4], out StableIdentity target))
                    command = new GroupCommand(
                        verb == "remove" ? GroupCommandKind.Remove : GroupCommandKind.TransferOwnership,
                        targetGroup, target: target);
                else if (verb == "role" && args.Length == 6 && ParseId(args[2], out Guid roleGroup) &&
                         TryWorldPlayerIdentity(args[3], args[4], out StableIdentity roleTarget) &&
                         TryRole(args[5], out GroupRole role))
                    command = new GroupCommand(
                        GroupCommandKind.SetRole, roleGroup, target: roleTarget, role: role);
                else if (verb == "delete" && args.Length == 3 && ParseId(args[2], out Guid deleteId))
                    command = new GroupCommand(GroupCommandKind.Delete, deleteId);
                else
                    return false;
                usage = string.Empty;
                return command != null;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is OverflowException)
            {
                usage = "Runic Group: invalid or out-of-range command value.";
                command = null;
                return false;
            }
        }

        private static bool ParseId(string value, out Guid id) =>
            GroupIdentity.TryParseCanonicalId(value, out id);

        private static bool TryWorldPlayerIdentity(
            string authority,
            string subject,
            out StableIdentity identity)
        {
            identity = null;
            if (!StableIdentity.TryCreate(authority, subject, out StableIdentity parsed) ||
                !IsWorldPlayerIdentity(parsed))
                return false;
            identity = parsed;
            return true;
        }

        private static bool IsWorldPlayerIdentity(StableIdentity identity)
        {
            if (identity == null ||
                !string.Equals(identity.Authority, "valheim.player", StringComparison.Ordinal) ||
                !long.TryParse(identity.SubjectId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long playerId) || playerId <= 0L)
                return false;
            return string.Equals(
                identity.SubjectId,
                playerId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        private static bool TryRole(string value, out GroupRole role)
        {
            if (string.Equals(value, "member", StringComparison.OrdinalIgnoreCase))
            {
                role = GroupRole.Member;
                return true;
            }
            if (string.Equals(value, "officer", StringComparison.OrdinalIgnoreCase))
            {
                role = GroupRole.Officer;
                return true;
            }
            role = GroupRole.Member;
            return false;
        }

        private static bool TryLocalActor(out StableIdentity actor)
        {
            actor = null;
            try
            {
                ZNet network = ZNet.instance;
                Player player = Player.m_localPlayer;
                if (network == null || !network.IsServer() || player == null || !player.IsOwner())
                    return false;
                long playerId = player.GetPlayerID();
                return playerId != 0 && StableIdentity.TryCreate(
                    "valheim.player",
                    playerId.ToString(CultureInfo.InvariantCulture),
                    out actor);
            }
            catch { return false; }
        }

        private static string ComputePayloadKey(byte[] payload)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(payload ?? Array.Empty<byte>()))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static RpcHandlerResult EncodeResponse(string text) =>
            RpcHandlerResult.Ok(GroupQueryProtocol.EncodeResponse(text));

        private static byte[] EncodeText(string text) =>
            GroupQueryProtocol.EncodeResponse(text);

        private static void ReportSend(RpcSendResult sent, Action<string> output)
        {
            output(sent != null && sent.Accepted
                ? "Runic Group: request sent to the authoritative server."
                : "Runic Group: request rejected locally (" +
                  (sent?.ReasonCode ?? "send-unavailable") + ").");
        }

        private static void ReportResponse(RpcResponse response, Action<string> output)
        {
            string text;
            try { text = StrictUtf8.GetString(response?.Payload ?? Array.Empty<byte>()); }
            catch { text = string.Empty; }
            output("Runic Group: " +
                   (response == null ? "no response" : response.Code.ToString()) +
                   " (" + (response?.ReasonCode ?? "response-missing") + ")" +
                   (text.Length == 0 ? string.Empty : "\n" + text));
        }

        private static void ReportLocal(GroupCommandExecutionResult result, Action<string> output) =>
            output("Runic Group: " + result.ReasonCode +
                   (result.Group == null ? string.Empty : " | " + result.Group.IdText));

        private void Audit(
            StableIdentity actor,
            GroupCommand command,
            GroupCommandReceipt receipt)
        {
            try
            {
                _log?.LogInfo(
                    "Group mutation: actor=" + actor.CanonicalKey +
                    "; operation=" + command.Kind.ToString().ToLowerInvariant() +
                    "; group=" + command.GroupId.ToString("N") +
                    "; token=" + receipt.Token +
                    "; result=" + receipt.ReasonCode + ".");
            }
            catch { }
        }
    }
}
