using System;
using System.Collections.Generic;

namespace ArcaneWaterWorks;

// Temporary, unsaved surface geometry for connected streamed sections.
internal static class RiverJoin
{
    internal static Route Combine(IReadOnlyList<(Route route,Node origin)> sections){
        var result=new Route{Width=sections[0].route.Width,Depth=sections[0].route.Depth};
        foreach(var section in sections)result.Width=Math.Max(result.Width,section.route.Width);
        var remaining=new List<(Route route,Node origin)>(sections);
        remaining.Reverse();
        int Find(Node p){for(int i=0;i<result.Points.Count;i++){
            var q=result.Points[i];if(Node.Distance(p,q)<.02f&&Math.Abs(p.Y-q.Y)<.02f)return i;
        }return -1;}
        bool progress=true;
        while(remaining.Count>0&&progress){
            progress=false;
            for(int s=remaining.Count-1;s>=0;s--){
                var section=remaining[s];var r=section.route;var o=section.origin;
                Node World(Node p)=>new Node(p.X+o.X,p.Y+o.Y,p.Z+o.Z);
                int first=Find(World(r.Points[0]));
                if(result.Points.Count>0&&first<0)continue;
                if(first<0){first=0;result.Points.Add(World(r.Points[0]));}
                var ids=new int[r.Points.Count];ids[0]=first;
                for(int i=1;i<r.Points.Count;i++){
                    var p=World(r.Points[i]);int existing=Find(p);
                    if(existing>=0){ids[i]=existing;continue;}
                    ids[i]=result.Points.Count;result.Points.Add(new Node(p.X,p.Y,p.Z,ids[r.Parent(i)],r.IsOutlet(i)));
                }
                remaining.RemoveAt(s);progress=true;
            }
        }
        return remaining.Count==0?result:null;
    }
}
