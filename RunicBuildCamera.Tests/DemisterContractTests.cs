using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicBuildCamera.Integration;
using UnityEngine;

namespace RunicBuildCamera.Tests
{
    internal static class DemisterContractTests
    {
        internal static void Register()
        {
            TestRunner.Run("Demister update hook is a postfix", DemisterUpdateHookIsPostfix);
            TestRunner.Run("Demister removal restores before vanilla cleanup", DemisterRemovalRestoresBeforeCleanup);
            TestRunner.Run("Demister follows only the local player's existing ball", DemisterUsesExistingLocalBall);
            TestRunner.Run("Demister never grants or instantiates an effect", DemisterNeverGrantsAnEffect);
            TestRunner.Run("Demister range captures and restores exact baselines", DemisterRangeRestoresBaselines);
            TestRunner.Run("Demister ball returns to the avatar immediately", DemisterBallReturnsImmediately);
            TestRunner.Run("Demister restores on every camera exit path", DemisterRestoresOnExit);
        }

        private static void DemisterUpdateHookIsPostfix()
        {
            Type patch = typeof(DemisterUpdateStatusEffectPatch);
            HarmonyMethod target = MergePatchAttributes(patch);
            TestAssert.Equal(typeof(SE_Demister), target.declaringType);
            TestAssert.Equal(nameof(SE_Demister.UpdateStatusEffect), target.methodName);
            TestAssert.NotNull(patch.GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic),
                "Demister must follow the ball after vanilla updates it.");
            TestAssert.True(patch.GetMethod(
                    "Prefix", BindingFlags.Static | BindingFlags.NonPublic) == null,
                "Demister follow must not replace vanilla's update.");

            MethodInfo postfix = patch.GetMethod(
                "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                postfix, typeof(DemisterRuntime), "AfterStatusEffectUpdate"));
            TestAssert.True(IlReader.Calls(
                postfix, typeof(DemisterRuntime), "OnCameraExit"));
        }

        private static void DemisterRemovalRestoresBeforeCleanup()
        {
            Type patch = typeof(DemisterRemoveEffectsPatch);
            HarmonyMethod target = MergePatchAttributes(patch);
            TestAssert.Equal(typeof(SE_Demister), target.declaringType);
            TestAssert.Equal("RemoveEffects", target.methodName);
            MethodInfo prefix = TestAssert.NotNull(
                patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic),
                "Demister RemoveEffects must have a prefix restoration hook.");
            TestAssert.True(patch.GetMethod(
                    "Postfix", BindingFlags.Static | BindingFlags.NonPublic) == null,
                "Restoring after vanilla destroys the ball is too late.");
            TestAssert.True(IlReader.Calls(
                prefix, typeof(DemisterRuntime), "BeforeRemoveEffects"));

            MethodInfo before = typeof(DemisterRuntime).GetMethod(
                "BeforeRemoveEffects", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(before, typeof(DemisterRuntime), "RestoreEffect"));
        }

