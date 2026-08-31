using System;

namespace RunicAgriculture.Core
{
    /// <summary>
    /// Distinguishes Valheim's respawning harvest component from similarly named destructive
    /// world-item pickups. Only Pickable has the availability, picked-state, skill, drop, and
    /// respawn contract used by area harvest.
    /// </summary>
    internal enum HarvestComponentContract : byte
    {
        None = 0,
        Pickable = 1,
        PickableItem = 2
    }

    internal static class HarvestPickableBatchPolicy
    {
        internal static bool Supports(HarvestComponentContract contract) =>
            contract == HarvestComponentContract.Pickable;

        internal static bool IsReady(
            bool networkViewValid,
            bool zdoIdentityValid,
            bool canBePicked,
            bool alreadyPicked,
            bool tarPreventsPicking,
            bool currentlyInTar) =>
            networkViewValid && zdoIdentityValid && canBePicked && !alreadyPicked &&
            (!tarPreventsPicking || !currentlyInTar);

        internal static bool IsExactPrefab(
            string aimedPrefabName,
            int aimedPrefabHash,
            string candidatePrefabName,
            int candidatePrefabHash) =>
            !string.IsNullOrEmpty(aimedPrefabName) &&
            aimedPrefabHash != 0 &&
            string.Equals(aimedPrefabName, candidatePrefabName, StringComparison.Ordinal) &&
            aimedPrefabHash == candidatePrefabHash;

        internal static bool OffersReplant(
            bool enabled,
            bool authorized,
            string plantPrefabName) =>
            enabled && authorized && !string.IsNullOrEmpty(plantPrefabName);

        // Bounded harvesting is a Pickable interaction feature, not a replant feature. Replant
        // settings and crop mappings must never disable harvesting wild or otherwise unmapped
        // Pickables; only the normal access check gates the area-harvest gesture.
        internal static bool AllowsAreaHarvest(bool authorized) => authorized;

        /// <summary>
        /// Aimed target first, then distance from that target, then stable ZDO identity. This
        /// makes the bounded subset independent of collider enumeration and Unity instance IDs.
        /// </summary>
        internal static int Compare(
            bool leftIsAimed,
            float leftDistanceSquared,
            long leftUserId,
            uint leftObjectId,
            bool rightIsAimed,
            float rightDistanceSquared,
            long rightUserId,
            uint rightObjectId)
        {
            if (leftIsAimed != rightIsAimed) return leftIsAimed ? -1 : 1;
            int distance = leftDistanceSquared.CompareTo(rightDistanceSquared);
            if (distance != 0) return distance;
            int user = leftUserId.CompareTo(rightUserId);
            return user != 0 ? user : leftObjectId.CompareTo(rightObjectId);
        }
    }
}
