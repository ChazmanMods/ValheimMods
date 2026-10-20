using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths;

/// <summary>Journaled dirt roads: bounded grading, obstacle clearance, and reversible settings updates.</summary>
internal sealed class TerrainPainter
{
    private const string StampKey = "RunicPaths.Applied.v6";
    private static readonly FieldInfo ModifiedPaint = AccessTools.Field(typeof(TerrainComp), "m_modifiedPaint");
    private static readonly FieldInfo ModifiedHeight = AccessTools.Field(typeof(TerrainComp), "m_modifiedHeight");
    private static readonly FieldInfo PaintMask = AccessTools.Field(typeof(TerrainComp), "m_paintMask");
    private static readonly FieldInfo Instances = AccessTools.Field(typeof(ZNetScene), "m_instances");
    private static readonly FieldInfo AllPieces = AccessTools.Field(typeof(Piece), "s_allPieces");
    private static readonly MethodInfo Save = AccessTools.Method(typeof(TerrainComp), "Save", new[] { typeof(bool) });
    private static readonly MethodInfo CheckLoad = AccessTools.Method(typeof(TerrainComp), "CheckLoad");
    private static readonly Dictionary<Heightmap, float> FirstSeen = new Dictionary<Heightmap, float>();
    private readonly Heightmap map;
    private readonly ZonePaths paths;
    private readonly List<Piece> pieces = new List<Piece>();
    private readonly List<Exclusion> playerAreas = new List<Exclusion>();
    private readonly List<Bounds> generatedStructures = new List<Bounds>();
    private readonly List<(int Index, Point Point, float Strength)> pending = new List<(int, Point, float)>();
    public TerrainPainter(Heightmap map, ZonePaths paths) { this.map = map; this.paths = paths; }
    internal static void ResetCaches() => FirstSeen.Clear();
    internal static void ValidateBindings()
    {
        if (Instances == null || ModifiedPaint == null || ModifiedHeight == null || PaintMask == null || AllPieces == null || Save == null || CheckLoad == null)
            throw new MissingMemberException("TerrainComp paint/save bindings were not found.");
    }
    internal static bool NeedsWork(Heightmap map, int signature)
    {
        if (FirstSeen.Count > 256)
        {
            var unloaded = new List<Heightmap>();
            foreach (var pair in FirstSeen) if (!pair.Key) unloaded.Add(pair.Key!);
            foreach (var old in unloaded) FirstSeen.Remove(old);
        }
        if (!FirstSeen.TryGetValue(map, out float first)) { FirstSeen[map] = Time.time; return false; }
        // Let network objects, pieces, and terrain data arrive before examining a newly loaded zone.
        if (Time.time - first < 5 || !ZNetScene.instance.IsAreaReady(map.transform.position)) return false;
        var compiler = TerrainComp.FindTerrainCompiler(map.transform.position);
        if (!compiler) return true;
        var view = compiler.GetComponent<ZNetView>();
        return view && view.IsValid() && view.IsOwner() && view.GetZDO().GetInt(StampKey, 0) != signature;
    }
    private void RefreshProtection()
    {
        pieces.Clear(); playerAreas.Clear(); generatedStructures.Clear();
        float range = map.m_width * map.m_scale * 0.72f + paths.BuildBuffer + 10;
        // Heightmap transforms sit at Y=0 even on mountains. Protection must use horizontal distance.
        Point center = GameTerrain.PointOf(map.transform.position);
        foreach (var piece in (List<Piece>)AllPieces.GetValue(null))
            if (piece && Point.Distance(GameTerrain.PointOf(piece.transform.position), center) < range) pieces.Add(piece);
        foreach (var piece in pieces)
            if (piece && piece.GetCreator() != 0 && !RoadObjects.Movable(piece.gameObject)) playerAreas.Add(new Exclusion { Center = GameTerrain.PointOf(piece.transform.position), Radius = paths.BuildBuffer });
        foreach(var piece in pieces)
            if(piece && piece.GetCreator()==0 && !RoadObjects.Movable(piece.gameObject))
                foreach(var collider in piece.GetComponentsInChildren<Collider>())
                    if(collider.enabled && !collider.isTrigger && (RoadObjects.SolidMask & (1<<collider.gameObject.layer))!=0)
                        generatedStructures.Add(collider.bounds);
        // Old terrain modifiers remain separate objects rather than compiler edits.
        foreach (var modifier in TerrainModifier.GetAllInstances())
            if (modifier && modifier.m_playerModifiction && Point.Distance(GameTerrain.PointOf(modifier.transform.position), center) < range + modifier.GetRadius())
                playerAreas.Add(new Exclusion { Center = GameTerrain.PointOf(modifier.transform.position), Radius = modifier.GetRadius() + 2 });
    }
    private int ProtectionStamp()
    {
        unchecked {int h=17;foreach(var area in playerAreas) {h=h*31+area.Center.X.GetHashCode();h=h*31+area.Center.Z.GetHashCode();h=h*31+area.Radius.GetHashCode();}foreach(var area in generatedStructures) h=h*31+area.GetHashCode();return h;}
    }
    private bool Protected(Point p) => playerAreas.Exists(e => e.Contains(p)) || generatedStructures.Exists(b=>
        p.X>=b.min.x-2 && p.X<=b.max.x+2 && p.Z>=b.min.z-2 && p.Z<=b.max.z+2);

