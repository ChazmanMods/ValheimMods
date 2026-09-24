namespace RunicCrafting.Integration
{
    internal static class CraftingAuthorityPolicy
    {
        internal static void Register() => RunicAutomation.ContainerAuthority.RegisterPolicy("crafting",
            (chest, context, principal, sender) => Configuration.Enabled.Value &&
                (!Configuration.ExcludePersonalContainers.Value || chest.m_privacy != Container.PrivacySetting.Private) &&
                RunicAutomation.ContainerAuthority.PlayerAccess(chest, context, principal, sender,
                    Configuration.SafeRangeCap, Configuration.RequireWardAccess.Value),
            chest => ValheimReflection.RefreshOwnedContainer(chest, ValheimReflection.GetView(chest)?.GetZDO()));
    }
}
