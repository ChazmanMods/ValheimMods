using System;
using System.Globalization;
using ArcaneWaterWorks;

int checks=0;

void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
Route Trace(Func<float,float,float?> height,float sea=-50,float length=48)=>Route.Trace(height,sea,0,length,4,.8f,out _);
var flat=Trace((x,z)=>0);
Check(flat!=null&&flat.Valid,"flat spring route");
Check(Math.Abs(flat.Length-48)<.01,"requested length");
var downhill=Trace((x,z)=>-z*.5f);
Check(downhill.Points[^1].Y< -20,"downhill water follows terrain");
var ocean=Trace((x,z)=>-z*.5f,-5);
Check(ocean.Mouth&&Math.Abs(ocean.Points[^1].Y+5)<.001,"sea outlet stops at sea level");
Check(Trace((x,z)=>null)==null,"unloaded terrain produces no route");
var hill=Trace((x,z)=>10);
Check(hill.Points.Count==2,"uphill is bounded to a short spring pool");
Check(!Route.TryDecode("1|NaN|0.8|0|0,0.12,0;0,0,2",out _),"reject NaN");
Check(!Route.TryDecode("1|4|0.8|0|0,0.12,0;0,Infinity,2",out _),"reject Infinity");
Check(!Route.TryDecode("4|4|0.8|0|0,0.12,0;0,0,2",out _),"reject future schema");
Check(!Route.TryDecode("1|4|0.8|0|0,0.12,0;0,0.2,2",out _),"reject uphill serialized segment");
Check(!Route.TryDecode("1|4|0.8|0|0,0.12,0;0,0,0",out _),"reject zero-length segment");
Check(!Route.TryDecode("1|4|0.8|0|0,0.12,0;0,0,100",out _),"reject unbounded segment");
Check(!Route.TryDecode(new string('x',20000),out _),"reject oversized payload");
Check(!Route.TryDecode("1|4|0.8|0|5,0.12,0;5,0,2",out _),"require source anchored route");
foreach(var culture in new[]{"en-US","de-DE","fr-FR"}){
    CultureInfo.CurrentCulture=new CultureInfo(culture);Check(Route.TryDecode(ocean.Encode(),out var decoded)&&decoded.Encode()==ocean.Encode(),"culture-independent persistence "+culture);
}
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var manual=Trace((x,z)=>-z*.1f,-50,4);
string before=manual.Encode();
Check(!manual.Append(new Node(0,0,8),(x,z)=>100,-50,out _)&&manual.Encode()==before,"uphill append is atomic");
Check(!manual.Append(new Node(0,0,100),(x,z)=>0,-50,out _)&&manual.Encode()==before,"long append rejected atomically");
Check(manual.Append(new Node(0,0,12),(x,z)=>-z*.1f,-50,out _)&&manual.Valid,"guided downhill append");
Check(!ocean.Append(new Node(0,-5,30),(x,z)=>-10,-5,out _),"mouth cannot be extended without undo");
Check(Math.Abs(flat.Bed(0,10,0)+1.15f)<.05f,"riverbed excavates expected depth");
Check(flat.Bed(20,10,0)==0,"outside footprint is unchanged");
Check(flat.Bed(0,10,-10)==-10,"overlay never raises existing deeper terrain");
Check(flat.Sample(0,10,out _,out var level,out _)&&Math.Abs(level+.35f)<.01,"water level follows centerline");
Check(!flat.Sample(8,10,out _,out _,out _),"water bounded to width");
var rng=new Random(1704);
for(int i=0;i<500;i++){
    float grade=(float)rng.NextDouble()*2,sea=-5-(float)rng.NextDouble()*100,heading=(float)rng.NextDouble()*6.28f;
    var route=Route.Trace((x,z)=>-(float)Math.Sqrt(x*x+z*z)*grade,sea,heading,180,2+(float)rng.NextDouble()*10,.4f+(float)rng.NextDouble()*2.6f,out _);
    Check(route!=null&&route.Valid,"random valid route");
    Check(Route.TryDecode(route.Encode(),out var saved)&&saved.Encode()==route.Encode(),"random route roundtrip");
    for(int j=1;j<route.Points.Count;j++)Check(route.Points[j].Y<=route.Points[j-1].Y+.001f,"monotonic flow");
    for(int j=0;j<10;j++){
        float x=(float)rng.NextDouble()*200-100,z=(float)rng.NextDouble()*200-100,original=(float)rng.NextDouble()*40-20;
        route.Sample(x,z,out float distance,out _,out _);
        if(distance<=route.Width*.325f)Check(route.Bed(x,z,original)<=original+.001,"deep channel interior is preserved");
        else if(distance>=route.Width*.5f+route.BankPadding)Check(Math.Abs(route.Bed(x,z,original)-original)<.001,"outside the bank remains unchanged");
        else Check(float.IsFinite(route.Bed(x,z,original)),"finite reversible bank profile");
    }
}
Console.WriteLine($"PASS: {checks} route, persistence, boundary, and terrain assertions.");

