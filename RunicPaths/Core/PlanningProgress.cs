using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace RunicPaths.Core;

[DataContract]
public sealed class PlannedLink
{
    [DataMember] public int A;
    [DataMember] public int B;
    [DataMember] public List<Point> Path = new List<Point>();
}

// Written only at complete attempt boundaries. An interrupted A* is retried;
// completed successes and failures are never searched again.
[DataContract]
public sealed class PlanningProgress
{
    [DataMember] public int Schema=1;
    [DataMember] public long World;
    [DataMember] public int Seed;
    [DataMember] public int Signature;
    [DataMember] public Point Landing;
    [DataMember] public Island Island = new Island();
    [DataMember] public List<Poi> Pois = new List<Poi>();
    [DataMember] public List<PlannedLink> Links = new List<PlannedLink>();
    [DataMember] public int Cursor, Attempts, LoopCursor, LoopAttempts, Loops;
    [DataMember] public long FailedNodes, TotalNodes;
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        {new DataContractJsonSerializer(typeof(PlanningProgress)).WriteObject(stream,this);stream.Flush(true);}
        if(File.Exists(path)) File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
    }
    public static PlanningProgress? Load(string path,long world,int seed,int signature)
    {
        if(!File.Exists(path)) return null;
        using var stream=File.OpenRead(path);
        var p=(PlanningProgress?)new DataContractJsonSerializer(typeof(PlanningProgress)).ReadObject(stream);
        if(p==null || p.Schema!=1 || p.World!=world || p.Seed!=seed || p.Signature!=signature ||
           p.Island?.Cells==null || p.Island.Cells.Count==0 || p.Island.Routes==null || p.Pois==null || p.Links==null ||
           p.Cursor<0 || p.LoopCursor<0 || p.Attempts<0 || p.FailedNodes<0 || p.TotalNodes<p.FailedNodes ||
           p.Links.Any(l=>l.A<0 || l.B<0 || l.A>=p.Pois.Count || l.B>=p.Pois.Count || l.Path==null || l.Path.Count<2 ||
               l.Path.Any(v=>float.IsNaN(v.X) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Z) || v.Length>10000)))
            throw new InvalidDataException("Invalid island preparation checkpoint or world identity mismatch");
        return p;
    }
}
