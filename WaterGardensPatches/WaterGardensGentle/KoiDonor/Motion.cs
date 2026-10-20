using System;using UnityEngine;using RunicLilyPads;
namespace WaterGardensKoi{
internal static class KoiMotion{
 // Visual-only displacement; no food state, ownership, or network messages change.
 internal static Quaternion Turn(Quaternion desired,KoiSchool school,int index)=>Quaternion.RotateTowards(school.fish[index].rotation,desired,120f*Mathf.Min(Time.deltaTime,.05f));
 internal static Vector3 Direction(Vector3 original,KoiSchool school,int index,Vector3 next){var delta=next-school.fish[index].position;return delta.sqrMagnitude>1e-8f?delta:original;}
 internal static Vector3 Adjust(Vector3 desired,KoiSchool school,int index){
  var fish=school.fish;if(fish==null||index>=fish.Length||!fish[index])return desired;
  var current=fish[index].position;var offset=Vector3.zero;
  var players=Player.GetAllPlayers();
  for(int i=0;i<players.Count;i++){
   var player=players[i];if(!player||player.IsDead())continue;
   var feet=player.transform.position;
   if(feet.y>school.Water+.12f||feet.y<school.Water-school.Depth-1.8f||!school.Inside(feet,1f))continue;
   Vector3 away=current-feet;away.y=0;float distance=away.magnitude;
   const float radius=1.6f;if(distance>=radius)continue;
   if(distance<.025f){float angle=index*2.39996f;away=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));}else away/=distance;
   float weight=1-distance/radius;offset+=away*(radius*weight*weight);
  }
  desired=school.KeepInside(desired+Vector3.ClampMagnitude(offset,1.6f),index);
  // Bound displacement even when a player enters quickly or food targets change.
  desired=Vector3.Lerp(current,desired,1-Mathf.Exp(-3f*Mathf.Min(Time.deltaTime,.05f)));
  return Vector3.MoveTowards(current,desired,(.8f+.35f*school.Options.Scale)*Mathf.Min(Time.deltaTime,.05f));
 }
 internal static double Random(double seed,int index,long step,int channel){unchecked{ulong v=(ulong)(long)(seed*100003) ^ ((ulong)(index+1)*0x9E3779B97F4A7C15UL) ^ ((ulong)step*0xBF58476D1CE4E5B9UL) ^ ((ulong)(channel+1)*0x94D049BB133111EBUL);v=(v^(v>>30))*0xBF58476D1CE4E5B9UL;v=(v^(v>>27))*0x94D049BB133111EBUL;v^=v>>31;return (v>>11)*(1.0/9007199254740992.0);}}
 internal static void Point(double seed,int index,long step,out double x,out double z){double a=Random(seed,index,step,0)*Math.PI*2,r=Math.Sqrt(Random(seed,index,step,1))*.86;x=Math.Cos(a)*r;z=Math.Sin(a)*r;}
 internal static void Wander(int shape,double width,double time,int index,double seed,out double x,out double z){double interval=5+Random(seed,index,0,2)*5;double t=time/interval+Random(seed,index,0,3)*100;long k=(long)Math.Floor(t);t-=k;double u=1-t;double w0=u*u*u/6,w1=(3*t*t*t-6*t*t+4)/6,w2=(-3*t*t*t+3*t*t+3*t+1)/6,w3=t*t*t/6;Point(seed,index,k-1,out var a,out var b);x=a*w0;z=b*w0;Point(seed,index,k,out a,out b);x+=a*w1;z+=b*w1;Point(seed,index,k+1,out a,out b);x+=a*w2;z+=b*w2;Point(seed,index,k+2,out a,out b);x+=a*w3;z+=b*w3;double r=KoiMath.Radius(shape,Math.Atan2(z,x))*width*.5;x*=r;z*=r*KoiMath.Aspect(shape);}
 // One third at the surface, with independent, evenly offset phases and smooth dives.
 internal static double SurfaceBlend(double time,int index,double seed){double period=36+Random(seed,0,0,5)*12;double phase=time/period+Random(seed,0,0,6)+index*.6180339887498949;phase-=Math.Floor(phase);double plateau=1.0/3,transition=2/period;if(phase<plateau)return 1;if(phase<plateau+transition)return 1-Smooth((phase-plateau)/transition);if(phase>1-transition)return Smooth((phase-1+transition)/transition);return 0;}
 static double Smooth(double t)=>t*t*(3-2*t);
}
public class KoiSchool:MonoBehaviour{
 internal Transform[] fish;internal bool Inside(Vector3 p,float margin)=>true;internal GardenPiece Pond;internal float Width,Depth,Water;internal KoiSettings Options;double seed;static Func<Vector3,float> Surface;
 internal Vector3 KeepInside(Vector3 p,int index)=>p;
 internal Vector3 Orbit(int index,double time){KoiMotion.Wander(Pond.Shape,Width,time,index,seed,out var x,out var z);var p=KeepInside(transform.position+Quaternion.Euler(0,transform.eulerAngles.y,0)*new Vector3((float)x,0,(float)z),index);float level=Surface(p);if(float.IsNegativeInfinity(level))level=Water;float deep=Mathf.Min(Depth*.55f,.65f);float top=Mathf.Min(.025f*Options.Scale,Depth*.15f);p.y=level-Mathf.Lerp(deep,top,(float)KoiMotion.SurfaceBlend(time,index,seed));return p;}
}
internal class KoiSettings{internal float Scale;}
internal static class KoiMath{internal static double Radius(int s,double a)=>1;internal static double Aspect(int s)=>1;}
}
