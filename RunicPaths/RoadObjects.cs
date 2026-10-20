using System;
using System.Collections.Generic;
using HarmonyLib;
using RunicPaths.Core;
using UnityEngine;

namespace RunicPaths;

internal static class RoadObjects
{
    internal const string PlayerKey="RunicPaths.PlayerObject";
    internal const string MovedKey="RunicPaths.Relocated";
    internal static readonly int SolidMask=LayerMask.GetMask("Default","static_solid","Default_small","piece");
    internal static bool Movable(GameObject obj)
    {
        if(obj.GetComponent<TreeBase>()) return true;
        var plant=obj.GetComponent<Plant>();
        if(plant) foreach(var grown in plant.m_grownPrefabs) if(grown && grown.GetComponent<TreeBase>()) return true;
        string name=obj.name.Replace("(Clone)","").Trim();
        if ((name.StartsWith("Beech_small",StringComparison.Ordinal) || name.StartsWith("FirTree_small",StringComparison.Ordinal)) && obj.GetComponent<Destructible>()) return true;
        bool rock=name.StartsWith("rock",StringComparison.OrdinalIgnoreCase) || name.Equals("stone",StringComparison.OrdinalIgnoreCase) || name.Equals("Pickable_Stone",StringComparison.OrdinalIgnoreCase);
        if(name.IndexOf("copper",StringComparison.OrdinalIgnoreCase)>=0) rock=false;
        if(name.EndsWith("_Stub",StringComparison.OrdinalIgnoreCase) && obj.GetComponent<Destructible>()) return true;
        bool bush=name.StartsWith("Bush",StringComparison.OrdinalIgnoreCase) || name.StartsWith("shrub",StringComparison.OrdinalIgnoreCase);
        return (rock || bush) && (obj.GetComponent<Destructible>() || obj.GetComponent<MineRock>() || obj.GetComponent<MineRock5>() || obj.GetComponent<Pickable>());
    }
    internal static float Radius(ZNetView view)
    {
        // Tree canopies are much wider than the trunk/planting footprint.
        if(view.GetComponent<TreeBase>() || view.GetComponent<Plant>()) return 1;
        float radius=0.5f;
        foreach(var c in view.GetComponentsInChildren<Collider>())
            if(!c.isTrigger && (SolidMask & (1<<c.gameObject.layer))!=0)
                radius=Math.Max(radius,Math.Max(c.bounds.extents.x,c.bounds.extents.z));
        return radius;
    }
    internal static bool PlayerOwned(ZNetView view) => view.GetZDO().GetBool(PlayerKey,false) ||
        (view.GetComponent<Piece>() && view.GetComponent<Piece>().GetCreator()!=0) || view.GetComponent<Plant>();

    internal static bool FindSide(ZNetView view, ZonePaths paths, Func<Point,bool> protectedPoint,
        List<(Vector3 Position,float Radius)> reserved, out Vector3 destination)
    {
        destination=default;
        Vector3 original=view.transform.position;
        Point origin=GameTerrain.PointOf(original);
        float radius=Radius(view);
        // Search nearby dry ground, preserving the object's scale, health and ownership ZDO.
        for(float distance=2;distance<=paths.RelocationDistance;distance+=2)
        for(int i=0;i<16;i++)
        {
            float angle=i*Mathf.PI/8;
            Point p=origin+new Point(Mathf.Cos(angle),Mathf.Sin(angle))*distance;
            if(paths.Distance(p)<paths.Width/2+radius+2 || protectedPoint(p)) continue;
            var map=Heightmap.FindHeightmap(GameTerrain.VectorOf(p));
            if(!map || !ZNetScene.instance.IsAreaReady(map.transform.position) || !map.GetWorldHeight(GameTerrain.VectorOf(p),out float h)) continue;
            if(h<ZoneSystem.instance.m_waterLevel+1 || Math.Abs(h-original.y)>4) continue;
            bool safe=true;
            foreach(var direction in new[]{new Point(radius,0),new Point(-radius,0),new Point(0,radius),new Point(0,-radius)})
            {
                var edge=Heightmap.FindHeightmap(GameTerrain.VectorOf(p+direction));
                if(!edge || !edge.GetWorldHeight(GameTerrain.VectorOf(p+direction),out float eh) || eh<ZoneSystem.instance.m_waterLevel+1 || Math.Abs(eh-h)>1.5f) {safe=false;break;}
            }
            if(!safe) continue;
            Vector3 position=GameTerrain.VectorOf(p,h);
            foreach(var item in reserved) if(Point.Distance(p,GameTerrain.PointOf(item.Position))<radius+item.Radius+1) {safe=false;break;}
            if(!safe) continue;
            foreach(var collider in Physics.OverlapSphere(position+Vector3.up, radius+0.5f,SolidMask,QueryTriggerInteraction.Ignore))
                if(collider && !collider.GetComponentInParent<Heightmap>() && collider.GetComponentInParent<ZNetView>()!=view) {safe=false;break;}
            if(!safe) continue;
            destination=position; reserved.Add((position,radius)); return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation")]
internal static class VegetationScope
{
    internal static int Depth;
    private static void Prefix() { Depth++; }
    private static Exception? Finalizer(Exception? __exception) { Depth=Math.Max(0,Depth-1); return __exception; }
}

[HarmonyPatch(typeof(ZNetView), "Awake")]
internal static class NaturalVegetationTag
{
    private static void Postfix(ZNetView __instance)
    {
        if(!__instance.IsValid() || !RoadObjects.Movable(__instance.gameObject)) return;
        if((PreparedRuntime.Active==null || __instance.GetZDO().GetBool(RoadObjects.MovedKey,false)) && !__instance.GetComponent<RelocatedObjectSync>()) __instance.gameObject.AddComponent<RelocatedObjectSync>();
        if(PlayerPlacementScope.Depth>0 && __instance.IsOwner()) {__instance.GetZDO().Set(RoadObjects.PlayerKey,true);return;}
        if(VegetationScope.Depth>0 && Plugin.CanTagNature && __instance.IsOwner()) __instance.GetZDO().Set(Plugin.NaturalKey,true);
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.Grow))]
internal static class PlantedTreeTag
{
    private static void Postfix(GameObject __result)
    {
        if(!__result) return;
        var view=__result.GetComponent<ZNetView>();
        if(view && view.IsValid() && view.IsOwner()) {view.GetZDO().Set(RoadObjects.PlayerKey,true);view.GetZDO().Set(Plugin.NaturalKey,false);}
    }
}

// Static trees/rocks have no native position sync. Mirror the authoritative ZDO for observers.
internal sealed class RelocatedObjectSync : MonoBehaviour
{
    private ZNetView? view;
    private float next;
    private void Update()
    {
        if(Time.time<next) return; next=Time.time+0.5f;
        if(!view) view=GetComponent<ZNetView>();
        if(view && view.IsValid() && !view.IsOwner() && view.GetZDO().GetBool(RoadObjects.MovedKey,false))
            transform.position=view.GetZDO().GetPosition();
    }
}


[HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
internal static class PlayerPlacementScope
{
    internal static int Depth;
    private static void Prefix() {Depth++;}
    private static Exception? Finalizer(Exception? __exception) {Depth=Math.Max(0,Depth-1);return __exception;}
}
