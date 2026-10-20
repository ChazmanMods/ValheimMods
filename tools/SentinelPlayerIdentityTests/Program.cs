using RunicSentinel.Runtime;
using RunicSentinel.PlayerActions;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;System.Console.WriteLine("PASS "+name);}
var peer=new ZNetPeer{m_uid=41,m_characterID=new ZDOID(7),m_playerID=0};
ZNet.instance=new ZNet();ZNet.instance.Peers.Add(peer);ZDOMan.instance=new ZDOMan();
var zdo=new ZDO{Owner=41,Character=123456};ZDOMan.instance.Objects[peer.m_characterID]=zdo;
Check(SentinelPlayerIdentity.CharacterId(peer)==123456,"Resolve spawned identity despite zero legacy peer ID");
zdo.Owner=99;Check(SentinelPlayerIdentity.CharacterId(peer)==0,"Reject another peer's character object");zdo.Owner=41;
zdo.Valid=false;Check(SentinelPlayerIdentity.CharacterId(peer)==0,"Reject invalid character object");zdo.Valid=true;
peer.Ready=false;Check(SentinelPlayerIdentity.CharacterId(peer)==0,"Reject disconnected peer");peer.Ready=true;
using var service=new SentinelPlayerCommandService();service.Tick(ZNet.instance);
void Hello(long id){var p=new ZPackage();p.Write(PlayerActionProtocol.Schema);p.Write(id);peer.m_rpc.Deliver(PlayerActionProtocol.CapabilityRpc,p);}
Hello(0);Check(!service.Ready("steam:account","0"),"Reject zero identity handshake");
Hello(999);Check(!service.Ready("steam:account","123456"),"Reject mismatched identity handshake");
Hello(123456);Check(service.Ready("steam:account","123456"),"Accept current client's unmodified v1 handshake");
Check(!service.Ready("steam:other","123456"),"Do not cross account boundaries");
string op=service.Start("steam:account\n123456\nheal\n","admin");
Check(service.Result(op,"admin").StartsWith("pending\n"),"Dispatch action for exact ready character");
zdo.Character=777;Check(!service.Ready("steam:account","123456"),"Invalidate readiness immediately on character change");
service.Tick(ZNet.instance);Check(service.Result(op,"admin").StartsWith("failed\n"),"Cancel action on character change");
Hello(777);Check(service.Ready("steam:account","777"),"Accept new character only after fresh handshake");
ZNet.instance.Peers.Clear();service.Tick(ZNet.instance);Check(!service.Ready("steam:account","777"),"Clear readiness on disconnect");
System.Console.WriteLine($"{passed}/{passed} passed");
public readonly record struct ZDOID(long Value){public static readonly ZDOID None=new(0);}
public class ZDO{public long Owner,Character;public bool Valid=true;public bool IsValid()=>Valid;public long GetOwner()=>Owner;public long GetLong(int key,long fallback)=>Character;}
public static class ZDOVars{public const int s_playerID=1;}
public class ZDOMan{public static ZDOMan instance;public Dictionary<ZDOID,ZDO> Objects=new();public ZDO GetZDO(ZDOID id)=>Objects.GetValueOrDefault(id);}
public class ZNetPeer{public long m_uid,m_playerID;public ZDOID m_characterID;public bool m_server,Ready=true;public string m_playerName="test";public ZRpc m_rpc=new();public bool IsReady()=>Ready;}
public class ZNet{public static ZNet instance;public List<ZNetPeer> Peers=new();public bool IsServer()=>true;public List<ZNetPeer> GetPeers()=>Peers;}
public class ZRpc{readonly Dictionary<string,Action<ZRpc,ZPackage>> handlers=new();public void Register<T>(string key,Action<ZRpc,T> action)=>handlers[key]=(rpc,p)=>action(rpc,(T)(object)p);public void Unregister(string key)=>handlers.Remove(key);public void Invoke(string key,ZPackage p){}public void Deliver(string key,ZPackage p)=>handlers[key](this,p);}
public class ZPackage{readonly List<object> values=new();int pos;public void Write(object v)=>values.Add(v);public int ReadInt()=>(int)values[pos++];public long ReadLong()=>(long)values[pos++];public bool ReadBool()=>(bool)values[pos++];public string ReadString()=>(string)values[pos++];public int Size()=>values.Count;public int GetPos()=>pos;}
public class Skills{public enum SkillType{None,Run,All}}
namespace UnityEngine{public static class Debug{public static void Log(string message)=>System.Console.WriteLine(message);}}
namespace RunicSentinel.Runtime{internal static class SentinelTransportIdentity{internal static bool TryResolvePeer(ZNetPeer p,out string a,out string s,out string reason){a="steam";s="account";reason="";return p.IsReady();}}internal static class SentinelPlayerReports{internal static void Record(string a,string k,string d){}}}
