using System;

namespace RunicStorage.Engine
{
    internal static class FirstUseDiscoveryRetryPolicy
    {
        internal const int MaximumRetries = 8;
        internal const float RetryDelaySeconds = 0.2f;

        internal static bool ShouldRetry(
            int completedRetries,
            int loadedContainers,
            int loadedContainersInRange,
            int indexedContainers)
        {
            if (completedRetries < 0) throw new ArgumentOutOfRangeException(nameof(completedRetries));
            if (completedRetries >= MaximumRetries || indexedContainers > 0) return false;

            // No loaded containers at all is the normal world-entry synchronization window.
            // Containers physically in range but not yet indexed means their ZDO identity is
            // still synchronizing. A loaded world with containers only outside the configured
            // range is a genuine no-nearby-container result and must not be delayed.
            return loadedContainers <= 0 || loadedContainersInRange > 0;
        }
    }
}
