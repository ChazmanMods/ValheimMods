using System;
namespace RunicLilyPads {
internal static class PondMath {
 public static double Bed(double original,double water,double depth,double distance,double width=6){
  if(distance<=1){double t=Math.Max(0,(distance-.45)/.55);t=t*t*(3-2*t);return water-depth+(depth-.04)*t;}
  double edge=(distance-1)*width*.5;
  if(edge>=2)return original;
  if(edge<.85){double t=edge/.85;t=t*t*(3-2*t);return water-.04+.10*t;}
  double blend=(edge-.85)/1.15;blend=blend*blend*(3-2*blend);
  return (water+.06)*(1-blend)+original*blend;
 }
}}
