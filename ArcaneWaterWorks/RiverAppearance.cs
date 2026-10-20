using System.Collections.Generic;
using UnityEngine;

namespace ArcaneWaterWorks;

internal static class RiverAppearance
{
    internal static float Depth(Spring river){
        if(river.SurfaceMembers==null)return river.Route.Depth;
        float total=0;int count=0;foreach(var member in river.SurfaceMembers)if(member&&member.Route!=null){total+=member.Route.Depth;count++;}
        return count>0?total/count:river.Route.Depth;
    }
    // Sample a shared world-space field, not the settings of whichever mesh owns
    // this triangle. Saved depth/rapids changes then blend across section edges.
    internal static Color Sample(Vector3 world,IReadOnlyList<Spring> rivers)=>Sample(world,rivers,out _);
    internal static Color Sample(Vector3 world,IReadOnlyList<Spring> rivers,out Vector2 flow){
        flow=Vector2.zero;
        float weight=0,depth=0,foam=0;
        foreach(var river in rivers){
            if(!river||river.Route==null)continue;
            var p=world-river.transform.position;var route=river.Route;
            river.Surface(p.x,p.z,out float distance,out float height,out _);
            float horizontal=1-Mathf.SmoothStep(0,1,Mathf.Max(0,distance-route.Width*.5f)/4);
            float vertical=1-Mathf.SmoothStep(0,1,Mathf.Abs(p.y-height)/4);
            float w=horizontal*vertical;if(w<=0)continue;
            weight+=w;depth+=route.Depth*w;
            foam+=route.Whitewater*w;
            var flowRoute=river.JoinedRoute??river.BankRoute??route;var fp=river.JoinedRoute!=null?world-river.JoinedOrigin:p;
            flowRoute.Flow(fp.x,fp.z,out float vx,out float vz);flow+=new Vector2(vx,vz)*w;
        }
        if(weight>0)flow*=1.7f/weight;
        return weight>0?new Color(depth/weight,foam/weight,0,1):new Color(1,0,0,1);
    }
    internal static void ConfigureLighting(MeshRenderer renderer){
        // Separate section bounds must not select different probes at their seam.
        renderer.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        if(Player.m_localPlayer)renderer.probeAnchor=Player.m_localPlayer.transform;
    }
}
