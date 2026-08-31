using HarmonyLib;
using System;
using UnityEngine;

namespace RunicAgriculture.Integration
{
    /// <summary>
    /// One-frame ownership leases for the two shared vanilla inputs Agriculture     /// consumes. The local Player.Update prefix samples first and arms only after accepting a
    /// crop-context gesture; all later readers in that exact frame see a neutral value.
    /// </summary>
    internal static class AgricultureInputConsumption
    {
        private static int _sampleFrame = -1;
        private static int _wheelFrame = -1;
        private static int _buildMenuFrame = -1;
        private static int _placeFrame = -1;

        internal static void BeginSample(int frame)
        {
            if (_sampleFrame == frame) return;
            _sampleFrame = frame;
            _wheelFrame = -1;
            _buildMenuFrame = -1;
            _placeFrame = -1;
        }

        internal static void ConsumeWheel(int frame) => _wheelFrame = frame;

        internal static void ConsumeBuildMenu(int frame) => _buildMenuFrame = frame;

        internal static void ConsumePlace(int frame) => _placeFrame = frame;

        internal static bool ShouldSuppressWheel(int frame) => _wheelFrame == frame;

        internal static bool ShouldSuppressBuildMenu(string action, int frame) =>
            _buildMenuFrame == frame &&
            string.Equals(action, "BuildMenu", System.StringComparison.Ordinal);

        internal static bool ShouldSuppressPlace(string action, int frame) =>
            _placeFrame == frame &&
            string.Equals(action, "Attack", System.StringComparison.Ordinal);

        internal static void Reset()
        {
            _sampleFrame = -1;
            _wheelFrame = -1;
            _buildMenuFrame = -1;
            _placeFrame = -1;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class AgricultureConsumedMouseWheelPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicPrecisionBuildTool")]
        private static bool Prefix(ref float __result)
        {
            if (!AgricultureInputConsumption.ShouldSuppressWheel(Time.frameCount)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class AgricultureConsumedBuildMenuPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicPrecisionBuildTool")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!AgricultureInputConsumption.ShouldSuppressBuildMenu(name, Time.frameCount) &&
                !AgricultureInputConsumption.ShouldSuppressPlace(name, Time.frameCount))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
    internal static class AgricultureBuildHintsReplacementPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicPrecisionBuildTool")]
        private static void Postfix(KeyHints __instance)
        {
            try { Plugin.Instance?.Runtime?.ApplyBuildHintsReplacement(__instance); }
            catch (Exception exception)
            {
                Plugin.Instance?.Log.LogError(
                    "Agriculture build-hint replacement failed closed: " + exception);
                Plugin.Instance?.Runtime?.DisableForSession(
                    Core.AgricultureReasonCodes.PlacementFailed);
            }
        }
    }
}
