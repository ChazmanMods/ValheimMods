namespace RunicCharacterVault
{
    public static class GracefulShutdownApi
    {
        public static bool TryRequest() =>
            CharacterVaultPlugin.Coordinator?.TryRequestShutdown() == true;
    }
}