var branch=Trace((x,z)=>0,-50,12);
int mainTip=branch.Points.Count-1;
Check(branch.AppendFrom(2,new Node(8,0,4),(x,z)=>0,-50,out _),"branch construction");
Check(branch.Parent(mainTip+1)==2,"branch connects to selected junction, not main endpoint");
Check(branch.Sample(4,4,out _,out _,out _),"branch contains water");
Check(!branch.Sample(7,9,out _,out _,out _),"space between branches stays dry");
Check(Route.TryDecode(branch.Encode(),out var branchSaved)&&branchSaved.Parent(mainTip+1)==2,"branch persists");
Check(Route.TryDecode("1|4|0.8|0|0,0.12,0;0,0.1,2",out _),"existing source schema supported");
Check(!Route.TryDecode("2|4|0.8|0|0,-0.35,0,-1;0,-0.35,2,5",out _),"reject invalid branch parent");
var deep=flat.Copy();deep.Depth=8;Check(deep.Valid,"eight metre riverbed");
deep.Sample(0,10,out _,out var deepLevel,out _);flat.Sample(0,10,out _,out var shallowLevel,out _);
Check(deepLevel==shallowLevel,"depth never lowers the water surface");
Check(deep.Bed(0,10,0)< -8,"deep trench is carved");
Check(flat.Bed(2,10,0)>shallowLevel,"riverbank rises above the water edge");
var longRiver=Trace((x,z)=>0,-50,180);longRiver.Sample(0,80,out _,out _,out _);
Check(longRiver.LastSampleCandidates<=12,"local queries don't scan all 90 segments");
longRiver.Sample(100,100,out _,out _,out _);Check(longRiver.LastSampleCandidates==0,"off-channel queries do no segment work");
// Compare the spatial lookup against a brute-force independent nearest segment oracle.
for(int k=0;k<2000;k++){
    float x=(float)rng.NextDouble()*24-8,z=(float)rng.NextDouble()*24-5,best=float.MaxValue,y=0;
    for(int i=1;i<branch.Points.Count;i++){
        var a=branch.Points[branch.Parent(i)];var b=branch.Points[i];float dx=b.X-a.X,dz=b.Z-a.Z;
        float t=Math.Clamp(((x-a.X)*dx+(z-a.Z)*dz)/(dx*dx+dz*dz),0,1);
        float d=MathF.Sqrt(MathF.Pow(x-a.X-dx*t,2)+MathF.Pow(z-a.Z-dz*t,2));
        if(d<best){best=d;y=a.Y+(b.Y-a.Y)*t;}
    }
    bool wet=branch.Sample(x,z,out float indexed,out float indexedLevel,out _);
    Check(wet==(best<=branch.Width*.5f),"indexed footprint matches independent oracle");
    if(best<=branch.Width*.5f+2)Check(Math.Abs(indexed-best)<.0001f&&Math.Abs(indexedLevel-y)<.0001f,"near-bank indexed height matches oracle");
}
var watch=System.Diagnostics.Stopwatch.StartNew();long candidates=0;
for(int i=0;i<200000;i++){longRiver.Sample(0,80,out _,out _,out _);candidates+=longRiver.LastSampleCandidates;}
watch.Stop();Console.WriteLine($"PASS: {checks} total assertions; 200,000 indexed samples in {watch.ElapsedMilliseconds} ms, {candidates/200000} candidates/query versus 90 previously.");

