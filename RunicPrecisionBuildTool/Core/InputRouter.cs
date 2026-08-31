using System;
using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Snapshot-style input abstraction. The runtime adapter is the only code that should touch
    /// Valheim's ZInput API; the core depends only on configured KeyCode values.
    /// </summary>
    internal interface IInputSource
    {
        bool IsPressed(KeyCode key);

        bool WasPressedThisFrame(KeyCode key);

        float ReadScrollDelta();
    }

    /// <summary>
    /// BepInEx-independent representation of one complete configured keyboard chord. Arrays are
    /// copied only when configuration is rebuilt; frame routing performs allocation-free reads.
    /// </summary>
    internal readonly struct InputChord
    {
        private static readonly KeyCode[] EmptyModifiers = Array.Empty<KeyCode>();
        private readonly KeyCode[] _modifiers;

        internal InputChord(KeyCode mainKey)
        {
            MainKey = mainKey;
            _modifiers = EmptyModifiers;
        }

        internal InputChord(KeyCode mainKey, KeyCode[] modifiers)
        {
            MainKey = mainKey;
            _modifiers = modifiers == null || modifiers.Length == 0
                ? EmptyModifiers
                : (KeyCode[])modifiers.Clone();
        }

        internal KeyCode MainKey { get; }

        internal int ModifierCount => _modifiers?.Length ?? 0;

        internal bool IsEmpty => MainKey == KeyCode.None;

        internal KeyCode GetModifier(int index) => _modifiers[index];

        internal bool Contains(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;
            if (MainKey == key)
                return true;

            int count = ModifierCount;
            for (int index = 0; index < count; index++)
            {
                if (_modifiers[index] == key)
                    return true;
            }

            return false;
        }
    }

    internal readonly struct RotationInputBindings
    {
        internal RotationInputBindings(
            InputChord yaw,
            InputChord pitch,
            InputChord roll,
            InputChord fineYaw,
            InputChord finePitch,
            InputChord fineRoll)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
            FineYaw = fineYaw;
            FinePitch = finePitch;
            FineRoll = fineRoll;
        }

        internal static RotationInputBindings Default => new RotationInputBindings(
            new InputChord(KeyCode.None),
            new InputChord(KeyCode.LeftAlt),
            new InputChord(KeyCode.LeftShift),
            new InputChord(KeyCode.V),
            new InputChord(KeyCode.V, new[] { KeyCode.LeftAlt }),
            new InputChord(KeyCode.V, new[] { KeyCode.LeftShift }));

        internal InputChord Yaw { get; }

        internal InputChord Pitch { get; }

        internal InputChord Roll { get; }

        internal InputChord FineYaw { get; }

        internal InputChord FinePitch { get; }

        internal InputChord FineRoll { get; }
    }

    internal readonly struct MovementInputBindings
    {
        internal MovementInputBindings(
            InputChord up,
            InputChord down,
            InputChord left,
            InputChord right,
            InputChord forward,
            InputChord backward,
            InputChord fineUp,
            InputChord fineDown,
            InputChord fineLeft,
            InputChord fineRight,
            InputChord fineForward,
            InputChord fineBackward)
        {
            Up = up;
            Down = down;
            Left = left;
            Right = right;
            Forward = forward;
            Backward = backward;
            FineUp = fineUp;
            FineDown = fineDown;
            FineLeft = fineLeft;
            FineRight = fineRight;
            FineForward = fineForward;
            FineBackward = fineBackward;
        }

        internal static MovementInputBindings Default => new MovementInputBindings(
            Chord(KeyCode.UpArrow, false),
            Chord(KeyCode.DownArrow, false),
            Chord(KeyCode.LeftArrow, false),
            Chord(KeyCode.RightArrow, false),
            Chord(KeyCode.PageUp, false),
            Chord(KeyCode.PageDown, false),
            Chord(KeyCode.UpArrow, true),
            Chord(KeyCode.DownArrow, true),
            Chord(KeyCode.LeftArrow, true),
            Chord(KeyCode.RightArrow, true),
            Chord(KeyCode.PageUp, true),
            Chord(KeyCode.PageDown, true));

        internal InputChord Up { get; }

        internal InputChord Down { get; }

        internal InputChord Left { get; }

        internal InputChord Right { get; }

        internal InputChord Forward { get; }

        internal InputChord Backward { get; }

        internal InputChord FineUp { get; }

        internal InputChord FineDown { get; }

        internal InputChord FineLeft { get; }

        internal InputChord FineRight { get; }

        internal InputChord FineForward { get; }

        internal InputChord FineBackward { get; }

        private static InputChord Chord(KeyCode mainKey, bool fine) =>
            fine
                ? new InputChord(mainKey, new[] { KeyCode.LeftAlt, KeyCode.V })
                : new InputChord(mainKey, new[] { KeyCode.LeftAlt });
    }

    internal readonly struct InputBindings
    {
        internal InputBindings(
            RotationInputBindings rotation,
            InputChord match,
            InputChord matchPitch,
            InputChord matchRoll,
            InputChord matchYaw,
            InputChord axisGuides,
            InputChord reset,
            MovementInputBindings movement)
            : this(
                rotation,
                match,
                matchPitch,
                matchRoll,
                matchYaw,
                new InputChord(KeyCode.Keypad4),
                new InputChord(KeyCode.Keypad5),
                new InputChord(KeyCode.Keypad6),
                new InputChord(KeyCode.Keypad7),
                new InputChord(KeyCode.Keypad8),
                new InputChord(KeyCode.Keypad9),
                new InputChord(KeyCode.KeypadPeriod),
                new InputChord(KeyCode.Keypad1, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.Keypad2, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.Keypad3, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.Keypad4, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.Keypad5, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.Keypad6, new[] { KeyCode.LeftShift }),
                new InputChord(KeyCode.P),
                axisGuides,
                reset,
                movement)
        {
        }

        internal InputBindings(
            RotationInputBindings rotation,
            InputChord match,
            InputChord matchPitch,
            InputChord matchRoll,
            InputChord matchYaw,
            InputChord matchPositionX,
            InputChord matchPositionY,
            InputChord matchPositionZ,
            InputChord matchPosition,
            InputChord matchTransform,
            InputChord matchSnapSide,
            InputChord repeatTransform,
            InputChord resetPitch,
            InputChord resetRoll,
            InputChord resetYaw,
            InputChord resetSway,
            InputChord resetHeave,
            InputChord resetSurge,
            InputChord precisionModeToggle,
            InputChord axisGuides,
            InputChord reset,
            MovementInputBindings movement)
        {
            Rotation = rotation;
            Match = match;
            MatchPitch = matchPitch;
            MatchRoll = matchRoll;
            MatchYaw = matchYaw;
            MatchPositionX = matchPositionX;
            MatchPositionY = matchPositionY;
            MatchPositionZ = matchPositionZ;
            MatchPosition = matchPosition;
            MatchTransform = matchTransform;
            MatchSnapSide = matchSnapSide;
            RepeatTransform = repeatTransform;
            ResetPitch = resetPitch;
            ResetRoll = resetRoll;
            ResetYaw = resetYaw;
            ResetSway = resetSway;
            ResetHeave = resetHeave;
            ResetSurge = resetSurge;
            PrecisionModeToggle = precisionModeToggle;
            AxisGuides = axisGuides;
            Reset = reset;
            Movement = movement;
        }

        internal static InputBindings Default => new InputBindings(
            RotationInputBindings.Default,
            new InputChord(KeyCode.Keypad0),
            new InputChord(KeyCode.Keypad1),
            new InputChord(KeyCode.Keypad2),
            new InputChord(KeyCode.Keypad3),
            new InputChord(KeyCode.Keypad4),
            new InputChord(KeyCode.Keypad5),
            new InputChord(KeyCode.Keypad6),
            new InputChord(KeyCode.Keypad7),
            new InputChord(KeyCode.Keypad8),
            new InputChord(KeyCode.Keypad9),
            new InputChord(KeyCode.KeypadPeriod),
            new InputChord(KeyCode.Keypad1, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.Keypad2, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.Keypad3, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.Keypad4, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.Keypad5, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.Keypad6, new[] { KeyCode.LeftShift }),
            new InputChord(KeyCode.P),
            new InputChord(KeyCode.G),
            new InputChord(KeyCode.F10),
            MovementInputBindings.Default);

        internal RotationInputBindings Rotation { get; }

        internal InputChord Match { get; }

        internal InputChord MatchPitch { get; }

        internal InputChord MatchRoll { get; }

        internal InputChord MatchYaw { get; }
        internal InputChord MatchPositionX { get; }
        internal InputChord MatchPositionY { get; }
        internal InputChord MatchPositionZ { get; }
        internal InputChord MatchPosition { get; }
        internal InputChord MatchTransform { get; }
        internal InputChord MatchSnapSide { get; }
        internal InputChord RepeatTransform { get; }
        internal InputChord ResetPitch { get; }
        internal InputChord ResetRoll { get; }
        internal InputChord ResetYaw { get; }
        internal InputChord ResetSway { get; }
        internal InputChord ResetHeave { get; }
        internal InputChord ResetSurge { get; }
        internal InputChord PrecisionModeToggle { get; }

        internal InputChord AxisGuides { get; }

        internal InputChord Reset { get; }

        internal MovementInputBindings Movement { get; }
    }

    internal readonly struct InputContext
    {
        internal InputContext(
            bool enabled,
            bool runtimeAvailable,
            bool isFocused,
            bool takeInput,
            bool hasActiveGhost,
            bool canRotate,
            bool canTranslate,
            int rotationIncrementsPerCircle,
            int fineRotationIncrementsPerCircle,
            float sideStepMetres,
            float upDownStepMetres,
            float forwardBackStepMetres,
            float fineSideStepMetres,
            float fineUpDownStepMetres,
            float fineForwardBackStepMetres)
        {
            Enabled = enabled;
            RuntimeAvailable = runtimeAvailable;
            IsFocused = isFocused;
            TakeInput = takeInput;
            HasActiveGhost = hasActiveGhost;
            CanRotate = canRotate;
            CanTranslate = canTranslate;
            RotationIncrementsPerCircle = rotationIncrementsPerCircle;
            FineRotationIncrementsPerCircle = fineRotationIncrementsPerCircle;
            SideStepMetres = sideStepMetres;
            UpDownStepMetres = upDownStepMetres;
            ForwardBackStepMetres = forwardBackStepMetres;
            FineSideStepMetres = fineSideStepMetres;
            FineUpDownStepMetres = fineUpDownStepMetres;
            FineForwardBackStepMetres = fineForwardBackStepMetres;
        }

        internal bool Enabled { get; }

        internal bool RuntimeAvailable { get; }

        internal bool IsFocused { get; }

        internal bool TakeInput { get; }

        internal bool HasActiveGhost { get; }

        internal bool CanRotate { get; }

        internal bool CanTranslate { get; }

        internal int RotationIncrementsPerCircle { get; }

        internal int FineRotationIncrementsPerCircle { get; }

        internal float RotationStepDegrees =>
            StepDegrees(RotationIncrementsPerCircle);

        internal float FineRotationStepDegrees =>
            StepDegrees(FineRotationIncrementsPerCircle);

        internal float SideStepMetres { get; }

        internal float UpDownStepMetres { get; }

        internal float ForwardBackStepMetres { get; }

        internal float FineSideStepMetres { get; }

        internal float FineUpDownStepMetres { get; }

        internal float FineForwardBackStepMetres { get; }

        internal bool AcceptsRunicInput =>
            Enabled && RuntimeAvailable && IsFocused && TakeInput && HasActiveGhost;

        private static float StepDegrees(int incrementsPerCircle) =>
            incrementsPerCircle > 0 ? 360f / incrementsPerCircle : 0f;
    }

    /// <summary>
    /// The complete allocation-free result for one frame. Every configured wheel action, including
    /// unmodified yaw, is represented explicitly and is consumed only when a real scroll occurs.
    /// </summary>
    internal readonly struct InputFrameResult
    {
        internal InputFrameResult(
            SemanticCommand wheelCommand,
            SemanticCommand discreteCommand,
            bool consumeWheel,
            RotationAxis activeAxis,
            float activeStep,
            bool activeStepIsFine,
            bool axisGuidesHeld)
        {
            WheelCommand = wheelCommand;
            DiscreteCommand = discreteCommand;
            ConsumeWheel = consumeWheel;
            ActiveAxis = activeAxis;
            ActiveStep = activeStep;
            ActiveStepIsFine = activeStepIsFine;
            AxisGuidesHeld = axisGuidesHeld;
        }

        internal SemanticCommand WheelCommand { get; }

        internal SemanticCommand DiscreteCommand { get; }

        internal bool ConsumeWheel { get; }

        internal RotationAxis ActiveAxis { get; }

        internal float ActiveStep { get; }

        internal bool ActiveStepIsFine { get; }

        internal bool AxisGuidesHeld { get; }
    }

    /// <summary>
    /// Sole owner of key/wheel arbitration. It emits at most one wheel command and one discrete
    /// command per call, uses key-down for movement, and accepts only exact configured chords.
    /// </summary>
    internal sealed class InputRouter
    {
        private const float ScrollEpsilon = 0.0001f;

        private readonly IInputSource _input;

        internal InputRouter(IInputSource input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        internal InputFrameResult ProcessFrame(in InputContext context, in InputBindings bindings)
        {
            // This is the first and only wheel read. Even a gated frame samples and
            // discards its wheel value so focus/UI transitions cannot replay stale scroll input.
            float wheelDelta = _input.ReadScrollDelta();
            if (!context.AcceptsRunicInput)
                return default;

            WheelSelection selection = ResolveWheelSelection(context, bindings);
            SemanticCommand wheelCommand = SemanticCommand.None;
            bool consumeWheel = false;

            if (context.CanRotate && selection.IsRecognized &&
                Math.Abs(wheelDelta) > ScrollEpsilon &&
                QuaternionMath.IsFinite(wheelDelta) &&
                QuaternionMath.IsFinite(selection.Step) && selection.Step > 0f)
            {
                float signedStep = wheelDelta > 0f ? selection.Step : -selection.Step;
                wheelCommand = SemanticCommand.Rotation(selection.Axis, signedStep);
                consumeWheel = !wheelCommand.IsNone;
            }

            SemanticCommand discreteCommand = ResolveDiscreteCommand(context, bindings);
            bool axisGuidesHeld = context.CanRotate &&
                                  IsControlChordHeld(bindings.AxisGuides);
            return new InputFrameResult(
                wheelCommand,
                discreteCommand,
                consumeWheel,
                context.CanRotate && selection.IsRecognized
                    ? selection.Axis
                    : RotationAxis.None,
                context.CanRotate && selection.IsRecognized ? selection.Step : 0f,
                context.CanRotate && selection.IsRecognized && selection.IsFine,
                axisGuidesHeld);
        }

        internal bool WasExactActionPressed(InputChord chord, in InputBindings bindings) =>
            IsExactChordDown(chord, bindings);

        private WheelSelection ResolveWheelSelection(
            in InputContext context,
            in InputBindings bindings)
        {
            float fineStep = context.FineRotationStepDegrees;
            if (IsExactHeldChord(
                    bindings.Rotation.FineRoll,
                    bindings.Rotation.FinePitch,
                    bindings,
                    false))
                return new WheelSelection(RotationAxis.Roll, fineStep, true);
            if (IsExactHeldChord(bindings.Rotation.FinePitch, bindings, false))
                return new WheelSelection(RotationAxis.Pitch, fineStep, true);
            if (IsExactHeldChord(bindings.Rotation.FineYaw, bindings, false))
                return new WheelSelection(RotationAxis.Yaw, fineStep, true);

            float normalStep = context.RotationStepDegrees;
            if (IsExactHeldChord(
                    bindings.Rotation.Roll,
                    bindings.Rotation.Pitch,
                    bindings,
                    false))
                return new WheelSelection(RotationAxis.Roll, normalStep, false);
            if (IsExactHeldChord(bindings.Rotation.Pitch, bindings, false))
                return new WheelSelection(RotationAxis.Pitch, normalStep, false);
            if (IsExactHeldChord(bindings.Rotation.Yaw, bindings, true))
                return new WheelSelection(RotationAxis.Yaw, normalStep, false);

            return default;
        }

        private SemanticCommand ResolveDiscreteCommand(
            in InputContext context,
            in InputBindings bindings)
        {
            // Safety-first deterministic priority for simultaneous key-downs.
            if (IsExactChordDown(bindings.Reset, bindings))
                return SemanticCommand.Reset;

            if (IsExactChordDown(bindings.ResetPitch, bindings))
                return SemanticCommand.ResetPitch;
            if (IsExactChordDown(bindings.ResetRoll, bindings))
                return SemanticCommand.ResetRoll;
            if (IsExactChordDown(bindings.ResetYaw, bindings))
                return SemanticCommand.ResetYaw;
            if (IsExactChordDown(bindings.ResetSway, bindings))
                return SemanticCommand.ResetSway;
            if (IsExactChordDown(bindings.ResetHeave, bindings))
                return SemanticCommand.ResetHeave;
            if (IsExactChordDown(bindings.ResetSurge, bindings))
                return SemanticCommand.ResetSurge;

            if (IsExactChordDown(bindings.MatchTransform, bindings))
                return SemanticCommand.MatchTransform;
            if (IsExactChordDown(bindings.MatchSnapSide, bindings))
                return SemanticCommand.MatchSnapSide;
            if (IsExactChordDown(bindings.RepeatTransform, bindings))
                return SemanticCommand.RepeatTransform;
            if (IsExactChordDown(bindings.MatchPosition, bindings))
                return SemanticCommand.MatchPosition;
            if (IsExactChordDown(bindings.MatchPositionX, bindings))
                return SemanticCommand.MatchPositionX;
            if (IsExactChordDown(bindings.MatchPositionY, bindings))
                return SemanticCommand.MatchPositionY;
            if (IsExactChordDown(bindings.MatchPositionZ, bindings))
                return SemanticCommand.MatchPositionZ;

            if (context.CanRotate)
            {
                // Component matches are checked before the exact match. This preserves the more
                // specific configured chord when a user gives actions shared keys.
                if (IsExactChordDown(bindings.MatchPitch, bindings))
                    return SemanticCommand.MatchPitch;
                if (IsExactChordDown(bindings.MatchRoll, bindings))
                    return SemanticCommand.MatchRoll;
                if (IsExactChordDown(bindings.MatchYaw, bindings))
                    return SemanticCommand.MatchYaw;
                if (IsExactChordDown(bindings.Match, bindings))
                    return SemanticCommand.MatchOrientation;
            }

            if (!context.CanTranslate)
                return SemanticCommand.None;

            MovementInputBindings movement = bindings.Movement;
            SemanticCommand command = TranslationIfDown(
                movement.FineUp,
                SemanticCommandKind.MoveHeave,
                context.FineUpDownStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.FineDown,
                SemanticCommandKind.MoveHeave,
                -context.FineUpDownStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.FineLeft,
                SemanticCommandKind.MoveSway,
                -context.FineSideStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.FineRight,
                SemanticCommandKind.MoveSway,
                context.FineSideStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.FineForward,
                SemanticCommandKind.MoveSurge,
                context.FineForwardBackStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.FineBackward,
                SemanticCommandKind.MoveSurge,
                -context.FineForwardBackStepMetres,
                bindings);
            if (!command.IsNone) return command;

            command = TranslationIfDown(
                movement.Up,
                SemanticCommandKind.MoveHeave,
                context.UpDownStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.Down,
                SemanticCommandKind.MoveHeave,
                -context.UpDownStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.Left,
                SemanticCommandKind.MoveSway,
                -context.SideStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.Right,
                SemanticCommandKind.MoveSway,
                context.SideStepMetres,
                bindings);
            if (!command.IsNone) return command;
            command = TranslationIfDown(
                movement.Forward,
                SemanticCommandKind.MoveSurge,
                context.ForwardBackStepMetres,
                bindings);
            if (!command.IsNone) return command;
            return TranslationIfDown(
                movement.Backward,
                SemanticCommandKind.MoveSurge,
                -context.ForwardBackStepMetres,
                bindings);
        }

        private SemanticCommand TranslationIfDown(
            InputChord chord,
            SemanticCommandKind kind,
            float signedStep,
            in InputBindings bindings)
        {
            if (!QuaternionMath.IsFinite(signedStep) || Math.Abs(signedStep) <= ScrollEpsilon)
                return SemanticCommand.None;

            return IsExactChordDown(chord, bindings)
                ? SemanticCommand.Translation(kind, signedStep)
                : SemanticCommand.None;
        }

        private bool IsExactChordDown(InputChord chord, in InputBindings bindings) =>
            !chord.IsEmpty &&
            AreChordKeysHeld(chord) &&
            HasOnlyAllowedControlKeys(bindings, chord) &&
            _input.WasPressedThisFrame(chord.MainKey);

        // The guide chord is a non-consuming overlay, not an action selector. All configured keys
        // are required, but unrelated keys stay available for simultaneous rotation/movement.
        private bool IsControlChordHeld(InputChord chord) =>
            !chord.IsEmpty && AreChordKeysHeld(chord);

        private bool IsExactHeldChord(
            InputChord chord,
            in InputBindings bindings,
            bool allowEmpty) =>
            IsExactHeldChord(chord, default, bindings, allowEmpty);

        private bool IsExactHeldChord(
            InputChord chord,
            InputChord supplementalAllowed,
            in InputBindings bindings,
            bool allowEmpty)
        {
            if (chord.IsEmpty)
                return allowEmpty &&
                       SupplementalChordIsFullyHeldOrReleased(supplementalAllowed, chord) &&
                       HasOnlyAllowedWheelKeys(bindings, chord, supplementalAllowed);

            return AreChordKeysHeld(chord) &&
                   SupplementalChordIsFullyHeldOrReleased(supplementalAllowed, chord) &&
                   HasOnlyAllowedWheelKeys(bindings, chord, supplementalAllowed);
        }

        private bool SupplementalChordIsFullyHeldOrReleased(
            InputChord supplemental,
            InputChord primary)
        {
            if (supplemental.IsEmpty)
                return true;

            bool hasSupplementalOnlyKey =
                SupplementalOnlyKeyIsHeld(supplemental.MainKey, primary);
            int count = supplemental.ModifierCount;
            for (int index = 0; index < count && !hasSupplementalOnlyKey; index++)
            {
                hasSupplementalOnlyKey =
                    SupplementalOnlyKeyIsHeld(supplemental.GetModifier(index), primary);
            }

            // The supplemental pitch selector is optional, but remains a complete custom chord:
            // accept none of its additional keys or require every one of them.
            return !hasSupplementalOnlyKey || AreChordKeysHeld(supplemental);
        }

        private bool SupplementalOnlyKeyIsHeld(KeyCode key, InputChord primary) =>
            key != KeyCode.None && !primary.Contains(key) && _input.IsPressed(key);

        private bool AreChordKeysHeld(InputChord chord)
        {
            if (chord.IsEmpty || !_input.IsPressed(chord.MainKey))
                return false;

            int count = chord.ModifierCount;
            for (int index = 0; index < count; index++)
            {
                KeyCode modifier = chord.GetModifier(index);
                if (modifier == KeyCode.None || !_input.IsPressed(modifier))
                    return false;
            }

            return true;
        }

        private bool HasOnlyAllowedControlKeys(
            in InputBindings bindings,
            InputChord allowed)
        {
            InputChord overlay = IsControlChordHeld(bindings.AxisGuides)
                ? bindings.AxisGuides
                : default;
            return KeyIsAllowedOrReleased(KeyCode.LeftAlt, allowed, overlay) &&
                   KeyIsAllowedOrReleased(KeyCode.RightAlt, allowed, overlay) &&
                   KeyIsAllowedOrReleased(KeyCode.LeftControl, allowed, overlay) &&
                   KeyIsAllowedOrReleased(KeyCode.RightControl, allowed, overlay) &&
                   KeyIsAllowedOrReleased(KeyCode.LeftShift, allowed, overlay) &&
                   KeyIsAllowedOrReleased(KeyCode.RightShift, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.Yaw, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.Pitch, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.Roll, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.FineYaw, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.FinePitch, allowed, overlay) &&
                   ChordKeysAreAllowedOrReleased(bindings.Rotation.FineRoll, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Match, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchPitch, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchRoll, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchYaw, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchPositionX, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchPositionY, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchPositionZ, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchPosition, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchTransform, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.MatchSnapSide, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.RepeatTransform, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetPitch, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetRoll, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetYaw, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetSway, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetHeave, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.ResetSurge, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.PrecisionModeToggle, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Reset, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Up, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Down, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Left, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Right, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Forward, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.Backward, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineUp, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineDown, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineLeft, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineRight, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineForward, allowed, overlay) &&
                   ChordModifiersAreAllowedOrReleased(bindings.Movement.FineBackward, allowed, overlay);
        }

        private bool HasOnlyAllowedWheelKeys(
            in InputBindings bindings,
            InputChord allowed,
            InputChord supplementalAllowed)
        {
            // Validate the complete wheel chord while allowing discrete movement and rotation in
            // the same frame. Roll wins resolver priority over the exact Roll+Pitch composite.
            InputChord overlay = IsControlChordHeld(bindings.AxisGuides)
                ? bindings.AxisGuides
                : default;
            return WheelKeyIsAllowedOrReleased(
                       KeyCode.LeftAlt, allowed, supplementalAllowed, overlay) &&
                   WheelKeyIsAllowedOrReleased(
                       KeyCode.RightAlt, allowed, supplementalAllowed, overlay) &&
                   WheelKeyIsAllowedOrReleased(
                       KeyCode.LeftShift, allowed, supplementalAllowed, overlay) &&
                   WheelKeyIsAllowedOrReleased(
                       KeyCode.RightShift, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.Yaw, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.Pitch, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.Roll, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.FineYaw, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.FinePitch, allowed, supplementalAllowed, overlay) &&
                   WheelChordKeysAreAllowedOrReleased(
                       bindings.Rotation.FineRoll, allowed, supplementalAllowed, overlay);
        }

        private bool WheelChordKeysAreAllowedOrReleased(
            InputChord candidate,
            InputChord allowed,
            InputChord supplementalAllowed,
            InputChord overlay)
        {
            if (!WheelKeyIsAllowedOrReleased(
                    candidate.MainKey, allowed, supplementalAllowed, overlay))
                return false;

            int count = candidate.ModifierCount;
            for (int index = 0; index < count; index++)
            {
                if (!WheelKeyIsAllowedOrReleased(
                        candidate.GetModifier(index),
                        allowed,
                        supplementalAllowed,
                        overlay))
                    return false;
            }

            return true;
        }

        private bool WheelKeyIsAllowedOrReleased(
            KeyCode key,
            InputChord allowed,
            InputChord supplementalAllowed,
            InputChord overlay) =>
            key == KeyCode.None || allowed.Contains(key) ||
            supplementalAllowed.Contains(key) || overlay.Contains(key) ||
            !_input.IsPressed(key);

        private bool ChordKeysAreAllowedOrReleased(
            InputChord candidate,
            InputChord allowed,
            InputChord overlay)
        {
            if (!KeyIsAllowedOrReleased(candidate.MainKey, allowed, overlay))
                return false;

            return ChordModifiersAreAllowedOrReleased(candidate, allowed, overlay);
        }

        private bool ChordModifiersAreAllowedOrReleased(
            InputChord candidate,
            InputChord allowed,
            InputChord overlay)
        {

            int count = candidate.ModifierCount;
            for (int index = 0; index < count; index++)
            {
                if (!KeyIsAllowedOrReleased(candidate.GetModifier(index), allowed, overlay))
                    return false;
            }

            return true;
        }

        private bool KeyIsAllowedOrReleased(
            KeyCode key,
            InputChord allowed,
            InputChord overlay) =>
            key == KeyCode.None || allowed.Contains(key) || overlay.Contains(key) ||
            !_input.IsPressed(key);

        private readonly struct WheelSelection
        {
            internal WheelSelection(RotationAxis axis, float step, bool isFine)
            {
                Axis = axis;
                Step = step;
                IsRecognized = true;
                IsFine = isFine;
            }

            internal RotationAxis Axis { get; }

            internal float Step { get; }

            internal bool IsRecognized { get; }

            internal bool IsFine { get; }
        }
    }
}
