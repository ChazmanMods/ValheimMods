using System;
using System.Collections.Generic;
using System.Linq;
namespace RunicSettlements.Core;
public static class SettlementSurface
{
    public static IEnumerable<int> AddSites(PreparedSurface surface,SettlementPlan plan,ITerrain terrain)
    {
        float radius=plan.Options.Radius,blend=plan.Options.Blend,reach=radius+blend;
        foreach(var site in plan.Sites)
        {
            var lo=Cell.From(site.Center-new Point(reach,reach),64);var hi=Cell.From(site.Center+new Point(reach,reach),64);
            for(int tx=lo.X;tx<=hi.X;tx++) for(int tz=lo.Z;tz<=hi.Z;tz++)
            {
                var cell=new Cell(tx,tz);
                var vertices=surface.Tiles.TryGetValue(cell,out var old)?old.Vertices.ToDictionary(v=>(int)v.Index):new Dictionary<int,SurfaceVertex>();
                for(int z=0;z<=64;z++) for(int x=0;x<=64;x++)
                {
                    var p=new Point(tx*64-32+x,tz*64-32+z);
                    float distance=Point.Distance(p,site.Center);
                    float t=Math.Max(0,Math.Min(1,(reach-distance)/blend));t=t*t*(3-2*t);
                    if(t==0) continue;
                    float original=terrain.Height(p),height=original+(site.Height-original)*t;
                    // Final per-vertex guard also covers narrow features between survey samples.
                    if(height-original>plan.Options.MaximumRaise+.01f || original-height>plan.Options.MaximumLower+.01f)
                        throw new InvalidOperationException("A clearing exceeds its cut/fill limit at "+p.X+","+p.Z+"; preparation stopped before publication");
                    int index=z*65+x;byte paint=(byte)Math.Round(t*255),material=0;
                    if(vertices.TryGetValue(index,out var road) && road.Paint>127 && road.Material==1)
                    {paint=Math.Max(paint,road.Paint);material=1;}
                    vertices[index]=new SurfaceVertex(index,height,(byte)Math.Round(t*255),paint,material);
                    if((x&7)==0) yield return 0;
                }
                var tile=new SurfaceTile {Cell=cell,Vertices=vertices.Values.OrderBy(v=>v.Index).ToArray()};tile.Warm();surface.Tiles[cell]=tile;yield return 0;
            }
        }
    }
    public static float Height(PreparedSurface surface,ITerrain terrain,Point p)
    {
        var cell=Cell.From(p,64);
        if(surface.Tiles.TryGetValue(cell,out var tile))
        {
            int x=(int)Math.Round(p.X-cell.X*64+32),z=(int)Math.Round(p.Z-cell.Z*64+32),index=z*65+x;
            foreach(var v in tile.Vertices) if(v.Index==index && v.Grade>0) return v.Height;
        }
        return terrain.Height(p);
    }
}
