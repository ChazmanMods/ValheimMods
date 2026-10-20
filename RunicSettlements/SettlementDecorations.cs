using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;
namespace RunicSettlements;

internal static class SettlementDecorations
{
    internal const string Key="RunicSettlements.Decoration",LampKey="RunicSettlements.Lamp";
    internal static SettlementPlan? Current;
    internal static string? File;
    internal static IEnumerable<int> Prepare(SettlementPlan plan,PreparedSurface surface,ITerrain terrain,string file,List<Exclusion> protection)
    {
        Current=plan;File=file;
        float RoadDistance(Point p)=>plan.Roads.SelectMany(r=>Enumerable.Range(1,r.Points.Count-1).Select(i=>Point.SegmentDistance(p,r.Points[i-1],r.Points[i]))).DefaultIfEmpty(float.MaxValue).Min();
        bool Safe(Point p,float radius)=>terrain.Height(p)>plan.Options.Water+2 && !protection.Any(e=>e.Contains(p,radius)) &&
            plan.Sites.All(s=>Point.Distance(p,s.Center)>plan.Options.Radius+2);
        if(plan.Decorations.Count==0 && plan.Options.Decorate)
        {
            var lampLists=new List<List<Point>>();
            foreach(var road in plan.Roads)
            {
                var points=new List<Point>();float next=plan.Options.LampSpacing/2,traveled=0;
                for(int i=1;i<road.Points.Count;i++)
                {
                    Point a=road.Points[i-1],b=road.Points[i],d=b-a;float length=d.Length;if(length<.01f) continue;
                    while(next<=traveled+length)
                    {
                        Point p=Point.Lerp(a,b,(next-traveled)/length)+new Point(-d.Z/length,d.X/length)*(plan.Options.Width/2+3)*(points.Count%2==0?1:-1);
                        if(Safe(p,2) && RoadDistance(p)>plan.Options.Width/2+1) points.Add(p);
                        next+=plan.Options.LampSpacing;
                    }
                    traveled+=length;yield return 0;
                }
                lampLists.Add(points);
            }
            int lamp=0;
            for(int n=0;n<lampLists.Select(l=>l.Count).DefaultIfEmpty(0).Max() && lamp<plan.Options.MaximumLamps;n++)
            foreach(var list in lampLists)
            {
                if(n>=list.Count || lamp>=plan.Options.MaximumLamps) continue;Point p=list[n];
                if(plan.Decorations.Any(d=>Point.Distance(p,d.Position)<plan.Options.LampSpacing*.7f)) continue;
                plan.Decorations.Add(new SettlementDecoration {Id="lamp-"+lamp++,Prefab="piece_groundtorch",Position=p,Height=SettlementSurface.Height(surface,terrain,p),Lamp=true});yield return 0;
            }
            foreach(var site in plan.Sites)
            {
                int placed=0;
                for(int i=0;i<24 && placed<plan.Options.TreesPerSite;i++)
                {
                    double angle=(i*137.50776+plan.Seed%360)*Math.PI/180;
                    Point p=site.Center+new Point((float)Math.Cos(angle),(float)Math.Sin(angle))*(plan.Options.Radius+9);
                    if(!Safe(p,6) || RoadDistance(p)<plan.Options.Width/2+8 || plan.Decorations.Any(d=>Point.Distance(d.Position,p)<12)) continue;
                    plan.Decorations.Add(new SettlementDecoration {Id=site.Name+"-tree-"+placed,Prefab=placed++%2==0?"Oak1":"PineTree",Position=p,Height=SettlementSurface.Height(surface,terrain,p),Rotation=(float)(angle*180/Math.PI)});
                    yield return 0;
                }
            }
            plan.Save(file);
        }
        SettlementWayfinding.Plan(plan,surface,terrain,file,protection);
        var records=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values.ToArray();
        var present=new HashSet<string>(records.Select(z=>z.GetString(Key,"")));
        var markerProtection=PreparedProtection.Capture(2,z=>z.GetString(Key,"")!="");
        int created=0;
        foreach(var decoration in plan.Decorations)
        {
            if(present.Contains(decoration.Id) || plan.RemovedDecorations.Contains(decoration.Id)) continue;
            var prefab=ZNetScene.instance.GetPrefab(decoration.Prefab);
            if(!prefab || !prefab.GetComponent<ZNetView>()) throw new InvalidOperationException("Missing settlement prefab: "+decoration.Prefab);
            // If a player has occupied this spot since preparation, do not respawn a prop into their build.
            if(!SettlementWayfinding.IsMarker(decoration.Id) && protection.Any(e=>e.Contains(decoration.Position,1))) continue;
            if(SettlementWayfinding.IsMarker(decoration.Id) && markerProtection.Any(e=>e.Contains(decoration.Position,1))) continue;
            var template=prefab.GetComponent<ZNetView>();
            var zdo=ZDOMan.instance.CreateNewZDO(GameTerrain.VectorOf(decoration.Position,decoration.Height),decoration.Prefab.GetStableHashCode());
            zdo.Persistent=true;zdo.Type=template.m_type;zdo.Distant=template.m_distant;
            zdo.SetPrefab(decoration.Prefab.GetStableHashCode());zdo.SetRotation(Quaternion.Euler(0,decoration.Rotation,0));
            zdo.Set(Key,decoration.Id);zdo.Set(LampKey,decoration.Lamp);
            zdo.Set(ZDOVars.s_creator,0L);
            if(decoration.Lamp) zdo.Set(ZDOVars.s_fuel,100f);
            if(!string.IsNullOrEmpty(decoration.Text)) zdo.Set(ZDOVars.s_text,decoration.Text);
            created++;yield return 0;
        }
        Plugin.Report($"Settlements: {plan.Sites.Count} sites; {plan.Decorations.Count(d=>d.Lamp)} planned lamps; {plan.Decorations.Count(d=>d.Prefab=="Oak1" || d.Prefab=="PineTree")} landmark trees; {created} new persistent objects.");
    }
}

