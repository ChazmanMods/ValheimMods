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
[BepInPlugin("chazman.IvySmoke","Ivy validation","1.1.0")]
[BepInDependency("chazman.RunicLilyPads")]
public partial class IvySmoke:BaseUnityPlugin {
 bool ready;int errors;string root;const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 void Awake(){root=Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,".."));Utils.SetSaveDataPath(root+"/saves");PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;StartCoroutine(Environment.GetEnvironmentVariable("IVY_NETWORK")=="server"?NetworkServer():Run());}
 void Check(bool yes,string text){if(!yes)errors++;Logger.LogInfo((yes?"IVY PASS ":"IVY FAIL ")+text);}
 object Call(IvyLights ivy,string method,params object[] args)=>typeof(IvyLights).GetMethod(method,Hidden).Invoke(ivy,args);
 IEnumerator Run(){
  float until=Time.realtimeSinceStartup+90;while(!ready&&Time.realtimeSinceStartup<until)yield return null;
  if(Environment.GetEnvironmentVariable("IVY_NETWORK")=="client"){yield return NetworkClient();yield break;}
  var world=World.GetCreateWorld("IvyValidation",FileHelpers.FileSource.Local);Game.SetProfile("ivy_validation",FileHelpers.FileSource.Local);ZNet.SetServer(true,false,false,"IvyValidation","",world);ZNet.ResetServerHost();typeof(FejdStartup).GetMethod("LoadMainScene",Hidden).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
  until=Time.realtimeSinceStartup+160;while(!Player.m_localPlayer&&Time.realtimeSinceStartup<until)yield return null;
  if(!Player.m_localPlayer){Check(false,"world loaded");Application.Quit(1);yield break;}
  var p=Player.m_localPlayer;p.SetGodMode(true);foreach(var b in UnityEngine.Object.FindObjectsOfType<Valkyrie>())b.DropPlayer(true);p.SetIntro(false);yield return new WaitForSecondsRealtime(2);
  var xyz=File.ReadAllText(root+"/persisted-position.txt").Split(',');var pos=new Vector3(float.Parse(xyz[0]),float.Parse(xyz[1]),float.Parse(xyz[2]));p.transform.position=pos;p.GetComponent<Rigidbody>().position=pos;p.GetComponent<Rigidbody>().isKinematic=true;yield return new WaitForSecondsRealtime(3);
  var existing=UnityEngine.Object.FindObjectsOfType<IvyLights>();Check(existing.Length>=2,"1.3.0 ivy IDs upgrade without losing pieces");
  if(File.Exists(root+"/reload-new.txt")){
   var saved=existing.FirstOrDefault(t=>t.GetComponent<ZNetView>().GetZDO().GetFloat("wg_ivy_group2",1)>2.9f);Check(saved,"new bulb/color state survived process restart");if(saved){var z=saved.GetComponent<ZNetView>().GetZDO();Check(z.GetFloat("wg_ivy_b",0)==1&&z.GetFloat("wg_ivy_r",1)==0,"blue persisted on restart");Check(z.GetFloat("wg_ivy_group0",1)==2&&z.GetFloat("wg_ivy_group1",1)==2.5f,"all three independent groups persisted");}TestHammer("piece_arcanedecor_ivy_gold_hanging",pos+Vector3.up*6+Vector3.forward*10,true);Finish();yield break;
  }
  foreach(var e in existing)Check(Math.Abs(e.transform.Find("garden_visual").localScale.x-e.GetComponent<ZNetView>().GetZDO().GetFloat("wg_tree_scale",1))<.01f,"prior ivy size retained");
  string[] ids={"piece_arcanedecor_ivy_gold_clinging","piece_arcanedecor_ivy_gold_hanging"};
  foreach(var id in ids){
   p.transform.position=pos;p.GetComponent<Rigidbody>().position=pos;
   var go=Instantiate(PrefabManager.Instance.GetPrefab(id),pos+Vector3.forward*2,Quaternion.identity);yield return null;yield return null;var ivy=go.GetComponent<IvyLights>();var visual=go.transform.Find("garden_visual");var model=visual.GetComponentInChildren<MeshFilter>();var initial=model.sharedMesh.vertices;var anchor=go.transform.position;
   float start=Time.realtimeSinceStartup;Check(ivy.Interact(p,false,false),"first interaction accepted "+id);yield return null;Check(Time.realtimeSinceStartup-start<.5f,"menu responds within one frame budget");
   var canvas=UnityEngine.Object.FindObjectOfType<GardenMenuCursor>().GetComponent<Canvas>();Check(canvas.GetComponentsInChildren<Image>().First().color.a==1,"opaque ivy menu");Check(canvas.GetComponentInChildren<TMPro.TMP_InputField>(),"hex input available");
   foreach(var hex in new[]{"#FF0000","#00FF00","#0000FF","#FFFFFF","#000000"}){Call(ivy,"SetHex",hex);var block=new MaterialPropertyBlock();model.GetComponent<MeshRenderer>().GetPropertyBlock(block,1);ColorUtility.TryParseHtmlString(hex,out var want);Check(block.GetColor("_Color")==want,"true bulb color "+hex);}
   Call(ivy,"SetHex","#0000FF");Call(ivy,"SetBulbs",2f);Call(ivy,"NextGroup");Call(ivy,"SetBulbs",2.5f);Call(ivy,"NextGroup");Call(ivy,"SetBulbs",3f);
   var after=model.sharedMesh.vertices;var groups=(int[])typeof(IvyLights).GetField("groups",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);bool leaves=true;var changed=new bool[3];for(int i=0;i<initial.Length;i++){if(groups[i]<0&&initial[i]!=after[i])leaves=false;if(groups[i]>=0&&initial[i]!=after[i])changed[groups[i]]=true;}Check(leaves&&changed.All(x=>x),"three bulb groups resize without moving foliage");
   Call(ivy,"SetSize",1.5f);Check(go.transform.position==anchor&&visual.localScale==Vector3.one*1.5f,"whole ivy size keeps attachment fixed");
   Capture(visual.gameObject,id);
   Call(ivy,"Cancel");Check(visual.localScale==Vector3.one&&model.sharedMesh.vertices.SequenceEqual(initial),"Cancel restores size and groups");
   Call(ivy,"Open");Call(ivy,"SetHex","#0000FF");Call(ivy,"SetBulbs",2f);Call(ivy,"NextGroup");Call(ivy,"SetBulbs",2.5f);Call(ivy,"NextGroup");Call(ivy,"SetBulbs",3f);Call(ivy,"Save");yield return null;
   Check(go.GetComponent<ZNetView>().GetZDO().GetFloat("wg_ivy_group2",0)==3,"Save writes selected group values");
   TestHammer(id,pos+Vector3.up*5);yield return null;pos+=Vector3.right*3;
  }
  TestHammer("piece_arcanedecor_ivy_gold_hanging",pos+Vector3.up*6,true);yield return null;
  Game.instance.SavePlayerProfile(true);typeof(ZNet).GetMethod("SaveWorld",Hidden).Invoke(ZNet.instance,new object[]{false});File.WriteAllText(root+"/reload-new.txt","saved");yield return new WaitForSecondsRealtime(2);Finish();
 }
 void Capture(GameObject target,string id){
  var transforms=target.GetComponentsInChildren<Transform>();var layers=transforms.Select(t=>t.gameObject.layer).ToArray();foreach(var t in transforms)t.gameObject.layer=30;
  var bounds=target.GetComponentInChildren<MeshRenderer>().bounds;var host=new GameObject("Ivy render camera");var camera=host.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=bounds.extents.y*1.15f;camera.transform.position=bounds.center+Vector3.back*5;camera.transform.LookAt(bounds.center);
  var rt=new RenderTexture(512,768,24);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(512,768,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,512,768),0,0);texture.Apply();File.WriteAllBytes(root+"/"+id+"-blue.png",ImageConversion.EncodeToPNG(texture));RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(texture);Destroy(host);for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.layer=layers[i];
 }
 void Finish(){Logger.LogInfo(errors==0?"IVY COMPLETE PASS":"IVY COMPLETE FAIL "+errors);Application.Quit(errors==0?0:1);}
}
