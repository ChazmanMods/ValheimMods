using HarmonyLib;
using UnityEngine;

namespace WifeMod
{
    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    internal static class BedInteractPatch
    {
        internal static bool Bypass;

        [HarmonyPrefix]
        private static bool Prefix(Bed __instance, Humanoid __0, bool __1, bool __2, ref bool __result)
        {
            if (Bypass) return true;
            Humanoid human = __0;
            bool repeat = __1;
            bool alt = __2;
            if (repeat || alt || !(human is Player player)) return true;
            WifeCompanion wife = FindReadyWife(__instance.transform.position, player.GetPlayerID(), 5f);
            if (wife == null) return true;
            if (!BedBreedingSequence.TryStart(__instance, player, wife)) return true;
            __result = true;
            return false;
        }

        private static WifeCompanion FindReadyWife(Vector3 position, long owner, float radius)
        {
            foreach (Collider hit in Physics.OverlapSphere(position, radius))
            {
                WifeCompanion wife = hit.GetComponentInParent<WifeCompanion>();
                if (wife != null && wife.OwnerId == owner && wife.IsReadyToBreed()) return wife;
            }
            return null;
        }
    }
}
