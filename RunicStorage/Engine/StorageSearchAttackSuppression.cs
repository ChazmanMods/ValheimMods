using System;

namespace RunicStorage.Engine
{
    /// <summary>
    /// Keeps a primary-pointer click owned by the Storage picker until its release has been
    /// observed for a complete frame. The native uGUI surface still receives the click; only
    /// Valheim's named gameplay Attack action is suppressed.
    /// </summary>
    internal sealed class StorageSearchAttackSuppression
    {
        internal const string PrimaryAttackAction = "Attack";

        private bool _pointerCaptured;
        private int _releaseFrame = -1;

        internal void CapturePrimaryPointer()
        {
            _pointerCaptured = true;
            _releaseFrame = -1;
        }

        internal bool ShouldSuppress(
            string actionName,
            bool pickerOpen,
            bool primaryAttackHeld,
            int frame)
        {
            if (!IsPrimaryAttack(actionName)) return false;

            if (pickerOpen)
            {
                if (primaryAttackHeld) CapturePrimaryPointer();
                return true;
            }

            if (!_pointerCaptured) return false;
            if (primaryAttackHeld)
            {
                _releaseFrame = -1;
                return true;
            }

            if (_releaseFrame < 0)
            {
                _releaseFrame = frame;
                return true;
            }
            if (frame <= _releaseFrame) return true;

            Reset();
            return false;
        }

        internal void Reset()
        {
            _pointerCaptured = false;
            _releaseFrame = -1;
        }

        internal static bool IsPrimaryAttack(string actionName) =>
            string.Equals(actionName, PrimaryAttackAction, StringComparison.Ordinal);
    }
}
