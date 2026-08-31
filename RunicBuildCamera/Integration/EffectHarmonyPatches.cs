using System.Reflection;
using HarmonyLib;

namespace RunicBuildCamera.Integration
{
    [HarmonyPatch]
    internal static class GameCameraRemoteEffectsPatch
    {
        private static MethodBase TargetMethod() => ValheimAdapter.UpdateCameraMethod;

        private static void Postfix()
        {
            try
            {
                RemotePickupRuntime.Tick();
                DemisterRuntime.RefreshActiveState();
            }
            catch
            {
                RemotePickupRuntime.Reset();
                DemisterRuntime.OnCameraExit();
            }
        }
    }

    [HarmonyPatch(typeof(SE_Demister), nameof(SE_Demister.UpdateStatusEffect))]
    internal static class DemisterUpdateStatusEffectPatch
    {
        private static void Postfix(SE_Demister __instance)
        {
            try
            {
                DemisterRuntime.AfterStatusEffectUpdate(__instance);
            }
            catch
            {
                DemisterRuntime.OnCameraExit();
            }
        }
    }

    [HarmonyPatch(typeof(SE_Demister), "RemoveEffects")]
    internal static class DemisterRemoveEffectsPatch
    {
        // Restore before vanilla destroys the actual ball so no modified force-field value can
        // leak into a pooled or externally retained instance.
        private static void Prefix(SE_Demister __instance)
        {
            try
            {
                DemisterRuntime.BeforeRemoveEffects(__instance);
            }
            catch
            {
                DemisterRuntime.OnCameraExit();
            }
        }
    }

    [HarmonyPatch(typeof(Player), "SetLocalPlayer")]
    internal static class RemoteEffectsLocalPlayerCleanupPatch
    {
        private static void Prefix()
        {
            RemotePickupRuntime.Reset();
            DemisterRuntime.OnCameraExit();
        }
    }
}
