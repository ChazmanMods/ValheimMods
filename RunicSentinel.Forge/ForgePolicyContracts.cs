namespace RunicSentinel.Contracts
{
    // The offline Forge links the canonical policy parser without loading BepInEx, Valheim, or any
    // gameplay assembly. Keep the numeric values identical to Sentinel's public contract.
    internal enum PluginClassification
    {
        Unknown = 0,
        Required = 1,
        ApprovedOptional = 2,
        ServerOnly = 3,
        Forbidden = 4,
        Unmanaged = 5,
        AdministratorOnly = 6,
        Quarantined = 7
    }
}
