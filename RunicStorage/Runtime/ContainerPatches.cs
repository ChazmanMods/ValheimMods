using System;
using HarmonyLib;

namespace RunicStorage.Runtime
{
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class StorageSearchCursorLeasePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => StorageSearchPanel.RenewCursorLease();
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class StorageSearchInputGatePatch
    {
        [HarmonyAfter("chazman.RunicBuildCamera")]
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__instance == Player.m_localPlayer && Plugin.SearchPanelOpen)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance)
        {
            ContainerHoverContents.Invalidate(__instance);
            Plugin.Index?.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class ContainerDestroyPatch
    {
        private static void Postfix(Container __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            ContainerHoverContents.Invalidate(__instance);
            Plugin.Index?.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "CheckForChanges")]
    internal static class ContainerSpatialRefreshPatch
    {
        private static void Postfix(Container __instance) => Plugin.Index?.Add(__instance);
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText), new Type[] { })]
    internal static class ContainerHoverTextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Container __instance, ref string __result)
        {
            try { ContainerHoverContents.Append(__instance, ref __result); }
            catch
            {
                // Hover text must remain a read-only fail-closed enhancement. Vanilla text wins.
            }
        }
    }
}
