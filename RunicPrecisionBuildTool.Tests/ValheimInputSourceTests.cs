using System.Collections.Generic;
using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class ValheimInputSourceTests
    {
        internal static void Register()
        {
            TestRunner.Run("Valheim input source routes every input category through one backend", RoutesAllInputCategories);
            TestRunner.Run("Valheim input source latches wheel and key-down edges per frame", LatchesPerFrame);
            TestRunner.Run("Same-frame key-down edge remains usable as a fine modifier", KeyDownEdgeActsAsHeldModifier);
            TestRunner.Run("Valheim-native Shift layers roll while Ctrl stays transparent", DrivesReportedWheelChords);
        }

        private static void RoutesAllInputCategories()
        {
            int frame = 25;
            HashSet<KeyCode> held = new HashSet<KeyCode>
            {
                KeyCode.LeftAlt,
                KeyCode.LeftControl,
                KeyCode.V
            };
            HashSet<KeyCode> down = new HashSet<KeyCode> { KeyCode.F10 };
            ValheimInputSource source = new ValheimInputSource(
                held.Contains,
                down.Contains,
                () => -120f,
                () => frame);

            TestAssert.True(source.IsPressed(KeyCode.LeftAlt));
            TestAssert.True(source.IsPressed(KeyCode.LeftControl));
            TestAssert.True(source.IsPressed(KeyCode.V));
            TestAssert.True(source.WasPressedThisFrame(KeyCode.F10));
            TestAssert.Near(-120f, source.ReadScrollDelta(), 0f);
        }

        private static void LatchesPerFrame()
        {
            int frame = 70;
            int downReads = 0;
            int scrollReads = 0;
            ValheimInputSource source = new ValheimInputSource(
                key => key == KeyCode.LeftControl,
                key =>
                {
                    downReads++;
                    return key == KeyCode.PageUp;
                },
                () =>
                {
                    scrollReads++;
                    return 1f;
                },
                () => frame);

            TestAssert.True(source.WasPressedThisFrame(KeyCode.PageUp));
            TestAssert.False(source.WasPressedThisFrame(KeyCode.PageUp));
            TestAssert.Equal(1, downReads);
            TestAssert.Near(1f, source.ReadScrollDelta(), 0f);
            TestAssert.Near(0f, source.ReadScrollDelta(), 0f);
            TestAssert.Equal(1, scrollReads);

            frame++;
            TestAssert.True(source.WasPressedThisFrame(KeyCode.PageUp));
            TestAssert.Near(1f, source.ReadScrollDelta(), 0f);
            TestAssert.Equal(2, downReads);
            TestAssert.Equal(2, scrollReads);
        }

        private static void KeyDownEdgeActsAsHeldModifier()
        {
            int frame = 90;
            int heldReads = 0;
            int downReads = 0;
            bool pressedEdge = true;
            ValheimInputSource source = new ValheimInputSource(
                key =>
                {
                    heldReads++;
                    return false;
                },
                key =>
                {
                    downReads++;
                    return pressedEdge && key == KeyCode.V;
                },
                () => 0f,
                () => frame);

            TestAssert.True(source.IsPressed(KeyCode.V));
            TestAssert.True(source.IsPressed(KeyCode.V));
            TestAssert.Equal(1, heldReads);
            TestAssert.Equal(1, downReads);

            // Observing the edge as held must not consume its key-down result.
            TestAssert.True(source.WasPressedThisFrame(KeyCode.V));
            TestAssert.False(source.WasPressedThisFrame(KeyCode.V));
            TestAssert.Equal(1, downReads);

            frame++;
            pressedEdge = false;
            TestAssert.False(source.IsPressed(KeyCode.V));
            TestAssert.Equal(2, heldReads);
            TestAssert.Equal(2, downReads);
        }

        private static void DrivesReportedWheelChords()
        {
            int frame = 100;
            HashSet<KeyCode> held = new HashSet<KeyCode>
            {
                KeyCode.LeftAlt,
                KeyCode.LeftControl
            };
            ValheimInputSource source = new ValheimInputSource(
                held.Contains,
                key => false,
                () => 1f,
                () => frame);
            InputRouter router = new InputRouter(source);

            InputFrameResult pitch = router.ProcessFrame(
                InputRouterTests.NewContext(),
                InputBindings.Default);
            TestAssert.Equal(SemanticCommandKind.RotatePitch, pitch.WheelCommand.Kind);
            TestAssert.Near(22.5f, pitch.WheelCommand.Delta, 0f);

            frame++;
            held.Clear();
            held.Add(KeyCode.LeftShift);
            held.Add(KeyCode.LeftControl);
            InputFrameResult roll = router.ProcessFrame(
                InputRouterTests.NewContext(),
                InputBindings.Default);
            TestAssert.Equal(SemanticCommandKind.RotateRoll, roll.WheelCommand.Kind);
            TestAssert.Near(22.5f, roll.WheelCommand.Delta, 0f);

            frame++;
            held.Clear();
            held.Add(KeyCode.V);
            InputFrameResult yaw = router.ProcessFrame(
                InputRouterTests.NewContext(),
                InputBindings.Default);
            TestAssert.Equal(SemanticCommandKind.RotateYaw, yaw.WheelCommand.Kind);
            TestAssert.Near(1f, yaw.WheelCommand.Delta, 0f);
        }
    }
}

// The core-only console-test build links the production adapter without loading Valheim's full
// game assembly. These members compile its default constructor; tests use the injected backend.
internal static class ZInput
{
    internal static bool GetKey(KeyCode key, bool logWarning = true) => false;

    internal static bool GetKeyDown(KeyCode key, bool logWarning = true) => false;

    internal static float GetMouseScrollWheel() => 0f;
}
