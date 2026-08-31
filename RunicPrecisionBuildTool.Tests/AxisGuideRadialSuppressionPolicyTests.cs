using System.Collections.Generic;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class AxisGuideRadialSuppressionPolicyTests
    {
        internal static void Register()
        {
            TestRunner.Run(
                "Radial input is suppressed only for an eligible reserved G guide",
                EligibleReservedGuideSuppresses);
            TestRunner.Run(
                "Every guide placement gate independently preserves radial input",
                EveryPlacementGateFailsOpen);
            TestRunner.Run(
                "Unreserved or released G preserves radial input",
                UnreservedOrReleasedFailsOpen);
            TestRunner.Run(
                "G main-key reservations require their configured modifiers",
                MainKeyRequiresDeclaredModifiers);
            TestRunner.Run(
                "G leader reservations precede their chord main key",
                ModifierLeaderReservesBeforeMainKey);
        }

        private static void EligibleReservedGuideSuppresses() =>
            TestAssert.True(Evaluate());

        private static void EveryPlacementGateFailsOpen()
        {
            TestAssert.False(Evaluate(canRun: false));
            TestAssert.False(Evaluate(isLocalPlayer: false));
            TestAssert.False(Evaluate(isInPlaceMode: false));
            TestAssert.False(Evaluate(hasActiveRotatableGhost: false));
            TestAssert.False(Evaluate(displayGateOpen: false));
            TestAssert.False(Evaluate(inputGateOpen: false));
        }

        private static void UnreservedOrReleasedFailsOpen()
        {
            TestAssert.False(Evaluate(configuredGuideReservesG: false));
            TestAssert.False(Evaluate(guideReservationIsActive: false));
        }

        private static void MainKeyRequiresDeclaredModifiers()
        {
            InputChord ctrlG = new InputChord(KeyCode.G, new[] { KeyCode.LeftControl });
            ReservationInputSource bareG = new ReservationInputSource(KeyCode.G);
            ReservationInputSource ctrlAndG =
                new ReservationInputSource(KeyCode.G, KeyCode.LeftControl);

            TestAssert.False(
                AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(ctrlG, bareG));
            TestAssert.True(
                AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(ctrlG, ctrlAndG));
        }

        private static void ModifierLeaderReservesBeforeMainKey()
        {
            InputChord gThenH = new InputChord(KeyCode.H, new[] { KeyCode.G });
            ReservationInputSource gOnly = new ReservationInputSource(KeyCode.G);
            TestAssert.True(
                AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(gThenH, gOnly),
                "G must be reserved before H arrives because Valheim can react to G-down.");

            InputChord gThenHWithCtrl =
                new InputChord(KeyCode.H, new[] { KeyCode.G, KeyCode.LeftControl });
            TestAssert.False(
                AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(
                    gThenHWithCtrl, gOnly),
                "A second declared modifier must still be held.");
            TestAssert.True(
                AxisGuideRadialSuppressionPolicy.IsGuideReservationActive(
                    gThenHWithCtrl,
                    new ReservationInputSource(KeyCode.G, KeyCode.LeftControl)));
        }

        private static bool Evaluate(
            bool canRun = true,
            bool isLocalPlayer = true,
            bool isInPlaceMode = true,
            bool hasActiveRotatableGhost = true,
            bool displayGateOpen = true,
            bool inputGateOpen = true,
            bool configuredGuideReservesG = true,
            bool guideReservationIsActive = true) =>
            AxisGuideRadialSuppressionPolicy.ShouldSuppress(
                canRun,
                isLocalPlayer,
                isInPlaceMode,
                hasActiveRotatableGhost,
                displayGateOpen,
                inputGateOpen,
                configuredGuideReservesG,
                guideReservationIsActive);

        private sealed class ReservationInputSource : IInputSource
        {
            private readonly HashSet<KeyCode> _held;

            internal ReservationInputSource(params KeyCode[] held)
            {
                _held = new HashSet<KeyCode>(held);
            }

            public bool IsPressed(KeyCode key) => _held.Contains(key);

            public bool WasPressedThisFrame(KeyCode key) => false;

            public float ReadScrollDelta() => 0f;
        }
    }
}
