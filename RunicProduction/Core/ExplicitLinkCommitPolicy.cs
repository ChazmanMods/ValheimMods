namespace RunicProduction.Core
{
    /// <summary>
    /// Final admission boundary for a two-stage explicit link. Station interaction reach is
    /// proven when the local selection begins; the later container interaction must
    /// not require the player to remain within ordinary use range of both endpoints.
    /// </summary>
    internal static class ExplicitLinkCommitPolicy
    {
        internal static bool Allows(
            bool hasMatchingUnexpiredServerSelection,
            bool actorCanReachSelectedContainer,
            bool stationToContainerWithinConfiguredRange) =>
            hasMatchingUnexpiredServerSelection &&
            actorCanReachSelectedContainer &&
            stationToContainerWithinConfiguredRange;
    }
}
