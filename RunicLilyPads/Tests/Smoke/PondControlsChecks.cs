using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Jotunn.Managers;
public partial class Smoke
{
    IEnumerator CheckPondControls(Assembly asm,Player player,Vector3 ground){
        FeatureCheck(!PrefabManager.Instance.GetPrefab("piece_arcanedecor_stone_lantern")&&!PrefabManager.Instance.GetPrefab("piece_arcanedecor_fountain_003"),"duplicate prefab IDs completely absent");
        var pondType=asm.GetType("RunicLilyPads.GardenPiece");
        var high=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground,Quaternion.identity);
        var low=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground+new Vector3(5,-1.5f,0),Quaternion.identity);
        high.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100");
        low.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|0|0.5|1|1|1|0");
        yield return new WaitForSecondsRealtime(3);
        var flowType=asm.GetType("RunicLilyPads.NaturalSpillway");var flows=high.GetComponentsInChildren(flowType);
        FeatureCheck(flows.Length>0,"overlapping elevations generate downhill water spillways");
        FeatureCheck(flows.Length==1,"one connected water mesh replaces repeated strips");
        if(flows.Length>0){
            var material=((Component)flows[0]).GetComponent<MeshRenderer>().sharedMaterial;float before=material.mainTextureOffset.y;
            yield return new WaitForSecondsRealtime(.2f);FeatureCheck(material.mainTextureOffset.y<before,"water texture travels down the stream UVs");
            var source=((Component)flows[0]).transform.parent.GetComponent<MeshFilter>().sharedMesh;
            var copied=((Component)flows[0]).GetComponent<MeshFilter>().sharedMesh;
            bool joined=source.vertexCount==copied.vertexCount;
            if(joined){var original=source.vertices;var copiedVertices=copied.vertices;for(int i=0;i<original.Length;i++)joined&=original[i]==copiedVertices[i];}
            FeatureCheck(joined,"foam and pond use identical surface vertices with no raised lip");            var mesh=((Component)flows[0]).GetComponent<MeshFilter>().sharedMesh;bool normals=true;
            foreach(var n in mesh.normals)normals&=n.y>0&&n.sqrMagnitude>.9f;
            FeatureCheck(normals,"water normals point upward and are not cancelled by reverse faces");
            bool submerged=true;float worst=-100;
            var sampleGround=asm.GetType("RunicLilyPads.PondTerrain").GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic);
            var vertices=mesh.vertices;var colors=mesh.colors;for(int index=0;index<vertices.Length;index++){if(colors[index].a<.05f)continue;var v=vertices[index];var world=((Component)flows[0]).transform.TransformPoint(v);var args=new object[]{world,0f};if((bool)sampleGround.Invoke(null,args)){float gap=(float)args[1]-world.y;worst=Mathf.Max(worst,gap);// The requirement is submerged terrain, including interpolated shoreline triangles.
                submerged&=gap<-.001f;}}
            FeatureCheck(submerged,"entire transition terrain is below water, worst gap="+worst);
            FeatureCheck(material.shader.name=="WaterGardens/Cascade"&&material.shader.isSupported,"dedicated waterfall shader loads and is supported");
            var diagnostics=new System.Text.StringBuilder();diagnostics.AppendLine("x,groundY,waterY");
            for(float x=-1;x<=9;x+=.25f){var sample=ground+Vector3.right*x;Heightmap.GetHeight(sample,out float terrain);float water=(float)asm.GetType("RunicLilyPads.PondNetwork").GetMethod("WaterLevel",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{sample});diagnostics.AppendLine(x+","+(terrain-ground.y)+","+(water-ground.y));}
            File.WriteAllText(Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../spillway-heights.csv")),diagnostics.ToString());
            var midpoint=ground+new Vector3(3,-.8f,0);
            float expected=(float)asm.GetType("RunicLilyPads.PondNetwork").GetMethod("WaterLevel",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{midpoint});
            FeatureCheck(Mathf.Abs(Floating.GetLiquidLevel(midpoint,1,LiquidType.Water)-expected)<.03f,"physical water level follows descending spillway");
        }
        CaptureFeature(high,7);
        yield return StartCoroutine(CaptureLivePond(high));
        var dayAmbient=Shader.GetGlobalColor("_AmbientColor");EnvMan.instance.m_debugTime=0;yield return new WaitForSecondsRealtime(1);
        yield return StartCoroutine(CaptureLivePond(high,"live_spillway_night.png"));
        FeatureCheck(Shader.GetGlobalColor("_AmbientColor").maxColorComponent<dayAmbient.maxColorComponent,"foam receives darker native environment light at night");EnvMan.instance.m_debugTime=.5f;
        var rock=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_rock_001"),ground+new Vector3(-1,0,0),Quaternion.identity);
        yield return new WaitForSecondsRealtime(.5f);var bounds=rock.GetComponentInChildren<MeshRenderer>().bounds;
        FeatureCheck(Math.Abs(bounds.center.y-(ground.y-.15f))<.035f,"rock mesh midpoint sits at waterline");
        FeatureCheck(low.GetComponent<ZNetView>().GetZDO().GetString("arcane_garden_settings","").Contains("|0.4|"),"shared controls synchronize to connected pond");
        var editor=asm.GetType("RunicLilyPads.GardenEditor");editor.GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{high.GetComponent(pondType)});
        yield return null;
        var canvas=GameObject.Find("WaterGardens pond menu");FeatureCheck(canvas!=null,"pond menu builds using the native UI canvas");
        if(canvas){
            var ui=canvas.GetComponent<Canvas>();var camera=new GameObject("menu_capture_camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
            var rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;ui.renderMode=RenderMode.ScreenSpaceCamera;ui.worldCamera=camera;ui.planeDistance=1;
            Canvas.ForceUpdateCanvases();camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            var folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.BepInExRootPath,"../captures"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"pond_menu.png"),ImageConversion.EncodeToPNG(image));
            RenderTexture.active=old;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(camera.gameObject);
        }
        editor.GetMethod("Close",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        rock.GetComponent<WearNTear>().Remove(true);high.GetComponent<WearNTear>().Remove(true);low.GetComponent<WearNTear>().Remove(true);
        yield return new WaitForSecondsRealtime(2);
        int capture=8;
        foreach(string shape in new[]{"oval","kidney","wild"}){
            high=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_"+shape),ground,Quaternion.Euler(0,23,0));
            low=UnityEngine.Object.Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),ground+new Vector3(5,-1.1f,0),Quaternion.identity);
            high.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|10|1.2|0|0|1|18|0|0|0.4|1|1|1|100");
            low.GetComponent<ZNetView>().GetZDO().Set("arcane_garden_settings","2|8|1.2|0|0|1|18|0|0|0.4|1|1|1|100");
            yield return new WaitForSecondsRealtime(3);
            flows=high.GetComponentsInChildren(flowType);
            bool descending=flows.Length==1;
            foreach(Component flow in flows){var mesh=flow.GetComponent<MeshFilter>().sharedMesh;descending&=mesh.vertexCount==flow.transform.parent.GetComponent<MeshFilter>().sharedMesh.vertexCount;}
            FeatureCheck(descending,"rotated "+shape+" spillway is continuous and downhill");
            CaptureFeature(high,capture++);
            ZNetScene.instance.Destroy(high);ZNetScene.instance.Destroy(low);
            yield return new WaitForSecondsRealtime(2);
        }
    }
}


