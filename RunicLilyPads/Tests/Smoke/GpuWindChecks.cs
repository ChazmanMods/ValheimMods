using System;
using System.Collections;
using System.Reflection;
using System.IO;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke {
 IEnumerator CheckRenderedWind(Assembly asm,Vector3 ground){
  string[] ids={"fern_red_001","fern_001","fern_002","fauna_001","cattail_001","reeds_006","glowshrooms_001","lily_blue_001","lily_blue_002","lily_blue_003","lily_white_001","lily_white_002","lily_white_003"};
  var saveWind1=Shader.GetGlobalVector("_GlobalWind1");var saveWind2=Shader.GetGlobalVector("_GlobalWind2");var saveForce=Shader.GetGlobalVector("_GlobalWindForce");float saveAlpha=Shader.GetGlobalFloat("_GlobalWindAlpha");
  foreach(var id in ids){
   var prefab=PrefabManager.Instance.GetPrefab("piece_arcanedecor_"+id);var placed=UnityEngine.Object.Instantiate(prefab,ground+Vector3.up*50,Quaternion.identity);yield return null;
   foreach(var c in placed.GetComponents<MonoBehaviour>())if(c&&c.GetType().Namespace=="RunicLilyPads")c.enabled=false;
   var model=placed.transform.Find("garden_visual");if(!model)model=placed.transform.Find("lily_visual");
   var renderers=model.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers){r.gameObject.layer=31;bounds.Encapsulate(r.bounds);}
   // Frame imported flower itself; lily stems are much taller than the flower.
   if(id.StartsWith("lily_"))foreach(var r in renderers)if(r.sharedMaterial.name.Contains("lilly")||r.sharedMaterial.name.Contains("iris")){bounds=r.bounds;break;}
   var camera=new GameObject("gpu_wind_check").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=Mathf.Max(.12f,bounds.size.y,bounds.size.x)*.75f;
   camera.transform.position=bounds.center+new Vector3(0,0,-5);camera.transform.LookAt(bounds.center);
   var rt=new RenderTexture(384,384,24);camera.targetTexture=rt;var prior=RenderTexture.active;var image=new Texture2D(384,384,TextureFormat.RGBA32,false);
   Color32[] first=null;int visible=0,changed=0;
   for(int step=0;step<2;step++){
    var wind=step==0?new Vector4(0,0,1,0):new Vector4(1,0,0,1);
    Shader.SetGlobalVector("_GlobalWind1",wind);Shader.SetGlobalVector("_GlobalWind2",wind);Shader.SetGlobalVector("_GlobalWindForce",new Vector4(wind.x*wind.w,0,wind.z*wind.w,0));Shader.SetGlobalFloat("_GlobalWindAlpha",0);
    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,384,384),0,0);image.Apply();var pixels=image.GetPixels32();
    if(step==0){first=pixels;foreach(var pixel in pixels)if(pixel.r+pixel.g+pixel.b>12)visible++;}
    else for(int i=0;i<pixels.Length;i++)if((pixels[i].r+pixels[i].g+pixels[i].b>12)!=(first[i].r+first[i].g+first[i].b>12))changed++;
    if(id=="fern_red_001"||id=="lily_white_003")File.WriteAllBytes(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures/wind_"+id+"_"+step+".png"),ImageConversion.EncodeToPNG(image));
   }
   FeatureCheck(visible>100&&changed>15,"imported plant visibly bends in native shader "+id+" silhouette pixels="+changed+" visible="+visible);
   Shader.SetGlobalVector("_GlobalWind1",saveWind1);Shader.SetGlobalVector("_GlobalWind2",saveWind2);Shader.SetGlobalVector("_GlobalWindForce",saveForce);Shader.SetGlobalFloat("_GlobalWindAlpha",saveAlpha);
   RenderTexture.active=prior;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(camera.gameObject);ZNetScene.instance.Destroy(placed);yield return null;
  }
 }
}
