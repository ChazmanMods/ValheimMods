// Controlled native transport/Unity facsimiles. ProductionChestHandoff.cs is source-linked,
// not reimplemented. This executable is not a replacement for an in-game network test.
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace UnityEngine
{
    public class GameObject { public T AddComponent<T>() where T : new() => new T(); }
    public class Component { public GameObject gameObject = new GameObject(); public ZNetView View; }
    public class MonoBehaviour : Component { }
    public static class Time { public static float realtimeSinceStartup; public static int frameCount; }
    public static class Mathf { public static float Clamp(float n, float min, float max) => Math.Min(max, Math.Max(min, n)); }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public int Id;
    public ZDOID(int id) { Id = id; }
    public bool IsNone() => Id == 0;
    public bool Equals(ZDOID other) => Id == other.Id;
    public override bool Equals(object o) => o is ZDOID other && Equals(other);
    public override int GetHashCode() => Id;
    public override string ToString() => Id.ToString();
}
public class ZDO
{
    public ZDOID m_uid;
    public long Owner;
    public int Items;
    public string Receipt = "";
    public long GetOwner() => Owner;
    public void SetOwner(long id) { Owner = id; Sim.OwnerChanges++; }
    public string GetString(string key, string fallback) => Receipt;
    public void Set(string key, string value) { Receipt = value; }
    public ZDO Copy() => new ZDO { m_uid = m_uid, Owner = Owner, Items = Items, Receipt = Receipt };
}
public class Container : UnityEngine.Component
{
    public bool Busy;
    public bool Synchronizable = true;
    public bool Access = true;
    public bool Loaded = true;
    public int LiveItems;
}
public class ZNetView
{
    public ZDO Data;
    public bool Valid = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Data.Owner == Sim.Local;
    public ZDO GetZDO() => Data;
    public Action<long, ZDOID, long, string> Handler;
    public void Register<A, B, C>(string name, Action<long, A, B, C> handler)
    {
        if (Handler != null) throw new Exception("duplicate RPC registration");
        Handler = (sender, station, principal, nonce) => handler(sender, (A)(object)station, (B)(object)principal, (C)(object)nonce);
    }
    public void InvokeRPC(long to, string name, params object[] args)
    {
        long sender = Sim.Local;
        int id = Data.m_uid.Id;
        Sim.Requests++;
        Sim.PendingRequests.Enqueue(() =>
        {
            Sim.Local = to;
            if (Sim.Chests.TryGetValue((to, id), out Container chest))
                chest.View.Handler?.Invoke(sender, (ZDOID)args[0], (long)args[1], (string)args[2]);
        });
    }
}
public static class ZNet { public static long GetUID() => Sim.Local; }
public class ZDOMan
{
    public static ZDOMan instance = new ZDOMan();
    public void ForceSendZDO(long to, ZDOID id)
    {
        if (!Sim.Chests.TryGetValue((Sim.Local, id.Id), out Container from)) return;
        ZDO snapshot = from.View.Data.Copy();
        Sim.PendingSnapshots.Enqueue(() =>
        {
            if (Sim.Chests.TryGetValue((to, id.Id), out Container chest))
                chest.View.Data = snapshot.Copy();
        });
    }
}
namespace RunicProduction
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    internal static class ProductionConfig
    {
        internal static Setting<bool> Enabled = new Setting<bool>(true);
        internal static Setting<bool> VerboseLogging = new Setting<bool>(false);
        internal static Setting<float> StockSchedulerIntervalSeconds = new Setting<float>(2f);
    }
    internal static class ProductionDiagnostics
    {
        internal static bool RuntimeAvailable = true;
        internal static void Warning(string text) => throw new Exception(text);
        internal static void Info(string text) { }
    }
}
namespace RunicProduction.Integration
{
    internal static class ValheimAccess
    {
        internal static bool IsNativeOwner(UnityEngine.Component c) => c != null && c.View.IsValid() && c.View.IsOwner();
        internal static ZNetView View(UnityEngine.Component c) => c?.View;
        internal static ZDO Zdo(UnityEngine.Component c) => c?.View.Data;
        internal static bool ContainerWritable(Container c) => c.Loaded && !c.Busy;
        internal static bool TrySynchronizeLocallyOwnedContainer(Container c, out object inventory)
        {
            inventory = null;
            if (!IsNativeOwner(c) || !ContainerWritable(c) || !c.Synchronizable) return false;
            c.LiveItems = c.View.Data.Items;
            return true;
        }
    }
    internal static class ProductionRuntime
    {
        internal static bool AuthorizesChest(UnityEngine.Component s, Container c, long p) =>
            s != null && c != null && c.Loaded && c.Access && p == 777 && Sim.Authorized;
        internal static UnityEngine.Component FindStation(ZDOID id) =>
            Sim.Stations.TryGetValue((Sim.Local, id.Id), out var s) ? s : null;
    }
    internal static class ProductionEndpointIdentity
    {
        internal static bool IsCanonicalToken(string s) => s != null && s.Length == 32 &&
            Guid.TryParseExact(s, "N", out Guid g) && g != Guid.Empty && g.ToString("N") == s;
    }
}
internal static class Sim
{
    private static long _local;
    internal static long Local
    {
        get => _local;
        set
        {
            if (value == _local) return;
            Contexts[_local] = Capture();
            _local = value;
            Restore(Contexts.TryGetValue(value, out Context context) ? context : new Context());
        }
    }
    private sealed class Context
    {
        internal readonly List<DictionaryEntry> States = new List<DictionaryEntry>();
        internal readonly List<object> Outstanding = new List<object>();
        internal bool Active = true;
        internal int Frame = -1, Requests;
    }
    private static readonly Dictionary<long, Context> Contexts = new Dictionary<long, Context>();
    private static FieldInfo Field(string name) => typeof(RunicProduction.Integration.ProductionChestHandoff)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
    private static Context Capture()
    {
        var c = new Context { Active = (bool)Field("_active").GetValue(null),
            Frame = (int)Field("_requestFrame").GetValue(null), Requests = (int)Field("_requestsThisFrame").GetValue(null) };
        foreach (DictionaryEntry item in (IDictionary)Field("States").GetValue(null)) c.States.Add(item);
        foreach (object item in (IEnumerable)Field("Outstanding").GetValue(null)) c.Outstanding.Add(item);
        return c;
    }
    private static void Restore(Context c)
    {
        var states = (IDictionary)Field("States").GetValue(null);
        states.Clear(); foreach (DictionaryEntry e in c.States) states.Add(e.Key, e.Value);
        object outstanding = Field("Outstanding").GetValue(null);
        outstanding.GetType().GetMethod("Clear").Invoke(outstanding, null);
        foreach (object e in c.Outstanding) outstanding.GetType().GetMethod("Add").Invoke(outstanding, new[] { e });
        Field("_active").SetValue(null, c.Active);
        Field("_requestFrame").SetValue(null, c.Frame);
        Field("_requestsThisFrame").SetValue(null, c.Requests);
    }
    internal static bool Authorized = true;
    internal static int Requests, OwnerChanges;
    internal static readonly Dictionary<(long, int), Container> Chests = new Dictionary<(long, int), Container>();
    internal static readonly Dictionary<(long, int), UnityEngine.Component> Stations = new Dictionary<(long, int), UnityEngine.Component>();
    internal static readonly Queue<Action> PendingRequests = new Queue<Action>();
    internal static readonly Queue<Action> PendingSnapshots = new Queue<Action>();
    internal static void Reset()
    {
        Chests.Clear(); Stations.Clear(); PendingRequests.Clear(); PendingSnapshots.Clear();
        Contexts.Clear(); _local = 1; Authorized = true; Requests = OwnerChanges = 0;
        UnityEngine.Time.realtimeSinceStartup = 10; UnityEngine.Time.frameCount++;
        RunicProduction.ProductionConfig.Enabled.Value = true;
        RunicProduction.Integration.ProductionChestHandoff.Initialize();
    }
    internal static Container Chest(long peer, int id, long owner, int count = 40)
    {
        Local = peer;
        var chest = new Container { View = new ZNetView { Data = new ZDO { m_uid = new ZDOID(id), Owner = owner, Items = count } } };
        Chests.Add((peer, id), chest);
        RunicProduction.Integration.ProductionChestHandoff.Register(chest);
        return chest;
    }
    internal static UnityEngine.Component Station(long peer, int id, long owner)
    {
        var c = new UnityEngine.Component { View = new ZNetView { Data = new ZDO { m_uid = new ZDOID(id), Owner = owner } } };
        Stations[(peer, id)] = c;
        return c;
    }
    internal static void RequestsNow() { while (PendingRequests.Count > 0) PendingRequests.Dequeue()(); }
    internal static void SnapshotsNow() { while (PendingSnapshots.Count > 0) PendingSnapshots.Dequeue()(); }
    internal static void Advance(float seconds = 4) { UnityEngine.Time.realtimeSinceStartup += seconds; UnityEngine.Time.frameCount++; }
}
