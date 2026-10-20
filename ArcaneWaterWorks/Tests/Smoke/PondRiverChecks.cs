using System;
using System.Collections;
using System.IO;
using ArcaneWaterWorks;
using RunicLilyPads;
using Jotunn.Managers;
using UnityEngine;
using BepInEx;

public sealed partial class Smoke
{
    IEnumerator PondRiverTest(GameObject prefab,Vector3 origin){
        var route=new Route{Width=6,Depth=2};route.Points.Add(new Node(0,0,0));route.Points.Add(new Node(0,-1,6));route.Points.Add(new Node(0,-2,12));
        var river=Instantiate(prefab,origin,Quaternion.identity);river.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",route.Encode());
        var pond=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),origin+new Vector3(4,.15f,6),Quaternion.identity);
        pond.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_health,-1f);
        yield return StartCoroutine(WaitForWaterWork(3));
        Check(pond,"InfinityHammer invulnerable pond survives cleanup while joined to a river");
        if(!pond){ZNetScene.instance.Destroy(river);yield break;}
        var spring=river.GetComponent<Spring>();var garden=pond.GetComponent<GardenPiece>();
        Check(spring.Route!=null,"valid test river loaded");if(spring.Route==null){ZNetScene.instance.Destroy(pond);ZNetScene.instance.Destroy(river);yield break;}
        var point=origin+new Vector3(2,0,6);
        spring.Surface(2,6,out _,out float riverHeight,out _);
        float pondHeight=RunicLilyPads.GardenIntegration.WaterLevel(point);
        Check(Mathf.Abs(origin.y+riverHeight-pondHeight)<.001f,"pond and river share overlap water height");
        spring.RawSurface(2,6,out _,out float raw,out _);
        Check(Mathf.Abs(pondHeight-RunicLilyPads.GardenIntegration.WaterHeight(garden))<.001f,"river adapts to pond without deforming original pond height");
        var pondSurface=pond.transform.Find("garden_generated/pond_water");
        var riverSurface=river.transform.Find("waterworks_river/river_surface");
        Check(pondSurface.GetComponent<MeshRenderer>().sharedMaterial.shader==RiverGardens.OutdoorWaterSource().shader,"connected pond uses native outdoor animated water shader");
        var approach=river.transform.Find("waterworks_river/pond_approach_water");
        Check(!approach,"matching native river and pond materials need no overlapping approach sheet");
        int Count(Transform surface,float x,float z,bool inclusive){
            var mesh=surface.GetComponent<MeshFilter>().sharedMesh;var vs=mesh.vertices;var ts=mesh.triangles;int count=0;
            for(int i=0;i<ts.Length;i+=3){var a=surface.TransformPoint(vs[ts[i]]);var b=surface.TransformPoint(vs[ts[i+1]]);var c=surface.TransformPoint(vs[ts[i+2]]);
                float Cross(Vector3 u,Vector3 v)=>(v.x-u.x)*(z-u.z)-(v.z-u.z)*(x-u.x);
                float ca=Cross(a,b),cb=Cross(b,c),cc=Cross(c,a),e=inclusive?-.00001f:.00001f;
                if(ca>e&&cb>e&&cc>e||ca< -e&&cb< -e&&cc< -e)count++;
            }return count;
        }
        int overlaps=0,holes=0;
        for(float x=.137f;x<4.3f;x+=.31f)for(float z=4.193f;z<7.9f;z+=.29f){
            var p=origin+new Vector3(x,0,z);
            if(RunicLilyPads.GardenIntegration.Distance(garden,p)>.95f)continue;
            int count=Count(pondSurface,p.x,p.z,false)+Count(riverSurface,p.x,p.z,false);if(count>1)overlaps++;
            if(Count(pondSurface,p.x,p.z,true)+Count(riverSurface,p.x,p.z,true)==0)holes++;
        }
        Check(overlaps==0&&holes==0,"pond-river join has one surface without overlapping or missing triangles");
        Physics.SyncTransforms();float liquid=Floating.GetLiquidLevel(new Vector3(point.x,pondHeight-.1f,point.z),1,LiquidType.Water);
        Check(Mathf.Abs(liquid-pondHeight)<.05f,"joined pond liquid query matches visible water model");
        Check(RiverTerrain.SampleCurrent(point,out float ground)&&ground<pondHeight-.05f,"joined terrain leaves an open submerged connection");
        yield return StartCoroutine(CaptureStable(spring,new Vector3(12,12,17),origin+new Vector3(2,-1,6),"pond-river-preview.png"));
        var quiet=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),origin+new Vector3(15,.15f,6),Quaternion.identity);
        yield return StartCoroutine(WaitForWaterWork(2));
        var quietSurface=quiet.transform.Find("garden_generated/pond_water");
        Check(quietSurface.GetComponent<MeshRenderer>().sharedMaterial.shader==RiverGardens.OutdoorWaterSource().shader,"unconnected pond uses the native outdoor animated water shader");
        Check(quietSurface.GetComponent<MeshFilter>().sharedMesh.vertexCount>2000,"unconnected pond retains fine surface mesh");
        var quietFocus=quiet.transform.position+Vector3.down*.15f;
        var animated=quietSurface.GetComponent<MeshRenderer>().sharedMaterial;float waterTime=animated.GetFloat("_WaterTime");
        yield return StartCoroutine(CaptureStable(spring,new Vector3(0,8,-3),quietFocus,"pond-motion-a.png"));
        yield return new WaitForSecondsRealtime(1.3f);
        yield return StartCoroutine(CaptureStable(spring,new Vector3(0,8,-3),quietFocus,"pond-motion-b.png"));
        Check(animated.GetFloat("_WaterTime")>waterTime+.5f&&animated.GetFloat("_WaveVel")>0,"native pond water animation advances over time");
        for(int frame=0;frame<10;frame++){
            Capture(spring,new Vector3(0,8,-3),quietFocus);
            File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../pond-motion-"+frame.ToString("D2")+".png"),true);
            yield return new WaitForSecondsRealtime(.2f);
        }
        ZNetScene.instance.Destroy(quiet);
        ZNetScene.instance.Destroy(pond);yield return StartCoroutine(WaitForWaterWork(2));
        spring.Surface(2,6,out _,out float restored,out _);
        Check(Mathf.Abs(restored-raw)<.001f,"removing pond restores river water height");
        ZNetScene.instance.Destroy(river);yield return StartCoroutine(WaitForWaterWork(1));
    }
}
