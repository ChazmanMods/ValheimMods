using System.Collections.Generic;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal enum ItemProtectionDomainEvidence : byte
    {
        NotApplicable = 0,
        InDomainUnknown = 1,
        ExactCurrentMember = 2
    }

    /// <summary>
    /// Bounded identity-only domain proof. It says nothing about lock state: one exact
    /// current member may proceed to topology validation; absence is NotApplicable; duplicate or
    /// oversized/unavailable membership evidence remains in-domain Unknown.
    /// </summary>
    internal static class ItemProtectionDomain
    {
        internal static ItemProtectionDomainEvidence Evaluate<T>(
            IReadOnlyList<T> governedItems,
            T candidate,
            int maximumItems)
            where T : class
        {
            if (candidate == null) return ItemProtectionDomainEvidence.NotApplicable;
            if (governedItems == null || maximumItems < 0 || governedItems.Count > maximumItems)
                return ItemProtectionDomainEvidence.InDomainUnknown;

            int exactReferences = 0;
            for (int index = 0; index < governedItems.Count; index++)
                if (ReferenceEquals(governedItems[index], candidate)) exactReferences++;
            if (exactReferences == 0) return ItemProtectionDomainEvidence.NotApplicable;
            return exactReferences == 1
                ? ItemProtectionDomainEvidence.ExactCurrentMember
                : ItemProtectionDomainEvidence.InDomainUnknown;
        }

        /// <summary>
        /// Final classification after the runtime has already proved one exact, stable member,
        /// authoritative topology, canonical coordinate, unique occupancy, and unchanged native
        /// references. Keeping the final two-fact rule pure makes the healthy Locked/Unlocked
        /// contract directly regression-testable without constructing Unity Player ownership.
        /// </summary>
        internal static ItemProtectionState ClassifyExactMember(
            bool occupiesSpecialRow,
            bool explicitlyLocked) =>
            occupiesSpecialRow || explicitlyLocked
                ? ItemProtectionState.Locked
                : ItemProtectionState.Unlocked;
    }
}
