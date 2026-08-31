using UnityEngine;

namespace QuietBuildRotation.Tests
{
    internal static class InputRouterTests
    {
        private static readonly InputBindings Bindings = InputBindings.Default;

        internal static void Register()
        {
            TestRunner.Run("Bare wheel is deterministic configured yaw", BareWheelYaw);
            TestRunner.Run("Circle divisions determine normal and fine rotation steps", CircleDivisions);
            TestRunner.Run("Layered normal wheel chords select all three axes with roll priority", NormalWheelChords);
            TestRunner.Run("Layered fine wheel chords select all three axes with roll priority", FineWheelChords);
            TestRunner.Run("Unconfigured Ctrl does not alter layered wheel selection", ControlIsTransparentToDefaults);
            TestRunner.Run("Unrelated selector keys still invalidate exact wheel chords", ExtraKeysInvalidateWheel);
            TestRunner.Run("Custom complete wheel chords require every declared key", CustomWheelChords);
            TestRunner.Run("Movement actions use independent axis distances", MovementCommands);
            TestRunner.Run("Fine movement actions use independent fine distances", FineMovementCommands);
            TestRunner.Run("Movement action chords are fully configurable", CustomMovementChord);
            TestRunner.Run("Held movement action keys do not repeat", MovementDoesNotRepeat);
            TestRunner.Run("None disables non-yaw wheel and discrete actions", NoneDisablesActions);
            TestRunner.Run("Reset wins deterministic discrete-command precedence", DiscretePrecedence);
            TestRunner.Run("Default match bindings use the standalone keypad cluster", DefaultMatchBindings);
            TestRunner.Run("Match is emitted only on an exact rotatable chord", MatchChord);
            TestRunner.Run("Pitch, roll, and yaw match actions emit independent commands", ComponentMatchChords);
            TestRunner.Run("Component match actions accept configurable complete chords", CustomComponentMatchChord);
            TestRunner.Run("A component match wins deterministic simultaneous-key precedence", ComponentMatchPrecedence);
            TestRunner.Run("Held component match keys do not repeat", ComponentMatchDoesNotRepeat);
            TestRunner.Run("Axis guides are true only while the default hold chord is held", AxisGuideHoldAndRelease);
            TestRunner.Run("Axis guide hold chords require configured keys and allow other controls", CustomAxisGuideChord);
            TestRunner.Run("Axis guide state is false on every gated input frame", AxisGuideGates);
            TestRunner.Run("Axis guide polling does not consume its key-down or wheel command", AxisGuideIsNonConsuming);
            TestRunner.Run("Configured action keys invalidate otherwise exact chords", ConfiguredKeysInvalidateDiscrete);
            TestRunner.Run("Failed higher-priority chord probes preserve shared key-downs", FailedChordProbeDoesNotConsume);
            TestRunner.Run("Focus/input/session gates discard one sampled wheel value", InputGates);
            TestRunner.Run("Discarded gated scroll does not reappear on resume", GatedScrollDoesNotReplay);
            TestRunner.Run("Non-rotatable ghosts receive no rotation or match command", NonRotatableGate);
            TestRunner.Run("Translation capability gate blocks movement", TranslationGate);
            TestRunner.Run("One frame can emit at most one wheel and one discrete command", FrameCommandBudget);
        }

        private static void BareWheelYaw()
        {
            FakeInputSource input = NewInput(2f);
            InputFrameResult result = Route(input);
            AssertCommand(SemanticCommandKind.RotateYaw, 22.5f, result.WheelCommand);
            TestAssert.True(result.ConsumeWheel);
            TestAssert.Equal(RotationAxis.Yaw, result.ActiveAxis);
            TestAssert.Near(22.5f, result.ActiveStep, 0f);
            TestAssert.False(result.ActiveStepIsFine);
            TestAssert.Equal(1, input.ScrollReadCount);

            input.ScrollDelta = -0.25f;
            result = Route(input);
            AssertCommand(SemanticCommandKind.RotateYaw, -22.5f, result.WheelCommand);
        }

