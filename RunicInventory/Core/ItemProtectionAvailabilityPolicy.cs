using System;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal enum ItemProtectionAvailability : byte
    {
        EvaluateItem = 0,
        NotApplicable = 1,
        Unknown = 2
    }

    /// <summary>
    /// Separates stable vanilla-fallback modes from short-lived states where an enabled provider
    /// may still own protection evidence. Stable fallback must not make optional consumers block
    /// ordinary gameplay merely because Runic Inventory is installed.
    /// </summary>
    internal static class ItemProtectionAvailabilityPolicy
    {
        internal static ItemProtectionAvailability Classify(
            bool featureEnabled,
            InventoryAuthorityMode mode,
            string reasonCode,
            bool disposed,
            bool playerLoadInProgress)
        {
            if (!featureEnabled || mode == InventoryAuthorityMode.Disabled)
                return ItemProtectionAvailability.NotApplicable;

            if (disposed || playerLoadInProgress)
                return ItemProtectionAvailability.Unknown;

            switch (mode)
            {
                case InventoryAuthorityMode.AuthoritativeLocal:
                    return ItemProtectionAvailability.EvaluateItem;
                case InventoryAuthorityMode.Unavailable:
                case InventoryAuthorityMode.RemoteDedicatedCompatibility:
                case InventoryAuthorityMode.BatchInert:
                    return ItemProtectionAvailability.NotApplicable;
                case InventoryAuthorityMode.MigrationSafeCompatibility:
                    if (SupportsIndependentLocks(mode, reasonCode))
                        return ItemProtectionAvailability.EvaluateItem;
                    return IsStableMigrationFallback(reasonCode)
                        ? ItemProtectionAvailability.NotApplicable
                        : ItemProtectionAvailability.Unknown;
                default:
                    return ItemProtectionAvailability.Unknown;
            }
        }

        internal static bool SupportsIndependentLocks(InventoryAuthorityMode mode, string reasonCode) =>
            mode == InventoryAuthorityMode.MigrationSafeCompatibility &&
            (reasonCode == "topology.clear-incompatible-special-row" ||
             reasonCode == "topology.equipped-item-outside-role");

        private static bool IsStableMigrationFallback(string reasonCode) =>
            string.Equals(
                reasonCode,
                "topology.clear-incompatible-special-row",
                StringComparison.Ordinal) ||
            string.Equals(
                reasonCode,
                "topology.equipped-item-outside-role",
                StringComparison.Ordinal) ||
            string.Equals(reasonCode, "topology.width-not-eight", StringComparison.Ordinal) ||
            string.Equals(reasonCode, "topology.height-too-small", StringComparison.Ordinal) ||
            string.Equals(reasonCode, "topology.slot-bound-exceeded", StringComparison.Ordinal) ||
            string.Equals(reasonCode, "topology.role-proof-failed", StringComparison.Ordinal);
    }
}
