using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace RunicPaths.Core;

// Values are world-space targets, not successive edits. Both copies of a seam
// vertex therefore receive exactly the same target, regardless of loading order.
public readonly struct SurfaceVertex
{
    public readonly ushort Index;
    public readonly float Height;
    public readonly byte Grade, Paint;
    public SurfaceVertex(int index,float height,byte grade,byte paint)
    { Index=(ushort)index; Height=height; Grade=grade; Paint=paint; }
}

public sealed class SurfaceTile
{
    public Cell Cell;
    public SurfaceVertex[] Vertices=Array.Empty<SurfaceVertex>();
    private ushort[]? clearedArea;
    private HashSet<int>? protectedVertices;
    public bool Protected(int index)
    {
        if(protectedVertices==null) protectedVertices=new HashSet<int>(Vertices.Where(v=>v.Grade==0 && v.Paint==0).Select(v=>(int)v.Index));
        return protectedVertices.Contains(index);
    }
    public bool Cleared(Point p, float radius=0)
    {
        if(clearedArea==null)
        {
            clearedArea=new ushort[66*66];
            foreach(var v in Vertices) if(v.Paint>127) clearedArea[(v.Index/65+1)*66+v.Index%65+1]=1;
            for(int row=1;row<=65;row++) for(int column=1;column<=65;column++)
            {int i=row*66+column;clearedArea[i]=(ushort)(clearedArea[i]+clearedArea[i-1]+clearedArea[i-66]-clearedArea[i-67]);}
        }
        int x=(int)Math.Round(p.X-Cell.X*64+32-0.5f),z=(int)Math.Round(p.Z-Cell.Z*64+32-0.5f);
        int r=(int)Math.Ceiling(radius);
        int loX=Math.Max(0,x-r),hiX=Math.Min(64,x+r),loZ=Math.Max(0,z-r),hiZ=Math.Min(64,z+r);
        if(loX>hiX || loZ>hiZ) return false;
        // Summed-area lookup: even a large boulder costs four array reads.
        return clearedArea[(hiZ+1)*66+hiX+1]-clearedArea[loZ*66+hiX+1]-clearedArea[(hiZ+1)*66+loX]+clearedArea[loZ*66+loX]>0;
    }
    public void Warm() {Cleared(Cell.At(64));Protected(0);}
}

public sealed class PreparedSurface
{
    public long World;
    public int Seed, Signature;
    public readonly Dictionary<Cell,SurfaceTile> Tiles=new Dictionary<Cell,SurfaceTile>();
    public bool Cleared(Point p,float radius=0)
    {
        Cell c=Cell.From(p,64);
        // Clearance footprints can overlap a neighboring tile.
        for(int x=-1;x<=1;x++) for(int z=-1;z<=1;z++)
            if(Tiles.TryGetValue(new Cell(c.X+x,c.Z+z),out var tile) && tile.Cleared(p,radius)) return true;
        return false;
    }

