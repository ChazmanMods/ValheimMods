using System;

namespace RunicCrafting.Domain
{
    internal static class ManualInteractionPolicy
    {
        internal static bool HasFuelRoom(float fuel, float maximum) =>
            !float.IsNaN(fuel) && !float.IsInfinity(fuel) && fuel >= 0f &&
            !float.IsNaN(maximum) && !float.IsInfinity(maximum) && maximum >= 1f &&
            fuel <= maximum - 1f;

        internal static bool ShouldRefuelFire(bool refillable, bool infinite, bool hold, bool alt,
            bool canToggle, float fuel, float maximum, float repeatInterval, float elapsed) =>
            refillable && !infinite && HasFuelRoom(fuel, maximum) &&
            !(hold && (repeatInterval <= 0f || elapsed < repeatInterval)) &&
            !(canToggle && !hold && !alt && fuel > 0f);

        internal static bool ShouldLoadCooking(bool finishedFood, int freeSlot, bool needsFire,
            bool fireLit, bool carriedFood) =>
            !finishedFood && freeSlot >= 0 && (!needsFire || fireLit) && !carriedFood;

        internal static bool AllowsProtection(bool governed, int state) => !governed || state == 1;
    }
}
