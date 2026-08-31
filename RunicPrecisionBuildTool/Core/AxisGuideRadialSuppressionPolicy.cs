using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Fail-open policy for the vanilla radial action that can share G with Runic's held guide.
    /// Runtime code supplies the Unity/Valheim placement facts; the decision remains testable
    /// without loading the game.
    /// </summary>
    internal static class AxisGuideRadialSuppressionPolicy
    {
        /// <summary>
        /// Determines whether the configured guide chord owns G at this instant. When G is the
        /// chord's main key, every declared modifier must already be held. When G is a
        /// modifier/leader, its down-edge reserves the chord while all other declared modifiers
        /// remain required.
        /// </summary>
        internal static bool IsGuideReservationActive(
            InputChord chord,
            IInputSource input)
        {
            const KeyCode radialKey = KeyCode.G;
            if (input == null || chord.IsEmpty || !chord.Contains(radialKey) ||
                !input.IsPressed(radialKey))
            {
                return false;
            }

            for (int index = 0; index < chord.ModifierCount; index++)
            {
                KeyCode modifier = chord.GetModifier(index);
                if (modifier == KeyCode.None || modifier == radialKey)
                    continue;
                if (!input.IsPressed(modifier))
                    return false;
            }

            return true;
        }

        internal static bool ShouldSuppress(
            bool canRun,
            bool isLocalPlayer,
            bool isInPlaceMode,
            bool hasActiveRotatableGhost,
            bool displayGateOpen,
            bool inputGateOpen,
            bool configuredGuideReservesG,
            bool guideReservationIsActive)
        {
            return canRun &&
                   isLocalPlayer &&
                   isInPlaceMode &&
                   hasActiveRotatableGhost &&
                   displayGateOpen &&
                   inputGateOpen &&
                   configuredGuideReservesG &&
                   guideReservationIsActive;
        }
    }
}
