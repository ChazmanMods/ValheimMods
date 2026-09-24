using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public readonly struct ExistingRowGrid
    {
        public ExistingRowGrid(PlanarPoint anchor, PlanarPoint right, double spacing, double rowSpacing)
        { Anchor = anchor; Right = right; Spacing = spacing; RowSpacing = rowSpacing; }
        public PlanarPoint Anchor { get; }
        public PlanarPoint Right { get; }
        public double Spacing { get; }
        public double RowSpacing { get; }
        public PlanarPoint Forward => new PlanarPoint(-Right.Forward, Right.Right);

        // Snap a generated cell, not the pattern centre. Even-sized grids have half-cell centres.
        public PlanarPoint SnapOrigin(PlanarPoint origin, PlanarPoint firstCell)
        {
            double x = origin.Right + Right.Right * firstCell.Right + Forward.Right * firstCell.Forward - Anchor.Right;
            double z = origin.Forward + Right.Forward * firstCell.Right + Forward.Forward * firstCell.Forward - Anchor.Forward;
            double along = x * Right.Right + z * Right.Forward;
            double across = x * Forward.Right + z * Forward.Forward;
            double dx = Math.Round(along / Spacing, MidpointRounding.AwayFromZero) * Spacing - along;
            double dz = Math.Round(across / RowSpacing, MidpointRounding.AwayFromZero) * RowSpacing - across;
            return new PlanarPoint(origin.Right + Right.Right * dx + Forward.Right * dz,
                origin.Forward + Right.Forward * dx + Forward.Forward * dz);
        }
    }

    public static class ExistingRowSnap
    {
        public const int MaximumSamples = 96;
        private const double Tolerance = .08;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static bool TryResolve(IReadOnlyList<PlanarPoint> plants, PlanarPoint cursor,
            PlanarPoint preferredRight, double minimumSpacing, double maximumSpacing, out ExistingRowGrid grid)
        {
            grid = default;
            if (plants == null || plants.Count < 2 || plants.Count > MaximumSamples ||
                !Finite(minimumSpacing) || !Finite(maximumSpacing) || minimumSpacing <= 0 || maximumSpacing < minimumSpacing ||
                !Finite(cursor.Right) || !Finite(cursor.Forward)) return false;
            int nearest = 0;
            double nearestDistance = double.MaxValue;
            for (int i = 0; i < plants.Count; i++)
            {
                if (!Finite(plants[i].Right) || !Finite(plants[i].Forward)) return false;
                double x = plants[i].Right - cursor.Right, z = plants[i].Forward - cursor.Forward;
                double distance = x * x + z * z;
                if (distance < nearestDistance || distance == nearestDistance &&
                    (plants[i].Right < plants[nearest].Right || plants[i].Right == plants[nearest].Right && plants[i].Forward < plants[nearest].Forward))
                { nearest = i; nearestDistance = distance; }
            }
            var anchor = plants[nearest];
            int bestSupport = 1;
            double bestSpacing = double.MaxValue, bestHeading = -1;
            PlanarPoint bestRight = default;
            var projections = new double[plants.Count];
            for (int candidate = 0; candidate < plants.Count; candidate++)
            {
                double x = plants[candidate].Right - anchor.Right, z = plants[candidate].Forward - anchor.Forward;
                double spacing = Math.Sqrt(x * x + z * z);
                if (spacing <= Tolerance || !Finite(spacing)) continue;
                double rx = x / spacing, rz = z / spacing;
                double heading = rx * preferredRight.Right + rz * preferredRight.Forward;
                if (heading < 0 || heading == 0 && (rx < 0 || rx == 0 && rz < 0)) { rx = -rx; rz = -rz; }
                int support = 0;
                int projected = 0;
                for (int j = 0; j < plants.Count; j++)
                {
                    double px = plants[j].Right - anchor.Right, pz = plants[j].Forward - anchor.Forward;
                    double along = px * rx + pz * rz, across = -px * rz + pz * rx;
                    if (Math.Abs(across) > Tolerance) continue;
                    projections[projected++] = along;
                }
                Array.Sort(projections, 0, projected);
                spacing = double.MaxValue;
                for (int j = 1; j < projected; j++)
                {
                    double gap = projections[j] - projections[j - 1];
                    if (gap > Tolerance) spacing = Math.Min(spacing, gap);
                }
                if (spacing < minimumSpacing - .001 || spacing > maximumSpacing + .001) continue;
                for (int j = 0; j < projected; j++)
                    if (Math.Abs(projections[j] - Math.Round(projections[j] / spacing) * spacing) <= Tolerance) support++;
                // Prefer the supported row over a diagonal or an isolated pair. Shorter intervals
                // win equal support, so a missing plant does not double an otherwise known spacing.
                if (support < 2) continue;
                heading = Math.Abs(heading);
                if (support > bestSupport || support == bestSupport &&
                    (spacing < bestSpacing - .001 || Math.Abs(spacing - bestSpacing) <= .001 && heading > bestHeading))
                { bestSupport = support; bestSpacing = spacing; bestHeading = heading; bestRight = new PlanarPoint(rx, rz); }
            }
            if (bestSupport < 2) return false;
            double rowSpacing = double.MaxValue;
            // Infer a parallel row only when at least two crops share its perpendicular offset.
            for (int i = 0; i < plants.Count; i++)
            {
                double x = plants[i].Right - anchor.Right, z = plants[i].Forward - anchor.Forward;
                double across = -x * bestRight.Forward + z * bestRight.Right;
                if (Math.Abs(across) < minimumSpacing - .001 || Math.Abs(across) > maximumSpacing + .001 || Math.Abs(across) >= rowSpacing) continue;
                int support = 0;
                for (int j = 0; j < plants.Count; j++)
                {
                    double px = plants[j].Right - anchor.Right, pz = plants[j].Forward - anchor.Forward;
                    double along = px * bestRight.Right + pz * bestRight.Forward;
                    double perpendicular = -px * bestRight.Forward + pz * bestRight.Right;
                    if (Math.Abs(perpendicular - across) <= Tolerance &&
                        Math.Abs(along - Math.Round(along / bestSpacing) * bestSpacing) <= Tolerance) support++;
                }
                if (support >= 2) rowSpacing = Math.Abs(across);
            }
            if (rowSpacing == double.MaxValue) rowSpacing = bestSpacing;
            grid = new ExistingRowGrid(anchor, bestRight, bestSpacing, rowSpacing);
            return true;
        }
    }
}
