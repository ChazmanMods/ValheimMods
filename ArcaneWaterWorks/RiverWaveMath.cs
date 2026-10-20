using System;
namespace ArcaneWaterWorks;
// Ocean-style sharpened wave trains, advected downstream in two overlapping phases.
// Keep this formula matched to RiverFlow.shader for buoyancy.
internal static class RiverWaveMath {
    internal static float Shape(float x,float z){
        double p=x*1.7+z*.9,q=-x*1.3+z*2.4;
        return (float)(Math.Sin(p-.45*Math.Cos(p))*.68+Math.Sin(q-.3*Math.Cos(q))*.32);
    }
    internal static float Offset(float x,float z,float vx,float vz,float amplitude,float time){
        float p=time/6-(float)Math.Floor(time/6),q=(time/6+.5f)-(float)Math.Floor(time/6+.5f),blend=Math.Abs(p*2-1);
        return amplitude*(Shape(x-vx*p*6,z-vz*p*6)*(1-blend)+Shape(x-vx*q*6,z-vz*q*6)*blend);
    }
}
