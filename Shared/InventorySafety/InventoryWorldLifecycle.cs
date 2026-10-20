using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;

namespace RunicAutomation.Operations
{
    internal static class InventoryWorldLifecycle
    {
        private const string Kind = "inventory.retire-chest.v2";
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.lifecycle.v2", () => new Lifecycle().Routes());
        internal static void Tick() { ((Action)Routes["tick"])(); RetiredChestContents.Tick(); }
        internal static void Clear() { ((Action)Routes["clear"])(); RetiredChestContents.Clear(); }
        internal static bool BeforeDestroyed(ZDOID id) => ((Func<ZDOID, bool>)Routes["destroy"])(id);
        internal static void RegisterAdapter(string name, Func<ZDO, Tuple<string, string>> identity, Action recover) =>
            ((Action<string, Func<ZDO, Tuple<string, string>>, Action>)Routes["adapter"])(name, identity, recover);
        internal static void RegisterRemovalSnapshot(string name, Func<ZDO, byte[]> capture) =>
            ((Action<string, Func<ZDO, byte[]>>)Routes["snapshot-adapter"])(name, capture);
        internal static byte[] PendingSnapshot(string kind, string token) => ((Func<string, string, byte[]>)Routes["pending-snapshot"])(kind, token);
        internal static bool RemovalPending(string kind, string token) => ((Func<string, string, bool>)Routes["removal-pending"])(kind, token);
        internal static bool ChestRemovalPending(string token) => RemovalPending(Kind, token);
        internal static bool Managed(ZDO zdo) => ((Func<ZDO, bool>)Routes["managed"])(zdo);
        internal static bool OwnsPatches
        {
            get
            {
                var owners = SharedState.Get("operations.lifecycle.patch-owner.v1", () => new Dictionary<string, string>());
                string assembly = typeof(InventoryWorldLifecycle).Assembly.FullName;
                lock (SharedState.Sync)
                { if (!owners.TryGetValue("owner", out string owner)) owners["owner"] = owner = assembly; return owner == assembly; }
            }
        }
        internal static void RecoverRemoved(ZDOID id, string kind, string token) => ((Action<ZDOID, string, string>)Routes["recover"])(id, kind, token);
        internal static ZDO PendingObject(string kind, string token) => ((Func<string, string, ZDO>)Routes["pending-object"])(kind, token);
        internal static void RetireMissingChest(ZDOID id, string token) => RecoverRemoved(id, Kind, token);
        private sealed class Pending
        { internal string Token, OperationKind = Kind; internal bool Submitted, Recovered; internal byte[] NativeSnapshot, NativeRecord; }
        private sealed class Lifecycle
        {
            private readonly Dictionary<ZDOID, Pending> pending = new();
            private readonly Dictionary<string, Tuple<Func<ZDO, Tuple<string, string>>, Action>> adapters = new();
            private readonly Dictionary<string, Func<ZDO, byte[]>> snapshots = new();
            private bool completing, registered, recovered;
            internal Dictionary<string, Delegate> Routes() => new()
            {
                ["tick"] = new Action(Tick),
                ["clear"] = new Action(() => { pending.Clear(); completing = false; recovered = false; }),
                ["destroy"] = new Func<ZDOID, bool>(BeforeDestroyed),
                ["pending-object"] = new Func<string, string, ZDO>((kind, token) => ZDOMan.instance?.GetZDO(pending.FirstOrDefault(p => p.Value.OperationKind == kind && p.Value.Token == token).Key)),
                ["pending-snapshot"] = new Func<string, string, byte[]>((kind, token) => pending.Values.FirstOrDefault(p => p.OperationKind == kind && p.Token == token)?.NativeSnapshot),
                ["removal-pending"] = new Func<string, string, bool>((kind, token) => pending.Values.Any(p => p.OperationKind == kind && p.Token == token)),
                ["managed"] = new Func<ZDO, bool>(zdo => zdo != null && (ManagedChestLedger.Managed(zdo) || adapters.Values.Any(a => a.Item1(zdo) != null))),
                ["adapter"] = new Action<string, Func<ZDO, Tuple<string, string>>, Action>((name, identity, recover) => adapters[name] = Tuple.Create(identity, recover)),
                ["snapshot-adapter"] = new Action<string, Func<ZDO, byte[]>>((name, capture) => snapshots[name] = capture),
                ["recover"] = new Action<ZDOID, string, string>((id, kind, token) => pending[id] = new Pending { Token = token, OperationKind = kind, Recovered = true })
            };
            private bool BeforeDestroyed(ZDOID id)
            {
                if (completing || !InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer()) return true;
                var zdo = ZDOMan.instance.GetZDO(id);
                if (pending.ContainsKey(id) || zdo == null) return true;
                Pending removal; Func<ZDO, byte[]> capture = null;
                if (ManagedChestLedger.Managed(zdo))
                { removal = new Pending { Token = zdo.GetString(ManagedChestLedger.Marker, "") }; capture = ManagedChestLedger.CaptureRemoval; }
                else
                {
                    var match = adapters.Select(a => Tuple.Create(a.Key, a.Value.Item1(zdo))).FirstOrDefault(x => x.Item2 != null);
                    if (match == null) return true;
                    removal = new Pending { OperationKind = match.Item2.Item1, Token = match.Item2.Item2 };
                    snapshots.TryGetValue(match.Item1, out capture);
                }
                pending.Add(id, removal);
                // Native destruction has already dropped construction resources. Holding
                // its ZDO alive here permits the piece to respawn and refund them again.
                // Capture only the fallback data before native releases ZDO extra-data;
                // ledger retirement and salvage run independently afterward.
                try { removal.NativeSnapshot = capture?.Invoke(zdo); }
                catch (Exception error)
                {
                    // Keep even unsupported native fields available for recovery rather
                    // than leaving a broken world object alive as an accidental archive.
                    try
                    {
                        var native = new ZPackage(); zdo.Serialize(native);
                        var record = new ZPackage(); record.Write(1); record.Write(id); record.Write(zdo.GetPosition());
                        record.Write(zdo.GetOwner()); record.Write((int)zdo.OwnerRevision); record.Write(zdo.DataRevision); record.Write(native.GetArray());
                        removal.NativeRecord = record.GetArray();
                    }
                    catch (Exception captureError) { MutationGate.Report("Removed inventory native record capture failed: " + captureError.Message); }
                    MutationGate.Report("Removed inventory retained for recovery; typed snapshot unavailable: " + error.Message);
                }
                return true;
            }
            private void Tick()
            {
                if (!InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (!registered)
                {
                    SharedOperationHost.Register(Kind, (actor, _) => actor == "world-destruction" ? null : "internal-world-operation-only", Plan);
                    registered = true;
                }
                if (!recovered)
                {
                    foreach (var persisted in SharedOperationHost.Enumerate("chest:"))
                    {
                        var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(persisted.Item2));
                        if (!body.Retired) continue;
                        foreach (var zdo in NetworkInventoryAccess.Nearby(body.Position, 1).Where(z => z.GetString(ManagedChestLedger.Marker, "") == body.Token))
                            pending[zdo.m_uid] = new Pending { Token = body.Token, Recovered = true };
                    }
                    foreach (var adapter in adapters.Values) adapter.Item2();
                    recovered = true;
                }
                foreach (var pair in pending.Where(p => !p.Value.Submitted).Take(8).ToArray())
                {
                    var packet = new ZPackage(); packet.Write(pair.Value.Token); pair.Value.Submitted = true;
                    Action<int, string, byte[]> finish = (status, reason, _) => {
                        if (status == (int)OperationStatus.Committed)
                        {
                            completing = true;
                            try
                            {
                                if (ZDOMan.instance.GetZDO(pair.Key) != null)
                                    AccessTools.Method(typeof(ZDOMan), "HandleDestroyedZDO").Invoke(ZDOMan.instance, new object[] { pair.Key });
                                pending.Remove(pair.Key);
                                // Also clear replicas created between the original native
                                // removal packet and its eventual salvage completion.
                                var removed = new ZPackage(); removed.Write(1); removed.Write(pair.Key);
                                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "DestroyZDO", removed);
                            }
                            finally { completing = false; }
                        }
                        else
                        {
                            bool terminal = status == (int)OperationStatus.Denied || status == (int)OperationStatus.IdConflict;
                            pair.Value.Submitted = terminal;
                            if (terminal) MutationGate.Report("Removed inventory needs recovery: " + reason);
                        }
                    };
                    var result = SharedOperationHost.Submit(ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), "world-destruction",
                        "retire:" + pair.Value.Token, pair.Value.OperationKind, packet.GetArray(), finish);
                    if (result.Item1 != (int)OperationStatus.Queued) finish(result.Item1, result.Item2, result.Item3);
                }
            }
            private Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] payload, Func<string, byte[]> read)
            {
                var packet = new ZPackage(payload); string token = packet.ReadString(); OperationPacket.End(packet);
                if (!Guid.TryParseExact(token, "N", out _)) throw new OperationRejectedException("invalid-chest-identity");
                string endpoint = "chest:" + token; byte[] before = read(endpoint);
                if (before == null)
                {
                    byte[] snapshot = pending.Values.FirstOrDefault(p => p.Token == token && p.OperationKind == Kind)?.NativeSnapshot;
                    if (snapshot == null) throw new OperationRejectedException("destroyed-chest-snapshot-unavailable");
                    before = SharedOperationHost.Import(endpoint, snapshot);
                }
                var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
                if (body.Retired) return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
                var changes = new List<Tuple<string, byte[], byte[]>>();
                // Retirement closes access immediately. Retain the authoritative items
                // until their ground drops commit; recovery resumes from this same record
                // after a restart and never creates another destructible chest.
                body.Retired = true;
                changes.Add(Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, body.Encode())));
                return Tuple.Create(changes.ToArray(), Array.Empty<byte>());
            }
        }
    }
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class RetireManagedInventoryBeforeWorldRemoval
    {
        private static bool Prefix(ZDOID uid, bool __runOriginal) => !__runOriginal || InventoryWorldLifecycle.BeforeDestroyed(uid);
    }
    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class PreventStaleNativeChestDrops
    {
        private static bool Prefix(Container __instance) => !InventoryOperationNetwork.Ready ||
            !ManagedChestLedger.Managed(ContainerAuthority.View(__instance)?.GetZDO());
    }
    [HarmonyPatch(typeof(Piece), "DropResources")]
    internal static class SalvageDoesNotCreateBuildingResources
    {
        private const string DroppedKey = "runic.inventory.building-resources-dropped.v1";
        private static bool Prepare() => InventoryWorldLifecycle.OwnsPatches;
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Piece __instance, bool __runOriginal, out ZDO __state)
        {
            __state = null;
            if (!__runOriginal) return true;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return true;
            if (zdo.GetBool(ManagedChestLedger.SalvageKey)) return false;
            if (!InventoryWorldLifecycle.Managed(zdo)) return true;
            if (zdo.GetBool(DroppedKey)) return false;
            __state = zdo;
            return true;
        }
        private static void Postfix(bool __runOriginal, ZDO __state)
        {
            if (__runOriginal && __state != null) __state.Set(DroppedKey, true);
        }
    }
}
