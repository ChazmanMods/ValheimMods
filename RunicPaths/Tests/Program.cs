using RunicPaths.Core;
using System.Globalization;
using System.Text;

int passed = 0;
void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
void Test(string name, Action run) { run(); Console.WriteLine("PASS " + name); passed++; }
var settings = new PlannerSettings { MaxIslandCells = 10000, MaxSearchNodes = 15000, MaxPois = 20 };
List<Point>? RouteFor(Planner p, Point a, Point b)
{ List<Point>? path = null; foreach (int _ in p.FindRoute(a, b, result => path = result)) { } return path; }
Island Build(Planner p, Point start, List<Poi> pois)
{ foreach (int _ in p.Build(start, pois)) { } return p.Result ?? throw new Exception(p.Status); }
Poi POI(string name, float x, float z) => new Poi { Name = name, Position = new Point(x, z), Radius = 8 };

Test("Disconnected islands remain unactivated until a separate landing", () =>
{
    var t = new Terrain(p => (Math.Abs(p.X + 180) < 115 || Math.Abs(p.X - 180) < 115) && Math.Abs(p.Z) < 110 ? 45 : 0);
    var pois = new List<Poi> { POI("StartTemple", -230, -40), POI("Ruin", -140, 40), POI("Ruin", 150, 40), POI("Ruin", 210, -30) };
    Island first = Build(new Planner(t, settings), new Point(-180, 0), pois);
    Assert(first.PoiCount == 2 && first.Routes.Count == 1, "First island route missing");
    Assert(first.Cells.All(c => c.X < 0), "Survey crossed ocean");
    var index = new RouteIndex(); index.Add(first);
    Assert(!index.ActivatedCells.Contains(Cell.From(new Point(180, 0), 24)), "Second island activated too early");
    Island second = Build(new Planner(t, settings), new Point(180, 0), pois); index.Add(second);
    Assert(second.PoiCount == 2 && second.Routes.Count == 1, "Second island route missing");
    Assert(index.ActivatedCells.Contains(Cell.From(new Point(180, 0), 24)), "Second landing not retained");
});

Test("Routes go around a lake and every segment remains dry", () =>
{
    var t = new Terrain(p => Math.Abs(p.X) < 240 && Math.Abs(p.Z) < 160 && !(Math.Abs(p.X) < 40 && Math.Abs(p.Z) < 70) ? 45 : 0);
    var planner = new Planner(t, settings);
    var path = RouteFor(planner, new Point(-160, 0), new Point(160, 0));
    Assert(path != null && path.Any(p => Math.Abs(p.Z) > 70), "Route failed to detour");
    for (int i = 1; i < path!.Count; i++) Assert(planner.SafeSegment(path[i - 1], path[i]), "Unsafe segment");
});

Test("A river across an island is never bridged", () =>
{
    var t = new Terrain(p => Math.Abs(p.X) < 240 && Math.Abs(p.Z) < 100 && Math.Abs(p.X) > 3 ? 45 : 0);
    var planner = new Planner(t, settings);
    Assert(RouteFor(planner, new Point(-120, 0), new Point(120, 0)) == null, "Route crossed narrow river");
    var island = Build(planner, new Point(-120, 0), new List<Poi> { POI("Ruin", -120, 0), POI("Ruin", 120, 0) });
    Assert(island.PoiCount == 1 && island.Routes.Count == 0, "River incorrectly connected survey");
});

Test("Path shoulders cannot overhang a narrow strip of dry ground", () =>
{
    var planner = new Planner(new Terrain(p => Math.Abs(p.Z) < 1 ? 45 : 0), settings);
    Assert(!planner.SafeSegment(new Point(-8, 0), new Point(8, 0)), "Only centerline was validated");
});

