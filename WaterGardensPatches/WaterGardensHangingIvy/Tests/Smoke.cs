using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using Jotunn.Managers;
using RunicLilyPads;
[BepInPlugin("chazman.IvySmoke","Ivy validation","1.0.0")]
[BepInDependency("chazman.RunicLilyPads")]
public partial class IvySmoke:BaseUnityPlugin {
 bool ready;int errors;string root;
 const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 void Awake(){root=Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,".."));Directory.CreateDirectory(root+"/saves");Utils.SetSaveDataPath(root+"/saves");PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;StartCoroutine(Environment.GetEnvironmentVariable("IVY_NETWORK")=="server"?NetworkServer():Run());}
 void Check(bool yes,string name){if(!yes)errors++;Logger.LogInfo((yes?"IVY PASS ":"IVY FAIL ")+name);}
 IEnumerator Run(){
  float deadline=Time.realtimeSinceStartup+90;while(!ready&&Time.realtimeSinceStartup<deadline)yield return null;
  yield return new WaitForSecondsRealtime(2);
  string[] ids={"piece_arcanedecor_ivy_gold_clinging","piece_arcanedecor_ivy_gold_hanging"};
  foreach(var id in ids){var p=PrefabManager.Instance.GetPrefab(id);Check(p&&p.GetComponent<Piece>().m_icon&&p.GetComponent<GardenTree>(),"registered icon and resize interaction "+id);if(!p){Application.Quit(1);yield break;}
   var mat=p.GetComponentInChildren<MeshRenderer>().sharedMaterial;Check(mat.HasProperty("_EmissiveTex")&&mat.HasProperty("_EmissionColor")&&mat.GetColor("_EmissionColor").maxColorComponent>1,"native material supports bulb emission "+id);
   Check(p.GetComponentsInChildren<Collider>().All(c=>c.isTrigger),"foliage remains pass-through "+id);
  }
  if(Environment.GetEnvironmentVariable("IVY_NETWORK")=="client"){yield return NetworkClient();yield break;}
  var world=World.GetCreateWorld("IvyValidation",FileHelpers.FileSource.Local);Game.SetProfile("ivy_validation",FileHelpers.FileSource.Local);ZNet.SetServer(true,false,false,"IvyValidation","",world);ZNet.ResetServerHost();
  typeof(FejdStartup).GetMethod("LoadMainScene",Hidden).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
  deadline=Time.realtimeSinceStartup+160;while((!Player.m_localPlayer||!ZNetScene.instance)&&Time.realtimeSinceStartup<deadline)yield return null;
  if(!Player.m_localPlayer){Check(false,"world load");Application.Quit(1);yield break;}
  var player=Player.m_localPlayer;player.SetGodMode(true);
  foreach(var bird in UnityEngine.Object.FindObjectsOfType<Valkyrie>())bird.DropPlayer(true);player.SetIntro(false);
  yield return new WaitForSecondsRealtime(2);
  var body=player.GetComponent<Rigidbody>();body.isKinematic=true;
  if(File.Exists(root+"/reload-position.txt")){
   var coords=File.ReadAllText(root+"/reload-position.txt").Split(',');var saved=new Vector3(float.Parse(coords[0]),float.Parse(coords[1]),float.Parse(coords[2]));player.transform.position=saved;body.position=saved;
   yield return new WaitForSecondsRealtime(5);
   var persisted=UnityEngine.Object.FindObjectsOfType<GardenTree>().Where(t=>t.name.StartsWith("piece_arcanedecor_ivy_gold_")).ToArray();
   Check(persisted.Length==2,"both ivy pieces survived full process save/reload");
   foreach(var t in persisted)Check(t.transform.Find("garden_visual").localScale==Vector3.one*1.5f,"saved size restored after process restart "+t.name);
   Logger.LogInfo(errors==0?"IVY RELOAD PASS":"IVY RELOAD FAIL");Application.Quit(errors==0?0:1);yield break;
  }
  var pos=player.transform.position+new Vector3(3,2,0);
  var contact=typeof(GardenTree).Assembly.GetType("RunicLilyPads.PondPlacement").GetMethod("HasSurfaceContact",BindingFlags.NonPublic|BindingFlags.Static);
  var editor=typeof(GardenTreeEditor);var ef=BindingFlags.Static|BindingFlags.NonPublic;
  foreach(var id in ids){
   player.transform.position=pos+new Vector3(0,-1,-2);body.position=player.transform.position;
   TestHammer(id,pos+Vector3.up*5);
   var go=Instantiate(PrefabManager.Instance.GetPrefab(id),pos,Quaternion.identity);yield return null;
   var tree=go.GetComponent<GardenTree>();var visual=go.transform.Find("garden_visual");var anchor=go.transform.position;
   Check(!(bool)contact.Invoke(null,new object[]{go,false}),"unsupported placement rejected "+id);
   var support=GameObject.CreatePrimitive(PrimitiveType.Cube);support.layer=LayerMask.NameToLayer("piece");support.transform.position=pos+new Vector3(0,id.EndsWith("hanging")?.23f:0,id.EndsWith("hanging")?0:.23f);support.transform.localScale=Vector3.one*.5f;Physics.SyncTransforms();
   Check((bool)contact.Invoke(null,new object[]{go,false}),"top attachment accepts "+(id.EndsWith("hanging")?"overhang":"wall"));
   bool editable=(bool)typeof(GardenTree).GetMethod("CanEdit",Hidden).Invoke(tree,null);Check(editable,"piece accessible for interaction "+id);
   if(editable){
    var started=Time.realtimeSinceStartup;tree.Interact(player,false,false);yield return null;var menu=GameObject.Find("WaterGardens tree menu");
    Check(menu&&Time.realtimeSinceStartup-started<.5f,"first interaction opens menu "+id);
    if(menu){
     var texts=menu.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.text).ToArray();Check(texts.Any(t=>t.Contains("IVY"))&&!texts.Any(t=>t.Contains("YGGDRASIL")),"ivy-specific menu labels "+id);
     var bg=menu.GetComponentsInChildren<Image>().First();Check(bg.color.a==1,"opaque menu "+id);
     var slider=menu.GetComponentInChildren<Slider>();slider.value=2;yield return null;
     Check(visual.localScale==Vector3.one*2&&go.transform.position==anchor,"resize keeps top fixed "+id+" scale="+visual.localScale+" distance="+Vector3.Distance(player.transform.position,anchor));
     editor.GetMethod("Close",ef).Invoke(null,null);yield return null;Check(visual.localScale==Vector3.one,"cancel restores size "+id);
    }
    Check((bool)typeof(GardenTree).GetMethod("Save",Hidden).Invoke(tree,new object[]{1.5f}),"save accepted "+id);yield return new WaitForSecondsRealtime(.2f);
    Check(Math.Abs(go.GetComponent<ZNetView>().GetZDO().GetFloat("wg_tree_scale",0)-1.5f)<.001f&&visual.localScale==Vector3.one*1.5f,"size stored and applied "+id);
   }
   Destroy(support);
   // Render the actual Unity material from both sides. No substituted preview shader.
   foreach(var a in new[]{0,180}){
    var icon=RenderManager.Instance.Render(new RenderManager.RenderRequest(visual.gameObject){Width=512,Height=512,Rotation=Quaternion.Euler(0,a,0),DistanceMultiplier=1.2f,ParticleSimulationTime=-1,UseCache=false});
    if(icon)File.WriteAllBytes(root+"/"+id+"-"+a+".png",ImageConversion.EncodeToPNG(icon.texture));
   }
   ZNetScene.instance.Destroy(go);yield return null;Check(!go,"network removal clears ivy "+id);
   pos+=Vector3.forward*2;
  }
  foreach(var id in ids){var saved=Instantiate(PrefabManager.Instance.GetPrefab(id),pos,Quaternion.identity);yield return null;saved.GetComponent<ZNetView>().GetZDO().Set("wg_tree_scale",1.5f);pos+=Vector3.right;}
  File.WriteAllText(root+"/reload-position.txt",player.transform.position.x+","+player.transform.position.y+","+player.transform.position.z);
  Game.instance.SavePlayerProfile(true);typeof(ZNet).GetMethod("SaveWorld",Hidden).Invoke(ZNet.instance,new object[]{false});yield return new WaitForSecondsRealtime(3);
  Logger.LogInfo(errors==0?"IVY COMPLETE PASS":"IVY COMPLETE FAIL "+errors);Application.Quit(errors==0?0:1);
 }
}