var estuary=Trace((x,z)=>-z*.5f,-5,48);
Check(estuary.Points[^1].Z>=18,"outlet continues below receiving water instead of ending at shore");
var end=estuary.Points[^1];Check(estuary.Bed(end.X+2,end.Z,-6)<=-6+.001,"outlet bank cannot build a dam in natural water");
Check(estuary.OutletBlend(end.X,end.Z)>.99,"outlet surface blends below receiving water");
var pondRoute=Route.Trace((x,z)=>-z*.5f,-50,0,48,4,.8f,out _,(x,z)=>z>=10?-5:-50);
Check(pondRoute.Mouth&&Math.Abs(pondRoute.Points[^1].Y+5)<.01,"elevated WaterGardens pond receives river above sea level");
var multiOutlet=estuary.Copy();int outletIndex=multiOutlet.Points.Count-1;
Check(multiOutlet.AppendFrom(1,new Node(8,-1,2),(x,z)=>-1,-5,out _),"branch after existing outlet");
Check(multiOutlet.IsOutlet(outletIndex)&&Route.TryDecode(multiOutlet.Encode(),out var multiReload)&&multiReload.IsOutlet(outletIndex),"earlier outlet survives branch save and reload");
Console.WriteLine($"PASS: {checks} total assertions including submerged outlets and pond connections.");

var finish=flat.Copy();finish.Volume=.2f;finish.Sound=2;finish.Whitewater=.7f;finish.Mud=false;
Check(finish.SmoothBank(2,10),"bank brush accepted near shore");
Check(!finish.SmoothBank(2,10),"duplicate strokes do not accumulate terrain work");
Check(!finish.SmoothBank(90,90),"remote brush rejected");
Check(Route.TryDecode(finish.Encode(),out var finished)&&finished.Encode()==finish.Encode(),"audio, mud, foam and bank strokes persist together");
Check(finished.Copy().Encode()==finished.Encode(),"copy retains finish settings and strokes");
Check(finished.Bed(0,10,0)==flat.Bed(0,10,0),"bank smoothing preserves channel floor");
Check(finished.Bed(2,10,0)<flat.Bed(2,10,0)&&finished.Bed(2,10,0)>-.35f,"brush lowers berm while retaining a dry bank");
Check(finished.Bed(2,35,0)==flat.Bed(2,35,0),"brush is local, not a whole-section reshape");
Check(finished.Bed(30,10,3)==3,"terrain outside smoothing footprint unchanged");
finished.Sample(0,10,out _,out var smoothLevel,out _);Check(smoothLevel==-.35f,"smoothing never changes water level");
finished.BankStrokes.Clear();Check(finished.Bed(2,10,0)==flat.Bed(2,10,0),"undo restores original bank profile");
foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f,1.1f}){var bad=flat.Copy();bad.Volume=invalid;Check(!Route.TryDecode(bad.Encode(),out _),"reject corrupt volume");bad=flat.Copy();bad.Whitewater=invalid;Check(!Route.TryDecode(bad.Encode(),out _),"reject corrupt foam setting");}
var badSound=flat.Copy();badSound.Sound=3;Check(!Route.TryDecode(badSound.Encode(),out _),"reject unknown sound");
var legacy="3|4|0.8|0|0,-0.35,0,-1,0;0,-0.35,2,-1,0";
Check(Route.TryDecode(legacy,out var migrated)&&migrated.Volume==.5f&&migrated.Sound== -1&&migrated.Mud,"old sections migrate to quieter width-based sound");
Console.WriteLine($"PASS: {checks} total assertions including sound, mud, whitewater and reversible local bank smoothing.");

