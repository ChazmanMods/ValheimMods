using System;

namespace RunicCrafting.Domain
{
    public static class DlcEligibility
    {
        public static bool IsAllowed(string dlcId, bool managerAvailable, bool installed)
        {
            if (string.IsNullOrEmpty(dlcId)) return true;
            return managerAvailable && installed;
        }
    }
}
