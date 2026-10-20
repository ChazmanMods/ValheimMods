using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // Native/third-party transfers have already changed their other endpoint when Save runs.
    // Keep those edits immediately, and merge their delta rather than replacing the ledger
    // with a possibly stale native snapshot. This intentionally follows native gameplay's
    // best-effort race semantics; it is not a transaction around an unknown mod's inventory.
    internal static class NativeChestInterop
    {
        internal const string Kind = "inventory.native-chest.v1";
        internal const string RevisionKey = "runic.inventory.native-revision.v1";
        internal const string ReceiptsKey = "runic.inventory.native-receipts.v1";
        private const string ServerActor = "native-chest:server";
        private static readonly FieldInfo Loading = AccessTools.Field(typeof(Container), "m_loading");
        private static readonly FieldInfo LastRevision = AccessTools.Field(typeof(Container), "m_lastRevision");
        private static readonly FieldInfo InventoryWidth = AccessTools.Field(typeof(Inventory), "m_width");
        private static readonly MethodInfo Changed = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.native-chest.v1", () => new Bridge().Routes());
        internal static bool IsApplying => ((Func<bool>)Routes["applying"])();
        internal static bool OwnsPatches
        {
            get
            {
                var owners = SharedState.Get("operations.native-chest.patch-owner.v1", () => new Dictionary<string, string>());
                string assembly = typeof(NativeChestInterop).Assembly.FullName;
                lock (SharedState.Sync)
                { if (!owners.TryGetValue("owner", out string owner)) owners["owner"] = owner = assembly; return owner == assembly; }
            }
        }
        internal static int PendingCount => 0;
        internal static void Seed(Container chest) { }
        internal static bool CaptureSave(Container chest) => false;
        internal static void ApplySnapshot(Container chest, byte[] items, int width, int height)
        {
            var view = chest == null ? null : ContainerAuthority.View(chest);
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            // Only the remote viewer's replica is refreshed. Never replace the live owner's
            // inventory from a query response, and never save a GUI snapshot back to the world.
            bool loading = (bool)Loading.GetValue(chest); Loading.SetValue(chest, true);
            try
            {
                var inventory = chest.GetInventory(); InventoryWidth.SetValue(inventory, width); inventory.SetHeight(height);
                inventory.Load(new ZPackage(items)); LastRevision.SetValue(chest, view.GetZDO().DataRevision);
            }
            finally { Loading.SetValue(chest, loading); }
        }
        internal static void Loaded(Container chest) { }
        internal static void Tick() { }
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static void ObserveReplica(ZDO zdo) { }
        internal static void Published(ZDO zdo, byte[] items, int width, int height, long revision, string receipts) =>
            ((Action<ZDO, byte[], int, int, long, string>)Routes["published"])(zdo, items, width, height, revision, receipts);

        // Native Container.Load expands rows after deserialization. Preserve that behavior
        // before the strict detached codec validates mod-expanded inventories.
        internal static Inventory LoadFlexible(byte[] bytes, int width, int height)
        {
            var packet = new ZPackage(bytes);
            if (packet.ReadInt() == 109)
            {
                int count = packet.ReadUShort();
                if (count > NativeInventoryCodec.MaximumItems) throw new InvalidDataException("Too many native chest items");
                for (int n = 0; n < count; n++)
                {
                    var item = ItemDrop.ItemData.Load(packet, (global::Version.Item)109).itemData;
                    width = Math.Max(width, item.m_gridPos.x + 1); height = Math.Max(height, item.m_gridPos.y + 1);
                }
            }
            return NativeInventoryCodec.Load(bytes, width, height);
        }

        internal static Inventory Merge(Inventory baseline, Inventory changed, Inventory latest)
        {
            if (baseline.GetWidth() == latest.GetWidth() && baseline.GetHeight() == latest.GetHeight() &&
                OwnerOperationQueue.Same(NativeInventoryCodec.Save(baseline), NativeInventoryCodec.Save(latest)))
                return NativeInventoryCodec.Clone(changed); // Includes intentional native sorting/layout/shape.
            var result = NativeInventoryCodec.Clone(latest);
            int width = Math.Max(result.GetWidth(), changed.GetWidth()), height = Math.Max(result.GetHeight(), changed.GetHeight());
            if (width <= 64 && height <= 64 && width * height <= NativeInventoryCodec.MaximumItems)
            { InventoryWidth.SetValue(result, width); result.SetHeight(height); }
            var before = Totals(baseline); var after = Totals(changed);
            int missing = 0;
            foreach (var pair in before)
            {
                long remove = pair.Value.Item2 - (after.TryGetValue(pair.Key, out var value) ? value.Item2 : 0);
                if (remove <= 0) continue;
                foreach (var item in result.GetAllItems().Where(x => NativeInventoryCodec.ItemIdentity(x) == pair.Key).ToArray())
                {
                    int take = (int)Math.Min(remove, item.m_stack); item.m_stack -= take; remove -= take;
                    if (item.m_stack == 0) result.GetAllItems().Remove(item);
                    if (remove == 0) break;
                }
                missing += (int)Math.Min(int.MaxValue - missing, remove);
            }
            foreach (var pair in after)
            {
                long add = pair.Value.Item2 - (before.TryGetValue(pair.Key, out var value) ? value.Item2 : 0);
                while (add > 0)
                {
                    var item = pair.Value.Item1.Clone(); item.m_stack = (int)Math.Min(add, ushort.MaxValue);
                    while (NativeInventoryCodec.CapacityFor(result, item) < item.m_stack && result.GetHeight() < 64 &&
                        result.GetWidth() * (result.GetHeight() + 1) <= NativeInventoryCodec.MaximumItems)
                        result.SetHeight(result.GetHeight() + 1);
                    if (!NativeInventoryCodec.AddExact(result, item)) throw new InvalidDataException("Native chest additions exceed supported capacity");
                    add -= item.m_stack;
                }
            }
            if (missing != 0) Debug.LogWarning("Runic native chest compatibility: concurrent withdrawal already consumed " + missing + " item(s); retained the native transfer and all additions.");
            NativeInventoryCodec.Validate(result); return result;
        }
        private static Dictionary<string, Tuple<ItemDrop.ItemData, long>> Totals(Inventory inventory)
        {
            var totals = new Dictionary<string, Tuple<ItemDrop.ItemData, long>>(StringComparer.Ordinal);
            foreach (var item in inventory.GetAllItems())
            {
                string key = NativeInventoryCodec.ItemIdentity(item);
                totals[key] = Tuple.Create(item, checked((totals.TryGetValue(key, out var previous) ? previous.Item2 : 0) + item.m_stack));
            }
            return totals;
        }
        private sealed class Edit
        {
            internal ZDOID Chest;
            internal string Token, Id, World;
            internal Inventory Before, After;
            internal byte[] Payload;
            internal bool Sent;
            internal float Retry;
            internal byte[] Encode()
            {
                var p = new ZPackage(); p.Write(1); p.Write(Chest); p.Write(Token); p.Write(Id);
                p.Write(Before.GetWidth()); p.Write(Before.GetHeight()); p.Write(NativeInventoryCodec.Save(Before));
                p.Write(After.GetWidth()); p.Write(After.GetHeight()); p.Write(NativeInventoryCodec.Save(After));
                using (var buffer = new MemoryStream())
                {
                    using (var zip = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true))
                    { var bytes = p.GetArray(); zip.Write(bytes, 0, bytes.Length); }
                    return buffer.ToArray();
                }
            }
            internal static Edit Decode(byte[] bytes)
            {
                using (var input = new MemoryStream(bytes, false))
                using (var zip = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var chunk = new byte[4096]; int count;
                    while ((count = zip.Read(chunk, 0, chunk.Length)) != 0)
                    { if (output.Length + count > 2 * 1024 * 1024 + 4096) throw new InvalidDataException("Native edit too large"); output.Write(chunk, 0, count); }
                    var p = new ZPackage(output.ToArray());
                    if (p.ReadInt() != 1) throw new InvalidDataException("Unsupported native edit");
                    var edit = new Edit { Chest = p.ReadZDOID(), Token = p.ReadString(), Id = p.ReadString() };
                    if (!Guid.TryParseExact(edit.Token, "N", out _) || !Guid.TryParseExact(edit.Id, "N", out _)) throw new InvalidDataException("Invalid native edit identity");
                    int width = p.ReadInt(), height = p.ReadInt(); edit.Before = LoadFlexible(OperationPacket.Bytes(p, 1024 * 1024), width, height);
                    width = p.ReadInt(); height = p.ReadInt(); edit.After = LoadFlexible(OperationPacket.Bytes(p, 1024 * 1024), width, height);
                    OperationPacket.End(p); return edit;
                }
            }
        }
        private sealed class Entry
        {
            internal Inventory Baseline;
            internal readonly List<Edit> Pending = new List<Edit>();
            internal long Revision = -1;
        }
        private sealed class Projection
        {
            internal byte[] Items;
            internal int Width, Height;
            internal long Revision;
            internal string Receipts;
        }
        private sealed class Bridge
        {
            private readonly Dictionary<Container, Entry> entries = new Dictionary<Container, Entry>();
            private readonly Dictionary<ZDOID, List<Projection>> published = new Dictionary<ZDOID, List<Projection>>();
            private readonly Dictionary<string, Edit> replicas = new Dictionary<string, Edit>();
            private readonly HashSet<string> preserved = new HashSet<string>();
            private int applying;
            private bool observing;
            private bool registered;
            private int frame = -1;
            internal Dictionary<string, Delegate> Routes() => new Dictionary<string, Delegate>
            {
                ["applying"] = new Func<bool>(() => applying > 0),
                ["pending"] = new Func<int>(() => entries.Values.Sum(x => x.Pending.Count) + replicas.Count),
                ["seed"] = new Action<Container>(Seed), ["capture"] = new Func<Container, bool>(Capture),
                ["apply"] = new Action<Container, byte[], int, int>(Apply), ["loaded"] = new Action<Container>(Loaded),
                ["tick"] = new Action(Tick), ["clear"] = new Action(() => { entries.Clear(); published.Clear(); replicas.Clear(); preserved.Clear(); applying = 0; observing = false; frame = -1; }),
                ["replica"] = new Action<ZDO>(ObserveReplica),
                ["published"] = new Action<ZDO, byte[], int, int, long, string>(Published)
            };
            private static ZDO Zdo(Container chest) => chest == null ? null : ContainerAuthority.View(chest)?.GetZDO();
            private static bool Eligible(Container chest) => chest != null && chest.GetInventory() != null && !ManagedDrawerInventory.IsDrawer(chest);
            private void Register()
            {
                if (registered) return;
                SharedOperationHost.Register(Kind, Authorize, Plan); registered = true;
            }
            private void Seed(Container chest)
            {
                if (!Eligible(chest) || applying != 0 || entries.ContainsKey(chest)) return;
                try { entries[chest] = new Entry { Baseline = NativeInventoryCodec.Clone(chest.GetInventory()) }; }
                catch (Exception error) { Warn(chest, "could not track its native baseline", error); }
            }
            private bool Capture(Container chest)
            {
                if (!Eligible(chest) || applying != 0 || (bool)Loading.GetValue(chest)) return false;
                var zdo = Zdo(chest);
                if (!InventoryOperationNetwork.Ready || !ManagedChestLedger.Managed(zdo))
                {
                    if (Eligible(chest))
                    {
                        try { entries[chest] = new Entry { Baseline = NativeInventoryCodec.Clone(chest.GetInventory()) }; }
                        catch { }
                    }
                    return false;
                }
                try
                {
                    Register();
                    if (!entries.TryGetValue(chest, out var entry))
                    {
                        var bytes = zdo.GetByteArray(ZDOVars.s_items);
                        if (bytes == null) return false;
                        entries[chest] = entry = new Entry { Baseline = LoadFlexible(bytes, chest.GetInventory().GetWidth(), chest.GetInventory().GetHeight()) };
                    }
                    Inventory current = NativeInventoryCodec.Clone(chest.GetInventory());
                    if (Same(entry.Baseline, current)) return true;
                    var edit = NewEdit(zdo, entry.Baseline, current);
                    // Keep a separate immutable edit for each native mutation; admission failure
                    // never discards it, and retries never invent a new operation identity.
                    entry.Pending.Add(edit); entry.Baseline = current;
                    if (entry.Pending.Count == 1)
                        Send(edit, (status, reason, response) => Complete(chest, entry, edit, status, reason, response));
                    return true;
                }
                catch (Exception error)
                {
                    Preserve(zdo, SafeSave(chest.GetInventory()), "native-save-unsupported");
                    Warn(chest, "kept an unsupported native save for recovery; allowing the native write", error); return false;
                }
            }
            private static bool Same(Inventory a, Inventory b) => a.GetWidth() == b.GetWidth() && a.GetHeight() == b.GetHeight() &&
                OwnerOperationQueue.Same(NativeInventoryCodec.Save(a), NativeInventoryCodec.Save(b));
            private static Edit NewEdit(ZDO zdo, Inventory before, Inventory after)
            {
                var edit = new Edit { Chest = zdo.m_uid, Token = zdo.GetString(ManagedChestLedger.Marker, ""),
                    Id = Guid.NewGuid().ToString("N"), World = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture),
                    Before = NativeInventoryCodec.Clone(before), After = NativeInventoryCodec.Clone(after) };
                edit.Payload = edit.Encode();
                if (edit.Payload.Length > 65536) throw new InvalidDataException("Native edit exceeds network packet capacity");
                return edit;
            }
            private void Loaded(Container chest)
            {
                if (!Eligible(chest) || applying != 0) return;
                var zdo = Zdo(chest);
                if (InventoryOperationNetwork.Ready && ManagedChestLedger.Managed(zdo) && zdo.GetByteArray(ZDOVars.s_items) is byte[] items)
                {
                    // The native body has already replaced m_inventory. Rejecting an old
                    // revision without restoring the tracked view would leave that stale
                    // replacement live and turn the next edit into a spurious delta.
                    if (entries.TryGetValue(chest, out var retained) && retained.Baseline != null &&
                        zdo.GetLong(RevisionKey, 0) < retained.Revision)
                    { SetInventory(chest, retained, NativeInventoryCodec.Clone(retained.Baseline), retained.Revision); return; }
                    try { Apply(chest, items, chest.GetInventory().GetWidth(), chest.GetInventory().GetHeight(), zdo.GetLong(RevisionKey, 0), zdo.GetString(ReceiptsKey, "")); }
                    catch (Exception error)
                    {
                        if (entries.TryGetValue(chest, out var entry) && entry.Pending.Count != 0 && entry.Baseline != null)
                            SetInventory(chest, entry, NativeInventoryCodec.Clone(entry.Baseline), entry.Revision);
                        Preserve(zdo, items, "native-load-overlay-unsupported"); Warn(chest, "retained pending native edits after a snapshot could not be merged", error);
                    }
                }
                else
                {
                    try { entries[chest] = new Entry { Baseline = NativeInventoryCodec.Clone(chest.GetInventory()) }; }
                    catch (Exception error) { Warn(chest, "could not track its loaded inventory", error); }
                }
            }
            private void Apply(Container chest, byte[] items, int width, int height)
            {
                if (!Eligible(chest)) return;
                var zdo = Zdo(chest); if (zdo == null) return;
                if (entries.ContainsKey(chest)) Capture(chest);
                Apply(chest, items, width, height, zdo.GetLong(RevisionKey, 0), zdo.GetString(ReceiptsKey, ""));
            }
            private void Apply(Container chest, byte[] items, int width, int height, long revision, string receipts)
            {
                if (!entries.TryGetValue(chest, out var entry)) entries[chest] = entry = new Entry();
                if (revision < entry.Revision) return;
                Inventory inventory = LoadFlexible(items, width, height);
                foreach (var edit in entry.Pending)
                    if (!HasReceipt(receipts, edit.Id)) inventory = Merge(edit.Before, edit.After, inventory);
                int targetWidth = Math.Max(inventory.GetWidth(), chest.GetInventory().GetWidth());
                int targetHeight = Math.Max(inventory.GetHeight(), chest.GetInventory().GetHeight());
                if (targetWidth <= 64 && targetHeight <= 64 && targetWidth * targetHeight <= NativeInventoryCodec.MaximumItems)
                { InventoryWidth.SetValue(inventory, targetWidth); inventory.SetHeight(targetHeight); }
                SetInventory(chest, entry, inventory, revision);
            }
            private void SetInventory(Container chest, Entry entry, Inventory inventory, long revision)
            {
                bool previousLoading = (bool)Loading.GetValue(chest);
                applying++; Loading.SetValue(chest, true);
                try
                {
                    var target = chest.GetInventory(); InventoryWidth.SetValue(target, inventory.GetWidth()); target.SetHeight(inventory.GetHeight());
                    target.GetAllItems().Clear(); target.GetAllItems().AddRange(inventory.GetAllItems());
                    entry.Baseline = NativeInventoryCodec.Clone(inventory); entry.Revision = revision;
                    Changed.Invoke(target, new object[] { false, false }); LastRevision.SetValue(chest, Zdo(chest).DataRevision);
                }
                finally { Loading.SetValue(chest, previousLoading); applying--; }
            }
            private void Tick()
            {
                Register();
                if (frame == Time.frameCount) return; frame = Time.frameCount;
                foreach (var pair in entries.ToArray())
                {
                    var chest = pair.Key; var entry = pair.Value;
                    if (chest == null && entry.Pending.Count == 0) { entries.Remove(chest); continue; }
                    if (entry.Pending.Count == 0) continue;
                    var edit = entry.Pending[0];
                    Send(edit, (status, reason, response) => Complete(chest, entry, edit, status, reason, response));
                }
                foreach (var edit in replicas.Values.ToArray())
                    Send(edit, (status, reason, response) => {
                        if (status == (int)OperationStatus.Committed) replicas.Remove(edit.Id);
                        else { edit.Sent = false; edit.Retry = Time.realtimeSinceStartup + 2f; Preserve(ZDOMan.instance?.GetZDO(edit.Chest), SafeSave(edit.After), "native-replica-" + reason); }
                    });
            }
            private void Send(Edit edit, Action<int, string, byte[]> completed)
            {
                if (edit.Sent || !InventoryOperationNetwork.Ready || Time.realtimeSinceStartup < edit.Retry || ZNet.instance == null ||
                    edit.World != ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture)) return;
                edit.Retry = Time.realtimeSinceStartup + .25f;
                if (ZNet.instance.IsServer())
                {
                    edit.Sent = true;
                    var result = SharedOperationHost.Submit(edit.World, ServerActor, edit.Id, Kind, edit.Payload, completed);
                    if (result.Item1 == (int)OperationStatus.Busy || result.Item1 == (int)OperationStatus.RecoveryPending) edit.Sent = false;
                    else if (result.Item1 != (int)OperationStatus.Queued) completed(result.Item1, result.Item2, result.Item3);
                }
                else edit.Sent = InventoryOperationNetwork.Send(edit.World, edit.Id, Kind, edit.Payload, completed);
            }
            private void Complete(Container chest, Entry entry, Edit edit, int status, string reason, byte[] response)
            {
                if (!entry.Pending.Contains(edit)) return;
                if (status != (int)OperationStatus.Committed)
                {
                    edit.Sent = false; edit.Retry = Time.realtimeSinceStartup + 2f;
                    Preserve(ZDOMan.instance?.GetZDO(edit.Chest), SafeSave(edit.After), "native-edit-" + reason);
                    return;
                }
                entry.Pending.Remove(edit);
                if (chest == null) return;
                try
                {
                    var p = new ZPackage(Expand(response, 1024 * 1024 + 8192)); long revision = p.ReadLong(); string receipts = p.ReadString();
                    int width = p.ReadInt(), height = p.ReadInt(); byte[] items = OperationPacket.Bytes(p, 1024 * 1024); OperationPacket.End(p);
                    Apply(chest, items, width, height, revision, receipts);
                }
                catch (Exception error) { Warn(chest, "native edit committed; refreshed view deferred", error); }
            }
            private string Authorize(string actor, byte[] bytes)
            {
                try
                {
                    var edit = Edit.Decode(bytes); var zdo = ZDOMan.instance?.GetZDO(edit.Chest);
                    if (zdo == null || !ManagedChestLedger.Managed(zdo) || zdo.GetString(ManagedChestLedger.Marker, "") != edit.Token) return "native-chest-identity-changed";
                    if (actor == ServerActor) return null;
                    return NetworkInventoryAccess.CanAccess(actor, zdo, 100f, false, false, out string reason) ? null : reason;
                }
                catch { return "invalid-native-chest-edit"; }
            }
            private Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
            {
                var edit = Edit.Decode(bytes); var zdo = ZDOMan.instance.GetZDO(edit.Chest);
                byte[] before = ManagedChestLedger.Read(zdo);
                var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
                Inventory merged = Merge(edit.Before, edit.After, NativeInventoryCodec.Load(body.Items, body.Width, body.Height));
                body.Width = merged.GetWidth(); body.Height = merged.GetHeight(); body.Items = NativeInventoryCodec.Save(merged);
                body.NativeReceipts = string.Join(",", (body.NativeReceipts ?? "").Split(',').Where(x => x.Length == 32 && x != edit.Id).Concat(new[] { edit.Id }).Reverse().Take(64).Reverse());
                byte[] after = FileOperationEndpoints.Next(before, body.Encode());
                var response = new ZPackage(); response.Write(FileOperationEndpoints.Revision(after)); response.Write(body.NativeReceipts);
                response.Write(body.Width); response.Write(body.Height); response.Write(body.Items);
                byte[] compressed;
                using (var buffer = new MemoryStream())
                {
                    using (var zip = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true))
                    { byte[] bytesToCompress = response.GetArray(); zip.Write(bytesToCompress, 0, bytesToCompress.Length); }
                    compressed = buffer.ToArray();
                }
                if (compressed.Length > 65536)
                {
                    Preserve(zdo, body.Items, "native-edit-response-too-large");
                    throw new OperationDeferredException("native-edit-response-too-large");
                }
                return Tuple.Create(new[] { Tuple.Create("chest:" + body.Token, before, after) }, compressed);
            }
            private void Published(ZDO zdo, byte[] items, int width, int height, long revision, string receipts)
            {
                if (!published.TryGetValue(zdo.m_uid, out var history)) published[zdo.m_uid] = history = new List<Projection>();
                if (!history.Any(x => x.Revision == revision))
                {
                    history.Add(new Projection { Items = (byte[])items.Clone(), Width = width, Height = height, Revision = revision, Receipts = receipts });
                    if (history.Count > 64) history.RemoveAt(0);
                }
            }
            private void ObserveReplica(ZDO zdo)
            {
                if (observing || zdo == null || !ManagedChestLedger.Managed(zdo) || ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active) return;
                byte[] incoming = zdo.GetByteArray(ZDOVars.s_items); if (incoming == null) return;
                if (!published.TryGetValue(zdo.m_uid, out var history) || history.Count == 0) return;
                var baseline = history.LastOrDefault(x => x.Revision == zdo.GetLong(RevisionKey, -1));
                if (baseline == null)
                {
                    // Pre-bridge worlds have no revision marker yet. An exact current
                    // projection needs no recovery copy and is safe to annotate on publish.
                    if (!OwnerOperationQueue.Same(history[history.Count - 1].Items, incoming)) Preserve(zdo, incoming, "unknown-native-baseline");
                    return;
                }
                if (OwnerOperationQueue.Same(baseline.Items, incoming)) return; // Includes an old packet carrying its old revision.
                observing = true;
                try
                {
                    var edit = NewEdit(zdo, LoadFlexible(baseline.Items, baseline.Width, baseline.Height), LoadFlexible(incoming, baseline.Width, baseline.Height));
                    // A retransmitted native ZDO packet must not be imported twice.
                    edit.Id = OperationRequest.Hash(edit.Token + ":" + baseline.Revision + ":" + Convert.ToBase64String(incoming)).Substring(0, 32).ToLowerInvariant();
                    if (HasReceipt(history[history.Count - 1].Receipts, edit.Id) || replicas.ContainsKey(edit.Id)) return;
                    edit.Payload = edit.Encode(); replicas.Add(edit.Id, edit);
                }
                catch (Exception error) { Preserve(zdo, incoming, "unsupported-native-replica"); Debug.LogWarning("Runic retained a native chest snapshot that could not be merged: " + error.Message); }
                finally { observing = false; }
            }
            private void Preserve(ZDO zdo, byte[] items, string reason)
            {
                if (zdo == null || items == null) return;
                string key = OperationRequest.Hash(zdo.m_uid + ":" + Convert.ToBase64String(items));
                if (!preserved.Add(key)) return;
                try
                {
                    string folder = Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "RunicOperations", ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), "native-recovery");
                    Directory.CreateDirectory(folder);
                    var packet = new ZPackage(); packet.Write(1); packet.Write(zdo.m_uid); packet.Write(zdo.GetPrefab()); packet.Write(zdo.GetPosition());
                    packet.Write(zdo.GetString(ManagedChestLedger.Marker, "")); packet.Write(reason); packet.Write(items);
                    File.WriteAllBytes(Path.Combine(folder, key + ".native-inventory"), packet.GetArray());
                    Debug.LogWarning("Runic preserved an external chest inventory for recovery: chest=" + zdo.m_uid + "; reason=" + reason + "; file=" + key + ".native-inventory");
                }
                catch (Exception error) { preserved.Remove(key); Debug.LogWarning("Runic could not write the retained native chest recovery copy: " + error.Message); }
            }
            private static byte[] SafeSave(Inventory inventory)
            { try { var packet = new ZPackage(); inventory.Save(packet); return packet.GetArray(); } catch { return null; } }
            private static void Warn(Container chest, string message, Exception error) => Debug.LogWarning("Runic native chest compatibility " + (chest == null ? "" : chest.name) + ": " + message + "; " + error.Message);
        }
        private static byte[] Expand(byte[] bytes, int maximum)
        {
            using (var input = new MemoryStream(bytes, false))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var chunk = new byte[4096]; int count;
                while ((count = zip.Read(chunk, 0, chunk.Length)) != 0)
                { if (output.Length + count > maximum) throw new InvalidDataException("Native chest response too large"); output.Write(chunk, 0, count); }
                return output.ToArray();
            }
        }
        private static bool HasReceipt(string receipts, string id) => (receipts ?? "").Split(',').Contains(id);
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class NativeChestInteropAwake
    {
        private static bool Prepare() => NativeChestInterop.OwnsPatches;
        private static void Postfix(Container __instance) => NativeChestInterop.Seed(__instance);
    }
    [HarmonyPatch(typeof(Container), "OnContainerChanged")]
    internal static class NativeChestInteropChanged
    {
        private static bool Prepare() => NativeChestInterop.OwnsPatches;
        private static bool Prefix(Container __instance, bool __runOriginal) => !__runOriginal || !NativeChestInterop.CaptureSave(__instance);
    }
    [HarmonyPatch(typeof(Container), "Save")]
    internal static class NativeChestInteropSave
    {
        private static bool Prepare() => NativeChestInterop.OwnsPatches;
        private static bool Prefix(Container __instance, bool __runOriginal) => !__runOriginal || !NativeChestInterop.CaptureSave(__instance);
    }
    [HarmonyPatch(typeof(Container), "Load")]
    internal static class NativeChestInteropLoad
    {
        private static bool Prepare() => NativeChestInterop.OwnsPatches;
        private static void Prefix(Container __instance, bool __runOriginal)
        { if (__runOriginal && ManagedChestLedger.Managed(ContainerAuthority.View(__instance)?.GetZDO())) NativeChestInterop.CaptureSave(__instance); }
        private static void Postfix(Container __instance, bool __result)
        { if (__result) NativeChestInterop.Loaded(__instance); }
    }
}
