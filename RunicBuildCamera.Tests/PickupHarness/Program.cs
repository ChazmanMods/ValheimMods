using System.Reflection;
using RunicBuildCamera;
using RunicBuildCamera.Integration;
using UnityEngine;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Reset() { RemotePickupRuntime.Reset(); ItemDrop.All.Clear(); PrivateArea.All.Clear(); Player.m_localPlayer = new Player(); BuildCameraRuntime.IsActive = true; BuildCameraRuntime.Position = new Vector3(); Player.AutoPickup = true; Time.time += 10; }
void Tick() { Time.time += .25f; RemotePickupRuntime.Tick(); }
ItemDrop Drop(int id, bool fish = false) { var item = new ItemDrop(id, fish); ItemDrop.All.Add(item); return item; }

Reset();
var first = Drop(1); Tick();
Check(Player.m_localPlayer.Picked.SequenceEqual(new[]{1}), "initial activation collects one real drop");
var later = Drop(2); later.transform.position = new Vector3(0,-3,0); Tick();
Check(Player.m_localPlayer.Picked.SequenceEqual(new[]{1,2}) && !ItemDrop.All.Contains(later), "later submerged drop is collected and removed without toggling camera");
Check(PrivateArea.Calls == 0, "ordinary drops remain collectible where building permission would be denied");
var fish = Drop(3, true); Tick();
Check(!Player.m_localPlayer.Picked.Contains(3) && ItemDrop.All.Contains(fish) && fish.Requests == 0, "pufferfish actor is neither claimed nor collected");
Player.AutoPickup = false; var off = Drop(4); Tick(); Tick();
Check(!Player.m_localPlayer.Picked.Contains(4) && Player.m_localPlayer.Messages == 1, "autopickup off blocks pickup with one visible notice");
Player.AutoPickup = true; Tick();
Check(Player.m_localPlayer.Picked.Contains(4), "autopickup resumes without camera restart");
var far = Drop(5); far.transform.position = new Vector3(0,-11,0); Tick();
Check(!Player.m_localPlayer.Picked.Contains(5), "depth counts toward pickup range");
BuildCameraRuntime.Position = new Vector3(0,-2,0); Tick();
Check(Player.m_localPlayer.Picked.Contains(5), "moving camera into range collects the existing ground drop");
Reset();
var full = Drop(6); Player.m_localPlayer.Inventory.HasRoom = false; Tick();
Check(full.Requests == 0, "inventory full prevents ownership and pickup");
Player.m_localPlayer.Inventory.HasRoom = true; Player.m_localPlayer.Inventory.Weight = 300; Tick();
Check(full.Requests == 0, "carry weight prevents pickup");
Player.m_localPlayer.Inventory.Weight = 0; full.m_autoPickup = false; Tick();
Check(full.Requests == 0, "drop auto-pickup exclusion is preserved");
full.m_autoPickup = true; full.Tar = true; Tick();
Check(full.Requests == 0, "tar exclusion is preserved");
full.Tar = false; full.Piece = true; Tick();
Check(full.Requests == 0, "placed item exclusion is preserved");
full.Piece = false; full.Owned = false; Tick();
Check(full.Requests == 1 && Player.m_localPlayer.Picked.Count == 0, "ownership delay defers collection");
full.Owned = true; Player.m_localPlayer.AllowPickup = false; Tick();
Check(Player.m_localPlayer.Picked.Count == 0 && ItemDrop.All.Contains(full), "normal pickup veto is preserved");
Player.m_localPlayer.AllowPickup = true; Tick(); Tick();
Check(Player.m_localPlayer.Picked.Contains(6), "collection recovers after temporary veto");
Reset();
for(int i=0;i<300;i++) Drop(100+i);
for(int i=0;i<100;i++) Tick();
Check(Player.m_localPlayer.Picked.Count == 300 && Player.m_localPlayer.Picked.Distinct().Count() == 300 && ItemDrop.All.Count == 0, "multiple batches collect all 300 drops exactly once despite registry removal");
Reset();
var worldWard = new PrivateArea(); PrivateArea.All.Add(worldWard);
Drop(500); Tick();
Check(Player.m_localPlayer.Picked.Contains(500), "game-created ward without a player creator does not block pickup");
var foreignWard = new PrivateArea(); foreignWard.Piece.Creator=42; PrivateArea.All.Add(foreignWard);
var guarded = Drop(501); guarded.transform.position=new Vector3(0,-3,0); Tick();
Check(guarded.Requests==0 && ItemDrop.All.Contains(guarded), "foreign player ward blocks submerged item before ownership request");
foreignWard.Owner=true; Tick();
Check(Player.m_localPlayer.Picked.Contains(501), "ward owner can pick up alongside overlapping game ward");
foreignWard.Owner=false; foreignWard.Permitted=true; Drop(502); Tick();
Check(Player.m_localPlayer.Picked.Contains(502), "permitted player can pick up");
foreignWard.Permitted=false; foreignWard.Enabled=false; Drop(503); Tick();
Check(Player.m_localPlayer.Picked.Contains(503), "disabled player ward does not block pickup");
foreignWard.Enabled=true; foreignWard.transform.position=new Vector3(30,0,0); Drop(504); Tick();
Check(Player.m_localPlayer.Picked.Contains(504), "player ward outside item position does not block pickup");
foreignWard.transform.position=new Vector3();
var ownWard=new PrivateArea{Owner=true}; ownWard.Piece.Creator=77; PrivateArea.All.Add(ownWard);
var overlap=Drop(505); Tick();
Check(overlap.Requests==0, "own ward cannot override another overlapping unauthorized player ward");
foreignWard.Permitted=true; overlap.OnRequest=()=>foreignWard.Permitted=false; Tick();
Check(!Player.m_localPlayer.Picked.Contains(505) && overlap.Requests==1, "permission is rechecked after ownership request before inventory mutation");
Console.WriteLine($"PASS: {checks}/{checks} pickup runtime scenarios (real collector with simulated game objects)");