Test("A cliff blocks a direct route but a gentle bypass remains available", () =>
{
    var planner = new Planner(new Terrain(p => Math.Abs(p.X) < 160 && Math.Abs(p.Z) < 160 ? (Math.Abs(p.X) < 16 && Math.Abs(p.Z) < 55 ? 100 : 45) : 0), settings);
    Assert(!planner.SafeSegment(new Point(-24, 0), new Point(24, 0)), "Cliff accepted");
    var path = RouteFor(planner, new Point(-100, 0), new Point(100, 0));
    Assert(path != null && path.Any(p => Math.Abs(p.Z) > 55), "No cliff bypass");
});

Test("POI footprints are protected, including rounded corners", () =>
{
    var t = new Terrain(p => Math.Abs(p.X) < 240 && Math.Abs(p.Z) < 160 ? 45 : 0);
    var pois = new List<Poi> { POI("StartTemple", -150, 0), POI("Ruin", 150, 0) };
    var exclusions = pois.Select(p => new Exclusion { Center = p.Position, Radius = p.Radius }).ToList();
    exclusions.Add(new Exclusion { Center = new Point(0, 0), Radius = 35 });
    var planner = new Planner(t, settings, exclusions);
    Island island = Build(planner, new Point(-100, 0), pois);
    Assert(island.Routes.Count == 1, "No route around protected ruin");
    var path = island.Routes[0].Points;
    for (int i = 1; i < path.Count; i++) Assert(planner.SafeSegment(path[i - 1], path[i]), "Smoothing cut protected terrain");
    WriteSvg(t, island, exclusions);
});

Test("Identical world inputs yield identical paths", () =>
{
    var t = new Terrain(p => p.Length < 220 ? 45 + (float)Math.Sin(p.X / 30) : 0);
    var pois = new List<Poi> { POI("StartTemple", -100, 0), POI("Ruin", 100, 0), POI("Ruin", 0, 120) };
    var a = Build(new Planner(t, settings), new Point(), pois);
    var b = Build(new Planner(t, settings), new Point(), pois.AsEnumerable().Reverse().ToList());
    Assert(a.Routes.Count == b.Routes.Count, "Route counts differ");
    for (int i = 0; i < a.Routes.Count; i++) Assert(a.Routes[i].Points.SequenceEqual(b.Routes[i].Points), "Nondeterministic route");
});

Test("Oversized surveys fail without committing partial islands", () =>
{
    var planner = new Planner(new Terrain(_ => 45), new PlannerSettings { MaxIslandCells = 30 });
    foreach (int _ in planner.Build(new Point(), new List<Poi>())) { }
    Assert(planner.LimitReached && planner.Result == null, "Partial island committed");
});

