using System;
namespace ArcaneWaterWorks;
internal static class PondRiverJoin
{
    // Approach the receiving pond without changing the pond itself. A level apron
    // accommodates the river mesh's one-metre cells at the shoreline cut.
    internal static float Height(float river,float pond,float pondEdge){
        float t=Math.Max(0,Math.Min(1,(pondEdge-1.5f)/2.5f));t=t*t*(3-2*t);
        return pond+(river-pond)*t;
    }
}
