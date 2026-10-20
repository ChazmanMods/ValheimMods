using System;
using System.Collections.Generic;

namespace ArcaneDecorFlowCraft.Plumbing;

// Pure hydraulic rules. Connections are established from matching physical ports by PipeNetwork.
internal static class PipeFlowModel
{
    internal const float PumpGallonsPerSecond=5;
    // Fixed nozzle area: more discharge produces greater launch speed, not just a thicker stream.
    internal static float JetSpeed(float gallonsPerSecond)=>Math.Min(40,Math.Max(1,gallonsPerSecond*.8f));
    internal const float GravityGallonsPerSecond=5;
    internal sealed class Node
    {
        internal bool Pump;
        internal bool Supplied,Running;
        internal int ConnectedPumps;
        internal double SourceHeight=double.NaN, Crest;
        internal readonly List<int> Neighbors=new List<int>();
        internal readonly List<Outlet> Outlets=new List<Outlet>();
    }
    internal sealed class Outlet
    {
        internal int Port;
        internal double Height;
        internal float Flow;
    }
    internal static void Solve(IReadOnlyList<Node> nodes)
    {
        var seen=new bool[nodes.Count];
        for(int start=0;start<nodes.Count;start++){
            if(seen[start])continue;
            var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
            double head=double.NegativeInfinity;int pumps=0;
            while(queue.Count>0){int i=queue.Dequeue();component.Add(i);var n=nodes[i];
                if(n.Pump)pumps++;if(!double.IsNaN(n.SourceHeight))head=Math.Max(head,n.SourceHeight);
                foreach(var outlet in n.Outlets)outlet.Flow=0;
                foreach(int j in n.Neighbors)if(!seen[j]){seen[j]=true;queue.Enqueue(j);}
            }
            foreach(int i in component){nodes[i].Running=false;nodes[i].Supplied=!double.IsNegativeInfinity(head);nodes[i].ConnectedPumps=pumps;}
            if(double.IsNegativeInfinity(head))continue;
            // Gravity cannot cross a crest above its supply level. Powered pipes lift water through the entire connected line.
            var gravity=new HashSet<int>();
            foreach(int i in component)if(!double.IsNaN(nodes[i].SourceHeight)&&nodes[i].Crest<=head+.02){gravity.Add(i);queue.Enqueue(i);}
            while(queue.Count>0){int i=queue.Dequeue();foreach(int j in nodes[i].Neighbors)
                if(nodes[j].Crest<=head+.02&&gravity.Add(j))queue.Enqueue(j);}
            var outlets=new List<Outlet>();var downhill=new List<Outlet>();
            foreach(int i in component)foreach(var outlet in nodes[i].Outlets){
                outlets.Add(outlet);if(gravity.Contains(i)&&outlet.Height<head-.02)downhill.Add(outlet);
            }
            foreach(var outlet in outlets)outlet.Flow=pumps*PumpGallonsPerSecond/outlets.Count;
            foreach(var outlet in downhill)outlet.Flow+=GravityGallonsPerSecond/downhill.Count;
            bool running=false;foreach(var outlet in outlets)if(outlet.Flow>.001f){running=true;break;}
            foreach(int i in component)nodes[i].Running=running;
        }
    }
}