Test("Network save/reload, backup, world mismatch and corrupt save handling", () =>
{
    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RunicPaths-tests-" + Guid.NewGuid());
    string path = System.IO.Path.Combine(dir, "world.json");
    try
    {
        var state = Persistence.Load(path, 123, 456);
        state.Islands.Add(new Island { Cells = new List<Cell> { new Cell(1, 2) }, Routes = new List<Route> { new Route { Points = new List<Point> { new Point(0, 0), new Point(8, 0) } } } });
        Persistence.Save(path, state); Persistence.Save(path, state);
        var restored = Persistence.Load(path, 123, 456);
        Assert(restored.Islands.Count == 1 && restored.Islands[0].Routes[0].Points.Count == 2, "Round trip failed");
        Assert(File.Exists(path + ".bak"), "Backup absent");
        bool rejected = false; try { Persistence.Load(path, 999, 456); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Wrong-world file accepted");
        File.WriteAllText(path, "broken"); rejected = false;
        try { Persistence.Load(path, 123, 456); } catch { rejected = true; }
        Assert(rejected, "Corruption silently reset world");
    }
    finally { Directory.Delete(dir, true); }
});

Test("Neighbouring zones agree on route strength across positive and negative seams", () =>
{
    var island = new Island { Routes = new List<Route> { new Route { Points = new List<Point> { new Point(-40, 0), new Point(40, 0) } } } };
    var index = new RouteIndex(); index.Add(island);
    foreach (int x in new[] { -32, 32 })
    {
        ZonePaths a = index.Get(new Cell(x < 0 ? -1 : 0, 0), 3, 12), b = index.Get(new Cell(x < 0 ? 0 : 1, 0), 3, 12);
        Assert(a.Strength(new Point(x, 0)) > 0.9f, "Route not indexed at seam");
        Assert(a.Strength(new Point(x, 1)) == b.Strength(new Point(x, 1)), "Seam mismatch");
    }
});

Test("Source contains no map reveal or pin creation calls", () =>
{
    string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../"));
    foreach (string source in Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
    {
        string code = File.ReadAllText(source);
        Assert(!code.Contains("Minimap.") && !code.Contains("ExploreAll(") && !code.Contains("AddPin("), "Unexpected map API in " + source);
    }
});
Test("River gaps stop on both banks without painting water", () =>
{
    var terrain=new Terrain(p=>Math.Abs(p.X)<240 && Math.Abs(p.Z)<120 ? (Math.Abs(p.X)<6 ? 20 : 45) : 0);
    var config=new PlannerSettings {MaximumGap=24,MaxIslandCells=10000,MaxSearchNodes=12000};
    var planner=new Planner(terrain,config);
    var path=RouteFor(planner,new Point(-100,0),new Point(100,0));
    Assert(path!=null,"No logical bank-to-bank connection");
    var pieces=planner.SplitGaps(path!);
    Assert(pieces.Count>=2,"Gap was not split into roads");
    foreach(var piece in pieces) for(int i=1;i<piece.Points.Count;i++) Assert(planner.SafeSegment(piece.Points[i-1],piece.Points[i]),"Road spans river");
    Assert(pieces.Any(r=>r.Points.All(p=>p.X<0)) && pieces.Any(r=>r.Points.All(p=>p.X>0)),"Missing bank road");
    var island=Build(planner,new Point(-100,0),new List<Poi>{POI("Ruin",-120,0),POI("Ruin",120,0)});
    Assert(island.PoiCount==2 && island.Routes.Count>=2,"River survey failed to include both banks");
});
Test("Ravine gaps do not create a disconnected road on the ravine floor", () =>
{
    var terrain=new Terrain(p=>Math.Abs(p.X)<5?32:50);
    var planner=new Planner(terrain,new PlannerSettings{MaximumGap=24});
    Assert(planner.GapSegment(new Point(-16,0),new Point(16,0)),"Ravine not detected");
    var pieces=planner.SplitGaps(new List<Point>{new Point(-40,0),new Point(-16,0),new Point(16,0),new Point(40,0)});
    Assert(pieces.Count==2 && pieces.All(r=>r.Points.All(p=>Math.Abs(p.X)>=5)),"Road appeared on ravine floor");
});
Test("Grading caps are absolute and signatures change with live road settings", () =>
{
    var terrain=new Terrain(p=>45+p.Z*2);
    var zone=new ZonePaths{Width=8,MaximumRaise=1,MaximumLower=0.5f};zone.Segments.Add((new Point(-20,0),new Point(20,0)));
    Assert(Math.Abs(Grading.Delta(terrain,zone,new Point(0,2),49)+0.5f)<0.001f,"Dig cap failed");
    Assert(Math.Abs(Grading.Delta(terrain,zone,new Point(0,-2),41)-1)<0.001f,"Raise cap failed");
    zone.ComputeSignature();int before=zone.Signature;zone.MaximumLower=0;zone.ComputeSignature();
    Assert(zone.Signature!=before && Grading.Delta(terrain,zone,new Point(0,2),49)==0,"Live lower setting failed");
});Test("POI approaches end near the protected footprint without entering it", () =>
{
    var land=new Terrain(p=>Math.Abs(p.X)<240 && Math.Abs(p.Z)<120?45:0);
    var protect=new List<Exclusion>{new Exclusion{Center=new Point(0,0),Radius=8},new Exclusion{Center=new Point(120,0),Radius=8}};
    var planner=new Planner(land,new PlannerSettings{Width=3,MaxIslandCells=10000},protect);
    var island=Build(planner,new Point(40,0),new List<Poi>{POI("StartTemple",0,0),POI("Ruin",120,0)});
    Assert(island.Routes.Count==1,"Expected one connection");
    var route=island.Routes[0];
    Assert(Point.Distance(route.Points[0],new Point(0,0))<=16 && Point.Distance(route.Points[^1],new Point(120,0))<=16,"Unnecessary POI setback remains");
    for(int i=1;i<route.Points.Count;i++) Assert(planner.SafeSegment(route.Points[i-1],route.Points[i]),"Approach enters a protected footprint");
});Test("Continuous land detour wins over a shorter ravine gap", () =>
{
    var terrain=new Terrain(p=>Math.Abs(p.X)<5 && Math.Abs(p.Z)<48?32:50);
    var planner=new Planner(terrain,new PlannerSettings{MaximumGap=24,MaxSearchNodes=16000});
    var path=RouteFor(planner,new Point(-40,0),new Point(40,0));
    Assert(path!=null,"Missing detour");
    Assert(planner.SplitGaps(path!).Count==1,"Planner preferred a broken shortcut");
    Assert(path!.Any(p=>Math.Abs(p.Z)>=48),"Route did not wind around the ravine");
});
Test("Network replacement retains established routes and is idempotent", () =>
{
    var old=new Island{Cells=new(){new Cell(0,0),new Cell(1,0)},Routes=new(){new Route{Points=new(){new Point(0,0),new Point(24,0)}}}};
    var replacement=new Island{PlanningSignature=25,Cells=new(){new Cell(0,0)},Routes=new(){new Route{Points=new(){new Point(0,0),new Point(0,24)}}}};
    var merged=NetworkUpdate.Preserve(replacement,new[]{old});
    var index=new RouteIndex();index.Add(merged);
    Assert(index.Get(new Cell(0,0),3,12).Strength(new Point(12,0))>0.9f,"Established road vanished after replacement");
    Assert(merged.Cells.Count==2 && merged.Routes.Count==2,"Partial replacement lost prior network data");
    Assert(NetworkUpdate.Preserve(replacement,new[]{merged}).Routes.Count==2,"Repeated planning duplicates roads");
    Assert(old.Routes.Count==1 && old.Cells.Count==2,"Merge mutated saved rollback state");
});
Test("Fine routing finds a narrow offset land corridor missed by the coarse grid", () =>
{
    var terrain=new Terrain(p=>Math.Abs(p.X)>24 || (p.Z>=1 && p.Z<=7)?45:0);
    var planner=new Planner(terrain,new PlannerSettings{Width=2,MaximumGap=0,MaxSearchNodes=16000});
    var path=RouteFor(planner,new Point(-40,0),new Point(40,0));
    Assert(path!=null,"Fine grid failed to find offset corridor");
    Assert(path!.Any(p=>Math.Abs(p.X)<24 && p.Z==4),"Missing fine-grid corridor points");
    for(int i=1;i<path!.Count;i++) Assert(planner.SafeSegment(path[i-1],path[i]),"Fine path cuts unsafe shoulders");
});
Test("Extended search finds a long land detour around an impassable cliff", () =>
{
    var terrain=new Terrain(p=>Math.Abs(p.X)<5 && Math.Abs(p.Z)<140?80:45);
    var planner=new Planner(terrain,new PlannerSettings{MaximumGap=0,MaxSearchNodes=16000});
    var path=RouteFor(planner,new Point(-40,0),new Point(40,0));
    Assert(path!=null && path.Any(p=>Math.Abs(p.Z)>=140),"Long switchback search stopped prematurely");
    for(int i=1;i<path!.Count;i++) Assert(planner.SafeSegment(path[i-1],path[i]),"Cliff detour violates slope constraints");
});
Test("Shallow diagonal switchbacks traverse a hillside too steep for 45-degree turns", () =>
{
    var terrain=new Terrain(p=>100+p.X*0.9f);
    var planner=new Planner(terrain,new PlannerSettings{Width=3,MaxSlope=0.55f,MaximumGap=0,MaximumRaise=4,MaximumLower=4});
    var path=RouteFor(planner,new Point(-24,0),new Point(24,0));
    Assert(path!=null && path.Any(p=>Math.Abs(p.Z)>=8),"Missing shallow-angle hillside switchback");
    for(int i=1;i<path!.Count;i++) Assert(planner.SafeSegment(path[i-1],path[i]),"Switchback exceeds slope/earthwork constraints");
});
Test("Grading cannot split a shared tile edge while its neighbor is unfinished", () =>
{
    var terrain=new Terrain(p=>100+p.Z*0.8f);
    var zone=new ZonePaths{Width=8,MaximumRaise=4,MaximumLower=4};
    zone.Segments.Add((new Point(-100,0),new Point(100,0)));
    foreach(float edge in new[]{-96f,-32f,32f,96f})
    {
        Assert(Grading.Delta(terrain,zone,new Point(edge,2),terrain.Height(new Point(edge,2)))==0,"Updated edge differs from untouched neighboring tile");
        Assert(Grading.SeamWeight(new Point(edge-0.01f,2))<0.001f && Grading.SeamWeight(new Point(edge+0.01f,2))<0.001f,"Abrupt seam transition");
    }
    Assert(Math.Abs(Grading.Delta(terrain,zone,new Point(0,2),terrain.Height(new Point(0,2))))>1,"Interior grading disabled");
});
Test("Wide roads use two-meter POI clearance without adding the width twice", () =>
{
    var terrain=new Terrain(p=>50);
    var planner=new Planner(terrain,new PlannerSettings{Width=8},new List<Exclusion>{new Exclusion{Center=new Point(0,0),Radius=8}});
    Assert(planner.SafeSegment(new Point(-20,16),new Point(20,16)),"Unnecessary clearance blocks safe approach");
    Assert(!planner.SafeSegment(new Point(-20,11),new Point(20,11)),"Road shoulder enters protected footprint");
});
Test("Grading samples only the final nearest segment, regardless of route density", () =>
{
    var terrain=new CountingTerrain();var paths=new ZonePaths{Width=8,MaximumRaise=4,MaximumLower=4};
    for(int z=200;z>=0;z--) paths.Segments.Add((new Point(-20,z),new Point(20,z)));
    float delta=Grading.Delta(terrain,paths,new Point(0,2),52);
    Assert(terrain.Calls==3,"Repeated expensive terrain samples while searching segments");
    Assert(Math.Abs(delta+2)<0.001f,"Optimization changed nearest-segment grading");
    terrain.Calls=0;Grading.Delta(terrain,paths,new Point(1000,1000),50);
    Assert(terrain.Calls==0,"Off-road cell samples terrain unnecessarily");
});
Test("Prepared surfaces share elevated seam vertices without flattening to tile edges", () =>
{
    var network=new WorldPaths{WorldId=42,Seed=7,Islands=new List<Island>{new Island{Routes=new List<Route>{new Route{Points=new List<Point>{new Point(-100,5),new Point(100,5)}}}}}};
    var terrain=new Terrain(p=>50+.2f*p.Z);
    var s=new PlannerSettings{Width=8,MaximumRaise=2,MaximumLower=2};
    var surface=new PreparedSurface{Signature=91};foreach(int _ in surface.Build(network,terrain,s)) {}
    var left=surface.Tiles[new Cell(0,0)];var right=surface.Tiles[new Cell(1,0)];
    var a=left.Vertices.First(v=>v.Index==35*65+64);var b=right.Vertices.First(v=>v.Index==35*65);
    Assert(a.Height==b.Height && a.Grade==b.Grade,"Prepared seams disagree");
    Assert(a.Height>terrain.Height(new Point(32,3))+.1f,"Seam still flattened back to baseline");
    foreach(var tile in surface.Tiles.Values) foreach(var v in tile.Vertices)
    {
        var p=new Point(tile.Cell.X*64-32+v.Index%65,tile.Cell.Z*64-32+v.Index/65);
        Assert(Math.Abs(v.Height-terrain.Height(p))<=2.0001f,"Earthwork cap exceeded");
    }
    for(int x=-99;x<100;x++) Assert(surface.Cleared(new Point(x,5)),"Interior grass gap at "+x);
    var bytes=surface.Encode();var loaded=PreparedSurface.Decode(bytes,42,7);
    Assert(bytes.SequenceEqual(loaded.Encode()),"Prepared cache changed on round trip");
    bytes[bytes.Length/2]^=1;bool corrupt=false;try{PreparedSurface.Decode(bytes,42,7);}catch(InvalidDataException){corrupt=true;}
    Assert(corrupt,"Corrupt cache accepted");
    bool wrong=false;try{PreparedSurface.Decode(surface.Encode(),43,7);}catch(InvalidDataException){wrong=true;}
    Assert(wrong,"Foreign world cache accepted");
});
Test("Prepared island planning accounts for every POI without the old selection cap", () =>
{
    var t=new Terrain(p=>Math.Abs(p.X)<220 && Math.Abs(p.Z)<110?50:0);
    var pois=Enumerable.Range(0,8).Select(i=>POI("Ruin",-160+i*45,0)).ToList();
    var island=Build(new Planner(t,new PlannerSettings{MaxPois=0,MaxIslandCells=2000}),new Point(0,0),pois);
    Assert(island.PoiCount==8 && island.Outcomes.Count==8,"POIs silently omitted");
    Assert(island.Outcomes.All(o=>o.Status=="Connected"),"Coverage failed on unobstructed island");
});
Test("An unreachable POI is explicitly reported", () =>
{
    var t=new Terrain(p=>Math.Abs(p.X)<220 && Math.Abs(p.Z)<110?50:0);
    var island=Build(new Planner(t,new PlannerSettings{MaxPois=0,MaxConnection=10}),new Point(0,0),new List<Poi>{POI("A",-100,0),POI("B",100,0)});
    Assert(island.Outcomes.Count==2 && island.Outcomes.All(o=>o.Status.StartsWith("Unconnected")),"Missing POIs reported as connected");
});
Test("Prepared networks share POI junctions and can add a useful loop", () =>
{
    var t=new Terrain(p=>Math.Abs(p.X)<200 && Math.Abs(p.Z)<200?50:0);
    var pois=new List<Poi>{POI("A",-80,-80),POI("B",80,-80),POI("C",80,80),POI("D",-80,80)};
    var tree=Build(new Planner(t,new PlannerSettings{MaxPois=0}),new Point(0,0),pois);
    var loop=Build(new Planner(t,new PlannerSettings{MaxPois=0,ExtraLoops=1}),new Point(0,0),pois);
    Assert(loop.Routes.Count>tree.Routes.Count,"Useful loop was not created");
    var ends=tree.Routes.SelectMany(r=>new[]{r.Points.First(),r.Points.Last()}).ToList();
    Assert(ends.Distinct().Count()==4,"Routes stop at different disconnected approaches to the same POI");
});
Test("Rectangular structure protection survives preparation and cache serialization", () =>
{
    var network=new WorldPaths{WorldId=1,Seed=1,Islands=new List<Island>{new Island{Routes=new List<Route>{new Route{Points=new List<Point>{new Point(-80,0),new Point(80,0)}}}}}};
    var t=new Terrain(p=>50+.2f*p.Z);
    var settings=new PlannerSettings{Width=8};
    var rectangle=new Exclusion{Center=new Point(0,0),HalfX=3,HalfZ=3,Radius=5};
    var surface=new PreparedSurface();foreach(int _ in surface.Build(network,t,settings,new[]{rectangle})){}
    Assert(surface.Tiles[new Cell(0,0)].Protected(32*65+32),"Structure footprint not protected");
    Assert(!surface.Cleared(new Point(0,0)) && surface.Cleared(new Point(5,0)),"Protection extends beyond the footprint");
    var copy=PreparedSurface.Decode(surface.Encode(),1,1);
    Assert(copy.Tiles[new Cell(0,0)].Protected(32*65+32),"Protection lost in cache");
    var original=new PreparedSurface();foreach(int _ in original.Build(network,t,settings)){}
    var rebuilt=new PreparedSurface();settings.Width=2;
    foreach(int _ in rebuilt.Build(network,t,settings,new[]{rectangle},original)){}
    var a=original.Tiles[new Cell(0,0)].Vertices.First(v=>v.Index==34*65+32);
    var b=rebuilt.Tiles[new Cell(0,0)].Vertices.First(v=>v.Index==a.Index);
    Assert(a.Height==b.Height && a.Paint==b.Paint,"Rebuild changed support terrain underneath a protected structure");
});
Test("Interrupted island resumes successful and failed attempts with identical output", () =>
{
    var t=new Terrain(p=>Math.Abs(p.X)<300 && Math.Abs(p.Z)<200 ? 50:0);
    var ps=new PlannerSettings {MaxPois=0,MaxSearchNodes=2000,ExtraLoops=2};
    var pois=Enumerable.Range(0,12).Select(i=>POI("Ruin",-220+i*40,(i%2)*80-40)).ToList();
    string file=Path.Combine(Path.GetTempPath(),"runic-progress-"+Guid.NewGuid()+".json");
    PlanningProgress? snapshot=null;
    var interrupted=new Planner(t,ps);
    using(var run=interrupted.Build(pois[0].Position,pois,null,p=>
    {p.World=5;p.Seed=7;p.Signature=9;p.Save(file);snapshot=PlanningProgress.Load(file,5,7,9);}).GetEnumerator())
        while(run.MoveNext() && (snapshot==null || snapshot.Attempts<3)) { }
    Assert(snapshot!=null && snapshot.Links.Count>=3,"No intermediate successful checkpoint");
    long savedNodes=snapshot!.TotalNodes;
    var resumed=new Planner(t,ps);
    foreach(var _ in resumed.Build(pois[0].Position,pois,snapshot)) { }
    var uninterrupted=new Planner(t,ps);Build(uninterrupted,pois[0].Position,pois);
    string Geometry(Island i)=>string.Join(";",i.Routes.SelectMany(r=>r.Points).Select(p=>$"{p.X:R},{p.Z:R}"));
    Assert(Geometry(resumed.Result!)==Geometry(uninterrupted.Result!),"Resume changed road geometry");
    Assert(resumed.TotalSearchNodes==uninterrupted.TotalSearchNodes && savedNodes>0,"Resume repeated completed search work");
    bool rejected=false;try {PlanningProgress.Load(file,6,7,9);}catch(InvalidDataException) {rejected=true;}
    Assert(rejected,"Checkpoint from another world accepted");
    File.Delete(file);if(File.Exists(file+".bak")) File.Delete(file+".bak");
});

Test("364 unreachable POIs have bounded retry work and resumable failures", () =>
{
    var timer=System.Diagnostics.Stopwatch.StartNew();
    var t=new Terrain(p=>Math.Abs(p.X)<900 && Math.Abs(p.Z)<900 ? 50:0);
    // Narrow enclosed cells allow island surveying but prevent road-width travel.
    var obstacles=new List<Exclusion>();
    for(int x=-800;x<=800;x+=40) obstacles.Add(new Exclusion {Center=new Point(x,0),HalfX=2,HalfZ=900,Radius=901});
    var ps=new PlannerSettings {MaxPois=0,MaxSearchNodes=128,MaxIslandCells=10000};
    var pois=Enumerable.Range(0,364).Select(i=>POI("Ruin",-780+(i%19)*80,-720+(i/19)*72)).ToList();
    var full=new Planner(t,ps,obstacles);Build(full,pois[0].Position,pois);
    Assert(full.Result!.PoiCount==364 && full.Result.Outcomes.Count==364,"POIs silently dropped");
    Assert(full.Progress!.Attempts<=364*8,"Quadratic candidate retries");
    Assert(full.TotalSearchNodes<=364L*8*ps.MaxSearchNodes*3,"Per-link budget multiplied across approach retries");
    PlanningProgress? snapshot=null;var first=new Planner(t,ps,obstacles);
    using(var run=first.Build(pois[0].Position,pois,null,p=>
    {if(p.FailedNodes>0 && snapshot==null) {using var ms=new MemoryStream();var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PlanningProgress));serializer.WriteObject(ms,p);ms.Position=0;snapshot=(PlanningProgress)serializer.ReadObject(ms)!;}}).GetEnumerator())
        while(run.MoveNext() && snapshot==null) { }
    Assert(snapshot!=null && snapshot.FailedNodes>0,"No failed-attempt checkpoint");
    var resumed=new Planner(t,ps,obstacles);foreach(var _ in resumed.Build(pois[0].Position,pois,snapshot)) { }
    Assert(resumed.TotalSearchNodes==full.TotalSearchNodes && resumed.Progress!.Attempts==full.Progress.Attempts,"Failed searches repeated after resume");
    Console.WriteLine($"  364-POI stress: {full.Progress.Attempts} attempts; {full.TotalSearchNodes} nodes; {timer.Elapsed.TotalSeconds:F2}s including resume");
});

