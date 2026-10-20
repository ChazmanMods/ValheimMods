using RunicSettlements.Core;
int passed=0;
void Check(bool b,string why){if(!b)throw new Exception(why);}
void Test(string name,Action action){action();Console.WriteLine("PASS "+name);passed++;}
SettlementPlan Plan()=>new SettlementPlan {World=123,Seed=42,Spawn=new Point(0,0),Options=new SettlementOptions {SearchRadius=850}};
var terrain=new Terrain(p=>Math.Abs(p.X)<950 && Math.Abs(p.Z)<950?50:0);
var protection=new List<Exclusion>{new Exclusion {Center=new Point(0,0),Radius=30}};
SettlementPlan Build(ITerrain t,SettlementPlan plan,List<Exclusion>? blocked=null)
{var planner=new SettlementPlanner(t,p=>true,blocked??protection);foreach(var _ in planner.Build(plan)){}return plan;}
var planned=Plan();
Test("Five 120m sites are separated, connected, and outside protected spawn",()=>
{
 Build(terrain,planned);Check(planned.Sites.Count==5,"Five sites missing on broad flat land");
 Check(planned.Roads.Count>=5,"Network missing roads");
 for(int i=0;i<planned.Roads.Count;i++)for(int j=0;j<i;j++)
 {var a=planned.Roads[i].Points;var b=planned.Roads[j].Points;Check(!((a[0].Equals(b[0]) && a.Last().Equals(b.Last())) || (a[0].Equals(b.Last()) && a.Last().Equals(b[0]))),"Optional loop duplicated an existing road");}
 foreach(var s in planned.Sites){Check(Point.Distance(s.Center,planned.Spawn)>174,"Spawn footprint overwritten");foreach(var other in planned.Sites.Where(p=>p!=s)) Check(Point.Distance(s.Center,other.Center)>=300,"Sites overlap");}
 var reached=new HashSet<Point>{planned.Anchor};bool change=true;
 while(change){change=false;foreach(var r in planned.Roads) if(reached.Contains(r.Points[0]) && reached.Add(r.Points.Last()))change=true;}
 Check(planned.Sites.All(s=>reached.Contains(s.Center)),"A site has no continuous road to spawn");
});
Test("Steep land is rejected rather than exceeding earthwork caps",()=>
{
 var t=new Terrain(p=>Math.Abs(p.X)<950 && Math.Abs(p.Z)<950 ? 300+p.X*.3f:0);
 var plan=Build(t,Plan());Check(plan.Sites.Count==0,"Unsafe steep platforms accepted");
});
Test("Separate islands cannot receive a settlement",()=>
{
 var t=new Terrain(p=>Math.Abs(p.X)<900 && Math.Abs(p.Z)<900 && !(p.X>100 && p.X<180)?50:0);
 var plan=Build(t,Plan());Check(plan.Sites.All(s=>s.Center.X<100),"Water crossing activated another island");
});
Test("Structure footprint and player buffer exclude the full clearing",()=>
{
 var block=new Exclusion {Center=new Point(-300,0),HalfX=80,HalfZ=130,Radius=160};
 var plan=Build(terrain,Plan(),protection.Append(block).ToList());
 Check(plan.Sites.All(s=>!block.Contains(s.Center,78)),"A site overlaps a protected structure");
});
Test("Prepared surfaces flatten complete sites, preserve seams, and distinguish pavement",()=>
{
 var t=new Terrain(p=>50+(float)Math.Sin(p.X*.02)*1.2f);
 var plan=new SettlementPlan {World=123,Seed=42,Options=new SettlementOptions(),Sites=new List<SettlementSite>{new SettlementSite {Center=new Point(32,0),Height=50}},Roads=new List<Route>{new Route{Points=new List<Point>{new Point(-100,0),new Point(100,0)}}}};
 var surface=new PreparedSurface();var net=new WorldPaths{WorldId=123,Seed=42,Islands=new List<Island>{new Island{Routes=plan.Roads}}};
 foreach(var _ in surface.Build(net,new SettlementTerrain(t,plan),new PlannerSettings {Width=5,MaximumRaise=4,MaximumLower=4})){}
 foreach(var _ in SettlementSurface.AddSites(surface,plan,t)){}
 foreach(var tile in surface.Tiles.Values)foreach(var v in tile.Vertices)
 {var p=new Point(tile.Cell.X*64-32+v.Index%65,tile.Cell.Z*64-32+v.Index/65);if(Point.Distance(p,plan.Sites[0].Center)<=60){Check(Math.Abs(v.Height-50)<.001f,"Interior not flat");Check(v.Paint==255,"Grassy road shoulder inside clearing");}Check(Math.Abs(v.Height-t.Height(p))<=4.01,"Earthwork cap exceeded");}
 var west=surface.Tiles[new Cell(0,0)];var east=surface.Tiles[new Cell(1,0)];
 foreach(var v in west.Vertices.Where(v=>v.Index%65==64)){var other=east.Vertices.FirstOrDefault(e=>e.Index==v.Index-64);Check(Math.Abs(v.Height-other.Height)<.001f,"Tile seam tear");}
 Check(surface.Tiles.Values.SelectMany(t=>t.Vertices).Any(v=>v.Material==0 && v.Paint==255),"No clearing surface");
 Check(surface.Tiles.Values.SelectMany(t=>t.Vertices).Any(v=>v.Material==1 && v.Paint==255),"No pavement");
 var bytes=surface.Encode();var loaded=PreparedSurface.Decode(bytes,123,42);Check(bytes.SequenceEqual(loaded.Encode()),"Cache round trip changed surface/material");
 bytes[30]^=1;bool rejected=false;try{PreparedSurface.Decode(bytes,123,42);}catch(InvalidDataException){rejected=true;}Check(rejected,"Corrupt surface accepted");
});
Test("World plan persistence locks layout and rejects another world",()=>
{
 string file=Path.Combine(Path.GetTempPath(),"settlement-test-"+Guid.NewGuid()+".json");planned.Save(file);
 var load=SettlementPlan.Load(file,123,42)!;Check(load.Complete && load.Sites.Count==5 && load.Options.Radius==60,"Plan changed on reload");
 bool rejected=false;try{SettlementPlan.Load(file,124,42);}catch(InvalidDataException){rejected=true;}Check(rejected,"Wrong world accepted");File.Delete(file);
});
Test("Interrupted preparation retains sites and resumes the same layout",()=>
{
 string file=Path.Combine(Path.GetTempPath(),"settlement-resume-"+Guid.NewGuid()+".json");
 int savedCount=0;var partial=Plan();var builder=new SettlementPlanner(terrain,p=>true,protection);
 using(var work=builder.Build(partial,p=>{p.Save(file);savedCount=p.Sites.Count;}).GetEnumerator())
     while(work.MoveNext() && savedCount<2){}
 var resumed=SettlementPlan.Load(file,123,42)!;Check(resumed.Sites.Count==2 && !resumed.Complete,"Partial sites were not saved");
 Build(terrain,resumed);
 Check(resumed.Sites.Select(s=>s.Center).SequenceEqual(planned.Sites.Select(s=>s.Center)),"Restart moved accepted sites");
 Check(resumed.Roads.Count==planned.Roads.Count,"Restart duplicated roads");
 File.Delete(file);if(File.Exists(file+".bak"))File.Delete(file+".bak");
});
Test("No minimap exploration or pin creation calls",()=>
{
 var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
 foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.TopDirectoryOnly))
 {string source=File.ReadAllText(file);Check(!source.Contains("Minimap.instance") && !source.Contains("ExploreAll(") && !source.Contains("AddPin("),"Fog/pin mutation in "+file);}
});
Console.WriteLine(passed+" settlement behavior tests passed.");
sealed class Terrain(Func<Point,float> height):ITerrain {public float Height(Point p)=>height(p);public bool Allowed(Point p)=>true;}
