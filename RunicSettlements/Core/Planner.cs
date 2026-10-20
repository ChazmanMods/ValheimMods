using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicSettlements.Core;

/// <summary>Pure deterministic planner. Iterators yield work units for a main-thread time budget.</summary>
public sealed class Planner
{
    private readonly ITerrain terrain;
    private readonly PlannerSettings settings;
    private readonly Dictionary<Cell, float> heights = new Dictionary<Cell, float>();
    private readonly HashSet<Cell> roadCells=new HashSet<Cell>();
    private readonly List<Exclusion> exclusions;
    private readonly Dictionary<Cell, List<Exclusion>> exclusionGrid = new Dictionary<Cell, List<Exclusion>>();
    public Island? Result { get; private set; }
    public string Status { get; private set; } = "Waiting";
    public bool LimitReached { get; private set; }
    public IReadOnlyCollection<Cell> SurveyedCells { get; private set; } = Array.Empty<Cell>();
    public int HeightSamples => heights.Count;
    public int SearchNodes { get; private set; }
    public long TotalSearchNodes { get; private set; }
    private int linkNodes, linkLimit;
    private bool boundedSearch;
    public PlanningProgress? Progress { get; private set; }
    private static readonly Cell[] Directions = { new Cell(1, 0), new Cell(0, 1), new Cell(-1, 0), new Cell(0, -1) };
    public Planner(ITerrain terrain, PlannerSettings settings, List<Exclusion>? exclusions = null)
    {
        this.terrain = terrain; this.settings = settings; this.exclusions = exclusions ?? new List<Exclusion>();
        foreach (Exclusion e in this.exclusions)
        {
            float r = e.Radius + settings.Width + 4;
            Cell lo = Cell.From(e.Center - new Point(r, r), 128), hi = Cell.From(e.Center + new Point(r, r), 128);
            for (int x = lo.X; x <= hi.X; x++)
            for (int z = lo.Z; z <= hi.Z; z++)
            {
                var c = new Cell(x, z);
                if (!exclusionGrid.TryGetValue(c, out var list)) exclusionGrid[c] = list = new List<Exclusion>();
                list.Add(e);
            }
        }
    }

    private float H(Point p)
    {
        Cell c = Cell.From(p, 1);
        if (!heights.TryGetValue(c, out float h)) heights[c] = h = terrain.Height(c.At(1));
        return h;
    }
    public bool Dry(Point p) => H(p) >= settings.WaterLevel + settings.ShoreClearance;

    public bool Passable(Point p, bool road)
    {
        if (p.Length > 9900 || !Dry(p)) return false;
        if (!road) return true;
        if (!terrain.Allowed(p) || (exclusionGrid.TryGetValue(Cell.From(p, 128), out var list) && list.Exists(e => e.Contains(p, 2)))) return false;
        return true;
    }

    public bool SafeSegment(Point a, Point b, bool road = true)
    {
        float length = Point.Distance(a, b);
        int steps = Math.Max(1, (int)Math.Ceiling(length / 2));
        float last = H(a);
        Point perpendicular = length > 0 ? new Point(-(b.Z - a.Z) / length, (b.X - a.X) / length) * (settings.Width * 0.5f + 0.8f) : new Point();
        for (int i = 0; i <= steps; i++)
        {
            Point p = Point.Lerp(a, b, i / (float)steps);
            if (!Passable(p, road)) return false;
            float h = H(p);
            if (road)
            {
                if (!Passable(p + perpendicular, true) || !Passable(p - perpendicular, true)) return false;
                if (i > 0 && Math.Abs(h - last) > settings.MaxSlope * length / steps + 0.05f) return false;
                // A contouring road may cut its uphill shoulder and fill its downhill
                // shoulder, but only within the configured earthwork limits.
                float left=H(p+perpendicular),right=H(p-perpendicular);
                if(Math.Max(left,right)-h>settings.Width*0.35f+settings.MaximumLower ||
                    h-Math.Min(left,right)>settings.Width*0.35f+settings.MaximumRaise) return false;
            }
            last = h;
        }
        return true;
    }

