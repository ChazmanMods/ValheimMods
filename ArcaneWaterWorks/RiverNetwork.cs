using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArcaneWaterWorks;

internal static class RiverNetwork
{
    static bool Connected(Spring a,Spring b){
        bool StartsOn(Spring start,Spring other){
            var p=start.transform.position+GardenBridge.V(start.Route.Points[0]);
            foreach(var node in other.Route.Points)if((p-other.transform.position-GardenBridge.V(node)).sqrMagnitude<.0004f)return true;
            return false;
        }
        if(StartsOn(a,b)||StartsOn(b,a))return true;
        var bounds=RiverTerrain.Bounds(a,a.Route);bounds.Expand(new Vector3(0,16,0));
        if(!bounds.Intersects(RiverTerrain.Bounds(b,b.Route)))return false;
        bool Crosses(Spring source,Spring target){
            var route=source.Route;
            for(int i=1;i<route.Points.Count;i++){
                var start=route.Points[route.Parent(i)];var end=route.Points[i];int count=Mathf.Max(1,Mathf.CeilToInt(Node.Distance(start,end)));
                for(int j=0;j<=count;j++){
                    var local=GardenBridge.V(Node.Lerp(start,end,j/(float)count));var p=source.transform.position+local-target.transform.position;
                    target.Route.Surface(p.x,p.z,out float distance,out float height,out _);
                    if(distance<source.Route.Width*.5f+target.Route.Width*.5f+1.5f&&Mathf.Abs(p.y-height)<6)return true;
                }
            }return false;
        }
        return Crosses(a,b)||Crosses(b,a);
    }
    internal static void Refresh(){
        var active=Spring.Active.Where(s=>s&&s.Live&&s.Route!=null).ToList();
        foreach(var s in active){s.BankRoute=s.Route.FitBanks(s.Ground,s.Sea);s.JoinedRoute=null;s.SurfaceMembers=null;s.SurfaceHeights=null;s.JoinedOrigin=s.transform.position;}
        var remaining=new HashSet<Spring>(active);
        while(remaining.Count>0){
            var group=new List<Spring>{remaining.First()};remaining.Remove(group[0]);
            for(int i=0;i<group.Count;i++)foreach(var other in remaining.ToArray())if(Connected(group[i],other)){group.Add(other);remaining.Remove(other);}
            if(group.Count<2)continue;
            group=group.OrderByDescending(s=>s.transform.position.y+s.Route.Points[0].Y).ThenBy(s=>s.transform.position.x).ThenBy(s=>s.transform.position.z).ToList();
            var origin=group[0].transform.position;
            var inputs=group.Select(s=>(s.Route,new Node(s.transform.position.x-origin.x,s.transform.position.y-origin.y,s.transform.position.z-origin.z))).ToList();
            var joined=RiverJoin.Combine(inputs);
            if(joined!=null)joined=joined.FitBanks((x,z)=>RiverTerrain.SampleBase(origin+new Vector3(x,0,z),out float ground)?ground-origin.y:(float?)null,OceanJoin.Sea-origin.y);
            var heights=new Dictionary<(float,float),float>();
            foreach(var s in group){s.JoinedRoute=joined;s.SurfaceMembers=group;s.SurfaceHeights=heights;s.JoinedOrigin=origin;}
        }
    }
}
