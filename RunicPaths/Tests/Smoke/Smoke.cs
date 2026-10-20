using System;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using RunicPaths.Core;
using Splatform;
using UnityEngine;

namespace RunicPaths.Smoke;

[BepInPlugin("com.chazman.runicpaths.smoke", "Runic Paths Isolated Smoke", "0.1.0")]
[BepInDependency(Plugin.Id)]
public sealed partial class Smoke : BaseUnityPlugin
{
    internal static Vector3? ForcedReference;
    private Plugin? plugin;
    private float next, start;
    private bool moved, done, objectClassificationChecked;
    private int stage;
    private Point target;
    private int expectedStamp;
    private float[]? beforeHeights;
    private int[]? beforePaint;
    private Heightmap? tracked;
    private Piece? protectedPiece;
    private Color[]? baseline;
    private float[]? originalHeights;
    private float nextDiagnostic;
    private float reproStarted;
    private string ResultFile => Path.Combine(Paths.GameRootPath, "smoke-result.txt");
    private void Awake()
    {
        if (Environment.GetEnvironmentVariable("RUNIC_PATHS_ISOLATED_SMOKE") != "1")
            throw new InvalidOperationException("This test plugin may only run in the isolated smoke runtime.");
        Utils.SetSaveDataPath(Path.Combine(Paths.GameRootPath, "saves"));
        SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
        // Dedicated servers normally force their local reference to (1000000,0,1000000).
        // Simulate a local observer ONLY in this isolated test so native terrain is instantiated.
        new Harmony("com.chazman.runicpaths.smoke.reference").PatchAll(typeof(Smoke).Assembly);
        // Regression: non-convex rock meshes must not report distance zero everywhere.
        var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(probe.GetComponent<BoxCollider>());
        var mesh = probe.AddComponent<MeshCollider>();
        mesh.sharedMesh = probe.GetComponent<MeshFilter>().sharedMesh;
        mesh.convex = false;
        Physics.SyncTransforms();
        var painter = typeof(Plugin).Assembly.GetType("RunicPaths.TerrainPainter");
        float distance = (float)AccessTools.Method(painter, "ObstacleDistance").Invoke(null, new object[] {mesh, new Vector3(5,0,0)});
        DestroyImmediate(probe);
        if (Math.Abs(distance - 4.5f) > 0.01f) throw new Exception("Non-convex obstacle distance regression: " + distance);
        Logger.LogInfo("PASS non-convex obstacle footprint regression.");
        start = Time.realtimeSinceStartup;
        if(File.Exists(Path.Combine(Paths.GameRootPath,"prepared.flag")))
        {
            var installed=FindFirstObjectByType<Plugin>();
            installed.Config["Preparation","Scope"].BoxedValue=File.Exists(Path.Combine(Paths.GameRootPath,"planning.flag"))?"SpawnIsland":"ExistingRoutes";
        }
        Logger.LogInfo("Isolated smoke harness loaded; no production world is used.");
    }
    private void Finish(string result)
    {
        done = true; Logger.LogInfo(result); File.WriteAllText(ResultFile, result);
        Application.Quit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
    }
    private void Update()
    {
        if (done) return;
        if (Time.realtimeSinceStartup - start > 600) { Finish("FAIL runtime smoke timed out at stage " + stage); return; }
        if (Time.realtimeSinceStartup < next) return; next = Time.realtimeSinceStartup + 0.5f;
        try
        {
            if (!ZNet.instance || !ZNetScene.instance || !ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;
            if (Time.realtimeSinceStartup >= nextDiagnostic)
            {
                nextDiagnostic = Time.realtimeSinceStartup + 15;
                Logger.LogInfo($"Smoke stage={stage}, reference={ZNet.instance.GetReferencePosition()}, maps={Heightmap.GetAllHeightmaps().Count}, timeScale={Time.timeScale}, connected={ZNet.GetConnectionStatus()}, simulation={ZNet.instance.GetSyncedSimulationDistance()}");
            }
            plugin ??= FindFirstObjectByType<Plugin>();
            if (plugin == null) return;
            if(File.Exists(Path.Combine(Paths.GameRootPath,"planning.flag")))
            {
                var runtimeType=typeof(Plugin).Assembly.GetType("RunicPaths.PreparedRuntime");
                var current=AccessTools.Field(runtimeType,"Current").GetValue(null);
                if(current!=null && (bool)AccessTools.Property(runtimeType,"Ready").GetValue(current))
                    Finish("PASS native spawn-island preparation completed in "+(Time.realtimeSinceStartup-start).ToString("F1")+" seconds including startup");
                return;
            }
            if(File.Exists(Path.Combine(Paths.GameRootPath,"prepared.flag"))) {RunPrepared();return;}
            if(!objectClassificationChecked)
            {
                var classifier=AccessTools.Method(typeof(Plugin).Assembly.GetType("RunicPaths.RoadObjects"),"Movable");
                var stone=ZNetScene.instance.GetPrefab("Pickable_Stone");var copper=ZNetScene.instance.GetPrefab("rock4_copper");
                if(!stone || !(bool)classifier.Invoke(null,new object[]{stone}) || (copper && (bool)classifier.Invoke(null,new object[]{copper})))
                {Finish("FAIL small stone / copper obstacle classification");return;}
                foreach(string name in new[]{"Beech_small1","Beech_small2"})
                {
                    var small=ZNetScene.instance.GetPrefab(name);
                    if(!small || !(bool)classifier.Invoke(null,new object[]{small})) {Finish("FAIL small tree classification: "+name);return;}
                    Logger.LogInfo(name+" components: "+string.Join(",",small.GetComponents<Component>().Select(c=>c.GetType().Name)));
                }
                objectClassificationChecked=true;Logger.LogInfo("PASS small loose stones included, copper deposit excluded.");
            }
            if ((bool)AccessTools.Field(typeof(Plugin), "faulted").GetValue(plugin)) { Finish("FAIL plugin faulted; inspect BepInEx log"); return; }
            var state = (WorldPaths?)AccessTools.Field(typeof(Plugin), "state").GetValue(plugin);
            if (!moved)
            {
                var spawn = ZoneSystem.instance.m_locationInstances.Values.First(x => x.m_location.m_prefabName == "StartTemple").m_position;
                ZNet.instance.SetReferencePosition(spawn); moved = true;
                ForcedReference = spawn;
                Logger.LogInfo("Smoke loading spawn terrain at " + spawn);
            }
            if (state == null || state.Islands.Count == 0) return;
            if (state.Islands[0].Routes.Count == 0) { Finish("FAIL spawn island had no routes"); return; }
            if (File.Exists(Path.Combine(Paths.GameRootPath, "road-v2.flag"))) { Advanced(state); return; }
            if (File.Exists(Path.Combine(Paths.GameRootPath, "repro.flag")))
            {
                if (reproStarted == 0) reproStarted = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - reproStarted < 45) return;
                var idx = new RouteIndex(); foreach (var island in state.Islands) idx.Add(island);
                int maps = 0, compilers = 0, paintedCells = 0, visibleCells = 0, nearbySegments = 0;
                foreach (var hmap in Heightmap.GetAllHeightmaps())
                {
                    if (!hmap || hmap.IsDistantLod) continue;
                    var zp = idx.Get(Cell.From(new Point(hmap.transform.position.x, hmap.transform.position.z),64),6,12);
                    if (zp.Segments.Count == 0) continue;
                    maps++; nearbySegments += zp.Segments.Count;
                    var tc = TerrainComp.FindTerrainCompiler(hmap.transform.position);
                    if (!tc) continue;
                    compilers++;
                    var flags = (bool[])AccessTools.Field(typeof(TerrainComp), "m_modifiedPaint").GetValue(tc);
                    paintedCells += flags.Count(p => p);
                    for (int z = 0; z <= hmap.m_width; z++)
                    for (int x = 0; x <= hmap.m_width; x++)
                    {
                        var point = new Point(hmap.transform.position.x + (x-hmap.m_width/2)*hmap.m_scale,hmap.transform.position.z+(z-hmap.m_width/2)*hmap.m_scale);
                        if (zp.Strength(point)>0.5f && hmap.IsCleared(new Vector3(point.X+0.5f,0,point.Z+0.5f))) visibleCells++;
                    }
                }
                Finish($"{(visibleCells>300 ? "PASS" : "FAIL")} unassisted spawn reproduction: route maps={maps}, nearby segments={nearbySegments}, compilers={compilers}, saved paint cells={paintedCells}, visible cleared route cells={visibleCells}. No test-created terrain compiler or player piece.");
                return;
            }
            if (stage == 0)
            {
                Setting("Terrain","MaximumRaise",0);Setting("Terrain","MaximumLower",0);
                Route route = state.Islands[0].Routes.OrderByDescending(r => r.Points.Count).First();
                target = route.Points[route.Points.Count / 2];
                var gen = WorldGenerator.instance; float h = gen.GetHeight(target.X, target.Z);
                ZNet.instance.SetReferencePosition(new Vector3(target.X, h + 1, target.Z)); stage = 1;
                ForcedReference = new Vector3(target.X, h + 1, target.Z);
                Logger.LogInfo($"Smoke following generated route at {target.X}, {target.Z}; routes={state.Islands[0].Routes.Count}");
            }
            var map = Heightmap.FindHeightmap(new Vector3(target.X, 0, target.Z));
            if (!map) return;
            if (!ZNetScene.instance.IsAreaReady(map.transform.position)) return;
            var comp = map.GetAndCreateTerrainCompiler();
            if (!comp) return;
            var view = comp.GetComponent<ZNetView>();
            if (protectedPiece == null)
            {
                map.GetWorldHeight(new Vector3(target.X, 0, target.Z), out float ground);
                var prefab = ZNetScene.instance.GetPrefab("woodwall");
                if (!prefab) prefab = ZNetScene.instance.GetPrefab("wood_wall");
                if (!prefab) { Finish("FAIL smoke wood wall prefab unavailable"); return; }
                protectedPiece = Instantiate(prefab, new Vector3(target.X, ground, target.Z), Quaternion.identity).GetComponent<Piece>();
                protectedPiece.SetCreator(123456, PlatformUserID.None);
                var migrationIndex = new RouteIndex(); foreach (var island in state.Islands) migrationIndex.Add(island);
                var migrationZone = migrationIndex.Get(Cell.From(new Point(map.transform.position.x,map.transform.position.z),64),6,12);
                view.GetZDO().Set("RunicPaths.Applied.v1", migrationZone.Signature);
                baseline = ((Color[])AccessTools.Field(typeof(TerrainComp), "m_paintMask").GetValue(comp)).ToArray();
                originalHeights = ((float[])AccessTools.Field(typeof(TerrainComp), "m_levelDelta").GetValue(comp)).Concat((float[])AccessTools.Field(typeof(TerrainComp), "m_smoothDelta").GetValue(comp)).ToArray();
                Logger.LogInfo("Smoke placed a player-owned wall on the planned route to verify build protection.");
                return;
            }
            int stamp = view.GetZDO().GetInt("RunicPaths.Applied.v6", 0);
            if (stamp == 0) return;
            bool[] painted = (bool[])AccessTools.Field(typeof(TerrainComp), "m_modifiedPaint").GetValue(comp);
            float[] levels = (float[])AccessTools.Field(typeof(TerrainComp), "m_levelDelta").GetValue(comp);
            float[] smooth = (float[])AccessTools.Field(typeof(TerrainComp), "m_smoothDelta").GetValue(comp);
            Color[] mask = (Color[])AccessTools.Field(typeof(TerrainComp), "m_paintMask").GetValue(comp);
            if (!painted.Any(p => p)) { Finish("FAIL stamped terrain without any paint"); return; }
            if (stage == 1)
            {
                if (!originalHeights!.SequenceEqual(levels.Concat(smooth))) { Finish("FAIL dirt path changed terrain heights"); return; }
                int pitch = map.m_width + 1;
                for (int z = 0; z < pitch; z++)
                for (int x = 0; x < pitch; x++)
                {
                    var p = new Point(map.transform.position.x + (x - map.m_width / 2) * map.m_scale, map.transform.position.z + (z - map.m_width / 2) * map.m_scale);
                    if (Point.Distance(p, target) < 12 && baseline![z * pitch + x] != mask[z * pitch + x])
                    { Finish("FAIL painted inside player build buffer"); return; }
                }
                tracked = map; expectedStamp = stamp;
                beforeHeights = levels.Concat(smooth).ToArray();
                beforePaint = mask.Select(c => c.GetHashCode()).ToArray(); stage = 2;
                next = Time.realtimeSinceStartup + 12;
                Logger.LogInfo("Smoke found painted terrain; checking repeat application stability.");
                return;
            }
            if (stage == 2)
            {
                if (map != tracked || stamp != expectedStamp || !beforeHeights!.SequenceEqual(levels.Concat(smooth)) || !beforePaint!.SequenceEqual(mask.Select(c => c.GetHashCode())))
                { Finish("FAIL terrain changed during repeated scan"); return; }
                Finish($"PASS runtime: world generated; {state.Islands[0].Routes.Count} spawn-island paths saved; terrain painted ({painted.Count(p => p)} cells); player-built wall buffer preserved; heights unchanged; v1 completion stamp migrated to v3; repeated scans stable. Elapsed {Time.realtimeSinceStartup - start:F1}s. Multiplayer clients and visual quality still require playtesting.");
            }
        }
        catch (Exception e) { Finish("FAIL " + e); }
    }
}

[HarmonyPatch(typeof(ZNet), nameof(ZNet.GetReferencePosition))]
internal static class TestObserver
{
    private static void Postfix(ref Vector3 __result)
    { if (Smoke.ForcedReference.HasValue) __result = Smoke.ForcedReference.Value; }
}