var automatic=flat.Copy();automatic.Sound=-1;
foreach(var entry in new[]{(2f,0),(3.99f,0),(4f,1),(7.99f,1),(8f,2),(12f,2)}){automatic.Width=entry.Item1;Check(automatic.ResolvedSound==entry.Item2,"automatic width threshold "+entry.Item1);Check(Route.TryDecode(automatic.Encode(),out var decodedSound)&&decodedSound.Sound==-1&&decodedSound.ResolvedSound==entry.Item2,"automatic selection survives reload");}
automatic.Width=12;automatic.Sound=0;Check(automatic.ResolvedSound==0,"manual brook overrides wide-river default");
Console.WriteLine($"PASS: {checks} total assertions including automatic sound thresholds and overrides.");

// Independent direction oracle: texture features must travel parent -> child in every compass direction.
for(int degrees=0;degrees<360;degrees+=15){
 float angle=degrees*MathF.PI/180,dx=MathF.Sin(angle),dz=MathF.Cos(angle);
 var flow=new Route();flow.Points.Add(new Node(0,-.35f,0));flow.Points.Add(new Node(dx*6,-2,dz*6));
 foreach(float t in new[]{-.2f,0f,.25f,.75f,1f,1.2f}){
  float x=dx*6*t+dz,z=dz*6*t-dx;
  int segment=flow.FlowSegment(x,z);Check(segment==1,"directional frame finds segment");
  flow.FlowCoordinates(segment,x,z,out var u0,out var v0);
  flow.FlowCoordinates(segment,x+dx,z+dz,out var u1,out var v1);
  Check(Math.Abs(u1-u0)<.0001f&&Math.Abs((v1-v0)-1)<.0001f,"one metre downstream advances V only: "+degrees);
  // UV(v) - time has constant phase when the feature moves one metre downstream per second.
  Check(Math.Abs(v1*.2f-.2f-v0*.2f)<.0001f,"animated feature moves downstream, including end caps");
 }
}
var forkFlow=new Route();forkFlow.Points.Add(new Node(0,-.35f,0));forkFlow.Points.Add(new Node(0,-1,4));forkFlow.Points.Add(new Node(0,-2,8));forkFlow.Points.Add(new Node(-4,-2,4,1));
forkFlow.FlowCoordinates(3,0,4,out _,out var branchStart);Check(Math.Abs(branchStart-4)<.0001,"branch starts at parent distance, not total unrelated path length");
forkFlow.FlowCoordinates(3,-4,4,out _,out var branchEnd);Check(Math.Abs(branchEnd-8)<.0001,"westbound branch UV follows downstream");
foreach(var culture in new[]{"en-US","fr-FR"}){
 CultureInfo.CurrentCulture=new CultureInfo(culture);
 var hot=new HotSpringSettings{Sound=5,Music=6,Warded=true,Volume=.43f};
 Check(HotSpringSettings.TryDecode(hot.Encode(),out var copy)&&copy.Encode()==hot.Encode(),"hot spring settings survive locale "+culture);
}
foreach(var bad in new[]{"1|6|0|.4|.5|.5|.5|0","1|1|7|.4|.5|.5|.5|0","1|1|1|NaN|.5|.5|.5|0","1|1|1|.4|.5|.5|.5|2"})Check(!HotSpringSettings.TryDecode(bad,out _),"reject invalid hot spring settings");
for(int shape=0;shape<4;shape++){
 int previous=0;for(float width=4;width<=24;width+=.5f){int count=HotSpringSettings.SeatCount(width,shape);Check(count>=previous&&count>=4&&count<=24,"seat growth bounded and monotonic");previous=count;}
}
for(float depth=.8f;depth<=3;depth+=.1f){
 double fraction=HotSpringSettings.SeatContour(depth),t=(fraction-.45)/.55;
 double bed=depth-(depth-.025)*t*t*(3-2*t);
 Check(Math.Abs(bed-(Math.Min(.45,depth*.4)+.25))<.0001,"sitting stones rest on bed contour across pool depths");
}
Console.WriteLine($"PASS: {checks} total assertions including river geometry, hot spring persistence and supported seats.");
Check(HotSpringBankProfile.Apply(-2,0,-1.2)==-2,"hot spring rim leaves deep basin unchanged");
Check(HotSpringBankProfile.Apply(2,0,2.5)==2,"hot spring rim leaves surrounding ground unchanged");
Check(HotSpringBankProfile.Apply(-.025,0,0)>.18,"hot spring shoreline rises above water");
Check(HotSpringBankProfile.Apply(.5,0,0)==.5,"hot spring rim never lowers higher existing ground");
double lowest=100;
foreach(int shape in new[]{0,1,2,3})foreach(double width in new[]{4d,6,12,24})foreach(double depth in new[]{.8,1.4,3})foreach(double offset in new[]{0d,.25,.5,.75}){
 double At(double x,double z){double d=ArcaneWaterWorks.Pools.PondMath.Distance(shape,x-offset,z-offset,width);return HotSpringBankProfile.Apply(ArcaneWaterWorks.Pools.PondMath.Bed(.15,0,depth,d,width),0,(d-1)*width*.5);}
 for(int i=0;i<128;i++){
  double angle=i*Math.PI/64,r=ArcaneWaterWorks.Pools.PondMath.Radius(shape,angle)*width*.5;
  double x=Math.Cos(angle)*r+offset,z=Math.Sin(angle)*r*ArcaneWaterWorks.Pools.PondMath.Aspect(shape)+offset;
  double Distance(double xx,double zz)=>ArcaneWaterWorks.Pools.PondMath.Distance(shape,xx-offset,zz-offset,width);
  double nx=Distance(x+.025,z)-Distance(x-.025,z),nz=Distance(x,z+.025)-Distance(x,z-.025),length=Math.Sqrt(nx*nx+nz*nz);
  x+=nx/length*1.5;z+=nz/length*1.5;
  if(Distance(x,z)<1)continue;
  double x0=Math.Floor(x),z0=Math.Floor(z),u=x-x0,v=z-z0;
  double a=At(x0,z0),b=At(x0+1,z0),c=At(x0,z0+1),d=At(x0+1,z0+1);
  double height=u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v);
  lowest=Math.Min(lowest,height);
  Check(height>0,$"water edge coverage shape={shape} width={width} depth={depth} offset={offset} angle={angle} height={height}");
 }
}
Console.WriteLine($"PASS: {checks} assertions; minimum terrain cover above hidden water edge: {lowest:F4} m");

