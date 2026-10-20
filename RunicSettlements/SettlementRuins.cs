using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;
namespace RunicSettlements;
internal static class SettlementRuins
{
    private static readonly string[] MinorPrefixes={"WoodHouse","WoodFarm","WoodVillage","StoneHouse","StoneTower","Ruin","Dolmen","Grave","StoneHenge","Runestone"};
    internal static bool Minor(ZoneSystem.ZoneLocation location)
    {
        if(location.m_iconAlways || location.m_iconPlaced || location.m_interiorRadius>0) return false;
        string name=location.m_prefabName;
        return MinorPrefixes.Any(p=>name.StartsWith(p,StringComparison.OrdinalIgnoreCase));
    }
    // Index footprints once, rather than reclassifying every world POI for every ZDO.
    internal static Func<ZDO,bool> ReplacementFilter(IEnumerable<ZoneSystem.LocationInstance> locations,bool allowed)
    {
        var cells=new Dictionary<Cell,List<ZoneSystem.LocationInstance>>();
        if(allowed) foreach(var location in locations)
        {
            if(!Minor(location.m_location)) continue;
            var p=GameTerrain.PointOf(location.m_position);float r=location.m_location.m_exteriorRadius+2;
            var lo=Cell.From(p-new Point(r,r),64);var hi=Cell.From(p+new Point(r,r),64);
            for(int x=lo.X;x<=hi.X;x++) for(int z=lo.Z;z<=hi.Z;z++)
            {var cell=new Cell(x,z);if(!cells.TryGetValue(cell,out var list)) cells[cell]=list=new List<ZoneSystem.LocationInstance>();list.Add(location);}
        }
        return zdo=>
        {
            if(!allowed || zdo.GetLong(ZDOVars.s_creator,0)!=0 || zdo.GetBool(RoadObjects.PlayerKey,false) || zdo.GetString(SettlementDecorations.Key,"")!="") return false;
            var position=zdo.GetPosition();var p=GameTerrain.PointOf(position);
            return cells.TryGetValue(Cell.From(p,64),out var nearby) && nearby.Any(l=>Point.Distance(p,GameTerrain.PointOf(l.m_position))<=l.m_location.m_exteriorRadius+2 && Math.Abs(position.y-l.m_position.y)<30);
        };
    }
    internal static bool ReplaceableObject(ZDO zdo,IEnumerable<ZoneSystem.LocationInstance> locations,bool allowed)
    {
        if(!allowed || zdo.GetLong(ZDOVars.s_creator,0)!=0 || zdo.GetBool(RoadObjects.PlayerKey,false) || zdo.GetString(SettlementDecorations.Key,"")!="") return false;
        Point p=GameTerrain.PointOf(zdo.GetPosition());
        return locations.Any(l=>Minor(l.m_location) && Point.Distance(p,GameTerrain.PointOf(l.m_position))<=l.m_location.m_exteriorRadius+2 && Math.Abs(zdo.GetPosition().y-l.m_position.y)<30);
    }
    internal static IEnumerable<int> Clear(SettlementPlan plan,string planFile)
    {
        if(!plan.Options.ReplaceSmallRuins) yield break;
        var locations=ZoneSystem.instance.m_locationInstances.ToArray();
        var replaced=locations.Where(pair=>Minor(pair.Value.m_location) && plan.Sites.Any(s=>Point.Distance(s.Center,GameTerrain.PointOf(pair.Value.m_position))<plan.Options.Radius+plan.Options.Blend+pair.Value.m_location.m_exteriorRadius)).ToArray();
        foreach(var pair in replaced)
        {
            var l=pair.Value;string key=l.m_location.m_prefabName+" at "+l.m_position.x.ToString("F0")+","+l.m_position.z.ToString("F0");
            if(!plan.ReplacedLocations.Contains(key)) plan.ReplacedLocations.Add(key);
        }
        // Save the reserved footprint list before changing the world's generated-location flags.
        plan.Save(planFile);
        foreach(var pair in replaced)
        {var location=pair.Value;location.m_placed=true;ZoneSystem.instance.m_locationInstances[pair.Key]=location;yield return 0;}
        var records=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values.ToArray();
        var replaceable=ReplacementFilter(replaced.Select(p=>p.Value),true);
        int removed=0;
        foreach(var zdo in records)
        {
            yield return 0;
            if(!replaceable(zdo)) continue;
            var prefab=ZNetScene.instance.GetPrefab(zdo.GetPrefab());if(!prefab) continue;
            // Trees/unknown planted objects are handled by conservative relocation instead.
            bool structure=prefab.GetComponent<Piece>() || prefab.GetComponent<LocationProxy>() || prefab.GetComponent<Container>() ||
                prefab.name.StartsWith("Runestone",StringComparison.OrdinalIgnoreCase);
            if(!structure || RoadObjects.Movable(prefab)) continue;
            zdo.SetOwner(ZDOMan.GetSessionID());ZDOMan.instance.DestroyZDO(zdo);removed++;yield return 0;
        }
        Plugin.Report($"Settlements reserve {replaced.Length} small generated location footprints; {removed} existing generated structure objects removed. Player-owned objects preserved.");
    }
}
