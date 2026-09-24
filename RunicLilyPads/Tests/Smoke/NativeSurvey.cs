using System.IO;
using System.Text;
using UnityEngine;
public partial class Smoke {
 void SurveyNative(){
 var report=new StringBuilder();
 foreach(var c in ClutterSystem.instance.m_clutter)report.AppendLine("CLUTTER "+c.m_name+" | "+c.m_biome+" | "+(c.m_prefab?c.m_prefab.name:"null")+" scale="+c.m_scaleMin+".."+c.m_scaleMax);
 foreach(var p in Resources.FindObjectsOfTypeAll<GameObject>()){
 string n=p.name.ToLowerInvariant();if(n.Contains("ygg")||n.Contains("bush")||n.Contains("grass")){
 report.AppendLine("ASSET "+p.name+" meshes="+p.GetComponentsInChildren<MeshFilter>(true).Length+" instance="+(p.GetComponent<InstanceRenderer>()!=null));
 if(n.Contains("ygg"))foreach(var f in p.GetComponentsInChildren<MeshFilter>(true))report.AppendLine("  MESH "+f.name+" "+(f.sharedMesh?f.sharedMesh.bounds.ToString():"null")+" active="+f.gameObject.activeSelf);
 }}
 File.WriteAllText(Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../native-survey.txt")),report.ToString());
 }
}
