namespace RunicCrafting.Domain
{
    internal static class SpatialMembershipPolicy
    {
        internal static bool CanRetainExistingRegistration(
            bool membershipExists,
            bool cellUnchanged,
            bool exactContainerPresent) =>
            membershipExists && cellUnchanged && exactContainerPresent;
    }
}
