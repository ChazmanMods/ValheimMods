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
using RunicSettlements.Core;
using UnityEngine;

namespace RunicSettlements;

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
    private readonly ConfigEntry<float> preparationBudget;
    private readonly SettlementOptions options=new SettlementOptions();
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
        options.ReplaceSmallRuins=config.Bind("Sites","ReplaceSmallRuins",true,"Allow replacing small generated ruins and stone formations inside clearings. Player builds, spawn, bosses, traders and dungeons stay protected.").Value;
        options.Count=config.Bind("Sites","Count",5,new ConfigDescription("Maximum community clearings on the spawn island. Fewer may fit safely.",new AcceptableValueRange<int>(1,5))).Value;
        options.Radius=config.Bind("Sites","Diameter",120f,new ConfigDescription("Flat building-area diameter in metres. Locked after the world plan is created.",new AcceptableValueRange<float>(40,160))).Value/2;
        options.MaximumRaise=config.Bind("Terrain","MaximumRaise",8f,new ConfigDescription("Maximum fill above original ground.",new AcceptableValueRange<float>(0.5f,8))).Value;
        options.MaximumLower=config.Bind("Terrain","MaximumLower",8f,new ConfigDescription("Maximum cut below original ground.",new AcceptableValueRange<float>(0.5f,8))).Value;
        options.SearchRadius=config.Bind("Sites","SearchRadius",2000f,new ConfigDescription("Maximum distance from spawn to search on connected dry land.",new AcceptableValueRange<float>(500,3000))).Value;
        options.Separation=config.Bind("Sites","MinimumSeparation",300f,new ConfigDescription("Minimum distance between community centres.",new AcceptableValueRange<float>(180,800))).Value;
        options.Width=config.Bind("Roads","Width",5f,new ConfigDescription("Paved road width in metres.",new AcceptableValueRange<float>(3,10))).Value;
        options.LampSpacing=config.Bind("Decorations","LampSpacing",40f,new ConfigDescription("Minimum spacing along the road; actual count also has a world limit.",new AcceptableValueRange<float>(20,100))).Value;
        options.MaximumLamps=config.Bind("Decorations","MaximumLamps",96,new ConfigDescription("World-wide limit for settlement road lights.",new AcceptableValueRange<int>(0,200))).Value;
        options.TreesPerSite=config.Bind("Decorations","TreesPerSite",2,new ConfigDescription("Sparse perimeter oaks and pines.",new AcceptableValueRange<int>(0,4))).Value;
        preparationBudget=config.Bind("Preparation","MillisecondsPerFrame",12f,new ConfigDescription("Work budget while loading; individual native calls may exceed it.",new AcceptableValueRange<float>(2,30)));
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
            rpc.Register<ZPackage>("RunicSettlements.PreparedRequest.1",Request);
            rpc.Register<ZPackage>("RunicSettlements.PreparedData.1",Receive);
        }
        try
        {
            SendChunks();
            if(error!=null || Ready || !ZNetScene.instance) return;
            if(!session.IsServer())
            {
                if(rpc!=null && Time.realtimeSinceStartup>=nextRequest && download==null)
                {nextRequest=Time.realtimeSinceStartup+5;rpc.InvokeRoutedRPC("RunicSettlements.PreparedRequest.1",new ZPackage());Status="Waiting for the server's prepared roads";}
                return;
            }
            if(!ZoneSystem.instance.LocationsGenerated) {Status="Waiting for Valheim's location generation";return;}
            // Loading gate prevents local spawn. Refuse to convert beneath any player.
            if(Player.m_localPlayer || session.GetAllCharacterZDOS().Count>0)
            {Status="Road preparation requires an empty world; log out all players and restart the host";return;}
            job ??= Prepare().GetEnumerator();
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed.TotalMilliseconds<preparationBudget.Value)
                if(!job.MoveNext()) {job.Dispose();job=null;break;}
        }
        catch(Exception e) {error=e.ToString();Status="Road preparation failed: "+e.Message;job?.Dispose();job=null;Plugin.Report(error);}
        finally
        {
            if(!Ready && Time.realtimeSinceStartup>=nextReport)
            {nextReport=Time.realtimeSinceStartup+15;Plugin.Report("Preparation: "+Status);}
        }
    }
    private IEnumerable<int> Prepare()
    {
        var w=session!.GetWorld();world=w.m_uid;seed=w.m_seed;
        string folder=Path.Combine(Paths.ConfigPath,"RunicSettlements",world.ToString());
        Directory.CreateDirectory(folder);
        string planFile=Path.Combine(folder,"settlements.json"),cache=Path.Combine(folder,"surface.bin");
        var plan=SettlementPlan.Load(planFile,world,seed);
        var terrain=new GameTerrain();
        var locations=ZoneSystem.instance.m_locationInstances.Values.ToList();
        if(plan==null || plan.Complete && plan.Sites.Count==0)
        {
            var spawn=locations.FirstOrDefault(l=>l.m_location.m_prefabName=="StartTemple");
            if(spawn.m_location==null) throw new InvalidOperationException("Spawn monument location is unavailable");
            options.Water=ZoneSystem.instance.m_waterLevel;
            plan=new SettlementPlan {World=world,Seed=seed,Spawn=GameTerrain.PointOf(spawn.m_position),Options=options};
            plan.Save(planFile);
        }
        // A completed plan is immutable. Changed config applies only to an unprepared world.
        Status="Checking existing structures";
        Plugin.Report("Preparation: "+Status+" (nearby-location lookup)");
        yield return 0;
        var replaceable=SettlementRuins.ReplacementFilter(locations,plan.Options.ReplaceSmallRuins);
        var protection=new List<Exclusion>();
        foreach(int step in PreparedProtection.CaptureSteps(protection,12,replaceable)) yield return step;
        var exclusions=locations.Select(l=>new Exclusion {Center=GameTerrain.PointOf(l.m_position),Radius=Math.Max(8,l.m_location.m_exteriorRadius)+2,Replaceable=plan.Options.ReplaceSmallRuins && SettlementRuins.Minor(l.m_location)}).Concat(protection).ToList();
        if(!plan.Complete)
        {
            var planner=new SettlementPlanner(terrain,p=>WorldGenerator.instance.GetBiome(p.X,p.Z)==Heightmap.Biome.Meadows,exclusions);
            foreach(int step in planner.Build(plan,p=>p.Save(planFile))) {Status=planner.Status;yield return step;}
        }
        Plugin.Report(plan.Report);
        if(plan.Sites.Count==0)
        {
            File.WriteAllText(Path.Combine(folder,"settlements-report.txt"),plan.Report+" No terrain or decorations were created. Adjust the settings while logged out and retry.");
            Publish(new PreparedSurface {World=world,Seed=seed,Signature=100});yield break;
        }
        PreparedSurface surface;
        if(File.Exists(cache)) {Status="Loading saved settlement terrain";surface=PreparedSurface.Decode(File.ReadAllBytes(cache),world,seed);}
        else
        {
            var network=new WorldPaths {WorldId=world,Seed=seed,Islands=new List<Island>{new Island {Routes=plan.Roads}}};
            var o=plan.Options;
            var settings=new PlannerSettings {Width=o.Width,MaximumRaise=o.MaximumRaise,MaximumLower=o.MaximumLower,WaterLevel=o.Water};
            surface=new PreparedSurface {Signature=100};
            foreach(int step in surface.Build(network,new SettlementTerrain(terrain,plan),settings,protection)) {Status=$"Compiling paved roads: {surface.Tiles.Count} tiles";yield return step;}
            foreach(int step in SettlementSurface.AddSites(surface,plan,terrain)) {Status=$"Flattening {plan.Sites.Count} community clearings: {surface.Tiles.Count} tiles";yield return step;}
            surface.Save(cache);
        }
        
        Status="Replacing small generated ruins inside clearings";
        foreach(int step in SettlementRuins.Clear(plan,planFile)) yield return step;
        Status="Clearing existing obstacles before play";
        foreach(int step in PreparedObjects.Relocate(surface,folder,plan.Options.Radius+40)) yield return step;
        Status="Placing permanent roadside lights and perimeter trees";
        foreach(int step in SettlementDecorations.Prepare(plan,surface,terrain,planFile,exclusions.Where(e=>!e.Replaceable || !plan.Sites.Any(s=>Point.Distance(s.Center,e.Center)<plan.Options.Radius+plan.Options.Blend+e.Radius)).ToList())) yield return step;
        File.WriteAllLines(Path.Combine(folder,"settlements-report.txt"),new[]{plan.Report}.Concat(plan.Sites.Select(s=>$"{s.Name}: X={s.Center.X:F0}, Z={s.Center.Z:F0}, elevation={s.Height:F1}")).Concat(plan.Decorations.Where(d=>SettlementWayfinding.IsMarker(d.Id) && d.Id.EndsWith("-front",StringComparison.Ordinal)).Select(d=>$"{d.Id}: X={d.Position.X:F0}, Z={d.Position.Z:F0}; {d.Text.Replace('\n',' ')}")).Concat(plan.ReplacedLocations.Select(n=>"Replaced generated location: "+n)));
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
            rpc.InvokeRoutedRPC(peer,"RunicSettlements.PreparedData.1",packet);
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
        GUI.Box(new Rect(20,20,Math.Min(Screen.width-40,900),85),"Runic Settlements — preparing before play\n"+Status+"\nPreparation checkpoints resume after a restart. See BepInEx/LogOutput.log for details.");
    }
    private void Reset()
    {
        if(applyMilliseconds.Count>0)
        {applyMilliseconds.Sort();Plugin.Report($"Prepared terrain hook: {applyMilliseconds.Count} samples, p95={applyMilliseconds[(int)((applyMilliseconds.Count-1)*.95)]:F3} ms, max={applyMilliseconds.Last():F3} ms. Hook time excludes native mesh building and total frame time.");}
        SettlementDecorations.Current=null;SettlementDecorations.File=null;
        applyMilliseconds.Clear();job?.Dispose();job=null;Active=null;Ready=false;payload=null;session=null;rpc=null;error=null;
        download?.Dispose();download=null;transfers.Clear();lastRequests.Clear();Status="Waiting for world locations";
    }
    public void Dispose() {Reset();if(Current==this) Current=null;}
    internal static bool LegacyTest => false;
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
                Color road=Color.Lerp(c,v.Material==1?Heightmap.m_paintMaskPaved:Heightmap.m_paintMaskDirt,v.Paint/255f);road.a=c.a;mask.SetPixel(x,z,road);
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
