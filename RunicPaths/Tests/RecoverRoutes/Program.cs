using RunicPaths.Core;

if(args.Length!=5) throw new ArgumentException("current.json previous.json output.json worldUid seed");
if(Path.GetFullPath(args[2])==Path.GetFullPath(args[0]) || Path.GetFullPath(args[2])==Path.GetFullPath(args[1])) throw new ArgumentException("Use a separate recovery output file");
long uid=long.Parse(args[3]);int seed=int.Parse(args[4]);
var current=Persistence.Load(args[0],uid,seed);
var old=Persistence.Load(args[1],uid,seed);
foreach(var island in old.Islands)
{
    var cells=new HashSet<Cell>(island.Cells);
    var matches=current.Islands.Where(i=>i.Cells.Any(c=>cells.Contains(c))).ToList();
    var empty=new Island{PlanningSignature=0};
    var recovered=NetworkUpdate.Preserve(empty,matches.Concat(new[]{island}));
    recovered.PoiCount=matches.Append(island).Max(i=>i.PoiCount);
    current.Islands.RemoveAll(i=>matches.Contains(i));current.Islands.Add(recovered);
}
Persistence.Save(args[2],current);
var verified=Persistence.Load(args[2],uid,seed);
var index=new RouteIndex();foreach(var island in verified.Islands) index.Add(island);
foreach(var route in old.Islands.SelectMany(i=>i.Routes))
foreach(var point in route.Points)
    if(index.Get(Cell.From(point,64),3,12).Distance(point)>0.01f) throw new Exception("Recovery lost an original road point");
Console.WriteLine($"Recovered network: {verified.Islands.Count} islands, {verified.Islands.Sum(i=>i.Routes.Count)} road sections. All previous road points retained.");
