// Small managed substitutes for Unity-bound game objects. The tests execute the production
// Storage transfer/snapshot code; these substitutes model the inspected Valheim 1.0.7 codec.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public struct Vector2i { public static Vector2i zero => new Vector2i(0,0); public int x, y; public Vector2i(int x, int y) { this.x=x; this.y=y; } }
public static class Version { public enum Item { Current = 109 } }
public sealed class ZPackage
{
    private readonly MemoryStream stream;
    private readonly BinaryReader reader;
    private readonly BinaryWriter writer;
    public ZPackage(string data) : this(Convert.FromBase64String(data)) {}
    public int Size() => (int)stream.Length;
    public ZPackage() : this(Array.Empty<byte>()) { }
    public ZPackage(byte[] bytes) { stream=new MemoryStream(); stream.Write(bytes); stream.Position=0; reader=new BinaryReader(stream); writer=new BinaryWriter(stream); }
    public void Write(int x)=>writer.Write(x);
    public void Write(ushort x)=>writer.Write(x);
    public void Write(byte x)=>writer.Write(x);
    public void Write(long x)=>writer.Write(x);
    public void Write(string x)=>writer.Write(x);
    public int ReadInt()=>reader.ReadInt32();
    public ushort ReadUShort()=>reader.ReadUInt16();
    public byte ReadByte()=>reader.ReadByte();
    public long ReadLong()=>reader.ReadInt64();
    public string ReadString()=>reader.ReadString();
    public int GetPos()=>(int)stream.Position;
    public byte[] GetArray()=>stream.ToArray();
    public string GetBase64()=>Convert.ToBase64String(GetArray());
}
public sealed class ItemDrop
{
    public ItemData m_itemData = new ItemData();
    public sealed class SharedData { public int m_maxStackSize=50; public bool m_questItem; }
    public sealed class ItemData
    {
        public FakePrefab m_dropPrefab;
        public int Prefab=1, m_stack=1, m_quality=1, m_variant, m_worldLevel;
        public float m_durability;
        public long m_crafterID;
        public string m_crafterName="";
        public bool m_pickedUp, m_equipped, m_cheated;
        public Vector2i m_gridPos;
        public SharedData m_shared=new SharedData();
        public Dictionary<string,string> m_customData=new Dictionary<string,string>();
        public ItemData Clone() { var result=(ItemData)MemberwiseClone(); result.m_customData=new Dictionary<string,string>(m_customData); return result; }
        public void Save(ZPackage p)
        {
            p.Write((int)(m_durability*100f)); p.Write((byte)m_gridPos.x); p.Write((byte)m_gridPos.y); p.Write((byte)m_worldLevel);
            int flags=(m_pickedUp?1:0)|(m_equipped?2:0)|(m_quality!=1?4:0)|(m_stack!=1?8:0)|(m_variant!=0?16:0)|(m_crafterID!=0?32:0)|64|(m_customData.Count!=0?128:0);
            p.Write((byte)flags);
            if((flags&4)!=0)p.Write((ushort)m_quality);
            if((flags&8)!=0)p.Write((ushort)m_stack);
            if((flags&16)!=0)p.Write(m_variant);
            if((flags&32)!=0){p.Write(m_crafterID);p.Write(m_crafterName);}
            p.Write(Prefab);
            // These fixtures use fewer than 128 custom entries (native WriteNumItems compact form).
            if((flags&128)!=0)p.Write((byte)m_customData.Count);
            foreach(var pair in m_customData){p.Write(pair.Key);p.Write(pair.Value);}
            p.Write((byte)(m_cheated?1:0));
        }
        public static (int prefabHash, ItemData itemData) Load(ZPackage p, Version.Item version)
        {
            var i=new ItemData(); i.m_durability=p.ReadInt()*0.01f; i.m_gridPos=new Vector2i(p.ReadByte(),p.ReadByte()); i.m_worldLevel=p.ReadByte(); int f=p.ReadByte();
            i.m_pickedUp=(f&1)!=0; i.m_equipped=(f&2)!=0; i.m_quality=(f&4)!=0?p.ReadUShort():1; i.m_stack=(f&8)!=0?p.ReadUShort():1; i.m_variant=(f&16)!=0?p.ReadInt():0;
            if((f&32)!=0){i.m_crafterID=p.ReadLong();i.m_crafterName=p.ReadString();}
            i.Prefab=(f&64)!=0?p.ReadInt():0;
            int count=(f&128)!=0?p.ReadByte():0; for(int n=0;n<count;n++)i.m_customData.Add(p.ReadString(),p.ReadString());
            i.m_cheated=(p.ReadByte()&1)!=0; return(i.Prefab,i);
        }
    }
}
public sealed class Inventory
{
    private readonly List<ItemDrop.ItemData> items=new List<ItemDrop.ItemData>();
    private readonly string name; private readonly int width,height;
    public Action m_onChanged;
    public Inventory(string name, object icon, int width,int height){this.name=name;this.width=width;this.height=height;}
    public string GetName()=>name; public int GetWidth()=>width; public int GetHeight()=>height;
    public List<ItemDrop.ItemData> GetAllItems()=>items;
    public ItemDrop.ItemData GetItemAt(int x,int y)=>items.FirstOrDefault(i=>i.m_gridPos.x==x&&i.m_gridPos.y==y);
    public void Save(ZPackage p){p.Write(109);p.Write((ushort)items.Count);foreach(var i in items)i.Save(p);}
    public void Load(ZPackage p){items.Clear();var v=(Version.Item)p.ReadInt();int n=p.ReadUShort();for(int i=0;i<n;i++)items.Add(ItemDrop.ItemData.Load(p,v).itemData);Changed();}
    private void Changed(bool success=false,bool cheatedStateChanged=false)=>m_onChanged?.Invoke();
    private bool AddItem(ItemDrop.ItemData item,int amount,int x,int y,bool skipValidPositionCheck=false)
    {
        if(x<0||x>=width||y<0||y>=height)return false;
        var at=GetItemAt(x,y);
        if(at==null){at=item.Clone();at.m_stack=amount;at.m_gridPos=new Vector2i(x,y);items.Add(at);}
        else {if(at.Prefab!=item.Prefab||at.m_stack+amount>at.m_shared.m_maxStackSize)return false;at.m_stack+=amount;}
        item.m_stack-=amount;Changed();return true;
    }
    // Match Valheim: removing an entire stack detaches it without zeroing its count.
    public bool RemoveItem(ItemDrop.ItemData item,int quantity){if(!items.Contains(item))return false;quantity=Math.Min(quantity,item.m_stack);if(quantity==item.m_stack)items.Remove(item);else item.m_stack-=quantity;Changed();return true;}
}
public sealed class Player
{
    public static Player m_localPlayer; public bool Owner=true; public Inventory Inventory;
    public Inventory GetInventory()=>Inventory; public bool IsOwner()=>Owner; public long GetPlayerID()=>1;
}
public class Container
{
    public ZNetView View = new ZNetView();
    public T GetComponent<T>() where T:class => View as T;
    public bool isActiveAndEnabled=true, Owner=true, AccessAllowed=true, m_checkGuardStone;
    public Inventory Inventory; public Transform transform=new Transform();
    public bool IsOwner()=>Owner; public Inventory GetInventory()=>Inventory;
    private bool CheckAccess(long id)=>AccessAllowed;
}
public sealed class Transform {public object position;}
public static class PrivateArea {public static bool Allowed=true;public static bool CheckAccess(object p,float r,bool flash,bool wardCheck)=>Allowed;}
namespace HarmonyLib {public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] args=null)=>args==null?t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance):t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);}}
namespace RunicStorage.Runtime
{
    internal static class ValheimContainerIdentity {internal static string ResourceId(ItemDrop.ItemData item)=>item.Prefab.ToString();}
    internal static class StorageContainerAuthority { internal static Func<bool> CaptureAuthority(Container c) => () => c == null || c.Owner;internal static bool TryClaimWritableInventory(Container c,bool allowCurrentUse)=>c.Owner;}
}
