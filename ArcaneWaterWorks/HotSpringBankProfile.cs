using System;
namespace ArcaneWaterWorks;
internal static class HotSpringBankProfile
{
    internal static double Height(double edge){
        if(edge<0||edge>=3)return 0;
        double t=edge<.25?1:(3-edge)/2.75;
        t=Math.Clamp(t,0,1);return .24*t*t*(3-2*t);
    }
    internal static double Apply(double original,double water,double edge){
        double lift=Height(edge);if(lift<=0)return original;
        double target=water-.025+lift;
        return Math.Max(original,target);
    }
}

