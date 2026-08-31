using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Runic.Foundation.Core;

namespace Runic.Foundation.Persistence
{
    internal static class PlayerIdentityBindingRuntime
    {
        internal static PlayerIdentityBindingStore Create(ManualLogSource log)
        {
            string root = Path.Combine(
                Paths.ConfigPath,
                "RunicPersistence",
                "player-bindings");
            return new PlayerIdentityBindingStore(
                GetCurrentWorldScope,
                new FilePlayerIdentityBindingStorage(root),
                log);
        }

        internal static string GetCurrentWorldScope()
        {
            try
            {
                ZNet network = ZNet.instance;
                if (network == null || !network.IsServer()) return string.Empty;
                long worldUid = network.GetWorldUID();
                if (worldUid == 0) return string.Empty;
                return "valheim." + unchecked((ulong)worldUid).ToString("x16", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// The command entry point accepts only the exact process-local F5 console. A connected client
    /// sends its bounded administrative request over the direct session RPC; the server requires a
    /// canonical Steam admin requester and binds only the exact current BackendAccount target and
    /// its transport-owned Player ZDO. Enrollment is never triggered by Resolve.
    /// </summary>
    internal static class PlayerIdentityBindingCommands
    {
        internal const string CommandName = "runic_bind";
        internal const string QueryEndpointId = "runic.persistence.binding.admin-query";
        internal const string EnrollEndpointId = "runic.persistence.binding.admin-enroll";
        internal const string RevokePlayerEndpointId = "runic.persistence.binding.admin-revoke-player";
        internal const int MaximumQueryResponseBytes = 128 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly RpcEndpointDescriptor QueryEndpoint = new RpcEndpointDescriptor(
            Plugin.ModuleId,
            QueryEndpointId,
            RunicCapabilityIds.ActorIdentityBinding,
            1,
            RpcEndpointDirection.ClientToServer,
            RpcOperationKind.ReadOnly,
            RpcReplayDurability.SessionOnly,
            RpcIdentityAssurance.BackendAccount,
            MaximumQueryResponseBytes);
        private static readonly RpcEndpointDescriptor EnrollEndpoint = new RpcEndpointDescriptor(
            Plugin.ModuleId,
            EnrollEndpointId,
            RunicCapabilityIds.ActorIdentityBinding,
            1,
            RpcEndpointDirection.ClientToServer,
            RpcOperationKind.Mutation,
            RpcReplayDurability.SessionOnly,
            RpcIdentityAssurance.BackendAccount,
            8);
        private static readonly RpcEndpointDescriptor RevokePlayerEndpoint = new RpcEndpointDescriptor(
            Plugin.ModuleId,
            RevokePlayerEndpointId,
            RunicCapabilityIds.ActorIdentityBinding,
            1,
            RpcEndpointDirection.ClientToServer,
            RpcOperationKind.Mutation,
            RpcReplayDurability.SessionOnly,
            RpcIdentityAssurance.BackendAccount,
            8);

        private static readonly object Gate = new object();
        private static Terminal.ConsoleCommand _command;
        private static PlayerIdentityBindingStore _store;
        private static IRunicRpcService _rpc;
        private static ModuleRegistration _module;
        private static ManualLogSource _log;

        internal static IDisposable ConfigureRpc(
            ModuleRegistration module,
            IRunicRpcService rpc,
            PlayerIdentityBindingStore store,
            ManualLogSource log)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (rpc == null) throw new ArgumentNullException(nameof(rpc));
            if (store == null) throw new ArgumentNullException(nameof(store));
            var registrations = new List<IDisposable>();
            try
            {
                registrations.Add(rpc.RegisterEndpoint(
                    module,
                    QueryEndpoint,
                    request => HandleAdminQuery(request, rpc, store, log)));
                registrations.Add(rpc.RegisterEndpoint(
                    module,
                    EnrollEndpoint,
                    request => HandleAdminEnroll(request, rpc, store, log)));
                registrations.Add(rpc.RegisterEndpoint(
                    module,
                    RevokePlayerEndpoint,
                    request => HandleAdminRevokePlayer(request, rpc, store, log)));
                return new CompositeLease(registrations);
            }
            catch
            {
                for (int index = registrations.Count - 1; index >= 0; index--)
                    try { registrations[index].Dispose(); }
                    catch { }
                throw;
            }
        }

        internal static void Attach(
            PlayerIdentityBindingStore store,
            IRunicRpcService rpc,
            ModuleRegistration module,
            ManualLogSource log)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (rpc == null) throw new ArgumentNullException(nameof(rpc));
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (Gate)
            {
                _store = store;
                _rpc = rpc;
                _module = module;
                _log = log;
                if (_command != null) return;
                _command = new Terminal.ConsoleCommand(
                    CommandName,
                    "Runic account binding: status | peers | enroll <peerUid> | list | revoke-account <authority> <subject> | revoke-player <playerId> | reload",
                    (Terminal.ConsoleEvent)OnCommand,
                    false,
                    false,
                    false,
                    false,
                    false,
                    null,
                    false,
                    false,
                    false);
            }
        }

        internal static void Detach(PlayerIdentityBindingStore store)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_store, store)) return;
                _store = null;
                _rpc = null;
                _module = null;
                _log = null;
            }
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (!IsExactProcessLocalConsole(args))
                {
                    args?.Context?.AddString(
                        "Runic binding: this command requires the exact local F5 console.");
                    return;
                }
                PlayerIdentityBindingStore store;
                IRunicRpcService rpc;
                ModuleRegistration module;
                lock (Gate)
                {
                    store = _store;
                    rpc = _rpc;
                    module = _module;
                }
                if (store == null || rpc == null || module == null)
                {
                    args.Context.AddString("Runic binding: service unavailable.");
                    return;
                }
                Execute(store, rpc, module, args.Args, args.Context.AddString);
            }
            catch (Exception exception)
            {
                try { args?.Context?.AddString("Runic binding: command failed closed."); }
                catch { }
                try { _log?.LogWarning("Runic binding command failed closed: " + exception.GetType().Name); }
                catch { }
            }
        }

        internal static void Execute(
            PlayerIdentityBindingStore store,
            IRunicRpcService rpc,
            ModuleRegistration module,
            string[] args,
            Action<string> output)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (rpc == null) throw new ArgumentNullException(nameof(rpc));
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (output == null) throw new ArgumentNullException(nameof(output));
            args = args ?? Array.Empty<string>();
            string verb = args != null && args.Length > 1
                ? (args[1] ?? string.Empty).Trim().ToLowerInvariant()
                : "status";
            switch (verb)
            {
                case "status":
                    if (!rpc.IsServer)
                    {
                        SendQuery(rpc, module, BindingQueryKind.Status, 0, output);
                        return;
                    }
                    output(
                        "Runic binding: world=" +
                        (store.WorldScope.Length == 0 ? "none" : store.WorldScope) +
                        "; state=" + store.State.ToString().ToLowerInvariant() +
                        "; reason=" + store.StateReason +
                        "; count=" + store.List().Count.ToString(CultureInfo.InvariantCulture) + ".");
                    return;
                case "peers":
                    if (!rpc.IsServer)
                    {
                        SendQuery(rpc, module, BindingQueryKind.Peers, 0, output);
                        return;
                    }
                    ShowPeers(rpc, output);
                    return;
                case "enroll":
                    if (!rpc.IsServer)
                    {
                        SendPeerMutation(rpc, module, EnrollEndpointId, args, output);
                        return;
                    }
                    EnrollPeer(store, rpc, args, output);
                    return;
                case "list":
                    if (!rpc.IsServer)
                    {
                        int offset = 0;
                        if (args.Length > 2 &&
                            (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out offset) ||
                             offset < 0))
                        {
                            output("Usage: runic_bind list [offset]");
                            return;
                        }
                        SendQuery(rpc, module, BindingQueryKind.Bindings, offset, output);
                        return;
                    }
                    ShowBindings(store, output);
                    return;
                case "revoke-account":
                    if (!rpc.IsServer)
                    {
                        output("Runic binding: remote revoke-account is disabled; use revoke-player with the verified list entry.");
                        return;
                    }
                    if (args.Length != 4)
                    {
                        output("Usage: runic_bind revoke-account <authority> <subject>");
                        return;
                    }
                    Report(
                        store.RevokeIdentity(args[2], args[3]),
                        output);
                    return;
                case "revoke-player":
                    if (args.Length != 3 || !TryNonZeroInt64(args[2], out long revokePlayerId))
                    {
                        output("Usage: runic_bind revoke-player <playerId>");
                        return;
                    }
                    if (!rpc.IsServer)
                    {
                        SendMutation(
                            rpc,
                            module,
                            RevokePlayerEndpointId,
                            EncodeInt64(revokePlayerId),
                            output);
                        return;
                    }
                    Report(store.RevokePlayer(revokePlayerId), output);
                    return;
                case "reload":
                    if (!rpc.IsServer)
                    {
                        output("Runic binding: reload is available only in the server process.");
                        return;
                    }
                    output(store.Reload(out string reason)
                        ? "Runic binding: reloaded (" + reason + ")."
                        : "Runic binding: reload failed closed (" + reason + ").");
                    return;
                default:
                    output(
                        "Usage: runic_bind status | peers | enroll <peerUid> | list | " +
                        "revoke-account <authority> <subject> | revoke-player <playerId> | reload");
                    return;
            }
        }

        internal static RpcHandlerResult HandleAdminQuery(
            RpcRequestContext request,
            IRunicRpcService rpc,
            PlayerIdentityBindingStore store,
            ManualLogSource log)
        {
            if (!TryAuthorizeSteamAdmin(request, rpc, out RpcActorSnapshot requester, out string denial))
                return RpcHandlerResult.Deny(denial);
            byte[] payload = request.Payload;
            if (payload.Length != 5 || !Enum.IsDefined(typeof(BindingQueryKind), payload[0]))
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, "binding-query-invalid");
            int offset = DecodeInt32(payload, 1);
            if (offset < 0)
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, "binding-query-offset-invalid");

            var lines = new List<string>();
            switch ((BindingQueryKind)payload[0])
            {
                case BindingQueryKind.Status:
                    lines.Add(
                        "world=" + (store.WorldScope.Length == 0 ? "none" : store.WorldScope) +
                        "; state=" + store.State.ToString().ToLowerInvariant() +
                        "; reason=" + store.StateReason +
                        "; count=" + store.List().Count.ToString(CultureInfo.InvariantCulture));
                    break;
                case BindingQueryKind.Peers:
                    foreach (RpcPeerSnapshot peer in rpc.GetPeers().Take(RunicRpcService.MaximumSessions))
                    {
                        string actorText = "actor-unavailable";
                        if (rpc.TryResolveActor(
                                peer,
                                RpcActorAssurance.TransportOwnedCharacter,
                                out RpcActorSnapshot actor,
                                out string actorReason))
                            actorText = "claimed-player=" +
                                        actor.ClaimedPlayerId.ToString(CultureInfo.InvariantCulture);
                        else
                            actorText += "(" + actorReason + ")";
                        lines.Add(
                            "peer=" + peer.PeerId.ToString(CultureInfo.InvariantCulture) +
                            "; session=" + peer.SessionId +
                            "; identity=" + FormatIdentity(peer.Identity) +
                            "; " + actorText);
                    }
                    if (lines.Count == 0) lines.Add("no-admitted-direct-peers");
                    break;
                case BindingQueryKind.Bindings:
                    IReadOnlyList<PlayerIdentityBindingSnapshot> bindings = store.List();
                    const int pageSize = 64;
                    if (offset > bindings.Count) offset = bindings.Count;
                    foreach (PlayerIdentityBindingSnapshot binding in
                             bindings.Skip(offset).Take(pageSize))
                    {
                        lines.Add(
                            binding.Authority + ":" + binding.SubjectId + " -> " +
                            binding.PlayerId.ToString(CultureInfo.InvariantCulture));
                    }
                    lines.Insert(
                        0,
                        "bindings offset=" + offset.ToString(CultureInfo.InvariantCulture) +
                        " returned=" + (lines.Count).ToString(CultureInfo.InvariantCulture) +
                        " total=" + bindings.Count.ToString(CultureInfo.InvariantCulture) +
                        (offset + pageSize < bindings.Count
                            ? "; next=" + (offset + pageSize).ToString(CultureInfo.InvariantCulture)
                            : string.Empty));
                    break;
            }

            byte[] response;
            try { response = StrictUtf8.GetBytes(string.Join("\n", lines)); }
            catch { return new RpcHandlerResult(RpcResultCode.HandlerFailed, "binding-query-encode-failed"); }
            if (response.Length > MaximumQueryResponseBytes)
                return new RpcHandlerResult(RpcResultCode.CapacityReached, "binding-query-response-too-large");
            Audit(log, requester.Peer.Identity, "query-" + ((BindingQueryKind)payload[0]).ToString().ToLowerInvariant(), "ok");
            return RpcHandlerResult.Ok(response);
        }

        internal static RpcHandlerResult HandleAdminEnroll(
            RpcRequestContext request,
            IRunicRpcService rpc,
            PlayerIdentityBindingStore store,
            ManualLogSource log)
        {
            if (!TryAuthorizeSteamAdmin(request, rpc, out RpcActorSnapshot requester, out string denial))
                return RpcHandlerResult.Deny(denial);
            if (!TryDecodeInt64(request.Payload, out long targetPeerId) || targetPeerId == 0)
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, "binding-target-invalid");
            if (!TryResolveCurrentTarget(
                    rpc,
                    targetPeerId,
                    out RpcPeerSnapshot targetPeer,
                    out RpcActorSnapshot targetActor,
                    out string targetReason))
                return new RpcHandlerResult(RpcResultCode.NotReady, targetReason);

            // A second exact-session/actor/admin pass is the commit barrier. Valheim dispatches
            // this synchronous handler on its network-update thread; no asynchronous yield occurs
            // between this barrier and the atomic file replacement below.
            if (!TryAuthorizeSteamAdmin(request, rpc, out requester, out denial))
                return RpcHandlerResult.Deny(denial);
            if (!TryResolveCurrentTarget(
                    rpc,
                    targetPeerId,
                    out RpcPeerSnapshot currentTarget,
                    out RpcActorSnapshot currentActor,
                    out targetReason) ||
                !SameTarget(targetPeer, targetActor, currentTarget, currentActor))
                return new RpcHandlerResult(RpcResultCode.NotReady, "binding-target-session-changed");

            PlayerIdentityBindingMutationResult result = store.Enroll(
                currentTarget.Identity,
                currentActor.ClaimedPlayerId);
            Audit(
                log,
                requester.Peer.Identity,
                "enroll " + FormatIdentity(currentTarget.Identity) + " -> " +
                currentActor.ClaimedPlayerId.ToString(CultureInfo.InvariantCulture),
                result.ReasonCode);
            return BindingMutationResponse(result);
        }

        internal static RpcHandlerResult HandleAdminRevokePlayer(
            RpcRequestContext request,
            IRunicRpcService rpc,
            PlayerIdentityBindingStore store,
            ManualLogSource log)
        {
            if (!TryAuthorizeSteamAdmin(request, rpc, out RpcActorSnapshot requester, out string denial))
                return RpcHandlerResult.Deny(denial);
            if (!TryDecodeInt64(request.Payload, out long playerId) || playerId == 0)
                return new RpcHandlerResult(RpcResultCode.InvalidRequest, "binding-player-id-invalid");
            if (!TryAuthorizeSteamAdmin(request, rpc, out requester, out denial))
                return RpcHandlerResult.Deny(denial);
            PlayerIdentityBindingMutationResult result = store.RevokePlayer(playerId);
            Audit(
                log,
                requester.Peer.Identity,
                "revoke-player " + playerId.ToString(CultureInfo.InvariantCulture),
                result.ReasonCode);
            return BindingMutationResponse(result);
        }

        private static bool TryAuthorizeSteamAdmin(
            RpcRequestContext request,
            IRunicRpcService rpc,
            out RpcActorSnapshot requester,
            out string reasonCode)
        {
            requester = null;
            reasonCode = "binding-admin-required";
            if (request == null || rpc == null || !request.ReceiverIsServer ||
                !request.IsConnectionCurrent || request.Peer == null ||
                !IsCanonicalSteamBackendIdentity(request.Peer.Identity))
                return false;
            if (!rpc.TryResolveActor(
                    request.Peer,
                    RpcActorAssurance.TransportOwnedCharacter,
                    out requester,
                    out _))
            {
                requester = null;
                reasonCode = "binding-admin-actor-required";
                return false;
            }
            if (!requester.IsServerAdmin ||
                requester.Peer == null ||
                !string.Equals(
                    requester.Peer.SessionId,
                    request.Peer.SessionId,
                    StringComparison.Ordinal))
            {
                requester = null;
                reasonCode = "binding-admin-required";
                return false;
            }
            reasonCode = "binding-admin-authorized";
            return true;
        }

        private static bool TryResolveCurrentTarget(
            IRunicRpcService rpc,
            long targetPeerId,
            out RpcPeerSnapshot peer,
            out RpcActorSnapshot actor,
            out string reasonCode)
        {
            peer = null;
            actor = null;
            reasonCode = "binding-target-unavailable";
            if (!rpc.TryGetPeer(targetPeerId, out peer) || peer == null ||
                !peer.Current || !peer.Ready || peer.Identity == null ||
                peer.Identity.Assurance < RpcIdentityAssurance.BackendAccount)
                return false;
            string expectedSession = peer.SessionId;
            if (!rpc.TryResolveActor(
                    peer,
                    RpcActorAssurance.TransportOwnedCharacter,
                    out actor,
                    out reasonCode) || actor == null || actor.ClaimedPlayerId == 0)
                return false;
            if (!rpc.TryGetPeer(targetPeerId, out RpcPeerSnapshot rechecked) ||
                rechecked == null || !rechecked.Current || !rechecked.Ready ||
                !string.Equals(expectedSession, rechecked.SessionId, StringComparison.Ordinal) ||
                !SameIdentity(peer.Identity, rechecked.Identity))
            {
                peer = null;
                actor = null;
                reasonCode = "binding-target-session-changed";
                return false;
            }
            peer = rechecked;
            reasonCode = "binding-target-resolved";
            return true;
        }

        private static bool SameTarget(
            RpcPeerSnapshot firstPeer,
            RpcActorSnapshot firstActor,
            RpcPeerSnapshot secondPeer,
            RpcActorSnapshot secondActor) =>
            firstPeer != null && firstActor != null && secondPeer != null && secondActor != null &&
            firstPeer.PeerId == secondPeer.PeerId &&
            string.Equals(firstPeer.SessionId, secondPeer.SessionId, StringComparison.Ordinal) &&
            SameIdentity(firstPeer.Identity, secondPeer.Identity) &&
            firstActor.CharacterId.Equals(secondActor.CharacterId) &&
            firstActor.ClaimedPlayerId == secondActor.ClaimedPlayerId;

        private static bool SameIdentity(RpcPeerIdentity left, RpcPeerIdentity right) =>
            left != null && right != null &&
            left.Assurance == right.Assurance &&
            string.Equals(left.Authority, right.Authority, StringComparison.Ordinal) &&
            string.Equals(left.SubjectId, right.SubjectId, StringComparison.Ordinal);

        internal static bool IsCanonicalSteamBackendIdentity(RpcPeerIdentity identity) =>
            RunicRpcService.IsEligibleSteamAdminIdentity(identity);

        private static RpcHandlerResult BindingMutationResponse(
            PlayerIdentityBindingMutationResult result) =>
            result.Success
                ? new RpcHandlerResult(RpcResultCode.Success, result.ReasonCode)
                : new RpcHandlerResult(
                    result.ReasonCode == "binding-capacity-reached"
                        ? RpcResultCode.CapacityReached
                        : RpcResultCode.HandlerFailed,
                    result.ReasonCode);

        private static void SendQuery(
            IRunicRpcService rpc,
            ModuleRegistration module,
            BindingQueryKind kind,
            int offset,
            Action<string> output)
        {
            byte[] payload = new byte[5];
            payload[0] = (byte)kind;
            EncodeInt32(offset, payload, 1);
            Send(
                rpc.SendToServer(
                    module,
                    QueryEndpointId,
                    payload,
                    string.Empty,
                    TimeSpan.FromSeconds(5),
                    response => ReportResponse(response, output)),
                output);
        }

        private static void SendPeerMutation(
            IRunicRpcService rpc,
            ModuleRegistration module,
            string endpointId,
            string[] args,
            Action<string> output)
        {
            if (args == null || args.Length != 3 || !TryNonZeroInt64(args[2], out long peerUid))
            {
                output("Usage: runic_bind enroll <peerUid>");
                return;
            }
            SendMutation(rpc, module, endpointId, EncodeInt64(peerUid), output);
        }

        private static void SendMutation(
            IRunicRpcService rpc,
            ModuleRegistration module,
            string endpointId,
            byte[] payload,
            Action<string> output)
        {
            Send(
                rpc.SendToServer(
                    module,
                    endpointId,
                    payload,
                    Guid.NewGuid().ToString("N"),
                    TimeSpan.FromSeconds(5),
                    response => ReportResponse(response, output)),
                output);
        }

        private static void Send(RpcSendResult result, Action<string> output)
        {
            if (result == null || !result.Accepted)
                output("Runic binding: request rejected locally (" +
                       (result?.ReasonCode ?? "send-unavailable") + ").");
            else
                output("Runic binding: request sent to the authoritative server.");
        }

        private static void ReportResponse(RpcResponse response, Action<string> output)
        {
            try
            {
                if (response == null)
                {
                    output("Runic binding: server response unavailable.");
                    return;
                }
                if (!response.Success)
                {
                    output("Runic binding: server refused request (" + response.ReasonCode + ").");
                    return;
                }
                byte[] payload = response.Payload;
                if (payload.Length == 0)
                {
                    output("Runic binding: server committed request (" + response.ReasonCode + ").");
                    return;
                }
                string text = StrictUtf8.GetString(payload);
                foreach (string line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    output("Runic binding: " + line);
            }
            catch
            {
                try { output("Runic binding: malformed server response."); }
                catch { }
            }
        }

        private static void Audit(
            ManualLogSource log,
            RpcPeerIdentity requester,
            string action,
            string result)
        {
            try
            {
                log?.LogInfo(
                    "Runic binding admin audit: requester=" + FormatIdentity(requester) +
                    "; action=" + action + "; result=" + result + ".");
            }
            catch { }
        }

        private static string FormatIdentity(RpcPeerIdentity identity) =>
            identity == null
                ? "none"
                : identity.Authority + ":" + identity.SubjectId +
                  "[" + identity.Assurance.ToString() + "]";

        internal static byte[] EncodeInt64(long value)
        {
            var bytes = new byte[8];
            unchecked
            {
                ulong raw = (ulong)value;
                for (int index = 0; index < bytes.Length; index++)
                    bytes[index] = (byte)(raw >> (index * 8));
            }
            return bytes;
        }

        private static bool TryDecodeInt64(byte[] bytes, out long value)
        {
            value = 0;
            if (bytes == null || bytes.Length != 8) return false;
            ulong raw = 0;
            for (int index = 0; index < bytes.Length; index++)
                raw |= (ulong)bytes[index] << (index * 8);
            value = unchecked((long)raw);
            return true;
        }

        private static void EncodeInt32(int value, byte[] destination, int offset)
        {
            unchecked
            {
                uint raw = (uint)value;
                for (int index = 0; index < 4; index++)
                    destination[offset + index] = (byte)(raw >> (index * 8));
            }
        }

        private static int DecodeInt32(byte[] source, int offset)
        {
            uint raw = 0;
            for (int index = 0; index < 4; index++)
                raw |= (uint)source[offset + index] << (index * 8);
            return unchecked((int)raw);
        }

        private static void EnrollPeer(
            PlayerIdentityBindingStore store,
            IRunicRpcService rpc,
            string[] args,
            Action<string> output)
        {
            if (args == null || args.Length != 3 || !TryNonZeroInt64(args[2], out long peerUid))
            {
                output("Usage: runic_bind enroll <peerUid>");
                return;
            }
            if (!rpc.TryGetPeer(peerUid, out RpcPeerSnapshot peer) || peer == null || !peer.Current)
            {
                output("Runic binding: current direct peer not found.");
                return;
            }
            if (peer.Identity == null ||
                peer.Identity.Assurance < RpcIdentityAssurance.BackendAccount)
            {
                output("Runic binding: peer has no authenticated backend-account identity.");
                return;
            }
            if (!rpc.TryResolveActor(
                    peer,
                    RpcActorAssurance.TransportOwnedCharacter,
                    out RpcActorSnapshot actor,
                    out string actorReason))
            {
                output("Runic binding: current transport-owned actor unavailable (" + actorReason + ").");
                return;
            }
            PlayerIdentityBindingMutationResult result = store.Enroll(
                peer.Identity,
                actor.ClaimedPlayerId);
            Report(result, output);
        }

        private static void ShowPeers(IRunicRpcService rpc, Action<string> output)
        {
            IReadOnlyList<RpcPeerSnapshot> peers = rpc.GetPeers();
            if (peers.Count == 0)
            {
                output("Runic binding: no admitted direct peers.");
                return;
            }
            foreach (RpcPeerSnapshot peer in peers)
            {
                string actorText = "actor=unavailable";
                if (rpc.TryResolveActor(
                        peer,
                        RpcActorAssurance.TransportOwnedCharacter,
                        out RpcActorSnapshot actor,
                        out string actorReason))
                    actorText = "claimed-player=" +
                                actor.ClaimedPlayerId.ToString(CultureInfo.InvariantCulture);
                else
                    actorText += "(" + actorReason + ")";
                string identity = peer.Identity == null
                    ? "none"
                    : peer.Identity.Authority + ":" + peer.Identity.SubjectId +
                      "[" + peer.Identity.Assurance.ToString() + "]";
                output(
                    "peer=" + peer.PeerId.ToString(CultureInfo.InvariantCulture) +
                    "; identity=" + identity + "; " + actorText + ".");
            }
        }

        private static void ShowBindings(
            PlayerIdentityBindingStore store,
            Action<string> output)
        {
            IReadOnlyList<PlayerIdentityBindingSnapshot> bindings = store.List();
            if (bindings.Count == 0)
            {
                output("Runic binding: no verified bindings in the active world.");
                return;
            }
            foreach (PlayerIdentityBindingSnapshot binding in bindings)
            {
                output(
                    binding.Authority + ":" + binding.SubjectId + " -> " +
                    binding.PlayerId.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void Report(
            PlayerIdentityBindingMutationResult result,
            Action<string> output)
        {
            output(
                "Runic binding: " + result.ReasonCode +
                (result.Success ? (result.Changed ? " (committed)." : " (unchanged).") : " (failed closed)."));
        }

        private static bool TryNonZeroInt64(string value, out long parsed) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed != 0;

        private static bool IsExactProcessLocalConsole(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (args == null || args.Context == null) return false;
                return IsExactProcessConsoleContext(args.Context, Console.instance);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsExactProcessConsoleContext(object commandContext, object processConsole) =>
            processConsole != null && ReferenceEquals(commandContext, processConsole);

        internal enum BindingQueryKind : byte
        {
            Status = 1,
            Peers = 2,
            Bindings = 3
        }

        private sealed class CompositeLease : IDisposable
        {
            private List<IDisposable> _registrations;

            internal CompositeLease(List<IDisposable> registrations)
            {
                _registrations = registrations;
            }

            public void Dispose()
            {
                List<IDisposable> registrations = _registrations;
                if (registrations == null) return;
                _registrations = null;
                for (int index = registrations.Count - 1; index >= 0; index--)
                {
                    try { registrations[index].Dispose(); }
                    catch { }
                }
            }
        }
    }
}
