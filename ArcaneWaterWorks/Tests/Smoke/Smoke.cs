using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using ArcaneWaterWorks;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;

[BepInPlugin("chazman.ArcaneWaterWorks.Smoke","ArcaneWaterWorks isolated tests","0.1.0")]
[BepInDependency(Plugin.Guid)]
public sealed partial class Smoke:BaseUnityPlugin
{
    bool ready;
    int failures;
    void Awake(){
        string root=Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../saves"));Directory.CreateDirectory(root);Utils.SetSaveDataPath(root);
        PrefabManager.OnVanillaPrefabsAvailable+=()=>ready=true;StartCoroutine(Run());StartCoroutine(Watchdog());
    }
    IEnumerator Watchdog(){yield return new WaitForSecondsRealtime(300);Logger.LogError("SMOKE FAIL watchdog timeout");Application.Quit(2);}
    void Check(bool value,string name){if(value)Logger.LogInfo("SMOKE PASS "+name);else{failures++;Logger.LogError("SMOKE FAIL "+name);}}
    IEnumerator WaitForWaterWork(float minimum){
        yield return new WaitForSecondsRealtime(minimum);float until=Time.realtimeSinceStartup+30;
        while(Time.realtimeSinceStartup<until&&(RiverRebuildQueue.IsBusy||Spring.Active.Any(s=>s&&s.Busy)))yield return null;
        Check(!RiverRebuildQueue.IsBusy&&!Spring.Active.Any(s=>s&&s.Busy),"queued water work completes");
    }
    IEnumerator Run(){
        float until=Time.realtimeSinceStartup+90;while(!ready&&Time.realtimeSinceStartup<until)yield return null;
        yield return StartCoroutine(WaitForWaterWork(2));
        var prefab=PrefabManager.Instance.GetPrefab("piece_arcanewaterworks_spring");
        Check(prefab,"spring registration");if(!prefab){Application.Quit(2);yield break;}
        var piece=prefab.GetComponent<Piece>();
        Check(piece.m_resources.Sum(r=>r.m_amount)==26&&piece.m_resources.Length==3,"recipe: 20 stone + 5 ancient bark + 1 surtling core");
        Check(!piece.m_craftingStation,"spring has no inherited workbench requirement");
        Check(piece.m_icon&&!prefab.GetComponent<ZNetView>().m_distant,"icon and streamed river sections");
        var probeMaterial=new Material(Plugin.Water);bool shaderReady=RiverSurface.Apply(probeMaterial);
        Check(shaderReady,"native water material is available on installed Valheim runtime");Destroy(probeMaterial);
        if(!shaderReady){Application.Quit(2);yield break;}
        // WaterVolume.StaticUpdate requires a live ZNet. Never create test volumes in the menu.
        yield return StartCoroutine(WorldTest(prefab));
        if(Environment.GetEnvironmentVariable("ARCANE_NATIVE_ONLY")=="1"||Environment.GetEnvironmentVariable("ARCANE_JUNCTION_ONLY")=="1"||Environment.GetEnvironmentVariable("ARCANE_POND_ONLY")=="1"){
            Logger.LogInfo(failures==0?"SMOKE COMPLETE PASS":"SMOKE COMPLETE FAIL");Application.Quit(failures==0?0:2);yield break;
        }
        yield return StartCoroutine(HotSpringsTest());
        var root=new GameObject("isolated_river_geometry");root.transform.position=new Vector3(4000,1000,4000);
        var spring=root.AddComponent<Spring>();var route=new Route();route.Points.Add(new Node(0,.12f,0));route.Points.Add(new Node(0,-1,4));route.Points.Add(new Node(2,-5,8));spring.Route=route;
        try{RiverVisuals.Rebuild(spring);Physics.SyncTransforms();}catch(Exception e){failures++;Logger.LogError("SMOKE FAIL geometry "+e);}
        yield return null;
        try{
            var volumes=root.GetComponentsInChildren<WaterVolume>();Check(volumes.Length==1&&volumes[0].GetComponent<BoxCollider>(),"one box water volume per river");
            float expected=999.56f;float liquid=Floating.GetLiquidLevel(root.transform.position+new Vector3(0,-.7f,2),1,LiquidType.Water);
            Check(Math.Abs(liquid-expected)<.03,"actual sloped liquid height "+liquid);
            float outside=Floating.GetLiquidLevel(root.transform.position+new Vector3(20,-.7f,2),1,LiquidType.Water);
            Check(outside< -1000,"outside river has no liquid");
            var surface=root.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
            Check(surface.normals.All(n=>n.y>0),"river triangle winding faces upwards");
            Check(root.GetComponentsInChildren<MeshCollider>().Length==0,"no convex river mesh colliders");
            float dry=Floating.GetLiquidLevel(root.transform.position+new Vector3(3,-.7f,0),1,LiquidType.Water);
            Check(dry< -1000,"dry space inside broad trigger is masked");
            float deep=Floating.GetLiquidLevel(root.transform.position+new Vector3(0,-20,2),1,LiquidType.Water);
            Check(Math.Abs(deep-expected)<.03,"deep riverbed retains water surface");
            // Verify broad-trigger floater arbitration with overlapping native water.
            var natural=new GameObject("test_native_water");natural.SetActive(false);natural.layer=LayerMask.NameToLayer("WaterVolume");natural.transform.position=root.transform.position+new Vector3(0,-1,0);
            var box=natural.AddComponent<BoxCollider>();box.isTrigger=true;box.size=new Vector3(20,50,20);
            var native=natural.AddComponent<WaterVolume>();native.m_waterSurface=root.GetComponentInChildren<MeshRenderer>();native.m_useGlobalWind=false;native.m_forceDepth=.1f;natural.SetActive(true);
            var floater=new GameObject("test_floater").AddComponent<TestFloater>();floater.transform.position=root.transform.position+new Vector3(3,-2,0);
            var inside=(System.Collections.Generic.List<IWaterInteractable>)typeof(WaterVolume).GetField("m_inWater",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(volumes[0]);inside.Add(floater);
            Physics.SyncTransforms();volumes[0].UpdateFloaters();Check(Math.Abs(floater.Level-999)<.03,"dry broad-trigger area preserves native lake buoyancy");
            WaterVolume cached=volumes[0];
            Check(Math.Abs(Floating.GetWaterLevel(floater.transform.position,ref cached)-999)<.03,"cached river lookup preserves native lake water");
            cached=native;
            Check(Math.Abs(Floating.GetWaterLevel(root.transform.position+new Vector3(0,-2,2),ref cached)-expected)<.03,"cached lake lookup recognizes higher river");
            cached=volumes[0];Check(Floating.IsUnderWater(floater.transform.position,ref cached),"cached underwater query recognizes native lake");
            inside.Clear();Destroy(floater.gameObject);Destroy(natural);
        }catch(Exception e){failures++;Logger.LogError("SMOKE FAIL physics "+e);}
        spring.Route=Route.Trace((x,z)=>0,-50,0,180,4,.8f,out _);
        var timer=System.Diagnostics.Stopwatch.StartNew();RiverVisuals.Rebuild(spring);timer.Stop();
        Check(root.GetComponentsInChildren<WaterVolume>().Length==1,"180 m river still uses exactly one volume");
        Logger.LogInfo("PERF 180m visual build milliseconds="+timer.Elapsed.TotalMilliseconds.ToString("0.00"));
        Physics.SyncTransforms();timer.Restart();
        for(int i=0;i<10000;i++)Floating.GetLiquidLevel(root.transform.position+new Vector3(0,-1,80),1,LiquidType.Water);
        timer.Stop();Logger.LogInfo("PERF 10000 native liquid queries milliseconds="+timer.Elapsed.TotalMilliseconds.ToString("0.00"));
        UnityEngine.Object.Destroy(root);yield return null;
        Check(!WaterVolume.Instances.Any(v=>v&&v.name=="river_water_volume"),"removal cleans up liquid volumes");
        Check(RiverLiquid.Registry.Count==0,"removal clears custom volume lookup");
        Logger.LogInfo(failures==0?"SMOKE COMPLETE PASS":"SMOKE COMPLETE FAIL "+failures);
        Application.Quit(failures==0?0:2);
    }
    IEnumerator WorldTest(GameObject prefab){
        Logger.LogInfo("SMOKE isolated local world; no public server; saves redirected under tests/runtime/saves");
        var seed=World.GetCreateWorld("ArcaneWaterWorksSmoke",FileHelpers.FileSource.Local).m_seedName;
        var world=new World("ArcaneWaterWorksSmoke_"+DateTime.UtcNow.Ticks,seed){m_fileSource=FileHelpers.FileSource.Local};
        world.SaveWorldFWLData(DateTime.Now);
        Game.SetProfile("arcane_waterworks_smoke",FileHelpers.FileSource.Local);
        ZNet.SetServer(true,false,false,"ArcaneWaterWorksSmoke","",world);ZNet.ResetServerHost();
        typeof(FejdStartup).GetMethod("LoadMainScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(UnityEngine.Object.FindObjectOfType<FejdStartup>(),null);
        float until=Time.realtimeSinceStartup+140;
        while((!Game.instance||!ZNetScene.instance||!Player.m_localPlayer)&&Time.realtimeSinceStartup<until)yield return null;
        if(!Player.m_localPlayer){Check(false,"world initialization");yield break;}
        var player=Player.m_localPlayer;player.SetGodMode(true);
        foreach(var valkyrie in UnityEngine.Object.FindObjectsOfType<Valkyrie>())valkyrie.DropPlayer(true);
        player.SetIntro(false);
        yield return StartCoroutine(WaitForWaterWork(3));
        var position=player.transform.position+new Vector3(12,0,0);
        // Pick loaded dry land and stand beside the test placement. A new profile may
        // still be hundreds of metres above the terrain during the opening flight.
        foreach(var map in Heightmap.GetAllHeightmaps()){
            var spot=map.transform.position+new Vector3(20,0,20);
            if(!map.IsDistantLod&&RiverTerrain.SampleBase(spot,out float candidate)&&candidate>ZoneSystem.instance.m_waterLevel+3){position=spot;break;}
        }
        if(!RiverTerrain.SampleBase(position,out var ground)){Check(false,"loaded terrain for world test");yield break;}
        position.y=ground;
        // This is the disposable test world only: keep the camera clear of random trees.
        foreach(var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)){
            if(!view||!view.IsValid()||(view.transform.position-position).sqrMagnitude>100*100)continue;
            if(view.GetComponent<TreeBase>()||view.GetComponent<TreeLog>()||view.GetComponent<Pickable>()||view.GetComponent<Destructible>()&&!view.GetComponent<Piece>()&&!view.GetComponent<Character>())ZNetScene.instance.Destroy(view.gameObject);
        }
        yield return null;
        player.transform.position=position+new Vector3(-6,2,0);player.GetComponent<Rigidbody>().position=player.transform.position;
        EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.5f;
        EnvMan.instance.m_debugEnv="Clear";
        var outdoorMaterial=RunicLilyPads.RiverGardens.OutdoorWaterSource();
        Logger.LogInfo("NATIVE WATER source="+outdoorMaterial.name+" shader="+outdoorMaterial.shader.name+" terrainVolumes="+WaterVolume.Instances.Count(v=>v&&v.m_heightmap));
        foreach(var property in new[]{"_ColorTop","_ColorBottom","_ColorBottomShallow","_DepthFade","_NormalPower","_WaveVel"})
            if(outdoorMaterial.HasProperty(property))Logger.LogInfo("NATIVE WATER "+property+"="+(property.StartsWith("_Color")?outdoorMaterial.GetColor(property).ToString():outdoorMaterial.GetFloat(property).ToString()));
        var oceanVolume=WaterVolume.Instances.First(v=>v&&v.m_heightmap);var oceanMesh=oceanVolume.m_waterSurface.GetComponent<MeshFilter>().sharedMesh;
        Logger.LogInfo("NATIVE MESH vertex="+oceanMesh.vertices[0]+" uv="+oceanMesh.uv[0]+" tangent="+oceanMesh.tangents[0]+" normal="+oceanMesh.normals[0]+" colors="+oceanMesh.colors.Length+" scale="+oceanVolume.m_waterSurface.transform.lossyScale);
        if(Environment.GetEnvironmentVariable("ARCANE_POND_ONLY")=="1"){
            yield return StartCoroutine(PondRiverTest(prefab,position+new Vector3(0,20,0)));yield break;
        }
        if(Environment.GetEnvironmentVariable("ARCANE_NATIVE_ONLY")=="1"){
            yield return StartCoroutine(NativeWaterTest(prefab,position+new Vector3(0,20,0)));
            yield return StartCoroutine(PondRiverTest(prefab,position+new Vector3(0,20,0)));
            yield return StartCoroutine(BoatTest());yield break;
        }
        if(Environment.GetEnvironmentVariable("ARCANE_JUNCTION_ONLY")=="1"){
            yield return StartCoroutine(JoinedRiverTest(prefab,position+new Vector3(0,20,0)));
            yield return StartCoroutine(IndependentRiverTest(prefab,position+new Vector3(0,20,0)));yield break;
        }
        Heightmap.FindHeightmap(position).GetWorldHeight(position,out float originalVertexHeight);
        GameObject placed=UnityEngine.Object.Instantiate(prefab,position,Quaternion.identity);
        yield return StartCoroutine(WaitForWaterWork(2));
        var spring=placed.GetComponent<Spring>();
        Check(spring.Live&&spring.View.IsOwner(),"live owned spring");
        Check(spring.Route!=null&&spring.Route.Valid,"automatic source generation in world: "+spring.Status);
        if(spring.Route!=null){
            string saved=spring.View.GetZDO().GetString("aww_route_v1","");
            Check(Route.TryDecode(saved,out _),"route persisted in real ZDO");
            var h=Heightmap.FindHeightmap(position);h.GetWorldHeight(position,out float lowered);
            Check(lowered<originalVertexHeight-.2f,"terrain carved beneath source: "+originalVertexHeight+" -> "+lowered);
            RiverTerrain.SampleBase(position,out float sampled);
            Check(Math.Abs(sampled-ground)<.03,"routing reads original terrain, preventing cumulative digging");
            var route=spring.Route.Copy();route.Width=6;route.Depth=1.2f;Check(spring.Save(route),"route settings save accepted");
            yield return StartCoroutine(WaitForWaterWork(1));
            Check(spring.Route.Width==6&&spring.Route.Depth==1.2f,"route settings applied through ZDO");
            yield return StartCoroutine(FinishTest(spring));
            yield return StartCoroutine(BoatTest());
            yield return StartCoroutine(JoinedRiverTest(prefab,position+new Vector3(24,20,0)));
            yield return StartCoroutine(IndependentRiverTest(prefab,position+new Vector3(24,20,0)));
            yield return StartCoroutine(PondRiverTest(prefab,position+new Vector3(24,20,0)));
            RiverHoe.Select(spring);
            var hoe=PrefabManager.Instance.GetPrefab("Hoe").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            Check(hoe.m_pieces.Count(p=>p.name.StartsWith("piece_arcanewaters_"))==7,"seven registered native hoe tools");
            Check(player.HaveUniqueKey("arcanewaters_hoe"),"hoe tools unlock persists on player");
            // A branch saves through the same ownership/ward checks as an actual hoe stroke.
            var branched=spring.Route.Copy();var junction=branched.Points[1];
            bool branchOK=branched.AppendFrom(1,new Node(junction.X+4,junction.Y,junction.Z),spring.Ground,spring.Sea,out _);
            Check(branchOK&&spring.Save(branched),"branch save accepted");
            yield return StartCoroutine(WaitForWaterWork(1.5f));
            Check(spring.Route.Points.Count==branched.Points.Count,"branch applied through ZDO");
            yield return StartCoroutine(GardenIntegration(spring));
            RiverEditor.Show(spring);yield return new WaitForSecondsRealtime(.5f);
            var menu=GameObject.Find("WaterGardens pond menu")?.transform;
            Check(menu&&menu.gameObject.activeInHierarchy,"original WaterGardens-style ArcaneWaters panel opens");
            Check(!(bool)typeof(Player).GetMethod("TakeInput",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(player,null),"native menu blocks gameplay input");
            CaptureMenu();
            yield return new WaitForSecondsRealtime(.5f);RiverEditor.Close();
            // Reload from the same saved ZDO data without creating a second source.
            string encoded=spring.View.GetZDO().GetString("aww_route_v1","");
            Check(Route.TryDecode(encoded,out var decoded)&&decoded.Encode()==spring.Route.Encode(),"reload payload matches applied river");
            if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){
                try{Capture(spring);Check(true,"rendered river capture");}catch(Exception ex){Check(false,"river capture: "+ex.Message);}
            }
        }
        placed.GetComponent<WearNTear>().Remove();
        yield return StartCoroutine(WaitForWaterWork(2));
        Check(!placed,"normal hammer removal destroys source");
        RiverTerrain.SampleBase(position,out var restoredBase);var terrain=Heightmap.FindHeightmap(position);
        float restored=0;if(terrain)terrain.GetWorldHeight(position,out restored);
        Check(terrain&&Math.Abs(restored-originalVertexHeight)<.1f,"terrain restored after removal");
        Check(!Spring.Active.Any(s=>s&&s.Route!=null),"removed source leaves no active route");
    }
    IEnumerator JoinedRiverTest(GameObject prefab,Vector3 origin){
        var a=new Route{Width=4,Depth=1,Whitewater=0};a.Points.Add(new Node(0,0,0));a.Points.Add(new Node(0,-8,4));
        var b=new Route{Width=4,Depth=5,Whitewater=1};b.Points.Add(new Node(0,0,0));b.Points.Add(new Node(4,-8,0));
        var upper=Instantiate(prefab,origin,Quaternion.identity);var lower=Instantiate(prefab,origin+new Vector3(0,-8,4),Quaternion.identity);
        upper.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",a.Encode());lower.GetComponent<ZNetView>().GetZDO().Set("aww_route_v1",b.Encode());
        lower.GetComponent<ZNetView>().GetZDO().Set("aww_continuation",true);
        yield return StartCoroutine(WaitForWaterWork(2));
        var sa=upper.GetComponent<Spring>();var sb=lower.GetComponent<Spring>();
        Check(sa.JoinedRoute!=null&&ReferenceEquals(sa.JoinedRoute,sb.JoinedRoute),"steep streamed sections share one unsaved surface model");
        sa.Surface(1,3,out _,out float ha,out _);sb.Surface(1,-1,out _,out float hb,out _);
        Check(Math.Abs(ha-hb+8)<.01f,"connected steep sections agree on world height where old caps differed by metres");
        var ma=upper.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        var mb=lower.transform.Find("waterworks_river/river_surface").GetComponent<MeshFilter>().sharedMesh;
        Check(ma.tangents.Length==ma.vertexCount&&mb.tangents.Length==mb.vertexCount,"native river surfaces carry tangent frames for animated normal maps");
        int shared=0;bool consistent=true;
        var va=ma.vertices;var vb=mb.vertices;var fa=ma.uv2;var fb=mb.uv2;
        for(int i=0;i<va.Length;i++)for(int j=0;j<vb.Length;j++){
            if((va[i]-vb[j]-new Vector3(0,-8,4)).sqrMagnitude>.000001f)continue;
            shared++;if((fa[i]-fb[j]).sqrMagnitude>.000001f)consistent=false;
        }
        Check(shared>0&&consistent,"steep section boundary shares color depth, foam and ripple flow despite different saved settings");
        foreach(var river in new[]{upper,lower}){
            var renderer=river.transform.Find("waterworks_river/river_surface").GetComponent<MeshRenderer>();
            Check(renderer.lightProbeUsage==UnityEngine.Rendering.LightProbeUsage.Off&&renderer.probeAnchor==Player.m_localPlayer.transform,"river reflections use a common probe anchor across section bounds");
        }
        int Hits(Mesh mesh,Vector3 offset,float x,float z,bool inclusive=false){
            int hits=0;var vs=mesh.vertices;var ts=mesh.triangles;
            for(int i=0;i<ts.Length;i+=3){var p=vs[ts[i]]+offset;var q=vs[ts[i+1]]+offset;var r=vs[ts[i+2]]+offset;
                float Cross(Vector3 u,Vector3 v)=>(v.x-u.x)*(z-u.z)-(v.z-u.z)*(x-u.x);
                float c1=Cross(p,q),c2=Cross(q,r),c3=Cross(r,p);
                float epsilon=inclusive? -1e-5f:1e-5f;
                if((c1>epsilon&&c2>epsilon&&c3>epsilon)||(c1< -epsilon&&c2< -epsilon&&c3< -epsilon))hits++;
            }return hits;
        }
        int duplicates=0,covered=0,holes=0;
        for(float x=-1.837f;x<3;x+=.31f)for(float z=1.137f;z<5;z+=.29f){
            int hits=Hits(ma,Vector3.zero,x,z)+Hits(mb,new Vector3(0,-8,4),x,z);if(hits>1)duplicates++;if(hits>0)covered++;
            a.Sample(x,z,out float da,out _,out _);b.Sample(x,z-4,out float db,out _,out _);
            sa.Surface(x,z,out _,out float height,out _);var point=origin+new Vector3(x,height,z);
            if(Mathf.Min(da,db)<1.7f&&RiverTerrain.SamplePlanned(sa,point,out float bed)&&point.y-bed>1&&Hits(ma,Vector3.zero,x,z,true)+Hits(mb,new Vector3(0,-8,4),x,z,true)==0)holes++;
        }
        Check(covered>30&&duplicates==0,"steep junction renders one water sheet with no overlapping shelf triangles");
        Check(holes==0,"steep junction has no uncovered interior water samples");
        Capture(sa,new Vector3(10,8,12),origin+new Vector3(1,-8,4));File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../joined-cliff-preview.png"),true);
        ZNetScene.instance.Destroy(lower);yield return StartCoroutine(WaitForWaterWork(1));
        Check(sa.JoinedRoute==null,"unloading continuation clears shared junction model");
        ZNetScene.instance.Destroy(upper);yield return StartCoroutine(WaitForWaterWork(1));
    }
    IEnumerator BoatTest(){
        var origin=Player.m_localPlayer.transform.position+new Vector3(0,25,0);
        var root=new GameObject("isolated_boat_river");root.transform.position=origin;
        var spring=root.AddComponent<Spring>();var route=new Route{Width=12,Depth=3};
        route.Points.Add(new Node(0,0,0));route.Points.Add(new Node(0,0,8));route.Points.Add(new Node(0,0,16));spring.Route=route;
        RiverVisuals.Rebuild(spring);Physics.SyncTransforms();
        var prefab=PrefabManager.Instance.GetPrefab("Raft");
        Check(prefab&&prefab.GetComponent<Ship>()&&prefab.GetComponent<Piece>().m_waterPiece,"native raft uses water placement and Ship buoyancy");
        if(!prefab){Destroy(root);yield break;}
        var player=Player.m_localPlayer;var camera=GameCamera.instance.transform;
        var oldPosition=player.transform.position;var cp=camera.position;var cr=camera.rotation;
        var ghostField=typeof(Player).GetField("m_placementGhost",BindingFlags.Instance|BindingFlags.NonPublic);var oldGhost=ghostField.GetValue(player);
        try{
            player.transform.position=origin+new Vector3(1,1,8);player.GetComponent<Rigidbody>().position=player.transform.position;
            camera.position=origin+new Vector3(0,3,8);camera.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);ghostField.SetValue(player,prefab);
            var args=new object[]{Vector3.zero,Vector3.zero,null,null,null,true};
            bool hit=(bool)typeof(Player).GetMethod("PieceRayTest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,args);
            Check(hit&&Math.Abs(((Vector3)args[0]).y-origin.y)<.03&&args[4] is BoxCollider,"native boat placement preview hits elevated river water");
        }finally{ghostField.SetValue(player,oldGhost);camera.SetPositionAndRotation(cp,cr);player.transform.position=oldPosition;player.GetComponent<Rigidbody>().position=oldPosition;}
        var boat=Instantiate(prefab,origin+new Vector3(0,.5f,8),Quaternion.identity);
        yield return new WaitForSecondsRealtime(8);
        Check(boat&&boat.transform.position.y>origin.y-2&&boat.transform.position.y<origin.y+2,"native raft remains afloat on elevated river without ground support");
        if(boat)ZNetScene.instance.Destroy(boat);
        var cliff=new Route{Width=4};cliff.Points.Add(new Node(0,0,0));cliff.Points.Add(new Node(0,-8,4));cliff.Points.Add(new Node(4,-16,4));spring.Route=cliff;
        RiverVisuals.Rebuild(spring);yield return null;Capture(spring);
        File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../steep-river-preview.png"),true);
        Destroy(root);yield return null;
    }
    IEnumerator FinishTest(Spring spring){
        var original=spring.Route.Copy();var soundOnly=original.Copy();soundOnly.Volume=0;soundOnly.Sound=2;
        var before=spring.transform.Find("waterworks_river");Check(spring.Save(soundOnly),"sound-only save accepted");yield return StartCoroutine(WaitForWaterWork(1));
        Check(spring.Route.Volume==0&&spring.Route.Sound==2&&before==spring.transform.Find("waterworks_river"),"muting persists without geometry or terrain rebuild");
        var clips=new[]{RiverAudio.Get(0),RiverAudio.Get(1),RiverAudio.Get(2)};Check(clips.All(c=>c)&&clips.Select(c=>c.samples).Distinct().Count()==3,"three distinct embedded sound recordings decode");
        var wide=spring.Route.Copy();wide.Width=12;wide.Sound=-1;wide.Mud=true;wide.Whitewater=1;wide.SmoothBank(6,2);Check(spring.Save(wide),"wide muddy river with smoothed bank saved");yield return StartCoroutine(WaitForWaterWork(1.5f));
        var root=spring.transform.Find("waterworks_river");Check(!root.Find("river_mud"),"no separate brown riverbed mesh");
        var paintedMap=Heightmap.FindHeightmap(spring.transform.position);
        var paint=(Texture2D)typeof(Heightmap).GetField("m_paintMask",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(paintedMap);
        paintedMap.WorldToVertexMask(spring.transform.position-new Vector3(.5f,0,.5f),out var px,out var pz);
        var dirt=paint.GetPixel(px,pz);Check(dirt.r>.95f&&dirt.g<.05f&&dirt.b<.05f,"riverbed uses native dirt paint channels");
        Check(spring.Route.Sound==-1&&root.GetComponentInChildren<AudioSource>().clip==clips[2],"12 metre automatic river selects supplied large recording");
        Check(!root.Find("flow_and_foam"),"striped full-surface overlay removed");
        var flow=root.Find("downstream_current");Check(flow&&flow.GetComponent<ParticleSystem>().main.maxParticles<=64,"downstream flecks have bounded particle budget");
        var surface=root.Find("river_surface").GetComponent<MeshFilter>().sharedMesh;
        var shared=surface.vertices.Select((v,i)=>new{v,n=surface.normals[i]}).GroupBy(x=>x.v);
        Check(shared.All(g=>g.All(x=>(x.n-g.First().n).sqrMagnitude<.00001f)),"shared water vertices have matching normals across triangle seams");
        var riverMaterial=root.Find("river_surface").GetComponent<MeshRenderer>().sharedMaterial;
        Check(riverMaterial.shader==RunicLilyPads.RiverGardens.OutdoorWaterSource().shader&&riverMaterial.shader.isSupported,"native outdoor water shader is active and supported");
        Check(riverMaterial.GetTexture("_Normal")&&riverMaterial.GetTexture("_NormalFine"),"native ocean normal textures bound to river");
        var outdoor=RunicLilyPads.RiverGardens.OutdoorWaterSource();
        Check(riverMaterial!=outdoor&&riverMaterial.GetColor("_ColorTop")==outdoor.GetColor("_ColorTop")&&riverMaterial.GetFloat("_DepthFade")==outdoor.GetFloat("_DepthFade"),"river copies vanilla outdoor tint and depth fade without modifying ocean material");
        Check(surface.uv4.Length==surface.vertexCount&&surface.uv4.Any(v=>v.x>=0&&v.x<.55f),"shore foam receives terrain contact depths");
        Check(root.GetComponentsInChildren<ParticleSystem>().Where(p=>p.name=="shore_splash").All(p=>p.main.maxParticles<=6&&p.emission.rateOverTime.constant<=2),"shore splashes retain small emission and particle budgets");
        Check(surface.uv2.Length==surface.vertexCount&&surface.uv2.Any(v=>v.sqrMagnitude>.05f),"river carries moving downstream flow field");
        var flowGroups=surface.vertices.Select((v,i)=>new{v,flow=surface.uv2[i]}).GroupBy(x=>x.v);
        Check(flowGroups.All(g=>g.All(x=>(x.flow-g.First().flow).sqrMagnitude<.00001f)),"shared water vertices have seamless flow coordinates");
        Check(root.GetComponentsInChildren<Collider>().Length==1,"current rendering adds no colliders");
        Capture(spring);File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../wide-river-preview.png"),true);
        Check(spring.Route.BankStrokes.Count==1,"bank brush persisted through real ZDO");
        var bend=new Route{Width=8,Depth=1.4f};bend.Points.Add(new Node(0,-.35f,0));
        for(int i=1;i<=4;i++)bend.Points.Add(new Node(0,-.35f-i*.12f,i*4));
        for(int i=1;i<=4;i++)bend.Points.Add(new Node(i*4,-.83f-i*.12f,16));
        Check(spring.Save(bend),"downstream bend fixture saved");yield return StartCoroutine(WaitForWaterWork(2));
        Capture(spring);File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../bend-flow-a.png"),true);
        yield return new WaitForSecondsRealtime(.8f);
        Capture(spring);File.Copy(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),Path.Combine(Paths.BepInExRootPath,"../bend-flow-b.png"),true);
        RiverEditor.Show(spring);var editor=UnityEngine.Object.FindObjectOfType<RiverEditor>();typeof(RiverEditor).GetMethod("Page",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(editor,new object[]{true});yield return null;CaptureMenu("sound-menu-preview.png");RiverEditor.Close();
        Check(spring.Save(original),"finish test restores previous section");yield return StartCoroutine(WaitForWaterWork(1.5f));
    }
    IEnumerator GardenIntegration(Spring spring){
        var r=Route.Trace((x,z)=>0,-50,0,48,6,1.2f,out _);Check(spring.Save(r),"river garden test route save");yield return StartCoroutine(WaitForWaterWork(1.5f));
        var origin=spring.transform.position;var stonePrefab=PrefabManager.Instance.GetPrefab("piece_arcanedecor_rivergarden");Check(stonePrefab,"WaterGardens river stone registration");
        var stone=Instantiate(stonePrefab,origin+new Vector3(4,0,20),Quaternion.identity);yield return StartCoroutine(WaitForWaterWork(1));
        var garden=stone.GetComponent<RunicLilyPads.GardenPiece>();var spans=GardenBridge.Section(stone.transform.position);
        Check(Math.Abs(spans.Sum(s=>Vector3.Distance(s.A,s.B))-20)<.05,"garden stone follows 20 metres of channel");
        Check(RunicLilyPads.RiverGardens.SettingsAt(origin+new Vector3(0,0,20))!=null,"river decor receives garden settings");
        Check(RunicLilyPads.RiverGardens.SettingsAt(origin+new Vector3(0,0,31))==null,"section settings stop at the 20 metre boundary");
        Check(stone.GetComponentsInChildren<WaterVolume>().Length==0,"garden stone adds no water volumes");
        var emitter=stone.GetComponentInChildren<ParticleSystem>();Check(emitter&&emitter.shape.shapeType==ParticleSystemShapeType.Mesh&&emitter.shape.mesh.vertexCount>20,"fireflies follow channel geometry with one emitter");
        var player=Player.m_localPlayer;player.transform.position=stone.transform.position+Vector3.up*2;player.GetComponent<Rigidbody>().position=player.transform.position;
        var settings=garden.Settings.Copy();settings.FlowerScale=1.1f;settings.FloatStatues=false;settings.NightOnly=false;settings.Track=2;
        Check(garden.Save(settings),"river garden shared controls save");yield return StartCoroutine(WaitForWaterWork(1));
        Check(Math.Abs(RunicLilyPads.RiverGardens.SettingsAt(origin+new Vector3(0,0,20)).FlowerScale-1.1f)<.01,"river flower scale survives ZDO save");
        var lily=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_lily_blue_001"),origin+new Vector3(0,-2,20),Quaternion.identity);yield return null;
        var visual=lily.transform.Find("lily_visual");Check(visual&&Math.Abs(visual.position.y-(origin.y-.335f))<.03,"WaterGardens lily floats on river surface");
        var statue=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_fountain_004"),origin+new Vector3(0,0,20),Quaternion.identity);yield return null;
        var floating=statue.GetComponent<RunicLilyPads.GardenFloat>();Check(floating&&!floating.Floating(null),"river stone controls statue grounding");
        var camera=GameCamera.instance.transform;var cp=camera.position;var cr=camera.rotation;var ghostField=typeof(Player).GetField("m_placementGhost",BindingFlags.Instance|BindingFlags.NonPublic);var originalGhost=ghostField.GetValue(player);
        foreach(var col in lily.GetComponentsInChildren<Collider>())col.enabled=false;
        // Keep the player within normal placement range; their falling height otherwise
        // depends on frame timing after the terrain edit above.
        player.transform.position=origin+new Vector3(1,.3f,19);player.GetComponent<Rigidbody>().position=player.transform.position;
        try{
            camera.position=origin+new Vector3(1,2,20);camera.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);ghostField.SetValue(player,lily);
            var args=new object[]{Vector3.zero,Vector3.zero,null,null,null,true};
            bool hit=(bool)typeof(Player).GetMethod("PieceRayTest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,args);
            Check(hit&&Math.Abs(((Vector3)args[0]).y-(origin.y-.35f))<.03&&args[4] is BoxCollider,"native placement ray hits river without mesh colliders");
        }finally{camera.SetPositionAndRotation(cp,cr);ghostField.SetValue(player,originalGhost);}
        RunicLilyPads.GardenEditor.Open(garden);yield return null;Check(GameObject.Find("WaterGardens pond menu"),"river stone opens the WaterGardens menu");CaptureMenu("garden-menu-preview.png");RunicLilyPads.GardenEditor.Close();
        var pond=Instantiate(PrefabManager.Instance.GetPrefab("piece_arcanedecor_pond_round"),origin+new Vector3(0,-.2f,24),Quaternion.identity);yield return StartCoroutine(WaitForWaterWork(1.5f));
        Check(Math.Abs(spring.Receiving(0,24).Value+.35f)<.03,"live elevated pond supplies receiving water level");
        Check(Math.Abs(RunicLilyPads.LilyFloat.WaterAt(origin+new Vector3(0,0,24))-(origin.y-.35f))<.03,"pond and river agree on shared decoration surface");
        ZNetScene.instance.Destroy(pond);yield return StartCoroutine(WaitForWaterWork(1));
        ZNetScene.instance.Destroy(lily);ZNetScene.instance.Destroy(statue);ZNetScene.instance.Destroy(stone);yield return null;
        Check(RunicLilyPads.RiverGardens.SettingsAt(origin+new Vector3(0,0,20))==null,"removing stone clears section controls");
        player.transform.position=origin+new Vector3(-3,2,4);player.GetComponent<Rigidbody>().position=player.transform.position;
        var legacyLong=Route.Trace((x,z)=>0,-50,0,180,6,1.2f,out _);Check(spring.Save(legacyLong),"legacy long section remains editable");yield return StartCoroutine(WaitForWaterWork(1));
        RiverHoe.Select(spring);typeof(RiverHoe).GetField("start",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,2);
        bool continued=RiverHoe.Extend(origin+new Vector3(16,0,4));Check(continued,"hoe automatically creates a continuation instead of rejecting length");yield return StartCoroutine(WaitForWaterWork(1));
        var continuation=RiverHoe.Selected;
        Check(continuation&&continuation!=spring&&continuation.Route!=null&&spring.Route.Length+continuation.Route.Length>192,"connected river exceeds old 192 metre cap");
        if(continuation&&continuation!=spring){Check(continuation.View.GetZDO().GetBool("aww_continuation",false)&&continuation.GetComponent<Piece>().m_resources.Length==0,"continuation persists without duplicate resource refunds");ZNetScene.instance.Destroy(continuation.gameObject);}
        yield return null;Check(spring.Save(r),"original section restored after continuation test");yield return StartCoroutine(WaitForWaterWork(1));

    }
    void CaptureMenu(string filename="menu-preview.png"){
        var canvas=GameObject.Find("WaterGardens pond menu").GetComponent<Canvas>();var previousMode=canvas.renderMode;var previousCamera=canvas.worldCamera;
        var go=new GameObject("menu_capture_camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);
        camera.cullingMask|=LayerMask.GetMask("UI");
        var rt=new RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;var previous=RenderTexture.active;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath,"../"+filename),texture.EncodeToPNG());}
        finally{canvas.renderMode=previousMode;canvas.worldCamera=previousCamera;RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(texture);Destroy(go);}
    }
    void Capture(Spring spring,Vector3? viewOffset=null,Vector3? focus=null){
        var trees=UnityEngine.Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeSelf).Select(t=>t.gameObject).ToArray();
        foreach(var tree in trees)tree.SetActive(false);
        var origin=spring.transform.position;var points=spring.Route.Points;var end=points[Math.Min(points.Count-1,8)];
        var target=focus??origin+new Vector3(end.X,end.Y,end.Z)*.5f;
        // Use the real camera's rendering components and command buffers: CopyFrom
        // does not copy Valheim's depth/refraction and post-processing setup.
        var camera=Camera.main;var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;var savedFov=camera.fieldOfView;var savedTarget=camera.targetTexture;
        var post=camera.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
        var profile=post?post.profile:null;
        bool motion=profile&&profile.motionBlur.enabled,taa=profile&&profile.antialiasing.enabled,dof=profile&&profile.depthOfField.enabled;
        if(profile){profile.motionBlur.enabled=false;profile.antialiasing.enabled=false;profile.depthOfField.enabled=false;}
        camera.depthTextureMode|=DepthTextureMode.Depth;
        camera.transform.position=target+(viewOffset??new Vector3(-14,18,-16));camera.transform.LookAt(target);camera.fieldOfView=55;
        var rt=new RenderTexture(1280,960,24);rt.Create();camera.targetTexture=rt;
        var previous=RenderTexture.active;var texture=new Texture2D(1280,960,TextureFormat.RGB24,false);
        try{camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,960),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath,"../river-preview.png"),texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;camera.targetTexture=savedTarget;camera.transform.SetPositionAndRotation(savedPosition,savedRotation);camera.fieldOfView=savedFov;if(profile){profile.motionBlur.enabled=motion;profile.antialiasing.enabled=taa;profile.depthOfField.enabled=dof;}rt.Release();Destroy(rt);Destroy(texture);foreach(var tree in trees)if(tree)tree.SetActive(true);}
    }
}

public sealed class TestFloater:MonoBehaviour,IWaterInteractable {
    public float Level;
    public void SetLiquidLevel(float level,LiquidType type,Component obj){Level=level;}
    public Transform GetTransform()=>transform;
    public int Increment(LiquidType type)=>1;
    public int Decrement(LiquidType type)=>0;
}



