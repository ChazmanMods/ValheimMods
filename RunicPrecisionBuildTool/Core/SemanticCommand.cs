using System;

namespace QuietBuildRotation
{
    /// <summary>
    /// Harmony- and device-independent operations understood by the placement core.
    /// </summary>
    internal enum SemanticCommandKind : byte
    {
        None = 0,
        RotateYaw,
        RotatePitch,
        RotateRoll,
        MoveSway,
        MoveHeave,
        MoveSurge,
        MatchOrientation,
        MatchPitch,
        MatchRoll,
        MatchYaw,
        MatchPositionX,
        MatchPositionY,
        MatchPositionZ,
        MatchPosition,
        MatchTransform,
        MatchSnapSide,
        RepeatTransform,
        ResetPitch,
        ResetRoll,
        ResetYaw,
        ResetSway,
        ResetHeave,
        ResetSurge,
        Reset
    }

    internal enum RotationAxis : byte
    {
        None = 0,
        /// <summary>Turn around Unity world Y (the fixed vertical centerline).</summary>
        Yaw,
        /// <summary>Tilt around Unity world X (the fixed left/right centerline).</summary>
        Pitch,
        /// <summary>Spin around Unity world Z (the fixed front/back centerline).</summary>
        Roll
    }

    internal enum PlacementReferenceFrame : byte
    {
        World = 0,
        Local = 1
    }

    /// <summary>
    /// A single resolved placement operation. Delta is expressed in degrees for rotation
    /// commands and metres for translation commands.
    /// </summary>
    internal readonly struct SemanticCommand : IEquatable<SemanticCommand>
    {
        internal static readonly SemanticCommand None = default;
        internal static readonly SemanticCommand MatchOrientation =
            new SemanticCommand(SemanticCommandKind.MatchOrientation, 0f);
        internal static readonly SemanticCommand MatchPitch =
            new SemanticCommand(SemanticCommandKind.MatchPitch, 0f);
        internal static readonly SemanticCommand MatchRoll =
            new SemanticCommand(SemanticCommandKind.MatchRoll, 0f);
        internal static readonly SemanticCommand MatchYaw =
            new SemanticCommand(SemanticCommandKind.MatchYaw, 0f);
        internal static readonly SemanticCommand MatchPositionX =
            new SemanticCommand(SemanticCommandKind.MatchPositionX, 0f);
        internal static readonly SemanticCommand MatchPositionY =
            new SemanticCommand(SemanticCommandKind.MatchPositionY, 0f);
        internal static readonly SemanticCommand MatchPositionZ =
            new SemanticCommand(SemanticCommandKind.MatchPositionZ, 0f);
        internal static readonly SemanticCommand MatchPosition =
            new SemanticCommand(SemanticCommandKind.MatchPosition, 0f);
        internal static readonly SemanticCommand MatchTransform =
            new SemanticCommand(SemanticCommandKind.MatchTransform, 0f);
        internal static readonly SemanticCommand MatchSnapSide =
            new SemanticCommand(SemanticCommandKind.MatchSnapSide, 0f);
        internal static readonly SemanticCommand RepeatTransform =
            new SemanticCommand(SemanticCommandKind.RepeatTransform, 0f);
        internal static readonly SemanticCommand ResetPitch =
            new SemanticCommand(SemanticCommandKind.ResetPitch, 0f);
        internal static readonly SemanticCommand ResetRoll =
            new SemanticCommand(SemanticCommandKind.ResetRoll, 0f);
        internal static readonly SemanticCommand ResetYaw =
            new SemanticCommand(SemanticCommandKind.ResetYaw, 0f);
        internal static readonly SemanticCommand ResetSway =
            new SemanticCommand(SemanticCommandKind.ResetSway, 0f);
        internal static readonly SemanticCommand ResetHeave =
            new SemanticCommand(SemanticCommandKind.ResetHeave, 0f);
        internal static readonly SemanticCommand ResetSurge =
            new SemanticCommand(SemanticCommandKind.ResetSurge, 0f);
        internal static readonly SemanticCommand Reset =
            new SemanticCommand(SemanticCommandKind.Reset, 0f);

        internal SemanticCommand(SemanticCommandKind kind, float delta)
        {
            Kind = kind;
            Delta = delta;
        }

        internal SemanticCommandKind Kind { get; }

        internal float Delta { get; }

        internal bool IsNone => Kind == SemanticCommandKind.None;

        internal bool IsRotation =>
            Kind == SemanticCommandKind.RotateYaw ||
            Kind == SemanticCommandKind.RotatePitch ||
            Kind == SemanticCommandKind.RotateRoll;

        internal bool IsTranslation =>
            Kind == SemanticCommandKind.MoveSway ||
            Kind == SemanticCommandKind.MoveHeave ||
            Kind == SemanticCommandKind.MoveSurge;

        internal static SemanticCommand Rotation(RotationAxis axis, float degrees)
        {
            switch (axis)
            {
                case RotationAxis.Yaw:
                    return new SemanticCommand(SemanticCommandKind.RotateYaw, degrees);
                case RotationAxis.Pitch:
                    return new SemanticCommand(SemanticCommandKind.RotatePitch, degrees);
                case RotationAxis.Roll:
                    return new SemanticCommand(SemanticCommandKind.RotateRoll, degrees);
                default:
                    return None;
            }
        }

        internal static SemanticCommand Translation(SemanticCommandKind kind, float metres)
        {
            return kind == SemanticCommandKind.MoveSway ||
                   kind == SemanticCommandKind.MoveHeave ||
                   kind == SemanticCommandKind.MoveSurge
                ? new SemanticCommand(kind, metres)
                : None;
        }

        public bool Equals(SemanticCommand other) =>
            Kind == other.Kind && Delta.Equals(other.Delta);

        public override bool Equals(object obj) =>
            obj is SemanticCommand other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ Delta.GetHashCode();
            }
        }

        public static bool operator ==(SemanticCommand left, SemanticCommand right) =>
            left.Equals(right);

        public static bool operator !=(SemanticCommand left, SemanticCommand right) =>
            !left.Equals(right);
    }
}
