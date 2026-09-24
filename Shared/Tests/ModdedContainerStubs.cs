using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

public sealed class FakePrefab
{
    public ItemDrop Drop = new ItemDrop();
    public T GetComponent<T>() where T : class => Drop as T;
}
public sealed class ObjectDB
{
    public static ObjectDB instance = new ObjectDB();
    public readonly Dictionary<string, FakePrefab> Prefabs = new Dictionary<string, FakePrefab>();
    public FakePrefab GetItemPrefab(string id) => Prefabs.TryGetValue(id, out var prefab) ? prefab : null;
}
public static class ZNet { public static long GetUID() => 1; }
public sealed class ZDO
{
    public int Prefab = "piece_drawer".GetStableHashCode();
    public string Payload = "";
    public long Owner = 1;
    public int GetPrefab() => Prefab;
    public long GetOwner() => Owner;
    public string GetString(string key, string fallback) => Payload;
}
public sealed class ZNetView
{
    public bool Valid = true;
    public ZDO Data = new ZDO();
    public bool IsValid() => Valid;
    public bool IsOwner() => Data.Owner == ZNet.GetUID();
    public ZDO GetZDO() => Data;
}
public static class StableHashFixture
{
    public static int GetStableHashCode(this string value)
    {
        unchecked
        {
            int a = 5381, b = a;
            for (int i = 0; i < value.Length; i += 2)
            {
                a = ((a << 5) + a) ^ value[i];
                if (i == value.Length - 1) break;
                b = ((b << 5) + b) ^ value[i + 1];
            }
            return a + b * 1566083941;
        }
    }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; }
    public sealed class ConfigFile
    {
        public string PullIds;
        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry)
        {
            entry = PullIds == null ? null : new ConfigEntry<T> { Value = (T)(object)PullIds };
            return entry != null;
        }
    }
}
namespace BepInEx.Bootstrap
{
    public sealed class FakePlugin { public BepInEx.Configuration.ConfigFile Config = new BepInEx.Configuration.ConfigFile(); }
    public sealed class PluginInfo { public FakePlugin Instance = new FakePlugin(); }
    public static class Chainloader { public static Dictionary<string, PluginInfo> PluginInfos = new Dictionary<string, PluginInfo>(); }
}

// Exact external type/assembly identity without loading Unity or shipping the third-party DLL.
public static class DrawerFixture
{
    private static readonly Type DrawerType = BuildType();
    private static Type BuildType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("itemdrawers"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("drawers").DefineType("DrawerContainer", TypeAttributes.Public, typeof(Container));
        type.DefineField("_lastRevision", typeof(uint?), FieldAttributes.Public);
        type.DefineField("_quantity", typeof(int), FieldAttributes.Public);
        var load = type.DefineMethod("Load", MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator();
        load.Emit(OpCodes.Ldarg_0);
        load.Emit(OpCodes.Call, typeof(DrawerFixture).GetMethod(nameof(Load)));
        load.Emit(OpCodes.Ret);
        return type.CreateType();
    }
    public static Container Create(int count, int capacity = 9999)
    {
        var prefab = new FakePrefab();
        prefab.Drop.m_itemData.m_dropPrefab = prefab;
        ObjectDB.instance.Prefabs["Wood"] = prefab;
        var drawer = (Container)Activator.CreateInstance(DrawerType);
        drawer.Inventory = new Inventory("drawer", null, 1, 1);
        Save(drawer, count);
        Load(drawer);
        drawer.Inventory.GetAllItems()[0].m_shared.m_maxStackSize = capacity;
        return drawer;
    }
    public static void Save(Container drawer, int count)
    {
        var package = new ZPackage(); package.Write(0); package.Write("Wood"); package.Write(count);
        drawer.View.Data.Payload = package.GetBase64();
        drawer.GetType().GetField("_quantity").SetValue(drawer, count);
    }
    public static void Load(Container drawer)
    {
        var package = new ZPackage(drawer.View.Data.Payload); package.ReadInt(); string id = package.ReadString(); int count = package.ReadInt();
        var item = ObjectDB.instance.GetItemPrefab(id).Drop.m_itemData.Clone();
        item.m_stack = count;
        item.m_shared = new ItemDrop.SharedData { m_maxStackSize = 9999 };
        drawer.Inventory.GetAllItems().Clear(); drawer.Inventory.GetAllItems().Add(item);
        drawer.GetType().GetField("_quantity").SetValue(drawer, count);
        drawer.Inventory.m_onChanged = () => Save(drawer, drawer.Inventory.GetAllItems().Count == 0 ? 0 : drawer.Inventory.GetAllItems()[0].m_stack);
    }
}
