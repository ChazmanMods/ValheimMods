using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ArcaneDecorWaterGardens.Input;

// WaterGardens-specific namespace is essential: HarmonyX keys __state by declaring type FullName,
// ignoring assembly identity. Sharing the RunicStorage patch type name leaks camera scopes.
internal static class ModalGameplayInput
{
    internal static Func<bool> IsOpen;
    [ThreadStatic] private static int _cameraDepth;
    private static int _lastOpenFrame = -10;
    internal static bool Blocked {
        get {
            if (IsOpen?.Invoke() == true) { _lastOpenFrame = Time.frameCount; return true; }
            return Time.frameCount <= _lastOpenFrame + 1;
        }
    }
    internal static bool CameraBlocked => _cameraDepth > 0;
    internal static void Reset() { IsOpen = null; _lastOpenFrame = -10; _cameraDepth = 0; }
    internal static void EnterCamera(bool blocked) { if (blocked) _cameraDepth++; }
    internal static void ExitCamera(bool blocked) { if (blocked) _cameraDepth = Math.Max(0, _cameraDepth - 1); }
    internal static bool GameplayAction(string name) => name != null &&
        (name.StartsWith("Hotbar", StringComparison.Ordinal) || name == "JoyHotbarUse" ||
         name == "Forward" || name == "Backward" || name == "Left" || name == "Right" ||
         name == "Attack" || name == "JoyAttack" || name == "SecondaryAttack" || name == "JoySecondaryAttack" ||
         name == "Block" || name == "JoyBlock" || name == "Jump" || name == "JoyJump" ||
         name == "Crouch" || name == "JoyCrouch" || name == "Run" || name == "JoyRun" ||
         name == "AutoRun" || name == "Dodge" || name == "JoyDodge" || name == "Use" || name == "JoyUse" ||
         name == "Inventory" || name == "JoyInventory" || name == "Chat" || name == "Console" ||
         name == "GuardianPower" || name == "JoyGuardianPower");
}

[HarmonyPatch(typeof(PlayerController), "TakeInput", typeof(bool))]
internal static class ModalControllerInputPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(ref bool __result) { if (ModalGameplayInput.Blocked) __result = false; }
}

[HarmonyPatch(typeof(Player), "TakeInput")]
internal static class ModalPlayerInputPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(Player __instance, ref bool __result)
    { if (__instance == Player.m_localPlayer && ModalGameplayInput.Blocked) __result = false; }
}

[HarmonyPatch(typeof(Player), "SetControls")]
internal static class ModalControlsPatch
{
    // Also neutralize controls already sampled before the panel opened this frame.
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
        ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
        ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
    {
        if (__instance != Player.m_localPlayer || !ModalGameplayInput.Blocked) return;
        movedir = Vector3.zero;
        attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
    }
}

[HarmonyPatch]
internal static class ModalCameraScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(GameCamera)
        .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.DeclaringType == typeof(GameCamera) && (m.Name == "UpdateCamera" || m.Name == "UpdateFreeFly"));
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(out bool __state)
    { __state = ModalGameplayInput.Blocked; ModalGameplayInput.EnterCamera(__state); }
    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, bool __state)
    { ModalGameplayInput.ExitCamera(__state); return __exception; }
}

[HarmonyPatch]
internal static class ModalCameraAxisPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ZInput)
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.ReturnType == typeof(float) && (m.Name == "GetMouseScrollWheel" ||
            m.Name.StartsWith("GetJoyLeftStick", StringComparison.Ordinal) || m.Name.StartsWith("GetJoyRightStick", StringComparison.Ordinal) ||
            m.Name == "GetJoyLTrigger" || m.Name == "GetJoyRTrigger"));
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(ref float __result) { if (ModalGameplayInput.CameraBlocked) __result = 0f; }
}

[HarmonyPatch(typeof(ZInput), "GetMouseDelta")]
internal static class ModalCameraLookPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(ref Vector2 __result) { if (ModalGameplayInput.CameraBlocked) __result = Vector2.zero; }
}

[HarmonyPatch]
internal static class ModalNamedActionPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "GetButton", "GetButtonDown", "GetButtonUp" }
        .Select(name => (MethodBase)AccessTools.Method(typeof(ZInput), name, new[] { typeof(string) }));
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(string name, ref bool __result)
    {
        if (ModalGameplayInput.CameraBlocked || ModalGameplayInput.Blocked && ModalGameplayInput.GameplayAction(name)) __result = false;
    }
}

[HarmonyPatch]
internal static class ModalShortcutKeyPatch
{
    // TMP/uGUI receives characters and pointer events directly from Unity. Mask only the
    // game's ZInput keyboard API so configurable mod shortcuts cannot fire while typing.
    // Escape remains owned by each panel's existing cancel/release handling.
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ZInput)
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.ReturnType == typeof(bool) && (m.Name == "GetKey" || m.Name == "GetKeyDown" || m.Name == "GetKeyUp") &&
            m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(KeyCode));
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(KeyCode key, ref bool __result)
    {
        if (key != KeyCode.Escape && (key < KeyCode.Mouse0 || key > KeyCode.Mouse6) && ModalGameplayInput.Blocked) __result = false;
    }
}
