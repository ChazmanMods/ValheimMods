using HarmonyLib;
using UnityEngine;

namespace RunicDisplayStands
{
    /// <summary>
    /// Redirects ItemStand.Interact / ArmorStand.Interact to the ContainerBridge instead of
    /// vanilla behavior, but only for prefabs listed in Plugin.StandPrefabSet, so unrelated
    /// mods' custom stands aren't touched.
    /// </summary>
    [HarmonyPatch(typeof(ItemStand), "Interact")]
    internal static class ItemStand_Interact_Patch
    {
        // Prefix returning false skips the original method entirely.
        private static bool Prefix(ItemStand __instance, Humanoid __0, bool __1, bool __2, ref bool __result)
        {
            if (!Plugin.IsManagedStand(__instance.gameObject)) return true;
            if (__1) return true; // let vanilla handle "hold" style interactions if any
            if (!PrivateArea.CheckAccess(__instance.transform.position))
            {
                __result = true;
                return false;
            }

            if (__2 || IsTakeOneHeld())
            {
                var bridge = ContainerBridge.GetOrCreate(__instance.gameObject);
                __result = bridge.TakeOne(__0);
            }
            else
            {
                var bridge = ContainerBridge.GetOrCreate(__instance.gameObject);
                __result = bridge.OpenAsContainer(__0);
            }
            return false;
        }

        private static bool IsTakeOneHeld()
        {
            return Plugin.CfgTakeOneKey.Value.IsDown();
        }
    }

    [HarmonyPatch(typeof(Switch), "Interact")]
    internal static class ArmorStand_Switch_Interact_Patch
    {
        [HarmonyBefore("chazman.RunicInteraction")]
        private static bool Prefix(Switch __instance, Humanoid __0, bool __1, ref bool __result)
        {
            var armorStand = __instance.GetComponentInParent<ArmorStand>();
            if (armorStand == null) return true;
            if (!Plugin.IsManagedStand(armorStand.gameObject)) return true;
            if (__1) return true;
            if (!PrivateArea.CheckAccess(armorStand.transform.position, 0f, flash: false))
            {
                __result = true;
                return false;
            }

            var bridge = ContainerBridge.GetOrCreate(armorStand.gameObject);
            __result = bridge.OpenAsContainer(__0);
            return false;
        }
    }
}
