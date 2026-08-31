using System;
using System.IO;
using HarmonyLib;

namespace RunicWorldEngine.Integration
{
    [HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", typeof(ZDOID), typeof(UnityEngine.Vector3), typeof(int))]
    internal static class ZdoCreatedPatch
    {
        private static void Postfix(ZDO __result) => ObservatoryRuntime.MarkCreated(__result);
    }

    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO", typeof(ZDOID))]
    internal static class ZdoDestroyedPatch
    {
        private static void Prefix(ZDOMan __instance, ZDOID __0, ref bool __state) =>
            __state = ObservatoryRuntime.Exists(__instance, __0);

        private static void Postfix(ZDOMan __instance, ZDOID __0, bool __state) =>
            ObservatoryRuntime.MarkDestroyed(__state, __instance, __0);
    }

    [HarmonyPatch(typeof(ZDOMan), "UpdateStats", typeof(float))]
    internal static class ZdoStatsPatch
    {
        private static void Postfix(ZDOMan __instance) => ObservatoryRuntime.Capture(__instance);
    }

    [HarmonyPatch(typeof(ZDOMan), "SaveAsync", typeof(BinaryWriter))]
    internal static class ZdoSaveTimingPatch
    {
        private static void Prefix(ref long __state) => __state = ObservatoryRuntime.BeginTimedOperation();

        private static Exception Finalizer(long __state, Exception __exception)
        {
            ObservatoryRuntime.EndSave(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZDOMan), "Load", typeof(BinaryReader), typeof(int))]
    internal static class ZdoLoadTimingPatch
    {
        private static void Prefix(ref long __state) => __state = ObservatoryRuntime.BeginTimedOperation();

        private static Exception Finalizer(long __state, Exception __exception)
        {
            ObservatoryRuntime.EndLoad(__state);
            return __exception;
        }
    }
}