    // All terrain calls occur during preparation. Runtime consumers only read arrays.
    public IEnumerable<int> Build(WorldPaths network,ITerrain terrain,PlannerSettings settings,IReadOnlyList<Exclusion>? protection=null,PreparedSurface? previous=null)
    {
        World=network.WorldId;Seed=network.Seed;
        var segments=new Dictionary<Cell,List<(Point A,Point B,float HA,float HB)>>();
        foreach(var route in network.Islands.SelectMany(i=>i.Routes))
        {
            var points=new List<Point>();
            for(int i=1;i<route.Points.Count;i++)
            {
                Point a=route.Points[i-1],b=route.Points[i];
                int n=Math.Max(1,(int)Math.Ceiling(Point.Distance(a,b)/2));
                for(int j=0;j<n;j++) points.Add(Point.Lerp(a,b,j/(float)n));
            }
            points.Add(route.Points[route.Points.Count-1]);
            var heights=new float[points.Count];
            for(int i=0;i<points.Count;i++) {heights[i]=terrain.Height(points[i]);yield return 0;}
            var smooth=new float[heights.Length];
            for(int i=0;i<smooth.Length;i++)
            {
                float total=0,weight=0;
                for(int j=Math.Max(0,i-2);j<=Math.Min(smooth.Length-1,i+2);j++)
                {float w=3-Math.Abs(i-j);total+=heights[j]*w;weight+=w;}
                smooth[i]=total/weight;
            }
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1];var b=points[i];float r=settings.Width/2+3;
                Cell lo=Cell.From(new Point(Math.Min(a.X,b.X)-r,Math.Min(a.Z,b.Z)-r),64);
                Cell hi=Cell.From(new Point(Math.Max(a.X,b.X)+r,Math.Max(a.Z,b.Z)+r),64);
                for(int x=lo.X;x<=hi.X;x++) for(int z=lo.Z;z<=hi.Z;z++)
                {
                    var c=new Cell(x,z);
                    if(!segments.TryGetValue(c,out var list)) segments[c]=list=new List<(Point,Point,float,float)>();
                    list.Add((a,b,smooth[i-1],smooth[i]));
                }
                yield return 0;
            }
        }
        foreach(var pair in segments.OrderBy(k=>k.Key))
        {
            var vertices=new List<SurfaceVertex>();
            var protectedAreas=protection?.Where(e=>Point.Distance(e.Center,pair.Key.At(64))<e.Radius+60).ToArray()??Array.Empty<Exclusion>();
            var previousVertices=previous!=null && previous.Tiles.TryGetValue(pair.Key,out var previousTile)?previousTile.Vertices.ToDictionary(v=>(int)v.Index):new Dictionary<int,SurfaceVertex>();
            // A small spatial index bounds preparation cost even at busy intersections.
            var grid=new Dictionary<Cell,List<(Point A,Point B,float HA,float HB)>>();
            float reach=settings.Width/2+3;
            foreach(var s in pair.Value)
            {
                Cell lo=Cell.From(new Point(Math.Min(s.A.X,s.B.X)-reach,Math.Min(s.A.Z,s.B.Z)-reach),8);
                Cell hi=Cell.From(new Point(Math.Max(s.A.X,s.B.X)+reach,Math.Max(s.A.Z,s.B.Z)+reach),8);
                for(int x=lo.X;x<=hi.X;x++) for(int z=lo.Z;z<=hi.Z;z++)
                {var c=new Cell(x,z);if(!grid.TryGetValue(c,out var list)) grid[c]=list=new List<(Point,Point,float,float)>();list.Add(s);}
            }
            (float Distance,float Height) Sample(Point p)
            {
                if(!grid.TryGetValue(Cell.From(p,8),out var list)) return (float.MaxValue,0);
                float best=float.MaxValue; double total=0,weight=0;
                foreach(var s in list)
                {
                    Point d=s.B-s.A;float length=d.X*d.X+d.Z*d.Z;
                    float t=length<0.0001f?0:Math.Max(0,Math.Min(1,((p.X-s.A.X)*d.X+(p.Z-s.A.Z)*d.Z)/length));
                    float distance=Point.Distance(p,Point.Lerp(s.A,s.B,t));
                    best=Math.Min(best,distance);
                    if(distance>=reach) continue;
                    double w=(reach-distance)*(reach-distance);
                    total+=(s.HA+(s.HB-s.HA)*t)*w;weight+=w;
                }
                return (best,weight>0?(float)(total/weight):0);
            }
            for(int z=0;z<=64;z++) for(int x=0;x<=64;x++)
            {
                Point p=new Point(pair.Key.X*64-32+x,pair.Key.Z*64-32+z);
                var sample=Sample(p);var paintSample=Sample(p+new Point(.5f,.5f));
                float grade=Smooth((settings.Width/2+2-sample.Distance)/2);
                float variation=1+.08f*(float)(Math.Sin((p.X+.5f)*.17+(p.Z+.5f)*.09)*Math.Sin((p.Z+.5f)*.23));
                float paint=Smooth((settings.Width*.5f*variation+.8f-paintSample.Distance)/1.1f);
                if(grade>0 || paint>0)
                {
                    if(protectedAreas.Any(e=>e.Contains(p)))
                    {
                        int index=z*65+x;
                        // Keep already prepared ground beneath existing structures when
                        // width/grade settings change. First conversion retains native edits.
                        vertices.Add(previousVertices.TryGetValue(index,out var old)?old:new SurfaceVertex(index,0,0,0));
                        continue;
                    }
                    float original=terrain.Height(p);
                    if(original>=settings.WaterLevel+settings.ShoreClearance && terrain.Allowed(p))
                    {
                        float height=original+Math.Max(-settings.MaximumLower,Math.Min(settings.MaximumRaise,sample.Height-original))*grade;
                        byte g=(byte)Math.Round(grade*255),c=(byte)Math.Round(paint*255);
                        if(g>0 || c>0) vertices.Add(new SurfaceVertex(z*65+x,height,g,c));
                    }
                }
                if((x&7)==0) yield return 0;
            }
            if(vertices.Count>0)
            {
                var present=new HashSet<int>(vertices.Select(v=>(int)v.Index));
                foreach(var old in previousVertices.Values)
                {
                    Point p=new Point(pair.Key.X*64-32+old.Index%65,pair.Key.Z*64-32+old.Index/65);
                    if(!present.Contains(old.Index) && protectedAreas.Any(e=>e.Contains(p))) vertices.Add(old);
                }
                var tile=new SurfaceTile{Cell=pair.Key,Vertices=vertices.OrderBy(v=>v.Index).ToArray()};tile.Warm();Tiles[pair.Key]=tile;
            }
            yield return 0;
        }
    }
    private static float Smooth(float t) {t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}

    public byte[] Encode()
    {
        using var data=new MemoryStream();
        using(var w=new BinaryWriter(data,System.Text.Encoding.UTF8,true))
        {
            w.Write(0x52505331);w.Write(World);w.Write(Seed);w.Write(Signature);w.Write(Tiles.Count);
            foreach(var pair in Tiles.OrderBy(p=>p.Key))
            {
                w.Write(pair.Key.X);w.Write(pair.Key.Z);w.Write(pair.Value.Vertices.Length);
                foreach(var v in pair.Value.Vertices) {w.Write(v.Index);w.Write(v.Height);w.Write(v.Grade);w.Write(v.Paint);}
            }
        }
        byte[] bytes=data.ToArray();using var sha=SHA256.Create();byte[] hash=sha.ComputeHash(bytes);
        data.Write(hash,0,hash.Length);return data.ToArray();
    }
    public static PreparedSurface Decode(byte[] bytes,long world,int seed)
    {
        if(bytes.Length<60 || bytes.Length>256*1024*1024) throw new InvalidDataException("Invalid prepared surface length");
        using var sha=SHA256.Create();var hash=sha.ComputeHash(bytes,0,bytes.Length-32);
        for(int i=0;i<32;i++) if(hash[i]!=bytes[bytes.Length-32+i]) throw new InvalidDataException("Prepared surface checksum failed");
        using var stream=new MemoryStream(bytes,0,bytes.Length-32);using var r=new BinaryReader(stream);
        if(r.ReadInt32()!=0x52505331) throw new InvalidDataException("Unsupported prepared surface format");
        var result=new PreparedSurface {World=r.ReadInt64(),Seed=r.ReadInt32(),Signature=r.ReadInt32()};
        if(result.World!=world || result.Seed!=seed) throw new InvalidDataException("Prepared surface world mismatch");
        int count=r.ReadInt32();if(count<0 || count>100000) throw new InvalidDataException("Invalid tile count");
        for(int i=0;i<count;i++)
        {
            Cell c=new Cell(r.ReadInt32(),r.ReadInt32());int n=r.ReadInt32();
            if(Math.Abs(c.X)>157 || Math.Abs(c.Z)>157 || n<0 || n>4225 || result.Tiles.ContainsKey(c)) throw new InvalidDataException("Invalid surface tile");
            var vertices=new SurfaceVertex[n];int previous=-1;
            for(int j=0;j<n;j++)
            {
                int index=r.ReadUInt16();float h=r.ReadSingle();byte grade=r.ReadByte(),paint=r.ReadByte();
                if(index<=previous || index>=4225 || float.IsNaN(h) || float.IsInfinity(h) || Math.Abs(h)>10000) throw new InvalidDataException("Invalid surface vertex");
                previous=index;vertices[j]=new SurfaceVertex(index,h,grade,paint);
            }
            var tile=new SurfaceTile{Cell=c,Vertices=vertices};tile.Warm();result.Tiles.Add(c,tile);
        }
        if(stream.Position!=stream.Length) throw new InvalidDataException("Trailing surface data");
        return result;
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using(var f=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        {byte[] bytes=Encode();f.Write(bytes,0,bytes.Length);f.Flush(true);}
        if(File.Exists(path)) File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
    }
}