namespace UnityEngine {
 public class Object { public static implicit operator bool(Object o) => o is not null; }
 public class GameObject : Object { public bool activeInHierarchy = true; internal ItemDrop Drop; }
 public class Transform { public Vector3 position; }
 public class Component : Object { public GameObject gameObject = new(); public Transform transform = new(); public virtual T GetComponent<T>() where T: class => null; }
 public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public float sqrMagnitude=>x*x+y*y+z*z; public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z); }
 public struct Quaternion { }
 public static class Time { public static float time; }
}
namespace HarmonyLib { public static class AccessTools { public static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static); public static T MethodDelegate<T>(MethodInfo method) where T:Delegate => (T)method.CreateDelegate(typeof(T)); public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance); } }
public struct ZDOID : IEquatable<ZDOID> { public int Id; public bool IsNone()=>Id==0; public static ZDOID None=>default; public bool Equals(ZDOID other)=>Id==other.Id; public override int GetHashCode()=>Id; }
public class ZDO { public ZDOID m_uid; }
public class ZNetView { public ZDO Data; public bool IsValid()=>true; public ZDO GetZDO()=>Data; }
public class Fish { }
public class ItemDrop : Component {
 private static List<ItemDrop> s_instances=new(); public static List<ItemDrop> All=>s_instances;
 public bool isActiveAndEnabled=true,m_autoPickup=true,Owned=true,Tar,Piece; public int Requests;
 public readonly int Id; readonly Fish fish; readonly ZNetView view; public ItemData m_itemData=new();
 public ItemDrop(int id,bool isFish){Id=id;fish=isFish?new():null;view=new(){Data=new(){m_uid=new(){Id=id}}};gameObject.Drop=this;}
 public override T GetComponent<T>() where T:class => (typeof(T)==typeof(Fish)? (object)fish : typeof(T)==typeof(ZNetView)?view:null) as T;
 public void Load(){} public bool IsPiece()=>Piece; public bool InTar()=>Tar; public Action OnRequest; public void RequestOwn(){Requests++;OnRequest?.Invoke();} public bool CanPickup(bool delay)=>Owned;
 public class ItemData { public SharedData m_shared=new(); public float GetWeight()=>1; public class SharedData { public bool m_questItem; public string m_name="scalehide"; } }
}
public class Inventory { public bool HasRoom=true; public float Weight; public bool CanAddItem(ItemDrop.ItemData i)=>HasRoom; public float GetTotalWeight()=>Weight; }
public class Player : UnityEngine.Object {
 private static bool m_enableAutoPickup=true; public static bool AutoPickup { get=>m_enableAutoPickup;set=>m_enableAutoPickup=value; }
 public static Player m_localPlayer; public Inventory Inventory=new(); public List<int> Picked=new(); public bool AllowPickup=true; public int Messages;
 public bool IsTeleporting()=>false; public Inventory GetInventory()=>Inventory; public bool HaveUniqueKey(string key)=>false; public float GetMaxCarryWeight()=>300;
 public bool Pickup(GameObject go,bool autoequip,bool autoPickupDelay){if(!AllowPickup)return false; Picked.Add(go.Drop.Id);ItemDrop.All.Remove(go.Drop);return true;}
 public void Message(MessageHud.MessageType type,string text){Messages++;}
}
public class MessageHud { public enum MessageType {TopLeft} }
public class Piece : Component { public long Creator; public bool IsPlacedByPlayer()=>Creator!=0; }
public class PrivateArea : Component {
 private static List<PrivateArea> m_allAreas=new(); public static List<PrivateArea> All=>m_allAreas;
 public Piece Piece=new(); public bool Enabled=true,Owner,Permitted; public float Radius=10;
 public override T GetComponent<T>() where T:class => Piece as T;
 private bool IsEnabled()=>Enabled;
 private bool IsInside(Vector3 p,float radius){var d=p-transform.position;return d.x*d.x+d.z*d.z<(Radius+radius)*(Radius+radius);}
 private bool HaveLocalAccess()=>Owner||Permitted;
 public static int Calls; public static bool CheckAccess(Vector3 p,float r,bool flash,bool wardCheck){Calls++;return false;}
}
namespace RunicBuildCamera {
 public class Entry<T>{public T Value; public Entry(T value){Value=value;} }
 internal static class BuildCameraConfig { public static event Action Changed; public static Entry<bool> PickupEnabled=new(true); public static Entry<float> PickupRange=new(10),PickupIntervalSeconds=new(.2f); }
 internal static class Diagnostics { public static void Warn(string s)=>Console.WriteLine(s); public static void Verbose(string s){} }
}
namespace RunicBuildCamera.Integration {
 internal static class BuildCameraRuntime { public static bool IsActive=true; public static Vector3 Position; public static bool TryGetActiveContext(out Player p,out Vector3 pos,out Quaternion rotation){p=Player.m_localPlayer;pos=Position;rotation=default;return IsActive;} }
}

