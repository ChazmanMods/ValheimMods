using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace RunicSettlements.Core;
[DataContract] public sealed class SettlementOptions
{
    [DataMember] public int Count=5;
    [DataMember] public float Radius=60,Blend=14,Separation=300,SearchRadius=2000;
    [DataMember] public float MaximumRaise=8,MaximumLower=8,Width=5,Slope=.45f,Water=30;
    [DataMember] public float LampSpacing=40;
    [DataMember] public int MaximumLamps=96,TreesPerSite=2;
    [DataMember] public bool Decorate=true,ReplaceSmallRuins=true;
}
[DataContract] public sealed class SettlementSite
{
    [DataMember] public Point Center;
    [DataMember] public float Height;
    [DataMember] public string Name="";
}
[DataContract] public sealed class SettlementDecoration
{
    [DataMember] public string Id="",Prefab="";
    [DataMember] public Point Position;
    [DataMember] public float Height,Rotation;
    [DataMember] public bool Lamp;
    [DataMember] public string Text="";
}
[DataContract] public sealed class SettlementPlan
{
    [DataMember] public int Schema=1;
    [DataMember] public long World;
    [DataMember] public int Seed;
    [DataMember] public bool Complete;
    [DataMember] public Point Spawn,Anchor;
    [DataMember] public SettlementOptions Options=new SettlementOptions();
    [DataMember] public List<SettlementSite> Sites=new List<SettlementSite>();
    [DataMember] public List<Route> Roads=new List<Route>();
    [DataMember] public List<SettlementDecoration> Decorations=new List<SettlementDecoration>();
    [DataMember] public string Report="";
    [DataMember] public List<string> ReplacedLocations=new List<string>();
    [DataMember] public List<string> RemovedDecorations=new List<string>();
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using(var stream=new FileStream(file+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        {new DataContractJsonSerializer(typeof(SettlementPlan)).WriteObject(stream,this);stream.Flush(true);}
        if(File.Exists(file)) File.Replace(file+".tmp",file,file+".bak");else File.Move(file+".tmp",file);
    }
    public static SettlementPlan? Load(string file,long world,int seed)
    {
        if(!File.Exists(file)) return null;
        using var stream=File.OpenRead(file);
        var plan=(SettlementPlan?)new DataContractJsonSerializer(typeof(SettlementPlan)).ReadObject(stream);
        if(plan==null || plan.Schema!=1 || plan.World!=world || plan.Seed!=seed || plan.Options==null ||
           plan.Sites==null || plan.Roads==null || plan.Decorations==null || plan.Sites.Count>5 ||
           !(plan.Options.Radius>=20 && plan.Options.Radius<=80) ||
           plan.Sites.Any(s=>!Valid(s.Center) || float.IsNaN(s.Height) || Math.Abs(s.Height)>1000) ||
           plan.Roads.Any(r=>r.Points==null || r.Points.Count<2 || r.Points.Any(p=>!Valid(p))))
            throw new InvalidDataException("Settlement plan is invalid or belongs to another world. Restore its matching backup.");
        return plan;
    }
    private static bool Valid(Point p)=>!float.IsNaN(p.X) && !float.IsNaN(p.Z) && p.Length<10000;
}

public sealed class SettlementTerrain : ITerrain
{
    private readonly ITerrain source;private readonly SettlementPlan plan;
    public SettlementTerrain(ITerrain source,SettlementPlan plan){this.source=source;this.plan=plan;}
    public bool Allowed(Point p)=>source.Allowed(p);
    public float Height(Point p)
    {
        float h=source.Height(p);
        foreach(var site in plan.Sites)
        {float t=Math.Max(0,Math.Min(1,(plan.Options.Radius+plan.Options.Blend-Point.Distance(p,site.Center))/plan.Options.Blend));
         if(t>0) return h+(site.Height-h)*t*t*(3-2*t);}
        return h;
    }
}
