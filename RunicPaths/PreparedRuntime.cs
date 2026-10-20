using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths;

// Preparation is a loading-screen state, never a task following an active player.
// Unity/native world sampling stays on the main thread. Only immutable arrays are
// consumed by the terrain hooks after publication.
internal sealed class PreparedRuntime : IDisposable
{
    internal static PreparedRuntime? Current;
    internal static PreparedSurface? Active;
    internal bool Ready {get;private set;}
    internal bool InUse {get;private set;}
    internal string Status="Waiting for world locations";
    private readonly ConfigFile config;
    private readonly ConfigEntry<string> scope,patterns;
    private readonly ConfigEntry<float> preparationBudget;
    private readonly ConfigEntry<int> revision;
    private readonly ConfigEntry<int> loops;
    private ZNet? session;
    private IEnumerator<int>? job;
    private ZRoutedRpc? rpc;
    private float nextReport,nextRequest;
    private string? error;
    private byte[]? payload;
    private MemoryStream? download;
    private int expectedBytes,expectedOffset;
    private long world;
    private int seed;
    private readonly Dictionary<long,int> transfers=new Dictionary<long,int>();
    private readonly Dictionary<long,float> lastRequests=new Dictionary<long,float>();
    private readonly List<double> applyMilliseconds=new List<double>();
    internal PreparedRuntime(Plugin plugin,ConfigFile config)
    {
        this.config=config;Current=this;
        scope=config.Bind("Preparation","Scope","WholeWorld",new ConfigDescription("WholeWorld prepares every eligible landmass before spawning. SpawnIsland is a shorter trial. ExistingRoutes compiles only previously saved road geometry. Change while logged out.",new AcceptableValueList<string>("WholeWorld","SpawnIsland","ExistingRoutes")));
        patterns=config.Bind("Preparation","POIs","StartTemple,Wood*,Stone*,Ruin*,Runestone*,Crypt*,TrollCave*,Dolmen*,Grave*,Village*,Farm*","Eligible location names. All matching POIs are considered; no count cap. Map exploration is never changed.");
        preparationBudget=config.Bind("Preparation","MillisecondsPerFrame",12f,new ConfigDescription("Loading-screen preparation budget. Individual native samples/checkpoints can exceed it. No preparation is run during gameplay.",new AcceptableValueRange<float>(2,30)));
        revision=config.Bind("Preparation","Revision",1,"Increase while logged out to explicitly request a new prepared surface. Previous cache is retained as .bak. Width/grade changes also take effect on the next world load.");
        loops=config.Bind("Preparation","ExtraLoopsPerIsland",2,new ConfigDescription("Maximum extra connections that shorten long detours. Zero produces a branching network.",new AcceptableValueRange<int>(0,8)));
    }
    private T Setting<T>(string section,string key,T fallback) => config.TryGetEntry<T>(section,key,out var entry)?entry.Value:fallback;
    private PlannerSettings Settings() => new PlannerSettings { Width=Setting("Paths","Width",3f),MaxSlope=Setting("Paths","MaximumSlope",.55f),
        MaximumRaise=Setting("Terrain","MaximumRaise",1.5f),MaximumLower=Setting("Terrain","MaximumLower",1.5f),MaximumGap=Setting("Paths","MaximumGap",32f),
        MaxIslandCells=Setting("Performance","MaximumIslandCells",80000),MaxSearchNodes=Setting("Performance","MaximumRouteSearchNodes",16000),MaxPois=0,
        WaterLevel=ZoneSystem.instance.m_waterLevel,ExtraLoops=loops.Value };
    private int Signature(PlannerSettings s)
    {
        unchecked {int h=304;foreach(char c in scope.Value+"|"+patterns.Value) h=h*31+c;
            foreach(float f in new[]{s.Width,s.MaxSlope,s.MaximumRaise,s.MaximumLower,s.MaximumGap,s.MaxIslandCells,s.MaxSearchNodes,s.ExtraLoops,revision.Value,Setting("Protection","PlayerBuildBuffer",12f)}) h=h*31+f.GetHashCode();return h;}
    }
    public void Update(bool enabled)
    {
        if(!enabled) {InUse=false;return;}
        InUse=true;
        if(!ZNet.instance || WorldGenerator.instance==null || !ZoneSystem.instance)
        {if(session) Reset();return;}
        if(session!=ZNet.instance)
        {
            Reset();session=ZNet.instance;
            if(session.IsServer() && (session.GetWorld()==null || session.GetWorld().m_menu)) {InUse=false;return;}
        }
        if(rpc!=ZRoutedRpc.instance && ZRoutedRpc.instance!=null)
        {
            rpc=ZRoutedRpc.instance;
            rpc.Register<ZPackage>("RunicPaths.PreparedRequest.1",Request);
            rpc.Register<ZPackage>("RunicPaths.PreparedData.1",Receive);
        }
        try
        {
            SendChunks();
            if(error!=null || Ready || !ZNetScene.instance) return;
            if(!session.IsServer())
            {
                if(rpc!=null && Time.realtimeSinceStartup>=nextRequest && download==null)
                {nextRequest=Time.realtimeSinceStartup+5;rpc.InvokeRoutedRPC("RunicPaths.PreparedRequest.1",new ZPackage());Status="Waiting for the server's prepared roads";}
                return;
            }
            if(!ZoneSystem.instance.LocationsGenerated) return;
            // Loading gate prevents local spawn. Refuse to convert beneath any player.
            if(Player.m_localPlayer || session.GetAllCharacterZDOS().Count>0)
            {Status="Road preparation requires an empty world; log out all players and restart the host";return;}
            job ??= Prepare().GetEnumerator();
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed.TotalMilliseconds<preparationBudget.Value)
                if(!job.MoveNext()) {job.Dispose();job=null;break;}
            if(Time.realtimeSinceStartup>=nextReport)
            {nextReport=Time.realtimeSinceStartup+15;Plugin.Report("Preparation: "+Status);}
        }
        catch(Exception e) {error=e.ToString();Status="Road preparation failed: "+e.Message;job?.Dispose();job=null;Plugin.Report(error);}
    }
    private bool Included(string name) => patterns.Value.Split(',').Any(raw=>
    {string p=raw.Trim();return p.EndsWith("*",StringComparison.Ordinal)?name.StartsWith(p.Substring(0,p.Length-1),StringComparison.OrdinalIgnoreCase):name.Equals(p,StringComparison.OrdinalIgnoreCase);});

    private IEnumerable<int> Prepare()
    {
        var w=session!.GetWorld();world=w.m_uid;seed=w.m_seed;
        string folder=Path.Combine(Paths.ConfigPath,"RunicPaths","Prepared",world.ToString());
        Directory.CreateDirectory(folder);
        string cache=Path.Combine(folder,"surface.bin");
        var settings=Settings();int signature=Signature(settings);
        PreparedSurface? previous=null;
        if(File.Exists(cache))
        {
            Status="Checking saved road surfaces";
            var loaded=PreparedSurface.Decode(File.ReadAllBytes(cache),world,seed);
            previous=loaded;
            if(loaded.Signature==signature)
            {
                Status="Checking existing roadside objects before play";
                foreach(int step in PreparedObjects.Relocate(loaded,folder,Setting("Protection","MaximumRelocationDistance",24f))) yield return step;
                Publish(loaded);yield break;
            }
        }
        Status="Loading resumable network checkpoint";yield return 0;
        string checkpoint=Path.Combine(folder,"network-"+signature+".json");
        string completedFile=Path.Combine(folder,"completed-"+signature+".txt");
        string previousNetwork=Path.Combine(folder,"network-"+previous?.Signature+".json");
        var network=Persistence.Load(File.Exists(checkpoint)?checkpoint:File.Exists(previousNetwork)?previousNetwork:Path.Combine(Paths.ConfigPath,"RunicPaths",world+".json"),world,seed);
        string partialFile=Path.Combine(folder,"island-"+signature+".json");
        var partial=PlanningProgress.Load(partialFile,world,seed,signature);
        var completed=File.Exists(completedFile)?new HashSet<string>(File.ReadAllLines(completedFile)):new HashSet<string>();
        var terrain=new GameTerrain();
        var locations=ZoneSystem.instance.m_locationInstances.Values.ToList();
        var protection=PreparedProtection.Capture(Setting("Protection","PlayerBuildBuffer",12f));
        var pois=locations.Where(l=>Included(l.m_location.m_prefabName) && terrain.Allowed(GameTerrain.PointOf(l.m_position)))
            .Select(l=>new Poi {Name=l.m_location.m_prefabName,Position=GameTerrain.PointOf(l.m_position),Radius=l.m_placed?2:Math.Max(8,l.m_location.m_exteriorRadius)})
            .OrderBy(p=>p.Name=="StartTemple"?0:1).ThenBy(p=>p.Position.X).ThenBy(p=>p.Position.Z).ToList();
        var plannerProtection=protection.Select(e=>new Exclusion {Center=e.Center,HalfX=Math.Max(.01f,e.HalfX-2),HalfZ=Math.Max(.01f,e.HalfZ-2),Radius=e.Radius});
        var exclusions=locations.Where(l=>!l.m_placed).Select(l=>new Exclusion {Center=GameTerrain.PointOf(l.m_position),Radius=Math.Max(8,l.m_location.m_exteriorRadius)}).Concat(plannerProtection).ToList();
        string Key(Poi p)=>p.Name+"|"+p.Position.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+p.Position.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        var covered=new HashSet<Cell>();
        foreach(var island in network.Islands.Where(i=>i.PlanningSignature==signature)) covered.UnionWith(island.Cells);
        var failures=new List<string>();
        if(scope.Value!="ExistingRoutes") foreach(var poi in (scope.Value=="SpawnIsland"?pois.Take(1):pois))
        {
            string key=Key(poi);
            if(completed.Contains(key) || covered.Contains(Cell.From(poi.Position,24))) continue;
            var planner=new Planner(terrain,settings,exclusions);
            void SaveProgress(PlanningProgress progress)
            {progress.World=world;progress.Seed=seed;progress.Signature=signature;progress.Save(partialFile);}
            foreach(int step in planner.Build(poi.Position,pois,partial!=null && partial.Landing.Equals(poi.Position)?partial:null,SaveProgress))
            {Status=$"{completed.Count}/{pois.Count} POIs on completed landmasses; {planner.Status}; {planner.TotalSearchNodes:N0} search nodes";yield return step;}
            partial=null;
            var result=planner.Result;
            if(result!=null)
            {
                result.PlanningSignature=signature;
                var cells=new HashSet<Cell>(result.Cells);
                var affected=network.Islands.Where(i=>i.Cells.Any(c=>cells.Contains(c))).ToList();
                result=NetworkUpdate.Preserve(result,affected);
                network.Islands.RemoveAll(i=>affected.Contains(i));network.Islands.Add(result);
                covered.UnionWith(result.Cells);
                foreach(var outcome in result.Outcomes) completed.Add(Key(new Poi{Name=outcome.Name,Position=outcome.Position}));
                foreach(var p in pois.Where(p=>covered.Contains(Cell.From(p.Position,24)))) completed.Add(Key(p));
            }
            else failures.Add(key+": "+planner.Status);
            completed.Add(key);
            // Network first: interruption can repeat an attempt, never skip unsaved roads.
            Persistence.Save(checkpoint,network);
            File.WriteAllLines(completedFile+".tmp",completed.OrderBy(x=>x,StringComparer.Ordinal));
            if(File.Exists(completedFile)) File.Replace(completedFile+".tmp",completedFile,completedFile+".bak");else File.Move(completedFile+".tmp",completedFile);
            // Remove only after both final network and completion records are durable.
            if(File.Exists(partialFile)) File.Delete(partialFile);
            yield return 0;
            if(scope.Value=="SpawnIsland") break;
        }
        Persistence.Save(checkpoint,network);
        Status="Compiling continuous road surfaces";yield return 0;
        var surface=new PreparedSurface {Signature=signature};
        foreach(int step in surface.Build(network,terrain,settings,protection,previous)) {Status=$"Compiling road surfaces: {surface.Tiles.Count} tiles";yield return step;}
        Status="Writing verified road cache";yield return 0;
        surface.Save(cache);
        var reports=new List<string> {"Runic Paths preparation report",$"World {world}; seed {seed}; signature {signature}; scope {scope.Value}",
            $"Eligible POIs: {pois.Count}; surfaces: {surface.Tiles.Count}; established route sections: {network.Islands.Sum(i=>i.Routes.Count)}"};
        reports.AddRange(failures);
        foreach(var p in pois)
        {
            var outcome=network.Islands.SelectMany(i=>i.Outcomes??new List<PoiOutcome>()).FirstOrDefault(o=>o.Name==p.Name && o.Position.Equals(p.Position));
            reports.Add(Key(p)+": "+(outcome==null?"No recorded connection (outside prepared scope, survey failed, or legacy geometry)":outcome.Status+"; component "+outcome.Component));
        }
        File.WriteAllLines(Path.Combine(folder,"coverage.txt"),reports);
        Status="Relocating existing roadside objects before play";
        foreach(int step in PreparedObjects.Relocate(surface,folder,Setting("Protection","MaximumRelocationDistance",24f))) yield return step;
        Publish(surface);
    }
    private void Publish(PreparedSurface surface)
    {
        Active=surface;payload=surface.Encode();Ready=true;Status=$"Ready: {surface.Tiles.Count} prepared terrain tiles";
        Plugin.Report(Status+". No route planning or terrain-painter work will run during play.");
    }
    private void Request(long sender,ZPackage p)
    {
        if(!session || !session.IsServer() || session.GetPeers().All(peer=>peer.m_uid!=sender)) return;
        if(lastRequests.TryGetValue(sender,out float time) && Time.realtimeSinceStartup-time<3) return;
        lastRequests[sender]=Time.realtimeSinceStartup;
        if(Ready && payload!=null && !transfers.ContainsKey(sender)) transfers[sender]=0;
    }
    private void SendChunks()
    {
        if(rpc==null || payload==null || !session || !session.IsServer()) return;
        // Reliable ordered RPC, one bounded packet per peer per frame during join.
        foreach(long peer in transfers.Keys.ToArray())
        {
            if(session.GetPeers().All(p=>p.m_uid!=peer)) {transfers.Remove(peer);continue;}
            int offset=transfers[peer],length=Math.Min(32768,payload.Length-offset);
            var part=new byte[length];Buffer.BlockCopy(payload,offset,part,0,length);
            var packet=new ZPackage();packet.Write(world);packet.Write(seed);packet.Write(payload.Length);packet.Write(offset);packet.Write(part);
            rpc.InvokeRoutedRPC(peer,"RunicPaths.PreparedData.1",packet);
            if(offset+length==payload.Length) transfers.Remove(peer);else transfers[peer]=offset+length;
        }
    }
    private void Receive(long sender,ZPackage packet)
    {
        if(!session || session.IsServer() || session.GetServerPeer()?.m_uid!=sender || Ready) return;
        try
        {
            long id=packet.ReadLong();int remoteSeed=packet.ReadInt(),total=packet.ReadInt(),offset=packet.ReadInt();byte[] bytes=packet.ReadByteArray();
            if(id!=session.GetWorldUID() || remoteSeed!=WorldGenerator.instance.GetSeed() || total<60 || total>256*1024*1024 || bytes.Length>32768) throw new InvalidDataException("Invalid road transfer");
            if(offset==0) {download?.Dispose();download=new MemoryStream();expectedBytes=total;expectedOffset=0;world=id;seed=remoteSeed;}
            if(download==null || total!=expectedBytes || offset!=expectedOffset || bytes.Length==0 || offset+bytes.Length>total) throw new InvalidDataException("Out-of-order road transfer");
            download.Write(bytes,0,bytes.Length);expectedOffset+=bytes.Length;Status=$"Receiving roads: {100L*expectedOffset/total}%";
            if(expectedOffset==total) {Publish(PreparedSurface.Decode(download.ToArray(),world,seed));download.Dispose();download=null;}
        }
        catch(Exception e) {download?.Dispose();download=null;error=e.Message;Status="Road transfer failed: "+e.Message;Plugin.Report(Status);}
    }
    internal void Measure(double ms)
    {
        if(applyMilliseconds.Count<10000) applyMilliseconds.Add(ms);
    }
    internal void DrawStatus()
    {
        if(!InUse || Ready || !session || ZNet.instance?.GetWorld()?.m_menu==true) return;
        GUI.Box(new Rect(20,20,Math.Min(Screen.width-40,900),85),"Runic Paths — preparing before play\n"+Status+"\nPreparation checkpoints resume after a restart. See BepInEx/LogOutput.log for details.");
    }
    private void Reset()
    {
        if(applyMilliseconds.Count>0)
        {applyMilliseconds.Sort();Plugin.Report($"Prepared terrain hook: {applyMilliseconds.Count} samples, p95={applyMilliseconds[(int)((applyMilliseconds.Count-1)*.95)]:F3} ms, max={applyMilliseconds.Last():F3} ms. Hook time excludes native mesh building and total frame time.");}
        applyMilliseconds.Clear();job?.Dispose();job=null;Active=null;Ready=false;payload=null;session=null;rpc=null;error=null;
        download?.Dispose();download=null;transfers.Clear();lastRequests.Clear();Status="Waiting for world locations";
    }
    public void Dispose() {Reset();if(Current==this) Current=null;}
    internal static bool LegacyTest => Environment.GetEnvironmentVariable("RUNIC_PATHS_LEGACY_TEST")=="1" && Environment.GetEnvironmentVariable("RUNIC_PATHS_ISOLATED_SMOKE")=="1";
    internal static bool Hold => Current!=null && Current.InUse && !Current.Ready && !LegacyTest;
}

