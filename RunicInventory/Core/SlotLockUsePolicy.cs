namespace RunicInventory.Core
{
    // Slot retention is not a ban on gameplay consumption. Only explicit native use
    // paths opt in; transfers, offerings, disposal, and unknown actions stay guarded.
    internal static class SlotLockUsePolicy
    {
        internal static bool AllowsUse(string action) =>
            action == "using it" || action == "cooking it" ||
            action == "processing it" || action == "fermenting it";
    }
}
