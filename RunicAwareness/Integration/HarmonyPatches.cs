using System;
using HarmonyLib;

namespace RunicAwareness.Integration
{
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip",
        typeof(ItemDrop.ItemData), typeof(UITooltip))]
    internal static class InventoryGridCreateItemTooltipPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ItemDrop.ItemData __0)
        {
            try { HoverItemCapture.Record(__0); }
            catch (Exception exception) { Plugin.Instance?.Runtime?.FailHook("item tooltip", exception); }
        }
    }

    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(Player))]
    internal static class RestedCalculateComfortPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __0, int __result)
        {
            try { ComfortCapture.Record(__0, __result); }
            catch (Exception exception) { Plugin.Instance?.Runtime?.FailHook("comfort capture", exception); }
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText), new Type[] { })]
    internal static class CraftingStationHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CraftingStation __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.Building, __instance, __instance, __result,
                "crafting station hover");
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText), new Type[] { })]
    internal static class CookingStationHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CookingStation __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.Production, __instance, __instance, __result,
                "cooking station hover");
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText), new Type[] { })]
    internal static class FermenterHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Fermenter __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.Production, __instance, __instance, __result,
                "fermenter hover");
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText), new Type[] { })]
    internal static class PlantHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Plant __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.Agriculture, __instance, __instance, __result,
                "plant hover");
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText), new Type[] { })]
    internal static class BeehiveHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Beehive __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.Agriculture, __instance, __instance, __result,
                "beehive hover");
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText), new Type[] { })]
    internal static class TameableHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Tameable __instance, string __result) =>
            SafeContext.Record(AwarenessContextKind.TamedAnimal, __instance, __instance, __result,
                "tameable hover");
    }

    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText), new Type[] { })]
    internal static class ProductionSwitchHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Switch __instance, string __result)
        {
            try
            {
                if (__instance == null) return;
                Smelter smelter = __instance.GetComponentInParent<Smelter>();
                if (smelter != null)
                {
                    ContextCapture.Record(
                        AwarenessContextKind.Production, __instance, smelter, __result);
                    return;
                }
                CookingStation cooking = __instance.GetComponentInParent<CookingStation>();
                if (cooking != null)
                {
                    ContextCapture.Record(
                        AwarenessContextKind.Production, __instance, cooking, __result);
                    return;
                }
                Fermenter fermenter = __instance.GetComponentInParent<Fermenter>();
                if (fermenter != null)
                    ContextCapture.Record(
                        AwarenessContextKind.Production, __instance, fermenter, __result);
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Runtime?.FailHook("production switch hover", exception);
            }
        }
    }

    internal static class SafeContext
    {
        internal static void Record(
            AwarenessContextKind kind,
            UnityEngine.Component source,
            UnityEngine.Component subject,
            string text,
            string hook)
        {
            try { ContextCapture.Record(kind, source, subject, text); }
            catch (Exception exception) { Plugin.Instance?.Runtime?.FailHook(hook, exception); }
        }
    }
}
