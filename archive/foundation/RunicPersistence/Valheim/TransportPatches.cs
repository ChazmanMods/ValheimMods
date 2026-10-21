using System;
using HarmonyLib;

namespace Runic.Foundation.Persistence
{
    /// <summary>
    /// Registration and compatibility hooks use each connection's direct ZRpc.
    /// ZRoutedRpc is used only as a lifecycle signal after vanilla admits a peer.
    /// </summary>
    internal static class TransportPatches
    {
        internal const string JotunnHarmonyId = "com.jotunn.jotunn";
        internal const string ServerSyncHarmonyId = "org.bepinex.helpers.ServerSync";
        internal const string ConditionalConfigSyncHarmonyId = "_shudnal.ConditionalConfigSync";

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(JotunnHarmonyId, ServerSyncHarmonyId, ConditionalConfigSyncHarmonyId)]
        private static bool ZNetOnNewConnectionPrefix(ZNetPeer peer) =>
            RpcTransportBoundary.RunGate(
                () => Plugin.Rpc != null && Plugin.Rpc.OnConnectionStarted(peer),
                exception => FailClosed(Plugin.Rpc, peer?.m_rpc, "on-new-connection-failed", exception));

        [HarmonyPatch(typeof(ZNet), "RPC_ServerHandshake")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(JotunnHarmonyId, ServerSyncHarmonyId, ConditionalConfigSyncHarmonyId)]
        private static void ZNetRpcServerHandshakePrefix(ZRpc rpc) =>
            RpcTransportBoundary.Run(
                () => RequireService().OnServerHandshake(rpc),
                exception => FailClosed(Plugin.Rpc, rpc, "server-handshake-failed", exception));

        [HarmonyPatch(typeof(ZNet), "RPC_ClientHandshake")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(JotunnHarmonyId, ServerSyncHarmonyId, ConditionalConfigSyncHarmonyId)]
        private static void ZNetRpcClientHandshakePrefix(ZRpc rpc) =>
            RpcTransportBoundary.Run(
                () => RequireService().OnClientHandshake(rpc),
                exception => FailClosed(Plugin.Rpc, rpc, "client-handshake-failed", exception));

        [HarmonyPatch(typeof(ZNet), "SendPeerInfo")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(JotunnHarmonyId, ServerSyncHarmonyId, ConditionalConfigSyncHarmonyId)]
        private static bool ZNetSendPeerInfoPrefix(ZRpc rpc, string password) =>
            RpcTransportBoundary.RunGate(
                () =>
                {
                    RunicRpcService service = RequireService();
                    if (service.CanSendPeerInfo(rpc, password, out string reason)) return true;
                    if (!string.Equals(reason, "runic-handshake-missing", StringComparison.Ordinal))
                    {
                        Plugin.Log?.LogWarning(
                            "Runic compatibility rejected the server before PeerInfo: " + reason + ".");
                        service.RejectProvisional(rpc, reason);
                        rpc.Invoke("Disconnect", Array.Empty<object>());
                    }
                    return false;
                },
                exception => FailClosed(Plugin.Rpc, rpc, "send-peer-info-gate-failed", exception));

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(JotunnHarmonyId, ServerSyncHarmonyId, ConditionalConfigSyncHarmonyId)]
        private static bool ZNetRpcPeerInfoPrefix(ZRpc rpc) =>
            RpcTransportBoundary.RunGate(
                () =>
                {
                    RunicRpcService service = RequireService();
                    if (service.CanReceivePeerInfo(rpc, out string reason)) return true;
                    Plugin.Log?.LogWarning(
                        "Runic compatibility rejected a client before RPC_PeerInfo world admission: " +
                        reason + ".");
                    service.RejectProvisional(rpc, reason);
                    rpc.Invoke("Error", new object[] { (int)ZNet.ConnectionStatus.ErrorVersion });
                    return false;
                },
                exception => FailClosed(Plugin.Rpc, rpc, "receive-peer-info-gate-failed", exception));

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.AddPeer))]
        [HarmonyPostfix]
        private static void RoutedRpcAddPeerPostfix(ZNetPeer peer) =>
            RpcTransportBoundary.Run(
                () => RequireService().OnPeerAdmitted(peer),
                exception => FailClosed(Plugin.Rpc, peer?.m_rpc, "peer-admission-failed", exception));

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RemovePeer))]
        [HarmonyPrefix]
        private static void RoutedRpcRemovePeerPrefix(ZNetPeer peer) =>
            RpcTransportBoundary.Run(
                () => Plugin.Rpc?.OnRoutedPeerRemoving(peer),
                exception => FailClosed(Plugin.Rpc, peer?.m_rpc, "routed-peer-cleanup-failed", exception));

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), typeof(ZNetPeer))]
        [HarmonyPrefix]
        private static void ZNetDisconnectPrefix(ZNetPeer peer) =>
            RpcTransportBoundary.Run(
                () => Plugin.Rpc?.OnConnectionClosed(peer),
                exception => FailClosed(Plugin.Rpc, peer?.m_rpc, "peer-disconnect-cleanup-failed", exception));

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPrefix]
        private static void ZNetOnDestroyPrefix() =>
            RpcTransportBoundary.Run(
                () => Plugin.Rpc?.OnNetworkStopped(),
                exception => FailClosed(Plugin.Rpc, null, "network-stop-cleanup-failed", exception));

        private static RunicRpcService RequireService() =>
            Plugin.Rpc ?? throw new InvalidOperationException("Runic RPC service is unavailable.");

        private static void FailClosed(
            RunicRpcService service,
            ZRpc rpc,
            string reason,
            Exception exception)
        {
            try { service?.FailClosed(rpc, reason, exception); }
            catch { }
            if (service != null) return;
            try { rpc?.GetSocket()?.Close(); }
            catch { }
        }
    }
}