for(int heading=0;heading<24;heading++){
 float angle=heading*(float)Math.PI/12,dx=(float)Math.Sin(angle),dz=(float)Math.Cos(angle);
 var r=new Route();r.Points.Add(new Node(0,0,0));r.Points.Add(new Node(dx*6,-.4f,dz*6));
 r.Flow(dx*3,dz*3,out var vx,out var vz);
 Check(vx*dx+vz*dz>.4,"surface flow follows downstream in every compass direction");
 Check(Math.Abs(vx*dz-vz*dx)<.0001,"no sideways flow on straight river");
}
var bend=new Route();bend.Points.Add(new Node(0,0,0));bend.Points.Add(new Node(0,-.2f,6));bend.Points.Add(new Node(6,-.4f,6));
bend.Flow(-.001f,6,out var leftX,out var leftZ);bend.Flow(.001f,6,out var rightX,out var rightZ);
Check(Math.Abs(leftX-rightX)+Math.Abs(leftZ-rightZ)<.005,"continuous flow at bend across closest-segment boundary");
Check(leftX>0&&leftZ>0,"flow blends through bend without reversing");
foreach(float width in new[]{2f,4,8,12})foreach(float depth in new[]{.4f,1,3,8}){
 var r=new Route{Width=width,Depth=depth};r.Points.Add(new Node(0,0,0));r.Points.Add(new Node(0,0,6));
 Check(r.Bed(width*.5f+1.5f,3,0)>.1,"outside water skirt is concealed by raised bank");
 Check(r.Bed(width*.5f+4,3,-2)==-2,"bank restores original height at outer boundary");
 Check(Math.Abs(r.Bed(width*.5f+3.9999f,3,-2)+2)<.001,"bank tapers continuously into low terrain");
 r.Mouth=true;Check(r.Bed(width*.5f+1,6,-2)==-2,"outlet bank does not dam receiving water");
}
Console.WriteLine($"PASS: {checks} assertions including downstream flow and concealed river banks.");
foreach(float width in new[]{2f,4,8,12})foreach(float depth in new[]{.4f,3,8})foreach(float original in new[]{-2f,0,.35f})foreach(float offset in new[]{0f,.25f,.5f,.75f}){
 var r=new Route{Width=width,Depth=depth};r.Points.Add(new Node(0,0,0));r.Points.Add(new Node(0,0,6));
 for(int j=0;j<24;j++){
  double angle=j*Math.PI/12,x=Math.Cos(angle)*(width*.5+1.5)+offset,z=Math.Sin(angle)*(width*.5+1.5)+offset;
  // The source cap exercises all angles against Valheim's triangular 1 m terrain grid.
  if(z>offset)continue;
  float At(double xx,double zz)=>r.Bed((float)xx-offset,(float)zz-offset,original);
  double x0=Math.Floor(x),z0=Math.Floor(z),u=x-x0,v=z-z0;
  double a=At(x0,z0),b=At(x0+1,z0),c=At(x0,z0+1),d=At(x0+1,z0+1);
  double h=u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v);
  Check(original<-.35f?h<=original+.3501f:h>0,"low banks have bounded buildup; normal banks cover the water edge");
 }
}
Console.WriteLine($"PASS: {checks} assertions including river bank terrain-grid coverage.");

