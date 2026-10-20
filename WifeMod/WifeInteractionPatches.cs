using HarmonyLib;
using UnityEngine;

namespace WifeMod
{
    /// <summary>
    /// HC_Companion already supplies its own Hoverable/Interactable component.
    /// Intercept at Player so wife-specific interaction is deterministic while
    /// preserving Offline Companions' hold-E radial menu.
    /// </summary>
    [HarmonyPatch]
    internal static class WifeInteractionPatches
    {
        [HarmonyPatch(typeof(ZNetView), "Awake")]
        [HarmonyPostfix]
        private static void RestorePersistedWifeComponents(ZNetView __instance)
        {
            if (__instance == null || __instance.GetZDO() == null) return;
            if (__instance.GetZDO().GetLong("WifeMod_owner", 0L) == 0L) return;
            GameObject target = __instance.gameObject;
            if (target.GetComponent<WifeCompanion>() == null) target.AddComponent<WifeCompanion>();
            if (target.GetComponent<FireTender>() == null) target.AddComponent<FireTender>();
        }

    }
}
