using System.Collections.Generic;
using System.Linq;

namespace RunicSettlements.Core;

public static class NetworkUpdate
{
    // Planning extends a persistent road network. Established roads never disappear
    // merely because a new search chose different connections or reached its budget.
    public static Island Preserve(Island planned, IEnumerable<Island> existing)
    {
        var result = new Island { PlanningSignature=planned.PlanningSignature,
            PoiCount=planned.PoiCount, ConnectedPairs=planned.ConnectedPairs, Outcomes=planned.Outcomes };
        var cells = new HashSet<Cell>();
        var edges = new HashSet<(Point,Point,string)>();
        var exclusions = new HashSet<(Point,float,float,float)>();
        foreach(var island in existing.Concat(new[]{planned}))
        {
            foreach(var cell in island.Cells) cells.Add(cell);
            foreach(var exclusion in island.Exclusions)
                if(exclusions.Add((exclusion.Center,exclusion.Radius,exclusion.HalfX,exclusion.HalfZ))) result.Exclusions.Add(exclusion);
            foreach(var route in island.Routes)
            {
                var points = new List<Point>();
                void Flush() {if(points.Count>1) result.Routes.Add(new Route{Surface=route.Surface,Points=points});points=new List<Point>();}
                for(int i=1;i<route.Points.Count;i++)
                {
                    var a=route.Points[i-1];var b=route.Points[i];
                    var edge = a.X<b.X || (a.X==b.X && a.Z<=b.Z) ? (a,b,route.Surface) : (b,a,route.Surface);
                    if(!edges.Add(edge)) {Flush();continue;}
                    if(points.Count==0) points.Add(a);
                    points.Add(b);
                }
                Flush();
            }
        }
        result.Cells=cells.OrderBy(c=>c).ToList();
        return result;
    }
}
