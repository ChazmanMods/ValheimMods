using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RunicSettlements.Core;
using UnityEngine;

namespace RunicSettlements;

// Runs only while the loading gate is closed. Work is recorded before any ZDO
// moves, and no object is destroyed. Unknown provenance receives the same care
// as a player-planted tree.
internal static class PreparedObjects
{
    internal static float PrefabRadius(GameObject prefab)
    {
        if(prefab.GetComponent<TreeBase>() || prefab.GetComponent<Plant>()) return 1;
        float radius=1;
        foreach(var c in prefab.GetComponentsInChildren<Collider>(true))
        {
            if(c.isTrigger) continue;
            Vector3 size=c is BoxCollider box?box.size:c is SphereCollider sphere?Vector3.one*sphere.radius*2:
                c is CapsuleCollider capsule?new Vector3(capsule.radius*2,capsule.height,capsule.radius*2):
                c is MeshCollider mesh && mesh.sharedMesh?mesh.sharedMesh.bounds.size:Vector3.one*2;
            size=Vector3.Scale(size,c.transform.lossyScale);
            radius=Math.Max(radius,Math.Max(Math.Abs(size.x),Math.Abs(size.z))*.5f);
        }
        return Math.Min(radius,40);
    }
    private sealed class Obstacle
    {
        internal ZDO Zdo=null!;
        internal Vector3 Position;
        internal float Radius;
        internal bool Movable;
    }
    internal static IEnumerable<int> Relocate(PreparedSurface surface,string directory,float maximumDistance)
    {
        var records=((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan),"m_objectsByID").GetValue(ZDOMan.instance)).Values.ToArray();
        var obstacles=new Dictionary<Cell,List<Obstacle>>();
        var move=new List<Obstacle>();
        var prefabs=new Dictionary<int,(bool movable,bool blocker,float radius)>();
        foreach(var zdo in records)
        {
            int hash=zdo.GetPrefab();
            if(!prefabs.TryGetValue(hash,out var info))
            {
                var prefab=ZNetScene.instance.GetPrefab(hash);
                if(!prefab) {prefabs[hash]=(false,false,0);continue;}
                bool movable=RoadObjects.Movable(prefab);
                bool blocker=movable || prefab.GetComponent<Piece>() || prefab.GetComponent<WearNTear>();
                info=(movable,blocker,blocker?PrefabRadius(prefab):1);prefabs[hash]=info;
            }
            if(info.blocker)
            {
                var ob=new Obstacle{Zdo=zdo,Position=zdo.GetPosition(),Radius=info.radius,Movable=info.movable};
                Add(ob);
                if(info.movable && surface.Cleared(GameTerrain.PointOf(ob.Position),ob.Radius)) move.Add(ob);
            }
            yield return 0;
        }
        void Add(Obstacle o)
        {
            Point p=GameTerrain.PointOf(o.Position);float r=o.Radius+1;
            Cell lo=Cell.From(p-new Point(r,r),32),hi=Cell.From(p+new Point(r,r),32);
            for(int x=lo.X;x<=hi.X;x++) for(int z=lo.Z;z<=hi.Z;z++)
            {var c=new Cell(x,z);if(!obstacles.TryGetValue(c,out var list)) obstacles[c]=list=new List<Obstacle>();list.Add(o);}
        }
        var terrain=new GameTerrain();int moved=0,retained=0;
        using var file=new FileStream(Path.Combine(directory,"relocations.jsonl"),FileMode.Append,FileAccess.Write,FileShare.Read);
        using var writer=new StreamWriter(file);
        foreach(var ob in move)
        {
            // Do not move dungeon objects or objects deliberately suspended above ground.
            Point origin=GameTerrain.PointOf(ob.Position);float ground=terrain.Height(origin);
            if(Math.Abs(ob.Position.y-ground)>8) {retained++;continue;}
            Vector3? destination=null;
            for(float distance=2;distance<=maximumDistance && destination==null;distance+=2)
            for(int direction=0;direction<24 && destination==null;direction++)
            {
                double angle=direction*Math.PI/12;
                Point p=origin+new Point((float)Math.Cos(angle),(float)Math.Sin(angle))*distance;
                if(surface.Cleared(p,ob.Radius+2)) continue;
                float h=terrain.Height(p);
                if(h<ZoneSystem.instance.m_waterLevel+1.2f || Math.Abs(h-ground)>4) continue;
                bool clear=true;
                foreach(var delta in new[]{new Point(ob.Radius,0),new Point(-ob.Radius,0),new Point(0,ob.Radius),new Point(0,-ob.Radius)})
                    if(Math.Abs(terrain.Height(p+delta)-h)>1.5f) {clear=false;break;}
                Cell lo=Cell.From(p-new Point(ob.Radius+1,ob.Radius+1),32),hi=Cell.From(p+new Point(ob.Radius+1,ob.Radius+1),32);
                for(int x=lo.X;x<=hi.X && clear;x++) for(int z=lo.Z;z<=hi.Z && clear;z++)
                    if(obstacles.TryGetValue(new Cell(x,z),out var list))
                        foreach(var other in list) if(other!=ob && Point.Distance(p,GameTerrain.PointOf(other.Position))<ob.Radius+other.Radius+1) {clear=false;break;}
                if(clear) destination=GameTerrain.VectorOf(p,h+(ob.Position.y-ground));
                yield return 0;
            }
            if(!destination.HasValue) {retained++;continue;}
            Vector3 target=destination.Value;
            string F(float f)=>f.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            writer.WriteLine("{\"world\":"+surface.World+",\"revision\":"+surface.Signature+",\"id\":\""+ob.Zdo.m_uid+"\",\"from\":["+F(ob.Position.x)+","+F(ob.Position.y)+","+F(ob.Position.z)+"],\"to\":["+F(target.x)+","+F(target.y)+","+F(target.z)+"]}");
            writer.Flush();file.Flush(true);
            ob.Zdo.SetOwner(ZDOMan.GetSessionID());ob.Zdo.SetPosition(target);ob.Zdo.Set(RoadObjects.MovedKey,true);
            ob.Position=target;Add(ob);moved++;yield return 0;
        }
        Plugin.Report($"Prepared object clearance: relocated {moved}, retained {retained} without a safe destination. Relocation history: {directory}");
    }
}
