using System;
using System.Reflection;
using HarmonyLib;
using RunicBuildCamera.Core;
using UnityEngine;

namespace RunicBuildCamera.Integration
{
    internal static class PlayerUpdateInputIsolation
    {
        [ThreadStatic]
        private static int _depth;

        [ThreadStatic]
        private static Player _player;

        [ThreadStatic]
        private static bool _captured;

        [ThreadStatic]
        private static bool _originalTakeInput;

        internal static bool Enter(Player player)
        {
            if (!BuildCameraRuntime.ShouldFreezePlayer(player)) return false;
            if (_depth == 0)
            {
                _player = player;
                _captured = false;
                _originalTakeInput = false;
            }
            _depth++;
            return true;
        }

        internal static void CaptureTakeInput(Player player, bool original)
        {
            if (_depth <= 0 || player == null || player != _player) return;
            _captured = true;
            _originalTakeInput = original;
        }

        internal static bool ShouldSuppress(Player player) =>
            _depth > 0 && player != null && player == _player &&
            BuildCameraRuntime.ShouldFreezePlayer(player);

        internal static bool PlacementInput(Player player, bool current) =>
            ShouldSuppress(player) && _captured ? _originalTakeInput : current;

        internal static void Exit()
        {
            if (_depth <= 0) return;
            _depth--;
            if (_depth != 0) return;
            _player = null;
            _captured = false;
            _originalTakeInput = false;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class PlayerUpdateInputScopePatch
    {
        private static void Prefix(Player __instance, ref bool __state)
        {
            try
            {
                __state = PlayerUpdateInputIsolation.Enter(__instance);
            }
            catch
            {
                __state = false;
                BuildCameraRuntime.ForceStop();
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) PlayerUpdateInputIsolation.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerTakeInputIsolationPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!PlayerUpdateInputIsolation.ShouldSuppress(__instance)) return;
            PlayerUpdateInputIsolation.CaptureTakeInput(__instance, __result);
            if (CameraExitInput.ReleaseIfRequested(__result,
                    BuildCameraRuntime.ExitInputRequested, BuildCameraRuntime.ForceStop)) return;
            __result = false;
        }
    }

    // Controller hotbar use is dispatched by HotkeyBar.Update, outside Player.Update.
    // Let the existing native action run exactly once after releasing the camera.
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem), typeof(int))]
    internal static class PlayerHotbarCameraExitPatch
    {
        private static void Prefix(Player __instance)
        {
            if (BuildCameraRuntime.ShouldFreezePlayer(__instance))
                BuildCameraRuntime.ForceStop();
        }
    }

    [HarmonyPatch(typeof(Player), "SetMouseLook", typeof(Vector2))]
    internal static class PlayerSetMouseLookIsolationPatch
    {
        private static void Prefix(Player __instance, ref Vector2 mouseLook)
        {
            if (BuildCameraRuntime.ShouldFreezePlayer(__instance))
                mouseLook = Vector2.zero;
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetStationBuildRange))]
    internal static class CraftingStationScopedBuildRangePatch
    {
        private static void Postfix(ref float __result)
        {
            if (ValheimAdapter.TryGetScopedStationRange(out float range) && __result < range)
                __result = range;
        }
    }

    [HarmonyPatch(typeof(Player), "PieceRayTest")]
    internal static class PlayerPieceRayRemoteLimitPatch
    {
        private static void Postfix(
            Player __instance,
            [HarmonyArgument("point")] ref Vector3 point,
            ref bool __result)
        {
            if (__result && BuildCameraRuntime.ShouldFreezePlayer(__instance) &&
                !BuildCameraRuntime.IsWithinRemoteActionLimit(__instance, point))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    internal static class GameCameraUpdateCameraPatch
    {
        private static MethodBase TargetMethod() => ValheimAdapter.UpdateCameraMethod;

        private static bool Prefix(GameCamera __instance, float dt)
        {
            try
            {
                return !BuildCameraRuntime.TryUpdateCamera(__instance, dt);
            }
            catch
            {
                BuildCameraRuntime.ForceStop();
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class PlayerSetControlsPatch
    {
        private static MethodBase TargetMethod() => ValheimAdapter.SetControlsMethod;

        private static void Prefix(
            Player __instance,
            ref Vector3 movedir,
            ref bool attack,
            ref bool attackHold,
            ref bool secondaryAttack,
            ref bool secondaryAttackHold,
            ref bool block,
            ref bool blockHold,
            ref bool jump,
            ref bool crouch,
            ref bool run,
            ref bool autoRun,
            ref bool dodge)
        {
            if (!BuildCameraRuntime.ShouldFreezePlayer(__instance)) return;

            movedir = Vector3.zero;
            attack = false;
            attackHold = false;
            secondaryAttack = false;
            secondaryAttackHold = false;
            block = false;
            blockHold = false;
            jump = false;
            crouch = false;
            run = false;
            autoRun = false;
            dodge = false;
        }
    }

    [HarmonyPatch]
    internal static class PlayerUpdatePlacementRangePatch
    {
        private static MethodBase TargetMethod() => ValheimAdapter.UpdatePlacementMethod;

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("chazman.RunicPrecisionBuildTool")]
        private static void Prefix(
            Player __instance,
            ref bool takeInput,
            ref ValheimAdapter.RangeLease __state)
        {
            try
            {
                takeInput = PlayerUpdateInputIsolation.PlacementInput(__instance, takeInput);
                __state = BuildCameraRuntime.EnterRemoteActionRange(__instance);
            }
            catch
            {
                __state = null;
                BuildCameraRuntime.ForceStop();
            }
        }

        private static Exception Finalizer(
            Exception __exception,
            ValheimAdapter.RangeLease __state)
        {
            try
            {
                __state?.Dispose();
            }
            catch
            {
                BuildCameraRuntime.ForceStop();
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class PlayerUpdatePlacementGhostRangePatch
    {
        private static MethodBase TargetMethod() => ValheimAdapter.UpdatePlacementGhostMethod;

        private static void Prefix(Player __instance, ref ValheimAdapter.RangeLease __state)
        {
            try
            {
                __state = BuildCameraRuntime.EnterRemoteActionRange(__instance);
            }
            catch
            {
                __state = null;
                BuildCameraRuntime.ForceStop();
            }
        }

        private static Exception Finalizer(
            Exception __exception,
            ValheimAdapter.RangeLease __state)
        {
            try
            {
                __state?.Dispose();
            }
            catch
            {
                BuildCameraRuntime.ForceStop();
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Player), "SetLocalPlayer")]
    internal static class PlayerSetLocalPlayerCameraCleanupPatch
    {
        private static void Postfix(Player __instance)
        {
            try
            {
                BuildCameraRuntime.OnLocalPlayerAssigned(__instance);
            }
            catch
            {
                BuildCameraRuntime.ForceStop();
            }
        }
    }
}
