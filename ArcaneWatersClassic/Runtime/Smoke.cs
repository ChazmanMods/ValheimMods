using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcaneWaterWorks;
using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

[BepInPlugin("chazman.ArcaneWaterWorks.Smoke","Classic trench regressions","0.1.0")]
[BepInDependency(Plugin.Guid)]
public sealed class Smoke:BaseUnityPlugin{
 bool ready;int failures;static bool flat;static float ground;
 void Awake(){Utils.SetSaveDataPath(Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../saves")));PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;new Harmony("classic.trench.regression").CreateClassProcessor(typeof(FlatGround)).Patch();StartCoroutine(Run());StartCoroutine(Watchdog());}
 IEnumerator Watchdog(){yield return new WaitForSecondsRealtime(280);Logger.LogError("CLASSIC FAIL timeout");Application.Quit(2);}
 void Check(bool value,string name){Logger.LogInfo((value?"CLASSIC PASS ":"CLASSIC FAIL ")+name);if(!value)failures++;}
 [HarmonyPatch(typeof(RiverTerrain),nameof(RiverTerrain.SampleBase))]
 sealed class FlatGround{static bool Prefix(ref float height,ref bool __result){if(!flat)return true;height=ground;__result=true;return false;}}
 IEnumerator Settled(){yield return new WaitForSecondsRealtime(.8f);float until=Time.realtimeSinceStartup+8;while(Spring.Active.Any(s=>s&&s.Busy)&&Time.realtimeSinceStartup<until)yield return null;}
 IEnumerator Run(){
  float until=Time.realtimeSinceStartup+80;while(!ready&&Time.realtimeSinceStartup<until)yield return null;
  var prefab=PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring");Check(prefab,"classic spring registered");if(!prefab){Application.Quit(2);yield break;}
  var seed=World.GetCreateWorld("ArcaneWaterWorksSmoke",FileHelpers.FileSource.Local).m_seedName;
  var world=new World("ClassicTrench_"+DateTime.UtcNow.Ticks,seed){m_fileSource=FileHelpers.FileSource.Local};world.SaveWorldFWLData(DateTime.Now);
  Game.SetProfile("arcane_waterworks_smoke",FileHelpers.FileSource.Local);ZNet.SetServer(true,false,false,"ClassicTrench","",world);ZNet.ResetServerHost();
  typeof(FejdStartup).GetMethod("LoadMainScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(UnityEngine.Object.FindFirstObjectByType<FejdStartup>(),null);
  until=Time.realtimeSinceStartup+140;while(!Player.m_localPlayer&&Time.realtimeSinceStartup<until)yield return null;
  if(!Player.m_localPlayer){Check(false,"world initialized");Application.Quit(2);yield break;}
  var player=Player.m_localPlayer;player.SetGodMode(true);foreach(var v in UnityEngine.Object.FindObjectsByType<Valkyrie>(FindObjectsSortMode.None))v.DropPlayer(true);player.SetIntro(false);yield return new WaitForSecondsRealtime(3);
  var origin=player.transform.position;origin.y=ZoneSystem.instance.m_waterLevel+20;ground=origin.y;flat=true;
  Logger.LogInfo("CLASSIC deterministic flat-ground fixture; real section objects, ZDO saves, ownership, selection and streaming; isolated disposable world.");
  var body=player.GetComponent<Rigidbody>();body.isKinematic=true;
  void Move(Vector3 point){body.position=point+Vector3.up*2;player.transform.position=body.position;}
  var route=new Route();route.Points.Add(new Node(0,-.35f,0));route.Points.Add(new Node(0,-.35f,4));
  var first=Instantiate(prefab,origin,Quaternion.identity);first.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());Move(origin);yield return StartCoroutine(Settled());
  var spring=first.GetComponent<Spring>();RiverHoe.Select(spring);
  var aim=origin+new Vector3(0,0,68);Move(aim);bool extended=RiverHoe.Extend(aim);Check(extended,"distant trench aim advances instead of reporting the 192 metre cap");yield return StartCoroutine(Settled());
  Check(spring.Route.Points.Last().Z>35.9f&&spring.Route.Points.Last().Z<36.1f,"distant aim advances one 32 metre step");
  var sections=new HashSet<ZDOID>{spring.View.GetZDO().m_uid};
  for(int i=0;i<12&&extended;i++){
   var selected=RiverHoe.Selected;var tip=selected.Route.Points.Last();var target=selected.transform.position+new Vector3(tip.X,tip.Y,tip.Z+32);Move(target);yield return new WaitForSecondsRealtime(.1f);
   extended=RiverHoe.Extend(target);Check(extended,"trench stroke "+i);yield return StartCoroutine(Settled());
   selected=RiverHoe.Selected;Check(selected&&selected.Route!=null&&selected.Live,"selected continuation initialized "+i);if(!selected||selected.Route==null)break;
   sections.Add(selected.View.GetZDO().m_uid);Check(Route.TryDecode(selected.View.GetZDO().GetString("aww_route_v1",""),out var stored)&&stored.Encode()==selected.Route.Encode(),"stroke saved to ZDO "+i);
  }
  var final=RiverHoe.Selected;float reached=final.transform.position.z+final.Route.Points.Last().Z-origin.z;
  Check(reached>400&&sections.Count>=4,"continuous trench exceeds 400 metres through multiple automatic handoffs: "+reached+" m / "+sections.Count+" sections");
  // Existing densely sampled routes can fill their node budget before 96 metres.
  var denseOrigin=final.transform.position+new Vector3(18,0,0);denseOrigin.y=ground;
  var dense=new Route();for(int i=0;i<128;i++)dense.Points.Add(new Node(0,-.35f,i*.5f));
  var denseObject=Instantiate(prefab,denseOrigin,Quaternion.identity);denseObject.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",dense.Encode());Move(denseOrigin);yield return StartCoroutine(Settled());
  var denseSpring=denseObject.GetComponent<Spring>();RiverHoe.Select(denseSpring);Move(denseOrigin+Vector3.forward*65);
  Check(RiverHoe.Extend(denseOrigin+Vector3.forward*65),"point-limited old route creates a continuation");yield return StartCoroutine(Settled());
  Check(RiverHoe.Selected!=denseSpring&&RiverHoe.Selected.View.GetZDO().GetBool("aww_continuation",false),"point-limit handoff selects the new persisted section");
  Check(RiverHoe.Selected.GetComponent<Piece>().m_resources.Length==0,"continuation cannot duplicate spring resource refunds");
  Logger.LogInfo(failures==0?"CLASSIC COMPLETE PASS":"CLASSIC COMPLETE FAIL "+failures);Application.Quit(failures==0?0:2);
 }
}
