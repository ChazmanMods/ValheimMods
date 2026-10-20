using System;
using System.Collections.Generic;
using ArcaneDecorFlowCraft.Plumbing;

internal static class PlumbingTests
{
    internal static void Run(){
        int checks=0;
        void Assert(bool pass,string name){checks++;if(!pass)throw new Exception("Plumbing: "+name);}
        var intake=new PipeFlowModel.Node{SourceHeight=10,Crest=9};
        var pipe=new PipeFlowModel.Node{Crest=9};
        var outlet=new PipeFlowModel.Outlet{Height=8};pipe.Outlets.Add(outlet);
        var nodes=new List<PipeFlowModel.Node>{intake,pipe};intake.Neighbors.Add(1);pipe.Neighbors.Add(0);
        PipeFlowModel.Solve(nodes);Assert(outlet.Flow==5,"unpowered downhill gravity");
        outlet.Height=11;pipe.Crest=11;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==0,"gravity cannot lift");
        pipe.Pump=true;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==5,"one pump lifts at five gallons per second");
        intake.Pump=true;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==10,"two pumps add capacity");
        intake.SourceHeight=double.NaN;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==0&&!pipe.Supplied,"pumps do not create a water supply");
        intake.SourceHeight=10;pipe.Neighbors.Clear();intake.Neighbors.Clear();PipeFlowModel.Solve(nodes);Assert(outlet.Flow==0,"disconnected pipe stops");
        intake.Neighbors.Add(1);pipe.Neighbors.Add(0);pipe.Outlets.Add(new PipeFlowModel.Outlet{Height=11});PipeFlowModel.Solve(nodes);
        Assert(pipe.Outlets[0].Flow==5&&pipe.Outlets[1].Flow==5,"multiple outlets split finite pumping capacity");
        pipe.Neighbors.Add(0);intake.Neighbors.Add(1);PipeFlowModel.Solve(nodes);Assert(outlet.Flow==5,"duplicate edges do not duplicate pumps");
        pipe.Outlets.RemoveAt(1);pipe.Crest=9;outlet.Height=8;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==15,"pumps add to downhill gravity capacity");
        intake.Pump=pipe.Pump=false;pipe.Crest=12;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==0,"gravity cannot cross high crests even with lower outlet");
        pipe.Crest=9;intake.Crest=12;PipeFlowModel.Solve(nodes);Assert(outlet.Flow==0,"gravity intake must not require suction lift");
        intake.Crest=9;pipe.Outlets.Clear();PipeFlowModel.Solve(nodes);Assert(pipe.Supplied,"closed line remains supplied without creating an outlet");
        // Distinct networks do not share pumps or sources, even when all are loaded.
        var separate=new PipeFlowModel.Node{Pump=true,Crest=0};var dry=new PipeFlowModel.Outlet{Height=-5};separate.Outlets.Add(dry);nodes.Add(separate);
        PipeFlowModel.Solve(nodes);Assert(dry.Flow==0,"separate powered network remains dry");
        // A second gravity source above a crest supplies a valid route independently.
        pipe.Outlets.Add(outlet);pipe.Crest=12;intake.SourceHeight=10;
        var high=new PipeFlowModel.Node{SourceHeight=14,Crest=13};nodes.Add(high);pipe.Neighbors.Add(3);high.Neighbors.Add(1);
        PipeFlowModel.Solve(nodes);Assert(outlet.Flow==5,"highest connected water source supplies gravity head");
        Assert(nodes[0].Running,"water discharge marks connected pumps running");
        foreach(var n in nodes)n.Outlets.Clear();PipeFlowModel.Solve(nodes);
        Assert(!nodes[0].Running,"sealed network is not running despite available supply");
        foreach(var n in nodes)n.SourceHeight=double.NaN;pipe.Outlets.Add(outlet);PipeFlowModel.Solve(nodes);
        Assert(!pipe.Running&&!pipe.Supplied,"dry line does not activate orb");
        Assert(PipeFlowModel.JetSpeed(10)>=PipeFlowModel.JetSpeed(5)*1.9f,"twice the outlet flow produces clearly greater launch speed");
        Console.WriteLine("PASS: "+checks+" plumbing hydraulic regressions.");
    }
}
