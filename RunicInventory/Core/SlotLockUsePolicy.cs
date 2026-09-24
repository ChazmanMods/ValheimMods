namespace RunicInventory.Core
{
    // Player slot locks are Quick Stack exclusions, never a veto on native actions.
    internal static class SlotLockUsePolicy
    {
        internal static bool AllowsUse(string action) => true;
    }
}
