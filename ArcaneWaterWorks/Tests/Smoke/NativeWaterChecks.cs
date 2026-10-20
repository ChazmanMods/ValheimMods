using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcaneWaterWorks;
using BepInEx;
using UnityEngine;

public sealed partial class Smoke
{
    IEnumerator NativeWaterTest(GameObject prefab,Vector3 origin){
        var native=RunicLilyPads.RiverGardens.OutdoorWaterSource();var tint=native.GetColor("_ColorTop");var shader=native.shader;
        Logger.LogInfo("NATIVE keywords "+string.Join(",",native.shaderKeywords));
        for(int property=0;property<shader.GetPropertyCount();property++){
            var name=shader.GetPropertyName(property);var type=shader.GetPropertyType(property);
            if(type==UnityEngine.Rendering.ShaderPropertyType.Float||type==UnityEngine.Rendering.ShaderPropertyType.Range)Logger.LogInfo("NATIVE property "+name+"="+native.GetFloat(name));
            if(type==UnityEngine.Rendering.ShaderPropertyType.Texture)Logger.LogInfo("NATIVE texture "+name+"="+native.GetTexture(name)+" scale="+native.GetTextureScale(name));
        }
        var a=new Route{Width=6,Depth=1.2f};a.Points.Add(new Node(0,0,0));a.Points.Add(new Node(0,-8,8));
        var b=new Route{Width=6,Depth=1.2f};b.Points.Add(new Node(0,0,0));b.Points.Add(new Node(0,-4,8));
        var left=Instantiate(prefab,origin,Quaternion.identity);var right=Instantiate(prefab,origin+new Vector3(4,-2,0),Quaternion.identity);
        left.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",a.Encode());right.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",b.Encode());
        yield return StartCoroutine(WaitForWaterWork(2));
        var sa=left.GetComponent<Spring>();var sb=right.GetComponent<Spring>();
        Check(sa.SurfaceMembers!=null&&ReferenceEquals(sa.SurfaceMembers,sb.SurfaceMembers),"separate unequal-slope rivers share one surface group");
        float worst=0;
        for(float x=0;x<=4;x+=.25f)for(float z=1;z<=7;z+=.25f){
            sa.Surface(x,z,out _,out float ha,out _);sb.Surface(x-4,z,out _,out float hb,out _);
            worst=Mathf.Max(worst,Mathf.Abs(ha-hb+2));
        }
        Check(worst<.001f,"unequal-slope overlap has identical world heights across both meshes");
        foreach(var root in new[]{left,right}){
            var material=root.transform.Find("waterworks_river/river_surface").GetComponent<MeshRenderer>().sharedMaterial;
            Check(material.shader==shader&&material!=native&&material.GetColor("_ColorTop")==tint,"river uses an independent copy of the outdoor water material");
            Check(material.GetFloat("_UseGlobalWind")==1&&material.GetFloatArray("_depth").All(d=>d==0),"native ripples stay enabled without adding ocean wave displacement to the river");
        }
        yield return StartCoroutine(CaptureStable(sa,new Vector3(10,7,12),origin+new Vector3(2,-4,4),"unequal-slope-native.png"));
        ZNetScene.instance.Destroy(left);ZNetScene.instance.Destroy(right);yield return StartCoroutine(WaitForWaterWork(1));
        Check(native.shader==shader&&native.GetColor("_ColorTop")==tint,"building and removing rivers does not alter native ocean material");


        // A shallow creek cut into existing ground, rather than an elevated ramp.
        var creekOrigin=origin;RiverTerrain.SampleBase(creekOrigin,out creekOrigin.y);creekOrigin.y-=.25f;
        var creekRoute=new Route{Width=5,Depth=.6f};creekRoute.Points.Add(new Node(0,0,0));creekRoute.Points.Add(new Node(0,-.15f,6));creekRoute.Points.Add(new Node(0,-.3f,12));
        var creek=Instantiate(prefab,creekOrigin,Quaternion.identity);creek.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",creekRoute.Encode());
        yield return StartCoroutine(WaitForWaterWork(2));
        var creekSpring=creek.GetComponent<Spring>();
        yield return StartCoroutine(CaptureStable(creekSpring,new Vector3(6,5,-8),creekOrigin+new Vector3(0,0,5),"natural-creek.png"));
        var rock=GameObject.CreatePrimitive(PrimitiveType.Cube);rock.name="isolated solid rock fixture";rock.layer=LayerMask.NameToLayer("static_solid");rock.transform.position=creekOrigin+new Vector3(0,-.15f,6);rock.transform.localScale=new Vector3(2,2,2);Physics.SyncTransforms();
        RiverRebuildQueue.Request(creekSpring);yield return StartCoroutine(WaitForWaterWork(1));
        var rockMesh=creek.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        Check(!rockMesh.vertices.Any(v=>Mathf.Abs(v.x)<.8f&&Mathf.Abs(v.z-6)<.8f),"water mesh excludes the interior of a solid rock collider");
        Destroy(rock);ZNetScene.instance.Destroy(creek);yield return StartCoroutine(WaitForWaterWork(1));

