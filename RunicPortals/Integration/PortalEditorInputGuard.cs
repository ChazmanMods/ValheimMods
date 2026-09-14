using System;
using HarmonyLib;
using UnityEngine;

namespace RunicPortals.Integration
{
    /// <summary>
    /// Owns gameplay input while the native portal editor is open. A short release latch prevents
    /// the click which closes the dialog from becoming an axe swing in the world behind it.
    /// </summary>
    internal static class PortalEditorInputGuard
    {
        private const string PrimaryAttackAction = "Attack";
        private const string ControllerCancelAction = "JoyButtonB";
        private static PortalEditorPanel _owner;
        private static bool _pollingCancel;
        private static bool _primaryPointerCaptured;
        private static int _primaryReleaseFrame = -1;
        private static int _cancelFrame = -1;

        internal static bool IsOpen => (_owner?.IsOpen ?? false) || NativeGroupInvitationPopup.IsOpen;

        internal static void Attach(PortalEditorPanel owner)
        {
            _owner = owner;
            _cancelFrame = -1;
        }

        internal static void Detach(PortalEditorPanel owner)
        {
            if (ReferenceEquals(_owner, owner)) _owner = null;
        }

        internal static bool PollCancel()
        {
            _pollingCancel = true;
            try
            {
                bool pressed = ZInput.GetKeyDown(KeyCode.Escape, false) ||
                               ZInput.GetButtonDown(ControllerCancelAction);
                if (pressed) _cancelFrame = Time.frameCount;
                return pressed;
            }
            finally
            {
                _pollingCancel = false;
            }
        }

        internal static void CapturePointer(Event current)
        {
            if (current == null || current.button != 0) return;
            if (current.type == EventType.MouseDown ||
                current.type == EventType.MouseUp ||
                current.type == EventType.MouseDrag)
                CapturePrimaryPointer();
        }

        internal static void CapturePrimaryPointer()
        {
            _primaryPointerCaptured = true;
            _primaryReleaseFrame = -1;
        }

        internal static bool SuppressEscape(KeyCode key)
        {
            if (key != KeyCode.Escape || _pollingCancel) return false;
            return IsOpen || Time.frameCount == _cancelFrame;
        }

        internal static bool SuppressButton(string action)
        {
            if (_pollingCancel) return false;
            if (NativeGroupInvitationPopup.IsOpen &&
                (string.Equals(action, "JoyHotbarUse", StringComparison.Ordinal) ||
                 action != null && action.StartsWith("Hotbar", StringComparison.Ordinal))) return true;
            if (string.Equals(action, ControllerCancelAction, StringComparison.Ordinal))
                return IsOpen || _cancelFrame >= 0 && Time.frameCount <= _cancelFrame + 1;
            if (!string.Equals(action, PrimaryAttackAction, StringComparison.Ordinal)) return false;

            bool held = false;
            try
            {
                ZInput.ButtonDef definition = ZInput.instance?.GetButtonDef(PrimaryAttackAction);
                held = definition != null && definition.Held;
            }
            catch
            {
                held = false;
            }

            if (IsOpen)
            {
                if (held) CapturePrimaryPointer();
                return true;
            }
            if (!_primaryPointerCaptured) return false;
            if (held)
            {
                _primaryReleaseFrame = -1;
                return true;
            }
            if (_primaryReleaseFrame < 0)
            {
                _primaryReleaseFrame = Time.frameCount;
                return true;
            }
            if (Time.frameCount <= _primaryReleaseFrame) return true;
            _primaryPointerCaptured = false;
            _primaryReleaseFrame = -1;
            return false;
        }

        internal static void Reset()
        {
            _owner = null;
            _pollingCancel = false;
            _primaryPointerCaptured = false;
            _primaryReleaseFrame = -1;
            _cancelFrame = -1;
        }

        internal static void RenewCursorLease()
        {
            _owner?.RenewCursorLease();
            NativeGroupInvitationPopup.RenewCursorLease();
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PortalEditorPlayerInputPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("chazman.RunicBuildCamera", "chazman.RunicStorage")]
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__instance == Player.m_localPlayer && PortalEditorInputGuard.IsOpen)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CanMove))]
    internal static class PortalEditorMovementPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__instance == Player.m_localPlayer && PortalEditorInputGuard.IsOpen)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class PortalEditorCursorLeasePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => PortalEditorInputGuard.RenewCursorLease();
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
    internal static class PortalEditorEscapePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage")]
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!PortalEditorInputGuard.SuppressEscape(key)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class PortalEditorHeldButtonPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage", "chazman.RunicProduction")]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!PortalEditorInputGuard.SuppressButton(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class PortalEditorDownButtonPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage", "chazman.RunicProduction")]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!PortalEditorInputGuard.SuppressButton(name)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class PortalEditorUpButtonPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyAfter("chazman.RunicStorage")]
        [HarmonyBefore("chazman.RunicAgriculture", "chazman.RunicInventory")]
        private static bool Prefix(string name, ref bool __result)
        {
            if (!PortalEditorInputGuard.SuppressButton(name)) return true;
            __result = false;
            return false;
        }
    }
}
