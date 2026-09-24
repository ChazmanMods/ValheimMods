using System;
using HarmonyLib;
using RunicSentinel.Core;
using Steamworks;

namespace RunicSentinel.Runtime
{
    internal static class SentinelSteamSessions
    {
        private static ZNet _network;
        private static Callback<ValidateAuthTicketResponse_t> _validation;
        private static readonly SteamSessionEvidence Evidence = new SteamSessionEvidence();

        internal static SteamSessionEvidence.Entry Begin(CSteamID subject)
        {
            ZNet network = ZNet.instance;
            if (network == null || !network.IsServer()) return null;
            if (!ReferenceEquals(_network, network))
            {
                Reset();
                // Capture the network, so a queued callback cannot affect another world.
                Action<ValidateAuthTicketResponse_t> receive = response =>
                {
                    if (ReferenceEquals(_network, network) && ReferenceEquals(ZNet.instance, network))
                        Evidence.Validate(response.m_SteamID.m_SteamID,
                            response.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseOK);
                };
                _validation = network.IsDedicated()
                    ? Callback<ValidateAuthTicketResponse_t>.CreateGameServer(data => receive(data))
                    : Callback<ValidateAuthTicketResponse_t>.Create(data => receive(data));
                _network = network;
            }

            ZSteamSocket exact = null;
            int inspected = 0;
            foreach (ZNetPeer peer in network.GetPeers())
            {
                if (++inspected > 64) return null;
                if (!SentinelSocketTransport.TryResolve(peer, out ISocket transport, out _) ||
                    !(transport is ZSteamSocket socket) ||
                    socket.GetPeerID().m_SteamID != subject.m_SteamID) continue;
                if (exact != null || peer.IsReady()) return null;
                exact = socket;
            }
            if (exact == null || !SentinelTransportIdentity.TryGetHandle(exact, out HSteamNetConnection handle))
                return null;
            return Evidence.Begin(subject.m_SteamID, exact, handle.m_HSteamNetConnection);
        }

        internal static void Complete(SteamSessionEvidence.Entry entry, bool accepted) => Evidence.Complete(entry, accepted);
        internal static void End(ulong subject) => Evidence.Remove(subject);
        internal static void Close(ZSteamSocket socket) => Evidence.RemoveConnection(socket);
        internal static bool IsRejected(ulong subject) =>
            ReferenceEquals(_network, ZNet.instance) && Evidence.IsRejected(subject);
        internal static bool TryValidate(ZSteamSocket socket, HSteamNetConnection handle, ulong subject, out string reason)
        {
            reason = global::Runic.Localization.RunicText.Get("text_5f7c6989c389");
            return ReferenceEquals(_network, ZNet.instance) && _validation != null &&
                Evidence.TryValidate(subject, socket, handle.m_HSteamNetConnection, out reason);
        }
        internal static void Reset()
        {
            _network = null;
            Evidence.Clear();
            _validation?.Dispose();
            _validation = null;
        }
    }

    // Observe Valheim's original ticket verification; never start a duplicate Steam auth session,
    // alter the ticket, change the native result, or accept a client-supplied account identity.
    [HarmonyPatch(typeof(ZSteamMatchmaking), nameof(ZSteamMatchmaking.VerifySessionTicket))]
    internal static class SentinelSteamTicketPatch
    {
        private static void Prefix([HarmonyArgument(1)] CSteamID subject, out SteamSessionEvidence.Entry __state)
        {
            __state = null;
            try { __state = SentinelSteamSessions.Begin(subject); }
            catch { /* No evidence: authentication remains fail closed, native login continues. */ }
        }
        private static void Postfix(bool __result, SteamSessionEvidence.Entry __state) =>
            SentinelSteamSessions.Complete(__state, __result);
        private static Exception Finalizer(Exception __exception, SteamSessionEvidence.Entry __state)
        {
            if (__exception != null) SentinelSteamSessions.Complete(__state, false);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.Close))]
    internal static class SentinelSteamClosePatch
    {
        private static void Prefix(ZSteamSocket __instance) => SentinelSteamSessions.Close(__instance);
    }
    [HarmonyPatch(typeof(SteamGameServer), nameof(SteamGameServer.EndAuthSession))]
    internal static class SentinelSteamServerEndAuthPatch
    {
        private static void Prefix([HarmonyArgument(0)] CSteamID subject) => SentinelSteamSessions.End(subject.m_SteamID);
    }
    [HarmonyPatch(typeof(SteamUser), nameof(SteamUser.EndAuthSession))]
    internal static class SentinelSteamUserEndAuthPatch
    {
        private static void Prefix([HarmonyArgument(0)] CSteamID subject) => SentinelSteamSessions.End(subject.m_SteamID);
    }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class SentinelSteamWorldEndPatch
    {
        private static void Prefix(ZNet __instance)
        {
            if (ReferenceEquals(ZNet.instance, __instance)) SentinelSteamSessions.Reset();
        }
    }
}
