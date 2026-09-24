// Managed environment for exercising the actual source-query runtime, not a Unity simulation.
using System;
using System.Collections.Generic;
using RunicCrafting.Domain;

namespace UnityEngine
{
    public static class Time { public static int frameCount; public static float realtimeSinceStartup; }
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public bool Equals(Vector3 b) => x.Equals(b.x) && y.Equals(b.y) && z.Equals(b.z);
        public override bool Equals(object b) => b is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x,y,z);
    }
}
public sealed class Transform { public UnityEngine.Vector3 position; }
public sealed class ItemDrop
{
    public sealed class ItemData { public string Resource; public int m_worldLevel, m_stack; }
}
public sealed class Inventory
{
    public readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
    public List<ItemDrop.ItemData> GetAllItems() => Items;
}
public static class Game { public static int m_worldLevel; }
public sealed class ObjectDB { public static ObjectDB instance = new ObjectDB(); }
public sealed class ZNet { public static ZNet instance = new ZNet(); public static long GetUID() => 1; }
public static class ZDOVars { public static int s_inUse; }
public sealed class ZDO { public ushort OwnerRevision; public bool Busy; public bool IsValid() => true; public int GetInt(int key,int fallback) => Busy?1:0; public long GetOwner() => 1; }
public sealed class ZNetView
{
    public readonly ZDO Data = new ZDO(); public bool Valid = true, Owner = true;
    public bool IsValid() => Valid;
    public ZDO GetZDO() => Data;
    public bool IsOwner() => Owner;
    public void ClaimOwnership() { Owner = true; }
}
public sealed class Wagon { public bool Busy; public bool InUse() => Busy; }
public sealed class Container
{
    public enum PrivacySetting { Public, Private }
    public string Id;
    public bool Drawer;
    public string PrefabId = "piece_drawer";
    public bool isActiveAndEnabled = true, Busy, Access = true, m_checkGuardStone;
    public Wagon m_wagon;
    public PrivacySetting m_privacy;
    public Transform transform = new Transform();
    public ZNetView View = new ZNetView();
    public Inventory Inventory = new Inventory();
    public bool IsInUse() => Busy;
    public Inventory GetInventory() => Inventory;
}
public sealed class CraftingStation { public bool Access = true; public ZDO Data = new ZDO(); }
public sealed class Player
{
    public CraftingStation Station;
    public CraftingStation GetCurrentCraftingStation() => Station;
    public static Player m_localPlayer;
    public bool Owner = true;
    public Inventory Inventory = new Inventory();
    public Inventory GetInventory() => Inventory;
    public long GetPlayerID() => 1;
}
public static class PrivateArea
{
    public static bool Allowed = true;
    public static bool CheckAccess(UnityEngine.Vector3 p,float r,bool flash,bool wardCheck) => Allowed;
}
namespace RunicCrafting
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T v) { Value=v; } }
    internal static class Configuration
    {
        internal static Setting<bool> Enabled = new Setting<bool>(true);
        internal static Setting<string> PullPrefabIds = new Setting<string>("piece_drawer");
        internal static float SafeRangeCap = 20;
        internal static int SafeMaximumCandidates = 64, SafeMaximumReturned = 32;
        internal static Setting<bool> ExcludePersonalContainers = new Setting<bool>(true), RequireWardAccess = new Setting<bool>(true);
    }
}
namespace Runic.Compatibility
{
    internal static class ModdedContainerCompatibility
    {
        // Simulate the former cross-mod resolver to catch accidental reintroduction.
        internal static string StoragePullIds;
        internal static string PullIds(string fallback) => StoragePullIds ?? fallback;
        internal static bool IsDrawer(Container container) => container.Drawer;
        internal static bool Listed(Container container, string ids) =>
            Array.Exists((ids ?? "").Split(new[] { ',', ';' }), id => id.Trim() == container.PrefabId);
    }
}
namespace RunicCrafting.Integration
{
    internal static class CachePerformance
    {
        internal static long UiHits, UiMisses, RefreshHits;
        internal static long StartQuery() => 0;
        internal static void EndQuery(long stamp) { }
    }
    internal static class CraftingDiagnostics { internal static void TraceGate(string a,string b,string c=null) {} }
    internal sealed class WorkshopAccessRuntime
    {
        internal WorkshopAccessDecision Evaluate(CraftingStation s,Player p,WorkshopAction a) => new WorkshopAccessDecision(s.Access,a,s.Access?"ok":"denied");
    }
    internal static class ContainerSpatialIndex
    {
        internal static int Queries;
        internal static List<Container> Containers = new List<Container>();
        internal static IReadOnlyList<Container> Query(UnityEngine.Vector3 origin,float radius,int maximum)
        {
            Queries++;
            return Containers.GetRange(0,Math.Min(maximum,Containers.Count));
        }
    }
    internal static class ValheimReflection
    {
        internal static int Reads, AccessChecks, WritableRefreshes;
        internal static Action DuringRead;
        internal static bool CanMutateLocalPlayer(Player p) => p != null && ReferenceEquals(p,Player.m_localPlayer) && p.Owner;
        internal static ZNetView GetView(Container c) => c.View;
        internal static ZDO StationZdo(CraftingStation s) => s?.Data;
        internal static void ObservePreviewWards() { }
        internal static bool ContainerAllows(Container c,long id) { AccessChecks++; return c.Access; }
        internal static string ContainerEndpointId(Container c) => c.Id;
        internal static string ResourceId(ItemDrop.ItemData item) => item.Resource;
        internal static bool TryReadContainerInventory(Container c,out PreviewMaterialCounts preview)
        { Reads++; preview=new PreviewMaterialCounts(c.Inventory,Game.m_worldLevel); DuringRead?.Invoke(); return true; }
        internal static bool RefreshOwnedContainer(Container c,ZDO z) { WritableRefreshes++; return true; }
    }
    // Writes are deliberately recognizable; the real transaction engine has separate existing tests.
    internal sealed class ValheimMaterialSource : IMutableMaterialSource
    {
        private readonly Inventory _inventory; private readonly MaterialSourceKind _kind;
        private readonly float _distance; private readonly Func<bool> _eligible;
        internal ValheimMaterialSource(string id,Inventory inventory,MaterialSourceKind kind,float distance,
            IEnumerable<string> resources,Func<bool> eligible,Func<bool> stillWritable=null)
        { SourceId=id; _inventory=inventory; _kind=kind; _distance=distance; _eligible=eligible; }
        public string SourceId { get; }
        public MaterialSourceSnapshot Snapshot() => new PreviewMaterialCounts(_eligible()?_inventory:new Inventory(),Game.m_worldLevel).ToSnapshot(SourceId,_kind,_distance);
        public bool TryTake(string id,int count,out IMaterialRestoreToken token) { token=null; return false; }
    }
}

namespace RunicAutomation
{
    // This UI harness substitutes the authority transport; the source-linked transport harness tests its protocol.
    public static class ContainerAuthority
    {
        public static int PlayerContext(Player player)=>1;
        public static bool TryAcquire(Container chest,string policy,int context,long principal)
        { return chest.View.IsOwner(); }
    }
}