        private static void CircleDivisions()
        {
            FakeInputSource input = NewInput(1f);
            InputFrameResult normal = Route(input, NewContext(rotationIncrements: 8));
            AssertCommand(SemanticCommandKind.RotateYaw, 45f, normal.WheelCommand);

            input = NewInput(-1f);
            input.Press(KeyCode.V);
            InputFrameResult fine = Route(
                input,
                NewContext(fineRotationIncrements: 32));
            AssertCommand(SemanticCommandKind.RotateYaw, -11.25f, fine.WheelCommand);
            TestAssert.True(fine.ActiveStepIsFine);

            input = NewInput(1f);
            InputFrameResult invalid = Route(input, NewContext(rotationIncrements: 0));
            TestAssert.True(invalid.WheelCommand.IsNone);
            TestAssert.False(invalid.ConsumeWheel);
        }

        private static void NormalWheelChords()
        {
            FakeInputSource input = NewInput(1f);
            InputFrameResult yaw = Route(input);
            AssertCommand(SemanticCommandKind.RotateYaw, 22.5f, yaw.WheelCommand);

            input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            InputFrameResult pitch = Route(input);
            AssertCommand(SemanticCommandKind.RotatePitch, 22.5f, pitch.WheelCommand);

            input = NewInput(1f);
            input.Press(KeyCode.LeftShift);
            InputFrameResult roll = Route(input);
            AssertCommand(SemanticCommandKind.RotateRoll, 22.5f, roll.WheelCommand);
            TestAssert.Equal(RotationAxis.Roll, roll.ActiveAxis);

            input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.LeftShift);
            InputFrameResult layeredRoll = Route(input);
            AssertCommand(SemanticCommandKind.RotateRoll, 22.5f, layeredRoll.WheelCommand);
        }

        private static void FineWheelChords()
        {
            FakeInputSource input = NewInput(-1f);
            input.Press(KeyCode.V);
            InputFrameResult yaw = Route(input);
            AssertCommand(SemanticCommandKind.RotateYaw, -1f, yaw.WheelCommand);
            TestAssert.True(yaw.ActiveStepIsFine);

            input = NewInput(-1f);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.V);
            InputFrameResult pitch = Route(input);
            AssertCommand(SemanticCommandKind.RotatePitch, -1f, pitch.WheelCommand);

            input = NewInput(-1f);
            input.Press(KeyCode.V);
            input.Press(KeyCode.LeftShift);
            InputFrameResult roll = Route(input);
            AssertCommand(SemanticCommandKind.RotateRoll, -1f, roll.WheelCommand);

