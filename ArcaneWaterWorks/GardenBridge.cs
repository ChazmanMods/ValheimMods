using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using ArcaneWaterWorks.Pools;
using UnityEngine;

namespace ArcaneWaterWorks;

internal static class GardenBridge
{
    internal static void Register(){RiverGardens.SampleWater=Water;RiverGardens.Section=Section;RiverGardens.PondRegionChanged+=PondsChanged;RiverGardens.NotifyChanged();}
    internal static void Unregister(){RiverGardens.PondRegionChanged-=PondsChanged;RiverGardens.SampleWater=null;RiverGardens.Section=null;RiverGardens.NotifyChanged();}
    static Spring Nearest(Vector3 world,out float level,out float margin){
        Spring selected=null;level=0;margin=0;
        foreach(var spring in Spring.Active){if(!spring||!spring.Live||spring.Route==null)continue;var p=world-spring.transform.position;
            spring.Surface(p.x,p.z,out float distance,out float h,out _);float inside=spring.Route.Width*.5f+1.5f-distance;
            if(inside<=margin)continue;selected=spring;level=h+spring.transform.position.y;margin=inside;
        }return selected;
    }
    internal static float JoinRiver(Vector3 world,float river){
        return RiverGardens.PondConnection(world,out float edge,out float pond)?PondRiverJoin.Height(river,pond,edge):river;
    }
    internal static float SurfaceHeight(Vector3 world,float fallback){
        float pond=RiverGardens.RawPondLevel(world);if(!float.IsNegativeInfinity(pond))return pond;
        return Nearest(world,out float river,out _)?river:fallback;
    }
    internal static Vector3 SurfaceNormal(Vector3 world){
        float left=SurfaceHeight(world+Vector3.left*.05f,world.y),right=SurfaceHeight(world+Vector3.right*.05f,world.y);
        float back=SurfaceHeight(world+Vector3.back*.05f,world.y),front=SurfaceHeight(world+Vector3.forward*.05f,world.y);
        return new Vector3((left-right)*10,1,(back-front)*10).normalized;
    }
    static void PondsChanged(Bounds region){RiverRebuildQueue.Region(region);foreach(var spring in Spring.Active)
        if(spring&&spring.Live&&spring.Route!=null&&region.Intersects(RiverTerrain.Bounds(spring,spring.Route)))RiverTerrain.Refresh(spring,spring.Route);}
    internal static float Water(Vector3 world){float level=float.NegativeInfinity;foreach(var spring in Spring.Active){
        if(!spring||!spring.Live||spring.Route==null)continue;var p=world-spring.transform.position;
        if(spring.Surface(p.x,p.z,out _,out var h,out _))level=Mathf.Max(level,RiverSurface.Level(spring,world,h+spring.transform.position.y));
    }return level;}
    internal static List<RiverGardens.Span> Section(Vector3 stone)
    {
        // Build only when the stone or river changes. Walking along a river does not rebuild this graph.
        var points=new List<Vector3>();var edges=new List<(int a,int b,float width)>();
        var lookup=new Dictionary<(int,int,int),int>();
        int Vertex(Vector3 p){var key=(Mathf.RoundToInt(p.x*20),Mathf.RoundToInt(p.y*20),Mathf.RoundToInt(p.z*20));if(lookup.TryGetValue(key,out int id))return id;lookup[key]=points.Count;points.Add(p);return points.Count-1;}
        foreach(var spring in Spring.Active){if(!spring||!spring.Live||spring.Route==null)continue;
            var r=spring.Route;for(int i=1;i<r.Points.Count;i++){var a=spring.transform.position+V(r.Points[r.Parent(i)]);var b=spring.transform.position+V(r.Points[i]);
                var span=new RiverGardens.Span(a,b,r.Width);var p=span.Closest(stone);var delta=p-stone;delta.y=0;if(delta.sqrMagnitude>32*32)continue;
                edges.Add((Vertex(a),Vertex(b),r.Width));}}
        var result=new List<RiverGardens.Span>();int nearest=-1;float best=float.PositiveInfinity,at=0;
        for(int i=0;i<edges.Count;i++){var e=edges[i];var a=points[e.a];var b=points[e.b];var span=new RiverGardens.Span(a,b,e.width);var p=span.Closest(stone);var d=stone-p;d.y=0;
            if(d.sqrMagnitude<best&&d.magnitude<=e.width*.5f+4){best=d.sqrMagnitude;nearest=i;at=Horizontal(a,p)/Mathf.Max(.001f,Horizontal(a,b));}}
        if(nearest<0)return result;
        var distance=new float[points.Count];var done=new bool[points.Count];for(int i=0;i<distance.Length;i++)distance[i]=float.PositiveInfinity;
        var seed=edges[nearest];float length=Horizontal(points[seed.a],points[seed.b]);distance[seed.a]=length*at;distance[seed.b]=length*(1-at);
        for(int count=0;count<points.Count;count++){
            int index=-1;float minimum=10.001f;for(int i=0;i<distance.Length;i++)if(!done[i]&&distance[i]<minimum){minimum=distance[i];index=i;}
            if(index<0)break;done[index]=true;
            foreach(var edge in edges){int next=edge.a==index?edge.b:edge.b==index?edge.a:-1;if(next>=0)distance[next]=Mathf.Min(distance[next],minimum+Horizontal(points[index],points[next]));}
        }
        for(int i=0;i<edges.Count;i++){var e=edges[i];var a=points[e.a];var b=points[e.b];float len=Horizontal(a,b),lo=0,hi=1;
            if(i==nearest){lo=Mathf.Clamp01(at-10/len);hi=Mathf.Clamp01(at+10/len);}
            else {bool fromA=distance[e.a]<10,fromB=distance[e.b]<10;if(!fromA&&!fromB)continue;if(!fromA)lo=Mathf.Clamp01(1-(10-distance[e.b])/len);if(!fromB)hi=Mathf.Clamp01((10-distance[e.a])/len);}
            if(hi>lo)result.Add(new RiverGardens.Span(Vector3.Lerp(a,b,lo),Vector3.Lerp(a,b,hi),e.width));
        }return result;
    }
    static float Horizontal(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    internal static Vector3 V(Node p)=>new Vector3(p.X,p.Y,p.Z);
}

// Analytic placement ray: floating decor needs a water hit, not another collection of mesh colliders.
[HarmonyPatch(typeof(Player),"PieceRayTest")]
internal static class RiverPlacementRay
{
    static void Postfix(GameObject ___m_placementGhost,Transform ___m_eye,float ___m_maxPlaceDistance,int ___m_placeRayMask,
        ref bool __result,ref Vector3 point,ref Vector3 normal,ref Piece piece,ref Heightmap heightmap,ref Collider waterSurface)
    {
        if(!___m_placementGhost||!GameCamera.instance)return;
        var placementPiece=___m_placementGhost.GetComponent<Piece>();
        if(!(placementPiece&&placementPiece.m_waterPiece))return;
        var camera=GameCamera.instance.transform;var ray=new Ray(camera.position,camera.forward);
        float nearest=__result?Vector3.Distance(ray.origin,point):50;Vector3 selected=default;Collider selectedVolume=null;
        if(Physics.Raycast(ray,out var obstruction,nearest,___m_placeRayMask,QueryTriggerInteraction.Ignore))nearest=obstruction.distance;
        foreach(var entry in RiverLiquid.Registry){var liquid=entry.Value;if(!liquid)continue;
            if(liquid.Pond){
                if(Mathf.Abs(ray.direction.y)<.00001f)continue;
                float t=(GardenIntegration.WaterHeight(liquid.Pond)-ray.origin.y)/ray.direction.y;
                if(t<0||t>=nearest)continue;var hit=ray.GetPoint(t);
                for(int refine=0;refine<4;refine++){float level=GardenIntegration.WaterLevel(hit);if(float.IsNegativeInfinity(level))break;t=(level-ray.origin.y)/ray.direction.y;hit=ray.GetPoint(t);}
                if(t<0||t>=nearest||GardenIntegration.Distance(liquid.Pond,hit)>1)continue;
                if(Vector3.Distance(___m_eye.position,hit)>=___m_maxPlaceDistance+___m_placementGhost.GetComponent<Piece>().m_extraPlacementDistance)continue;
                if(RiverTerrain.SampleCurrent(hit,out float ground)&&ground>=hit.y-.015f)continue;
                nearest=t;selected=hit;selectedVolume=entry.Key.GetComponent<Collider>();continue;
            }
            if(!liquid.Spring||liquid.Spring.Route==null)continue;
            var spring=liquid.Spring;var r=spring.Route;
            for(int i=1;i<r.Points.Count;i++){
                var a=spring.transform.position+GardenBridge.V(r.Points[r.Parent(i)]);var b=spring.transform.position+GardenBridge.V(r.Points[i]);var d=b-a;
                float len2=d.x*d.x+d.z*d.z;if(len2<.001f)continue;
                float sx=d.y*d.x/len2,sz=d.y*d.z/len2,denom=ray.direction.y-sx*ray.direction.x-sz*ray.direction.z;if(Mathf.Abs(denom)<.00001f)continue;
                float t=(a.y-ray.origin.y+sx*(ray.origin.x-a.x)+sz*(ray.origin.z-a.z))/denom;if(t<0||t>=nearest)continue;
                // The blended river surface is curved at bends. Refine the plane
                // intersection against the same height function used by buoyancy.
                for(int refine=0;refine<8;refine++){
                    var probe=ray.GetPoint(t)-spring.transform.position;
                    spring.Surface(probe.x,probe.z,out _,out float h,out _);
                    h=OceanJoin.Level(probe+spring.transform.position,h+spring.transform.position.y)-spring.transform.position.y;
                    float error=probe.y-h;if(Mathf.Abs(error)<.005f)break;
                    var forward=probe+ray.direction*.02f;
                    spring.Surface(forward.x,forward.z,out _,out float h2,out _);
                    h2=OceanJoin.Level(forward+spring.transform.position,h2+spring.transform.position.y)-spring.transform.position.y;
                    float derivative=ray.direction.y-(h2-h)/.02f;
                    if(float.IsNaN(derivative)||Mathf.Abs(derivative)<.0001f)break;
                    t-=Mathf.Clamp(error/derivative,-2,2);
                    if(t<0||t>=nearest)break;
                }
                if(float.IsNaN(t)||t<0||t>=nearest)continue;
                var hit=ray.GetPoint(t);float along=((hit.x-a.x)*d.x+(hit.z-a.z)*d.z)/len2;if(along<0||along>1)continue;
                var local=hit-spring.transform.position;if(!spring.Surface(local.x,local.z,out _,out var level,out _))continue;
                if(Mathf.Abs(hit.y-OceanJoin.Level(hit,spring.transform.position.y+level))>.1f)continue;
                if(RiverTerrain.SampleCurrent(hit,out var ground)&&ground>=hit.y-.015f)continue;
                if(Vector3.Distance(___m_eye.position,hit)>=___m_maxPlaceDistance+___m_placementGhost.GetComponent<Piece>().m_extraPlacementDistance)continue;
                nearest=t;selected=hit;selectedVolume=entry.Key.GetComponent<Collider>();
            }
        }
        if(!selectedVolume)return;
        point=selected;normal=Vector3.up;piece=null;heightmap=Heightmap.FindHeightmap(point);waterSurface=selectedVolume;__result=true;
    }
}
