using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke {
 IEnumerator CheckFlora(Assembly asm,Vector3 position){
  var player=Player.m_localPlayer;var prior=player.transform.position;player.transform.position=position+new Vector3(-7,1,0);
  foreach(string suffix in new[]{"fern_red_001","grass_plains_gold","grass_plains_green","grass_blackforest","bush_forest","bush_plains","yggdrasil_001","yggdrasil_002","yggdrasil_003"}){
   var prefab=PrefabManager.Instance.GetPrefab("piece_arcanedecor_"+suffix);
   FeatureCheck(prefab!=null,"new flora registered "+suffix);if(!prefab)continue;
   bool tree=suffix.StartsWith("yggdrasil");int solid=0;foreach(var c in prefab.GetComponentsInChildren<Collider>(true))if(!c.isTrigger)solid++;
   FeatureCheck(tree?solid>0:solid==0,"appropriate foliage/trunk collision "+suffix);
   FeatureCheck(prefab.GetComponentInChildren<Pickable>(true)==null&&prefab.GetComponentInChildren<TreeBase>(true)==null,"decorative flora has no harvest/felling logic "+suffix);
   FeatureCheck(prefab.GetComponent<Piece>().m_icon!=null,"model icon "+suffix);
   var visual=prefab.transform.Find("garden_visual");
   var thumbnail=RenderManager.Instance.Render(new RenderManager.RenderRequest(visual.gameObject){Width=512,Height=512,Rotation=RenderManager.IsometricRotation,DistanceMultiplier=suffix.StartsWith("bush_")?.6f:1,ParticleSimulationTime=-1,UseCache=false});
   if(thumbnail)File.WriteAllBytes(Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures/flora_"+suffix+".png")),ImageConversion.EncodeToPNG(thumbnail.texture));
   var placed=UnityEngine.Object.Instantiate(prefab,position,Quaternion.identity);yield return new WaitForSecondsRealtime(.3f);
   typeof(Piece).GetField("m_creator",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(placed.GetComponent<Piece>(),1L);
   placed.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator,1L);
   var contact=asm.GetType("RunicLilyPads.PondPlacement").GetMethod("HasSurfaceContact",BindingFlags.Static|BindingFlags.NonPublic);
   FeatureCheck((bool)contact.Invoke(null,new object[]{placed,true}),"flora accepts terrain placement "+suffix);
   placed.transform.position+=Vector3.up*2;Physics.SyncTransforms();FeatureCheck(!(bool)contact.Invoke(null,new object[]{placed,true}),"flora rejects floating placement "+suffix);placed.transform.position=position;
   if(tree){
    player.transform.position=position+new Vector3(-3,1,0);yield return null;
    var type=asm.GetType("RunicLilyPads.GardenTree");var editor=asm.GetType("RunicLilyPads.GardenTreeEditor");var component=placed.GetComponent(type);
    var other=UnityEngine.Object.Instantiate(prefab,position+Vector3.forward*3,Quaternion.identity);yield return null;
    var treeVisual=placed.transform.Find("garden_visual");var originalRoot=placed.transform.position;
    FeatureCheck((bool)type.GetMethod("CanEdit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(component,null),"player is in range for tree edit "+suffix+" distance="+Vector3.Distance(player.transform.position,position));
    var flags=BindingFlags.NonPublic|BindingFlags.Static;
    editor.GetMethod("Open",flags).Invoke(null,new[]{component});yield return null;
    var menu=GameObject.Find("WaterGardens tree menu");FeatureCheck(menu!=null,"individual tree has its own native menu "+suffix);
    if(menu){CaptureTreeMenu(menu);var slider=menu.GetComponentInChildren<UnityEngine.UI.Slider>();slider.value=1.5f;yield return null;
     FeatureCheck(Mathf.Abs(treeVisual.localScale.x-1.5f)<.001f&&placed.transform.position==originalRoot,"slider previews scale without moving roots "+suffix);
     editor.GetMethod("Close",flags).Invoke(null,null);yield return null;
     FeatureCheck(Mathf.Abs(treeVisual.localScale.x-1)<.001f,"cancel restores saved tree size "+suffix);
    }
    bool saved=(bool)type.GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(component,new object[]{1.75f});yield return new WaitForSecondsRealtime(.6f);
    FeatureCheck(saved&&Mathf.Abs(placed.GetComponent<ZNetView>().GetZDO().GetFloat("wg_tree_scale",0)-1.75f)<.001f,"tree size saved in its ZDO "+suffix);
    FeatureCheck(other.transform.Find("garden_visual").localScale==Vector3.one,"neighboring tree size unchanged "+suffix);
    bool scaledColliders=true;foreach(var collider in treeVisual.GetComponentsInChildren<Collider>())scaledColliders&=collider.transform.IsChildOf(treeVisual);
    FeatureCheck(scaledColliders&&treeVisual.localScale==Vector3.one*1.75f,"trunk and selection colliders inherit size "+suffix);
    placed.GetComponent<ZNetView>().GetZDO().Set("wg_tree_scale",.5f);yield return null;
    FeatureCheck(treeVisual.localScale==Vector3.one*.5f,"replicated saved size applies to existing tree "+suffix);
    player.transform.position=position+new Vector3(-7,1,0);other.GetComponent<WearNTear>().Remove(true);yield return new WaitForSecondsRealtime(.3f);
   }
   int before=CountDrops(position),cost=tree?10:2;
   placed.GetComponent<WearNTear>().Remove();yield return new WaitForSecondsRealtime(.6f);
   int returned=CountDrops(position)-before;FeatureCheck(!placed&&returned==cost,"flora removed with exact material refund "+suffix+" returned="+returned+" expected="+cost+" survived="+(placed!=null));
  }
  player.transform.position=prior;
 }
 void CaptureTreeMenu(GameObject menu){
  var ui=menu.GetComponent<Canvas>();var camera=new GameObject("tree_menu_capture").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
  var rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;ui.renderMode=RenderMode.ScreenSpaceCamera;ui.worldCamera=camera;ui.planeDistance=1;
  Canvas.ForceUpdateCanvases();camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;
  var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
  File.WriteAllBytes(Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures/tree_menu.png")),ImageConversion.EncodeToPNG(image));
  RenderTexture.active=old;ui.renderMode=RenderMode.ScreenSpaceOverlay;ui.worldCamera=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(camera.gameObject);
 }
}
