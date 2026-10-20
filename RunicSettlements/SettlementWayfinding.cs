using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;
namespace RunicSettlements;

// Persistent native pieces: clients need no custom prefab registration or map reveal.
internal static class SettlementWayfinding
{
    internal static bool IsMarker(string id)=>id.StartsWith("wayfinder-",StringComparison.Ordinal);
    internal static void Plan(SettlementPlan plan,PreparedSurface surface,ITerrain terrain,string file,List<Exclusion> protection)
    {
        if(plan.Sites.Count==0 || plan.Roads.Count==0) return;
        var buildings=PreparedProtection.Capture(2,z=>z.GetString(SettlementDecorations.Key,"")!="");
        // A sign does not reshape terrain. The spawn location's broad terrain exclusion
        // need not keep a freestanding sign far away; actual buildings remain protected.
        var exclusions=protection.Where(e=>Point.Distance(e.Center,plan.Spawn)>1).Concat(buildings).ToList();
        float Ground(Point p)=>SettlementSurface.Height(surface,terrain,p);
        bool Safe(Point p)=>Ground(p)>plan.Options.Water+2 &&
            !exclusions.Any(e=>e.Contains(p,4)) &&
            new[]{new Point(3,0),new Point(-3,0),new Point(0,3),new Point(0,-3)}.All(d=>Math.Abs(Ground(p+d)-Ground(p))<1.5f);
        void Marker(string id,Point origin,Point toward,bool spawn)
        {
            string prefix="wayfinder-"+id;
            if(plan.Decorations.Any(d=>d.Id.StartsWith(prefix+"-",StringComparison.Ordinal))) return;
            Point direction=toward-origin;double bearing=Math.Atan2(direction.X,direction.Z);
            Point? found=null;
            for(float r=spawn?24:plan.Options.Width/2+5;r<=(spawn?64:24) && found==null;r+=4)
            for(int i=0;i<24 && found==null;i++)
            {
                double angle=bearing+(i%2==0?1:-1)*((i+1)/2)*Math.PI/12;
                var p=origin+new Point((float)Math.Sin(angle),(float)Math.Cos(angle))*r;
                if(!Safe(p)) continue;
                if(plan.Roads.Any(road=>Enumerable.Range(1,road.Points.Count-1).Any(n=>Point.SegmentDistance(p,road.Points[n-1],road.Points[n])<plan.Options.Width/2+3))) continue;
                found=p;
            }
            if(found==null){Plugin.Report("No safe position for "+id+" direction sign; will retry on next load.");return;}
            Point position=found.Value,toRoad=plan.Anchor-position;
            string[] compass={"N","NE","E","SE","S","SW","W","NW"};
            int sector=((int)Math.Round(Math.Atan2(toRoad.X,toRoad.Z)*4/Math.PI)+8)%8;
            string text=spawn?"SETTLEMENTS\nROAD "+compass[sector]+" "+Math.Round(toRoad.Length)+"m":"SETTLEMENT ROAD\nFOLLOW THE LIGHTS";
            // Two large boards back-to-back on a tall post, with a light on each side.
            float yaw=(float)(Math.Atan2((plan.Spawn-position).X,(plan.Spawn-position).Z)*180/Math.PI);
            Point side=new Point((float)Math.Cos(yaw*Math.PI/180),-(float)Math.Sin(yaw*Math.PI/180));
            Point front=new Point(-side.Z,side.X);
            void Add(string suffix,string prefab,Point p,float lift,float rotation,bool lamp=false,string label="")=>
                plan.Decorations.Add(new SettlementDecoration{Id=prefix+"-"+suffix,Prefab=prefab,Position=p,Height=Ground(p)+lift,Rotation=rotation,Lamp=lamp,Text=label});
            Add("post","wood_pole2",position,0,0);
            Add("post-top","wood_pole2",position,2,0);
            Add("front","sign",position+front*.12f,2.5f,yaw,false,text);
            Add("back","sign",position-front*.12f,2.5f,yaw+180,false,text);
            Add("light-left","piece_groundtorch",position+side*3,0,0,true);
            Add("light-right","piece_groundtorch",position-side*3,0,0,true);
            Plugin.Report($"{id} sign at X={position.X:F0}, Z={position.Z:F0}: {text.Replace('\n',' ')}");
        }
        Marker("spawn",plan.Spawn,plan.Anchor,true);
        Marker("entrance",plan.Anchor,plan.Spawn,false);
        plan.Save(file);
    }
}

[HarmonyPatch(typeof(Sign),"Awake")]
internal static class LargeSettlementSign
{
    private static void Postfix(Sign __instance)
    {
        var zdo=__instance.GetComponent<ZNetView>()?.GetZDO();
        if(zdo!=null && SettlementWayfinding.IsMarker(zdo.GetString(SettlementDecorations.Key,"")))
            __instance.transform.localScale=Vector3.one*2.5f;
    }
}

[HarmonyPatch(typeof(WearNTear),"Awake")]
internal static class SettlementMarkerWeatherProtection
{
    private static void Postfix(WearNTear __instance)
    {
        var zdo=__instance.GetComponent<ZNetView>()?.GetZDO();
        if(zdo==null || !SettlementWayfinding.IsMarker(zdo.GetString(SettlementDecorations.Key,""))) return;
        __instance.m_noRoofWear=false;
        __instance.m_noSupportWear=false;
    }
}
