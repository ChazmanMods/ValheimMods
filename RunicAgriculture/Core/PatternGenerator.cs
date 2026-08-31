using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public interface IAgriculturePatternService
    {
        IReadOnlyList<PlanarPoint> Generate(PatternRequest request);
    }

    public sealed class AgriculturePatternService : IAgriculturePatternService
    {
        public IReadOnlyList<PlanarPoint> Generate(PatternRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var points = new List<PlanarPoint>(request.MaximumPoints);
            switch (request.Pattern)
            {
                case PlantPattern.Row:
                    AddRow(points, request.Columns, request.Spacing, request.MaximumPoints);
                    break;
                case PlantPattern.Grid:
                    AddGrid(
                        points,
                        request.Rows,
                        request.Columns,
                        request.Spacing,
                        request.MaximumPoints);
                    break;
                case PlantPattern.Circle:
                    AddMaskedShape(points, request, IncludesCircle);
                    break;
                case PlantPattern.Star:
                    AddMaskedShape(points, request, IncludesStar);
                    break;
                case PlantPattern.RightTriangle:
                    AddMaskedShape(points, request, IncludesRightTriangle);
                    break;
                case PlantPattern.HalfCircle:
                    AddMaskedShape(points, request, IncludesHalfCircle);
                    break;
                case PlantPattern.Trapezoid:
                    AddMaskedShape(points, request, (right, forward) =>
                        IncludesTrapezoid(right, forward, request.LeftPinch, request.RightPinch));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
            return points.AsReadOnly();
        }

        private static void AddRow(
            ICollection<PlanarPoint> points,
            int count,
            double spacing,
            int maximum)
        {
            var candidates = new List<PlanarPoint>(count);
            double offset = (count - 1) * spacing * 0.5d;
            for (int column = 0; column < count; column++)
                candidates.Add(new PlanarPoint(column * spacing - offset, 0d));
            AddOrdered(points, PlantPattern.Row, candidates, maximum);
        }

        private static void AddGrid(
            ICollection<PlanarPoint> points,
            int rows,
            int columns,
            double spacing,
            int maximum)
        {
            var candidates = new List<PlanarPoint>(rows * columns);
            double rightOffset = (columns - 1) * spacing * 0.5d;
            double forwardOffset = (rows - 1) * spacing * 0.5d;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    candidates.Add(new PlanarPoint(
                        column * spacing - rightOffset,
                        row * spacing - forwardOffset));
                }
            }
            AddOrdered(points, PlantPattern.Grid, candidates, maximum);
        }

        private static void AddOrdered(
            ICollection<PlanarPoint> points,
            PlantPattern pattern,
            IReadOnlyList<PlanarPoint> candidates,
            int maximum)
        {
            IReadOnlyList<PlanarPoint> ordered = PlantingFillOrder.Order(pattern, candidates);
            int count = Math.Min(maximum, ordered.Count);
            for (int index = 0; index < count; index++) points.Add(ordered[index]);
        }

        private delegate bool ShapePredicate(double normalizedRight, double normalizedForward);

        private static void AddMaskedShape(
            ICollection<PlanarPoint> points,
            PatternRequest request,
            ShapePredicate includes)
        {
            var candidates = new List<PlanarPoint>(request.Rows * request.Columns);
            for (int row = 0; row < request.Rows; row++)
            {
                double normalizedForward = NormalizeCell(row, request.Rows);
                double forward = CenteredOffset(row, request.Rows, request.Spacing);
                for (int column = 0; column < request.Columns; column++)
                {
                    double normalizedRight = NormalizeCell(column, request.Columns);
                    if (!includes(normalizedRight, normalizedForward)) continue;
                    candidates.Add(new PlanarPoint(
                        CenteredOffset(column, request.Columns, request.Spacing),
                        forward));
                }
            }

            // Extremely small lattices can miss a narrow polygon altogether. Keeping the origin
            // produces a useful, safe one-point preview instead of making a configured pattern
            // disappear.
            if (candidates.Count == 0) candidates.Add(new PlanarPoint(0d, 0d));
            IReadOnlyList<PlanarPoint> ordered = PlantingFillOrder.Order(
                request.Pattern, candidates);
            int count = Math.Min(request.MaximumPoints, ordered.Count);
            for (int index = 0; index < count; index++)
                points.Add(new PlanarPoint(
                    request.Mirrored ? -ordered[index].Right : ordered[index].Right,
                    ordered[index].Forward));
        }

        private static double CenteredOffset(int index, int count, double spacing) =>
            (index - (count - 1) * 0.5d) * spacing;

        // Cell-centre normalization keeps 1xN and 2x2 shapes useful while retaining a stable
        // physical lattice at the configured crop spacing.
        private static double NormalizeCell(int index, int count) =>
            count <= 1 ? 0d : (2d * index + 1d - count) / count;

        private static bool IncludesCircle(double right, double forward) =>
            right * right + forward * forward <= 1d + 1e-12d;

        private static bool IncludesHalfCircle(double right, double forward) =>
            right >= -1e-12d && IncludesCircle(right, forward);

        // Right angle at the rear-left of the unmirrored footprint. Mirroring moves it to the
        // rear-right without changing point count or the source-centred footprint.
        private static bool IncludesRightTriangle(double right, double forward) =>
            right + forward <= 1e-12d;

        private static bool IncludesTrapezoid(
            double right,
            double forward,
            double leftPinch,
            double rightPinch)
        {
            double towardFront = (forward + 1d) * 0.5d;
            double leftEdge = -1d + leftPinch * towardFront;
            double rightEdge = 1d - rightPinch * towardFront;
            return right >= leftEdge - 1e-12d && right <= rightEdge + 1e-12d;
        }

        private static bool IncludesStar(double right, double forward)
        {
            // A fixed five-arm star is varied by the independently scalable row/column lattice.
            // Point-in-polygon avoids radial aliasing and also supports rectangular footprints.
            const int vertexCount = 10;
            bool inside = false;
            int previous = vertexCount - 1;
            for (int current = 0; current < vertexCount; current++)
            {
                StarVertex(current, out double currentX, out double currentY);
                StarVertex(previous, out double previousX, out double previousY);
                bool crosses = (currentY > forward) != (previousY > forward) &&
                               right < (previousX - currentX) * (forward - currentY) /
                               (previousY - currentY) + currentX;
                if (crosses) inside = !inside;
                previous = current;
            }
            return inside;
        }

        private static void StarVertex(int index, out double right, out double forward)
        {
            double radius = (index & 1) == 0 ? 1d : 0.42d;
            double angle = Math.PI * 0.5d + index * Math.PI / 5d;
            right = Math.Cos(angle) * radius;
            forward = Math.Sin(angle) * radius;
        }

    }
}