    private bool PotentialGap(Point a, Point b)
    {
        if(settings.MaximumGap<=0) return false;
        int steps=Math.Max(1,(int)Math.Ceiling(Point.Distance(a,b)/2));
        float edge=H(a);
        for(int i=1;i<=steps;i++)
        {
            Point p=Point.Lerp(a,b,i/(float)steps);
            if(!Dry(p) || H(p)<edge-3) return true;
        }
        return false;
    }
    // A logical connection only: terrain is never painted across the gap itself.
    public bool GapSegment(Point a, Point b)
    {
        if(settings.MaximumGap<=0 || !SafeSegment(a,a) || !SafeSegment(b,b)) return false;
        float length=Point.Distance(a,b);
        if(length>settings.MaximumGap+48) return false;
        int steps=Math.Max(1,(int)Math.Ceiling(length));
        float gap=0; bool barrier=false;
        float low=Math.Min(H(a),H(b));
        for(int i=1;i<steps;i++)
        {
            Point p=Point.Lerp(a,b,i/(float)steps);
            if(exclusionGrid.TryGetValue(Cell.From(p,128),out var protectedAreas) && protectedAreas.Exists(e=>e.Contains(p,settings.Width/2+1))) return false;
            bool missing=!Dry(p) || H(p)<low-3;
            if(missing) {gap+=length/steps;barrier=true;}
            else if(!terrain.Allowed(p) || H(p)>Math.Max(H(a),H(b))+3) return false;
        }
        return barrier && gap<=settings.MaximumGap;
    }

    public List<Route> SplitGaps(List<Point> path)
    {
        var result=new List<Route>();var current=new List<Point>();
        void Flush() {if(current.Count>=2) result.Add(new Route {Points=current});current=new List<Point>();}
        foreach(var first in path.Take(1)) current.Add(first);
        for(int edge=1;edge<path.Count;edge++)
        {
            Point a=path[edge-1],b=path[edge];
            if(SafeSegment(a,b)) {current.Add(b);continue;}
            int steps=Math.Max(1,(int)Math.Ceiling(Point.Distance(a,b)));
            Point previous=a;
            for(int i=1;i<=steps;i++)
            {
                Point p=Point.Lerp(a,b,i/(float)steps);
                if(!SafeSegment(previous,p)) break;
                current.Add(p);previous=p;
            }
            Flush();var farBank=new List<Point>{b};previous=b;
            for(int i=1;i<=steps;i++)
            {
                Point p=Point.Lerp(b,a,i/(float)steps);
                if(!SafeSegment(previous,p)) break;
                farBank.Add(p);previous=p;
            }
            farBank.Reverse();current=farBank;
        }
        Flush();return result;
    }
    private Cell? FindLand(Point start)
    {
        Cell center = Cell.From(start, PlannerSettings.IslandGrid);
        for (int r = 0; r <= 2; r++)
        for (int x = -r; x <= r; x++)
        for (int z = -r; z <= r; z++)
        {
            Cell c = new Cell(center.X + x, center.Z + z);
            if (Passable(c.At(PlannerSettings.IslandGrid), false) && SafeSegment(start, c.At(PlannerSettings.IslandGrid), false)) return c;
        }
        return null;
    }

