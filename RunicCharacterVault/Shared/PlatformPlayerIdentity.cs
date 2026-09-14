using System;
using System.Linq;

namespace RunicCharacterVault.Shared
{
    public static class PlatformPlayerIdentity
    {
        public static string Resolve(ZNet.PlayerInfo player)
        {
            string playerListId = player.m_userInfo.m_id.ToString();
            if (!string.IsNullOrWhiteSpace(playerListId))
            {
                return playerListId;
            }

            return FindPeer(player)?.m_socket?.GetHostName();
        }

        public static ZNetPeer FindPeer(ZNet.PlayerInfo player)
        {
            if (ZNet.instance == null || player.m_characterID.IsNone())
            {
                return null;
            }

            return ZNet.instance.GetPeers().FirstOrDefault(peer =>
                peer != null && peer.m_characterID.Equals(player.m_characterID));
        }
    }
}
