using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => o != null; }
    public class GameObject : Object { public ItemDrop Drop; }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x=a; y=b; z=c; }
        public static Vector3 up => new Vector3(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public static class Time { public static float time; }
    public static class Mathf { public static int Min(int a, int b) => System.Math.Min(a,b); }
}
public class ZNetView : UnityEngine.Object { public bool Valid = true; public bool IsValid() => Valid; }
public class ItemDrop : UnityEngine.Object
{
    private static List<ItemDrop> s_instances = new List<ItemDrop>();
    public static List<ItemDrop> All => s_instances;
    public bool m_autoPickup = true, Piece, Tar, Owned = true, Requested;
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public UnityEngine.GameObject gameObject;
    public ItemData m_itemData = new ItemData();
    public ZNetView View = new ZNetView();
    public ItemDrop(float distance) { transform.position = new UnityEngine.Vector3(distance,1,0); gameObject = new UnityEngine.GameObject { Drop = this }; All.Add(this); }
    public bool IsPiece() => Piece;
    public bool InTar() => Tar;
    public T GetComponent<T>() where T:class => View as T;
    public void Load() { }
    public bool CanPickup() => Owned;
    public void RequestOwn() => Requested = true;
    public class ItemData { public Shared m_shared = new Shared(); public float Weight = 1; public float GetWeight() => Weight; }
    public class Shared { public string m_name = "loot"; }
}
public class Inventory
{
    public int Slots = 1000, Count;
    public float Weight;
    public bool CanAddItem(ItemDrop.ItemData item) => Count < Slots;
    public float GetTotalWeight() => Weight;
}
public class Player : UnityEngine.Object
{
    public static Player m_localPlayer;
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public Inventory Inventory = new Inventory();
    public bool Dead, Teleporting, Cutscene;
    public float MaxWeight = 300;
    public HashSet<string> Unique = new HashSet<string>();
    public bool IsDead() => Dead;
    public bool IsTeleporting() => Teleporting;
    public bool InCutscene() => Cutscene;
    public Inventory GetInventory() => Inventory;
    public bool HaveUniqueKey(string s) => Unique.Contains(s);
    public float GetMaxCarryWeight() => MaxWeight;
    public bool Pickup(UnityEngine.GameObject go)
    {
        Inventory.Count++; Inventory.Weight += go.Drop.m_itemData.Weight;
        int i = ItemDrop.All.IndexOf(go.Drop);
        ItemDrop.All[i] = ItemDrop.All[ItemDrop.All.Count-1];
        ItemDrop.All.RemoveAt(ItemDrop.All.Count-1);
        return true;
    }
}
