using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcaneDecorFlowCraft;
using ArcaneDecorFlowCraft.Plumbing;
using BepInEx;
using BepInEx.Bootstrap;
using Jotunn.Managers;
using UnityEngine;

[BepInPlugin("chazman.FlowCraft.Smoke","FlowCraft standalone checks","0.1.0")]
[BepInDependency(Plugin.Guid)]
public sealed partial class StandaloneSmoke:BaseUnityPlugin
{
    bool ready;int checks,failures;
    bool Combined=>Environment.GetEnvironmentVariable("FC_COMBINED")=="1";
    void Awake(){
        Utils.SetSaveDataPath(Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../saves")));
        PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;
        StartCoroutine(Run());StartCoroutine(Watchdog());
    }
    void Check(bool pass,string label){checks++;if(pass)Logger.LogInfo("STANDALONE PASS "+label);else{failures++;Logger.LogError("STANDALONE FAIL "+label);}}
    IEnumerator Watchdog(){yield return new WaitForSecondsRealtime(340);Logger.LogError("STANDALONE FAIL timeout");Application.Quit(2);}
    IEnumerator Settle(){yield return new WaitForSecondsRealtime(2);}
    void Finish(){Logger.LogInfo(failures==0?"STANDALONE COMPLETE PASS "+checks:"STANDALONE COMPLETE FAIL "+failures+" / "+checks);Application.Quit(failures==0?0:2);}
    GameObject CreateWater(Vector3 origin){
        if(Combined)return Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_round"),origin,Quaternion.identity);
        var go=new GameObject("isolated native water volume");go.SetActive(false);go.layer=LayerMask.NameToLayer("WaterVolume");go.transform.position=origin;
        var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.center=Vector3.down;box.size=new Vector3(5,4,5);
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=Plugin.Water;
        var volume=go.AddComponent<WaterVolume>();volume.m_waterSurface=renderer;volume.m_forceDepth=0;volume.m_useGlobalWind=false;volume.m_surfaceOffset=0;
        go.SetActive(true);Physics.SyncTransforms();return go;
    }
    IEnumerator Run(){
        float until=Time.realtimeSinceStartup+90;while(!ready&&Time.realtimeSinceStartup<until)yield return null;yield return null;
        Check(Chainloader.PluginInfos.ContainsKey("chazman.ArcaneNaturalWaters")==Combined,"natural mod installed only in combined fixture");
        Check(!Chainloader.PluginInfos.ContainsKey("chazman.RunicLilyPads"),"WaterGardens absent");
        Check(!PrefabManager.Instance.GetPrefab("piece_arcanewaters_pipe"),"old plumbing prefabs absent");
        Check((bool)PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring")==Combined,"river registration belongs only to natural mod");
        foreach(var key in PipePrefabs.Keys)Check(PrefabManager.Instance.GetPrefab("piece_flowcraft_"+key),"FlowCraft owns piece "+key);
        if(failures>0){Finish();yield break;}
        var seed=World.GetCreateWorld("FlowCraftSeed",FileHelpers.FileSource.Local).m_seedName;
        var world=new World("FlowCraft_"+DateTime.UtcNow.Ticks,seed){m_fileSource=FileHelpers.FileSource.Local};world.SaveWorldFWLData(DateTime.Now);
        Game.SetProfile("flowcraft_test",FileHelpers.FileSource.Local);ZNet.SetServer(true,false,false,"FlowCraft","",world);ZNet.ResetServerHost();
        typeof(FejdStartup).GetMethod("LoadMainScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
        until=Time.realtimeSinceStartup+110;while((!Game.instance||!ZNetScene.instance||!Player.m_localPlayer)&&Time.realtimeSinceStartup<until)yield return null;
        Check(Player.m_localPlayer,"isolated world started");if(!Player.m_localPlayer){Finish();yield break;}
        var player=Player.m_localPlayer;player.SetGodMode(true);foreach(var bird in UnityEngine.Object.FindObjectsOfType<Valkyrie>())bird.DropPlayer(true);player.SetIntro(false);yield return Settle();
        var origin=player.transform.position;
        foreach(var map in Heightmap.GetAllHeightmaps()){
            var p=map.transform.position+new Vector3(10,0,10);
            if(!map.IsDistantLod&&PipeEnvironment.SampleGround(p,out float ground)&&ground>ZoneSystem.instance.m_waterLevel+3){origin=p;origin.y=ground;break;}
        }
        if(Environment.GetEnvironmentVariable("FC_FOUNTAIN_ONLY")=="1"){yield return FountainTest(origin);Finish();yield break;}
        if(Environment.GetEnvironmentVariable("FC_RECONNECT_ONLY")=="1"){yield return ReconnectTest(origin);Finish();yield break;}
        if(Environment.GetEnvironmentVariable("FC_VISUAL_ONLY")!="1"){yield return PlumbingTest(origin);yield return ExpansionTest(origin);}
        yield return ReportedRegressionTest(origin);yield return PowerGlowTest(origin);yield return CompleteSetTest(origin);yield return ReconnectTest(origin);yield return FountainTest(origin);Finish();
    }
}
