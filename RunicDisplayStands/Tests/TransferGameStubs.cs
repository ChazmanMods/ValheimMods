// Deterministic doubles for fault-injecting the production transfer helpers.
using System.Text;

public static class Version { public const int c_ItemDataVersion = 109; }
public struct Vector2i
{
    public int x, y;
    public Vector2i(int x, int y) { this.x = x; this.y = y; }
}
public sealed class ItemDrop
{
    public sealed class Prefab { public string name = "Sword"; }
    public sealed class ItemData
    {
        public enum ItemType { Material, OneHandedWeapon, Bow, Shield, Helmet, Chest, Legs, Shoulder, Utility, TwoHandedWeapon, TwoHandedWeaponLeft, Tool, Torch, Misc }
        public sealed class SharedData { public ItemType m_itemType = ItemType.Misc; }
        public SharedData m_shared = new();
        public Prefab m_dropPrefab = new();
        public int m_stack = 1, m_quality = 3, m_variant = 2;
        public float m_durability = 37;
        public bool m_equipped;
        public Vector2i m_gridPos;
        public Dictionary<string, string> m_customData = new();
        public bool FailSave;
        public ItemData Clone()
        {
            var clone = (ItemData)MemberwiseClone();
            clone.m_customData = new Dictionary<string, string>(m_customData);
            return clone;
        }
        public void Save(ZPackage package)
        {
            if (FailSave) throw new InvalidOperationException("Injected serializer failure");
            package.Write(m_stack); package.Write(m_quality); package.Write(m_variant);
            package.Write(m_durability.ToString(System.Globalization.CultureInfo.InvariantCulture));
            package.Write(m_equipped.ToString()); package.Write(m_gridPos.x); package.Write(m_gridPos.y);
            foreach (var pair in m_customData.OrderBy(pair => pair.Key)) { package.Write(pair.Key); package.Write(pair.Value); }
        }
    }
}
public sealed class Inventory
{
    private readonly List<ItemDrop.ItemData> _items = new();
    public int Width = 8, Height = 5;
    public bool ProtectedRow;
    public List<ItemDrop.ItemData> GetAllItems() => _items;
    public int GetWidth() => Width;
    public int GetHeight() => Height;
    public List<ItemDrop.ItemData> GetEquippedItems() => _items.Where(i => i.m_equipped).ToList();
    public ItemDrop.ItemData? GetItemAt(int x, int y) => _items.FirstOrDefault(i => i.m_gridPos.x == x && i.m_gridPos.y == y);
    public bool RemoveItem(ItemDrop.ItemData item)
    {
        if (item.m_equipped) throw new InvalidOperationException("Removal before unequip");
        return _items.Remove(item);
    }
    public bool AddItem(ItemDrop.ItemData item, Vector2i pos)
    {
        if (pos.x < 0 || pos.x >= Width || pos.y < 0 || pos.y >= Height || GetItemAt(pos.x, pos.y) != null) return false;
        item.m_gridPos = pos; _items.Add(item); return true;
    }
    public bool AddItem(ItemDrop.ItemData item)
    {
        for (int y = 0; y < Height - (ProtectedRow ? 1 : 0); y++)
            for (int x = 0; x < Width; x++)
                if (GetItemAt(x, y) == null) return AddItem(item, new Vector2i(x, y));
        return false;
    }
}
public sealed class ZPackage
{
    private readonly MemoryStream _stream = new();
    public void Write(byte value) => _stream.WriteByte(value);
    public void Write(int value) { var bytes = BitConverter.GetBytes(value); _stream.Write(bytes); }
    public void Write(string value) { var bytes = Encoding.UTF8.GetBytes(value); Write(bytes.Length); _stream.Write(bytes); }
    public byte[] GetArray() => _stream.ToArray();
    public string GetBase64() => Convert.ToBase64String(GetArray());
}
public sealed class ZNetView
{
    public readonly ZDO Data = new();
    public bool Valid = true, Owner = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Owner;
    public ZDO GetZDO() => Data;
}
public sealed class ZDO
{
    private readonly Dictionary<int, int> _ints = new();
    private readonly Dictionary<int, string> _strings = new();
    private readonly Dictionary<int, byte[]> _bytes = new();
    public int Writes, ThrowOnWrite = -1;
    public bool IgnoreWrites;
    private void BeforeWrite()
    {
        if (++Writes == ThrowOnWrite) throw new InvalidOperationException("Injected write failure");
    }
    public int GetInt(int key, int fallback) => _ints.GetValueOrDefault(key, fallback);
    public string GetString(int key, string fallback) => _strings.GetValueOrDefault(key, fallback);
    public byte[] GetByteArray(int key, byte[]? fallback) => _bytes.TryGetValue(key, out var value) ? value : fallback!;
    public void Set(int key, int value) { BeforeWrite(); if (!IgnoreWrites) _ints[key] = value; }
    public void Set(int key, string value) { BeforeWrite(); if (!IgnoreWrites) _strings[key] = value; }
    public void Set(int key, byte[] value) { BeforeWrite(); if (!IgnoreWrites) _bytes[key] = value; }
    public void RemoveString(int key) { BeforeWrite(); if (!IgnoreWrites) _strings.Remove(key); }
}
public static class StableHashStub
{
    public static int GetStableHashCode(this string text)
    {
        int result = 17;
        foreach (char value in text) result = unchecked(result * 31 + value);
        return result;
    }
}
namespace RunicDisplayStands
{
    internal static class ItemDataSerializer
    {
        internal static byte[] Serialize(ItemDrop.ItemData item)
        {
            var package = new ZPackage(); item.Save(package); return package.GetArray();
        }
    }
}
