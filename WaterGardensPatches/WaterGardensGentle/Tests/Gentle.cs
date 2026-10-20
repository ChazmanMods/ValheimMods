using System;using System.Collections;using System.Linq;using System.Reflection;using System.Linq.Expressions;using System.IO;using UnityEngine;using RunicLilyPads;using HarmonyLib;
public partial class Probe{
Delegate MakeTurn(Type motion,Type school){var q=Expression.Parameter(typeof(Quaternion));var s=Expression.Parameter(typeof(object));var i=Expression.Parameter(typeof(int));return Expression.Lambda<Func<Quaternion,object,int,Quaternion>>(Expression.Call(AccessTools.Method(motion,"Turn"),q,Expression.Convert(s,school),i),q,s,i).Compile();}
IEnumerator Gentle(){
 yield return new WaitForSecondsRealtime(6);var player=Player.m_localPlayer;var pond=FindObjectsOfType<GardenPiece>().Where(g=>g.Shape>=0&&g.GetComponent<ZNetView>().IsValid()).OrderBy(g=>Vector3.Distance(g.transform.position,player.transform.position)).First();
 var asm=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="WaterGardensKoi");var type=asm.GetType("WaterGardensKoi.KoiSchool");var school=(Behaviour)pond.GetComponent(type);var fish=(Transform[])AccessTools.Field(type,"fish").GetValue(school);var water=(float)AccessTools.Field(type,"Water").GetValue(school);
 Check(fish!=null&&fish.Length>0,"fish render with patched Update and no runtime exception");
 var oldPlayer=player.transform.position;var rb=player.GetComponent<Rigidbody>();bool oldKinematic=rb.isKinematic;rb.isKinematic=true;school.enabled=false;
 var pos=pond.transform.position;pos.y=water-.15f;player.transform.position=pos;rb.position=pos;fish[0].position=pos+Vector3.right*.3f;
 var motion=asm.GetType("WaterGardensKoi.KoiMotion");var method=AccessTools.Method(motion,"Adjust");var v=Expression.Parameter(typeof(Vector3));var s=Expression.Parameter(typeof(object));var i=Expression.Parameter(typeof(int));var adjust=Expression.Lambda<Func<Vector3,object,int,Vector3>>(Expression.Call(method,v,Expression.Convert(s,type),i),v,s,i).Compile();
 var turn=(Func<Quaternion,object,int,Quaternion>)MakeTurn(motion,type);var startRotation=fish[0].rotation;var turned=turn(startRotation*Quaternion.Euler(0,180,0),school,0);Check(Quaternion.Angle(startRotation,turned)<=6.01f,"turning capped at 120 degrees/sec and 6 degrees per frame");
 var desired=fish[0].position;float initial=Vector3.Distance(fish[0].position,pos),maxStep=0;float start=Time.time;while(Time.time-start<2){yield return null;var next=adjust(desired,school,0);maxStep=Mathf.Max(maxStep,Vector3.Distance(next,fish[0].position));fish[0].position=next;}
 float final=Vector3.Distance(fish[0].position,pos);Check(final>initial+.2f&&maxStep<.10f,"wading player avoidance grows separation "+initial+" -> "+final+"; max frame step="+maxStep);
 player.transform.position=pos+Vector3.up*3;rb.position=player.transform.position;desired=fish[0].position;Check(Vector3.Distance(adjust(desired,school,0),desired)<.0001f,"player above pond does not scare fish");
 player.transform.position=pos;rb.position=pos;fish[0].position=pos;Check(!float.IsNaN(adjust(pos,school,0).x),"exact player/fish overlap has a finite escape direction");
 var watch=System.Diagnostics.Stopwatch.StartNew();for(int n=0;n<3000;n++)adjust(desired,school,0);watch.Stop();Logger.LogInfo("METRIC 3000 avoidance evaluations ms="+watch.Elapsed.TotalMilliseconds);
 player.transform.position=oldPlayer;rb.position=oldPlayer;rb.isKinematic=oldKinematic;school.enabled=true;
 var bed=(Func<double,double,double,double,double,double>)Delegate.CreateDelegate(typeof(Func<double,double,double,double,double,double>),AccessTools.Method(typeof(GardenPiece).Assembly.GetType("RunicLilyPads.PondMath"),"Bed"));bool shore=true;double maxSlopeStep=0;
 foreach(double width in new[]{6d,12d,24d})foreach(double depth in new[]{.3,1.2,3d}){
 shore &= Math.Abs(bed(10.15,10,depth,0,width)-(10-depth))<1e-6;
 shore &= Math.Abs(bed(10.15,10,depth,1,width)-9.96)<1e-6;
 shore &= Math.Abs(bed(10.15,10,depth,1+1.7/width,width)-10.06)<1e-6;
 shore &= Math.Abs(bed(10.15,10,depth,1+4/width,width)-10.15)<1e-6;
 for(double edge=-.1;edge<2.1;edge+=.005){double a=bed(10.15,10,depth,1+edge*2/width,width),b=bed(10.15,10,depth,1+(edge+.005)*2/width,width);maxSlopeStep=Math.Max(maxSlopeStep,Math.Abs(a-b));}}
 Check(shore&&maxSlopeStep<.02,"shallow bank joins water and original ground continuously across sizes/depths; max 5mm step="+maxSlopeStep);
 var wg=typeof(GardenPiece).Assembly;var fp=wg.GetType("RunicLilyPads.PondFootprint");var group=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(fp));var ctor=fp.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Single();group.Add(ctor.Invoke(new object[]{0d,0d,6d,10d,1.2d,0,0d}));group.Add(ctor.Invoke(new object[]{3d,0d,6d,10.8d,1.2d,0,0d}));var merge=wg.GetType("RunicLilyPads.PondMergeMath");bool wet=true;for(double x=.2;x<2.8;x+=.1){double level=(double)AccessTools.Method(merge,"Level").Invoke(null,new object[]{group,x,0d});double ground=(double)AccessTools.Method(merge,"Bed").Invoke(null,new object[]{group,x,0d,11d,null});wet&=ground<level-.24;}Check(wet,"unequal-height overlapping pond transition stays submerged");
 var renderer=pond.transform.Find("garden_generated/pond_water").GetComponent<MeshRenderer>();Check(renderer.reflectionProbeUsage!=UnityEngine.Rendering.ReflectionProbeUsage.Off&&Mathf.Abs(renderer.sharedMaterial.GetFloat("_NormalPower")-.35f)<.0001,"approved water reflections and normal strength retained");
 var camera=GameCamera.instance.GetComponent<Camera>();GameCamera.instance.enabled=false;camera.transform.position=pond.transform.position+new Vector3(0,5,-7);camera.transform.LookAt(pond.transform.position);var rt=new RenderTexture(1100,750,24);var tex=new Texture2D(1100,750,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1100,750),0,0);tex.Apply();File.WriteAllBytes(root+"/gentle-shore.png",tex.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;Destroy(rt);Destroy(tex);
 yield return Candidate(pond.gameObject,null);
 Logger.LogInfo("GENTLE COMPLETE");
}
}
