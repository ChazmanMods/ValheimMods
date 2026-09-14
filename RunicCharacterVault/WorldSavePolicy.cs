using System;

namespace RunicCharacterVault
{
    internal static class WorldSavePolicy
    {
        internal static void Handle(
            bool isServer, Action requestCharacterCheckpoint)
        {
            if (isServer)
            {
                requestCharacterCheckpoint();
            }
        }
    }
}
