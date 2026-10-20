namespace RunicSentinel.Runtime
{
    internal static class SentinelPlayerIdentity
    {
        // Valheim 1.0 registers the legacy PlayerID RPC but no longer sends it.
        // Resolve only the spawned character currently bound to this exact peer.
        internal static long CharacterId(ZNetPeer peer)
        {
            if (peer == null || !peer.IsReady() || peer.m_characterID == ZDOID.None || ZDOMan.instance == null)
                return 0;
            var character = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (character == null || !character.IsValid() || character.GetOwner() != peer.m_uid)
                return 0;
            return character.GetLong(ZDOVars.s_playerID, 0L);
        }
    }
}