[HarmonyPatch(typeof(Fireplace),"Awake")]
internal static class PermanentSettlementLight
{
    private static void Postfix(Fireplace __instance)
    {
        var view=__instance.GetComponent<ZNetView>();
        if(!view || !view.IsValid() || !view.GetZDO().GetBool(SettlementDecorations.LampKey,false)) return;
        __instance.m_infiniteFuel=true;
        __instance.CancelInvoke("UpdateIgnite");
        foreach(var light in __instance.GetComponentsInChildren<Light>(true)) {light.shadows=LightShadows.None;light.range=Math.Min(light.range,16);}
    }
}

[HarmonyPatch(typeof(ZDOMan),"HandleDestroyedZDO")]
internal static class RememberRemovedDecoration
{
    private static void Prefix(ZDOMan __instance,ZDOID uid)
    {
        if(!ZNet.instance || !ZNet.instance.IsServer() || SettlementDecorations.Current==null || SettlementDecorations.File==null) return;
        var zdo=__instance.GetZDO(uid);if(zdo==null) return;
        string id=zdo.GetString(SettlementDecorations.Key,"");if(id.Length==0 || SettlementDecorations.Current.RemovedDecorations.Contains(id)) return;
        SettlementDecorations.Current.RemovedDecorations.Add(id);SettlementDecorations.Current.Save(SettlementDecorations.File);
    }
}

[HarmonyPatch(typeof(Fireplace),nameof(Fireplace.CanBeRemoved))]
internal static class RemoveSettlementLight
{
    private static void Postfix(Fireplace __instance,ref bool __result)
    {
        var view=__instance.GetComponent<ZNetView>();
        if(view && view.IsValid() && view.GetZDO().GetBool(SettlementDecorations.LampKey,false)) __result=true;
    }
}
