using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // Native inventories are the source of truth. Snapshots below are short-lived transaction
    // inputs, never world projections. A request changes only its own item/slot/rule delta on
    // the native owner's game thread, through the installed game's Inventory methods.
    internal static class LiveChestWorld
    {
        internal const string TokenKey = "runic.live-chest.token.v1";
        private const string ReceiptsKey = "runic.live-chest.receipts.v1";
        private const string SnapshotRpc = "RunicLiveChest_Snapshot_v1";
        private const string SnapshotReplyRpc = "RunicLiveChest_SnapshotReply_v1";
        private const string ApplyRpc = "RunicLiveChest_Apply_v1";
        private const string ApplyReplyRpc = "RunicLiveChest_ApplyReply_v1";
        private const int MaxPacket = 4 * 1024 * 1024;
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.live-chests.v1", () => new World().Routes());
        internal static void Tick() => ((Action)Routes["tick"])();
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static byte[] Read(ZDO zdo) => ((Func<ZDO, byte[]>)Routes["read"])(zdo);
        // Background Production may consume a reply from its previous tick instead of
        // making an optional link wait for a new request-specific snapshot each time.
        internal static byte[] ReadAvailable(ZDO zdo)
        {
            // An older independently installed module can have registered the shared
            // provider first. Preserve its read API rather than failing on a missing route.
            return ((Func<ZDO, byte[]>)Routes[Routes.ContainsKey("read-available") ? "read-available" : "read"])(zdo);
        }
        internal static string Endpoint(ZDOID id) => ((Func<ZDOID, string>)Routes["endpoint"])(id);
        internal static byte[] Cached(string endpoint) => ((Func<string, byte[]>)Routes["cached"])(endpoint);
        internal static bool IsLive(string endpoint) => ((Func<string, bool>)Routes["known"])(endpoint);
        internal static void RequestContext(string key) => ((Action<string>)Routes["context"])(key);
        internal static void Complete(string key) => ((Action<string>)Routes["complete"])(key);
        internal static byte[] ReadNative(ZDO zdo, string endpoint) => ((Func<ZDO, string, byte[]>)Routes["read-native"])(zdo, endpoint);
        internal static void RegisterNative(string prefix, Func<ZDO, string> identity, Func<ZDO, byte[]> capture,
            Func<ZDO, byte[], byte[], string> apply, Func<byte[], ZDO> resolve, Func<byte[], bool> retired) =>
            ((Action<string, Func<ZDO, string>, Func<ZDO, byte[]>, Func<ZDO, byte[], byte[], string>, Func<byte[], ZDO>, Func<byte[], bool>>)Routes["register-native"])
                (prefix, identity, capture, apply, resolve, retired);
        internal static int Apply(OperationRecord record, out string reason)
        {
            var result = ((Func<string, Tuple<string, byte[], byte[]>[], bool, Tuple<int, string>>)Routes["apply"])(record.Request.Key,
                record.Plan.Changes.Select(c => Tuple.Create(c.Endpoint, c.Before, c.After)).ToArray(), record.Plan.Native);
            reason = result.Item2; return result.Item1;
        }
        // 0: no native endpoints, 1: waiting, 2: applied, 3: refused and compensated.
        private sealed class Snapshot
        {
            internal ZDOID Id; internal byte[] Image; internal long Owner; internal float At, Requested, RequestStarted; internal string Nonce;
            internal bool Unavailable, ReadyForBackground;
            internal readonly HashSet<string> Seen = new HashSet<string>(), Waiting = new HashSet<string>();
        }
        private sealed class Step
        { internal string Endpoint, Key; internal byte[] Before, After; internal bool Done, Sent; internal float SentAt; internal long Owner; internal string Failure; }
        private sealed class Transaction
        { internal readonly List<Step> Steps = new List<Step>(); internal bool Undo; internal string Failure; }
        private sealed class Adapter
        {
            internal Func<ZDO, string> Identity;
            internal Func<ZDO, byte[]> Capture;
            internal Func<ZDO, byte[], byte[], string> Apply;
            internal Func<byte[], ZDO> Resolve;
            internal Func<byte[], bool> Retired;
        }
        private sealed class World
        {
            private readonly Dictionary<string, Snapshot> snapshots = new Dictionary<string, Snapshot>();
            private readonly Dictionary<string, Transaction> transactions = new Dictionary<string, Transaction>();
            private readonly Dictionary<string, Step> awaiting = new Dictionary<string, Step>();
            private readonly Dictionary<string, Adapter> adapters = new Dictionary<string, Adapter>();
            private ZRoutedRpc registered;
            private long sequence;
            private string currentRequest;
            private string WorldId => ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            internal Dictionary<string, Delegate> Routes() => new Dictionary<string, Delegate>
            {
                ["tick"] = new Action(Register),
                ["clear"] = new Action(() => { snapshots.Clear(); transactions.Clear(); awaiting.Clear(); sequence = 0; }),
                ["read"] = new Func<ZDO, byte[]>(Read),
                ["read-available"] = new Func<ZDO, byte[]>(zdo => ReadNative(zdo, Endpoint(zdo.m_uid), false)),
                ["read-native"] = new Func<ZDO, string, byte[]>((zdo, key) => ReadNative(zdo, key)),
                ["register-native"] = new Action<string, Func<ZDO, string>, Func<ZDO, byte[]>, Func<ZDO, byte[], byte[], string>, Func<byte[], ZDO>, Func<byte[], bool>>
                    ((prefix, identity, capture, apply, resolve, retired) => adapters[prefix] = new Adapter { Identity = identity, Capture = capture, Apply = apply, Resolve = resolve, Retired = retired }),
                ["endpoint"] = new Func<ZDOID, string>(Endpoint),
                ["cached"] = new Func<string, byte[]>(key => snapshots.TryGetValue(key, out var s) ? s.Image : null),
                ["known"] = new Func<string, bool>(key => snapshots.ContainsKey(key)),
                ["context"] = new Action<string>(key => currentRequest = key),
                ["complete"] = new Action<string>(key =>
                {
                    if (transactions.TryGetValue(key, out var tx))
                        foreach (var step in tx.Steps) awaiting.Remove(step.Key);
                    transactions.Remove(key);
                    foreach (var snapshot in snapshots.Values) { snapshot.Seen.Remove(key); snapshot.Waiting.Remove(key); }
                }),
                ["apply"] = new Func<string, Tuple<string, byte[], byte[]>[], bool, Tuple<int, string>>(Apply)
            };
            private void Register()
            {
                if (ZRoutedRpc.instance == null || ReferenceEquals(registered, ZRoutedRpc.instance)) return;
                registered = ZRoutedRpc.instance;
                registered.Register<ZPackage>(SnapshotRpc, SnapshotRequest);
                registered.Register<ZPackage>(SnapshotReplyRpc, SnapshotReply);
                registered.Register<ZPackage>(ApplyRpc, ApplyRequest);
                registered.Register<ZPackage>(ApplyReplyRpc, ApplyReply);
            }
            private string Endpoint(ZDOID id)
            {
                var zdo = ZDOMan.instance?.GetZDO(id);
                if (zdo == null) throw new OperationRejectedException("container-no-longer-exists");
                string token = zdo.GetString(TokenKey, "");
                if (!Guid.TryParseExact(token, "N", out _)) token = zdo.GetString(ManagedChestLedger.Marker, "");
                if (!Guid.TryParseExact(token, "N", out _))
                {
                    // Stable identity only; no inventory enrollment or ownership change.
                    token = OperationRequest.Hash(WorldId + ":native:" + id).Substring(0, 32).ToLowerInvariant();
                }
                string key = "chest:" + token;
                if (!snapshots.TryGetValue(key, out var snapshot)) snapshots[key] = snapshot = new Snapshot();
                snapshot.Id = id; return key;
            }
            private static Container Local(ZDOID id)
            {
                var obj = ZNetScene.instance?.FindInstance(id);
                var chest = obj == null ? null : obj.GetComponent<Container>();
                var view = chest == null ? null : ContainerAuthority.View(chest);
                return view != null && view.IsValid() && view.IsOwner() ? chest : null;
            }
            private static bool Owned(ZDOID id)
            {
                var obj = ZNetScene.instance?.FindInstance(id); var view = obj == null ? null : obj.GetComponent<ZNetView>();
                return view != null && view.IsValid() && view.IsOwner();
            }
            private Adapter AdapterFor(string key) => adapters.FirstOrDefault(a => key.StartsWith(a.Key, StringComparison.Ordinal)).Value;
            private bool Matches(ZDO zdo, string key) => key.StartsWith("chest:", StringComparison.Ordinal)
                ? key == Endpoint(zdo.m_uid) : AdapterFor(key)?.Identity(zdo) == key;
            private byte[] CaptureEndpoint(ZDO zdo, string key) => key.StartsWith("chest:", StringComparison.Ordinal)
                ? Capture(Local(zdo.m_uid), key.Substring(6)) : AdapterFor(key).Capture(zdo);
            private byte[] Capture(Container chest, string token)
            {
                var zdo = ContainerAuthority.View(chest).GetZDO();
                var inventory = chest.GetInventory();
                if (inventory == null) throw new OperationRejectedException("container-inventory-unavailable");
                // Identity belongs to the native owner; it does not enroll or replace inventory.
                // Persist it on capture so a new link survives a save before its first transfer.
                const string productionTokenKey = "runic.transactions.world-object.token";
                string productionToken = zdo.GetString(productionTokenKey, "");
                if (productionToken.Length == 0) productionToken = token;
                if (!Guid.TryParseExact(productionToken, "N", out var parsed) || parsed == Guid.Empty)
                    throw new OperationRejectedException("container-link-identity-invalid");
                bool publish = zdo.GetString(TokenKey, "") != token ||
                    zdo.GetString(productionTokenKey, "") != productionToken ||
                    zdo.GetInt("runic.transactions.world-object.token-present", 0) != 1;
                zdo.Set(TokenKey, token);
                zdo.Set(productionTokenKey, productionToken);
                zdo.Set("runic.transactions.world-object.token-present", 1);
                if (publish) ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                var body = new ManagedChestLedger.Image
                {
                    Prefab = zdo.GetPrefab(), Position = zdo.GetPosition(), Token = token,
                    Width = inventory.GetWidth(), Height = inventory.GetHeight(), Items = NativeInventoryCodec.Save(inventory),
                    Rules = zdo.GetString(ManagedChestLedger.RulesKey, ""), AutoRules = zdo.GetString("SmartSort.AutoRules.v1", ""),
                    ProductionToken = productionToken,
                    Creator = zdo.GetLong(ZDOVars.s_creator), SourceOrigin = zdo.GetString(WorldInventoryOrigin.Key, "")
                };
                return body.Encode();
            }
            private void SetSnapshot(string key, byte[] body, long owner)
            {
                var snapshot = snapshots[key];
                if (snapshot.Image == null || !OwnerOperationQueue.Same(FileOperationEndpoints.Inventory(snapshot.Image), body))
                    snapshot.Image = FileOperationEndpoints.Transient(WorldId, key, ++sequence, body);
                snapshot.Owner = owner; snapshot.At = Time.realtimeSinceStartup; snapshot.Nonce = null; snapshot.Unavailable = false;
                snapshot.ReadyForBackground = true;
                if (snapshot.Seen.Count > 1024) snapshot.Seen.Clear();
                snapshot.Seen.UnionWith(snapshot.Waiting); snapshot.Waiting.Clear();
            }
            private byte[] Read(ZDO zdo)
            { return ReadNative(zdo, Endpoint(zdo.m_uid)); }
            private void Unavailable(string key, long owner)
            {
                var snapshot = snapshots[key];
                snapshot.Image = null; snapshot.Owner = owner; snapshot.At = Time.realtimeSinceStartup;
                snapshot.Nonce = null; snapshot.Unavailable = true; snapshot.ReadyForBackground = false;
                snapshot.Seen.Clear(); snapshot.Waiting.Clear();
            }
            private byte[] ReadNative(ZDO zdo, string key, bool requireFresh = true)
            {
                Register();
                if (zdo == null) throw new OperationRejectedException("container-no-longer-exists");
                if (!snapshots.TryGetValue(key, out var snapshot)) snapshots[key] = snapshot = new Snapshot { Id = zdo.m_uid };
                if (Owned(zdo.m_uid))
                {
                    SetSnapshot(key, CaptureEndpoint(zdo, key), zdo.GetOwner());
                    return snapshot.Image;
                }
                long owner = zdo.GetOwner();
                if (snapshot.Image != null && snapshot.Owner == owner &&
                    (currentRequest != null && snapshot.Seen.Contains(currentRequest) ||
                     !requireFresh && snapshot.ReadyForBackground ||
                     (!requireFresh || currentRequest == null) && Time.realtimeSinceStartup - snapshot.At < 1f))
                {
                    // Production's normal scheduler interval exceeds the foreground cache
                    // lifetime. Consume an unread reply once on its next tick; native Apply
                    // still checks the actual items before changing anything.
                    if (!requireFresh)
                    {
                        snapshot.ReadyForBackground = false;
                        if (currentRequest != null) snapshot.Seen.Add(currentRequest);
                    }
                    return snapshot.Image;
                }
                if (snapshot.Unavailable && snapshot.Owner == owner && Time.realtimeSinceStartup - snapshot.At < .5f)
                    throw new OperationRejectedException("container-native-snapshot-unavailable");
                if (owner == 0 || !InventoryOperationNetwork.Compatible(owner))
                    throw new OperationRejectedException("container-native-owner-unavailable");
                // This is only a read before planning/commit, never a partially applied
                // transaction. Release a silent/unloaded endpoint instead of retaining an
                // obsolete station request through a portal trip. Apply/recovery is unchanged.
                if (snapshot.Nonce != null && snapshot.Owner == owner && Time.realtimeSinceStartup - snapshot.RequestStarted >= 5f)
                {
                    Unavailable(key, owner);
                    throw new OperationRejectedException("container-native-snapshot-unavailable");
                }
                if (currentRequest != null) snapshot.Waiting.Add(currentRequest);
                if (snapshot.Nonce == null || Time.realtimeSinceStartup - snapshot.Requested > .5f || snapshot.Owner != owner)
                {
                    // A resend must keep its identity or a slow but valid reply will
                    // forever arrive for the nonce that the next retry just replaced.
                    if (snapshot.Nonce == null || snapshot.Owner != owner)
                    { snapshot.Nonce = Guid.NewGuid().ToString("N"); snapshot.RequestStarted = Time.realtimeSinceStartup; }
                    snapshot.Requested = Time.realtimeSinceStartup; snapshot.Owner = owner;
                    var p = new ZPackage(); p.Write(WorldId); p.Write(zdo.m_uid); p.Write(key); p.Write(snapshot.Nonce);
                    registered.InvokeRoutedRPC(owner, SnapshotRpc, p);
                }
                throw new OperationDeferredException("container-live-state-requested");
            }
            private bool FromServer(long sender) => ZNet.instance != null &&
                sender == (ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0);
            private void SnapshotRequest(long sender, ZPackage packet)
            {
                if (!FromServer(sender) || packet.Size() > 2048) return;
                try
                {
                    if (packet.ReadString() != WorldId) return;
                    var id = packet.ReadZDOID(); string key = packet.ReadString(), nonce = packet.ReadString(); OperationPacket.End(packet);
                    var zdo = ZDOMan.instance.GetZDO(id);
                    // An empty snapshot is an explicit unavailable reply from the requested
                    // owner. Without it, unloading the object silently strands its request.
                    byte[] body = zdo != null && Owned(id) && Matches(zdo, key) ? CaptureEndpoint(zdo, key) : Array.Empty<byte>();
                    var p = new ZPackage(); p.Write(WorldId); p.Write(key); p.Write(nonce); p.Write(body);
                    registered.InvokeRoutedRPC(sender, SnapshotReplyRpc, Pack(p));
                }
                catch (Exception error) { MutationGate.Report("Live chest capture: " + error.Message); }
            }
            private void SnapshotReply(long sender, ZPackage packet)
            {
                if (ZNet.instance?.IsServer() != true) return;
                try
                {
                    var p = Unpack(packet); if (p.ReadString() != WorldId) return;
                    string key = p.ReadString(), nonce = p.ReadString(); byte[] body = OperationPacket.Bytes(p, MaxPacket); OperationPacket.End(p);
                    if (!snapshots.TryGetValue(key, out var s) || s.Nonce != nonce || s.Owner != sender ||
                        ZDOMan.instance.GetZDO(s.Id)?.GetOwner() != sender) return;
                    if (body.Length == 0) { Unavailable(key, sender); return; }
                    if (key.StartsWith("chest:", StringComparison.Ordinal))
                    {
                        var decoded = ManagedChestLedger.Image.Decode(body);
                        if ("chest:" + decoded.Token != key || decoded.Prefab != ZDOMan.instance.GetZDO(s.Id).GetPrefab()) return;
                    }
                    SetSnapshot(key, body, sender);
                }
                catch (Exception error) { MutationGate.Report("Live chest reply: " + error.Message); }
            }
            private Tuple<int, string> Apply(string key, Tuple<string, byte[], byte[]>[] changes, bool native)
            {
                Register();
                if (!transactions.TryGetValue(key, out var tx))
                {
                    tx = new Transaction();
                    foreach (var change in changes.Where(c => AdapterFor(c.Item1) != null))
                    {
                        var adapter = AdapterFor(change.Item1);
                        // A saved removal/salvage receipt is a retained recovery record,
                        // not an instruction to locate or recreate its destroyed station.
                        if (adapter.Retired(FileOperationEndpoints.Inventory(change.Item2)) || adapter.Retired(FileOperationEndpoints.Inventory(change.Item3)))
                        { snapshots.Remove(change.Item1); continue; }
                        if (snapshots.ContainsKey(change.Item1)) continue;
                        var instance = adapter.Resolve(FileOperationEndpoints.Inventory(change.Item2));
                        if (instance == null) return Tuple.Create(3, "native-object-no-longer-exists");
                        snapshots[change.Item1] = new Snapshot { Id = instance.m_uid, Image = change.Item2, Owner = instance.GetOwner() };
                    }
                    foreach (var change in changes.Where(c => c.Item1.StartsWith("chest:", StringComparison.Ordinal)))
                    {
                        var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(change.Item2));
                        var next = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(change.Item3));
                        if (body.Retired || next.Retired) { snapshots.Remove(change.Item1); continue; } // Legacy pending recovery outputs are not live containers.
                        if (snapshots.ContainsKey(change.Item1)) continue;
                        var candidates = NetworkInventoryAccess.Nearby(body.Position, 2f).Where(z => z.GetPrefab() == body.Prefab &&
                            (z.GetString(TokenKey, "") == body.Token || z.GetString(ManagedChestLedger.Marker, "") == body.Token ||
                             OperationRequest.Hash(WorldId + ":native:" + z.m_uid).Substring(0, 32).Equals(body.Token, StringComparison.OrdinalIgnoreCase))).ToArray();
                        if (candidates.Length != 1) return Tuple.Create(3, "native-container-no-longer-exists");
                        snapshots[change.Item1] = new Snapshot { Id = candidates[0].m_uid, Image = change.Item2, Owner = candidates[0].GetOwner() };
                    }
                    foreach (var change in changes.Where(c => IsLive(c.Item1)))
                        tx.Steps.Add(new Step { Endpoint = change.Item1, Before = change.Item2, After = change.Item3,
                            Done = !native && OwnerOperationQueue.Same(SharedOperationHost.ReadStored(change.Item1), change.Item3),
                            Key = OperationRequest.Hash(key + ":" + change.Item1).Substring(0, 32).ToLowerInvariant() });
                    if (tx.Steps.Count == 0) return Tuple.Create(0, "");
                    transactions[key] = tx;
                }
                if (tx.Failure != null && !tx.Undo)
                {
                    tx.Undo = true;
                    foreach (var step in tx.Steps)
                    {
                        if (!step.Done) { step.Done = true; step.Failure = null; continue; }
                        step.Done = false; step.Sent = false; step.Failure = null;
                        var before = step.Before; step.Before = step.After; step.After = before; step.Key += "u";
                    }
                }
                foreach (var step in tx.Steps)
                {
                    if (step.Done) continue;
                    if (step.Failure != null)
                    {
                        if (tx.Undo) throw new InvalidOperationException("Native chest compensation failed: " + step.Failure);
                        tx.Failure = step.Failure; return Tuple.Create(1, "native-change-recompute");
                    }
                    var snapshot = snapshots[step.Endpoint]; var zdo = ZDOMan.instance.GetZDO(snapshot.Id);
                    if (zdo == null) { step.Failure = "container-was-removed"; return Tuple.Create(1, step.Failure); }
                    if (zdo.GetString(ReceiptsKey, "").Split(',').Contains(step.Key)) { step.Done = true; continue; }
                    long owner = zdo.GetOwner();
                    if (!step.Sent || step.Owner != owner || Time.realtimeSinceStartup - step.SentAt > .5f)
                    {
                        step.Owner = owner; step.Sent = true; step.SentAt = Time.realtimeSinceStartup;
                        if (Owned(snapshot.Id))
                        {
                            step.Failure = ApplyLocal(zdo, step);
                            step.Done = step.Failure == null;
                            SetSnapshot(step.Endpoint, CaptureEndpoint(zdo, step.Endpoint), owner);
                            if (step.Done) continue;
                        }
                        else if (owner != 0 && InventoryOperationNetwork.Compatible(owner))
                        {
                            awaiting[step.Key] = step;
                            var p = new ZPackage(); p.Write(WorldId); p.Write(snapshot.Id); p.Write(step.Endpoint); p.Write(step.Key);
                            p.Write(FileOperationEndpoints.Inventory(step.Before)); p.Write(FileOperationEndpoints.Inventory(step.After));
                            registered.InvokeRoutedRPC(owner, ApplyRpc, Pack(p));
                        }
                    }
                    // One step at a time: a failure compensates only preceding deltas, never
                    // restores a whole inventory or touches unrelated native modifications.
                    return Tuple.Create(1, "native-operation-running");
                }
                return Tuple.Create(tx.Undo ? 3 : 2, tx.Failure ?? "");
            }
            private void ApplyRequest(long sender, ZPackage packet)
            {
                if (!FromServer(sender)) return;
                try
                {
                    var p = Unpack(packet); if (p.ReadString() != WorldId) return;
                    var id = p.ReadZDOID(); string endpoint = p.ReadString(), key = p.ReadString();
                    var before = OperationPacket.Bytes(p, MaxPacket); var after = OperationPacket.Bytes(p, MaxPacket); OperationPacket.End(p);
                    var zdo = ZDOMan.instance.GetZDO(id); if (!Owned(id) || !Matches(zdo, endpoint)) return;
                    var step = new Step { Endpoint = endpoint, Key = key,
                        Before = FileOperationEndpoints.Transient(WorldId, endpoint, 0, before),
                        After = FileOperationEndpoints.Transient(WorldId, endpoint, 1, after) };
                    string failure = ApplyLocal(zdo, step);
                    var reply = new ZPackage(); reply.Write(WorldId); reply.Write(key); reply.Write(failure ?? "");
                    reply.Write(CaptureEndpoint(zdo, endpoint));
                    registered.InvokeRoutedRPC(sender, ApplyReplyRpc, Pack(reply));
                }
                catch (Exception error) { MutationGate.Report("Native chest operation: " + error); }
            }
            private void ApplyReply(long sender, ZPackage packet)
            {
                if (ZNet.instance?.IsServer() != true) return;
                try
                {
                    var p = Unpack(packet); if (p.ReadString() != WorldId) return;
                    string key = p.ReadString(), failure = p.ReadString(); byte[] body = OperationPacket.Bytes(p, MaxPacket); OperationPacket.End(p);
                    if (!awaiting.TryGetValue(key, out var step) || sender != step.Owner) return;
                    step.Failure = failure.Length == 0 ? null : failure; step.Done = failure.Length == 0;
                    SetSnapshot(step.Endpoint, body, sender); awaiting.Remove(key);
                }
                catch (Exception error) { MutationGate.Report("Native chest completion: " + error.Message); }
            }
            private string ApplyLocal(ZDO zdo, Step step)
            {
                var receipts = zdo.GetString(ReceiptsKey, "").Split(',').Where(x => x.Length > 0).ToList();
                if (receipts.Contains(step.Key)) return null;
                if (AdapterFor(step.Endpoint) is Adapter adapter)
                {
                    string result = adapter.Apply(zdo, FileOperationEndpoints.Inventory(step.Before), FileOperationEndpoints.Inventory(step.After));
                    if (result != null) return result;
                    receipts.Add(step.Key); zdo.Set(ReceiptsKey, string.Join(",", receipts.Skip(Math.Max(0, receipts.Count - 256))));
                    ZDOMan.instance.ForceSendZDO(zdo.m_uid); return null;
                }
                var chest = Local(zdo.m_uid);
                var before = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(step.Before));
                var after = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(step.After));
                var live = chest.GetInventory();
                var oldInventory = NativeInventoryCodec.Load(before.Items, before.Width, before.Height);
                var newInventory = NativeInventoryCodec.Load(after.Items, after.Width, after.Height);
                string failure = ApplyDelta(live, oldInventory, newInventory);
                if (failure != null) return failure;
                if (before.Rules != after.Rules) zdo.Set(ManagedChestLedger.RulesKey, after.Rules);
                if (before.AutoRules != after.AutoRules) zdo.Set("SmartSort.AutoRules.v1", after.AutoRules);
                zdo.Set(TokenKey, before.Token);
                receipts.Add(step.Key); zdo.Set(ReceiptsKey, string.Join(",", receipts.Skip(Math.Max(0, receipts.Count - 256))));
                // Native saving remains responsible for contents and ordinary world persistence.
                AccessTools.Method(typeof(Container), "Save").Invoke(chest, Array.Empty<object>());
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
                return null;
            }
        }

        private static Dictionary<string, Tuple<ItemDrop.ItemData, long>> Quantities(Inventory inventory) => inventory.GetAllItems()
            .GroupBy(NativeInventoryCodec.ItemIdentity).ToDictionary(g => g.Key, g => Tuple.Create(g.First(), g.Sum(i => (long)i.m_stack)));

        internal static string ApplyDelta(Inventory live, Inventory before, Inventory after)
        {
            var prior = Quantities(before); var next = Quantities(after);
            var changes = prior.Keys.Union(next.Keys).Select(key =>
            {
                prior.TryGetValue(key, out var a); next.TryGetValue(key, out var b);
                return Tuple.Create(key, b?.Item1 ?? a.Item1, (b?.Item2 ?? 0) - (a?.Item2 ?? 0));
            }).Where(x => x.Item3 != 0).ToArray();
            // Sorting, consolidation and splitting have no quantity delta. Apply their
            // explicit layout only to the exact inventory the user selected; a newer
            // native edit must produce a refusal, never a successful no-op or overwrite.
            if (changes.Length == 0 && !OwnerOperationQueue.Same(NativeInventoryCodec.ComparisonBytes(before), NativeInventoryCodec.ComparisonBytes(after)))
            {
                if (live.GetWidth() != after.GetWidth() || live.GetHeight() != after.GetHeight() ||
                    !OwnerOperationQueue.Same(NativeInventoryCodec.ComparisonBytes(live), NativeInventoryCodec.ComparisonBytes(before))) return "native-layout-changed";
                var available = live.GetAllItems().ToList();
                var desired = new List<ItemDrop.ItemData>();
                foreach (var item in after.GetAllItems())
                {
                    var match = available.FirstOrDefault(x => NativeInventoryCodec.ItemIdentity(x) == NativeInventoryCodec.ItemIdentity(item));
                    if (match != null) available.Remove(match);
                    else match = item.Clone();
                    match.m_stack = item.m_stack; match.m_gridPos = item.m_gridPos; desired.Add(match);
                }
                live.GetAllItems().Clear(); live.GetAllItems().AddRange(desired);
                AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }).Invoke(live, new object[] { false, false });
                return null;
            }
            // A quantity-only delta loses which stack the player selected and which
            // destination cell they dropped onto. Preserve the planner's per-slot edits;
            // identical items elsewhere are neither substitute sources nor destinations.
            var slots = SlotChanges(before, after).ToArray();
            foreach (var change in slots.Where(x => x.Amount < 0))
                if (SlotQuantity(live, change) < -change.Amount) return "native-materials-changed";
            // Preflight on a current native clone, honoring installed Inventory patches.
            var trial = NativeInventoryCodec.Clone(live);
            foreach (var change in slots.OrderBy(x => x.Amount))
                if (!ChangeSlot(trial, change, change.Amount)) return "native-inventory-capacity-changed";
            var applied = new List<SlotChange>();
            foreach (var change in slots.OrderBy(x => x.Amount))
            {
                int quantity = SlotQuantity(live, change);
                if (ChangeSlot(live, change, change.Amount)) { applied.Add(change); continue; }
                int partial = SlotQuantity(live, change) - quantity;
                if (partial != 0 && !ChangeSlot(live, change, -partial))
                    throw new InvalidOperationException("Installed inventory behavior refused a partial-transfer return");
                foreach (var undo in applied.AsEnumerable().Reverse())
                    if (!ChangeSlot(live, undo, -undo.Amount))
                        throw new InvalidOperationException("Installed inventory behavior refused a compensating delta");
                return "native-inventory-operation-refused";
            }
            return null;
        }
        private sealed class SlotChange
        {
            internal Vector2i Slot;
            internal string Identity;
            internal ItemDrop.ItemData Exemplar;
            internal int Amount;
        }
        private static IEnumerable<SlotChange> SlotChanges(Inventory before, Inventory after)
        {
            var prior = before.GetAllItems().ToDictionary(item => item.m_gridPos);
            var next = after.GetAllItems().ToDictionary(item => item.m_gridPos);
            foreach (var slot in prior.Keys.Union(next.Keys))
            {
                prior.TryGetValue(slot, out var oldItem); next.TryGetValue(slot, out var newItem);
                string oldIdentity = oldItem == null ? null : NativeInventoryCodec.ItemIdentity(oldItem);
                string newIdentity = newItem == null ? null : NativeInventoryCodec.ItemIdentity(newItem);
                if (oldIdentity == newIdentity)
                {
                    int delta = newItem.m_stack - oldItem.m_stack;
                    if (delta != 0) yield return new SlotChange { Slot = slot, Identity = oldIdentity, Exemplar = newItem, Amount = delta };
                    continue;
                }
                if (oldItem != null) yield return new SlotChange { Slot = slot, Identity = oldIdentity, Exemplar = oldItem, Amount = -oldItem.m_stack };
                if (newItem != null) yield return new SlotChange { Slot = slot, Identity = newIdentity, Exemplar = newItem, Amount = newItem.m_stack };
            }
        }
        private static int SlotQuantity(Inventory inventory, SlotChange change)
        {
            var item = inventory.GetItemAt(change.Slot.x, change.Slot.y);
            return item != null && NativeInventoryCodec.ItemIdentity(item) == change.Identity ? item.m_stack : 0;
        }
        private static bool ChangeSlot(Inventory inventory, SlotChange change, int amount)
        {
            if (change.Slot.x < 0 || change.Slot.y < 0 || change.Slot.x >= inventory.GetWidth() || change.Slot.y >= inventory.GetHeight()) return false;
            var existing = inventory.GetItemAt(change.Slot.x, change.Slot.y);
            int quantity = SlotQuantity(inventory, change);
            if (amount < 0)
            {
                if (quantity < -amount || !inventory.RemoveItem(existing, -amount)) return false;
            }
            else
            {
                if (existing != null && (quantity == 0 || !existing.IsSameType(change.Exemplar))) return false;
                int maximum = existing?.m_shared.m_maxStackSize ?? change.Exemplar.m_shared.m_maxStackSize;
                if (amount > Math.Max(0, maximum - quantity)) return false;
                var moved = change.Exemplar.Clone(); moved.m_stack = amount;
                var insert = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
                if (!(bool)insert.Invoke(inventory, new object[] { moved, amount, change.Slot.x, change.Slot.y, false })) return false;
            }
            return SlotQuantity(inventory, change) == quantity + amount;
        }
        private static ZPackage Pack(ZPackage packet)
        {
            using (var output = new MemoryStream())
            {
                using (var zip = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                { var data = packet.GetArray(); zip.Write(data, 0, data.Length); }
                if (output.Length > MaxPacket) throw new InvalidDataException("Native operation exceeds transport capacity");
                return new ZPackage(output.ToArray());
            }
        }
        private static ZPackage Unpack(ZPackage packet)
        {
            if (packet.Size() > MaxPacket) throw new InvalidDataException("Native operation exceeds transport capacity");
            using (var input = new MemoryStream(packet.GetArray()))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192]; int count;
                while ((count = zip.Read(buffer, 0, buffer.Length)) != 0)
                { if (output.Length + count > MaxPacket) throw new InvalidDataException("Native operation exceeds transport capacity"); output.Write(buffer, 0, count); }
                return new ZPackage(output.ToArray());
            }
        }
    }
}
