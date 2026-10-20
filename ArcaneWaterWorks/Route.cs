using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArcaneWaterWorks;

// Engine-independent persisted model. Coordinates are offsets from the spring anchor.
internal readonly struct Node
{
    internal readonly float X, Y, Z;
    internal readonly int Parent;
    internal readonly bool Outlet;
    internal Node(float x, float y, float z, int parent = -1,bool outlet=false) { X = x; Y = y; Z = z; Parent = parent; Outlet=outlet; }
    internal static float Distance(Node a, Node b) => (float)Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    internal static Node Lerp(Node a, Node b, float t) => new Node(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
}

internal sealed class Route
{
    internal const float MaxLength = 192;
    internal const int MaxNodes = 128;
    internal float Width = 4, Depth = .8f;
    internal float Volume = .5f, Whitewater = 1;
    internal int Sound = -1;
    internal int ResolvedSound => Sound>=0?Sound:Width<4?0:Width<8?1:2;
    internal bool Mud = true;
    internal readonly List<Node> BankStrokes = new List<Node>();
    internal float BankPadding => BankStrokes.Count==0?4:6;
    internal float BankSoftness(float x,float z){float weight=0;foreach(var p in BankStrokes){float t=Math.Max(0,1-Node.Distance(p,new Node(x,0,z))/8);weight=Math.Max(weight,t*t*(3-2*t));}return weight;}
    internal bool SmoothBank(float x,float z){
        float distance=float.PositiveInfinity;
        // Brush placement is an edit, so a bounded one-off segment scan is sufficient.
        for(int i=1;i<Points.Count;i++){var a=Points[Parent(i)];var b=Points[i];float dx=b.X-a.X,dz=b.Z-a.Z;float t=Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/(dx*dx+dz*dz)));distance=Math.Min(distance,Node.Distance(Node.Lerp(a,b,t),new Node(x,0,z)));}
        if(distance>Width*.5f+6||BankStrokes.Count>=64)return false;
        if(BankStrokes.Any(p=>Node.Distance(p,new Node(x,0,z))<1))return false;
        BankStrokes.Add(new Node(x,0,z));Invalidate();return true;
    }
    internal bool Mouth;
    internal readonly HashSet<int> ClosedEnds = new HashSet<int>();
    internal bool IsTip(int index)=>index>=0&&index<Points.Count&&!Enumerable.Range(1,Points.Count-1).Any(i=>Parent(i)==index);
    internal int EditableTip=>Enumerable.Range(1,Math.Max(0,Points.Count-1)).Reverse().FirstOrDefault(i=>IsTip(i)&&!IsOutlet(i)&&!ClosedEnds.Contains(i));
    internal bool SetEnd(int index,bool closed){if(!IsTip(index))return false;if(closed)ClosedEnds.Add(index);else ClosedEnds.Remove(index);return true;}
    internal bool IsOutlet(int i)=>Points[i].Outlet||(Mouth&&i==Points.Count-1);
    internal float OutletBlend(float x,float z){float best=0;for(int i=1;i<Points.Count;i++)if(IsOutlet(i)){float d=Node.Distance(Points[i],new Node(x,0,z));best=Math.Max(best,Math.Max(0,1-d/8));}return best*best*(3-2*best);}
    internal readonly List<Node> Points = new List<Node>();
    float[] profileCeiling,profileSlope;
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    internal int Parent(int i) => Points[i].Parent < 0 ? i-1 : Points[i].Parent;
    internal float Length => Enumerable.Range(1, Math.Max(0, Points.Count-1)).Sum(i => Node.Distance(Points[Parent(i)], Points[i]));
    static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    internal bool Valid
    {
        get
        {
            if(ClosedEnds.Any(i=>!IsTip(i)))return false;
            if(!Finite(Volume)||Volume<0||Volume>1||Sound< -1||Sound>2||!Finite(Whitewater)||Whitewater<0||Whitewater>1||BankStrokes.Count>64)return false;
            foreach(var p in BankStrokes)if(!Finite(p.X)||!Finite(p.Z)||Math.Abs(p.X)>MaxLength+12||Math.Abs(p.Z)>MaxLength+12)return false;
            if (!Finite(Width)||!Finite(Depth)||Width<2||Width>12||Depth<.4f||Depth>8||Points.Count<2||Points.Count>MaxNodes) return false;
            if (Math.Abs(Points[0].X)>.01f||Math.Abs(Points[0].Z)>.01f||(Points[0].Y < -.5f || Points[0].Y > .13f)) return false;
            foreach (var p in Points) if (!Finite(p.X)||!Finite(p.Y)||!Finite(p.Z)||Math.Abs(p.X)>MaxLength||Math.Abs(p.Z)>MaxLength||p.Y < -256||p.Y>1) return false;
            for (int i=1;i<Points.Count;i++) if (Parent(i)<0||Parent(i)>=i||Node.Distance(Points[Parent(i)],Points[i])<.05f||Node.Distance(Points[Parent(i)],Points[i])>8.01f||Points[i].Y>Points[Parent(i)].Y+.005f) return false;
            return Length<=MaxLength+.01f;
        }
    }
    internal string Encode() => "5|"+Width.ToString("R",Culture)+"|"+Depth.ToString("R",Culture)+"|"+(Mouth?"1":"0")+"|"+
        string.Join(";",Points.Select((p,i)=>p.X.ToString("R",Culture)+","+p.Y.ToString("R",Culture)+","+p.Z.ToString("R",Culture)+","+p.Parent.ToString(Culture)+","+(IsOutlet(i)?"1":"0")))+
        "|"+Volume.ToString("R",Culture)+"|"+Sound+"|"+Whitewater.ToString("R",Culture)+"|"+(Mud?"1":"0")+"|"+string.Join(";",BankStrokes.Select(p=>p.X.ToString("R",Culture)+","+p.Z.ToString("R",Culture)))+"|"+string.Join(",",ClosedEnds.OrderBy(i=>i));
    internal static bool TryDecode(string raw,out Route route)
    {
        route=null;
        if (string.IsNullOrEmpty(raw)||raw.Length>16000) return false;
        try {
            var fields=raw.Split('|'); if(fields.Length<5||(fields[0]!="1"&&fields[0]!="2"&&fields[0]!="3"&&fields[0]!="4"&&fields[0]!="5")||fields.Length!=(fields[0]=="5"?11:fields[0]=="4"?10:5)||(fields[3]!="0"&&fields[3]!="1"))return false;
            var r=new Route{Width=float.Parse(fields[1],Culture),Depth=float.Parse(fields[2],Culture),Mouth=fields[3]=="1"};
            var entries=fields[4].Split(';');if(entries.Length>MaxNodes)return false;
            foreach(var entry in entries){var xyz=entry.Split(',');if(xyz.Length!=(fields[0]=="1"?3:fields[0]=="2"?4:5))return false;if(xyz.Length==5&&xyz[4]!="0"&&xyz[4]!="1")return false;r.Points.Add(new Node(float.Parse(xyz[0],Culture),float.Parse(xyz[1],Culture),float.Parse(xyz[2],Culture),fields[0]=="1"?-1:int.Parse(xyz[3],Culture),xyz.Length==5&&xyz[4]=="1"));}
            if(fields[0]=="4"||fields[0]=="5"){
                r.Volume=float.Parse(fields[5],Culture);r.Sound=int.Parse(fields[6],Culture);r.Whitewater=float.Parse(fields[7],Culture);
                if(fields[8]!="0"&&fields[8]!="1")return false;r.Mud=fields[8]=="1";
                if(fields[9]!=""){var strokes=fields[9].Split(';');if(strokes.Length>64)return false;foreach(var s in strokes){var xz=s.Split(',');if(xz.Length!=2)return false;r.BankStrokes.Add(new Node(float.Parse(xz[0],Culture),0,float.Parse(xz[1],Culture)));}}
            }
            if(fields[0]=="5"&&fields[10]!="")foreach(var end in fields[10].Split(','))r.ClosedEnds.Add(int.Parse(end,Culture));
            if(!r.Valid)return false;route=r;return true;
        }catch(FormatException){return false;}catch(OverflowException){return false;}
    }
    void PreserveOutlet(){if(Mouth&&Points.Count>1){var p=Points[Points.Count-1];Points[Points.Count-1]=new Node(p.X,p.Y,p.Z,p.Parent,true);}}
    internal void CopyAppearance(Route from){Volume=from.Volume;Sound=from.Sound;Whitewater=from.Whitewater;Mud=from.Mud;}
    internal Route Copy(){var r=new Route{Width=Width,Depth=Depth,Mouth=Mouth};r.CopyAppearance(this);r.Points.AddRange(Points);r.BankStrokes.AddRange(BankStrokes);r.ClosedEnds.UnionWith(ClosedEnds);return r;}
    // Runtime surface only: keep the saved route reversible, but put water below
    // the lower bank instead of draping an elevated sheet over its outside face.
    internal Route FitBanks(Func<float,float,float?> ground,float sea){
        var fitted=Copy();var limits=Points.Select(p=>p.Y).ToArray();
        var floors=Points.Select(p=>sea).ToArray();
        for(int i=Points.Count-1;i>0;i--){if(IsOutlet(i))floors[i]=Math.Max(floors[i],Points[i].Y);floors[Parent(i)]=Math.Max(floors[Parent(i)],floors[i]);}
        float radius=Width*.5f+1.5f;
        for(int i=1;i<Points.Count;i++){
            int parent=Parent(i);var a=Points[parent];var b=Points[i];float length=Node.Distance(a,b),sx=(b.Z-a.Z)/length,sz=-(b.X-a.X)/length,lower=0;
            int steps=Math.Max(1,(int)Math.Ceiling(length/.5f));
            for(int j=0;j<=steps;j++){
                var p=Node.Lerp(a,b,j/(float)steps);
                foreach(int side in new[]{-1,1}){
                    var bank=ground(p.X+sx*radius*side,p.Z+sz*radius*side);
                    if(bank.HasValue)lower=Math.Min(lower,bank.Value-.12f-p.Y);
                }
            }
            limits[parent]=Math.Min(limits[parent],a.Y+lower);limits[i]=Math.Min(limits[i],b.Y+lower);
        }
        for(int i=0;i<Points.Count;i++){
            var p=Points[i];float level=Math.Max(floors[i],Math.Min(limits[i],i==0?p.Y:fitted.Points[Parent(i)].Y));
            fitted.Points[i]=new Node(p.X,level,p.Z,p.Parent,IsOutlet(i));
        }
        // Spread a sudden drop upstream by lowering the intervening terraces.
        // Never raise a fitted bank ceiling or change the receiving-water floor.
        fitted.profileCeiling=fitted.Points.Select(p=>p.Y).ToArray();
        for(int pass=0;pass<64;pass++){
            float change=0;
            for(int child=fitted.Points.Count-1;child>0;child--){
                int i=fitted.Parent(child);if(i==0||fitted.IsOutlet(i))continue;
                int parent=fitted.Parent(i);var a=fitted.Points[parent];var p=fitted.Points[i];var b=fitted.Points[child];
                float before=Node.Distance(a,p),after=Node.Distance(p,b);
                float target=(a.Y*after+b.Y*before)/(before+after);
                float h=Math.Max(floors[i],Math.Min(p.Y,target));change=Math.Max(change,p.Y-h);
                fitted.Points[i]=new Node(p.X,h,p.Z,p.Parent,p.Outlet);
            }
            for(int i=1;i<fitted.Points.Count;i++){
                var p=fitted.Points[i];float h=Math.Max(floors[i],Math.Min(p.Y,fitted.Points[fitted.Parent(i)].Y));
                fitted.Points[i]=new Node(p.X,h,p.Z,p.Parent,p.Outlet);
            }
            if(change<.0001f)break;
        }
        // Shared node slopes give the effective surface rounded transitions.
        fitted.profileSlope=new float[Points.Count];var childCount=new int[Points.Count];
        var incoming=new float[Points.Count];var outgoing=new float[Points.Count];
        for(int i=1;i<Points.Count;i++){
            int parent=fitted.Parent(i);float slope=(fitted.Points[i].Y-fitted.Points[parent].Y)/Node.Distance(fitted.Points[i],fitted.Points[parent]);
            incoming[i]=slope;outgoing[parent]+=slope;childCount[parent]++;
        }
        fitted.profileSlope[0]=childCount[0]>0?outgoing[0]/childCount[0]:0;
        for(int i=1;i<Points.Count;i++){
            float slope=incoming[i];
            if(childCount[i]==0)fitted.profileSlope[i]=slope;
            else{
                float next=outgoing[i]/childCount[i];
                fitted.profileSlope[i]=slope*next>0?2*slope*next/(slope+next):0;
            }
        }
        fitted.Invalidate();return fitted;
    }

    // Spatial buckets bound work to nearby segments, independent of total river length.
    Dictionary<(int,int),List<int>> buckets;
    int indexedCount; float indexedWidth,indexedPadding;
    float[] lengths,starts;
    internal void Invalidate(){buckets=null;}
    void Index(){
        if(buckets!=null&&indexedCount==Points.Count&&indexedWidth==Width&&indexedPadding==BankPadding)return;
        indexedCount=Points.Count;indexedWidth=Width;indexedPadding=BankPadding;
        buckets=new Dictionary<(int,int),List<int>>();lengths=new float[Points.Count];starts=new float[Points.Count];
        float r=Width*.5f+BankPadding;
        for(int i=1;i<Points.Count;i++){
            int parent=Parent(i);var a=Points[parent];var b=Points[i];lengths[i]=Node.Distance(a,b);
            starts[i]=parent==0?0:starts[parent]+lengths[parent];
            for(int z=(int)Math.Floor((Math.Min(a.Z,b.Z)-r)/8);z<=(int)Math.Floor((Math.Max(a.Z,b.Z)+r)/8);z++)
            for(int x=(int)Math.Floor((Math.Min(a.X,b.X)-r)/8);x<=(int)Math.Floor((Math.Max(a.X,b.X)+r)/8);x++){
                if(!buckets.TryGetValue((x,z),out var entries))buckets[(x,z)]=entries=new List<int>();entries.Add(i);
            }
        }
    }
    internal int LastSampleCandidates {get;private set;}
    internal int FlowSegment(float x,float z){
        Index();int selected=-1;float nearest=float.PositiveInfinity;
        if(!buckets.TryGetValue(((int)Math.Floor(x/8),(int)Math.Floor(z/8)),out var entries))return selected;
        foreach(int i in entries){var a=Points[Parent(i)];var b=Points[i];float dx=b.X-a.X,dz=b.Z-a.Z,len=lengths[i];
            float t=Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/(len*len)));
            float px=x-a.X-dx*t,pz=z-a.Z-dz*t,d=px*px+pz*pz;
            if(d<nearest){nearest=d;selected=i;}}
        return selected;
    }
    // Continuous, position-based flow: shared vertices never choose different UV frames.
    // Only indexed nearby segments participate; this is evaluated when building the mesh.
    internal void Flow(float x,float z,out float vx,out float vz){
        Index();vx=vz=0;float sum=0;
        if(!buckets.TryGetValue(((int)Math.Floor(x/8),(int)Math.Floor(z/8)),out var entries))return;
        float reach=Width*.5f+2;
        foreach(int i in entries){
            var a=Points[Parent(i)];var b=Points[i];float len=lengths[i],dx=b.X-a.X,dz=b.Z-a.Z;
            float t=Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/(len*len)));
            float px=x-a.X-dx*t,pz=z-a.Z-dz*t,d=(float)Math.Sqrt(px*px+pz*pz);
            float w=Math.Max(0,1-d/reach);w=w*w*w*w;if(w<=0)continue;
            float speed=.45f+Math.Min(1.1f,Math.Max(0,(a.Y-b.Y)/len)*1.6f);
            vx+=dx/len*speed*w;vz+=dz/len*speed*w;sum+=w;
        }
        if(sum>0){vx/=sum;vz/=sum;}
    }
    internal void FlowCoordinates(int segment,float x,float z,out float across,out float downstream){
        Index();across=downstream=0;if(segment<1||segment>=Points.Count)return;
        var a=Points[Parent(segment)];var b=Points[segment];float dx=(b.X-a.X)/lengths[segment],dz=(b.Z-a.Z)/lengths[segment];
        // Use one segment frame per triangle: U is across the channel, V increases
        // from parent to child even for south/west flow, branches and end caps.
        across=(x-a.X)*dz-(z-a.Z)*dx;
        downstream=starts[segment]+(x-a.X)*dx+(z-a.Z)*dz;
    }
    internal bool Sample(float x,float z,out float distance,out float level,out float progress){
        Index();distance=float.PositiveInfinity;level=0;progress=0;LastSampleCandidates=0;
        if(!buckets.TryGetValue(((int)Math.Floor(x/8),(int)Math.Floor(z/8)),out var entries))return false;
        float nearest=float.PositiveInfinity;
        foreach(int i in entries){
            LastSampleCandidates++;
            var a=Points[Parent(i)];var b=Points[i];float dx=b.X-a.X,dz=b.Z-a.Z,len=lengths[i];
            float t=Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/(len*len)));
            var p=Node.Lerp(a,b,t);float d=(p.X-x)*(p.X-x)+(p.Z-z)*(p.Z-z);
            if(d<nearest){nearest=d;level=p.Y;progress=starts[i]+len*t;}
        }
        distance=(float)Math.Sqrt(nearest);return distance<=Width*.5f;
    }
    // Blend the planes of nearby reaches instead of switching height abruptly at
    // their nearest-segment boundary. Extend their slope into rounded end caps.
    internal bool Surface(float x,float z,out float distance,out float level,out float progress){
        bool wet=Sample(x,z,out distance,out level,out progress);
        if(float.IsInfinity(distance))return false;
        float sum=0,height=0;
        if(!buckets.TryGetValue(((int)Math.Floor(x/8),(int)Math.Floor(z/8)),out var entries))return wet;
        foreach(int i in entries){
            var a=Points[Parent(i)];var b=Points[i];float len=lengths[i],dx=b.X-a.X,dz=b.Z-a.Z;
            float projection=((x-a.X)*dx+(z-a.Z)*dz)/(len*len);
            float t=Math.Max(0,Math.Min(1,projection));
            float px=x-a.X-dx*t,pz=z-a.Z-dz*t,d=(float)Math.Sqrt(px*px+pz*pz);
            float weight=Math.Max(0,1-(d-distance));weight*=weight;
            if(weight==0)continue;
            // Never extend an outlet below its receiving lake or pool.
            float extended=IsOutlet(i)?t:Math.Max(-Width*.5f/len,Math.Min(1+Width*.5f/len,projection));
            float segment=a.Y+(b.Y-a.Y)*extended;
            if(profileSlope!=null&&projection>=0&&projection<=1){
                float delta=(b.Y-a.Y)/len;
                float ma=Math.Max(3*delta,Math.Min(0,profileSlope[Parent(i)])),mb=Math.Max(3*delta,Math.Min(0,profileSlope[i]));
                float t2=t*t,t3=t2*t;
                segment=(2*t3-3*t2+1)*a.Y+(t3-2*t2+t)*len*ma+(-2*t3+3*t2)*b.Y+(t3-t2)*len*mb;
                segment=Math.Max(b.Y,Math.Min(a.Y,segment));
            }
            if(profileCeiling!=null)segment=Math.Min(segment,profileCeiling[Parent(i)]+(profileCeiling[i]-profileCeiling[Parent(i)])*extended);
            height+=segment*weight;sum+=weight;
        }
        // Extending a steep plane backwards must not invent water above the source.
        if(sum>0)level=Math.Min(Points[0].Y,height/sum);
        return wet;
    }
    internal float Bed(float x,float z,float original,float? surfaceLevel=null)
    {
        float result=Math.Min(original+.35f,UnboundedBed(x,z,original,surfaceLevel));
        // An outlet can extend eight metres into receiving water. Remove the rim along
        // that entire submerged tail, not just inside a circle around its last point.
        float reach=Width*.5f+BankPadding;
        for(int end=1;end<Points.Count;end++)if(IsOutlet(end)){
            float water=Points[end].Y;if(original>water+.1f)continue;
            for(int i=end;i>0;i=Parent(i)){
                var b=Points[i];var a=Points[Parent(i)];if(b.Y>water+.1f)break;
                if(a.Y>water+.1f)a=b;
                float dx=b.X-a.X,dz=b.Z-a.Z,length2=dx*dx+dz*dz;
                float t=length2>0?Math.Max(0,Math.Min(1,((x-a.X)*dx+(z-a.Z)*dz)/length2)):0;
                float distance=Node.Distance(new Node(x,0,z),new Node(a.X+dx*t,0,a.Z+dz*t));
                float weight=Math.Max(0,Math.Min(1,(reach-distance)/1.5f));weight=weight*weight*(3-2*weight);
                float cleared=Math.Min(result,Math.Min(original,water-.3f));result+=(cleared-result)*weight;
                if(Points[Parent(i)].Y>water+.1f)break;
            }
        }
        return result;
    }
    // Trenching may excavate freely, but its decorative rim cannot turn low banks into walls.
    // The cap is relative to the underlying terrain, never the previous river overlay.
    float UnboundedBed(float x,float z,float original,float? surfaceLevel)
    {
        Surface(x,z,out float distance,out float level,out _);
        if(surfaceLevel.HasValue)level=surfaceLevel.Value;
        float softness=BankSoftness(x,z),radius=Width*.5f,bank=4+2*softness;
        if(distance>=radius+bank)return original;
        // The bed meets the water at its banks; depth only changes the channel bottom.
        float t=Math.Min(1,distance/radius);
        float bed=level-Depth*(1-t*t)+.18f*t*t;
        if(OutletBlend(x,z)>0&&original<level-.1f)return distance<=radius?Math.Min(original,bed):original;
        if(distance<=radius){
            // Preserve deep excavation in the channel; the outer bank must contain the water
            // even on a cross-slope. This narrow berm is part of the reversible overlay.
            float bankBlend=Math.Max(0,(t-.65f)/.35f);bankBlend*=bankBlend;
            float floor=Math.Min(original,bed);
            float result=floor+(level+.18f-.14f*softness-floor)*bankBlend;
            return result+(Math.Min(original,bed)-result)*OutletBlend(x,z);
        }
        t=(distance-radius)/bank;t=t*t*(3-2*t);
        float lip=level+.24f;
        float bankHeight=lip+(original-lip)*t;
        // A rounded outside rim conceals the extended water mesh even on low ground.
        float rim=Math.Max(0,Math.Min(1,(4-(distance-radius))/1.5f));rim=rim*rim*(3-2*rim);
        bankHeight=Math.Max(bankHeight,original+(level+.24f-original)*rim);
        return bankHeight+(Math.Min(original,bankHeight)-bankHeight)*OutletBlend(x,z);
    }

    internal static Route Trace(Func<float,float,float?> ground,float sea,float heading,float length,float width,float depth,out string status,Func<float,float,float?> water=null)
    {
        var r=new Route{Width=width,Depth=depth};r.Points.Add(new Node(0,-.35f,0));status="Reached the requested length.";
        for(int i=0;i<Math.Min(90,(int)(length/2));i++){
            var a=r.Points[r.Points.Count-1];Node best=default;float score=float.PositiveInfinity,bestAngle=heading;bool found=false;
            for(int j=-3;j<=3;j++){
                float angle=heading+j*.22f,x=a.X+(float)Math.Sin(angle)*2,z=a.Z+(float)Math.Cos(angle)*2;
                var h=ground(x,z);if(!h.HasValue||h.Value>a.Y+.85f)continue;
                bool loop=r.Points.Take(Math.Max(0,r.Points.Count-3)).Any(p=>Node.Distance(p,new Node(x,0,z))<1.5f);if(loop)continue;
                float s=h.Value+Math.Abs(j)*.12f;if(s>=score)continue;
                float receiving=water?.Invoke(x,z)??sea;if(receiving>a.Y+.05f)continue;
                score=s;bestAngle=angle;best=new Node(x,Math.Min(a.Y,Math.Max(receiving,h.Value-.35f)),z);found=true;
            }
            if(!found){status="Stopped at a rise or unloaded terrain. Guide the route to continue.";break;}
            r.Points.Add(best);heading=bestAngle;
            var bottom=ground(best.X,best.Z);
            float receiver=water?.Invoke(best.X,best.Z)??sea;
            if(best.Y<=receiver+.03f&&bottom.HasValue&&bottom.Value<receiver-.15f){r.Mouth=true;r.ExtendOutlet(ground,sea,water);status="Connected to receiving water with a submerged outlet.";break;}
        }
        // A short spring pool is still useful when no downhill route exists.
        if(r.Points.Count==1){var x=(float)Math.Sin(heading)*1.5f;var z=(float)Math.Cos(heading)*1.5f;
            if(!ground(x,z).HasValue)return null;r.Points.Add(new Node(x,-.35f,z));}
        return r.Valid?r:null;
    }
    void ExtendOutlet(Func<float,float,float?> ground,float sea,Func<float,float,float?> water){
        if(Points.Count<2)return;int lastIndex=Points.Count-1;var last=Points[lastIndex];var previous=Points[Parent(lastIndex)];
        float d=Node.Distance(previous,last),dx=(last.X-previous.X)/d,dz=(last.Z-previous.Z)/d;
        Points[lastIndex]=new Node(last.X,last.Y,last.Z,last.Parent);
        for(int i=1;i<=4&&Points.Count<MaxNodes&&Length+2<=MaxLength;i++){
            float x=last.X+dx*i*2,z=last.Z+dz*i*2;var h=ground(x,z);float receiver=water?.Invoke(x,z)??sea;
            if(!h.HasValue||h.Value>=last.Y-.15f||Math.Abs(receiver-last.Y)>.05f)break;
            Points.Add(new Node(x,last.Y,z));
        }
        var end=Points[Points.Count-1];Points[Points.Count-1]=new Node(end.X,end.Y,end.Z,end.Parent,true);
    }
    internal bool Append(Node target,Func<float,float,float?> ground,float sea,out string error,Func<float,float,float?> water=null) => AppendFrom(Points.Count-1,target,ground,sea,out error,water);
    internal bool NeedsContinuation(Node target,int start){
        float distance=Node.Distance(Points[start],target);
        return Length+distance>96||Points.Count+(int)Math.Ceiling(distance/2)>MaxNodes;
    }
    internal bool AppendFrom(int start,Node target,Func<float,float,float?> ground,float sea,out string error,Func<float,float,float?> water=null)
    {
        error="";if(start<0||start>=Points.Count){error="Select a river point first.";return false;}
        if(ClosedEnds.Contains(start)){error="This branch is ended. Use Reopen River End here, or select another branch with River Start.";return false;}
        if(IsOutlet(start)){error="This branch ends at natural water. Select another branch or an upstream junction with River Start.";return false;}
        var a=Points[start];float d=Node.Distance(a,target);
        if(!Finite(d)||d<1||d>64.001f){error="Aim at least 1 metre beyond the selected river point; each trench step advances up to 64 metres.";return false;}
        if(Length+d>MaxLength){error="This section is full. Continue from its endpoint into a new section.";return false;}
        int count=(int)Math.Ceiling(d/2);if(Points.Count+count>MaxNodes){error="Route point limit reached.";return false;}
        var added=new List<Node>();float last=a.Y;bool mouth=false;
        for(int i=1;i<=count;i++){
            var p=Node.Lerp(a,target,i/(float)count);var h=ground(p.X,p.Z);
            if(!h.HasValue||h.Value>last+8f){error="The route meets rising or unloaded terrain. Choose a point within eight metres of the water level.";return false;}
            float receiving=water?.Invoke(p.X,p.Z)??sea;
            if(receiving>last+.05f){error="The receiving water is higher than this river. Start upstream at its level.";return false;}
            float y=Math.Min(last,Math.Max(receiving,h.Value-.35f));
            bool outlet=y<=receiving+.03f&&h.Value<receiving-.15f;
            added.Add(new Node(p.X,y,p.Z,i==1?start:-1,outlet));last=y;
            if(outlet){mouth=true;break;}
        }
        var candidate=Copy();candidate.PreserveOutlet();candidate.Points.AddRange(added);candidate.Mouth=mouth;if(mouth)candidate.ExtendOutlet(ground,sea,water);
        if(!candidate.Valid){error="That route exceeds the supported bounds.";return false;}
        Points.Clear();Points.AddRange(candidate.Points);Mouth=candidate.Mouth;Invalidate();return true;
    }
}
