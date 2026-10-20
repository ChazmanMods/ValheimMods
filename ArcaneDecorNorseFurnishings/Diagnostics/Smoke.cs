using System;
using System.Collections;
using System.Linq;
using System.IO;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;
[BepInPlugin("chazman.NorseFurnishingsSmoke","Norse Furnishings targeted checks","0.1.1")]
[BepInDependency(ArcaneDecorNorseFurnishings.Plugin.Guid)]
public class Smoke:BaseUnityPlugin
{
 string folder; int errors;
 void Awake(){folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,".."));Utils.SetSaveDataPath(Path.Combine(folder,"saves"));StartCoroutine(Run());}
 void Check(bool ok,string text){File.AppendAllText(Path.Combine(folder,"targeted-results.txt"),(ok?"PASS ":"FAIL ")+text+Environment.NewLine);if(!ok)errors++;}
 IEnumerator Run()
 {
  float end=Time.realtimeSinceStartup+100;GameObject p=null;
  while(!p&&Time.realtimeSinceStartup<end){p=PrefabManager.Instance.GetPrefab(ArcaneDecorNorseFurnishings.Plugin.BedPrefab);yield return new WaitForSecondsRealtime(1);}
  if(!p){Check(false,"prefab registration");Application.Quit();yield break;}
  yield return new WaitForSecondsRealtime(3);
  var visual=p.transform.Find("norse_king_bed_visual");var mesh=visual.GetComponent<MeshFilter>().sharedMesh;var mat=visual.GetComponent<MeshRenderer>().sharedMaterial;
  var bed=p.GetComponent<Bed>();Check(Vector3.Dot(bed.m_spawnPoint.localRotation*Vector3.forward,Vector3.back)>.999f,"sleep anchor rotated 180 degrees toward correct native pose");
  var normals=mesh.normals;var ids=mesh.triangles;int smooth=0;
  for(int i=0;i<ids.Length;i+=3)if((normals[ids[i]]-normals[ids[i+1]]).sqrMagnitude>1e-12f)smooth++;
  Check(smooth>ids.Length/6,"smooth shading across shared geometric vertices; GPU vertices="+mesh.vertexCount);
  Check(mat.GetColor("_Color").r>1.6f&&(!mat.HasProperty("_ValueNoise")||mat.GetFloat("_ValueNoise")==0)&&!mat.IsKeywordEnabled("_NORMALMAP"),"brighter albedo multiplier and no inherited noise/normal-map shading");
  var piece=p.GetComponent<Piece>();var req=piece.m_resources.Select(r=>r.m_resItem.gameObject.name+":"+r.m_amount).ToArray();
  Check(req.SequenceEqual(new[]{"WolfPelt:2","LoxPelt:2","Frostwood:3","JuteRed:2","AmberPearl:1"})&&p.GetComponentsInChildren<Collider>(true).Length==2&&mesh.triangles.Length==119988&&p.GetComponent<ZNetView>()&&p.GetComponent<WearNTear>()&&piece.m_comfort==PrefabManager.Instance.GetPrefab("piece_bed02").GetComponent<Piece>().m_comfort,"neighboring recipe, collider count, triangle count, native components and comfort preserved");
  File.AppendAllText(Path.Combine(folder,"targeted-results.txt"),"SIZE "+mesh.bounds.size+"\n");
  var model=UnityEngine.Object.Instantiate(visual.gameObject);model.transform.position=Vector3.zero;model.transform.rotation=Quaternion.identity;model.layer=31;model.SetActive(true);
  var center=mesh.bounds.center;float extent=Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z);
  var light=new GameObject("TestLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(45,-35,0);light.cullingMask=1<<31;
  RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.4f,.4f,.4f);RenderSettings.fog=false;
  var cam=new GameObject("TestCamera").AddComponent<Camera>();cam.cullingMask=1<<31;cam.transform.position=center+new Vector3(1.4f,1.1f,-2f)*extent;cam.transform.LookAt(center);cam.orthographic=true;cam.orthographicSize=extent*.75f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.18f,.18f,1);cam.allowHDR=false;
  var rt=new RenderTexture(768,768,24);cam.targetTexture=rt;yield return null;cam.Render();RenderTexture.active=rt;
  var image=new Texture2D(768,768,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"rendered-bed.png"),image.EncodeToPNG());RenderTexture.active=null;cam.targetTexture=null;
  File.AppendAllText(Path.Combine(folder,"targeted-results.txt"),"RESULT errors="+errors+"; controlled Unity render written\n");
  yield return new WaitForSecondsRealtime(2);Application.Quit();
 }
}
