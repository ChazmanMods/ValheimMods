using System;
using HarmonyLib;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class AgricultureControllerGetButtonPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!AgricultureControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class AgricultureControllerGetButtonDownPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!AgricultureControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class AgricultureControllerGetButtonUpPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!AgricultureControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonPressedTimer), typeof(string))]
    internal static class AgricultureControllerPressedTimerPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(string name, ref float __result)
        {
            if (!AgricultureControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonLastPressedTimer), typeof(string))]
    internal static class AgricultureControllerLastPressedTimerPatch
    {
        [HarmonyPriority(Priority.Normal)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicInventory")]
        private static bool Prefix(string name, ref float __result)
        {
            if (!AgricultureControllerCollisionGuard.ShouldSuppress(name)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class PlayerUpdateAgricultureInputPatch
    {
        private static void Prefix(Player __instance)
        {
            try { Plugin.Instance?.Runtime?.TickInput(__instance); }
            catch (Exception exception)
            {
                Plugin.Instance?.Log.LogError("Agriculture input routing failed closed: " + exception);
                Plugin.Instance?.Runtime?.DisableForSession(Core.AgricultureReasonCodes.PlacementFailed);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost", typeof(bool))]
    internal static class PlayerUpdatePlacementGhostPatch
    {
        private static void Postfix(Player __instance)
        {
            try { Plugin.Instance?.Runtime?.UpdatePreview(__instance); }
            catch (Exception exception)
            {
                Plugin.Instance?.Log.LogError("Agriculture preview failed closed: " + exception);
                Plugin.Instance?.Runtime?.DisableForSession(Core.AgricultureReasonCodes.PlacementFailed);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
    internal static class PlayerInteractPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicProduction")]
        private static bool Prefix(Player __instance, GameObject go)
        {
            try
            {
                Plugin plugin = Plugin.Instance;
                if (plugin?.Runtime == null || !plugin.Runtime.IsOperational) return true;
                if (!plugin.Runtime.IsAreaHarvestRequested(__instance, go)) return true;
                return !plugin.Runtime.TryAreaHarvest(__instance, go);
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Log.LogWarning(
                    "Area harvest failed; preserving the original single interaction: " + exception.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    internal static class PlantHoverTextPatch
    {
        private static void Postfix(Plant __instance, ref string __result)
        {
            if (!(AgricultureConfig.Enabled?.Value ?? false) ||
                !(AgricultureConfig.ShowHoverStatus?.Value ?? false)) return;
            try
            {
                double growTime = Math.Max(0.001d, ValheimAccess.PlantGrowTime(__instance));
                int progress = Mathf.Clamp(
                    Mathf.FloorToInt((float)(ValheimAccess.PlantAge(__instance) / growTime * 100d)),
                    0,
                    100);
                __result += "\n<color=#8fd694>[Runic]</color> Growth " + progress + "% - " +
                            FriendlyPlantStatus(__instance.GetStatus());
            }
            catch (Exception)
            {
                // Read-only status is best effort; vanilla hover text remains intact.
            }
        }

        private static string FriendlyPlantStatus(Plant.Status status)
        {
            switch (status)
            {
                case Plant.Status.Healthy: return "healthy";
                case Plant.Status.NoSun: return global::Runic.Localization.RunicText.Get("text_6734c4c664b0");
                case Plant.Status.NoSpace: return global::Runic.Localization.RunicText.Get("text_52fdd9c6a0e0");
                case Plant.Status.WrongBiome: return global::Runic.Localization.RunicText.Get("text_b2f3314ac35c");
                case Plant.Status.NotCultivated: return global::Runic.Localization.RunicText.Get("text_0ec94147c6f6");
                case Plant.Status.NoAttachPiece: return global::Runic.Localization.RunicText.Get("text_4b14a5e69d87");
                case Plant.Status.TooHot: return global::Runic.Localization.RunicText.Get("text_1070aa4c5f46");
                case Plant.Status.TooCold: return global::Runic.Localization.RunicText.Get("text_1b68768db506");
                default: return status.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    internal static class BeehiveHoverTextPatch
    {
        private static void Postfix(Beehive __instance, ref string __result)
        {
            if (!(AgricultureConfig.Enabled?.Value ?? false) ||
                !(AgricultureConfig.ShowHoverStatus?.Value ?? false)) return;
            try
            {
                if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, false, false)) return;
                string happiness = !ValheimAccess.BeeBiomeValid(__instance)
                    ? "wrong biome"
                    : !ValheimAccess.BeeHasSpace(__instance)
                        ? "blocked"
                        : "happy";
                __result += "\n<color=#f4c95d>[Runic]</color> Honey " +
                            ValheimAccess.BeeHoney(__instance) + "/" + __instance.m_maxHoney +
                            global::Runic.Localization.RunicText.Get("text_d64450916899") + happiness;
            }
            catch (Exception)
            {
                // Preserve vanilla hover text on any compatibility mismatch.
            }
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
    internal static class PickableHarvestHoverTextPatch
    {
        private static void Postfix(Pickable __instance, ref string __result)
        {
            if (!(AgricultureConfig.Enabled?.Value ?? false)) return;
            try
            {
                AgricultureRuntime runtime = Plugin.Instance?.Runtime;
                if (runtime == null || !runtime.CanOfferAreaHarvest(Player.m_localPlayer, __instance))
                    return;
                bool ready = AgricultureRuntime.IsPickableReady(__instance, out _);
                if (AgricultureConfig.ShowHoverStatus?.Value ?? false)
                    __result += ready
                        ? global::Runic.Localization.RunicText.Get("text_4c20c99ce5b2")
                        : global::Runic.Localization.RunicText.Get("text_1463627b2b40");
                if (ready && (AgricultureConfig.ShowContextualControls?.Value ?? false))
                {
                    string hint = Plugin.Instance?.Runtime?.HarvestControlHint();
                    if (!string.IsNullOrEmpty(hint))
                        __result += global::Runic.Localization.RunicText.Get("text_ed914c31e9f7") + hint;
                }
            }
            catch (Exception)
            {
                // Preserve vanilla hover text.
            }
        }
    }

}
