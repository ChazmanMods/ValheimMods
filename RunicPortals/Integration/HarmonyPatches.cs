using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RunicPortals.Integration
{
    [HarmonyPatch(typeof(TeleportWorld), "Awake")]
    internal static class TeleportWorldAwakePatch
    {
        private static void Postfix(TeleportWorld __instance)
        {
            try { if (Plugin.RuntimeReady) Plugin.CurrentRuntime?.Observe(__instance); }
            catch (Exception exception) { Plugin.DisableAfterPatchFault(exception, "portal-observation"); }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact),
        typeof(Humanoid), typeof(bool), typeof(bool))]
    internal static class TeleportWorldInteractPatch
    {
        private static bool Prefix(
            TeleportWorld __instance,
            Humanoid human,
            bool hold,
            bool alt,
            ref bool __result)
        {
            try
            {
                if (!Plugin.RuntimeReady || Plugin.CurrentRuntime == null ||
                    !Plugin.CurrentRuntime.TryHandleInteract(
                        __instance, human, hold, alt, out bool handled)) return true;
                __result = handled;
                return false;
            }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "interaction");
                __result = false;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorldHoverPatch
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            try
            {
                if (!Plugin.RuntimeReady || Plugin.CurrentRuntime == null) return;
                Plugin.CurrentRuntime.NoteHoveredPortal(__instance);
                if (Plugin.CurrentRuntime.TryGetHoverText(__instance, out string text)) __result = text;
            }
            catch (Exception exception) { Plugin.DisableAfterPatchFault(exception, "hover-text"); }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "HaveTarget")]
    internal static class TeleportWorldHaveTargetPatch
    {
        private static void Postfix(TeleportWorld __instance, ref bool __result)
        {
            try
            {
                if (Plugin.RuntimeReady && Plugin.CurrentRuntime != null)
                    __result = Plugin.CurrentRuntime.ResolvePortalVisualState(__instance, __result, false);
            }
            catch (Exception exception) { Plugin.DisableAfterPatchFault(exception, "target-presence"); }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "TargetFound")]
    internal static class TeleportWorldTargetFoundPatch
    {
        private static void Postfix(TeleportWorld __instance, ref bool __result)
        {
            try
            {
                if (Plugin.RuntimeReady && Plugin.CurrentRuntime != null)
                    __result = Plugin.CurrentRuntime.ResolvePortalVisualState(__instance, __result, true);
            }
            catch (Exception exception) { Plugin.DisableAfterPatchFault(exception, "target-resolution"); }
        }
    }

    [HarmonyPatch(typeof(Game), "FindRandomUnconnectedPortal",
        new[] { typeof(List<ZDO>), typeof(ZDO), typeof(string) })]
    internal static class VanillaPortalCandidateBoundaryPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref List<ZDO> __0, ZDO __1)
        {
            try
            {
                if (Plugin.RuntimeReady && Plugin.CurrentRuntime != null &&
                    Plugin.CurrentRuntime.TryFilterVanillaPortalCandidates(
                        __1, __0, out List<ZDO> filtered)) __0 = filtered;
                return true;
            }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "vanilla-pair-boundary");
                __0 = new List<ZDO>();
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport), typeof(Player))]
    internal static class TeleportWorldTeleportPatch
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            try
            {
                if (!Plugin.RuntimeReady || Plugin.CurrentRuntime == null ||
                    !Plugin.CurrentRuntime.TryHandleTeleport(__instance, player, out bool handled))
                    return true;
                return !handled;
            }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "travel-commit");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CanMove))]
    internal static class PortalMapPickerPlayerMovementPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            try
            {
                if (Plugin.RuntimeReady &&
                    (Plugin.CurrentRuntime?.BlocksPlayerMovement(__instance) ?? false))
                    __result = false;
            }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "map-picker-movement");
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick", new Type[] { })]
    [HarmonyAfter("chazman.RunicExploration")]
    internal static class PortalMapPickerLeftClickPatch
    {
        private static bool Prefix()
        {
            try { return !PortalMapPickerHarmonyGuard.TryClick(); }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "map-picker-left-click");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapDblClick", new Type[] { })]
    internal static class PortalMapPickerDoubleClickPatch
    {
        private static bool Prefix() => !PortalMapPickerHarmonyGuard.Blocks("double-click");
    }

    [HarmonyPatch(typeof(Minimap), "OnMapMiddleClick", typeof(UIInputHandler))]
    internal static class PortalMapPickerMiddleClickPatch
    {
        private static bool Prefix() => !PortalMapPickerHarmonyGuard.Blocks("middle-click");
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer", new Type[] { })]
    internal static class PortalMapPickerRemoveUnderPointerPatch
    {
        private static bool Prefix() => !PortalMapPickerHarmonyGuard.Blocks("right-click");
    }

    [HarmonyPatch(typeof(Minimap), "ShowPinNameInput", typeof(Vector3))]
    internal static class PortalMapPickerShowPinNamePatch
    {
        private static bool Prefix() => !PortalMapPickerHarmonyGuard.TryClick(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Vector3), typeof(float))]
    internal static class PortalMapPickerRemoveAtPositionPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!PortalMapPickerHarmonyGuard.Blocks("remove-pin")) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "GetClosestPin", typeof(Vector3), typeof(float), typeof(bool))]
    internal static class PortalMapPickerClosestPinPatch
    {
        private static bool Prefix(ref Minimap.PinData __result)
        {
            if (!PortalMapPickerHarmonyGuard.Blocks("closest-pin")) return true;
            __result = null;
            return false;
        }
    }

    internal static class PortalMapPickerHarmonyGuard
    {
        internal static bool Blocks(string operation)
        {
            try { return Plugin.RuntimeReady &&
                         (Plugin.CurrentRuntime?.BlocksModalMapMutation ?? false); }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "map-picker-" + operation);
                return true;
            }
        }

        internal static bool TryClick()
        {
            try { return Plugin.RuntimeReady &&
                         (Plugin.CurrentRuntime?.TryHandleMapPickerClick() ?? false); }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "map-picker-left-click");
                return true;
            }
        }

        internal static bool TryClick(Vector3 screenPoint)
        {
            try { return Plugin.RuntimeReady &&
                         (Plugin.CurrentRuntime?.TryHandleMapPickerClick(screenPoint) ?? false); }
            catch (Exception exception)
            {
                Plugin.DisableAfterPatchFault(exception, "map-picker-gamepad-click");
                return true;
            }
        }
    }

}
