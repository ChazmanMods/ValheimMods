using System;
using System.Reflection;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal static class SentinelServerAdministrator
    {
        private static readonly FieldInfo AdminListField = typeof(ZNet).GetField(
            "m_adminList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool IsAdministrator(ZNet network, ZNetPeer peer, string authority, string subject)
        {
            if (network == null || !network.IsServer() ||
                !(SentinelConfig.UseServerAdminList?.Value ?? true)) return false;
            try
            {
                // A null peer is used only by the in-process host UI path, never a remote request.
                if (peer == null) return !network.IsDedicated() && !UnityEngine.Application.isBatchMode;
                if (!peer.IsReady() || peer.m_rpc == null || peer.m_socket == null) return false;
                if (SentinelAdministratorRules.MatchesSteamList(authority, subject,
                        id => (AdminListField?.GetValue(network) as SyncedList)?.Contains(id) == true))
                    return true;
                // Other backends use Valheim's server-side identity/administrator resolution.
                return network.IsAdmin(peer.m_socket.GetHostName());
            }
            catch { return false; }
        }
    }
}
