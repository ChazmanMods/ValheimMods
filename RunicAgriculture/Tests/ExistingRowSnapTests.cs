using System;
using System.Collections.Generic;
using RunicAgriculture.Core;

namespace RunicAgriculture.Tests
{
    internal static class ExistingRowSnapTests
    {
        private static PlanarPoint P(double x, double z) => new PlanarPoint(x, z);
        private static ExistingRowGrid Resolve(IReadOnlyList<PlanarPoint> crops, PlanarPoint cursor, PlanarPoint heading)
        {
            TestAssert.True(ExistingRowSnap.TryResolve(crops, cursor, heading, 1, 6, out var grid), "Expected a row.");
            return grid;
        }
        internal static void RowAndSpacing()
        {
            var row = Resolve(new[] { P(0,0), P(2,0), P(6,0) }, P(6.1,.2), P(1,0));
            TestAssert.Near(2, row.Spacing, .0001, "Missing crop doubled spacing.");
            var origin = row.SnapOrigin(P(8.3,.2), P(0,0));
            TestAssert.Near(8, origin.Right, .0001, "Row extension missed lattice.");
            TestAssert.Near(0, origin.Forward, .0001, "Origin did not snap to row.");
        }
        internal static void ParallelRows()
        {
            var crops = new[] { P(0,0), P(2,0), P(4,0), P(6,0), P(0,3), P(2,3), P(4,3), P(6,3) };
            var row = Resolve(crops, P(.1,.1), P(1,0));
            TestAssert.Near(2, row.Spacing, .0001, "Within-row spacing.");
            TestAssert.Near(3, row.RowSpacing, .0001, "Parallel-row spacing.");
            var reversed = (PlanarPoint[])crops.Clone(); Array.Reverse(reversed);
            var other = Resolve(reversed, P(.1,.1), P(1,0));
            TestAssert.Equal(row.Anchor, other.Anchor, "Input order changed anchor.");
            TestAssert.Equal(row.Right, other.Right, "Input order changed direction.");
        }
        internal static void RotatedEvenGrid()
        {
            double a = Math.PI / 5, x = Math.Cos(a), z = Math.Sin(a);
            var row = Resolve(new[] { P(10,20), P(10+2*x,20+2*z), P(10+4*x,20+4*z) }, P(10,20), P(x,z));
            var planar = new AgriculturePatternService().Generate(new PatternRequest(PlantPattern.Grid, 2, 4, row.Spacing, 8));
            var origin = row.SnapOrigin(P(15,25), planar[0]);
            foreach (var cell in planar)
            {
                double px = origin.Right + row.Right.Right*cell.Right + row.Forward.Right*cell.Forward - row.Anchor.Right;
                double pz = origin.Forward + row.Right.Forward*cell.Right + row.Forward.Forward*cell.Forward - row.Anchor.Forward;
                double along = (px*row.Right.Right+pz*row.Right.Forward)/row.Spacing;
                double across = (px*row.Forward.Right+pz*row.Forward.Forward)/row.RowSpacing;
                TestAssert.Near(Math.Round(along), along, .0001, "Even-column cell is half a spacing out.");
                TestAssert.Near(Math.Round(across), across, .0001, "Even-row cell is half a spacing out.");
            }
        }
        internal static void DiagonalAndLimits()
        {
            var crops = new List<PlanarPoint>();
            for (int x=0;x<3;x++) for(int z=0;z<3;z++) crops.Add(P(x*2,z*2));
            var row = Resolve(crops, P(0,0), P(1,0));
            TestAssert.Near(2,row.Spacing,.0001,"Chose a diagonal over a row.");
            TestAssert.Near(1,row.Right.Right,.0001,"Ignored heading tie-break.");
            TestAssert.False(ExistingRowSnap.TryResolve(new[]{P(0,0)},P(0,0),P(1,0),1,6,out _),"Single crop inferred a row.");
            TestAssert.False(ExistingRowSnap.TryResolve(new[]{P(0,0),P(.5,0),P(1,0)},P(0,0),P(1,0),1,6,out _),"Unsafe tight row accepted.");
            TestAssert.False(ExistingRowSnap.TryResolve(new PlanarPoint[97],P(0,0),P(1,0),1,6,out _),"Oversized sample accepted.");
            var pair=Resolve(new[]{P(0,0),P(2,0)},P(0,0),P(1,0));
            TestAssert.Near(2,pair.Spacing,.0001,"Two crops should define a usable row.");
        }
    }
}