var cliff=new Route{Width=4};
cliff.Points.Add(new Node(0,0,0));cliff.Points.Add(new Node(0,-8,4));cliff.Points.Add(new Node(4,-16,4));
cliff.Surface(0,5,out _,out float capHeight,out _);
Check(capHeight < -8,"steep junction does not leave a flat disk at the node height");
cliff.Surface(1.999f,2,out _,out float seamA,out _);
cliff.Surface(2.001f,2,out _,out float seamB,out _);
Check(Math.Abs(seamA-seamB)<.03f,"steep bend height is continuous across nearest-segment boundary");
var straight=new Route{Width=4};straight.Points.Add(new Node(0,0,0));straight.Points.Add(new Node(0,-8,4));
straight.Surface(0,5,out _,out float endHeight,out _);
Check(Math.Abs(endHeight+10)<.001f,"sloping terminal cap continues downhill instead of making a flat shelf");
straight.Mouth=true;straight.Surface(0,5,out _,out float outletHeight,out _);
Check(Math.Abs(outletHeight+8)<.001f,"receiving-water outlet retains its level");
Console.WriteLine($"PASS: {checks} assertions including steep river seams and caps.");

var upper=new Route{Width=4};upper.Points.Add(new Node(0,0,0));upper.Points.Add(new Node(0,-8,4));
var lower=new Route{Width=4};lower.Points.Add(new Node(0,0,0));lower.Points.Add(new Node(4,-8,0));
upper.Surface(1,3,out _,out float upperShelf,out _);lower.Surface(1,-1,out _,out float lowerShelf,out _);
Check(Math.Abs(upperShelf-(lowerShelf-8))>3,"reproduce separate steep sections disagreeing at overlapping caps");
var joined=RiverJoin.Combine(new[]{(upper,new Node(0,0,0)),(lower,new Node(0,-8,4))});
Check(joined!=null&&joined.Points.Count==3,"continuation merges at shared node without a phantom connecting segment");
for(float x=-1;x<=3;x+=.125f)for(float z=1;z<=5;z+=.125f){
    joined.Surface(x,z,out _,out float actual,out _);cliff.Surface(x,z,out _,out float expected,out _);
    Check(Math.Abs(actual-expected)<.001f,"split sections reproduce unsplit river surface");
}
Console.WriteLine($"PASS: {checks} assertions including split steep section reproduction.");
Check(PondRiverJoin.Height(30,32,-1)==32,"river overlap adopts unchanged receiving pond height");
Check(PondRiverJoin.Height(30,32,1)==32,"river apron matches pond at clipping boundary");
Check(PondRiverJoin.Height(30,32,4)==30,"river outside transition retains original height");
for(int i=0;i<=100;i++){
    float edge=i/20f,h=PondRiverJoin.Height(30,32,edge);
    Check(h>=30&&h<=32,"approach remains bounded");
    if(i>0)Check(h<=PondRiverJoin.Height(30,32,edge-.05f)+.00001f,"river approaches unchanged pond continuously");
}
Console.WriteLine($"PASS: {checks} assertions including receiving pond approach.");

