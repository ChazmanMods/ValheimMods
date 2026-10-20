using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcaneWaterWorks;
using ArcaneWaterWorks.Pools;
using BepInEx;
using BepInEx.Bootstrap;
using Jotunn.Managers;
using UnityEngine;

[BepInPlugin("chazman.ArcaneWaterWorks.StandaloneSmoke","ArcaneNaturalWaters standalone checks","0.8.0")]
[BepInDependency(Plugin.Guid)]
public sealed partial class StandaloneSmoke : BaseUnityPlugin
{
    bool ready;
    int failures, checks;
    void Awake(){
        Utils.SetSaveDataPath(Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../saves")));
        PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;
        StartCoroutine(Run());StartCoroutine(Watchdog());
    }
    void Check(bool pass,string label){checks++;if(pass)Logger.LogInfo("STANDALONE PASS "+label);else{failures++;Logger.LogError("STANDALONE FAIL "+label);}}
    IEnumerator Watchdog(){yield return new WaitForSecondsRealtime(240);Logger.LogError("STANDALONE FAIL timeout");Application.Quit(2);}
    IEnumerator Settle(){yield return new WaitForSecondsRealtime(2);float until=Time.realtimeSinceStartup+20;while(RiverRebuildQueue.IsBusy&&Time.realtimeSinceStartup<until)yield return null;Check(!RiverRebuildQueue.IsBusy,"rebuild queue completes");}
    IEnumerator Run(){
        float until=Time.realtimeSinceStartup+90;while(!ready&&Time.realtimeSinceStartup<until)yield return null;
        yield return null;
        Check(!Chainloader.PluginInfos.ContainsKey("chazman.RunicLilyPads"),"WaterGardens is not installed");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="ArcaneDecorWaterGardens"),"WaterGardens assembly is not loaded");
        var springPrefab=PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring");
        Check(springPrefab,"river prefab registered");
        var ambiencePrefab=PrefabManager.Instance.GetPrefab("piece_arcanewaters_river_ambience");
        Check(ambiencePrefab,"independent ambience stone registered");
        Check(!PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),"no WaterGardens prefabs needed or registered");
        foreach(string shape in HotSpringPrefabs.Shapes)Check(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_"+shape),"hot spring registered: "+shape);
        Check(!PrefabManager.Instance.GetPrefab("piece_arcanewaters_pipe"),"retired plumbing absent from natural mod");
        Check(!PrefabManager.Instance.GetPrefab("piece_flowcraft_pipe"),"FlowCraft absent in natural-only fixture");
        if(failures>0){Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_WAKE_PROBE")=="1"){
            foreach(string key in new[]{"Karve","VikingShip","vfx_watersplash_bathtub"}){
                var prefab=PrefabManager.Instance.GetPrefab(key);if(!prefab)continue;
                foreach(var ps in prefab.GetComponentsInChildren<ParticleSystem>(true)){
                    string path=ps.name;var parent=ps.transform.parent;while(parent&&parent!=prefab.transform){path=parent.name+"/"+path;parent=parent.parent;}
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();var mat=renderer.sharedMaterial;
                    Logger.LogInfo("WAKE "+key+"/"+path+" mode="+renderer.renderMode+" mesh="+renderer.mesh+" mat="+mat+" shader="+(mat?mat.shader.name:"")+" tex="+(mat?mat.mainTexture:null)+" size="+ps.main.startSize+" life="+ps.main.startLifetime+" rate="+ps.emission.rateOverTime+" shape="+ps.shape.shapeType+" space="+ps.main.simulationSpace);
                    if(mat)foreach(var name in mat.GetTexturePropertyNames())Logger.LogInfo("WAKE TEX "+name+"="+mat.GetTexture(name));
                }
            }
            Finish();yield break;
        }
        if(Environment.GetEnvironmentVariable("AW_ICONS_ONLY")=="1"){
            foreach(string mode in new[]{"start","trench","deepen","shallow","smooth","unsmooth","undo"}){
                var icon=PrefabManager.Instance.GetPrefab("piece_arcanewaters_"+mode).GetComponent<Piece>().m_icon;
                Check(icon&&icon.name=="hoe-"+mode&&icon.texture.name=="native hoe "+mode,"native Valheim hoe icon "+mode);
                File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath,"../hoe-"+mode+".png"),icon.texture.EncodeToPNG());
            }
            Finish();yield break;
        }
        var seed=World.GetCreateWorld("ArcaneStandaloneSeed",FileHelpers.FileSource.Local).m_seedName;
        var world=new World("ArcaneStandalone_"+DateTime.UtcNow.Ticks,seed){m_fileSource=FileHelpers.FileSource.Local};
        world.SaveWorldFWLData(DateTime.Now);
        Game.SetProfile("arcane_standalone_test",FileHelpers.FileSource.Local);
        ZNet.SetServer(true,false,false,"ArcaneStandalone","",world);ZNet.ResetServerHost();
        typeof(FejdStartup).GetMethod("LoadMainScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
        until=Time.realtimeSinceStartup+110;
        while((!Game.instance||!ZNetScene.instance||!Player.m_localPlayer)&&Time.realtimeSinceStartup<until)yield return null;
        Check(Player.m_localPlayer,"isolated world started");if(!Player.m_localPlayer){Finish();yield break;}
        var player=Player.m_localPlayer;player.SetGodMode(true);
        foreach(var bird in UnityEngine.Object.FindObjectsOfType<Valkyrie>())bird.DropPlayer(true);
        player.SetIntro(false);yield return Settle();
        var origin=player.transform.position;
        foreach(var map in Heightmap.GetAllHeightmaps()){
            var p=map.transform.position+new Vector3(10,0,10);
            if(!map.IsDistantLod&&RiverTerrain.SampleBase(p,out float ground)&&ground>ZoneSystem.instance.m_waterLevel+3){origin=p;origin.y=ground;break;}
        }
        player.transform.position=origin+new Vector3(-3,2,0);player.GetComponent<Rigidbody>().position=player.transform.position;
        if(Environment.GetEnvironmentVariable("AW_POOL_ROCK")=="1"){yield return PoolRockTest(origin);Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_BOAT_BANK")=="1"){yield return BoatBankTest(origin);Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_SHORE_VISUAL")=="1"){yield return ShoreVisualTest(origin);Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_STEP_VISUAL")=="1"){yield return StairVisualTest(origin);yield return Settle();yield return TrenchTerrainTest(origin+new Vector3(24,0,0));Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_WAKE_VISUAL")=="1"){yield return WakeVisualTest(origin);yield return Settle();yield return TrenchTerrainTest(origin+new Vector3(24,0,0));Finish();yield break;}
        if(Environment.GetEnvironmentVariable("AW_TERRAIN_ONLY")=="1"){yield return TrenchTerrainTest(origin);Finish();yield break;}
        for(int shape=0;shape<4;shape++){
            var go=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanewaters_hotspring_"+HotSpringPrefabs.Shapes[shape]),origin,Quaternion.identity);
            yield return Settle();var hot=go.GetComponent<HotSpring>();
            Check(hot.Live&&hot.Pond.Shape==shape,"live standalone pool "+shape);
            Check(hot.Garden.Depth==1.4f&&hot.Garden.Track==0,"hot spring defaults "+shape);
            Check(hot.Seats.Length==HotSpringSettings.SeatCount(6,shape),"sitting stone count "+shape);
            Check(hot.Seats.All(s=>s.m_attachPoint&&s.transform.Find("rock_001")),"embedded sitting stone models "+shape);
            var volume=go.GetComponentInChildren<WaterVolume>();
            Check(volume&&go.GetComponentsInChildren<WaterVolume>().Length==1,"single liquid volume "+shape);
            Check(volume&&Math.Abs(volume.GetWaterSurface(origin)-hot.Water)<.03f,"water height "+shape);
            if(shape==0){
                var garden=hot.Garden.Copy();garden.Width=12;garden.Track=2;
                var settings=hot.Settings.Copy();settings.Sound=4;settings.Music=2;settings.Warded=true;
                Check(hot.Save(garden,settings),"pool save accepted");yield return Settle();
                Check(hot.Garden.Width==12&&hot.Settings.Sound==4&&hot.Settings.Music==2&&hot.Settings.Warded,"saved pool state applied");
                Check(hot.Seats.Length==12,"seats resize independently");
                Check(GardenSettings.TryDecode(hot.View.GetZDO().GetString("arcane_garden_settings",""),out var restored)&&restored.Width==12,"legacy pool settings key preserved");
                Check(HotSpringSettings.TryDecode(hot.View.GetZDO().GetString("arcane_pool_controller",""),out var saved)&&saved.Warded,"legacy controller key preserved");
                HotSpringEditor.Show(hot);yield return null;
                Check(GardenMenuCursor.IsOpen&&GameObject.Find("ArcaneNaturalWaters pool menu"),"self-contained pool menu opens");HotSpringEditor.Close();yield return null;
                Check(!GardenMenuCursor.IsOpen,"pool menu cursor released");
            }
            ZNetScene.instance.Destroy(go);yield return Settle();
            Check(HotSpring.All.Count==0&&GardenPiece.Ponds.Count==0,"pool registry cleans up "+shape);
        }
        for(int i=1;i<=7;i++)Check(GardenAudio.Get(i),"embedded ambience recording "+i);
        Check(GardenAudio.Get(9)&&GardenAudio.Get(10),"embedded cascade recordings");
        var river=Instantiate(springPrefab,origin,Quaternion.identity);yield return Settle();
        var spring=river.GetComponent<Spring>();Check(spring.Live&&spring.Route!=null&&spring.Route.Valid,"standalone river generates");
        if(spring.Route!=null){
            Check(Route.TryDecode(spring.View.GetZDO().GetString("aww_route_v1",""),out _),"legacy river route key preserved");
            RiverEditor.Show(spring);yield return null;Check(GardenMenuCursor.IsOpen,"river menu opens");RiverEditor.Close();
            var stone=Instantiate(ambiencePrefab,origin+Vector3.right*2,Quaternion.identity);yield return Settle();
            var garden=stone.GetComponent<GardenPiece>();Check(garden.Live&&garden.Shape==-2,"independent river ambience controller");
            GardenEditor.Open(garden);yield return null;Check(GardenMenuCursor.IsOpen,"ambience menu opens");GardenEditor.Close();
            ZNetScene.instance.Destroy(stone);yield return null;
        }
        ZNetScene.instance.Destroy(river);yield return Settle();
        Check(RiverLiquid.Registry.Count==0,"all liquid registrations cleaned up");
        Finish();
    }
    void Finish(){Logger.LogInfo(failures==0?"STANDALONE COMPLETE PASS "+checks:"STANDALONE COMPLETE FAIL "+failures+" / "+checks);Application.Quit(failures==0?0:2);}
}
