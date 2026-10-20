using System;
namespace RunicLilyPads {
 internal readonly struct PondChannel {
  internal readonly PondFootprint High,Low;internal readonly double DX,DZ,Start,End,Width;
  internal double Length=>End-Start;
  PondChannel(PondFootprint h,PondFootprint l,double dx,double dz,double d){High=h;Low=l;DX=dx;DZ=dz;Start=d*.5-.6;End=d*.5+.6;Width=Math.Min(h.Width,l.Width)*.3;}
  internal static bool TryCreate(PondFootprint h,PondFootprint l,out PondChannel c){
   c=default;if(h.Water<=l.Water+.15||!PondMergeMath.Overlap(h,l))return false;double dx=l.X-h.X,dz=l.Z-h.Z,d=Math.Sqrt(dx*dx+dz*dz);if(d<.25)return false;
   c=new PondChannel(h,l,dx/d,dz/d,d);return true;
  }
  internal double Height(double t)=>High.Water+(Low.Water-High.Water)*Math.Max(0,Math.Min(1,t));
  internal void Project(double x,double z,out double t,out double cross){
   double delta=(PondMergeMath.Edge(High,x,z)-PondMergeMath.Edge(Low,x,z))*2.4;
   t=1/(1+Math.Exp(-Math.Max(-700,Math.Min(700,delta))));cross=-(x-High.X)*DZ+(z-High.Z)*DX;
  }
  internal bool Contains(double x,double z,out double height){Project(x,z,out double t,out _);height=Height(t);return t>.005&&t<.995&&Math.Min(High.Distance(x,z),Low.Distance(x,z))<=1.00001;}
  internal void Point(double t,double u,out double x,out double z){
   double distance=Math.Sqrt((High.X-Low.X)*(High.X-Low.X)+(High.Z-Low.Z)*(High.Z-Low.Z)),a=0,b=distance,cross=u*Width;
   for(int i=0;i<24;i++){double along=(a+b)/2;Project(High.X+DX*along-DZ*cross,High.Z+DZ*along+DX*cross,out double value,out _);if(value<t)a=along;else b=along;}
   x=High.X+DX*(a+b)/2-DZ*cross;z=High.Z+DZ*(a+b)/2+DX*cross;
  }
 }
}