        private static void DemisterUsesExistingLocalBall()
        {
            const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;
            FieldInfo installed = typeof(SE_Demister).GetField("m_ballInstance", AllFields);
            TestAssert.NotNull(installed, "Installed SE_Demister.m_ballInstance is missing.");
            TestAssert.Equal(typeof(GameObject), installed.FieldType);
            TestAssert.False(installed.IsStatic);

            MethodBase initializer = typeof(DemisterRuntime).TypeInitializer;
            TestAssert.True(IlReader.LoadsString(initializer, "m_ballInstance"));
            TestAssert.True(IlReader.Calls(initializer, typeof(AccessTools), nameof(AccessTools.Field)));

            MethodInfo after = typeof(DemisterRuntime).GetMethod(
                "AfterStatusEffectUpdate", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                after, typeof(BuildCameraRuntime), "TryGetActiveContext"));
            TestAssert.True(IlReader.AccessesField(after, typeof(Player), "m_localPlayer"));
            TestAssert.True(IlReader.AccessesField(after, typeof(StatusEffect), "m_character"));
            TestAssert.True(IlReader.Calls(after, typeof(Transform), "set_position"));
        }

        private static void DemisterNeverGrantsAnEffect()
        {
            foreach (MethodInfo method in typeof(DemisterRuntime).GetMethods(
                         BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (method.GetMethodBody() == null) continue;
                IReadOnlyList<MethodBase> calls = IlReader.Calls(method);
                TestAssert.False(calls.Any(call =>
                        string.Equals(call.Name, "AddStatusEffect", StringComparison.Ordinal)),
                    method.Name + " grants a status effect instead of following an existing one.");
                TestAssert.False(calls.Any(call =>
                        call.DeclaringType == typeof(UnityEngine.Object) &&
                        string.Equals(call.Name, nameof(UnityEngine.Object.Instantiate), StringComparison.Ordinal)),
                    method.Name + " instantiates a demister object.");
            }
        }

        private static void DemisterRangeRestoresBaselines()
        {
            Type baseline = typeof(DemisterRuntime).GetNestedType(
                "ForceFieldBaseline", BindingFlags.NonPublic);
            TestAssert.NotNull(baseline, "Force-field baseline state is missing.");
            TestAssert.NotNull(baseline.GetProperty(
                "EndRange", BindingFlags.Instance | BindingFlags.NonPublic),
                "Original force-field range is not retained.");

            MethodInfo capture = typeof(DemisterRuntime).GetMethod(
                "GetOrCaptureBall", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo apply = typeof(DemisterRuntime).GetMethod(
                "ApplyRange", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo restore = typeof(DemisterRuntime).GetMethod(
                "RestoreState", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                capture, typeof(GameObject), "GetComponentsInChildren"));
            TestAssert.True(IlReader.Calls(
                capture, typeof(ParticleSystemForceField), "get_endRange"));
            TestAssert.True(IlReader.Calls(
                apply, typeof(ParticleSystemForceField), "set_endRange"));
            TestAssert.True(IlReader.Calls(
                restore, typeof(ParticleSystemForceField), "set_endRange"));
        }

        private static void DemisterRestoresOnExit()
        {
            string[] entryPoints = { "OnCameraExit", "Shutdown", "OnConfigurationChanged" };
            foreach (string entryPoint in entryPoints)
            {
                MethodInfo method = typeof(DemisterRuntime).GetMethod(
                    entryPoint, BindingFlags.Static | BindingFlags.NonPublic);
                TestAssert.True(IlReader.Calls(method, typeof(DemisterRuntime), "RestoreAll"),
                    entryPoint + " does not restore all tracked force fields.");
            }

            MethodInfo refresh = typeof(DemisterRuntime).GetMethod(
                "RefreshActiveState", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(refresh, typeof(DemisterRuntime), "RestoreAll"));

            MethodInfo localPlayerCleanup = typeof(RemoteEffectsLocalPlayerCleanupPatch).GetMethod(
                "Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(IlReader.Calls(
                localPlayerCleanup, typeof(DemisterRuntime), "OnCameraExit"));
        }

        private static void DemisterBallReturnsImmediately()
        {
            MethodInfo restore = typeof(DemisterRuntime).GetMethod(
                "RestoreState", BindingFlags.Static | BindingFlags.NonPublic);
            IReadOnlyList<MethodBase> calls = IlReader.Calls(restore);
            int center = IlReader.CallIndex(calls, typeof(Character), nameof(Character.GetCenterPoint));
            if (center < 0)
                center = calls.ToList().FindIndex(method => method.Name == nameof(Character.GetCenterPoint));
            int position = IlReader.CallIndex(calls, typeof(Transform), "set_position");
            TestAssert.True(center >= 0 && position > center,
                "Demister restoration no longer moves the existing ball back to the avatar immediately.");
            TestAssert.True(IlReader.AccessesField(restore, typeof(Player), "m_localPlayer"));
        }

        private static HarmonyMethod MergePatchAttributes(Type patch)
        {
            var merged = new HarmonyMethod();
            foreach (HarmonyPatch attribute in patch.GetCustomAttributes<HarmonyPatch>())
            {
                HarmonyMethod info = attribute.info;
                if (info.declaringType != null) merged.declaringType = info.declaringType;
                if (!string.IsNullOrEmpty(info.methodName)) merged.methodName = info.methodName;
                if (info.argumentTypes != null) merged.argumentTypes = info.argumentTypes;
            }
            return merged;
        }
    }
}
