using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime
{
    /// <summary>
    /// Bridges the picker's raw OnGUI pointer observation to the pure attack-suppression latch.
    /// Presentation itself is native uGUI; this observer exists only because it sees the press
    /// before a native Button can close and destroy the Canvas. Reading
    /// ButtonDef.Held avoids calling a patched ZInput getter recursively and follows the player's
    /// actual primary-attack binding.
    /// </summary>
    internal static class StorageSearchGameplayInputGuard
    {
        private static readonly StorageSearchAttackSuppression AttackSuppression =
            new StorageSearchAttackSuppression();
        private static bool _pollingPickerEscape;
        private static int _pickerEscapeFrame = -1;

        internal static bool PollPickerEscape()
        {
            _pollingPickerEscape = true;
            try
            {
                bool pressed = ZInput.GetKeyDown(KeyCode.Escape, false);
                if (pressed) _pickerEscapeFrame = Time.frameCount;
                return pressed;
            }
            finally
            {
                _pollingPickerEscape = false;
            }
        }

        internal static bool ShouldSuppressEscape(KeyCode key)
        {
            if (key != KeyCode.Escape || _pollingPickerEscape) return false;
            return Plugin.SearchPanelOpen || Time.frameCount == _pickerEscapeFrame;
        }

        internal static void CaptureGuiPointer(Event current)
        {
            if (current == null || current.button != 0) return;
            if (current.type == EventType.MouseDown ||
                current.type == EventType.MouseUp ||
                current.type == EventType.MouseDrag)
                AttackSuppression.CapturePrimaryPointer();
        }

        internal static bool ShouldSuppressPrimaryAttack(string actionName)
        {
            if (!StorageSearchAttackSuppression.IsPrimaryAttack(actionName)) return false;

            bool held = false;
            try
            {
                ZInput input = ZInput.instance;
                if (input != null)
                {
                    ZInput.ButtonDef attack = input.GetButtonDef(
                        StorageSearchAttackSuppression.PrimaryAttackAction);
                    held = attack != null && attack.Held;
                }
            }
            catch { held = false; }

            return AttackSuppression.ShouldSuppress(
                actionName,
                Plugin.SearchPanelOpen,
                held,
                Time.frameCount);
        }

        internal static void Reset()
        {
            AttackSuppression.Reset();
            _pollingPickerEscape = false;
            _pickerEscapeFrame = -1;
        }
    }
}
