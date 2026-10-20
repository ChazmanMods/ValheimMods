using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ArcaneWaterWorks;

[HarmonyPatch(typeof(Heightmap),"ApplyModifiers")]
internal static class RiverTerrain
{
    sealed class Base { internal float[] Heights; }
    static readonly ConditionalWeakTable<Heightmap,Base> Bases=new ConditionalWeakTable<Heightmap,Base>();
    [HarmonyPostfix,HarmonyPriority(Priority.Last)]
    static void Postfix(Heightmap __instance,List<float> ___m_heights,Texture2D ___m_paintMask){
        if(__instance.IsDistantLod)return;
        // Unaffected maps need neither a snapshot nor per-vertex river queries.
        var map=__instance.transform.position;
        bool nearby=false;
        foreach(var candidate in Spring.Active)if(candidate&&candidate.Live&&candidate.Route!=null&&Touches(__instance,candidate,candidate.Route)){nearby=true;break;}
        if(!nearby){Bases.Remove(__instance);return;}
        Bases.GetOrCreateValue(__instance).Heights=___m_heights.ToArray();
        int n=__instance.m_width;float spacing=__instance.m_scale,half=n*spacing*.5f;
        bool painted=false;var original=___m_heights.ToArray();var applied=new bool[original.Length];
        foreach(var spring in Spring.Active){
            if(!spring||spring.Route==null||!spring.Live)continue;
            var origin=spring.transform.position;
            Bounds bounds=Bounds(spring,spring.Route);
            if(!Touches(__instance,spring,spring.Route))continue;
            int minX=Mathf.Clamp(Mathf.FloorToInt((bounds.min.x-map.x+half)/spacing),0,n),maxX=Mathf.Clamp(Mathf.CeilToInt((bounds.max.x-map.x+half)/spacing),0,n);
            int minZ=Mathf.Clamp(Mathf.FloorToInt((bounds.min.z-map.z+half)/spacing),0,n),maxZ=Mathf.Clamp(Mathf.CeilToInt((bounds.max.z-map.z+half)/spacing),0,n);
            for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++){
                float wx=map.x+x*spacing-half,wz=map.z+z*spacing-half;int index=z*(n+1)+x;
                spring.Route.Surface(wx-origin.x,wz-origin.z,out var distance,out _,out _);if(distance>=spring.Route.Width*.5f+spring.Route.BankPadding)continue;
                float bed=PlannedBed(spring,new Vector3(wx,original[index]+map.y,wz))-map.y;
                ___m_heights[index]=applied[index]?Mathf.Min(___m_heights[index],bed):bed;applied[index]=true;
            }
            if(spring.Route.Mud&&___m_paintMask)for(int z=minZ;z<=maxZ&&z<___m_paintMask.height;z++)for(int x=minX;x<=maxX&&x<___m_paintMask.width;x++){
                float wx=map.x+(x+.5f)*spacing-half,wz=map.z+(z+.5f)*spacing-half;
                spring.Route.Surface(wx-origin.x,wz-origin.z,out float distance,out _,out _);
                float weight=Mathf.Clamp01((spring.Route.Width*.5f+.5f-distance)/1.5f);
                weight=weight*weight*(3-2*weight);
                if(weight<=0)continue;
                // Use Valheim's own dirt paint, exactly as the hoe's Dirt operation does.
                // Alpha is the separate vegetation mask, not the dirt material selector.
                var originalColor=___m_paintMask.GetPixel(x,z);
                var color=Color.Lerp(originalColor,Heightmap.m_paintMaskDirt,weight);color.a=originalColor.a;
                ___m_paintMask.SetPixel(x,z,color);painted=true;
            }
        }
        if(painted)___m_paintMask.Apply();
    }
    internal static bool SampleBase(Vector3 p,out float height)=>Sample(p,true,out height);
    internal static bool SampleCurrent(Vector3 p,out float height)=>Sample(p,false,out height);
    // Interpolate the same four terrain vertices used by Heightmap's mesh, including the planned bed.
    internal static bool SamplePlanned(Spring spring,Vector3 p,out float height)=>Sample(p,true,out height,spring);
    static bool Sample(Vector3 p,bool underlying,out float height,Spring spring=null){
        height=0;var h=Heightmap.FindHeightmap(p);if(!h||h.IsDistantLod)return false;
        var local=p-h.transform.position;float gx=local.x/h.m_scale+h.m_width*.5f,gz=local.z/h.m_scale+h.m_width*.5f;
        int x=Mathf.Clamp(Mathf.FloorToInt(gx),0,h.m_width-1),z=Mathf.Clamp(Mathf.FloorToInt(gz),0,h.m_width-1);float u=Mathf.Clamp01(gx-x),v=Mathf.Clamp01(gz-z);
        Bases.TryGetValue(h,out var data);
        float At(int xx,int zz){
            float value=underlying&&data?.Heights!=null?data.Heights[zz*(h.m_width+1)+xx]:h.GetHeight(xx,zz);
            if(spring&&spring.Route!=null){var origin=spring.transform.position;var map=h.transform.position;
                float wx=map.x+(xx-h.m_width*.5f)*h.m_scale,wz=map.z+(zz-h.m_width*.5f)*h.m_scale,baseHeight=value+map.y;bool found=false;float merged=baseHeight;
                foreach(var other in Spring.Active){if(!other||!other.Live||other.Route==null)continue;var o=other.transform.position;
                    other.Route.Surface(wx-o.x,wz-o.z,out var distance,out _,out _);if(distance>=other.Route.Width*.5f+other.Route.BankPadding)continue;
                    float result=PlannedBed(other,new Vector3(wx,baseHeight,wz));merged=found?Mathf.Min(merged,result):result;found=true;
                }value=merged-map.y;
            }return value;
        }
        float a=At(x,z),b=At(x+1,z),c=At(x,z+1),d=At(x+1,z+1);
        height=(u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v))+h.transform.position.y;return true;
    }
    internal static Bounds Bounds(Spring spring,Route route){
        var b=new Bounds(spring.transform.position,Vector3.zero);
        foreach(var p in route.Points)b.Encapsulate(spring.transform.position+new Vector3(p.X,p.Y,p.Z));
        b.Expand(new Vector3(route.Width+2*route.BankPadding,8,route.Width+2*route.BankPadding));return b;
    }
    static float PlannedBed(Spring spring,Vector3 world){
        var p=world-spring.transform.position;var route=spring.Route;
        spring.Surface(p.x,p.z,out _,out float level,out _);
        float bed=route.Bed(p.x,p.z,p.y,level)+spring.transform.position.y;
        // A bank must not become a dam across a connected WaterGardens pool.
        float pond=ArcaneWaterWorks.Pools.GardenIntegration.WaterLevel(world);
        if(!float.IsNegativeInfinity(pond))bed=Mathf.Min(bed,Mathf.Min(world.y,pond-.3f));
        return bed;
    }
    static bool Touches(Heightmap h,Spring spring,Route route){
        if(route==null||!Overlaps(h,Bounds(spring,route)))return false;
        // A winding river's enclosing rectangle can contain many untouched terrain tiles.
        var origin=spring.transform.position;float padding=route.Width+2*route.BankPadding;
        for(int i=1;i<route.Points.Count;i++){
            var a=route.Points[route.Parent(i)];var c=route.Points[i];
            var segment=new Bounds(origin+new Vector3(a.X,a.Y,a.Z),Vector3.zero);
            segment.Encapsulate(origin+new Vector3(c.X,c.Y,c.Z));segment.Expand(new Vector3(padding,8,padding));
            if(Overlaps(h,segment))return true;
        }
        return false;
    }
    static bool Overlaps(Heightmap h,Bounds b){
        float half=h.m_width*h.m_scale*.5f;
        return !h.IsDistantLod&&Math.Abs(h.transform.position.x-b.center.x)<=half+b.extents.x&&Math.Abs(h.transform.position.z-b.center.z)<=half+b.extents.z;
    }
    internal static void Refresh(Spring spring,Route route,Route old=null){
        if(route==null&&old==null)return;
        var b=Bounds(spring,route??old);if(old!=null)b.Encapsulate(Bounds(spring,old));
        foreach(var h in Heightmap.GetAllHeightmaps())if(Touches(h,spring,route)||Touches(h,spring,old))h.Poke(1);
        if(ClutterSystem.instance)ClutterSystem.instance.ResetGrass(b.center,Mathf.Max(b.extents.x,b.extents.z));
    }
}

