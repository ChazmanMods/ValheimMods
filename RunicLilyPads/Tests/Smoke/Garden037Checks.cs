using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke {
 IEnumerator CheckWind(Assembly asm,string[] ids,Vector3 ground){
  string[] namedIds={"grass_plains_gold","grass_plains_green","grass_blackforest","bush_forest","bush_plains","fauna_001"};
  string[] labels={"Tall Grass 1","Tall Grass 2","Tall Grass 3","Bush 1","Bush 2","Garden Flora Cluster"};
  for(int i=0;i<labels.Length;i++)FeatureCheck(PrefabManager.Instance.GetPrefab("piece_arcanedecor_"+namedIds[i]).GetComponent<Piece>().m_name==labels[i],"garden display name "+labels[i]);
  var type=asm.GetType("RunicLilyPads.GardenWind");int count=0;
  foreach(var id in ids){var prefab=PrefabManager.Instance.GetPrefab(id);if(!prefab||!prefab.GetComponent(type))continue;count++;EnvMan.instance.SetDebugWind(0,.8f);
   var placed=UnityEngine.Object.Instantiate(prefab,ground,Quaternion.identity);yield return new WaitForSecondsRealtime(.2f);
   var anchor=placed.transform.position;bool gpu=true;int materials=0;
   foreach(var renderer in placed.GetComponentsInChildren<MeshRenderer>())foreach(var material in renderer.sharedMaterials){materials++;gpu&=material.HasProperty("_SwayDistance")&&material.GetFloat("_SwayDistance")>0;}
   FeatureCheck(gpu&&materials>0&&!((Behaviour)placed.GetComponent(type)).enabled&&placed.transform.position==anchor,"native GPU wind without per-frame CPU deformation "+id);
   ZNetScene.instance.Destroy(placed);yield return null;
  }
  EnvMan.instance.ResetDebugWind();FeatureCheck(count==34,"all 32 plant entries plus 2 legacy lily IDs have wind");
 }
 IEnumerator CheckShoreAndStone(Assembly asm,Vector3 ground){
  var sample=asm.GetType("RunicLilyPads.PondTerrain").GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic);
  var pond=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground,Quaternion.identity);
  foreach(int width in new[]{8,16,6}){
   pond.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|"+width+"|1.2|0|0|1|18|0|0|0.4|1|1|1|100");yield return new WaitForSecondsRealtime(3);
   var stone=pond.transform.Find("garden_visual/control_stone");var args=new object[]{stone.position,0f};bool sampled=(bool)sample.Invoke(null,args);
   FeatureCheck(sampled&&Mathf.Abs(stone.position.y-(float)args[1]-.015f)<.03f,"configuration rock rests on terrain at width "+width);
   float worst=-100;for(int i=0;i<64;i++){float angle=i*Mathf.PI*2/64;var p=ground+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*width*.5f;args=new object[]{p,0f};if((bool)sample.Invoke(null,args))worst=Mathf.Max(worst,(float)args[1]-(ground.y-.15f));}
   FeatureCheck(worst<.10f,"water reaches bank after placement/resize width "+width+" maximum dry lip="+worst);
  }
  ZNetScene.instance.Destroy(pond);yield return new WaitForSecondsRealtime(2);
 }
}



