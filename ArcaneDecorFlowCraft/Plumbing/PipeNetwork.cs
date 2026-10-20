using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArcaneDecorFlowCraft.Plumbing;

internal sealed class PipeNetwork:MonoBehaviour
{
    internal const float ConnectionTolerance=.09f;
    float next;
    static bool dirty=true;
    internal static void Invalidate()=>dirty=true;
    internal static void RefreshIfDirty(){if(dirty)Recalculate();}
    internal static int Revision;
    sealed class Port {internal int Node,Index;internal Vector3 Position,Direction;internal bool Paired;}
    static (int,int,int) Cell(Vector3 p)=>(Mathf.FloorToInt(p.x/ConnectionTolerance),Mathf.FloorToInt(p.y/ConnectionTolerance),Mathf.FloorToInt(p.z/ConnectionTolerance));
    void Update(){if(!dirty&&Time.time<next)return;next=Time.time+.5f;Recalculate();}
    internal static bool CanConnect(Vector3 a,Vector3 directionA,Vector3 b,Vector3 directionB)=>(a-b).sqrMagnitude<=ConnectionTolerance*ConnectionTolerance&&Vector3.Dot(directionA,directionB)<-.94f;
    internal static void Recalculate(){
        dirty=false;Physics.SyncTransforms();
        var pipes=new List<PipePiece>();foreach(var piece in PipePiece.Active)if(piece&&piece.isActiveAndEnabled&&piece.Live)pipes.Add(piece);
        // Stable pairing when a player stacks more than two ports at one point. One port has only one mate.
        pipes.Sort((a,b)=>string.CompareOrdinal(a.View.GetZDO().m_uid.ToString(),b.View.GetZDO().m_uid.ToString()));
        var nodes=new List<PipeFlowModel.Node>();var ports=new List<Port>();var buckets=new Dictionary<(int,int,int),List<Port>>();
        for(int n=0;n<pipes.Count;n++){
            var pipe=pipes[n];if(pipe.WallAdapter)pipe.WallAdapter.gameObject.SetActive(false);var node=new PipeFlowModel.Node{Pump=PipePrefabs.Powered(pipe.Kind),SourceHeight=pipe.Source(),Crest=double.NegativeInfinity};nodes.Add(node);
            for(int i=0;i<PipePiece.PortCount(pipe.Kind);i++){
                if(pipe.Joints[i])pipe.Joints[i].gameObject.SetActive(false);pipe.Connected[i]=false;var port=new Port{Node=n,Index=i,Position=pipe.Port(i),Direction=pipe.Direction(i)};node.Crest=Math.Max(node.Crest,port.Position.y);ports.Add(port);
                var key=Cell(port.Position);if(!buckets.TryGetValue(key,out var list)){list=new List<Port>();buckets.Add(key,list);}list.Add(port);
            }
        }
        foreach(var port in ports){
            if(port.Paired||PipePrefabs.Dragon(pipes[port.Node].Kind))continue;var cell=Cell(port.Position);Port best=null;float distance=float.PositiveInfinity;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                if(buckets.TryGetValue((cell.Item1+x,cell.Item2+y,cell.Item3+z),out var list))foreach(var other in list){
                    if(other.Paired||PipePrefabs.Dragon(pipes[other.Node].Kind)||PipePrefabs.Narrow(pipes[port.Node].Kind)!=PipePrefabs.Narrow(pipes[other.Node].Kind)||port.Node==other.Node||!CanConnect(port.Position,port.Direction,other.Position,other.Direction))continue;
                    float d=(port.Position-other.Position).sqrMagnitude;if(d<distance){distance=d;best=other;}
                }
            if(best==null)continue;port.Paired=best.Paired=true;
            pipes[port.Node].Connected[port.Index]=pipes[best.Node].Connected[best.Index]=true;
            pipes[port.Node].ShowJoint(port.Index,best.Position,best.Direction);
            nodes[port.Node].Neighbors.Add(best.Node);nodes[best.Node].Neighbors.Add(port.Node);
        }
        // Connect a dragon through its one supporting wall, consuming exactly one matching rear pipe port.
        foreach(var inlet in ports){
            var dragon=pipes[inlet.Node];if(!PipePrefabs.Dragon(dragon.Kind)||inlet.Index!=0||!DragonMount.Find(dragon,out var mount))continue;
            Port best=null;bool elbow=false;float nearest=float.PositiveInfinity;
            foreach(var candidate in ports){
                if(candidate.Paired||!DragonMount.Target(pipes[candidate.Node],candidate.Index,dragon,mount,out var target,out bool bend))continue;
                if(!DragonMount.MatchesFeed(pipes[candidate.Node],candidate.Index,mount,target,bend,out float distance)||distance>nearest)continue;
                nearest=distance;best=candidate;elbow=bend;
            }
            if(best==null)continue;best.Paired=inlet.Paired=true;
            dragon.Connected[0]=pipes[best.Node].Connected[best.Index]=true;
            nodes[inlet.Node].Neighbors.Add(best.Node);nodes[best.Node].Neighbors.Add(inlet.Node);
            if(elbow){
                if(!dragon.WallAdapter){var go=new GameObject("automatic rear wall elbow");go.transform.SetParent(dragon.transform,false);dragon.WallAdapter=go.AddComponent<WallPipeAdapter>();}
                dragon.WallAdapter.Show(best.Position,best.Direction,mount.Normal,PipePrefabs.DiameterScale(dragon.Kind));
            }
        }
        foreach(var port in ports){
            var pipe=pipes[port.Node];bool dragon=PipePrefabs.Dragon(pipe.Kind);
            if(port.Paired||PipePrefabs.BaseKind(pipe.Kind)==PipeKind.Intake||dragon&&(port.Index==0||!pipe.Connected[0]))continue;
            if(Exposed(pipe,port.Position,port.Direction))nodes[port.Node].Outlets.Add(new PipeFlowModel.Outlet{Port=port.Index,Height=port.Position.y});
        }
        PipeFlowModel.Solve(nodes);for(int i=0;i<pipes.Count;i++)pipes[i].Apply(nodes[i]);Revision++;
    }
    static bool Exposed(PipePiece pipe,Vector3 point,Vector3 direction){
        var beyond=point+direction*.12f;
        if(PipeEnvironment.SampleGround(beyond,out float ground)&&ground>beyond.y-.04f)return false;
        // Raycasts do not report the collider containing their origin. Wall sockets
        // deliberately sit slightly inside the stone, so check the outlet volume too.
        foreach(var collider in Physics.OverlapCapsule(point-direction*.025f,point+direction*.16f,.025f,LayerMask.GetMask("piece","static_solid","Default"),QueryTriggerInteraction.Ignore))
            if(collider.GetComponentInParent<PipePiece>()!=pipe)return false;
        var hits=Physics.RaycastAll(point,direction,.16f,LayerMask.GetMask("piece","static_solid","Default"),QueryTriggerInteraction.Ignore);
        foreach(var hit in hits)if(hit.collider.GetComponentInParent<PipePiece>()!=pipe)return false;
        return true;
    }
}
