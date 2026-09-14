using System;

namespace RunicBuildCamera.Core
{
    internal static class CameraExitInput
    {
        private static readonly string[] HotbarButtons =
        {
            "Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4",
            "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8"
        };

        // Run inside native TakeInput, before its result is suppressed. Never synthesize
        // an equip action or override a native UI/input denial.
        internal static bool ReleaseIfRequested(bool nativeInput, Func<bool> requested, Action stop)
        {
            if (!nativeInput || !requested()) return false;
            stop();
            return true;
        }

        internal static bool HotbarRequested(Func<string, bool> down)
        {
            foreach (string button in HotbarButtons)
                if (down(button)) return true;
            return false;
        }

        internal static bool HideRequested(bool keyboardHide, bool alternateGamepadLayout,
            bool shortControllerRelease, bool inPlaceMode, bool altKeys)
        {
            if (alternateGamepadLayout)
                return !inPlaceMode && shortControllerRelease && altKeys;
            return keyboardHide || (shortControllerRelease && !altKeys && !inPlaceMode);
        }
    }
}
