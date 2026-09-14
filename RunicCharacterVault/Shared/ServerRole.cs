namespace RunicCharacterVault.Shared
{
    public static class ServerRole
    {
        public static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        public static bool IsDedicatedServer => ZNet.instance != null &&
            ZNet.instance.IsServer() && ZNet.instance.IsDedicated();
    }
}