foreach(float drop in new[]{2f,12,24,60})foreach(float width in new[]{2f,4,12}){
 var r=new Route{Width=width};r.Points.Add(new Node(0,0,0));r.Points.Add(new Node(0,-drop,2));r.Points.Add(new Node(4,-drop,4));
 for(float x=-8;x<=12;x+=.5f)for(float z=-8;z<=12;z+=.5f){
  r.Surface(x,z,out _,out var height,out _);Check(height<=.0001f,"steep cap never extrapolates above river source");
  foreach(float original in new[]{0f,-5,-30})Check(r.Bed(x,z,original)<=original+.3501f,"trench cannot construct a tall bank on low ground");
 }
 r.SmoothBank(0,2);Check(r.Bed(width*.5f,1,-30)<=-29.6499f,"bank smoothing respects maximum buildup");
}
Console.WriteLine($"PASS: {checks} assertions including steep-trench wall prevention.");

foreach(float width in new[]{2f,4,12}){
 var mouth=new Route{Width=width,Mouth=true};mouth.Points.Add(new Node(0,0,0));
 for(int i=1;i<=8;i++)mouth.Points.Add(new Node(0,0,i*2));
 Check(Route.TryDecode(mouth.Encode(),out var saved),"existing outlet persists without migration");
 foreach(float original in new[]{-.05f,0,.1f,-2f})for(float z=0;z<=18;z+=.5f)for(float x=-width*.5f-2;x<=width*.5f+2;x+=.5f){
  float result=saved.Bed(x,z,original);Check(result<=original+.00001f,"natural-water tail never raises underwater terrain");
  if(Math.Abs(x)<=width*.5f+1&&z<=16)Check(result<=-.2999f,"full outlet tail has no visible terrain rim");
 }
 Check(Math.Abs(saved.Bed(width*.5f+4,8,-.05f)+.05f)<.0001f,"mouth cleanup ends at river footprint");
}
Console.WriteLine($"PASS: {checks} assertions including open natural-water mouths.");

// Branch endpoints survive save/reload and never close a sibling.
var ends=branch.Copy();
Check(ends.SetEnd(mainTip,true),"close main endpoint independently");
Check(!ends.SetEnd(2,true),"cannot close a junction");
Check(Route.TryDecode(ends.Encode(),out var endReload)&&endReload.ClosedEnds.Contains(mainTip),"closed endpoint persists");
Check(!endReload.AppendFrom(mainTip,new Node(0,0,16),(x,z)=>0,-50,out _),"closed endpoint rejects append");
Check(endReload.Append(new Node(12,0,4),(x,z)=>0,-50,out _),"sibling remains extendable");
Check(endReload.SetEnd(mainTip,false)&&endReload.AppendFrom(mainTip,new Node(0,0,16),(x,z)=>0,-50,out _),"reopened main endpoint extends");
Check(estuary.EditableTip==0,"all-outlet section selects source for new branch");
Check(multiOutlet.EditableTip==multiOutlet.Points.Count-1,"select open sibling rather than natural outlet");
var largeStroke=Trace((x,z)=>0,-50,4);
Check(largeStroke.Append(new Node(0,0,68),(x,z)=>0,-50,out _)&&largeStroke.Valid,"64 metre trench stroke supported");
Check(largeStroke.Points.Count==35,"large stroke retains two metre terrain sampling");
Console.WriteLine($"PASS: {checks} total assertions including larger trench sizing.");