Console.WriteLine($"{passed} behavioral tests passed.");

void WriteSvg(Terrain terrain, Island island, List<Exclusion> exclusions)
{
    string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../artifacts"));
    Directory.CreateDirectory(dir);
    var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='-260 -190 520 390'><rect x='-260' y='-190' width='520' height='390' fill='#172b39'/><rect x='-240' y='-160' width='480' height='320' rx='6' fill='#344b35'/>");
    foreach (Exclusion e in exclusions) svg.Append(FormattableString.Invariant($"<circle cx='{e.Center.X}' cy='{e.Center.Z}' r='{e.Radius}' fill='#677061' stroke='#a9b09c' stroke-dasharray='3 3'/>"));
    foreach (Route route in island.Routes)
    {
        string points = string.Join(" ", route.Points.Select(p => p.X.ToString("F2", CultureInfo.InvariantCulture) + "," + p.Z.ToString("F2", CultureInfo.InvariantCulture)));
        svg.Append($"<polyline points='{points}' fill='none' stroke='#ad9265' stroke-width='4' stroke-linejoin='round' stroke-linecap='round'/>");
    }
    svg.Append("<text x='-240' y='180' font-family='sans-serif' font-size='12' fill='#e3e8df'>Synthetic planner test — protected POIs, curved dirt route (not in-game footage)</text></svg>");
    File.WriteAllText(System.IO.Path.Combine(dir, "planner-preview.svg"), svg.ToString());
}

sealed class Terrain(Func<Point, float> height) : ITerrain
{
    public float Height(Point p) => height(p);
    public bool Allowed(Point p) => true;
}




sealed class CountingTerrain : ITerrain { public int Calls; public float Height(Point p) {Calls++;return 50;} public bool Allowed(Point p)=>true; }

