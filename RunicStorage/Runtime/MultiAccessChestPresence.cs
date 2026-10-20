using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicStorage.Runtime;

// Viewer presence controls lids only. It never locks inventory or transfers ownership.
internal static class MultiAccessChestPresence
{
    internal const string ViewerKey = "runic.storage.viewers.v2";
    private const string Kind = "storage.viewer.v2";
    private static readonly Dictionary<ZDOID, Dictionary<string, float>> Viewers = new();
    private static ZDOID lastChest;
    private static float nextHeartbeat;
    private sealed class PresenceRequest
    {
        internal string Id, World;
        internal ZDOID Chest;
        internal bool Open, Sent;
        internal float Retry;
    }
    private static PresenceRequest pendingHeartbeat, pendingClose;
    internal static void Register() => SharedOperationHost.RegisterQuery(Kind, Authorize, Plan);
    private static Tuple<ZDO, bool> Decode(byte[] bytes)
    {
        var p = new ZPackage(bytes); var zdo = ZDOMan.instance.GetZDO(p.ReadZDOID()); bool open = p.ReadBool(); OperationPacket.End(p);
        if (zdo == null) throw new OperationRejectedException("chest-unavailable");
        return Tuple.Create(zdo, open);
    }
    private static string Authorize(string actor, byte[] bytes)
    {
        try { var request = Decode(bytes); return NetworkInventoryAccess.CanAccess(actor, request.Item1, 5f, true, false, out string reason) ? null : reason; }
        catch { return "invalid-viewer-request"; }
    }
    private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
    {
        var request = Decode(bytes); var id = request.Item1.m_uid;
        if (!Viewers.TryGetValue(id, out var actors)) Viewers[id] = actors = new Dictionary<string, float>();
        if (request.Item2) actors[actor] = Time.realtimeSinceStartup + 6; else actors.Remove(actor);
        Update(id, actors);
        return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
    }
    private static void Update(ZDOID id, Dictionary<string, float> actors)
    {
        var zdo = ZDOMan.instance.GetZDO(id); if (zdo == null) { Viewers.Remove(id); return; }
        foreach (string actor in actors.Where(x => x.Value < Time.realtimeSinceStartup || !NetworkInventoryAccess.TryPosition(x.Key, out _, out _)).Select(x => x.Key).ToArray()) actors.Remove(actor);
        int open = actors.Count > 0 ? 1 : 0;
        if (zdo.GetInt(ViewerKey) != open) { zdo.Set(ViewerKey, open); ZDOMan.instance.ForceSendZDO(id); }
        if (open == 0) Viewers.Remove(id);
    }
    internal static void Tick()
    {
        if (!InventoryOperationNetwork.Ready) return;
        if (ZNet.instance.IsServer()) foreach (var pair in Viewers.ToArray()) Update(pair.Key, pair.Value);
        var chest = MultiAccessChestView.Opened;
        ZDOID current = MultiAccessChestView.IsRunicView(chest) && MultiAccessChestView.CanView(chest) && Player.m_localPlayer?.IsDead() == false ?
            RunicAutomation.ContainerAuthority.View(chest).GetZDO().m_uid : ZDOID.None;
        if (lastChest != current)
        {
            Cancel(ref pendingHeartbeat);
            Cancel(ref pendingClose);
            if (!lastChest.IsNone()) pendingClose = Create(lastChest, false);
            lastChest = current; nextHeartbeat = 0;
        }
        if (!current.IsNone() && pendingHeartbeat == null && Time.realtimeSinceStartup >= nextHeartbeat)
        { nextHeartbeat = Time.realtimeSinceStartup + 2; pendingHeartbeat = Create(current, true); }
        Send(pendingClose);
        Send(pendingHeartbeat);
    }
    private static PresenceRequest Create(ZDOID id, bool open) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Chest = id, Open = open,
        World = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture)
    };
    private static void Send(PresenceRequest request)
    {
        if (request == null || request.Sent || Time.realtimeSinceStartup < request.Retry) return;
        request.Retry = Time.realtimeSinceStartup + .25f;
        var p = new ZPackage(); p.Write(request.Chest); p.Write(request.Open);
        request.Sent = InventoryOperationNetwork.Send(request.World, request.Id, Kind, p.GetArray(), (_, __, ___) =>
        {
            if (ReferenceEquals(pendingHeartbeat, request)) pendingHeartbeat = null;
            if (ReferenceEquals(pendingClose, request)) pendingClose = null;
        });
    }
    private static void Cancel(ref PresenceRequest request)
    {
        if (request != null) InventoryOperationNetwork.CancelQuery(request.Id);
        request = null;
    }
    internal static void Clear()
    {
        Cancel(ref pendingHeartbeat); Cancel(ref pendingClose);
        Viewers.Clear(); lastChest = ZDOID.None; nextHeartbeat = 0;
    }
}
[HarmonyPatch(typeof(ZNet), "Update")]
internal static class MultiAccessPresenceTick
{
    private static void Postfix()
    { MultiAccessChestView.Tick(); MultiAccessChestPresence.Tick(); }
}
[HarmonyPatch(typeof(Container), "SetInUse")]
internal static class MultiAccessLocalInUse
{
    private static bool Prefix(Container __instance, bool inUse, ref bool ___m_inUse, bool __runOriginal)
    {
        if (!__runOriginal || !MultiAccessChestView.IsRunicView(__instance)) return true;
        // Native RPC_RequestOpen checks m_inUse, so a Runic viewer must not acquire
        // that exclusive lock. Our separate presence and active view control the lid.
        return false;
    }
}
[HarmonyPatch(typeof(Container), "UpdateUseVisual")]
internal static class MultiAccessLidVisual
{
    private static void Postfix(Container __instance, bool __runOriginal)
    {
        if (!__runOriginal || !(PluginConfig.Enabled?.Value ?? false)) return;
        var zdo = RunicAutomation.ContainerAuthority.View(__instance)?.GetZDO();
        if (zdo == null || zdo.GetInt(MultiAccessChestPresence.ViewerKey) == 0 &&
            !(MultiAccessChestView.IsRunicView(__instance) && MultiAccessChestView.Opened == __instance)) return;
        // Runic viewers affect the lid only, never vanilla's exclusive-use lock.
        if (__instance.m_open != null) __instance.m_open.SetActive(true);
        if (__instance.m_closed != null) __instance.m_closed.SetActive(false);
    }
}
