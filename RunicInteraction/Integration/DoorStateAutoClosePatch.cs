using System;
using HarmonyLib;

namespace RunicInteraction.Integration
{
    [HarmonyPatch(typeof(Door), "UpdateState", new Type[0])]
    internal static class DoorStateAutoClosePatch
    {
        private static void Postfix(Door __instance)
        {
            try { DoorAutoCloseRuntime.ObserveState(__instance); }
            catch (Exception exception) { Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_99c4c74cd304")); }
        }
    }
}
