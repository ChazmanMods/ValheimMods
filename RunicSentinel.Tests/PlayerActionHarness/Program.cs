using RunicSentinel.PlayerActions;
using RunicSentinel.Runtime;
using UnityEngine;

int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;System.Console.WriteLine("PASS "+name);}
void Reject(Action action,string name){try{action();}catch(InvalidOperationException){Check(true,name);return;}throw new Exception(name);}
var server=new ZNet{Server=true};var client=new ZNet();var player=new Player{Id=42,Name="Target"};client.Local=player;
var otherClient=new ZNet{Local=new Player{Id=99,Name="Other"}};
ZNetPeer Connect(ZNet target,long id,string account)
{
    var serverRpc=new ZRpc{Context=server};var clientRpc=new ZRpc{Context=target};serverRpc.Partner=clientRpc;clientRpc.Partner=serverRpc;
    var peer=new ZNetPeer{m_rpc=serverRpc,m_playerID=id,Account=account};server.Peers.Add(peer);target.ServerPeer=new ZNetPeer{m_rpc=clientRpc,m_server=true};return peer;
}
var peer=Connect(client,42,"steam:42");Connect(otherClient,99,"steam:99");
ZNet.instance=server;using var service=new SentinelPlayerCommandService();service.Tick(server);
using var receiver=new PlayerActionReceiver();using var otherReceiver=new PlayerActionReceiver();
void Context(ZNet network,Action action){var old=ZNet.instance;var local=Player.m_localPlayer;ZNet.instance=network;Player.m_localPlayer=network.Local;try{action();}finally{ZNet.instance=old;Player.m_localPlayer=local;}}
Context(client,receiver.Tick);Context(otherClient,otherReceiver.Tick);
Check(service.Ready("steam:42","42"),"capability handshake binds the online character");
string Start(string action,string args)=>service.Start("steam:42\n42\n"+action+"\n"+args,"steam:admin");
var id=Start("raiseskill","Run 5");
Check(player.Skills.Level==5&&otherClient.Local.Skills.Level==0,"raise skill affects only the selected target");
Check(service.Result(id,"steam:admin").StartsWith("done\n"),"completion waits for the selected client's acknowledgment");
Reject(()=>service.Result(id,"steam:another-admin"),"another administrator cannot retrieve the action result");
Reject(()=>service.Start("steam:42\n500\nheal\n","steam:admin"),"stale character selection is rejected");
Reject(()=>service.Start("steam:absent\n42\nheal\n","steam:admin"),"missing receiver or player is rejected");
Reject(()=>Start("raiseskill","Run 500"),"skill amount is bounded");
Reject(()=>Start("raiseskill","123 2"),"numeric skill enums are not accepted");
Reject(()=>Start("arbitrary-command","save"),"arbitrary remote command execution is rejected");
Reject(()=>Start("heal","extra"),"zero-argument actions reject injected arguments");
Achievements.Confirmed=false;id=Start("heal","");Check(service.Result(id,"steam:admin").StartsWith("failed\n")&&player.Heals==0,"native cheat confirmation is preserved");Achievements.Confirmed=true;
id=Start("heal","");Check(player.Heals==1&&player.Stamina==100&&player.Eitr==100,"heal restores native health stamina and eitr");
id=Start("resetskill","Run");Check(player.Skills.Level==0,"reset skill uses the target skills object");
id=Start("puke","");Check(player.FoodCleared,"clear food uses the target character");
id=Start("clearstatus","");Check(player.SeMan.Cleared&&player.HardDeathCleared,"clear status preserves native clearstatus behavior");
id=Start("adrenaline","75");Check(player.Adrenaline==75,"adrenaline is set rather than blindly added");
id=Start("addstatus","Missing");Check(service.Result(id,"steam:admin").StartsWith("failed\n"),"unknown target-client status effect is rejected");
ZPackage Packet(string op,long character,string action,string args)
{var p=new ZPackage();p.Write(op);p.Write(character);p.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()+30);p.Write(action);p.Write(args);return p;}
string replayId=Guid.NewGuid().ToString("N");var packet=Packet(replayId,42,"raiseskill","Run 3");
peer.m_rpc.Invoke(PlayerActionProtocol.ActionRpc,packet);peer.m_rpc.Invoke(PlayerActionProtocol.ActionRpc,packet);
Check(player.Skills.Level==3,"duplicate operation executes at most once");
Context(client,()=>client.ServerPeer.m_rpc.Handlers[PlayerActionProtocol.ActionRpc](new ZRpc(),Packet(Guid.NewGuid().ToString("N"),42,"raiseskill","Run 3")));
Check(player.Skills.Level==3,"receiver rejects a connection other than its exact server RPC");
peer.m_rpc.Invoke(PlayerActionProtocol.ActionRpc,Packet(Guid.NewGuid().ToString("N"),999,"raiseskill","Run 3"));
Check(player.Skills.Level==3,"receiver rejects a different local character identity");
foreach(string mode in new[]{"god","ghost","fly","nocost"})
{
    id=Start(mode,"on");Check(service.Result(id,"steam:admin").StartsWith("done\n"),mode+" grant acknowledged");
    id=Start(mode,"on");Check(service.Result(id,"steam:admin").Contains("On"),mode+" On is idempotent");
    Context(client,()=>Check(PlayerActionReceiver.Granted(mode),mode+" has a connection-bound grant"));
    id=Start(mode,"off");Context(client,()=>Check(!PlayerActionReceiver.Granted(mode),mode+" Off removes grant"));
}
Check(!otherClient.Local.InGodMode()&&!otherClient.Local.IsDebugFlying(),"mode actions leave other players unchanged");
Reject(()=>Start("god","true"),"mode actions require explicit on/off");
Achievements.Confirmed=false;id=Start("god","on");Check(service.Result(id,"steam:admin").StartsWith("failed\n")&&!player.InGodMode(),"mode grants preserve cheat confirmation");
id=Start("modes","");Check(service.Result(id,"steam:admin").StartsWith("done\n"),"mode status query needs no cheat acknowledgment");Achievements.Confirmed=true;
ObjectDB.instance.m_StatusEffects.AddRange(new StatusEffect[]{new StatusEffect{name="Rested"},new SE_Shield{name="Shield"},new SE_Burning{name="Burning"},new SE_Poison{name="Poison"}});
id=Start("addstatus","rested 3600");Check(player.SeMan.Last.m_ttl==3600&&ObjectDB.instance.m_StatusEffects[0].m_ttl==60,"duration changes the applied effect, not the template");
id=Start("addstatus","Rested 0");Check(player.SeMan.Last.m_ttl==0,"zero duration remains indefinite");
id=Start("addstatus","Shield 30 200");Check(((SE_Shield)player.SeMan.Last).m_absorbDamage==200,"shield intensity sets absorption");
id=Start("addstatus","Burning 20 100");Check(((SE_Burning)player.SeMan.Last).m_fireDamageLeft==100,"burning intensity sets total damage");
id=Start("addstatus","Poison 20 100");Check(((SE_Poison)player.SeMan.Last).m_damagePerHit==10,"poison intensity uses duration and tick interval");
id=Start("addstatus","Rested 20 100");Check(service.Result(id,"steam:admin").StartsWith("failed\n"),"unsupported intensity is rejected");
foreach(string statusArgs in new[]{"Rested NaN","Rested Infinity","Rested -1","Burning 20 -5","Rested 20 extra extra"})Reject(()=>Start("addstatus",statusArgs),"reject invalid status parameters: "+statusArgs);
Start("god","on");Start("fly","on");Context(client,receiver.Dispose);Check(!player.InGodMode()&&!player.IsDebugFlying(),"disconnect cleanup restores prior character modes");
Context(client,receiver.Tick);Start("ghost","on");var replacement=new Player{Id=43};Context(client,()=>{Player.m_localPlayer=replacement;receiver.Tick();});Check(!player.InGhostMode(),"character switch clears old character grant");Context(client,receiver.Tick);
client.ServerPeer.m_rpc.DropActions=true;id=Start("heal","");Check(service.Result(id,"steam:admin").StartsWith("pending\n"),"unacknowledged action is not reported as success");
server.Peers.Remove(peer);service.Tick(server);Check(service.Result(id,"steam:admin").StartsWith("failed\n"),"disconnect leaves an explicitly unconfirmed result");
System.Console.WriteLine(passed+" player action tests passed.");