            input = NewInput(-1f);
            input.Press(KeyCode.V);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.LeftShift);
            InputFrameResult layeredRoll = Route(input);
            AssertCommand(SemanticCommandKind.RotateRoll, -1f, layeredRoll.WheelCommand);
        }

        private static void ControlIsTransparentToDefaults()
        {
            AssertWheelWithControl(
                SemanticCommandKind.RotateYaw, false, KeyCode.LeftControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotatePitch, false,
                KeyCode.LeftAlt, KeyCode.LeftControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotateRoll, false,
                KeyCode.LeftShift, KeyCode.LeftControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotateRoll, false,
                KeyCode.LeftAlt, KeyCode.LeftShift, KeyCode.LeftControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotateYaw, true,
                KeyCode.V, KeyCode.RightControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotatePitch, true,
                KeyCode.V, KeyCode.LeftAlt, KeyCode.RightControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotateRoll, true,
                KeyCode.V, KeyCode.LeftShift, KeyCode.RightControl);
            AssertWheelWithControl(
                SemanticCommandKind.RotateRoll, true,
                KeyCode.V, KeyCode.LeftAlt, KeyCode.LeftShift, KeyCode.RightControl);
        }

        private static void ExtraKeysInvalidateWheel()
        {
            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.RightShift);
            InputFrameResult shiftedPitch = Route(input);
            TestAssert.True(shiftedPitch.WheelCommand.IsNone);
            TestAssert.False(shiftedPitch.ConsumeWheel);
            TestAssert.Equal(RotationAxis.None, shiftedPitch.ActiveAxis);
        }

        private static void AssertWheelWithControl(
            SemanticCommandKind expectedKind,
            bool fine,
            params KeyCode[] held)
        {
            FakeInputSource input = NewInput(1f);
            foreach (KeyCode key in held)
                input.Press(key);
            InputFrameResult result = Route(input);
            AssertCommand(expectedKind, fine ? 1f : 22.5f, result.WheelCommand);
            TestAssert.True(result.ConsumeWheel);
            TestAssert.Equal(fine, result.ActiveStepIsFine);
        }

        private static void CustomWheelChords()
        {
            RotationInputBindings rotation = new RotationInputBindings(
                new InputChord(KeyCode.Q),
                new InputChord(KeyCode.E, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.R, new[] { KeyCode.RightShift }),
                new InputChord(KeyCode.T),
                new InputChord(KeyCode.Y, new[] { KeyCode.LeftControl }),
                new InputChord(KeyCode.U, new[] { KeyCode.RightControl }));
            InputBindings custom = new InputBindings(
                rotation,
                new InputChord(KeyCode.F),
                new InputChord(KeyCode.P),
                new InputChord(KeyCode.R),
                new InputChord(KeyCode.Y),
                new InputChord(KeyCode.G),
                new InputChord(KeyCode.F10),
                MovementInputBindings.Default);

            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.E);
            InputFrameResult missingModifier =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(missingModifier.WheelCommand.IsNone);

            input.Press(KeyCode.LeftShift);
            InputFrameResult pitch =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            AssertCommand(SemanticCommandKind.RotatePitch, 22.5f, pitch.WheelCommand);

            input = NewInput(1f);
            input.Press(KeyCode.Y);
            input.Press(KeyCode.LeftControl);
            InputFrameResult finePitch =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            AssertCommand(SemanticCommandKind.RotatePitch, 1f, finePitch.WheelCommand);
            TestAssert.True(finePitch.ActiveStepIsFine);

            input = NewInput(1f);
            input.Press(KeyCode.R);
            input.Press(KeyCode.RightShift);
            input.Press(KeyCode.E);
            InputFrameResult partialLayer =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(partialLayer.WheelCommand.IsNone);

            input.Press(KeyCode.LeftShift);
            InputFrameResult completeLayer =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            AssertCommand(SemanticCommandKind.RotateRoll, 22.5f, completeLayer.WheelCommand);
        }

        private static void MovementCommands()
        {
            InputContext context = NewContext(
                sideStep: 0.2f,
                upDownStep: 0.3f,
                forwardBackStep: 0.4f);
            AssertMovement(
                KeyCode.UpArrow, false, SemanticCommandKind.MoveHeave, 0.3f, context);
            AssertMovement(
                KeyCode.DownArrow, false, SemanticCommandKind.MoveHeave, -0.3f, context);
            AssertMovement(
                KeyCode.LeftArrow, false, SemanticCommandKind.MoveSway, -0.2f, context);
            AssertMovement(
                KeyCode.RightArrow, false, SemanticCommandKind.MoveSway, 0.2f, context);
            AssertMovement(
                KeyCode.PageUp, false, SemanticCommandKind.MoveSurge, 0.4f, context);
            AssertMovement(
                KeyCode.PageDown, false, SemanticCommandKind.MoveSurge, -0.4f, context);
        }

        private static void FineMovementCommands()
        {
            InputContext context = NewContext(
                fineSideStep: 0.02f,
                fineUpDownStep: 0.03f,
                fineForwardBackStep: 0.04f);
            AssertMovement(
                KeyCode.UpArrow, true, SemanticCommandKind.MoveHeave, 0.03f, context);
            AssertMovement(
                KeyCode.DownArrow, true, SemanticCommandKind.MoveHeave, -0.03f, context);
            AssertMovement(
                KeyCode.LeftArrow, true, SemanticCommandKind.MoveSway, -0.02f, context);
            AssertMovement(
                KeyCode.RightArrow, true, SemanticCommandKind.MoveSway, 0.02f, context);
            AssertMovement(
                KeyCode.PageUp, true, SemanticCommandKind.MoveSurge, 0.04f, context);
            AssertMovement(
                KeyCode.PageDown, true, SemanticCommandKind.MoveSurge, -0.04f, context);
        }

        private static void CustomMovementChord()
        {
            MovementInputBindings movement = DisabledMovement(
                right: new InputChord(KeyCode.D, new[] { KeyCode.RightAlt }),
                fineForward: new InputChord(KeyCode.W, new[] { KeyCode.RightControl }));
            InputBindings custom = new InputBindings(
                RotationInputBindings.Default,
                new InputChord(KeyCode.F),
                new InputChord(KeyCode.P),
                new InputChord(KeyCode.R),
                new InputChord(KeyCode.Y),
                new InputChord(KeyCode.G),
                new InputChord(KeyCode.F10),
                movement);

            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.RightAlt);
            input.PressThisFrame(KeyCode.D);
            InputFrameResult right = new InputRouter(input).ProcessFrame(
                NewContext(sideStep: 0.7f), custom);
            AssertCommand(SemanticCommandKind.MoveSway, 0.7f, right.DiscreteCommand);

            input = NewInput(0f);
            input.Press(KeyCode.RightControl);
            input.PressThisFrame(KeyCode.W);
            InputFrameResult forward = new InputRouter(input).ProcessFrame(
                NewContext(fineForwardBackStep: 0.08f), custom);
            AssertCommand(SemanticCommandKind.MoveSurge, 0.08f, forward.DiscreteCommand);
        }

        private static void MovementDoesNotRepeat()
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.UpArrow);
            InputFrameResult result = Route(input);
            TestAssert.True(result.DiscreteCommand.IsNone);
        }

        private static void NoneDisablesActions()
        {
            RotationInputBindings rotation = new RotationInputBindings(
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None));
            InputBindings custom = new InputBindings(
                rotation,
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                DisabledMovement());

            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            InputFrameResult pitchDisabled =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(pitchDisabled.WheelCommand.IsNone);

            input = NewInput(0f);
            input.PressThisFrame(KeyCode.UpArrow);
            InputFrameResult movementDisabled =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(movementDisabled.DiscreteCommand.IsNone);

            // The one deliberate exception: the empty yaw chord is the deterministic bare wheel.
            input = NewInput(1f);
            InputFrameResult yaw = new InputRouter(input).ProcessFrame(NewContext(), custom);
            AssertCommand(SemanticCommandKind.RotateYaw, 22.5f, yaw.WheelCommand);
        }

        private static void DiscretePrecedence()
        {
            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(KeyCode.Keypad0);
            input.PressThisFrame(KeyCode.F10);
            InputFrameResult result = Route(input);
            TestAssert.Equal(SemanticCommandKind.Reset, result.DiscreteCommand.Kind);
        }

        private static void DefaultMatchBindings()
        {
            TestAssert.Equal(KeyCode.Keypad0, Bindings.Match.MainKey);
            TestAssert.Equal(0, Bindings.Match.ModifierCount);
            TestAssert.Equal(KeyCode.Keypad1, Bindings.MatchPitch.MainKey);
            TestAssert.Equal(KeyCode.Keypad2, Bindings.MatchRoll.MainKey);
            TestAssert.Equal(KeyCode.Keypad3, Bindings.MatchYaw.MainKey);
            TestAssert.Equal(0, Bindings.MatchPitch.ModifierCount);
            TestAssert.Equal(0, Bindings.MatchRoll.ModifierCount);
            TestAssert.Equal(0, Bindings.MatchYaw.ModifierCount);
        }

        private static void MatchChord()
        {
            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(KeyCode.Keypad0);
            InputFrameResult result = Route(input);
            TestAssert.Equal(SemanticCommandKind.MatchOrientation, result.DiscreteCommand.Kind);

            input = NewInput(0f);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.Keypad0);
            result = Route(input);
            TestAssert.True(result.DiscreteCommand.IsNone);
        }

        private static void ComponentMatchChords()
        {
            AssertComponentMatch(KeyCode.Keypad1, SemanticCommandKind.MatchPitch);
            AssertComponentMatch(KeyCode.Keypad2, SemanticCommandKind.MatchRoll);
            AssertComponentMatch(KeyCode.Keypad3, SemanticCommandKind.MatchYaw);
        }

        private static void CustomComponentMatchChord()
        {
            InputBindings custom = new InputBindings(
                RotationInputBindings.Default,
                new InputChord(KeyCode.F),
                new InputChord(KeyCode.G, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.H, new[] { KeyCode.RightControl }),
                new InputChord(KeyCode.J, new[] { KeyCode.RightAlt }),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.F10),
                MovementInputBindings.Default);

            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(KeyCode.G);
            InputFrameResult missingModifier =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(missingModifier.DiscreteCommand.IsNone);

            input = NewInput(0f);
            input.Press(KeyCode.LeftShift);
            input.PressThisFrame(KeyCode.G);
            InputFrameResult pitch =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.Equal(SemanticCommandKind.MatchPitch, pitch.DiscreteCommand.Kind);

            input = NewInput(0f);
            input.Press(KeyCode.RightControl);
            input.PressThisFrame(KeyCode.H);
            InputFrameResult roll =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.Equal(SemanticCommandKind.MatchRoll, roll.DiscreteCommand.Kind);

            input = NewInput(0f);
            input.Press(KeyCode.RightAlt);
            input.PressThisFrame(KeyCode.J);
            InputFrameResult yaw =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.Equal(SemanticCommandKind.MatchYaw, yaw.DiscreteCommand.Kind);

            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.J);
            InputFrameResult extraModifier =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(extraModifier.DiscreteCommand.IsNone);
        }

        private static void ComponentMatchPrecedence()
        {
            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(KeyCode.Keypad0);
            input.PressThisFrame(KeyCode.Keypad1);
            InputFrameResult simultaneous = Route(input);
            TestAssert.Equal(
                SemanticCommandKind.MatchPitch,
                simultaneous.DiscreteCommand.Kind);

            InputBindings sharedMainKey = new InputBindings(
                RotationInputBindings.Default,
                new InputChord(KeyCode.F),
                new InputChord(KeyCode.F, new[] { KeyCode.Alpha1 }),
                new InputChord(KeyCode.R),
                new InputChord(KeyCode.Y),
                new InputChord(KeyCode.G),
                new InputChord(KeyCode.F10),
                MovementInputBindings.Default);

            input = NewInput(0f);
            input.Press(KeyCode.Alpha1);
            input.PressThisFrame(KeyCode.F);
            InputFrameResult component =
                new InputRouter(input).ProcessFrame(NewContext(), sharedMainKey);
            TestAssert.Equal(SemanticCommandKind.MatchPitch, component.DiscreteCommand.Kind);

            input = NewInput(0f);
            input.PressThisFrame(KeyCode.F);
            InputFrameResult exact =
                new InputRouter(input).ProcessFrame(NewContext(), sharedMainKey);
            TestAssert.Equal(SemanticCommandKind.MatchOrientation, exact.DiscreteCommand.Kind);
        }

        private static void ComponentMatchDoesNotRepeat()
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.Keypad1);
            InputFrameResult result = Route(input);
            TestAssert.True(result.DiscreteCommand.IsNone);
        }

        private static void AxisGuideHoldAndRelease()
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.G);
            InputFrameResult held = Route(input);
            TestAssert.True(held.AxisGuidesHeld);

            InputFrameResult released = Route(NewInput(0f));
            TestAssert.False(released.AxisGuidesHeld);
        }

        private static void CustomAxisGuideChord()
        {
            InputBindings custom = BindingsWithAxis(
                new InputChord(KeyCode.H, new[] { KeyCode.RightControl }));

            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.H);
            InputFrameResult missingModifier =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.False(missingModifier.AxisGuidesHeld);

            input = NewInput(0f);
            input.Press(KeyCode.RightControl);
            input.Press(KeyCode.H);
            InputFrameResult complete =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(complete.AxisGuidesHeld);

            input = NewInput(1f);
            input.Press(KeyCode.RightControl);
            input.Press(KeyCode.H);
            input.PressThisFrame(KeyCode.Keypad0);
            InputFrameResult simultaneous =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(simultaneous.AxisGuidesHeld);
            AssertCommand(
                SemanticCommandKind.RotateYaw,
                22.5f,
                simultaneous.WheelCommand);
            TestAssert.Equal(
                SemanticCommandKind.MatchOrientation,
                simultaneous.DiscreteCommand.Kind);

            input.Press(KeyCode.LeftAlt);
            InputFrameResult extraModifier =
                new InputRouter(input).ProcessFrame(NewContext(), custom);
            TestAssert.True(extraModifier.AxisGuidesHeld);
        }

        private static void AxisGuideGates()
        {
            AssertAxisGuideGated(NewContext(enabled: false));
            AssertAxisGuideGated(NewContext(runtimeAvailable: false));
            AssertAxisGuideGated(NewContext(isFocused: false));
            AssertAxisGuideGated(NewContext(takeInput: false));
            AssertAxisGuideGated(NewContext(hasActiveGhost: false));
            AssertAxisGuideGated(NewContext(canRotate: false));
        }

        private static void AxisGuideIsNonConsuming()
        {
            FakeInputSource input = NewInput(1f);
            input.PressThisFrame(KeyCode.G);
            input.Press(KeyCode.LeftAlt);
            input.Press(KeyCode.LeftShift);
            input.Press(KeyCode.LeftControl);
            input.Press(KeyCode.V);
            input.PressThisFrame(KeyCode.Keypad0);
            InputFrameResult result = Route(input);

            TestAssert.True(result.AxisGuidesHeld);
            AssertCommand(SemanticCommandKind.RotateRoll, 1f, result.WheelCommand);
            TestAssert.True(result.DiscreteCommand.IsNone);
            TestAssert.True(input.WasPressedThisFrame(KeyCode.G));
        }

        private static void AssertAxisGuideGated(InputContext context)
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.G);
            InputFrameResult result = Route(input, context);
            TestAssert.False(result.AxisGuidesHeld);
        }

        private static void AssertComponentMatch(
            KeyCode key,
            SemanticCommandKind expectedKind)
        {
            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(key);
            InputFrameResult result = Route(input);
            TestAssert.Equal(expectedKind, result.DiscreteCommand.Kind);
        }

        private static void ConfiguredKeysInvalidateDiscrete()
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.V);
            input.PressThisFrame(KeyCode.Keypad0);
            InputFrameResult match = Route(input);
            TestAssert.True(match.DiscreteCommand.IsNone);

            input = NewInput(0f);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.F10);
            InputFrameResult reset = Route(input);
            TestAssert.True(reset.DiscreteCommand.IsNone);
        }

        private static void FailedChordProbeDoesNotConsume()
        {
            InputBindings sharedMainKey = new InputBindings(
                RotationInputBindings.Default,
                new InputChord(KeyCode.F),
                new InputChord(KeyCode.P),
                new InputChord(KeyCode.R),
                new InputChord(KeyCode.Y),
                new InputChord(KeyCode.G),
                new InputChord(KeyCode.F, new[] { KeyCode.LeftControl }),
                MovementInputBindings.Default);

            FakeInputSource input = NewInput(0f);
            input.PressThisFrame(KeyCode.F);
            InputFrameResult result =
                new InputRouter(input).ProcessFrame(NewContext(), sharedMainKey);
            TestAssert.Equal(SemanticCommandKind.MatchOrientation, result.DiscreteCommand.Kind);
        }

        private static void InputGates()
        {
            AssertGated(NewContext(isFocused: false));
            AssertGated(NewContext(takeInput: false));
            AssertGated(NewContext(hasActiveGhost: false));
            AssertGated(NewContext(enabled: false));
            AssertGated(NewContext(runtimeAvailable: false));
        }

        private static void GatedScrollDoesNotReplay()
        {
            FakeInputSource input = NewInput(1f);
            InputRouter router = new InputRouter(input);
            InputFrameResult gated = router.ProcessFrame(
                NewContext(isFocused: false), Bindings);
            TestAssert.True(gated.WheelCommand.IsNone);

            input.ScrollDelta = 0f;
            InputFrameResult resumed = router.ProcessFrame(NewContext(), Bindings);
            TestAssert.True(resumed.WheelCommand.IsNone);
            TestAssert.Equal(2, input.ScrollReadCount);
        }

        private static void NonRotatableGate()
        {
            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.V);
            input.PressThisFrame(KeyCode.Keypad0);
            InputFrameResult result = Route(input, NewContext(canRotate: false));
            TestAssert.True(result.WheelCommand.IsNone);
            TestAssert.True(result.DiscreteCommand.IsNone);
            TestAssert.False(result.ConsumeWheel);
            TestAssert.Equal(RotationAxis.None, result.ActiveAxis);
        }

        private static void TranslationGate()
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.UpArrow);
            InputFrameResult result = Route(input, NewContext(canTranslate: false));
            TestAssert.True(result.DiscreteCommand.IsNone);
        }

        private static void FrameCommandBudget()
        {
            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.UpArrow);
            InputFrameResult result = Route(input);
            AssertCommand(SemanticCommandKind.RotatePitch, 22.5f, result.WheelCommand);
            AssertCommand(SemanticCommandKind.MoveHeave, 0.25f, result.DiscreteCommand);
        }

        private static void AssertMovement(
            KeyCode key,
            bool fine,
            SemanticCommandKind kind,
            float delta,
            InputContext context)
        {
            FakeInputSource input = NewInput(0f);
            input.Press(KeyCode.LeftAlt);
            if (fine)
                input.Press(KeyCode.V);
            input.PressThisFrame(key);
            InputFrameResult result = Route(input, context);
            AssertCommand(kind, delta, result.DiscreteCommand);
        }

        private static void AssertGated(InputContext context)
        {
            FakeInputSource input = NewInput(1f);
            input.Press(KeyCode.LeftAlt);
            input.PressThisFrame(KeyCode.UpArrow);
            InputFrameResult result = Route(input, context);
            TestAssert.True(result.WheelCommand.IsNone);
            TestAssert.True(result.DiscreteCommand.IsNone);
            TestAssert.Equal(1, input.ScrollReadCount);
        }

        private static void AssertCommand(
            SemanticCommandKind expectedKind,
            float expectedDelta,
            SemanticCommand actual)
        {
            TestAssert.Equal(expectedKind, actual.Kind);
            TestAssert.Near(expectedDelta, actual.Delta, 0.000001f);
        }

        private static InputFrameResult Route(FakeInputSource input) =>
            Route(input, NewContext());

        private static InputFrameResult Route(FakeInputSource input, InputContext context)
        {
            InputRouter router = new InputRouter(input);
            return router.ProcessFrame(context, Bindings);
        }

        private static InputBindings BindingsWithAxis(InputChord axisGuides) =>
            new InputBindings(
                RotationInputBindings.Default,
                new InputChord(KeyCode.Keypad0),
                new InputChord(KeyCode.Keypad1),
                new InputChord(KeyCode.Keypad2),
                new InputChord(KeyCode.Keypad3),
                axisGuides,
                new InputChord(KeyCode.F10),
                MovementInputBindings.Default);

        internal static InputContext NewContext(
            bool enabled = true,
            bool runtimeAvailable = true,
            bool isFocused = true,
            bool takeInput = true,
            bool hasActiveGhost = true,
            bool canRotate = true,
            bool canTranslate = true,
            int rotationIncrements = 16,
            int fineRotationIncrements = 360,
            float sideStep = 0.25f,
            float upDownStep = 0.25f,
            float forwardBackStep = 0.25f,
            float fineSideStep = 0.05f,
            float fineUpDownStep = 0.05f,
            float fineForwardBackStep = 0.05f) =>
            new InputContext(
                enabled,
                runtimeAvailable,
                isFocused,
                takeInput,
                hasActiveGhost,
                canRotate,
                canTranslate,
                rotationIncrements,
                fineRotationIncrements,
                sideStep,
                upDownStep,
                forwardBackStep,
                fineSideStep,
                fineUpDownStep,
                fineForwardBackStep);

        private static MovementInputBindings DisabledMovement(
            InputChord right = default,
            InputChord fineForward = default) =>
            new MovementInputBindings(
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                right,
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                new InputChord(KeyCode.None),
                fineForward,
                new InputChord(KeyCode.None));

        private static FakeInputSource NewInput(float scrollDelta) =>
            new FakeInputSource { ScrollDelta = scrollDelta };
    }

    internal sealed class FakeInputSource : IInputSource
    {
        private const int KeyCapacity = 1024;
        private readonly bool[] _pressed = new bool[KeyCapacity];
        private readonly bool[] _pressedThisFrame = new bool[KeyCapacity];

        internal float ScrollDelta { get; set; }

        internal int ScrollReadCount { get; private set; }

        public bool IsPressed(KeyCode key)
        {
            int index = (int)key;
            return index >= 0 && index < KeyCapacity && _pressed[index];
        }

        public bool WasPressedThisFrame(KeyCode key)
        {
            int index = (int)key;
            if (index < 0 || index >= KeyCapacity || !_pressedThisFrame[index])
                return false;

            _pressedThisFrame[index] = false;
            return true;
        }

        public float ReadScrollDelta()
        {
            ScrollReadCount++;
            return ScrollDelta;
        }

        internal void Press(KeyCode key)
        {
            int index = (int)key;
            if (index >= 0 && index < KeyCapacity)
                _pressed[index] = true;
        }

        internal void PressThisFrame(KeyCode key)
        {
            int index = (int)key;
            if (index < 0 || index >= KeyCapacity)
                return;
            _pressed[index] = true;
            _pressedThisFrame[index] = true;
        }
    }
}
