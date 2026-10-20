using System;
using System.Collections;
using System.Linq;
using ArcaneWaterWorks;
using ArcaneWaterWorks.Pools;
using Jotunn.Managers;
using UnityEngine;
public sealed partial class StandaloneSmoke {
    IEnumerator PoolRockTest(Vector3 origin){
        var clearing=origin;
        bool found=false;
        for(int r=128;r<6000&&!found;r+=96)for(int i=0;i<48&&!found;i++){
            float a=i*Mathf.PI*2/48;var p=origin+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r;
            if(WorldGenerator.instance.GetBiome(p.x,p.z)!=Heightmap.Biome.Mountain)continue;
            p.y=WorldGenerator.instance.GetHeight(p.x,p.z);
            if(p.y>110){origin=p;found=true;}
        }
        Check(found,"mountain fixture located");if(!found)yield break;
        var player=Player.m_localPlayer;player.transform.position=origin+Vector3.up*3;player.GetComponent<Rigidbody>().position=player.transform.position;
        yield return new WaitForSecondsRealtime(12);
        if(RiverTerrain.SampleBase(origin,out float ground))origin.y=ground;
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.3f;EnvMan.instance.m_debugEnv="Clear";
        var rockPrefab=PrefabManager.Instance.GetPrefab("rock1_mountain")??PrefabManager.Instance.GetPrefab("rock2_mountain");
        Check(rockPrefab,"native rock fixture available");if(!rockPrefab)yield break;
        var rock=Instantiate(rockPrefab,origin+new Vector3(2,-1,0),Quaternion.Euler(10,25,15));
        rock.transform.localScale*=.15f; Physics.SyncTransforms();
        var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_round"),origin,Quaternion.identity);
        yield return Settle();yield return new WaitForSecondsRealtime(2);
        var hot=go.GetComponent<HotSpring>();var surface=go.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="pond_water");
        var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
        foreach(var ps in go.GetComponentsInChildren<ParticleSystem>())ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        player.transform.position=origin+new Vector3(-12,4,0);player.GetComponent<Rigidbody>().position=player.transform.position;
        Logger.LogInfo("POOL ROCK shader="+surface.sharedMaterial.shader.name+" vertices="+mesh.vertexCount+" tangents="+mesh.tangents.Length+" depth="+GameCamera.instance.GetComponent<Camera>().depthTextureMode);
        string phase=Environment.GetEnvironmentVariable("AW_POOL_BASELINE")=="1"?"before":"after";
        CaptureWake("pool-rock-"+phase+".png",origin+new Vector3(-6,2,-7),origin+new Vector3(0,-.1f,0));
        CaptureWake("pool-rock-overhead-"+phase+".png",origin+new Vector3(-1,9,-4),origin);
        Logger.LogInfo("POOL invalid tangents="+mesh.tangents.Count(t=>new Vector3(t.x,t.y,t.z).sqrMagnitude<.5f)+" vertices="+mesh.vertexCount);
        if(phase=="before"){
            surface.enabled=false;
            CaptureWake("pool-rock-no-water.png",origin+new Vector3(-6,2,-7),origin);
            surface.enabled=true;mesh.RecalculateTangents();
            CaptureWake("pool-rock-tangents.png",origin+new Vector3(-6,2,-7),origin);
            Logger.LogInfo("POOL repaired invalid tangents="+mesh.tangents.Count(t=>new Vector3(t.x,t.y,t.z).sqrMagnitude<.5f));
        }
        Check(hot.Live&&hot.Seats.Length>0,"pool remains active with sitting stones");
        if(phase=="after"){
            Check(surface.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow"&&surface.sharedMaterial.shader.isSupported,"hot spring uses supported depth-clipped surface");
            Check(mesh.tangents.All(t=>new Vector3(t.x,t.y,t.z).sqrMagnitude>.5f),"extended shoreline has valid tangents");
            Check(mesh.colors.All(c=>c.b==0)&&mesh.uv4.All(c=>c.y==4),"calm pool has no displacement or river mouth fade");
            var saved=hot.View.GetZDO().GetString("arcane_garden_settings","");
            hot.Pond.RefreshSurface();yield return null;
            Check(saved==hot.View.GetZDO().GetString("arcane_garden_settings","")&&Mathf.Abs(go.GetComponentInChildren<WaterVolume>().GetWaterSurface(origin)-hot.Water)<.001f,"surface rebuild preserves saved settings and liquid height");
            var neighbor=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_oval"),origin+new Vector3(-4,0,0),Quaternion.identity);
            yield return Settle();
            Check(go.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name=="pond_water"&&r.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow")&&neighbor.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name=="pond_water"&&r.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow"),"joined pool rebuild keeps both corrected materials");
            CaptureWake("pool-connected-after.png",origin+new Vector3(-6,8,-7),origin+Vector3.left*2);
            ZNetScene.instance.Destroy(neighbor);
        }
        ZNetScene.instance.Destroy(go);ZNetScene.instance.Destroy(rock);yield return Settle();
        if(phase=="after"){
            origin=clearing;player.transform.position=origin+new Vector3(-8,3,0);player.GetComponent<Rigidbody>().position=player.transform.position;
            yield return new WaitForSecondsRealtime(10);
        }
        if(phase=="after")for(int shape=0;shape<4;shape++){
            var open=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_"+HotSpringPrefabs.Shapes[shape]),origin,Quaternion.identity);
            yield return Settle();
            var pool=open.GetComponent<HotSpring>();var water=open.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="pond_water");var m=water.GetComponent<MeshFilter>().sharedMesh;
            Check(water.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow"&&m.uv2.Length==m.vertexCount&&m.uv3.Length==m.vertexCount&&m.uv4.Length==m.vertexCount&&m.colors.Length==m.vertexCount&&pool.Seats.Length>0,"open shape has complete surface data and seats: "+shape);
            CaptureWake("pool-shape-"+shape+"-after.png",origin+new Vector3(-3,7,-5),origin);
            ZNetScene.instance.Destroy(open);yield return Settle();
        }
        if(phase=="after"){
            var ghost=new GameObject("pool preview fixture");ghost.SetActive(false);ghost.transform.position=origin;
            var piece=ghost.AddComponent<GardenPiece>();piece.Shape=0;piece.Settings.Width=6;piece.Settings.Depth=1.4f;ghost.AddComponent<HotSpring>();
            PondBuilder.Build(piece,ghost.transform,false);
            var r=ghost.GetComponentsInChildren<MeshRenderer>(true).First(x=>x.name=="pond_water");
            Check(r.sharedMaterial.shader.name=="ArcaneWaters/RiverFlow"&&ghost.GetComponentsInChildren<WaterVolume>(true).Length==0,"preview uses corrected material without creating liquid");
            Destroy(ghost);
        }
    }
}


