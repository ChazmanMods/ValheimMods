using System;
using System.Collections.Generic;
namespace RunicLilyPads
{
    internal readonly struct PondFootprint
    {
        internal readonly double X,Z,Width,Water,Depth,Yaw;internal readonly int Shape;
        internal PondFootprint(double x,double z,double width,double water,double depth,int shape,double yaw=0){X=x;Z=z;Width=width;Water=water;Depth=depth;Shape=shape;Yaw=yaw;}
        internal double Distance(double x,double z){double dx=x-X,dz=z-Z,c=Math.Cos(Yaw),s=Math.Sin(Yaw);return PondMath.Distance(Shape,c*dx-s*dz,s*dx+c*dz,Width);}
        internal void Boundary(double angle,out double x,out double z){double r=PondMath.Radius(Shape,angle)*Width*.5,lx=Math.Cos(angle)*r,lz=Math.Sin(angle)*r*PondMath.Aspect(Shape);x=X+Math.Cos(Yaw)*lx+Math.Sin(Yaw)*lz;z=Z-Math.Sin(Yaw)*lx+Math.Cos(Yaw)*lz;}
    }
    internal static class PondMergeMath
    {
        internal static bool Overlap(PondFootprint a,PondFootprint b){
            if(Math.Abs(a.X-b.X)>(a.Width+b.Width)*.5||Math.Abs(a.Z-b.Z)>(a.Width+b.Width)*.5)return false;
            if(a.Distance(b.X,b.Z)<1||b.Distance(a.X,a.Z)<1)return true;
            for(int i=0;i<96;i++){double angle=i*Math.PI*2/96;a.Boundary(angle,out var x,out var z);if(b.Distance(x,z)<.999)return true;b.Boundary(angle,out x,out z);if(a.Distance(x,z)<.999)return true;}
            return false;
        }
        internal static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
        internal static double Edge(PondFootprint p,double x,double z)=>(p.Distance(x,z)-1)*p.Width*.5;
        internal static double Level(IReadOnlyList<PondFootprint> ponds,double x,double z){
            double nearest=double.PositiveInfinity;foreach(var p in ponds)nearest=Math.Min(nearest,Edge(p,x,z));
            double weight=0,height=0;foreach(var p in ponds){double w=Math.Exp(-(Edge(p,x,z)-nearest)*2.4);weight+=w;height+=w*p.Water;}return height/weight;
        }
        internal static List<List<PondFootprint>> Groups(IReadOnlyList<PondFootprint> ponds){
            var result=new List<List<PondFootprint>>();var used=new bool[ponds.Count];
            for(int i=0;i<ponds.Count;i++){if(used[i])continue;var group=new List<PondFootprint>{ponds[i]};used[i]=true;
                for(int j=0;j<group.Count;j++)for(int k=0;k<ponds.Count;k++)if(!used[k]&&Overlap(group[j],ponds[k])){used[k]=true;group.Add(ponds[k]);}result.Add(group);
            }return result;
        }
        internal static bool Affects(IReadOnlyList<PondFootprint> ponds,double x,double z){foreach(var p in ponds)if(Edge(p,x,z)<3)return true;return false;}
        // A connected group shares one continuous water-height field and submerged bed.
        internal static double Bed(IReadOnlyList<PondFootprint> ponds,double x,double z,double original,IReadOnlyList<PondChannel> channels=null){
            double distance=double.PositiveInfinity;PondFootprint nearest=default;
            foreach(var p in ponds){double d=p.Distance(x,z);if(d<distance){distance=d;nearest=p;}}
            if(double.IsPositiveInfinity(distance)||!Affects(ponds,x,z))return original;
            double water=Level(ponds,x,z),bed=PondMath.Bed(original,water,nearest.Depth,distance,nearest.Width);
            // Retain enough cover for coarse heightmap triangles under a short cascade.
            double min=double.PositiveInfinity,max=double.NegativeInfinity;int interiors=0;
            foreach(var p in ponds)if(p.Distance(x,z)<1){interiors++;min=Math.Min(min,p.Water);max=Math.Max(max,p.Water);}
            if(interiors>1)bed=Math.Min(bed,water-Math.Max(.25,(max-min)*.55+.25));
            return bed;
        }
    }
}