var bankTest=new Route{Width=4};bankTest.Points.Add(new Node(0,0,0));bankTest.Points.Add(new Node(0,-1,6));bankTest.Points.Add(new Node(0,-2,12));
string originalBankRoute=bankTest.Encode();
var fittedBanks=bankTest.FitBanks((x,z)=>-z/6-2-Math.Abs(x)*.2f,-50);
Check(bankTest.Encode()==originalBankRoute,"bank fitting does not rewrite saved routes");
Check(fittedBanks.Points[0].Y < -2.7f,"lower bank constrains source surface instead of draping water");
for(int i=1;i<fittedBanks.Points.Count;i++)Check(fittedBanks.Points[i].Y<=fittedBanks.Points[fittedBanks.Parent(i)].Y,"fitted current never runs uphill");
var fittedOutlet=estuary.FitBanks((x,z)=>-100,-5);
Check(Math.Abs(fittedOutlet.Points[^1].Y+5)<.0001f,"bank fit preserves receiving-water level");
Check(fittedOutlet.Points.TrueForAll(p=>p.Y>=-5),"outlet floor propagates upstream to prevent uphill connection");
Console.WriteLine($"PASS: {checks} total assertions including bank-contained surface profiles.");

for(int i=0;i<1000;i++){
 float x=(float)rng.NextDouble()*1000,z=(float)rng.NextDouble()*1000,t=(float)rng.NextDouble()*60;
 float value=RiverWaveMath.Offset(x,z,1,.3f,.12f,t);
 Check(Math.Abs(value)<=.12001f,"wave displacement remains bounded");
 Check(Math.Abs(RiverWaveMath.Offset(x,z,1,.3f,.12f,6-.00001f)-RiverWaveMath.Offset(x,z,1,.3f,.12f,6+.00001f))<.0001f,"advection phase wraps continuously");
 Check(RiverWaveMath.Offset(x,z,1,.3f,0,t)==0,"shoreline zero amplitude remains fixed");
}
Console.WriteLine($"PASS: {checks} assertions including continuous surface-wave math.");

var stairs=new Route{Width=8,Mouth=true};
float[] stairHeights={0,0,0,-2,-2,-2,-4,-4,-4,-6};
for(int i=0;i<stairHeights.Length;i++)stairs.Points.Add(new Node(0,stairHeights[i],i*2));
string stairSave=stairs.Encode();var smoothStairs=stairs.FitBanks((x,z)=>20,-6);
Check(stairs.Encode()==stairSave,"smoothing leaves persisted route unchanged");
Check(smoothStairs.Points[1].Y<-.2f&&smoothStairs.Points[2].Y<-.5f,"terrace drop spreads upstream instead of retaining a flat shelf");
Check(Math.Abs(smoothStairs.Points[^1].Y+6)<.0001f,"smoothed stair profile retains receiving water height");
float previousHeight=float.PositiveInfinity;
for(float z=0;z<=18;z+=.05f){
 smoothStairs.Surface(0,z,out _,out float h,out _);stairs.Surface(0,z,out _,out float ceiling,out _);
 Check(h<=previousHeight+.002f,"smoothed stair surface flows downhill");
 Check(h<=ceiling+.001f&&h>=-6.001f,"smoothed stair surface stays below original ceiling and above outlet z="+z+" h="+h+" ceiling="+ceiling);previousHeight=h;
}
Console.WriteLine($"PASS: {checks} assertions including terrace smoothing.");