    private bool EditedNear(Point p, bool[] painted, bool[] changed)
    {
        map.WorldToVertexMask(GameTerrain.VectorOf(p), out int x, out int z);
        int pitch = map.m_width + 1;
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int xx = x + dx, zz = z + dz;
            if (xx < 0 || zz < 0 || xx >= pitch || zz >= pitch) continue;
            int i = zz * pitch + xx;
            if (changed[i]) return true;
        }
        return false;
    }

    internal static float ObstacleDistance(Collider obstacle, Vector3 surface)
    {
        // Unity rejects ClosestPoint on non-convex meshes and returns the input point.
        // That falsely gives distance zero everywhere. Use conservative bounds for those props.
        bool supported = obstacle is BoxCollider || obstacle is SphereCollider || obstacle is CapsuleCollider ||
            (obstacle is MeshCollider mesh && mesh.convex);
        Vector3 closest = supported ? obstacle.ClosestPoint(surface) : obstacle.bounds.ClosestPoint(surface);
        return Vector3.Distance(closest, surface);
    }
    public IEnumerable<int> Apply()
    {
        if(!map || !ZNetScene.instance || !ZNetScene.instance.IsAreaReady(map.transform.position)) yield break;
        var compiler=map.GetAndCreateTerrainCompiler(); if(!compiler) yield break;
        var view=compiler.GetComponent<ZNetView>();
        if(!view || !view.IsValid() || !view.IsOwner()) yield break;
        CheckLoad.Invoke(compiler,null);
        int pitch=map.m_width+1;
        var baseline=new TerrainState(compiler);
        var oldJournal=TerrainJournal.Load(view.GetZDO().GetByteArray(TerrainJournal.Key),baseline.Painted.Length);
        RefreshProtection();
        Point CellPoint(int i) => new Point(map.transform.position.x+(i%pitch-map.m_width/2)*map.m_scale,map.transform.position.z+(i/pitch-map.m_width/2)*map.m_scale);
        oldJournal.Restore(baseline,i=>Protected(CellPoint(i)));
        oldJournal.RestoreSeamHeights(baseline,i=>Grading.SeamWeight(CellPoint(i))==0);
        var candidates=new HashSet<ZNetView>();
        var blockers=new HashSet<Collider>();
        foreach(var segment in paths.Segments)
        {
            int steps=Math.Max(1,Mathf.CeilToInt(Point.Distance(segment.A,segment.B)/2));
            for(int step=0;step<=steps;step++)
            {
                if(!map || !view || !view.IsValid() || !view.IsOwner()) yield break;
                Point p=Point.Lerp(segment.A,segment.B,step/(float)steps);
                if(!map.GetWorldHeight(GameTerrain.VectorOf(p),out float h) || Protected(p)) continue;
                foreach(var hit in Physics.OverlapSphere(GameTerrain.VectorOf(p,h+1),paths.Width/2+2,RoadObjects.SolidMask,QueryTriggerInteraction.Ignore))
                {
                    if(!hit || hit.GetComponentInParent<Heightmap>() || hit.GetComponentInParent<Character>()) continue;
                    var candidate=hit.GetComponentInParent<ZNetView>();
                    if(!candidate || !candidate.IsValid()) continue;
                    Point root=GameTerrain.PointOf(candidate.transform.position);
                    if(RoadObjects.Movable(candidate.gameObject) && paths.Distance(root)<paths.Width/2+RoadObjects.Radius(candidate)+1 && !Protected(root)) candidates.Add(candidate);
                    else blockers.Add(hit);
                }
                yield return 0;
            }
        }
        // Pickable stones may only have trigger/item colliders. Enumerate actual world objects too.
        var loaded=((Dictionary<ZDO,ZNetView>)Instances.GetValue(ZNetScene.instance)).Values.ToArray();
        int inspected=0;
        foreach(var candidate in loaded)
        {
            if(++inspected%64==0) yield return 0;
            if(!map || !view || !view.IsValid() || !view.IsOwner()) yield break;
            if(!candidate || !candidate.IsValid()) continue;
            Vector3 location=candidate.transform.position;
            float reach=map.m_width*map.m_scale/2+16;
            if(Math.Abs(location.x-map.transform.position.x)>reach || Math.Abs(location.z-map.transform.position.z)>reach) continue;
            if(!RoadObjects.Movable(candidate.gameObject)) continue;
            Point root=GameTerrain.PointOf(location);
            if(paths.Distance(root)>paths.Width/2+RoadObjects.Radius(candidate)+1 || Protected(root)) continue;
            if(!map.GetWorldHeight(location,out float ground) || Math.Abs(location.y-ground)>paths.MaximumRaise+paths.MaximumLower+5) continue;
            candidates.Add(candidate);
        }
        var clear=new HashSet<ZNetView>();
        var moves=new Dictionary<ZNetView,Vector3>();
        var reserved=new List<(Vector3 Position,float Radius)>();
        int retained=0;
        foreach(var candidate in candidates)
        {
            if(!candidate || !candidate.IsValid()) continue;
            if(!candidate.IsOwner()) yield break;
            if(candidate.GetZDO().GetBool(Plugin.NaturalKey,false) && !RoadObjects.PlayerOwned(candidate)) clear.Add(candidate);
            else if(RoadObjects.FindSide(candidate,paths,Protected,reserved,out Vector3 destination)) moves.Add(candidate,destination);
            else
            {
                retained++;
                foreach(var hit in candidate.GetComponentsInChildren<Collider>())
                    if(!hit.isTrigger && (RoadObjects.SolidMask & (1<<hit.gameObject.layer))!=0) blockers.Add(hit);
            }
            yield return 0;
        }
        // No changes have been made yet. Reload after all yields to respect concurrent player edits.
        if(!map || !view || !view.IsValid() || !view.IsOwner()) yield break;
        CheckLoad.Invoke(compiler,null);
        RefreshProtection(); baseline=new TerrainState(compiler);
        var before=new TerrainState(baseline);
        oldJournal.Restore(baseline,i=>Protected(CellPoint(i)));
        oldJournal.RestoreSeamHeights(baseline,i=>Grading.SeamWeight(CellPoint(i))==0);
        var result=new TerrainState(baseline);
        var journal=new TerrainJournal();
        RefreshProtection();
        foreach(var candidate in candidates)
            if(candidate && candidate.IsValid() && (!candidate.IsOwner() || Protected(GameTerrain.PointOf(candidate.transform.position)))) yield break;
        int protectionStamp=ProtectionStamp();
        var revision=view.GetZDO().DataRevision;
        var objectPositions=new Dictionary<ZNetView,Vector3>();
        foreach(var candidate in candidates) if(candidate && candidate.IsValid()) objectPositions[candidate]=candidate.transform.position;
        var terrain=new GameTerrain();
        int painted=0,graded=0;
        for(int z=0;z<pitch;z++)
        {
        for(int x=0;x<pitch;x++)
        {
            // A full row can contain hundreds of route-segment comparisons per vertex.
            if(x%8==0)
            {
                yield return 0;
                if(!map || !view || !view.IsValid() || !view.IsOwner() || view.GetZDO().DataRevision!=revision) yield break;
            }
            int i=z*pitch+x;
            Point p=new Point(map.transform.position.x+(x-map.m_width/2)*map.m_scale,map.transform.position.z+(z-map.m_width/2)*map.m_scale);
            if(paths.Distance(p)>paths.Width/2+2 || Protected(p)) continue;
            float original=terrain.Height(p);
            // Leave water and steep banks alone; the planner emits separate roads on each bank.
            if(original<ZoneSystem.instance.m_waterLevel+0.4f) continue;
            bool blocked=false;
            Vector3 surface=GameTerrain.VectorOf(p,original+0.5f);
            foreach(var hit in blockers)
            {
                if(!hit) continue;
                var obstacle=hit.GetComponentInParent<ZNetView>();
                if(obstacle && (clear.Contains(obstacle) || moves.ContainsKey(obstacle))) continue;
                if(ObstacleDistance(hit,surface)<0.5f) {blocked=true;break;}
            }
            if(blocked) continue;
            float delta=Grading.Delta(terrain,paths,p,original);
            bool changeHeight=!EditedNear(p,baseline.Painted,baseline.Changed) && !baseline.Changed[i] && Math.Abs(delta)>0.001f && original+delta>=ZoneSystem.instance.m_waterLevel+0.4f;
            bool changePaint=!baseline.Painted[i] && paths.Strength(p)>0.02f;
            if(changeHeight) {result.Level[i]=delta;result.Smoothing[i]=0;result.Changed[i]=true;graded++;}
            if(changePaint)
            {
                Color dirt=Color.Lerp(baseline.Colors[i],Heightmap.m_paintMaskDirt,paths.Strength(p)); dirt.a=baseline.Colors[i].a;
                result.Colors[i]=dirt;result.Painted[i]=true;painted++;
            }
            journal.Record(i,baseline,result,changePaint,changeHeight);
        }
        yield return 0;
        if(!map || !view || !view.IsValid() || !view.IsOwner() || view.GetZDO().DataRevision!=revision) yield break;
        }
        RefreshProtection();if(protectionStamp!=ProtectionStamp()) yield break;
        foreach(var pair in objectPositions)
            if(pair.Key && pair.Key.IsValid() && (!pair.Key.IsOwner() || pair.Key.transform.position!=pair.Value)) yield break;
        bool paintOnly=before.Level.SequenceEqual(result.Level) && before.Smoothing.SequenceEqual(result.Smoothing);
        // Serialize before the indivisible native commit, with a budget checkpoint.
        byte[] journalBytes=journal.Serialize();
        yield return 0;
        if(!map || !view || !view.IsValid() || !view.IsOwner() || view.GetZDO().DataRevision!=revision) yield break;
        RefreshProtection();if(protectionStamp!=ProtectionStamp()) yield break;
        foreach(var pair in objectPositions)
            if(pair.Key && pair.Key.IsValid() && (!pair.Key.IsOwner() || pair.Key.transform.position!=pair.Value)) yield break;
        var commitClock=System.Diagnostics.Stopwatch.StartNew();
        // Commit without yielding: vegetation, terrain, and journal are one main-thread operation.
        foreach(var pair in moves)
        {
            if(!pair.Key || !pair.Key.IsValid()) continue;
            pair.Key.transform.position=pair.Value;
            pair.Key.GetZDO().SetPosition(pair.Value);
            pair.Key.GetZDO().Set(RoadObjects.MovedKey,true);
        }
        foreach(var candidate in clear) if(candidate && candidate.IsValid()) ZNetScene.instance.Destroy(candidate.gameObject);
        result.Commit(compiler);
        Save.Invoke(compiler,new object[]{false});
        view.GetZDO().Set(TerrainJournal.Key,journalBytes);
        view.GetZDO().Set(StampKey,paths.Signature);
        map.Poke(0,paintOnly);
        if(ClutterSystem.instance) ClutterSystem.instance.ResetGrass(map.transform.position,map.m_width*map.m_scale*0.5f);
        Plugin.Report($"Road {paths.Zone}: painted={painted}, graded={graded}, natural removed={clear.Count}, relocated={moves.Count}, retained without safe destination={retained}, native commit={commitClock.Elapsed.TotalMilliseconds:F1} ms, paint-only={paintOnly}.");
    }
}





