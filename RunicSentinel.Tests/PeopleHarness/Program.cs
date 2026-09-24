using RunicSentinel.Runtime;
using System.Reflection;

var root=Path.Combine(Path.GetTempPath(),"sentinel-people-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
const string owner="76561198000000001",guest="76561198000000002";
int passed=0;
try
{
    void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    void Reject(Action action,string text){try{action();throw new Exception("Expected rejection: "+text);}catch(InvalidOperationException e){Check(e.Message.Contains(text),e.Message);}}
    void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    var network=ZNet.instance=new ZNet();network.m_adminList=new SyncedList(Path.Combine(root,"adminlist.txt"),owner);network.m_bannedList=new SyncedList(Path.Combine(root,"bannedlist.txt"));
    var managed=new SentinelManagedPolicyService();var runtime=new SentinelRuntime();
    string Apply(string action,string subject,bool value)=>SentinelPeople.Apply(action+"\nsteam:"+subject+"\n"+(value?"1":"0"),"steam",owner,runtime,managed);
    Test("grant role writes native file and creates restorable backup",()=>{
        Apply("admin",guest,true);Check(File.ReadAllLines(network.m_adminList.m_fileLocation.m_path).Contains(guest),"Role not persisted");
        var backups=Directory.GetFiles(root,"adminlist.txt.sentinel-*.bak");Check(backups.Length==1,"Backup missing");Check(File.ReadAllLines(backups[0]).SequenceEqual(new[]{owner}),"Backup changed");
    });
    Test("revocation removes native aliases and signed role",()=>{
        network.m_adminList.Add("Steam_"+guest);network.m_adminList.Add("V_"+guest);managed.Document.Administrators="steam|"+guest;Apply("admin",guest,false);
        Check(network.m_adminList.GetList().SequenceEqual(new[]{owner}),"Alias remained");Check(managed.Document.Administrators=="","Signed role remained");
    });
    Test("signed update failure restores original native list",()=>{
        Apply("admin",guest,true);managed.Document.Administrators="steam|"+guest;managed.FailApply=true;
        Reject(()=>Apply("admin",guest,false),"signing failed");managed.FailApply=false;
        Check(File.ReadAllLines(network.m_adminList.m_fileLocation.m_path).Contains(guest),"Rollback did not persist");
    });
    Test("missing signing key prevents partial native revocation",()=>{
        managed.Document.ManagedSigningKey=false;Reject(()=>Apply("admin",guest,false),"signing key");Check(network.m_adminList.GetList().Contains(guest),"Native role changed");managed.Document.ManagedSigningKey=true;
    });
    Test("ban and unban persist server file",()=>{Apply("ban",guest,true);Check(File.ReadAllLines(network.m_bannedList.m_fileLocation.m_path).Contains(guest),"Ban missing");Apply("ban",guest,false);Check(!File.ReadAllLines(network.m_bannedList.m_fileLocation.m_path).Contains(guest),"Ban remains");});
    Test("kick addresses the exact authenticated peer",()=>{
        var first=new ZNetPeer{Subject=guest};var second=new ZNetPeer{Subject="76561198000000003"};network.Peers.Add(first);network.Peers.Add(second);Apply("kick",guest,true);Check(ReferenceEquals(network.Kicked,first),"Wrong peer kicked");
    });
    Test("self lockout and malformed account requests are rejected",()=>{
        Reject(()=>Apply("ban",owner,true),"Self-removal");Reject(()=>Apply("admin",owner,false),"Self-removal");Reject(()=>Apply("kick",owner,true),"Self-removal");Reject(()=>Apply("ban","123",true),"17 digits");
    });
    Test("disabled native roles cannot be falsely reported as effective grants",()=>{
        SentinelConfig.UseServerAdminList.Value=false;Reject(()=>Apply("admin",guest,true),"disabled");SentinelConfig.UseServerAdminList.Value=true;
    });
    Console.WriteLine(passed+"/8 tests passed.");
}
finally{Directory.Delete(root,true);}

public static class FileHelpers
{
    public enum FileSource{Local,Cloud}
    public struct FileLocation{public string m_path;public FileSource m_fileSource;}
}
public class SyncedList
{
    public FileHelpers.FileLocation m_fileLocation;
    private readonly List<string> entries=new();
    public SyncedList(string path,params string[] initial){m_fileLocation=new(){m_path=path};entries.AddRange(initial);Save();}
    public List<string> GetList()=>new(entries);
    public void Add(string value){if(!entries.Contains(value))entries.Add(value);Save();}
    public void Remove(string value){entries.Remove(value);Save();}
    private void Save()=>File.WriteAllLines(m_fileLocation.m_path,entries);
}
public class Socket{public string GetHostName()=>"host";}
public class ZNetPeer{public string Subject;public Socket m_socket=new();public bool IsReady()=>true;}
public class ZNet
{
    public static ZNet instance;public SyncedList m_adminList,m_bannedList;public List<ZNetPeer> Peers=new();public ZNetPeer Kicked;
    public List<ZNetPeer> GetPeers()=>Peers;private void InternalKick(ZNetPeer peer)=>Kicked=peer;
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo Field(Type type,string name)=>type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        public static MethodInfo Method(Type type,string name,Type[] args)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null);
    }
}
namespace RunicSentinel.Runtime
{
    internal class SentinelRuntime{}
    internal class SentinelPlayerCommandService{internal static SentinelPlayerCommandService Current=null;internal bool Ready(string a,string c)=>false;}
    internal class ConfigValue{public bool Value=true;}
    internal static class SentinelConfig{public static ConfigValue UseServerAdminList=new();}
    internal class SentinelAdminDocument{public string Administrators="",BannedUsers="";public bool ManagedSigningKey=true;}
    internal class SentinelManagedPolicyService
    {
        internal SentinelAdminDocument Document=new();public bool FailApply;
        public SentinelAdminDocument CreateDocument(string unused)=>new(){Administrators=Document.Administrators,BannedUsers=Document.BannedUsers,ManagedSigningKey=Document.ManagedSigningKey};
        public void Apply(SentinelAdminDocument doc,string a,string s){if(FailApply)throw new InvalidOperationException("signing failed");Document=doc;}
    }
    internal class SentinelPlayerRecord{public string account,name,activity,observedUtc,location,characterId,commandTarget;public bool online,playerActionsReady,administrator,banned,nativeAdministrator,signedAdministrator,nativeBanned,signedBanned;}
    internal class SentinelPlayerList{public SentinelPlayerRecord[] players;public string generatedUtc,recordingSince;public int schema;}
    internal static class SentinelPlayerReports{public static SentinelPlayerRecord[] Records()=>Array.Empty<SentinelPlayerRecord>();public static void Record(string a,string b,string c){}}
    internal static class SentinelJson{public static string Write(object value)=>"";}
    internal static class SentinelTransportIdentity{public static bool TryResolvePeer(ZNetPeer peer,out string a,out string s,out string reason){a="steam";s=peer.Subject;reason="";return true;}}
}
