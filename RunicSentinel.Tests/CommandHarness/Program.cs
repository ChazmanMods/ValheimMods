using System;
using System.Collections.Generic;
using System.Reflection;
using RunicSentinel.Runtime;
using UnityEngine;

internal static class Program
{
    static int count;
    static void Check(bool value,string label){if(!value)throw new Exception(label);count++;System.Console.WriteLine("PASS "+label);}
    static void Reject(Action action,string label){try{action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
    static void Main()
    {
        int saves=0,local=0;
        new Terminal.ConsoleCommand("save","save world",a=>{saves++;a.Context.AddString("Saved test world");});
        new Terminal.ConsoleCommand("debugmode","debug",a=>local++);
        new Terminal.ConsoleCommand("spawn","spawn",a=>local++);
        new Terminal.ConsoleCommand("event","event",a=>throw new Exception("Must use Sentinel's headless handler"));
        new Terminal.ConsoleCommand("skiptime","seconds",(Terminal.ConsoleEventFailable)(a=>false));
        Check(SentinelCommandProvider.Authorize("save","steam:1")==SentinelCommandProvider.ServerRoute+"Saved test world"&&saves==1,"server handler runs once and captures output");
        Check(SentinelCommandProvider.Authorize("debugmode","steam:1")==SentinelCommandProvider.ClientRoute&&local==0,"client mode is routed without server invocation");
        Check(SentinelCommandProvider.Authorize("spawn Wood 1","steam:1")==SentinelCommandProvider.ClientRoute&&local==0,"spawn remains player-local");
        Reject(()=>SentinelCommandProvider.Authorize("server debugmode","steam:1"),"headless player-local execution rejected");
        Reject(()=>SentinelCommandProvider.Authorize("missing","steam:1"),"unknown command rejected");
        Reject(()=>SentinelCommandProvider.Authorize("skiptime invalid","steam:1"),"native handler parameter failure surfaced");
        Check(SentinelCommandProvider.Authorize("save","steam:1").Contains("Saved test world"),"output capture cleaned up after handler failure");
        ZNet.instance.Peers.Add(new ZNetPeer{m_playerName="Player One",Account="steam:1",m_uid=1,m_characterID=new ZDOID(1),m_refPos=new Vector3(10,20,30)});
        ZNet.instance.Peers.Add(new ZNetPeer{m_playerName="Player Two",Account="steam:2",m_uid=2,m_characterID=new ZDOID(2),m_refPos=new Vector3(40,50,60)});
        SentinelCommandProvider.Authorize("tp Player One | Player Two","steam:1");
        Check(ZRoutedRpc.instance.LastPeer==1&&((Vector3)ZRoutedRpc.instance.LastArgs[0]).x==40,"teleport addresses exact character owner and destination");
        SentinelCommandProvider.Authorize("tp steam:2 | 100,200,30","steam:1");
        var position=(Vector3)ZRoutedRpc.instance.LastArgs[0];
        Check(position.x==100&&position.z==200&&position.y==30,"teleport coordinate order is x,z,y");
        Reject(()=>SentinelCommandProvider.Authorize("tp steam:2 | NaN,0,0","steam:1"),"invalid teleport coordinates rejected");
        ZNet.instance.Peers.Add(new ZNetPeer{m_playerName="Player One",Account="steam:3",m_uid=3});
        Reject(()=>SentinelCommandProvider.Authorize("tp Player One | Player Two","steam:1"),"duplicate names require exact identity");
        SentinelCommandProvider.Authorize("event testevent","steam:1");
        Check(RandEventSystem.instance.Position.x==10,"event uses authenticated requester position on headless server");
        System.Console.WriteLine(count+" command execution tests passed.");
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]public sealed class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string name,Type[] args){}}
    public static class AccessTools{public static FieldInfo Field(Type type,string name)=>type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);}
}
namespace UnityEngine
{
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public string ToString(string format)=>$"{x},{y},{z}";}
    public struct Quaternion{public static Quaternion identity=>new Quaternion();}
}
public class Terminal
{
    private static Dictionary<string,ConsoleCommand> commands=new Dictionary<string,ConsoleCommand>();
    public delegate void ConsoleEvent(ConsoleEventArgs a);public delegate object ConsoleEventFailable(ConsoleEventArgs a);
    public class ConsoleEventArgs{public Terminal Context;public ConsoleEventArgs(string line,Terminal context,ConsoleCommand command){Context=context;}}
    public class ConsoleCommand
    {
        public string Command,Description;private ConsoleEvent action;private ConsoleEventFailable actionFailable;
        public ConsoleCommand(string command,string description,ConsoleEvent handler){Command=command;Description=description;action=handler;commands[command]=this;}
        public ConsoleCommand(string command,string description,ConsoleEventFailable handler){Command=command;Description=description;actionFailable=handler;commands[command]=this;}
    }
    public void AddString(string text)=>SentinelCommandProvider.Capture(this,text);
}
public class Console:Terminal{public static Console instance=new Console();}
public struct ZDOID{readonly int value;public ZDOID(int id){value=id;}public static ZDOID None=>new ZDOID();public static bool operator==(ZDOID a,ZDOID b)=>a.value==b.value;public static bool operator!=(ZDOID a,ZDOID b)=>a.value!=b.value;public override bool Equals(object o)=>o is ZDOID id&&id==this;public override int GetHashCode()=>value;}
public class ZNetPeer{public ZRpc m_rpc=new ZRpc();public string m_playerName,Account;public bool m_server;public long m_uid;public ZDOID m_characterID;public Vector3 m_refPos;public bool IsReady()=>m_uid!=0;}
public class ZNet{public static ZNet instance=new ZNet();public List<ZNetPeer> Peers=new List<ZNetPeer>();public List<ZNetPeer> GetPeers()=>Peers;}
public class ZRoutedRpc{public static ZRoutedRpc instance=new ZRoutedRpc();public long LastPeer;public object[] LastArgs;public void InvokeRoutedRPC(long peer,ZDOID id,string name,params object[] args){LastPeer=peer;LastArgs=args;}}
public class RandEventSystem{public static RandEventSystem instance=new RandEventSystem();public Vector3 Position;public bool HaveEvent(string name)=>name=="testevent";public void SetRandomEventByName(string name,Vector3 p){Position=p;}}
namespace RunicSentinel.Runtime{internal static class SentinelTransportIdentity{internal static bool TryResolvePeer(ZNetPeer peer,out string authority,out string subject,out string reason){var parts=peer.Account.Split(':');authority=parts[0];subject=parts[1];reason="";return true;}}}

public class ZRpc{public Socket GetSocket()=>new Socket();}
public class Socket{public string GetHostName()=>"Steam_1";}
public class ZDOMan{public static ZDOMan instance=new ZDOMan();public ZDO GetZDO(ZDOID id)=>new ZDO();}
public class ZDO{public long GetLong(int id)=>1;}
public static class ZDOVars{public static int s_playerID=1;}
namespace RunicSentinel.Devcommands{
 public static class TerminalUtils{public static bool SkipProcessing(string text)=>false;}
 public static class TryRunCommand{public static string CheckLogic(string text)=>text;}
 public static class RedirectOutput{public static ZRpc Target;}
 public static class PermissionLoader{public static RuleData Data=new RuleData();}
 public class RuleData{public Rules Resolve(string id,string character)=>new Rules();}
 public class Rules{public bool IsAdmin;public bool IsCommandAllowed(Terminal.ConsoleCommand command,string line,bool remote)=>true;}
}
