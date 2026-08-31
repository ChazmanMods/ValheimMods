using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class ExtendedInputRouterTests
    {
        internal static void Register()
        {
            TestRunner.Run("Every default position/snap/repeat action emits its exact command", ExtendedActionsEmitExactly);
            TestRunner.Run("Every default axis-reset chord emits its exact command", AxisResetActionsEmitExactly);
            TestRunner.Run("Axis reset requires Shift and wins its unmodified match on the same key", ResetModifierArbitrationIsExact);
            TestRunner.Run("Precision mode toggle is an exact independently sampled action", PrecisionToggleIsExact);
            TestRunner.Run("Extended transform actions are gated with placement input", ExtendedActionsAreGated);
        }

        private static void ExtendedActionsEmitExactly()
        {
            AssertCommand(KeyCode.Keypad4, SemanticCommandKind.MatchPositionX);
            AssertCommand(KeyCode.Keypad5, SemanticCommandKind.MatchPositionY);
            AssertCommand(KeyCode.Keypad6, SemanticCommandKind.MatchPositionZ);
            AssertCommand(KeyCode.Keypad7, SemanticCommandKind.MatchPosition);
            AssertCommand(KeyCode.Keypad8, SemanticCommandKind.MatchTransform);
            AssertCommand(KeyCode.Keypad9, SemanticCommandKind.MatchSnapSide);
            AssertCommand(KeyCode.KeypadPeriod, SemanticCommandKind.RepeatTransform);
        }

        private static void AxisResetActionsEmitExactly()
        {
            AssertShiftCommand(KeyCode.Keypad1, SemanticCommandKind.ResetPitch);
            AssertShiftCommand(KeyCode.Keypad2, SemanticCommandKind.ResetRoll);
            AssertShiftCommand(KeyCode.Keypad3, SemanticCommandKind.ResetYaw);
            AssertShiftCommand(KeyCode.Keypad4, SemanticCommandKind.ResetSway);
            AssertShiftCommand(KeyCode.Keypad5, SemanticCommandKind.ResetHeave);
            AssertShiftCommand(KeyCode.Keypad6, SemanticCommandKind.ResetSurge);
        }

        private static void ResetModifierArbitrationIsExact()
        {
            FakeInputSource input = new FakeInputSource();
            input.Press(KeyCode.LeftShift);
            input.PressThisFrame(KeyCode.Keypad4);
            InputFrameResult result = new InputRouter(input).ProcessFrame(
                InputRouterTests.NewContext(), InputBindings.Default);
            TestAssert.Equal(SemanticCommandKind.ResetSway, result.DiscreteCommand.Kind);

            input = new FakeInputSource();
            input.PressThisFrame(KeyCode.Keypad4);
            result = new InputRouter(input).ProcessFrame(
                InputRouterTests.NewContext(), InputBindings.Default);
            TestAssert.Equal(SemanticCommandKind.MatchPositionX, result.DiscreteCommand.Kind);
        }

        private static void PrecisionToggleIsExact()
        {
            FakeInputSource input = new FakeInputSource();
            InputRouter router = new InputRouter(input);
            input.PressThisFrame(KeyCode.P);
            router.ProcessFrame(InputRouterTests.NewContext(enabled: false), InputBindings.Default);
            TestAssert.True(router.WasExactActionPressed(
                InputBindings.Default.PrecisionModeToggle, InputBindings.Default));

            input = new FakeInputSource();
            router = new InputRouter(input);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.P);
            router.ProcessFrame(InputRouterTests.NewContext(enabled: false), InputBindings.Default);
            TestAssert.False(router.WasExactActionPressed(
                InputBindings.Default.PrecisionModeToggle, InputBindings.Default));
        }

        private static void ExtendedActionsAreGated()
        {
            FakeInputSource input = new FakeInputSource();
            input.PressThisFrame(KeyCode.Keypad8);
            InputFrameResult result = new InputRouter(input).ProcessFrame(
                InputRouterTests.NewContext(takeInput: false), InputBindings.Default);
            TestAssert.True(result.DiscreteCommand.IsNone);
        }

        private static void AssertCommand(KeyCode key, SemanticCommandKind expected)
        {
            FakeInputSource input = new FakeInputSource();
            input.PressThisFrame(key);
            InputFrameResult result = new InputRouter(input).ProcessFrame(
                InputRouterTests.NewContext(), InputBindings.Default);
            TestAssert.Equal(expected, result.DiscreteCommand.Kind);
        }

        private static void AssertShiftCommand(KeyCode key, SemanticCommandKind expected)
        {
            FakeInputSource input = new FakeInputSource();
            input.Press(KeyCode.LeftShift);
            input.PressThisFrame(key);
            InputFrameResult result = new InputRouter(input).ProcessFrame(
                InputRouterTests.NewContext(), InputBindings.Default);
            TestAssert.Equal(expected, result.DiscreteCommand.Kind);
        }
    }
}
