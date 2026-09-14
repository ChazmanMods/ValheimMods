using System;
using System.Linq;

namespace RunicCharacterVault
{
    public static class ServerDisconnectApi
    {
        public static bool TrySaveBeforeDisconnect(
            ZRpc rpc, string reason, Action<bool> completed)
        {
            ZNetPeer peer = ZNet.instance?.GetPeers()
                .FirstOrDefault(candidate =>
                    candidate?.m_rpc != null &&
                    ReferenceEquals(candidate.m_rpc, rpc));
            if (peer == null || completed == null)
            {
                return false;
            }
            return CharacterVaultPlugin.ServerDisconnects?.TryRequest(
                peer, reason, (_, saved) => completed(saved), out _) == true;
        }
    }
}
