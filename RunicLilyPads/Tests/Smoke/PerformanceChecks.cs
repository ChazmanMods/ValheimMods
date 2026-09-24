using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke {
 IEnumerator CheckPerformance(Assembly asm,Vector3 ground){
  var report=new StringBuilder();var materials=new HashSet<Material>();
  foreach(var name in new[]{"piece_arcanedecor_fern_red_001","piece_arcanedecor_ferns","piece_arcanedecor_grass_plains_gold","piece_arcanedecor_yggdrasil_001"}){
   var p=PrefabManager.Instance.GetPrefab(name);foreach(var r in p.GetComponentsInChildren<MeshRenderer>(true))foreach(var m in r.sharedMaterials)if(materials.Add(m)){
    report.AppendLine(name+" material="+m.name+" shader="+m.shader.name+" keywords="+string.Join(",",m.shaderKeywords));
    for(int i=0;i<m.shader.GetPropertyCount();i++){var n=m.shader.GetPropertyName(i);var t=m.shader.GetPropertyType(i);report.AppendLine(n+" "+t+" "+(t==UnityEngine.Rendering.ShaderPropertyType.Float||t==UnityEngine.Rendering.ShaderPropertyType.Range?m.GetFloat(n).ToString():""));}
   }
  }
  File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath,"../materials-"+asm.GetName().Version+".txt"),report.ToString());
  if(asm.GetName().Version.Build>=8)yield return StartCoroutine(CheckRenderedWind(asm,ground));
  var all=new List<GameObject>();
  for(int i=0;i<4;i++){
   var pond=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground+new Vector3(i*5,-i*.65f,0),Quaternion.identity);all.Add(pond);
   pond.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100");
  }
  yield return new WaitForSecondsRealtime(5);
  var windType=asm.GetType("RunicLilyPads.GardenWind");var flowType=asm.GetType("RunicLilyPads.NaturalSpillway");
  var plantIds=new[]{"fern_red_001","fern_001","fern_002","fauna_001","reeds_006","lily_white_003","lily_blue_003"};
  for(int i=0;i<70;i++){all.Add(UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_"+plantIds[i%plantIds.Length]),ground+new Vector3(i%14,-.1f,i/14-2),Quaternion.identity));}
  yield return new WaitForSecondsRealtime(3);
  var winds=UnityEngine.Object.FindObjectsOfType(windType);var flows=UnityEngine.Object.FindObjectsOfType(flowType);
  var windUpdate=windType.GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);var next=windType.GetField("next",BindingFlags.Instance|BindingFlags.NonPublic);var flowUpdate=flowType.GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
  var hidden=new List<Renderer>();
  foreach(var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>()){
   if(!renderer.enabled||renderer.GetComponentInParent<Heightmap>())continue;bool garden=false;
   foreach(var component in renderer.GetComponentsInParent<MonoBehaviour>())if(component&&component.GetType().Namespace=="RunicLilyPads")garden=true;
   if(!garden){renderer.enabled=false;hidden.Add(renderer);}
  }
  var benchCamera=new GameObject("rendered_benchmark").AddComponent<Camera>();benchCamera.CopyFrom(Camera.main);benchCamera.enabled=false;
  benchCamera.transform.position=ground+new Vector3(10,8,-14);benchCamera.transform.LookAt(ground+new Vector3(7,-1,0));benchCamera.fieldOfView=55;
  var target=new RenderTexture(1920,1080,24);benchCamera.targetTexture=target;benchCamera.depthTextureMode|=DepthTextureMode.Depth;
  QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
  for(int test=0;test<3;test++){
   var timer=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<20;i++)foreach(var c in flows)flowUpdate.Invoke(c,null);timer.Stop();Logger.LogInfo("PERF "+asm.GetName().Version+" waterfall CPU ms/frame="+timer.Elapsed.TotalMilliseconds/20+" flows="+flows.Length);
   timer.Restart();for(int i=0;i<10;i++)foreach(var c in winds){if(next!=null)next.SetValue(c,0f);if(windUpdate!=null)windUpdate.Invoke(c,null);}timer.Stop();Logger.LogInfo("PERF "+asm.GetName().Version+" plant CPU ms/update="+timer.Elapsed.TotalMilliseconds/10+" plants="+winds.Length);
   yield return null;
   double sum=0;int frames=0;float until=Time.realtimeSinceStartup+5;while(Time.realtimeSinceStartup<until){benchCamera.Render();yield return null;sum+=Time.unscaledDeltaTime;frames++;}Logger.LogInfo("PERF "+asm.GetName().Version+" rendered 1920x1080 scene FPS="+frames/sum);
  }
  var savedTarget=RenderTexture.active;RenderTexture.active=target;var shot=new Texture2D(1920,1080,TextureFormat.RGB24,false);shot.ReadPixels(new Rect(0,0,1920,1080),0,0);shot.Apply();File.WriteAllBytes(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures/benchmark_"+asm.GetName().Version+".png"),ImageConversion.EncodeToPNG(shot));RenderTexture.active=savedTarget;UnityEngine.Object.Destroy(shot);
  foreach(var renderer in hidden)if(renderer)renderer.enabled=true;
  UnityEngine.Object.Destroy(benchCamera.gameObject);UnityEngine.Object.Destroy(target);
  foreach(var p in all)if(p)ZNetScene.instance.Destroy(p);yield return new WaitForSecondsRealtime(2);
 }
}