[HarmonyPatch(typeof(Game),"UpdateRespawn")]
internal static class PreparedSpawnGate {private static bool Prefix() => !PreparedRuntime.Hold;}

[HarmonyPatch(typeof(ZNetScene),"CreateObjects")]
internal static class PreparedObjectGate {private static bool Prefix() => !PreparedRuntime.Hold;}

[HarmonyPatch(typeof(ZoneSystem),"SpawnZone")]
internal static class PreparedZoneGate
{
    private static bool Prefix(ref GameObject root,ref bool __result)
    {if(!PreparedRuntime.Hold) return true;root=null!;__result=false;return false;}
}

[HarmonyPatch(typeof(Heightmap),"ApplyModifiers")]
internal static class PreparedTerrainBase
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var apply=AccessTools.Method(typeof(Texture2D),nameof(Texture2D.Apply),Type.EmptyTypes);
        int replaced=0;
        foreach(var instruction in instructions)
        {
            if(instruction.Calls(apply))
            {
                var self=new CodeInstruction(instruction){opcode=OpCodes.Ldarg_0,operand=null};yield return self;
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PreparedTerrainBase),nameof(PaintAndUpload)));replaced++;
            }
            else yield return instruction;
        }
        if(replaced!=1) throw new InvalidOperationException("Terrain mask upload hook changed; refusing an unsafe patch");
    }
    private static void PaintAndUpload(Texture2D mask,Heightmap map)
    {
        var surface=PreparedRuntime.Active;
        if(surface!=null && !map.IsDistantLod && map.m_width==64 && map.m_scale==1 && surface.Tiles.TryGetValue(Cell.From(GameTerrain.PointOf(map.transform.position),64),out var tile))
        {
            long started=Stopwatch.GetTimestamp();
            var compiler=TerrainComp.FindTerrainCompiler(map.transform.position);
            var clean=compiler?PreparedLegacy.CachedClean(compiler):null;
            foreach(var v in tile.Vertices)
            {
                if(v.Paint==0 || clean!=null && clean.Painted[v.Index]) continue;
                int x=v.Index%65,z=v.Index/65;Color c=mask.GetPixel(x,z);
                // Cultivated fields and paving take precedence over automatic dirt.
                if(c.g>.5f || c.b>.5f) continue;
                Color road=Color.Lerp(c,Heightmap.m_paintMaskDirt,v.Paint/255f);road.a=c.a;mask.SetPixel(x,z,road);
            }
            PreparedRuntime.Current?.Measure((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
        }
        mask.Apply();
    }
    private static void Prefix(Heightmap __instance,List<float> ___m_heights,Texture2D ___m_paintMask)
    {
        var surface=PreparedRuntime.Active;
        if(surface==null || __instance.IsDistantLod || __instance.m_width!=64 || __instance.m_scale!=1) return;
        if(!surface.Tiles.TryGetValue(Cell.From(GameTerrain.PointOf(__instance.transform.position),64),out var tile)) return;
        long started=Stopwatch.GetTimestamp();float y=__instance.transform.position.y;
        var compiler=TerrainComp.FindTerrainCompiler(__instance.transform.position);
        var protectedHeights=compiler?PreparedLegacy.OriginalProtection(compiler):Array.Empty<byte>();
        foreach(var v in tile.Vertices)
        {
            bool protectedHeight=protectedHeights.Length>v.Index/8 && (protectedHeights[v.Index/8]&(1<<(v.Index%8)))!=0;
            if(v.Grade>0 && !protectedHeight) ___m_heights[v.Index]=v.Height-y;
        }
        PreparedRuntime.Current?.Measure((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
        // Native ApplyModifiers uploads the mask, then Generate builds meshes once.
    }
}

[HarmonyPatch(typeof(ZoneSystem),"InsideClearArea")]
internal static class PreparedVegetation
{
    private static void Postfix(Vector3 point,ref bool __result)
    {if(!__result && VegetationScope.Depth>0 && PreparedRuntime.Active!=null) __result=PreparedRuntime.Active.Cleared(GameTerrain.PointOf(point),1.5f);}
}