    public IEnumerable<int> Build(Point landing, IReadOnlyList<Poi> pois, PlanningProgress? resume = null, Action<PlanningProgress>? checkpoint = null)
    {
        Result = null;
        LimitReached = false;
        roadCells.Clear(); TotalSearchNodes=0;
        if(resume!=null && !resume.Landing.Equals(landing)) throw new ArgumentException("Checkpoint landing mismatch");
        Cell? origin = resume==null ? FindLand(landing) : resume.Island.Cells.First();
        if (!origin.HasValue) { Status = "No connected dry land near landing"; yield break; }
        Status = "Surveying island";
        var island = resume==null ? new HashSet<Cell> { origin.Value } : new HashSet<Cell>(resume.Island.Cells);
        SurveyedCells = island;
        var queue = new Queue<Cell>(); if(resume==null) queue.Enqueue(origin.Value);
        var wet = new HashSet<Cell>();
        while (queue.Count > 0)
        {
            Cell c = queue.Dequeue();
            foreach (Cell d in Directions)
            {
                Cell n = new Cell(c.X + d.X, c.Z + d.Z);
                if (island.Contains(n) || wet.Contains(n)) continue;
                if (!Passable(n.At(PlannerSettings.IslandGrid), false) || !SafeSegment(c.At(PlannerSettings.IslandGrid), n.At(PlannerSettings.IslandGrid), false))
                {
                    bool found=false;
                    for(int jump=1;jump<=Math.Ceiling((settings.MaximumGap+48)/PlannerSettings.IslandGrid) && settings.MaximumGap>0;jump++)
                    {
                        var across=new Cell(c.X+d.X*jump,c.Z+d.Z*jump);
                        if(island.Contains(across)) continue;
                        if(GapSegment(c.At(PlannerSettings.IslandGrid),across.At(PlannerSettings.IslandGrid))) {n=across;found=true;break;}
                    }
                    if(!found) continue;
                }
                island.Add(n); queue.Enqueue(n);
                if (island.Count > settings.MaxIslandCells)
                { LimitReached = true; Status = "Island survey limit reached; no partial network committed"; yield break; }
            }
            yield return 0;
        }
        var result = resume?.Island ?? new Island { Cells = island.OrderBy(c => c).ToList() };
        bool OnIsland(Point p)
        {
            Cell c = Cell.From(p, PlannerSettings.IslandGrid);
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Cell n = new Cell(c.X + x, c.Z + z);
                if (island.Contains(n) && SafeSegment(p, n.At(PlannerSettings.IslandGrid), false)) return true;
            }
            return false;
        }
        var chosen = new List<Poi>();
        foreach (Poi p in (resume?.Pois ?? pois).OrderBy(p => p.Name == "StartTemple" ? 0 : 1).ThenBy(p => p.Position.X).ThenBy(p => p.Position.Z))
        {
            if (resume!=null || OnIsland(p.Position)) chosen.Add(p);
            yield return 0;
        }
        // Evenly retain POIs across large islands rather than taking only one geographical corner.
        if (settings.MaxPois > 0 && chosen.Count > settings.MaxPois)
        {
            var sparse = new List<Poi> { chosen[0] };
            while (sparse.Count < settings.MaxPois)
            {
                Poi next = chosen.Where(p => !sparse.Contains(p)).OrderByDescending(p => sparse.Min(q => Point.Distance(p.Position, q.Position))).First();
                sparse.Add(next); yield return 0;
            }
            chosen = sparse;
        }
        result.PoiCount = chosen.Count;
        result.Exclusions = exclusions.Where(e => island.Contains(Cell.From(e.Center, PlannerSettings.IslandGrid)) || chosen.Exists(p => Point.Distance(p.Position, e.Center) < 1)).ToList();
        var edges = new List<(int A, int B, float D)>();
        for (int a = 0; a < chosen.Count; a++)
        for (int b = a + 1; b < chosen.Count; b++)
        {
            float distance = Point.Distance(chosen[a].Position, chosen[b].Position);
            if (distance <= settings.MaxConnection) edges.Add((a, b, distance));
        }
        edges.Sort((a, b) => { int d = a.D.CompareTo(b.D); if (d != 0) return d; d = a.A.CompareTo(b.A); return d != 0 ? d : a.B.CompareTo(b.B); });
        // Limit candidates, not POIs: each location contributes its eight nearest links.
        var candidates=new HashSet<(int,int)>();
        for(int i=0;i<chosen.Count;i++)
            foreach(var e in edges.Where(e=>e.A==i || e.B==i).Take(8)) candidates.Add((e.A,e.B));
        edges.RemoveAll(e=>!candidates.Contains((e.A,e.B)));
        Progress=resume ?? new PlanningProgress {Landing=landing,Island=result,Pois=chosen};
        TotalSearchNodes=Progress.TotalNodes;
        checkpoint?.Invoke(Progress);
        int[] roots = Enumerable.Range(0, chosen.Count).ToArray();
        var anchors=new Dictionary<int,Point>();
        var connectedEdges=new HashSet<(int,int)>();
        var graph=Enumerable.Range(0,chosen.Count).Select(_=>new List<(int Node,float Distance)>()).ToArray();
        void Remember(int a,int b,List<Point> path)
        {
            float length=0;
            for(int j=1;j<path.Count;j++)
            {
                float distance=Point.Distance(path[j-1],path[j]);length+=distance;
                for(int k=0;k<=Math.Ceiling(distance/2);k++) roadCells.Add(Cell.From(Point.Lerp(path[j-1],path[j],Math.Min(1,k*2/Math.Max(.01f,distance))),4));
            }
            graph[a].Add((b,length));graph[b].Add((a,length));connectedEdges.Add((a,b));
            if(!anchors.ContainsKey(a)) anchors[a]=path[0];
            if(!anchors.ContainsKey(b)) anchors[b]=path[path.Count-1];
        }
        int Root(int n) { while (roots[n] != n) n = roots[n]; return n; }
        foreach(var saved in Progress.Links)
        { Remember(saved.A,saved.B,saved.Path); roots[Root(saved.B)]=Root(saved.A); }
        boundedSearch=true;
        int attempts = Progress.Attempts;
        for(int edgeIndex=Progress.Cursor;edgeIndex<edges.Count;edgeIndex++)
        {
            var edge=edges[edgeIndex];
            if (Root(edge.A) == Root(edge.B)) continue;
            attempts++; linkNodes=0;
            linkLimit=Progress.FailedNodes>=settings.MaxSearchNodes*12L ? Math.Min(768,settings.MaxSearchNodes*3) : settings.MaxSearchNodes*3;
            Status = $"{result.ConnectedPairs} links built; candidate {edgeIndex+1}/{edges.Count} ({attempts} searches); {chosen.Count} POIs";
            List<Point>? path = null;
            // A safe point on a ridge may be unreachable from the opposite POI.
            // Try another side of each footprint before abandoning the connection.
            int combinations=(anchors.ContainsKey(edge.A)?1:2)*(anchors.ContainsKey(edge.B)?1:2);
            for(int attempt=0;attempt<combinations && path==null;attempt++)
            {
                Point? start = anchors.TryGetValue(edge.A,out var fixedStart)?fixedStart:Approach(chosen[edge.A], chosen[edge.B].Position,attempt%2);
                Point? end = anchors.TryGetValue(edge.B,out var fixedEnd)?fixedEnd:Approach(chosen[edge.B], chosen[edge.A].Position,anchors.ContainsKey(edge.A)?attempt:attempt/2);
                if (!start.HasValue || !end.HasValue) continue;
                foreach (int step in FindRoute(start.Value, end.Value, p => path = p)) yield return step;
            }
            Progress.Cursor=edgeIndex+1; Progress.Attempts=attempts; Progress.TotalNodes=TotalSearchNodes;
            if (path == null)
            { Progress.FailedNodes+=linkNodes; checkpoint?.Invoke(Progress); yield return 0; continue; }
            foreach (int step in Smooth(path)) yield return step;
            result.Routes.AddRange(SplitGaps(path));
            Remember(edge.A,edge.B,path);
            roots[Root(edge.B)] = Root(edge.A);
            result.ConnectedPairs++;
            Progress.Links.Add(new PlannedLink {A=edge.A,B=edge.B,Path=path});
            checkpoint?.Invoke(Progress);
            yield return 0;
        }
        // Add a few useful loops only when the existing network requires a long
        // detour. Shared anchors make these actual junctions, not disconnected
        // approaches on opposite sides of the same POI.
        Progress.Cursor=edges.Count;
        int loops=Progress.Loops,loopAttempts=Progress.LoopAttempts;
        for(int loopIndex=Progress.LoopCursor;loopIndex<edges.Count;loopIndex++)
        {
            var edge=edges[loopIndex];
            if(loops>=settings.ExtraLoops || loopAttempts>=settings.ExtraLoops*4 || Progress.FailedNodes>=settings.MaxSearchNodes*12L) break;
            if(connectedEdges.Contains((edge.A,edge.B)) || Root(edge.A)!=Root(edge.B) || !anchors.ContainsKey(edge.A) || !anchors.ContainsKey(edge.B)) continue;
            var distances=Enumerable.Repeat(float.MaxValue,chosen.Count).ToArray();distances[edge.A]=0;
            var remaining=new HashSet<int>(Enumerable.Range(0,chosen.Count));
            while(remaining.Count>0)
            {
                int node=remaining.OrderBy(n=>distances[n]).ThenBy(n=>n).First();remaining.Remove(node);
                if(node==edge.B || distances[node]==float.MaxValue) break;
                foreach(var next in graph[node]) distances[next.Node]=Math.Min(distances[next.Node],distances[node]+next.Distance);
            }
            if(distances[edge.B]<edge.D*2) continue;
            loopAttempts++;linkNodes=0;linkLimit=settings.MaxSearchNodes*3;List<Point>? path=null;Status="Routing optional loop "+(loops+1);
            foreach(int step in FindRoute(anchors[edge.A],anchors[edge.B],p=>path=p)) yield return step;
            Progress.LoopCursor=loopIndex+1;Progress.LoopAttempts=loopAttempts;Progress.TotalNodes=TotalSearchNodes;
            if(path==null) {Progress.FailedNodes+=linkNodes;checkpoint?.Invoke(Progress);yield return 0;continue;}
            foreach(int step in Smooth(path)) yield return step;
            result.Routes.AddRange(SplitGaps(path));Remember(edge.A,edge.B,path);loops++;
            Progress.Loops=loops;Progress.Links.Add(new PlannedLink {A=edge.A,B=edge.B,Path=path});
            checkpoint?.Invoke(Progress);yield return 0;
        }
        for (int i = 0; i < chosen.Count; i++)
            result.Outcomes.Add(new PoiOutcome { Name=chosen[i].Name, Position=chosen[i].Position,
                Component=Root(i), Status=chosen.Count==1 ? "Only eligible POI on this landmass" :
                Enumerable.Range(0,chosen.Count).Any(j=>j!=i && Root(j)==Root(i)) ? "Connected" :
                "Unconnected: bounded candidate/search budget exhausted or no safe route; see component" });
        boundedSearch=false;
        Result = result;
        Status = $"Ready: {result.Routes.Count} paths, {result.PoiCount} POIs";
        heights.Clear();
    }

    private Point? Approach(Poi poi, Point toward, int choice=0)
    {
        double angle = Math.Atan2(toward.Z - poi.Position.Z, toward.X - poi.Position.X);
        float radius = Math.Max(8, poi.Radius) + settings.Width * 0.5f + 2;
        // Grid-aligned approaches remain outside the full POI exclusion and never cut through a ruin.
        var seen=new HashSet<Point>();
        for (int i = 0; i < 16; i++)
        {
            double offset = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * Math.PI / 8;
            Point p = Cell.From(poi.Position + new Point((float)Math.Cos(angle + offset), (float)Math.Sin(angle + offset)) * radius, PlannerSettings.RouteGrid).At(PlannerSettings.RouteGrid);
            if (seen.Add(p) && SafeSegment(p, p) && choice--==0) return p;
        }
        return null;
    }

    public IEnumerable<int> FindRoute(Point start, Point end, Action<List<Point>?> completed)
    {
        // A gap must be a fallback, never a cheaper shortcut around a walkable descent.
        List<Point>? path = null;
        foreach (int step in FindRouteAttempt(start,end,false,8,2.5f,p=>path=p)) yield return step;
        if(path==null)
            foreach(int step in FindRouteAttempt(start,end,false,4,6,p=>path=p)) yield return step;
        if(path==null && settings.MaximumGap>0)
            foreach(int step in FindRouteAttempt(start,end,true,4,6,p=>path=p)) yield return step;
        completed(path);
    }

    private IEnumerable<int> FindRouteAttempt(Point start, Point end, bool allowGaps, float grid, float detourFactor, Action<List<Point>?> completed)
    {
        Cell first = Cell.From(start, grid), goal = Cell.From(end, grid);
        if (!SafeSegment(start, first.At(grid)) || !SafeSegment(goal.At(grid), end)) { completed(null); yield break; }
        var frontier = new MinHeap(); frontier.Push(first, 0);
        SearchNodes = 0;
        var cost = new Dictionary<Cell, float> { [first] = 0 };
        var parent = new Dictionary<Cell, Cell>();
        var closed = new HashSet<Cell>();
        float maxLength = Math.Max(256, Point.Distance(start, end) * detourFactor);
        int searchLimit=boundedSearch ? Math.Max(1,linkLimit/3) : settings.MaxSearchNodes;
        while (frontier.Count > 0 && closed.Count < searchLimit && (!boundedSearch || linkNodes<linkLimit))
        {
            Cell current = frontier.Pop();
            if (!closed.Add(current)) continue;
            SearchNodes = closed.Count; TotalSearchNodes++; linkNodes++;
            if (current.Equals(goal))
            {
                var path = new List<Point> { end, current.At(grid) };
                while (parent.TryGetValue(current, out Cell previous)) { current = previous; path.Add(current.At(grid)); }
                path.Add(start); path.Reverse();
                for (int i = path.Count - 1; i > 0; i--) if (Point.Distance(path[i], path[i - 1]) < 0.01f) path.RemoveAt(i);
                completed(path); yield break;
            }
            Point cp = current.At(grid);
            int reach=grid<=4?2:1;
            for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                if(Math.Abs(dx)==2 && Math.Abs(dz)!=1 || Math.Abs(dz)==2 && Math.Abs(dx)!=1) continue;
                Cell next = new Cell(current.X + dx, current.Z + dz);
                if (closed.Contains(next)) continue;
                Point np = next.At(grid);
                if (Point.Distance(start, np) + Point.Distance(np, end) > maxLength) continue;
                bool gap=false;
                if(!SafeSegment(cp,np))
                {
                    if(!allowGaps || !PotentialGap(cp,np)) continue;
                    bool found=false;
                    for(int jump=1;jump<=Math.Ceiling((settings.MaximumGap+16)/grid) && settings.MaximumGap>0;jump++)
                    {
                        var across=new Cell(current.X+dx*jump,current.Z+dz*jump);
                        Point ap=across.At(grid);
                        if(!closed.Contains(across) && GapSegment(cp,ap)) {next=across;np=ap;found=true;gap=true;break;}
                    }
                    if(!found) continue;
                }
                float distance = Point.Distance(cp, np);
                float slope = Math.Abs(H(cp) - H(np)) / distance;
                float turn = 0;
                if (parent.TryGetValue(current, out Cell prev))
                {
                    Point old = cp - prev.At(grid), dir = np - cp;
                    turn = (1 - (old.X * dir.X + old.Z * dir.Z) / (old.Length * dir.Length)) * 2;
                }
                float reuse=roadCells.Contains(Cell.From(np,4))?.65f:1;
                float candidate = cost[current] + distance * reuse * (1 + 12 * slope * slope) + turn + (gap ? 80 : 0);
                if (!cost.TryGetValue(next, out float oldCost) || candidate < oldCost)
                { cost[next] = candidate; parent[next] = current; frontier.Push(next, candidate + Point.Distance(np, end)*.65f); }
            }
            yield return 0;
        }
        completed(null);
    }

    public IEnumerable<int> Smooth(List<Point> path)
    {
        // Bounded corner rounding; every shortcut is rechecked for water, slope, and POI protection.
        for (int pass = 0; pass < 2; pass++)
        {
            var rounded = new List<Point> { path[0] };
            for (int i = 1; i < path.Count - 1; i++)
            {
                Point before = Point.Lerp(path[i], path[i - 1], 0.25f);
                Point after = Point.Lerp(path[i], path[i + 1], 0.25f);
                if (SafeSegment(rounded[rounded.Count - 1], before) && SafeSegment(before, after) && SafeSegment(after, path[i + 1]))
                { rounded.Add(before); rounded.Add(after); }
                else rounded.Add(path[i]);
                yield return 0;
            }
            rounded.Add(path[path.Count - 1]); path.Clear(); path.AddRange(rounded);
        }
    }

    private sealed class MinHeap
    {
        private readonly List<(Cell Cell, float Priority, long Order)> items = new List<(Cell, float, long)>();
        private long sequence;
        public int Count => items.Count;
        private bool Less(int a, int b) => items[a].Priority < items[b].Priority || (items[a].Priority == items[b].Priority && items[a].Order < items[b].Order);
        private void Swap(int a, int b) { var t = items[a]; items[a] = items[b]; items[b] = t; }
        public void Push(Cell cell, float priority)
        {
            items.Add((cell, priority, sequence++)); int i = items.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (!Less(i, p)) break; Swap(i, p); i = p; }
        }
        public Cell Pop()
        {
            Cell result = items[0].Cell; items[0] = items[items.Count - 1]; items.RemoveAt(items.Count - 1);
            int i = 0;
            while (true) { int c = i * 2 + 1; if (c >= items.Count) break; if (c + 1 < items.Count && Less(c + 1, c)) c++; if (!Less(c, i)) break; Swap(c, i); i = c; }
            return result;
        }
    }
}




