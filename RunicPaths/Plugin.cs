using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths;

[BepInPlugin(Id, "Runic Paths", Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.chazman.runicpaths";
    public const string Version = "0.3.1";
    private PreparedRuntime? prepared;
    internal static Plugin? Instance;
    internal const string NaturalKey = "RunicPaths.Natural";
    private Harmony? harmony;
    private new ConfigEntry<bool> enabled = null!;
    private ConfigEntry<float> width = null!, buildBuffer = null!, budget = null!, slope = null!;
    private ConfigEntry<float> maximumRaise = null!, maximumLower = null!, relocationDistance = null!, maximumGap = null!;
    private bool settingsDirty;
    private DateTime configWriteTime;
    private float nextConfigPoll;
    private ConfigEntry<int> maxCells = null!, maxPois = null!, maxSearch = null!;
    private ConfigEntry<string> poiPatterns = null!, confirmedNaturalWorld = null!;
    private bool baselineApplied;
    private readonly Dictionary<Cell,ZonePaths> clutterRoads=new Dictionary<Cell,ZonePaths>();
    private ZNet? session;
    private ZRoutedRpc? rpc;
    private WorldPaths? state;
    private RouteIndex index = new RouteIndex();
    private readonly GameTerrain terrain = new GameTerrain();
    private readonly HashSet<Cell> failedLandings = new HashSet<Cell>();
    private readonly Queue<Point> landingQueue = new Queue<Point>();
    private readonly HashSet<Cell> queuedLandings = new HashSet<Cell>();
    private readonly Dictionary<Cell, ZonePaths> receivedZones = new Dictionary<Cell, ZonePaths>();
    private readonly Dictionary<Cell, float> requestedZones = new Dictionary<Cell, float>();
    private readonly Dictionary<long, float> requestTimes = new Dictionary<long, float>();
    private Island? replacingIsland;
    private readonly HashSet<Island> failedReplans=new HashSet<Island>();
    private int planningSignature;
    private Planner? planner;
    private IEnumerator<int>? work;
    private TerrainPainter? painter;
    private IEnumerator<int>? paintWork;
    private string savePath = "";
    private float nextDiscovery, nextTerrain, nextStatus;
    private bool faulted, spawnConsidered;
    private Cell planningLanding;

    private void Awake()
    {
        Instance = this;
        prepared = new PreparedRuntime(this, Config);
        enabled = Config.Bind("General", "Enabled", true, "Enable island discovery and path application. Disabling does not undo saved terrain.");
        width = Config.Bind("Paths", "Width", 3f, new ConfigDescription("Dirt path width in metres. Prepared roads rebuild before spawning on the next world load.", new AcceptableValueRange<float>(2, 12)));
        buildBuffer = Config.Bind("Protection", "PlayerBuildBuffer", 12f, new ConfigDescription("Metres protected around player pieces, excluding trees and stones which are relocated off the road.", new AcceptableValueRange<float>(6, 40)));
        budget = Config.Bind("Performance", "WorkMilliseconds", 3f, new ConfigDescription("Legacy test painter budget; unused by prepared mode. See Preparation / MillisecondsPerFrame.", new AcceptableValueRange<float>(1, 8)));
        slope = Config.Bind("Paths", "MaximumSlope", 0.55f, new ConfigDescription("Maximum rise/run; 0.55 is about 29 degrees.", new AcceptableValueRange<float>(0.2f, 0.8f)));
        maxCells = Config.Bind("Performance", "MaximumIslandCells", 80000, new ConfigDescription("24 m survey cells before an island is deferred. No partial network is saved.", new AcceptableValueRange<int>(1000, 160000)));
        maxPois = Config.Bind("Paths", "MaximumPOIsPerIsland", 40, new ConfigDescription("Legacy setting; prepared mode considers all matching POIs without this cap.", new AcceptableValueRange<int>(2, 80)));
        maxSearch = Config.Bind("Performance", "MaximumRouteSearchNodes", 16000, new ConfigDescription("Work limit per attempted connection. Unsuccessful connections are recorded in coverage.txt; raise for complicated terrain.", new AcceptableValueRange<int>(2000, 60000)));
        poiPatterns = Config.Bind("Paths", "POIs", "StartTemple,WoodHouse*,StoneHouse*,StoneTower*,Ruin*,Runestone*,Crypt*,TrollCave*,Dolmen*,StoneHenge*,Grave*", "Legacy selector. Prepared mode uses Preparation / POIs.");
        confirmedNaturalWorld=Config.Bind("Protection","ConfirmExistingNaturalWorld","","World UID confirmed to have no player-planted/placed roadside trees or stones. Captures existing object IDs once. Player-marked objects and future unmarked objects remain protected.");
        maximumRaise = Config.Bind("Terrain", "MaximumRaise", 1.5f, new ConfigDescription("Maximum metres above original ground. Set 0 to prevent raising. Changes rebuild prepared roads before the next spawn.",new AcceptableValueRange<float>(0,8)));
        maximumLower = Config.Bind("Terrain", "MaximumLower", 1.5f, new ConfigDescription("Maximum metres below original ground. Set 0 to prevent digging. Changes rebuild prepared roads before the next spawn.",new AcceptableValueRange<float>(0,8)));
        relocationDistance = Config.Bind("Protection", "MaximumRelocationDistance", 24f, new ConfigDescription("Maximum search distance beside the road for player/unknown trees and stones. Retain objects if no safe destination exists.",new AcceptableValueRange<float>(4,48)));
        maximumGap = Config.Bind("Paths", "MaximumGap", 32f, new ConfigDescription("Maximum river/ravine gap in metres. Roads end at each bank without a bridge. Changes take effect during preparation on the next world load.",new AcceptableValueRange<float>(0,64)));
        Config.SettingChanged += (_,__) => settingsDirty=true;
        configWriteTime=File.GetLastWriteTimeUtc(Config.ConfigFilePath);
        try
        {
            TerrainPainter.ValidateBindings();
            harmony = new Harmony(Id); harmony.PatchAll();
            Logger.LogInfo($"Runic Paths {Version}: dirt trails; no map exploration changes.");
        }
        catch (Exception e) { faulted = true; Logger.LogError("Unsupported game API; paths disabled: " + e); }
    }

    private void ResetSession()
    {
        work?.Dispose(); paintWork?.Dispose(); work = null; paintWork = null; planner = null; painter = null;
        baselineApplied=false;clutterRoads.Clear();
        replacingIsland=null;failedReplans.Clear();
        session = null; rpc = null; state = null; index = new RouteIndex();
        receivedZones.Clear(); requestedZones.Clear(); requestTimes.Clear(); failedLandings.Clear();
        landingQueue.Clear(); queuedLandings.Clear();
        TerrainPainter.ResetCaches(); scanCursor = 0;
        spawnConsidered = false; nextDiscovery = nextTerrain = nextStatus = 0;
    }

    private void OnDestroy() { prepared?.Dispose(); ResetSession(); harmony?.UnpatchSelf(); if (Instance == this) Instance = null; }

    private void OnGUI() => prepared?.DrawStatus();

    private void Update()
    {
        // The legacy painter remains available for isolated regression tests only.
        // Production gameplay never enters its player-following mutation loop.
        if (!PreparedRuntime.LegacyTest)
        {
            prepared?.Update(enabled.Value && !faulted);
            return;
        }
        if(Time.unscaledTime>=nextConfigPoll)
        {
            nextConfigPoll=Time.unscaledTime+2;
            DateTime modified=File.GetLastWriteTimeUtc(Config.ConfigFilePath);
            if(modified!=configWriteTime) {try {Config.Reload();configWriteTime=modified;} catch(Exception e) {Logger.LogWarning("Config reload deferred: "+e.Message);}}
        }
        if(settingsDirty)
        {
            settingsDirty=false; failedReplans.Clear(); failedLandings.Clear(); paintWork?.Dispose();paintWork=null;painter=null;nextTerrain=0;
            if(work!=null && planningSignature!=PlanningSignature())
            {
                bool wasNew=replacingIsland==null; Point retry=planningLanding.At(PlannerSettings.IslandGrid);
                work.Dispose();work=null;planner=null;replacingIsland=null;
                if(wasNew) {spawnConsidered=false;QueueLanding(retry);}
            }
            Logger.LogInfo("Road settings changed; existing roads will update as their terrain loads.");
        }
        if (!enabled.Value || faulted) return;
        if (!ZNet.instance || !ZoneSystem.instance || WorldGenerator.instance == null || !ZNetScene.instance)
        { if (session) ResetSession(); return; }
        try
        {
            if (session != ZNet.instance)
            {
                ResetSession(); session = ZNet.instance;
                if (session.IsServer())
                {
                    var world = session.GetWorld();
                    if (world == null || world.m_menu) { session = null; return; }
                    savePath = Path.Combine(Paths.ConfigPath, "RunicPaths", world.m_uid + ".json");
                    state = Persistence.Load(savePath, world.m_uid, world.m_seed);
                    foreach (Island island in state.Islands) index.Add(island);
                    Logger.LogInfo($"Loaded {state.Islands.Count} saved island networks.");
                }
            }
            if(session.IsServer() && state!=null && !baselineApplied && confirmedNaturalWorld.Value.Trim()==state.WorldId.ToString() && ZoneSystem.instance.LocationsGenerated)
            {
                NaturalBaseline.Apply(Path.Combine(Paths.ConfigPath,"RunicPaths",state.WorldId+".natural-baseline.txt"),state.WorldId);
                baselineApplied=true;
            }
            if (rpc != ZRoutedRpc.instance && ZRoutedRpc.instance != null)
            {
                rpc = ZRoutedRpc.instance;
                rpc.Register<ZPackage>("RunicPaths.Query.v3", ReceiveQuery);
                rpc.Register<ZPackage>("RunicPaths.Zone.v3", ReceiveZone);
            }
            if (session.IsServer() && state != null && ZoneSystem.instance.LocationsGenerated && Time.time >= nextDiscovery)
            { nextDiscovery = Time.time + 4; Discover(); }
            if(session.IsServer() && state!=null && work==null && ZoneSystem.instance.LocationsGenerated)
            {
                int signature=PlanningSignature();
                var outdated=state.Islands.FirstOrDefault(i=>i.PlanningSignature!=signature && i.Cells.Count>0 && !failedReplans.Contains(i));
                if(outdated!=null) BeginIsland(outdated.Cells[0].At(PlannerSettings.IslandGrid),outdated);
            }
            // Remember new landings even while another island is being planned.
            for (int tries = 0; work == null && landingQueue.Count > 0 && tries < 16; tries++)
            {
                Point landing = landingQueue.Dequeue();
                queuedLandings.Remove(Cell.From(landing, PlannerSettings.IslandGrid));
                BeginIsland(landing);
            }
            var watch = Stopwatch.StartNew();
            if (paintWork == null && Time.time >= nextTerrain)
            { nextTerrain = Time.time + 0.25f; ScheduleTerrain(); }
            if (work != null)
            {
                while (watch.Elapsed.TotalMilliseconds < budget.Value * (paintWork!=null ? 0.2 : 0.65))
                    if (!work.MoveNext()) { CompleteIsland(); break; }
                if (Time.time >= nextStatus && planner != null)
                { nextStatus = Time.time + 30; Logger.LogInfo($"{planner.Status}; {planner.HeightSamples} sampled terrain points, {planner.SearchNodes} current search nodes."); }
            }

            while (paintWork != null && watch.Elapsed.TotalMilliseconds < budget.Value)
                if (!paintWork.MoveNext()) { paintWork.Dispose(); paintWork = null; painter = null; }
        }
        catch (Exception e)
        {
            faulted = true; work?.Dispose(); paintWork?.Dispose(); work = null; paintWork = null;
            Logger.LogError("Paths stopped to avoid partial/repeated world changes. Restart after addressing this error: " + e);
        }
    }

    private bool Included(string name)
    {
        foreach (string raw in poiPatterns.Value.Split(','))
        {
            string pattern = raw.Trim();
            if (pattern.EndsWith("*", StringComparison.Ordinal) ? name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase) : name.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void Discover()
    {
        if (!spawnConsidered)
        {
            spawnConsidered = true;
            foreach (var loc in ZoneSystem.instance.m_locationInstances.Values)
                if (loc.m_location.m_prefabName == "StartTemple") { QueueLanding(GameTerrain.PointOf(loc.m_position)); break; }
        }
        foreach (ZDO character in session!.GetAllCharacterZDOS())
        {
            Vector3 p = character.GetPosition(); Point point = GameTerrain.PointOf(p);
            // Reject ships on water, airborne spawn, and dungeon interiors. Walkable dry landing only.
            float ground = terrain.Height(point);
            if (ground < ZoneSystem.instance.m_waterLevel + 1.2f || Math.Abs(p.y - ground) > 5) continue;
            QueueLanding(point);
        }
    }

    private void QueueLanding(Point point)
    {
        Cell cell = Cell.From(point, PlannerSettings.IslandGrid);
        if (index.ActivatedCells.Contains(cell) || failedLandings.Contains(cell) || queuedLandings.Contains(cell) ||
            (planner != null && planner.SurveyedCells.Contains(cell))) return;
        queuedLandings.Add(cell); landingQueue.Enqueue(point);
    }

    private bool BeginIsland(Point landing, Island? replacing = null)
    {
        Cell cell = Cell.From(landing, PlannerSettings.IslandGrid);
        if (replacing==null && (index.ActivatedCells.Contains(cell) || failedLandings.Contains(cell))) return false;
        // A shoreline position may round to a wet grid cell. Check adjacent activated land without crossing water.
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        {
            Cell near = new Cell(cell.X + x, cell.Z + z);
            if (replacing==null && index.ActivatedCells.Contains(near))
            {
                var check = new Planner(terrain, Settings());
                if (check.SafeSegment(landing, near.At(PlannerSettings.IslandGrid), false)) return false;
            }
        }
        var pois = new List<Poi>(); var protect = new List<Exclusion>();
        foreach (var loc in ZoneSystem.instance.m_locationInstances.Values)
        {
            float radius = Math.Max(8, loc.m_location.m_exteriorRadius);
            var point = GameTerrain.PointOf(loc.m_position);
            protect.Add(new Exclusion { Center = point, Radius = radius });
            if (Included(loc.m_location.m_prefabName) && terrain.Allowed(point))
                pois.Add(new Poi { Name = loc.m_location.m_prefabName, Position = point, Radius = radius });
        }
        replacingIsland=replacing;planningSignature=PlanningSignature();
        planner = new Planner(terrain, Settings(), protect); planningLanding = cell;
        work = planner.Build(landing, pois).GetEnumerator(); nextStatus = Time.time + 30;
        Logger.LogInfo("New island reached; planning its dirt path network in the background.");
        return true;
    }

    private int PlanningSignature()
    {
        unchecked
        {
            int value=27;
            foreach(char c in poiPatterns.Value) value=value*31+c;
            foreach(float v in new[]{maximumGap.Value,slope.Value,maxPois.Value,maxCells.Value,maxSearch.Value,maximumRaise.Value,maximumLower.Value}) value=value*31+v.GetHashCode();
            return value==0?1:value;
        }
    }
    private PlannerSettings Settings() => new PlannerSettings { WaterLevel = ZoneSystem.instance.m_waterLevel, Width = width.Value, MaxSlope = slope.Value, MaxIslandCells = maxCells.Value, MaxPois = maxPois.Value, MaxSearchNodes = maxSearch.Value, MaximumGap=maximumGap.Value, MaximumRaise=maximumRaise.Value, MaximumLower=maximumLower.Value };

    private void CompleteIsland()
    {
        work!.Dispose(); work = null;
        Island? result = planner!.Result;
        if (result == null)
        {
            if(replacingIsland!=null) failedReplans.Add(replacingIsland);
            replacingIsland=null;
            failedLandings.Add(planningLanding);
            foreach (Cell c in planner.SurveyedCells) failedLandings.Add(c);
            Logger.LogWarning(planner.Status); planner = null; return;
        }
        if(result.Routes.Count==0 && replacingIsland!=null && replacingIsland.Routes.Count>0)
        {
            failedReplans.Add(replacingIsland);
            Logger.LogWarning("No replacement routes were found within the planning limits. Existing roads retained; raise MaximumRouteSearchNodes or adjust gap/POI settings to retry.");
            replacingIsland=null;planner=null;return;
        }
        result.PlanningSignature=planningSignature;
        var previous=state!.Islands.ToList();
        var resultCells=new HashSet<Cell>(result.Cells);
        var affected=previous.Where(i=>i==replacingIsland || i.Cells.Any(c=>resultCells.Contains(c))).ToList();
        result=NetworkUpdate.Preserve(result,affected);
        state.Islands.RemoveAll(i=>affected.Contains(i));
        state.Islands.Add(result);
        try {Persistence.Save(savePath,state);}
        catch {state.Islands=previous;throw;}
        replacingIsland=null;index=new RouteIndex();foreach(var island in state.Islands) index.Add(island);
        // Reducing MaximumGap can split an activated network. Replan every already-known fragment.
        foreach(var old in previous)
            foreach(var cell in old.Cells)
                if(!index.ActivatedCells.Contains(cell)) QueueLanding(cell.At(PlannerSettings.IslandGrid));
        Logger.LogInfo(planner.Status + $". Extended network saved ({result.Routes.Count} sections); established roads preserved.");
        planner = null;
    }

    private void ScheduleTerrain()
    {
        if (rpc == null || session == null || (!session.IsServer() && !Player.m_localPlayer)) return;
        // Do this before any early return from a query or paint job.
        if (requestedZones.Count > 256)
        {
            foreach (Cell c in requestedZones.Where(p => Time.time - p.Value > 120).Select(p => p.Key).ToList())
            { requestedZones.Remove(c); receivedZones.Remove(c); }
        }
        Vector3 reference = session.GetReferencePosition();
        var maps = Heightmap.GetAllHeightmaps().Where(m=>m).OrderBy(m=>
                (new Vector2(m.transform.position.x-reference.x,m.transform.position.z-reference.z)).sqrMagnitude).ToList();
        // Finish nearby loaded roads before spending time on distant tiles.
        for (int offset = 0; offset < maps.Count; offset++)
        {
            int i = offset;
            var map = maps[i]; if (!map || map.IsDistantLod || Vector3.Distance(map.transform.position, new Vector3(reference.x, map.transform.position.y, reference.z)) > 150) continue;
            Cell cell = Cell.From(GameTerrain.PointOf(map.transform.position), 64);
            ZonePaths? zone;
            if (session.IsServer()) zone = GetZone(cell);
            else
            {
                receivedZones.TryGetValue(cell, out zone);
                if ((!requestedZones.TryGetValue(cell, out float last) || Time.time - last > 20) && rpc != null)
                {
                    // One request per scan; retry negative responses after new island planning finishes.
                    var request = new ZPackage(); request.Write(cell.X); request.Write(cell.Z);
                    rpc.InvokeRoutedRPC("RunicPaths.Query.v3", request); requestedZones[cell] = Time.time;
                    scanCursor = (i + 1) % maps.Count;
                    if (zone == null || zone.Segments.Count == 0) return;
                }
            }
            if(zone==null) continue;
            bool firstClutterVisit=!clutterRoads.TryGetValue(cell,out var cachedClutter) || cachedClutter.Signature!=zone.Signature;
            clutterRoads[cell]=zone;
            if(firstClutterVisit && zone.Segments.Count>0 && ClutterSystem.instance) ClutterSystem.instance.ResetGrass(map.transform.position,map.m_width*map.m_scale*0.5f);
            if(zone.Segments.Count==0)
            {
                var existing=TerrainComp.FindTerrainCompiler(map.transform.position);
                if(!existing || existing.GetComponent<ZNetView>().GetZDO().GetByteArray(TerrainJournal.Key)==null) continue;
            }
            if(!TerrainPainter.NeedsWork(map,zone.Signature)) continue;
            painter = new TerrainPainter(map, zone);
            paintWork = painter.Apply().GetEnumerator(); scanCursor = (i + 1) % maps.Count;
            return;
        }
    }
    private ZonePaths GetZone(Cell cell)
    {
        var zone=index.Get(cell,width.Value,buildBuffer.Value);
        zone.MaximumRaise=maximumRaise.Value;zone.MaximumLower=maximumLower.Value;zone.RelocationDistance=relocationDistance.Value;zone.ClearanceRevision=state!=null && confirmedNaturalWorld.Value.Trim()==state.WorldId.ToString()?1:0;
        zone.ComputeSignature();return zone;
    }
    private int scanCursor;

    private void ReceiveQuery(long sender, ZPackage package)
    {
        if (!enabled.Value || faulted || session == null || !session.IsServer() || state == null || rpc == null) return;
        var peer = session.GetPeers().Find(p => p.m_uid == sender && p.IsReady());
        if (peer == null) return;
        if (requestTimes.TryGetValue(sender, out float last) && Time.time - last < 0.2f) return;
        requestTimes[sender] = Time.time;
        try
        {
            Cell c = new Cell(package.ReadInt(), package.ReadInt());
            if (Math.Abs((long)c.X) > 160 || Math.Abs((long)c.Z) > 160 || Point.Distance(c.At(64), GameTerrain.PointOf(peer.m_refPos)) > 192) return;
            ZonePaths zone = GetZone(c);
            var reply = new ZPackage(); reply.Write(3); reply.Write(session.GetWorldUID());
            reply.Write(c.X); reply.Write(c.Z); reply.Write(zone.Width); reply.Write(zone.BuildBuffer); reply.Write(zone.MaximumRaise);reply.Write(zone.MaximumLower);reply.Write(zone.RelocationDistance);reply.Write(zone.ClearanceRevision);
            reply.Write(zone.Segments.Count);
            foreach (var s in zone.Segments) { reply.Write(s.A.X); reply.Write(s.A.Z); reply.Write(s.B.X); reply.Write(s.B.Z); }
            reply.Write(zone.Exclusions.Count);
            foreach (var e in zone.Exclusions) { reply.Write(e.Center.X); reply.Write(e.Center.Z); reply.Write(e.Radius); }
            rpc.InvokeRoutedRPC(sender, "RunicPaths.Zone.v3", reply);
        }
        catch (Exception e) { Logger.LogWarning("Rejected path query: " + e.Message); }
    }

    private void ReceiveZone(long sender, ZPackage package)
    {
        if (!enabled.Value || faulted || session == null || session.IsServer() || session.GetServerPeer()?.m_uid != sender) return;
        try
        {
            if (package.ReadInt() != 3 || package.ReadLong() != session.GetWorldUID()) return;
            Cell c = new Cell(package.ReadInt(), package.ReadInt());
            if (!requestedZones.ContainsKey(c)) return;
            var zone = new ZonePaths { Zone = c, Width = package.ReadSingle(), BuildBuffer = package.ReadSingle(), MaximumRaise=package.ReadSingle(), MaximumLower=package.ReadSingle(), RelocationDistance=package.ReadSingle(), ClearanceRevision=package.ReadInt() };
            if (!(zone.Width >= 2 && zone.Width <= 12 && zone.BuildBuffer >= 6 && zone.BuildBuffer <= 40 && zone.MaximumRaise>=0 && zone.MaximumRaise<=8 && zone.MaximumLower>=0 && zone.MaximumLower<=8 && zone.RelocationDistance>=4 && zone.RelocationDistance<=48 && zone.ClearanceRevision>=0 && zone.ClearanceRevision<=1)) return;
            int count = package.ReadInt(); if (count < 0 || count > 8192) return;
            for (int i = 0; i < count; i++)
            {
                var a = new Point(package.ReadSingle(), package.ReadSingle()); var b = new Point(package.ReadSingle(), package.ReadSingle());
                if (!Finite(a) || !Finite(b) || Point.Distance(a, c.At(64)) > 128 || Point.Distance(b, c.At(64)) > 128) return;
                zone.Segments.Add((a, b));
            }
            count = package.ReadInt(); if (count < 0 || count > 2048) return;
            for (int i = 0; i < count; i++)
            {
                var e = new Exclusion { Center = new Point(package.ReadSingle(), package.ReadSingle()), Radius = package.ReadSingle() };
                if (!Finite(e.Center) || !(e.Radius >= 0 && e.Radius < 10000)) return;
                zone.Exclusions.Add(e);
            }
            zone.ComputeSignature(); receivedZones[c] = zone;
        }
        catch (Exception e) { Logger.LogWarning("Rejected path response: " + e.Message); }
    }
    private static bool Finite(Point p) => !float.IsNaN(p.X) && !float.IsInfinity(p.X) && !float.IsNaN(p.Z) && !float.IsInfinity(p.Z);

    internal static bool SuppressRoadClutter(Vector3 point, Heightmap map)
    {
        if(Instance==null || !Instance.enabled.Value || Instance.faulted || !map) return false;
        if (PreparedRuntime.Active != null) return PreparedRuntime.Active.Cleared(GameTerrain.PointOf(point)) && map.IsCleared(point);
        Point p=GameTerrain.PointOf(point);
        return Instance.clutterRoads.TryGetValue(Cell.From(p,64),out var road) && road.Strength(p)>0.1f && map.IsCleared(point);
    }
    internal static bool CanTagNature => Instance != null && Instance.enabled.Value && !Instance.faulted;
    internal static void Report(string message) => Instance?.Logger.LogInfo(message);
}













