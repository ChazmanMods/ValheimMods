using System;

namespace RunicProduction.Core
{
    // Only explicit link/unlink gestures use this path. Background automation never claims objects.
    internal static class ProductionSetupOwnership
    {
        internal static bool TryAcquire(
            Func<bool> accessStillValid,
            Func<bool> ownsStation, Action claimStation,
            Func<bool> ownsChest, Action claimChest)
        {
            if (!accessStillValid()) return false;
            if (!ownsStation()) claimStation();
            if (!ownsStation() || !accessStillValid()) return false;
            if (!ownsChest()) claimChest();
            return ownsStation() && ownsChest() && accessStillValid();
        }
    }
}