        // Stream a batch, then repeat invalidations before a frame: each affected
        // section should build once, without the former all-rivers notification loop.
        var batch=new List<GameObject>();int before=RiverRebuildQueue.Completed;RiverRebuildQueue.MaxSliceMilliseconds=0;
        for(int i=0;i<12;i++){
            var route=new Route{Width=4,Depth=1};route.Points.Add(new Node(0,0,0));route.Points.Add(new Node(0,-1,8));
            var river=Instantiate(prefab,origin+new Vector3(0,30-i,i*8),Quaternion.identity);
            river.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());batch.Add(river);
        }
        float start=Time.realtimeSinceStartup,last=start,maxGap=0;
        do{yield return null;float now=Time.realtimeSinceStartup;maxGap=Mathf.Max(maxGap,now-last);last=now;}
        while(Time.realtimeSinceStartup-start<2||RiverRebuildQueue.IsBusy&&Time.realtimeSinceStartup-start<30);
        Check(!RiverRebuildQueue.IsBusy,"twelve-section batch finishes within the streaming test deadline");
        Logger.LogInfo("PERF twelve-section whole-frame maximum gap ms="+(maxGap*1000).ToString("0.00"));
        Logger.LogInfo("PERF twelve-section completed builds="+(RiverRebuildQueue.Completed-before));
        Check(RiverRebuildQueue.Completed-before==12,"loading twelve linked sections builds each once");
        Logger.LogInfo("PERF queued twelve-section maximum work slice ms="+RiverRebuildQueue.MaxSliceMilliseconds.ToString("0.00"));
        Check(RiverRebuildQueue.MaxSliceMilliseconds<33,"twelve-section queue avoids long synchronous mesh rebuild stalls");
        before=RiverRebuildQueue.Completed;
        for(int i=0;i<20;i++)RunicLilyPads.RiverGardens.NotifyChanged();
        yield return StartCoroutine(WaitForWaterWork(1));
        Check(RiverRebuildQueue.Completed==before,"river notifications do not trigger duplicate global rebuilds");
        foreach(var river in batch)ZNetScene.instance.Destroy(river);
        yield return StartCoroutine(WaitForWaterWork(1));

        // Place an explicit outlet at a loaded coastal tile and inspect its mesh.
        Vector3 coast=origin;bool found=false;
        foreach(var map in Heightmap.GetAllHeightmaps().OrderBy(m=>(m.transform.position-origin).sqrMagnitude)){
            if(map.IsDistantLod)continue;
            for(int x=-24;x<=24&&!found;x+=8)for(int z=-24;z<=24&&!found;z+=8){
                var p=map.transform.position+new Vector3(x,0,z);
                if(map.GetWorldHeight(p,out float ground)&&ground<OceanJoin.Sea-1){coast=p;found=true;}
            }if(found)break;
        }
        Check(found,"loaded native ocean for river outlet regression");
        if(found){
            coast.y=OceanJoin.Sea+2;
            var route=new Route{Width=4,Depth=1,Mouth=true};route.Points.Add(new Node(0,0,0));route.Points.Add(new Node(0,-2,4));route.Points.Add(new Node(0,-2,8));
            var river=Instantiate(prefab,coast,Quaternion.identity);river.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());
            yield return StartCoroutine(WaitForWaterWork(2));
            var surface=river.transform.Find("waterworks_river/river_surface");var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
            Logger.LogInfo("NATIVE OUTLET vertices="+mesh.vertexCount+" bounds="+mesh.bounds);
            Check(mesh.vertexCount>0&&mesh.vertices.All(v=>v.z<=4.001f),"custom river mesh ends at ocean instead of extending a flat overlapping sheet");
            var seaPoint=coast+new Vector3(0,-2,4);float expected=OceanJoin.Level(seaPoint,OceanJoin.Sea);
            Physics.SyncTransforms();float actual=Floating.GetLiquidLevel(seaPoint+Vector3.down*.5f,1,LiquidType.Water);
            Check(Mathf.Abs(expected-actual)<.05f,"outlet buoyancy follows native wave height");
            yield return StartCoroutine(CaptureStable(river.GetComponent<Spring>(),new Vector3(8,7,-9),coast+new Vector3(0,-1,3),"native-ocean-join.png"));
            surface.gameObject.SetActive(false);
            Capture(river.GetComponent<Spring>(),new Vector3(8,7,-9),coast+new Vector3(0,-1,3));
            File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../native-ocean-baseline.png"),true);
            ZNetScene.instance.Destroy(river);yield return StartCoroutine(WaitForWaterWork(1));
        }
    }
    IEnumerator CaptureStable(Spring spring,Vector3 offset,Vector3 focus,string filename){
        var trees=UnityEngine.Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeSelf).Select(t=>t.gameObject).ToArray();
        foreach(var tree in trees)tree.SetActive(false);
        var player=Player.m_localPlayer;var body=player.GetComponent<Rigidbody>();var position=body.position;bool kinematic=body.isKinematic;
        var gameCamera=GameCamera.instance;bool enabled=gameCamera.enabled;var camera=Camera.main;
        var cameraPosition=camera.transform.position;var rotation=camera.transform.rotation;
        try{
            body.isKinematic=true;body.position=focus+new Vector3(-6,4,0);player.transform.position=body.position;
            gameCamera.enabled=false;camera.transform.position=focus+offset;camera.transform.LookAt(focus);
            yield return new WaitForSecondsRealtime(3);
            ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath,"../screen-"+filename));
            yield return new WaitForSecondsRealtime(.5f);
            Capture(spring,offset,focus);
            File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../"+filename),true);
            var savedTarget=camera.targetTexture;var rt=new RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;
            try{
                for(int frame=0;frame<12;frame++){camera.Render();yield return null;}
                var active=RenderTexture.active;RenderTexture.active=rt;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try{pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath,"../steady-"+filename),pixels.EncodeToPNG());}
                finally{RenderTexture.active=active;Destroy(pixels);}
            }finally{camera.targetTexture=savedTarget;rt.Release();Destroy(rt);}
        }finally{body.position=position;player.transform.position=position;body.isKinematic=kinematic;camera.transform.SetPositionAndRotation(cameraPosition,rotation);gameCamera.enabled=enabled;foreach(var tree in trees)if(tree)tree.SetActive(true);}
    }
}