internal sealed class RiverLiquid:MonoBehaviour
{
    internal static readonly Dictionary<WaterVolume,RiverLiquid> Registry=new Dictionary<WaterVolume,RiverLiquid>();
    internal Spring Spring;
    internal ArcaneWaterWorks.Pools.GardenPiece Pond;
    internal Bounds WorldBounds;
    WaterVolume water;
    internal static bool NeedsFullQuery(Vector3 point,ref WaterVolume previous){
        if(previous&&Registry.ContainsKey(previous))previous=null;
        foreach(var river in Registry.Values)if(river&&river.WorldBounds.Contains(point))return true;
        return false;
    }
    void OnEnable(){water=GetComponent<WaterVolume>();if(water)Registry[water]=this;}
    void OnDisable(){if(!ReferenceEquals(water,null))Registry.Remove(water);}
    void OnDestroy(){if(!ReferenceEquals(water,null))Registry.Remove(water);}
}
[HarmonyPatch(typeof(WaterVolume),"GetWaterSurface")]
internal static class RiverLiquidHeight
{
    static bool Prefix(WaterVolume __instance,Vector3 point,ref float __result){
        if(!RiverLiquid.Registry.TryGetValue(__instance,out var river))return true;
        __result=-10000;
        if(river&&river.Pond){
            float level=ArcaneWaterWorks.Pools.GardenIntegration.WaterLevel(point);
            if(!float.IsNegativeInfinity(level))__result=level;
            return false;
        }
        if(river&&river.Spring&&river.Spring.Route!=null){
            var p=point-river.Spring.transform.position;
            if(river.Spring.Surface(p.x,p.z,out _,out float level,out _))__result=RiverSurface.Level(river.Spring,point,river.Spring.transform.position.y+level);
        }
        return false;
    }
}
// A broad trigger must use the highest actual liquid at each floater, including native ocean water.
// Otherwise an out-of-channel part of its bounding box could overwrite a lake's level.
[HarmonyPatch(typeof(WaterVolume),"UpdateFloaters")]
internal static class RiverFloaters
{
    static bool Prefix(WaterVolume __instance,List<IWaterInteractable> ___m_inWater){
        if(!RiverLiquid.Registry.ContainsKey(__instance))return true;
        for(int i=___m_inWater.Count-1;i>=0;i--){
            var item=___m_inWater[i];
            if(item==null||!item.GetTransform()){___m_inWater.RemoveAt(i);continue;}
            item.SetLiquidLevel(Floating.GetLiquidLevel(item.GetTransform().position,1,LiquidType.Water),LiquidType.Water,__instance);
        }
        return false;
    }
}

// Native floating-object caches assume every point in a volume's box contains water.
// Use an actual overlap query near our masked boxes so boats can cross a river/lake junction.
[HarmonyPatch(typeof(Floating),"GetWaterLevel")]
internal static class RiverCachedWaterLevel
{
    static bool Prefix(Vector3 p,ref WaterVolume previousAndOut,ref float __result){
        if(!RiverLiquid.NeedsFullQuery(p,ref previousAndOut))return true;
        __result=Floating.GetLiquidLevel(p,1,LiquidType.Water);return false;
    }
}
[HarmonyPatch(typeof(Floating),"IsUnderWater")]
internal static class RiverCachedUnderwater
{
    static bool Prefix(Vector3 p,ref WaterVolume previousAndOut,ref bool __result){
        if(!RiverLiquid.NeedsFullQuery(p,ref previousAndOut))return true;
        __result=Floating.GetLiquidLevel(p,1,LiquidType.Water)>p.y;return false;
    }
}
