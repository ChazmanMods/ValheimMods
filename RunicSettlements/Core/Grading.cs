using System;

namespace RunicSettlements.Core;

public static class Grading
{
    // Tile edges contain duplicate vertices owned by separate terrain compilers.
    // Return to the untouched baseline at the shared edge, even if the other
    // compiler is unloaded, blocked, or has not processed its road yet.
    public static float SeamWeight(Point point)
    {
        var center=Cell.From(point,64).At(64);
        float distance=32-Math.Max(Math.Abs(point.X-center.X),Math.Abs(point.Z-center.Z));
        float t=Math.Max(0,Math.Min(1,distance/6));
        return t*t*(3-2*t);
    }
    // Relative to original ground, never relative to the result of a previous update.
    public static float Delta(ITerrain terrain, ZonePaths paths, Point point, float originalHeight)
    {
        float best = float.MaxValue, target = originalHeight;
        Point nearest=default,along=default;
        foreach (var segment in paths.Segments)
        {
            Point d = segment.B - segment.A;
            float length = d.Length;
            if (length < 0.001f) continue;
            float t = Math.Max(0, Math.Min(1, ((point.X-segment.A.X)*d.X+(point.Z-segment.A.Z)*d.Z)/(length*length)));
            Point center = Point.Lerp(segment.A, segment.B, t);
            float distance = Point.Distance(point, center);
            if (distance >= best) continue;
            best = distance;
            nearest=center; along=d*(4/length);
        }
        float blend = Math.Max(0, Math.Min(1, (paths.Width/2 + 2 - best)/2));
        blend = blend*blend*(3-2*blend);
        if(blend==0 || SeamWeight(point)==0 || paths.MaximumRaise==0 && paths.MaximumLower==0) return 0;
        target = (terrain.Height(nearest-along) + 2*terrain.Height(nearest) + terrain.Height(nearest+along))/4;
        return Math.Max(-paths.MaximumLower, Math.Min(paths.MaximumRaise, target-originalHeight))*blend*SeamWeight(point);
    }
}
