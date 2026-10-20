using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths.Smoke;

public sealed partial class Smoke
{
    private bool ClearingRun => System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.GameRootPath,"clearing.flag"));
    private ZNetView? laterStone;
    private Vector3 laterOrigin;
    private int editedHeightIndex=-1;
    private int advancedStage,firstCount,widerCount,lastStamp,externalIndex=-1;
    private ZNetView? naturalTree,naturalStone,playerTree,playerStone;
    private Vector3 originalTree,originalStone;
    private Heightmap? advancedMap;
    private float[]? stableHeight;
    private Color[]? stableMask;
    private void Setting(string section,string name,float value)
    {plugin!.Config[new ConfigDefinition(section,name)].BoxedValue=value;}
    private ZNetView SpawnObject(string prefab,Vector3 position,bool natural,bool player)
    {
        var original=ZNetScene.instance.GetPrefab(prefab); if(!original) throw new Exception("Missing prefab "+prefab);
        var obj=Instantiate(original,position,Quaternion.identity).GetComponent<ZNetView>();
        obj.GetZDO().Set("RunicPaths.Natural",natural);obj.GetZDO().Set("RunicPaths.PlayerObject",player);return obj;
    }
    private void Advanced(WorldPaths state)
    {
        if(advancedStage==7)
        {
            var tc=TerrainComp.FindTerrainCompiler(advancedMap!.transform.position);
            var nowHeight=(float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(tc);
            var nowMask=(Color[])AccessTools.Field(typeof(TerrainComp),"m_paintMask").GetValue(tc);
            if(!nowHeight.SequenceEqual(stableHeight!) || !nowMask.SequenceEqual(stableMask!)) throw new Exception("Established terrain disappeared after network completion");
            string file=(string)AccessTools.Field(typeof(Plugin),"savePath").GetValue(plugin);
            var saved=Persistence.Load(file,state.WorldId,state.Seed);
            var routes=new RouteIndex();foreach(var island in saved.Islands) routes.Add(island);
            if(routes.Get(Cell.From(target,64),2,12).Distance(target)>1) throw new Exception("Established route missing from persisted replacement");
            Finish("PASS 0.2.5: road paint covers height-only edits while preserving their elevation; native clearing/grading/live-width/external-paint checks; actual CompleteIsland with a partial replacement preserved established road terrain and saved route geometry.");return;
        }
        if(advancedStage==6)
        {
            int signature=(int)AccessTools.Method(typeof(Plugin),"PlanningSignature").Invoke(plugin,null);
            if(state.Islands.Any(i=>i.PlanningSignature!=signature)) return;
            Finish($"PASS advanced: bounded native grading; natural tree and stone removed; player tree and stone relocated with ZDO positions; live width 3 to 8 to 2 ({firstCount}, {widerCount} then narrowed); zero grading restored original heights; external player paint preserved; repeated scan stable; old island network replanned and saved ({state.Islands.Sum(i=>i.Routes.Count)} road sections).");return;
        }
        if(advancedStage==0)
        {
            plugin!.Config[new ConfigDefinition("Paths","MaximumPOIsPerIsland")].BoxedValue=8;
            plugin.Config[new ConfigDefinition("Performance","MaximumRouteSearchNodes")].BoxedValue=16000;
            Setting("Paths","Width",3);Setting("Terrain","MaximumRaise",1);Setting("Terrain","MaximumLower",0.75f);
            // Exercise road editing against a fixed saved route first; migrate its network in stage 6.
            var job=AccessTools.Field(typeof(Plugin),"work").GetValue(plugin) as IDisposable;job?.Dispose();
            AccessTools.Field(typeof(Plugin),"work").SetValue(plugin,null);
            AccessTools.Field(typeof(Plugin),"planner").SetValue(plugin,null);
            AccessTools.Field(typeof(Plugin),"replacingIsland").SetValue(plugin,null);
            int planSignature=(int)AccessTools.Method(typeof(Plugin),"PlanningSignature").Invoke(plugin,null);
            foreach(var island in state.Islands) island.PlanningSignature=planSignature;
            var route=state.Islands[0].Routes.OrderByDescending(r=>r.Points.Count).First();
            target=route.Points[route.Points.Count/2];
            ForcedReference=new Vector3(target.X,WorldGenerator.instance.GetHeight(target.X,target.Z)+1,target.Z);
            advancedStage=1;return;
        }
        if(advancedStage==1)
        {
            var map=Heightmap.FindHeightmap(new Vector3(target.X,0,target.Z));
            if(!map || !ZNetScene.instance.IsAreaReady(map.transform.position)) return;
            advancedMap=map;
            var index=new RouteIndex();foreach(var island in state.Islands) index.Add(island);
            var zone=index.Get(Cell.From(new Point(map.transform.position.x,map.transform.position.z),64),3,12);
            var points=zone.Segments.Select(x=>x.A).Where(p=>Point.Distance(p,target)<18 && !zone.Protected(p,3)).ToList();
            Point p1=points.OrderBy(p=>Point.Distance(p,target)).First();
            Point p2=points.OrderBy(p=>Math.Abs(Point.Distance(p,p1)-9)).First();
            Point p3=points.OrderBy(p=>Math.Abs(Point.Distance(p,p1)-16)).First();
            if(ClearingRun)
            {
                var initial=map.GetAndCreateTerrainCompiler();
                map.WorldToVertexMask(new Vector3(p1.X,0,p1.Z),out int hx,out int hz);
                editedHeightIndex=hz*(map.m_width+1)+hx;
                ((bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedHeight").GetValue(initial))[editedHeightIndex]=true;
                ((float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(initial))[editedHeightIndex]=0.25f;
                AccessTools.Method(typeof(TerrainComp),"Save",new[]{typeof(bool)}).Invoke(initial,new object[]{false});
                SeedOldSeam(initial);
                map.Poke(0,false);
            }
            Vector3 Ground(Point p) {map.GetWorldHeight(new Vector3(p.X,0,p.Z),out float h);return new Vector3(p.X,h,p.Z);}
            naturalTree=SpawnObject(ClearingRun?"Beech_small1":"Beech1",Ground(p1),!ClearingRun,false);
            playerTree=SpawnObject(ClearingRun?"Beech_small2":"Beech1",Ground(p2),true,true);originalTree=playerTree.transform.position;
            naturalStone=SpawnObject(ClearingRun?"Pickable_Stone":"Rock_4",Ground(p1),!ClearingRun,false);
            playerStone=SpawnObject("Rock_4",Ground(p3),false,true);originalStone=playerStone.transform.position;
            if(ClearingRun) plugin!.Config[new ConfigDefinition("Protection","ConfirmExistingNaturalWorld")].BoxedValue=state.WorldId.ToString();
            Logger.LogInfo("Advanced smoke planted synthetic natural/player trees and player stone on the road.");
            advancedStage=2;return;
        }
        if(!advancedMap) return;
        var compiler=TerrainComp.FindTerrainCompiler(advancedMap.transform.position);if(!compiler) return;
        var zdo=compiler.GetComponent<ZNetView>().GetZDO();
        int stamp=zdo.GetInt("RunicPaths.Applied.v6",0);if(stamp==0 || (stamp==lastStamp && advancedStage!=5)) return;
        var flags=(bool[])AccessTools.Field(typeof(TerrainComp),"m_modifiedPaint").GetValue(compiler);
        var heights=(float[])AccessTools.Field(typeof(TerrainComp),"m_levelDelta").GetValue(compiler);
        var colors=(Color[])AccessTools.Field(typeof(TerrainComp),"m_paintMask").GetValue(compiler);
        int count=flags.Count(x=>x);
        if(advancedStage==5)
        {
            if(stamp!=lastStamp || !heights.SequenceEqual(stableHeight!) || !colors.SequenceEqual(stableMask!)) throw new Exception("Road repeated without a settings change");
            if(ClearingRun)
            {
                var old=state.Islands[0];
                var replacement=new Island{Cells=old.Cells.ToList(),Exclusions=old.Exclusions.ToList(),PoiCount=old.PoiCount};
                replacement.Routes.Add(old.Routes.OrderByDescending(r=>Point.Distance(r.Points[0],target)).First());
                var fake=new Planner((ITerrain)Activator.CreateInstance(typeof(Plugin).Assembly.GetType("RunicPaths.GameTerrain"),true),new PlannerSettings());
                AccessTools.Field(typeof(Planner),"<Result>k__BackingField").SetValue(fake,replacement);
                (AccessTools.Field(typeof(Plugin),"work").GetValue(plugin) as IDisposable)?.Dispose();
                AccessTools.Field(typeof(Plugin),"work").SetValue(plugin,Enumerable.Empty<int>().GetEnumerator());
                AccessTools.Field(typeof(Plugin),"planner").SetValue(plugin,fake);
                AccessTools.Field(typeof(Plugin),"replacingIsland").SetValue(plugin,old);
                AccessTools.Field(typeof(Plugin),"planningSignature").SetValue(plugin,(int)AccessTools.Method(typeof(Plugin),"PlanningSignature").Invoke(plugin,null));
                AccessTools.Method(typeof(Plugin),"CompleteIsland").Invoke(plugin,null);
                advancedStage=7;next=Time.realtimeSinceStartup+15;return;
            }
            foreach(var island in state.Islands) island.PlanningSignature=0;
            advancedStage=6;Logger.LogInfo("Advanced road checks passed; waiting for migrated island network planning to finish.");return;
        }
        if(heights.Any(h=>h>1.001f || h< -0.751f)) throw new Exception("Grading exceeded configured original-ground bounds");
        if(advancedStage==2)
        {
            if(ClearingRun) CheckSeamsAndStructures(advancedMap,heights);
            if(ClearingRun && (!flags[editedHeightIndex] || Math.Abs(heights[editedHeightIndex]-0.25f)>0.001f)) throw new Exception("Road paint skipped player-shaped ground or overwrote its height");
            if(!heights.Any(h=>Math.Abs(h)>0.02f)) throw new Exception("No native terrain grading");
            int hi=Array.FindIndex(heights,h=>Math.Abs(h)>0.02f);int pitch=advancedMap.m_width+1;
            Vector3 hp=advancedMap.transform.position+new Vector3((hi%pitch-advancedMap.m_width/2)*advancedMap.m_scale,0,(hi/pitch-advancedMap.m_width/2)*advancedMap.m_scale);
            object[] originalArgs={hp,0f};AccessTools.Method(typeof(Heightmap),"GetWorldBaseHeight").Invoke(advancedMap,originalArgs);
            advancedMap.GetWorldHeight(hp,out float actual);
            if(Math.Abs(actual-((float)originalArgs[1]+heights[hi]))>0.02f) throw new Exception("Native ground mesh did not receive grading");
            if((naturalTree && naturalTree.IsValid()) || (naturalStone && naturalStone.IsValid())) throw new Exception("Natural tree or stone retained");
            if(!playerTree || !playerStone || !playerTree.IsValid() || !playerStone.IsValid()) throw new Exception("Player object deleted");
            if(Vector3.Distance(playerTree.transform.position,originalTree)<2 || Vector3.Distance(playerStone.transform.position,originalStone)<2) throw new Exception("Player object not relocated");
            if(Vector3.Distance(playerTree.GetZDO().GetPosition(),playerTree.transform.position)>0.01f) throw new Exception("Relocation was not persisted in ZDO");
            if(zdo.GetByteArray("RunicPaths.TerrainJournal.v1")==null) throw new Exception("Missing terrain journal");
            if(ClearingRun)
            {
                CheckRoadClutter(advancedMap,flags);
                advancedMap.GetWorldHeight(new Vector3(target.X,0,target.Z),out float fh);
                laterOrigin=new Vector3(target.X,fh,target.Z);
                laterStone=SpawnObject("Pickable_Stone",laterOrigin,false,false);
            }
            firstCount=count;lastStamp=stamp;Setting("Paths","Width",8);advancedStage=3;
            Logger.LogInfo($"Advanced initial road: {count} cells; grading and relocation passed. Widening live to 8 m.");return;
        }
        if(advancedStage==3)
        {
            if(ClearingRun && (!laterStone || !laterStone.IsValid() || laterStone.GetZDO().GetBool("RunicPaths.Natural",false) || Vector3.Distance(laterStone.transform.position,laterOrigin)<2)) throw new Exception("Future unmarked placement was deleted or not relocated");
            if(count<=firstCount) throw new Exception("Live widening did not add paint");
            widerCount=count;lastStamp=stamp;
            externalIndex=Array.FindIndex(flags,x=>x);colors[externalIndex]=new Color(0,1,0,1);
            AccessTools.Method(typeof(TerrainComp),"Save",new[]{typeof(bool)}).Invoke(compiler,new object[]{false});
            Setting("Paths","Width",2);Setting("Terrain","MaximumRaise",0);Setting("Terrain","MaximumLower",0);advancedStage=4;return;
        }
        if(advancedStage==4)
        {
            if(count>=widerCount) throw new Exception("Narrowing did not restore shoulders");
            if(heights.Where((h,i)=>i!=editedHeightIndex).Any(h=>Math.Abs(h)>0.0001f)) throw new Exception("Zero grading settings failed to restore original terrain");
            if(ClearingRun && Math.Abs(heights[editedHeightIndex]-0.25f)>0.001f) throw new Exception("Player-shaped height lost on narrowing");
            if(colors[externalIndex]!=new Color(0,1,0,1)) throw new Exception("External player paint overwritten");
            stableHeight=heights.ToArray();stableMask=colors.ToArray();lastStamp=stamp;advancedStage=5;
            next=Time.realtimeSinceStartup+10;return;
        }
    }
}








