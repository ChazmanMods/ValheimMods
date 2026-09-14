namespace RunicCharacterVault
{
    internal static class NewCharacterPolicy
    {
        internal static bool HasNeverJoinedAWorld(PlayerProfile profile)
        {
            if (profile == null || !profile.m_firstSpawn || profile.m_playerStats == null)
            {
                return false;
            }

            foreach (PlayerProfile.PlayerStats stats in profile.m_playerStats)
            {
                if (stats != null && stats.m_knownWorlds != null && stats.m_knownWorlds.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
