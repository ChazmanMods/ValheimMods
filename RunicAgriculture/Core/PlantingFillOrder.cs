using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    /// <summary>
    /// Defines which valid cells receive a limited supply of planting resources. Grids and rows
    /// advance from the player's right toward the player's left. Shaped masks grow from their
    /// centre outward. Every comparison has a deterministic tie-break so the preview and commit
    /// paths always agree.
    /// </summary>
    public static class PlantingFillOrder
    {
        public static IReadOnlyList<PlanarPoint> Order(
            PlantPattern pattern,
            IReadOnlyList<PlanarPoint> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (!Enum.IsDefined(typeof(PlantPattern), pattern))
                throw new ArgumentOutOfRangeException(nameof(pattern));

            var ordered = new List<PlanarPoint>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
                ordered.Add(candidates[index]);
            ordered.Sort(pattern == PlantPattern.Grid || pattern == PlantPattern.Row
                ? CompareRightToLeft
                : CompareCenterOut);
            return ordered.AsReadOnly();
        }

        private static int CompareRightToLeft(PlanarPoint left, PlanarPoint right)
        {
            // Positive Right is the player's right. Finish a whole column front-to-back before
            // moving one column left, making a partially supplied grid visually unambiguous.
            int horizontal = right.Right.CompareTo(left.Right);
            return horizontal != 0
                ? horizontal
                : right.Forward.CompareTo(left.Forward);
        }

        private static int CompareCenterOut(PlanarPoint left, PlanarPoint right)
        {
            int distance = SquaredDistance(left).CompareTo(SquaredDistance(right));
            if (distance != 0) return distance;
            int forward = right.Forward.CompareTo(left.Forward);
            return forward != 0 ? forward : right.Right.CompareTo(left.Right);
        }

        private static double SquaredDistance(PlanarPoint point) =>
            point.Right * point.Right + point.Forward * point.Forward;
    }
}
