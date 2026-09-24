using System;

namespace RunicAgriculture.Core
{
    public enum PatternEditAction
    {
        None = 0,
        IncreaseRows = 1,
        DecreaseRows = 2,
        IncreaseColumns = 3,
        DecreaseColumns = 4,
        ToggleSide = 5,
        DecreaseLeftPinch = 6,
        IncreaseLeftPinch = 7,
        DecreaseRightPinch = 8,
        IncreaseRightPinch = 9,
        IncreaseSpacing = 10,
        DecreaseSpacing = 11
    }

    public readonly struct PatternEditState : IEquatable<PatternEditState>
    {
        public PatternEditState(
            int rows,
            int columns,
            bool mirrored,
            double leftPinch,
            double rightPinch)
            : this(rows, columns, mirrored, leftPinch, rightPinch, 1.5d)
        {
        }

        public PatternEditState(
            int rows,
            int columns,
            bool mirrored,
            double leftPinch,
            double rightPinch,
            double spacing)
        {
            if (rows < 1 || rows > PatternRequest.AbsoluteMaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(rows));
            if (columns < 1 || columns > PatternRequest.AbsoluteMaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(columns));
            if (!Unit(leftPinch)) throw new ArgumentOutOfRangeException(nameof(leftPinch));
            if (!Unit(rightPinch)) throw new ArgumentOutOfRangeException(nameof(rightPinch));
            if (double.IsNaN(spacing) || double.IsInfinity(spacing) ||
                spacing < PatternEditor.MinimumSpacing || spacing > PatternEditor.MaximumSpacing)
                throw new ArgumentOutOfRangeException(nameof(spacing));
            Rows = rows;
            Columns = columns;
            Mirrored = mirrored;
            LeftPinch = leftPinch;
            RightPinch = rightPinch;
            Spacing = spacing;
        }

        public int Rows { get; }
        public int Columns { get; }
        public bool Mirrored { get; }
        public double LeftPinch { get; }
        public double RightPinch { get; }
        public double Spacing { get; }

        public bool Equals(PatternEditState other) =>
            Rows == other.Rows && Columns == other.Columns && Mirrored == other.Mirrored &&
            LeftPinch.Equals(other.LeftPinch) && RightPinch.Equals(other.RightPinch) &&
            Spacing.Equals(other.Spacing);

        public override bool Equals(object obj) => obj is PatternEditState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Rows;
                hash = hash * 397 ^ Columns;
                hash = hash * 397 ^ Mirrored.GetHashCode();
                hash = hash * 397 ^ LeftPinch.GetHashCode();
                hash = hash * 397 ^ RightPinch.GetHashCode();
                return hash * 397 ^ Spacing.GetHashCode();
            }
        }

        private static bool Unit(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d && value <= 1d;
    }

    public static class PatternEditor
    {
        public const double PinchStep = 0.1d;
        public const double SpacingStep = 0.1d;
        public const double MinimumSpacing = 0.5d;
        public const double MaximumSpacing = 6d;

        public static PatternEditState Apply(
            PlantPattern pattern,
            PatternEditState state,
            PatternEditAction action)
        {
            if (!Enum.IsDefined(typeof(PlantPattern), pattern))
                throw new ArgumentOutOfRangeException(nameof(pattern));
            if (!Enum.IsDefined(typeof(PatternEditAction), action))
                throw new ArgumentOutOfRangeException(nameof(action));

            int rows = state.Rows;
            int columns = state.Columns;
            bool mirrored = state.Mirrored;
            double left = state.LeftPinch;
            double right = state.RightPinch;
            double spacing = state.Spacing;
            switch (action)
            {
                case PatternEditAction.IncreaseRows:
                    if (pattern != PlantPattern.Row) rows = Math.Min(rows + 1, PatternRequest.AbsoluteMaximumDimension);
                    break;
                case PatternEditAction.DecreaseRows:
                    if (pattern != PlantPattern.Row) rows = Math.Max(rows - 1, 1);
                    break;
                case PatternEditAction.IncreaseColumns:
                    columns = Math.Min(columns + 1, PatternRequest.AbsoluteMaximumDimension);
                    break;
                case PatternEditAction.DecreaseColumns:
                    columns = Math.Max(columns - 1, 1);
                    break;
                case PatternEditAction.ToggleSide:
                    if (SupportsMirror(pattern)) mirrored = !mirrored;
                    break;
                case PatternEditAction.DecreaseLeftPinch:
                    if (pattern == PlantPattern.Trapezoid) left = ClampUnit(left - PinchStep);
                    break;
                case PatternEditAction.IncreaseLeftPinch:
                    if (pattern == PlantPattern.Trapezoid) left = ClampUnit(left + PinchStep);
                    break;
                case PatternEditAction.DecreaseRightPinch:
                    if (pattern == PlantPattern.Trapezoid) right = ClampUnit(right - PinchStep);
                    break;
                case PatternEditAction.IncreaseRightPinch:
                    if (pattern == PlantPattern.Trapezoid) right = ClampUnit(right + PinchStep);
                    break;
                case PatternEditAction.IncreaseSpacing:
                    spacing = ClampSpacing(spacing + SpacingStep);
                    break;
                case PatternEditAction.DecreaseSpacing:
                    spacing = ClampSpacing(spacing - SpacingStep);
                    break;
            }
            return new PatternEditState(rows, columns, mirrored, left, right, spacing);
        }

        public static PlantPattern Next(PlantPattern pattern)
        {
            if (!Enum.IsDefined(typeof(PlantPattern), pattern))
                throw new ArgumentOutOfRangeException(nameof(pattern));
            return pattern == PlantPattern.Trapezoid
                ? PlantPattern.Row
                : (PlantPattern)((int)pattern + 1);
        }

        public static bool SupportsMirror(PlantPattern pattern) =>
            pattern == PlantPattern.RightTriangle ||
            pattern == PlantPattern.HalfCircle ||
            pattern == PlantPattern.Trapezoid;

        public static string OrientationLabel(PlantPattern pattern, bool mirrored)
        {
            switch (pattern)
            {
                case PlantPattern.RightTriangle: return global::Runic.Localization.RunicText.Get("text_59be2c9939e5") + (mirrored ? "right" : "left");
                case PlantPattern.HalfCircle: return global::Runic.Localization.RunicText.Get("text_90eb042fe698") + (mirrored ? "left" : "right");
                case PlantPattern.Trapezoid: return mirrored ? global::Runic.Localization.RunicText.Get("text_ed059fe4e0d0") : global::Runic.Localization.RunicText.Get("text_a7248eeb45eb");
                default: return string.Empty;
            }
        }

        private static double ClampUnit(double value) => Math.Max(0d, Math.Min(1d, Math.Round(value, 3)));

        private static double ClampSpacing(double value) =>
            Math.Max(MinimumSpacing, Math.Min(MaximumSpacing, Math.Round(value, 3)));
    }
}
