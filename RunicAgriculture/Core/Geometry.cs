using System;

namespace RunicAgriculture.Core
{
    public readonly struct PlanarPoint : IEquatable<PlanarPoint>
    {
        public PlanarPoint(double right, double forward)
        {
            if (double.IsNaN(right) || double.IsInfinity(right))
                throw new ArgumentOutOfRangeException(nameof(right));
            if (double.IsNaN(forward) || double.IsInfinity(forward))
                throw new ArgumentOutOfRangeException(nameof(forward));
            Right = right;
            Forward = forward;
        }

        public double Right { get; }
        public double Forward { get; }

        public bool Equals(PlanarPoint other) =>
            Right.Equals(other.Right) && Forward.Equals(other.Forward);

        public override bool Equals(object obj) => obj is PlanarPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return Right.GetHashCode() * 397 ^ Forward.GetHashCode(); }
        }

        public override string ToString() => $"({Right:0.###}, {Forward:0.###})";
    }

    public enum PlantPattern
    {
        Row = 0,
        Grid = 1,
        Circle = 2,
        Star = 3,
        RightTriangle = 4,
        HalfCircle = 5,
        Trapezoid = 6
    }

    public enum AgricultureAlignment
    {
        PlayerHeading = 0,
        WorldAxes = 1,
        ExistingCropRow = 2
    }

    public sealed class PatternRequest
    {
        // The fixed safety boundary supports a complete 40x40 Grid. Rows and columns remain
        // independently editable; configurations whose product exceeds this absolute boundary are
        // explicitly reported as safety-capped by the runtime.
        public const int AbsoluteMaximumPoints = 1600;
        public const int AbsoluteMaximumDimension = 256;

        public PatternRequest(
            PlantPattern pattern,
            int rows,
            int columns,
            double spacing,
            int maximumPoints,
            bool mirrored = false,
            double leftPinch = 0.5d,
            double rightPinch = 0.5d)
        {
            if (!Enum.IsDefined(typeof(PlantPattern), pattern))
                throw new ArgumentOutOfRangeException(nameof(pattern));
            if (rows < 1 || rows > AbsoluteMaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(rows));
            if (columns < 1 || columns > AbsoluteMaximumDimension)
                throw new ArgumentOutOfRangeException(nameof(columns));
            if (spacing <= 0d || double.IsNaN(spacing) || double.IsInfinity(spacing))
                throw new ArgumentOutOfRangeException(nameof(spacing));
            if (maximumPoints < 1 || maximumPoints > AbsoluteMaximumPoints)
                throw new ArgumentOutOfRangeException(nameof(maximumPoints));
            if (!IsUnitInterval(leftPinch))
                throw new ArgumentOutOfRangeException(nameof(leftPinch));
            if (!IsUnitInterval(rightPinch))
                throw new ArgumentOutOfRangeException(nameof(rightPinch));

            Pattern = pattern;
            Rows = rows;
            Columns = columns;
            Spacing = spacing;
            MaximumPoints = maximumPoints;
            Mirrored = mirrored;
            LeftPinch = leftPinch;
            RightPinch = rightPinch;
        }

        public PlantPattern Pattern { get; }
        public int Rows { get; }
        public int Columns { get; }
        public double Spacing { get; }
        public int MaximumPoints { get; }
        public bool Mirrored { get; }
        public double LeftPinch { get; }
        public double RightPinch { get; }

        private static bool IsUnitInterval(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d && value <= 1d;
    }

    public static class PlacementRange
    {
        public static bool IsWithin(double distance, double maximumDistance, double extraDistance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0d)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (double.IsNaN(maximumDistance) || double.IsInfinity(maximumDistance))
                throw new ArgumentOutOfRangeException(nameof(maximumDistance));
            if (double.IsNaN(extraDistance) || double.IsInfinity(extraDistance))
                throw new ArgumentOutOfRangeException(nameof(extraDistance));
            return distance < Math.Max(0d, maximumDistance + extraDistance);
        }
    }
}
