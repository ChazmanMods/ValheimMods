using HarmonyLib;
using UnityEngine;

namespace Ghostbuster
{
    // The server's original SetSector notification runs before m_position is assigned.
    // Keep its sector bookkeeping intact and repeat ONLY the player notification
    // after InternalSetPosition completes. Vanilla handles peer ownership, visibility,
    // duplicate suppression and delivery through its existing invalid-sector queue.
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.InternalSetPosition), typeof(Vector3))]
    internal static class PlayerSectorPatch
    {
        private static readonly int PlayerPrefab = "Player".GetStableHashCode();

        [HarmonyPrefix]
        internal static void Prefix(ZDO __instance, Vector3 pos, out bool __state)
        {
            __state = false;
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                ZDOMan.instance == null || __instance.GetPrefab() != PlayerPrefab)
                return;

            // Match vanilla's approximate Vector3 equality guard. Do not re-notify
            // for unchanged positions or movement inside the same sector.
            __state = __instance.GetPosition() != pos &&
                __instance.GetSectorIndex() != ZoneSystem.GetSectorIndex(pos);
        }

        [HarmonyPostfix]
        internal static void Postfix(ZDO __instance, bool __state)
        {
            if (__state && ZNet.instance != null && ZNet.instance.IsServer() &&
                ZDOMan.instance != null)
                ZDOMan.instance.ZDOSectorInvalidated(__instance);
        }
    }
}