public class ZPackage
{
    readonly MemoryStream stream;readonly BinaryReader reader;readonly BinaryWriter writer;
    public ZPackage(){stream=new();reader=new(stream);writer=new(stream);}
    public ZPackage(byte[] bytes):this(){writer.Write(bytes);stream.Position=0;}
    public byte[] Bytes()=>stream.ToArray();public int Size()=>(int)stream.Length;public int GetPos()=>(int)stream.Position;
    public void Write(string value)=>writer.Write(value);public void Write(long value)=>writer.Write(value);public void Write(int value)=>writer.Write(value);public void Write(bool value)=>writer.Write(value);
    public string ReadString()=>reader.ReadString();public long ReadLong()=>reader.ReadInt64();public int ReadInt()=>reader.ReadInt32();public bool ReadBool()=>reader.ReadBoolean();
}
public class ZRpc
{
    public ZRpc Partner;public ZNet Context;public bool DropActions;public Dictionary<string,Action<ZRpc,ZPackage>> Handlers=new();
    public void Register<T>(string name,Action<ZRpc,T> action)=>Handlers[name]=(rpc,p)=>action(rpc,(T)(object)p);
    public void Unregister(string name)=>Handlers.Remove(name);
    public void Invoke(string name,ZPackage p)
    {
        if(Partner==null||(name==PlayerActionProtocol.ActionRpc&&Partner.DropActions)||!Partner.Handlers.TryGetValue(name,out var handler))return;
        var old=ZNet.instance;var local=Player.m_localPlayer;ZNet.instance=Partner.Context;Player.m_localPlayer=Partner.Context.Local;
        try{handler(Partner,new ZPackage(p.Bytes()));}finally{ZNet.instance=old;Player.m_localPlayer=local;}
    }
}
public class ZNet{public static ZNet instance;public bool Server;public Player Local;public List<ZNetPeer> Peers=new();public ZNetPeer ServerPeer;public bool IsServer()=>Server;public ZNetPeer GetServerPeer()=>ServerPeer;public List<ZNetPeer> GetPeers()=>Peers;}
public class ZNetPeer{public ZRpc m_rpc;public bool m_server;public long m_playerID;public string Account,m_playerName;public bool IsReady()=>true;}
public class Skills
{
    public enum SkillType{None,Run,Jump,All}public float Level;
    public void CheatRaiseSkill(string name,float value)=>Level+=value;public void CheatResetSkill(string name)=>Level=0;
}
public class Player
{
    private bool m_debugFly;private bool god,ghost,nocost;
    public bool InGodMode()=>god;public bool InGhostMode()=>ghost;public bool IsDebugFlying()=>m_debugFly;public bool NoCostCheat()=>nocost;
    public void SetGodMode(bool v)=>god=v;public void SetGhostMode(bool v)=>ghost=v;public void SetNoPlacementCost(bool v)=>nocost=v;public void ToggleDebugFly()=>m_debugFly=!m_debugFly;
    public static Player m_localPlayer;public long Id;public string Name;public Skills Skills=new();public SEMan SeMan=new();public int Heals;public float Stamina,Eitr,Adrenaline;public bool FoodCleared,HardDeathCleared;
    public long GetPlayerID()=>Id;public string GetPlayerName()=>Name;public bool IsDead()=>false;public Skills GetSkills()=>Skills;public void Message(MessageHud.MessageType t,string s){}
    public float GetMaxHealth()=>100;public float GetMaxStamina()=>100;public float GetMaxEitr()=>100;public void Heal(float v)=>Heals++;public void AddStamina(float v)=>Stamina+=v;public void AddEitr(float v)=>Eitr+=v;
    public void ClearFood()=>FoodCleared=true;public void ClearHardDeath()=>HardDeathCleared=true;public SEMan GetSEMan()=>SeMan;public void AddAdrenaline(float v)=>Adrenaline+=v;public float GetAdrenaline()=>Adrenaline;
}
public class SEMan{public bool Cleared;public void RemoveAllStatusEffects()=>Cleared=true;public StatusEffect Last;public StatusEffect AddStatusEffect(int id,bool reset)=>Last=ObjectDB.instance.m_StatusEffects.First(e=>e.name.GetStableHashCode()==id).Copy();}
public class StatusEffect{public string name;public float m_ttl=60;public StatusEffect Copy()=>(StatusEffect)MemberwiseClone();}
public class SE_Shield:StatusEffect{public float m_absorbDamage;}
public class SE_Burning:StatusEffect{public float m_fireDamageLeft,m_spiritDamageLeft;public void AddFireDamage(float v)=>m_fireDamageLeft+=v;public void AddSpiritDamage(float v)=>m_spiritDamageLeft+=v;}
public class SE_Poison:StatusEffect{public float m_damageLeft,m_damagePerHit,m_damageInterval=2;}
public class ObjectDB{public static ObjectDB instance=new();public List<StatusEffect> m_StatusEffects=new();}
public class MessageHud{public enum MessageType{TopLeft}}
public static class Achievements{public static bool Confirmed=true;public static bool IsCheatedAtAll()=>Confirmed;}
public static class Hash{public static int GetStableHashCode(this string value)=>value.GetHashCode();}
namespace UnityEngine{public static class Debug{public static void Log(object message){}}public static class Time{public static float realtimeSinceStartup=>100;}}
namespace RunicSentinel.Runtime
{
    internal static class SentinelPlayerIdentity{internal static long CharacterId(ZNetPeer p)=>p.m_playerID;}
    internal static class SentinelTransportIdentity{internal static bool TryResolvePeer(ZNetPeer p,out string a,out string s,out string reason){var parts=p.Account.Split(':');a=parts[0];s=parts[1];reason="";return true;}}
    internal static class SentinelPlayerReports{internal static void Record(string a,string b,string c){}}
}
