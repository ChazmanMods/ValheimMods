namespace RunicStorage.Runtime
{
    internal static class StorageAuthorityPolicy
    {
        internal static void Register() => RunicAutomation.ContainerAuthority.RegisterPolicy("storage",
            (chest, context, principal, sender) => PluginConfig.Enabled.Value &&
                RunicAutomation.ContainerAuthority.PlayerAccess(chest, context, principal, sender,
                    UnityEngine.Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f), false),
            chest => StorageContainerAuthority.TryGetOwnedInventory(chest, false, out _, true));
    }
}
