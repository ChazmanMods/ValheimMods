using System;
using ArcaneWaterWorks;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var ordinary=new Route();ordinary.Points.Add(new Node(0,-.35f,0));ordinary.Points.Add(new Node(0,-.35f,4));
Check(ordinary.Valid,"baseline valid");string saved=ordinary.Encode();
Check(!ordinary.Append(new Node(0,0,60),(x,z)=>0,-50,out var error),"raw model rejects an oversized atomic step");
Check(!error.Contains("192")&&ordinary.Encode()==saved,"oversized step does not falsely blame total river length or mutate the route");
var dense=new Route();for(int i=0;i<128;i++)dense.Points.Add(new Node(0,-.35f,i*.5f));
Check(dense.Valid&&dense.Length<96,"dense existing section remains readable");
Check(dense.NeedsContinuation(new Node(0,0,65),127),"point-limited section rolls over before Append rejects it");
var length=Route.Trace((x,z)=>0,-50,0,96,4,.8f,out _);
Check(length.NeedsContinuation(new Node(0,0,100),length.Points.Count-1),"normal length rollover");
Check(!ordinary.NeedsContinuation(new Node(0,0,8),1),"short edit stays in its section");
float total=0;int sections=0;var current=ordinary;
for(int i=0;i<100;i++){
 var tip=current.Points[current.Points.Count-1];var target=new Node(tip.X,0,tip.Z+32);
 if(current.NeedsContinuation(target,current.Points.Count-1)){total+=current.Length;sections++;current=new Route();current.Points.Add(new Node(0,-.35f,0));target=new Node(0,0,32);}
 Check(current.Append(target,(x,z)=>0,-50,out error),"repeated extension "+i+": "+error);
 Check(current.Valid&&Route.TryDecode(current.Encode(),out var restored)&&restored.Encode()==current.Encode(),"section roundtrip "+i);
}
total+=current.Length;
Check(total>3200&&sections>30,"connected river extends beyond three kilometres using bounded sections");
Console.WriteLine($"PASS {checks} classic DLL regression checks; {total} m across {sections+1} sections.");
