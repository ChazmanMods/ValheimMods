using System;
using System.Collections.Generic;
using System.Linq;
namespace RunicSettlements.Core;

public sealed class SettlementPlanner
{
    private readonly ITerrain terrain;
    private readonly Func<Point,bool> meadow;
    private readonly List<Exclusion> protection;
    public string Status {get;private set;}="Waiting";
    public SettlementPlanner(ITerrain terrain,Func<Point,bool> meadow,List<Exclusion> protection)
    {this.terrain=terrain;this.meadow=meadow;this.protection=protection;}
    public IEnumerable<int> Build(SettlementPlan plan,Action<SettlementPlan>? checkpoint=null)
    {
        var o=plan.Options;var land=new HashSet<Cell>();var seen=new HashSet<Cell>();
        var queue=new Queue<Cell>();queue.Enqueue(Cell.From(plan.Spawn,24));
        var survey=new Planner(terrain,new PlannerSettings {WaterLevel=o.Water,Width=o.Width});
        Status="Surveying dry land connected to spawn";
        while(queue.Count>0)
        {
            var c=queue.Dequeue();if(!seen.Add(c)) continue;Point p=c.At(24);
            if(Point.Distance(p,plan.Spawn)>o.SearchRadius || !survey.Dry(p)) continue;
            if(land.Count>=30000) break;
            land.Add(c);
            foreach(var d in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
            {var n=new Cell(c.X+d.X,c.Z+d.Z);if(!seen.Contains(n) && survey.SafeSegment(p,n.At(24),false)) queue.Enqueue(n);}
            yield return 0;
        }
        var candidates=new List<(SettlementSite Site,float Score)>();
        int scanned=0,protectedCount=0,steepCount=0,denseRejected=0,routeRejected=0;
        foreach(var cell in land.OrderBy(c=>c))
        {

            Point center=cell.At(24);float reach=o.Radius+o.Blend;
            if(Point.Distance(center,plan.Spawn)<reach+100 || !meadow(center)) continue;
            if(protection.Any(e=>!e.Replaceable && e.Contains(center,reach+4))) {protectedCount++;continue;}
            float lo=float.MaxValue,hi=float.MinValue;bool valid=true;
            for(float x=-o.Radius;x<=o.Radius && valid;x+=12)
            for(float z=-o.Radius;z<=o.Radius && valid;z+=12)
            {
                if(x*x+z*z>o.Radius*o.Radius) continue;
                Point p=center+new Point(x,z);float h=terrain.Height(p);lo=Math.Min(lo,h);hi=Math.Max(hi,h);
                if(h<o.Water+3 || !meadow(p) || hi-lo>o.MaximumRaise+o.MaximumLower) valid=false;
                yield return 0;
            }
            if(valid)
            {
                float height=(hi-o.MaximumLower+lo+o.MaximumRaise)/2;
                candidates.Add((new SettlementSite {Center=center,Height=height},(hi-lo)*25+Point.Distance(center,plan.Spawn)*.02f));
            }
            if(!valid) steepCount++;
            scanned++;Status=$"Evaluating {o.Radius*2} m clearings: {scanned} candidates checked; {candidates.Count} suitable footprints";
            yield return 0;
        }
        var routing=new PlannerSettings {Width=o.Width,MaxSlope=o.Slope,MaximumRaise=o.MaximumRaise,MaximumLower=o.MaximumLower,WaterLevel=o.Water,MaxSearchNodes=6000,MaximumGap=0};
        if(plan.Sites.Count==0)
        {
            bool found=false;
            // Start just beyond the spawn monument footprint, using the closest safe approach.
            for(float r=24;r<=160 && !found;r+=8) for(int i=0;i<24 && !found;i++)
            {var p=plan.Spawn+new Point((float)Math.Cos(i*Math.PI/12),(float)Math.Sin(i*Math.PI/12))*r;
             if(!protection.Any(e=>e.Contains(p,o.Width/2+3)) && survey.SafeSegment(p,p)) {plan.Anchor=p;found=true;}}
            if(!found) throw new InvalidOperationException("No safe road approach outside the spawn monument");
        }
        int attempts=0;
        while(plan.Sites.Count<o.Count && candidates.Count>0 && attempts<40)
        {
            var choice=candidates.Where(c=>plan.Sites.All(s=>Point.Distance(c.Site.Center,s.Center)>=Math.Max(o.Separation,2*(o.Radius+o.Blend)+20)))
                .OrderBy(c=>c.Score-(plan.Sites.Count==0?0:Math.Min(700,plan.Sites.Min(s=>Point.Distance(s.Center,c.Site.Center)))*.08f))
                .ThenBy(c=>c.Site.Center.X).ThenBy(c=>c.Site.Center.Z).FirstOrDefault();
            if(choice.Site==null) break;candidates.Remove(choice);attempts++;
            var site=choice.Site;bool valid=true;
            // Dense final inspection prevents sampled hills/shorelines inside a selected platform.
            for(float x=-o.Radius-o.Blend;x<=o.Radius+o.Blend && valid;x+=2)
            for(float z=-o.Radius-o.Blend;z<=o.Radius+o.Blend && valid;z+=2)
            {
                float d=(float)Math.Sqrt(x*x+z*z);if(d>o.Radius+o.Blend) continue;
                Point p=site.Center+new Point(x,z);float h=terrain.Height(p);
                float t=Math.Max(0,Math.Min(1,(o.Radius+o.Blend-d)/o.Blend));t=t*t*(3-2*t);
                float delta=(site.Height-h)*t;
                if(h<o.Water+2 || !meadow(p) || delta>o.MaximumRaise-.05f || -delta>o.MaximumLower-.05f) valid=false;
                yield return 0;
            }
            if(!valid) {denseRejected++;continue;}
            Status=$"Connecting community site {plan.Sites.Count+1}/{o.Count} (candidate attempt {attempts}/40)";
            var sources=plan.Sites.Select(s=>s.Center).Append(plan.Anchor).OrderBy(p=>Point.Distance(p,site.Center)).Take(2).ToArray();
            plan.Sites.Add(site);var planner=new Planner(new SettlementTerrain(terrain,plan),routing,protection.Where(e=>!e.Replaceable || !plan.Sites.Any(s=>Point.Distance(s.Center,e.Center)<o.Radius+o.Blend+e.Radius)).ToList());
            List<Point>? route=null;
            foreach(var source in sources)
            {
                foreach(int step in planner.FindRoute(source,site.Center,p=>route=p)) yield return step;
                if(route!=null) break;
            }
            if(route==null) {routeRejected++;plan.Sites.Remove(site);continue;}
            foreach(int step in planner.Smooth(route)) yield return step;
            site.Name="Settlement "+plan.Sites.Count;plan.Roads.Add(new Route {Points=route});
            checkpoint?.Invoke(plan);yield return 0;
        }
        // One bounded alternate connection creates a useful loop between sites.
        if(plan.Sites.Count>=3)
        {
            var pairs=new List<(Point A,Point B)>();
            for(int a=0;a<plan.Sites.Count;a++) for(int b=a+1;b<plan.Sites.Count;b++)
            {
                Point pa=plan.Sites[a].Center,pb=plan.Sites[b].Center;
                if(!plan.Roads.Any(r=>(r.Points[0].Equals(pa) && r.Points.Last().Equals(pb)) || (r.Points[0].Equals(pb) && r.Points.Last().Equals(pa)))) pairs.Add((pa,pb));
            }
            if(pairs.Count>0)
            {
                var pair=pairs.OrderBy(p=>Point.Distance(p.A,p.B)).First();
                var planner=new Planner(new SettlementTerrain(terrain,plan),routing,protection.Where(e=>!e.Replaceable || !plan.Sites.Any(s=>Point.Distance(s.Center,e.Center)<o.Radius+o.Blend+e.Radius)).ToList());List<Point>? loop=null;
                Status="Checking an additional road between settlements";
                foreach(int step in planner.FindRoute(pair.A,pair.B,p=>loop=p)) yield return step;
                if(loop!=null) {foreach(int step in planner.Smooth(loop)) yield return step;plan.Roads.Add(new Route {Points=loop});}
            }
        }
        plan.Complete=true;
        plan.Report=$"Prepared {plan.Sites.Count} of {o.Count} requested sites, diameter {o.Radius*2} m, {plan.Roads.Count} connecting roads. Fewer sites means no additional safe, separated, road-connected footprint was found within the bounded search. Geometry is locked for this world. Survey: {land.Count} land cells; {scanned} evaluated; {protectedCount} protected; {steepCount} rough/wet; {denseRejected} rejected by dense inspection; {routeRejected} unreachable.";
        Status=plan.Report;checkpoint?.Invoke(plan);
    }
}
