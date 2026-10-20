using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // Receipts accompany item changes in ordinary character saves; inventory actions do not
    // force profile writes. Replies never replace the player's whole inventory with a snapshot.
    internal static class CharacterOperationEscrow
    {
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.character.v2", () => new Session().Routes());
        internal static bool Busy => ((Func<bool>)Routes["busy"])();
        internal static string Status => ((Func<string>)Routes["status"])();
        internal static Tuple<int, int> CompletedItemCounts => ((Func<Tuple<int, int>>)Routes["completed-counts"])();
        internal static void Tick() => ((Action)Routes["tick"])();
        // A native action becoming ready may wake its own deferred processor. Delivery
        // still runs through the next normal Tick and existing action validation.
        internal static bool WakeDelivery(string kind, byte[] request) =>
            ((Func<string, byte[], bool>)Routes["wake-delivery"])(kind, request);
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static void RegisterDeliveryProcessor(string kind, Func<Player, byte[], Inventory, bool> process) =>
            ((Action<string, Func<Player, byte[], Inventory, bool>>)Routes["processor"])(kind, process);
        internal static bool Begin(string kind, Func<byte[], byte[]> payload, Tuple<ItemDrop.ItemData, int>[] items,
            Action<bool, string> completed) =>
            ((Func<string, Func<byte[], byte[]>, Tuple<ItemDrop.ItemData, int>[], Action<bool, string>, bool>)Routes["begin"])(kind, payload, items, completed);

        private enum Stage { Prepared, Returning, Applied }
        private sealed class Receipt
        {
            internal string World, Id, Kind, Reason;
            internal byte[] Request, Escrow, Delivery;
            // Packed escrow coordinates are wire-local, never character inventory coordinates.
            // Null means a legacy receipt whose original character slots were not recorded.
            internal Vector2i[] Origins;
            internal Stage Stage;
            internal bool Accepted, Processed;
            internal string Encode()
            {
                var packet = new ZPackage(); packet.Write(4); packet.Write(World); packet.Write(Id); packet.Write(Kind);
                packet.Write((int)Stage); packet.Write(Accepted); packet.Write(Reason ?? "");
                packet.Write(Processed);
                packet.Write(Request); packet.Write(Escrow); packet.Write(Delivery ?? Array.Empty<byte>());
                packet.Write(Origins?.Length ?? -1);
                if (Origins != null) foreach (var origin in Origins) { packet.Write(origin.x); packet.Write(origin.y); }
                return Convert.ToBase64String(packet.GetArray());
            }
            internal static Receipt Decode(string value)
            {
                if (value.Length > 2 * 1024 * 1024) throw new InvalidDataException("Character receipt too large");
                var packet = new ZPackage(Convert.FromBase64String(value));
                int version = packet.ReadInt();
                if (version < 2 || version > 4) throw new InvalidDataException("Unsupported character receipt");
                var receipt = new Receipt { World = packet.ReadString(), Id = packet.ReadString(), Kind = packet.ReadString(),
                    Stage = (Stage)packet.ReadInt(), Accepted = packet.ReadBool(), Reason = packet.ReadString(), Processed = version >= 3 && packet.ReadBool(),
                    Request = OperationPacket.Bytes(packet), Escrow = OperationPacket.Bytes(packet), Delivery = OperationPacket.Bytes(packet) };
                if (version >= 4)
                {
                    int count = packet.ReadInt();
                    if (count < -1 || count > NativeInventoryCodec.MaximumItems) throw new InvalidDataException("Invalid character origin count");
                    if (count >= 0)
                    {
                        receipt.Origins = new Vector2i[count]; var occupied = new HashSet<Vector2i>();
                        for (int n = 0; n < count; n++)
                        {
                            var origin = new Vector2i(packet.ReadInt(), packet.ReadInt());
                            if (origin.x < 0 || origin.y < 0 || origin.x > byte.MaxValue || origin.y > byte.MaxValue || !occupied.Add(origin))
                                throw new InvalidDataException("Invalid character origin slot");
                            receipt.Origins[n] = origin;
                        }
                    }
                }
                OperationPacket.End(packet);
                if (!Enum.IsDefined(typeof(Stage), receipt.Stage) || !Guid.TryParseExact(receipt.Id, "N", out _) ||
                    receipt.World.Length > 128 || receipt.Kind.Length > 128 || receipt.Reason.Length > 4096 ||
                    receipt.Request.Length > 65536 || receipt.Escrow.Length > 65536 || receipt.Delivery.Length > 65536)
                    throw new InvalidDataException("Invalid character receipt");
                return receipt;
            }
        }
        private sealed class Session
        {
            private const string PendingKey = "runic.operations.deliveries.v1";
            private Player player;
            private Receipt receipt;
            private Receipt lastCompleted;
            private readonly List<Receipt> pending = new();
            private readonly Dictionary<string, Action<bool, string>> pendingCallbacks = new();
            private string status = "";
            private bool finished, corrupt;
            private float retry;
            private Action<bool, string> completed;
            private readonly Dictionary<string, Func<Player, byte[], Inventory, bool>> processors = new(StringComparer.Ordinal);
            internal Dictionary<string, Delegate> Routes() => new Dictionary<string, Delegate>
            {
                ["busy"] = new Func<bool>(() => { Load(); return corrupt || (receipt != null && !finished); }),
                ["status"] = new Func<string>(() => status),
                ["completed-counts"] = new Func<Tuple<int, int>>(CompletedItemCounts),
                ["tick"] = new Action(Tick),
                ["wake-delivery"] = new Func<string, byte[], bool>((kind, request) =>
                {
                    Load();
                    if (player == null || receipt == null || corrupt || finished || receipt.Stage != Stage.Returning ||
                        receipt.Processed || receipt.Kind != kind || request == null || !OwnerOperationQueue.Same(receipt.Request, request)) return false;
                    retry = 0; return true;
                }),
                ["clear"] = new Action(() => { player = null; receipt = null; lastCompleted = null; pending.Clear(); pendingCallbacks.Clear(); completed = null; finished = false; corrupt = false; retry = 0; status = ""; }),
                ["begin"] = new Func<string, Func<byte[], byte[]>, Tuple<ItemDrop.ItemData, int>[], Action<bool, string>, bool>(Begin),
                ["processor"] = new Action<string, Func<Player, byte[], Inventory, bool>>((kind, process) => processors[kind] = process)
            };
            private Tuple<int, int> CompletedItemCounts()
            {
                try
                {
                    Load();
                    var done = lastCompleted ?? (finished ? receipt : null);
                    if (player == null || corrupt || done == null || done.Stage != Stage.Applied) return null;
                    return Tuple.Create(NativeInventoryCodec.Load(done.Escrow, 32, 32).GetAllItems().Sum(x => x.m_stack),
                        NativeInventoryCodec.Load(done.Delivery, 32, 32).GetAllItems().Sum(x => x.m_stack));
                }
                catch { return null; } // Feedback must never affect the completed inventory transaction.
            }
            private void Load()
            {
                if (ReferenceEquals(player, Player.m_localPlayer)) return;
                player = Player.m_localPlayer; receipt = null; lastCompleted = null; pending.Clear(); pendingCallbacks.Clear(); completed = null; finished = false; corrupt = false; retry = 0;
                if (player == null) return;
                string saved = CharacterOperationCheckpoint.Receipt(player);
                try
                {
                    if (player.m_customData.TryGetValue(PendingKey, out string waiting))
                    {
                        var packet = new ZPackage(Convert.FromBase64String(waiting));
                        if (packet.ReadInt() != 1) throw new InvalidDataException("Unsupported pending deliveries");
                        int count = packet.ReadInt();
                        if (count < 0 || count > 4096) throw new InvalidDataException("Invalid pending delivery count");
                        for (int n = 0; n < count; n++)
                        {
                            var held = Receipt.Decode(packet.ReadString());
                            if (held.Stage != Stage.Returning || pending.Any(p => p.Id == held.Id)) throw new InvalidDataException("Invalid pending delivery");
                            pending.Add(held);
                        }
                        OperationPacket.End(packet);
                    }
                    if (saved.Length == 0) return;
                    receipt = Receipt.Decode(saved);
                    // Recovery resumes whatever receipt the last ordinary character save captured.
                    finished = receipt.Stage == Stage.Applied;
                    status = finished ? "" : "Recovering pending inventory operation";
                }
                catch (Exception error) { corrupt = true; status = "Character operation receipt needs recovery"; MutationGate.Report(status + ": " + error); }
            }
            private bool Begin(string kind, Func<byte[], byte[]> buildPayload, Tuple<ItemDrop.ItemData, int>[] items, Action<bool, string> callback)
            {
                Load();
                if (player == null || player.IsDead() || corrupt || (receipt != null && !finished) || !InventoryOperationNetwork.Ready ||
                    buildPayload == null || items == null || items.Length > NativeInventoryCodec.MaximumItems) return false;
                Inventory inventory = player.GetInventory();
                var escrow = new Inventory("Runic pending transfer", null, 32, 32);
                var unique = new HashSet<ItemDrop.ItemData>();
                var origins = new Vector2i[items.Length]; var occupied = new HashSet<Vector2i>();
                for (int n = 0; n < items.Length; n++)
                {
                    var item = items[n]?.Item1; int amount = items[n]?.Item2 ?? 0;
                    if (item == null || item.m_equipped || amount < 1 || amount > item.m_stack || !inventory.ContainsItem(item) || !unique.Add(item)) return false;
                    if (item.m_gridPos.x < 0 || item.m_gridPos.y < 0 || item.m_gridPos.x >= inventory.GetWidth() || item.m_gridPos.y >= inventory.GetHeight() ||
                        item.m_gridPos.x > byte.MaxValue || item.m_gridPos.y > byte.MaxValue || !occupied.Add(item.m_gridPos)) return false;
                    origins[n] = item.m_gridPos;
                    var held = item.Clone(); held.m_stack = amount; held.m_equipped = false; held.m_gridPos = new Vector2i(n % 32, n / 32);
                    escrow.GetAllItems().Add(held);
                }
                byte[] savedEscrow = NativeInventoryCodec.Save(escrow);
                if (savedEscrow.Length > 65536) return false;
                byte[] payload = buildPayload(savedEscrow);
                if (payload == null || payload.Length > 65536) return false;
                var next = new Receipt { World = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), Id = Guid.NewGuid().ToString("N"),
                    Kind = kind, Request = payload, Escrow = savedEscrow, Origins = origins, Stage = Stage.Prepared, Reason = "" };
                string encoded = next.Encode(); // Validate and allocate before touching live items.
                foreach (var take in items)
                {
                    take.Item1.m_stack -= take.Item2;
                    if (take.Item1.m_stack == 0) inventory.GetAllItems().Remove(take.Item1);
                }
                receipt = next; completed = callback; finished = false; retry = 0;
                CharacterOperationCheckpoint.SetReceipt(player, encoded);
                Notify(inventory);
                Tick(); return true;
            }
            private void Tick()
            {
                Load();
                if (player == null || corrupt || Time.realtimeSinceStartup < retry) return;
                if (player.IsDead()) { status = "Pending inventory delivery will resume after respawn"; return; }
                if (receipt == null || finished) { DrainPending(); return; }
                retry = Time.realtimeSinceStartup + 2;
                // Native autosaves, manual saves and logout persist receipt metadata along with
                // the inventory. Per-action profile saves would stall normal crafting/transfers.
                if (receipt.Stage == Stage.Applied) { Finish(); return; }
                if (ZNet.instance == null || receipt.World != ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture))
                { status = "Return to the original world to reconcile the pending transfer"; return; }
                if (receipt.Stage == Stage.Prepared)
                {
                    status = "Waiting for inventory operation";
                    string expectedId = receipt.Id;
                    InventoryOperationNetwork.Send(receipt.World, receipt.Id, receipt.Kind, receipt.Request,
                        (code, reason, response) => Receive(expectedId, code, reason, response));
                }
                else Deliver();
            }
            private void Receive(string id, int code, string reason, byte[] payload)
            {
                if (player == null || receipt == null || receipt.Id != id || receipt.Stage != Stage.Prepared) return;
                if (code != (int)OperationStatus.Committed && code != (int)OperationStatus.Denied)
                {
                    status = "Inventory operation requires reconciliation: " + reason;
                    // An id conflict is not a rejection proving the original operation did not commit.
                    // Retain escrow rather than automatically refunding and duplicating its value.
                    corrupt = true; return;
                }
                byte[] delivery = code == (int)OperationStatus.Committed ? payload : receipt.Escrow;
                try { NativeInventoryCodec.Load(delivery, 32, 32); }
                catch (Exception error) { status = "Inventory delivery codec is incompatible"; MutationGate.Report(status + ": " + error); return; }
                receipt.Accepted = code == (int)OperationStatus.Committed; receipt.Reason = reason;
                receipt.Delivery = delivery; receipt.Stage = Stage.Returning;
                // An ordinary save can capture this replayable result while it waits for space
                // or a native crafting action. Receipt updates themselves do not force a save.
                CharacterOperationCheckpoint.SetReceipt(player, receipt.Encode()); retry = 0;
            }
            private void Deliver()
            {
                Inventory delivery = NativeInventoryCodec.Load(receipt.Delivery, 32, 32);
                Inventory live = player.GetInventory();
                bool processedNow = false;
                if (receipt.Accepted && !receipt.Processed && processors.TryGetValue(receipt.Kind, out var process))
                {
                    // A native craft can consume reserved materials without putting them into bag
                    // slots first. Its result and the processed receipt share one character save.
                    var before = new NativeInventoryMemoryCheckpoint(live);
                    Action changed = live.m_onChanged; live.m_onChanged = null;
                    try
                    {
                        if (!process(player, receipt.Request, delivery))
                        {
                            before.Restore(); status = "Waiting to complete the pending action"; return;
                        }
                        receipt.Delivery = NativeInventoryCodec.Save(delivery);
                        receipt.Processed = true; processedNow = true;
                        CharacterOperationCheckpoint.SetReceipt(player, receipt.Encode());
                    }
                    catch (Exception error)
                    {
                        try { before.Restore(); }
                        catch (Exception restoreError) { MutationGate.Report("Action rollback failed: " + restoreError); }
                        corrupt = true; status = "Pending action requires reconciliation";
                        MutationGate.Report(status + ": " + error); return;
                    }
                    finally { live.m_onChanged = changed; }
                    // Keep the processed receipt in memory before applying any leftovers so
                    // ordinary character saves include its already-completed effects.
                }
                Inventory shadow = NativeInventoryCodec.Clone(live);
                if (!AddDelivery(shadow, delivery, receipt))
                {
                    ParkPending();
                    if (processedNow) Notify(live);
                    return;
                }
                // The capacity check and application run together on Unity's game thread. Suppress
                // callbacks until all items and the receipt are in the same consistent state.
                var checkpoint = new NativeInventoryMemoryCheckpoint(live);
                Action callbacks = live.m_onChanged;
                live.m_onChanged = null;
                bool capacityChanged = false;
                try
                {
                    if (!AddDelivery(live, delivery, receipt))
                    {
                        // A mod may accept a detached capacity preview but refuse the
                        // actual player slot. Restore it and wait without locking deposits.
                        checkpoint.Restore(); capacityChanged = true;
                    }
                    else
                    {
                        receipt.Stage = Stage.Applied;
                        CharacterOperationCheckpoint.SetReceipt(player, receipt.Encode());
                    }
                }
                catch (Exception error)
                {
                    try { checkpoint.Restore(); }
                    catch (Exception restoreError) { MutationGate.Report("Inventory delivery rollback failed: " + restoreError); }
                    corrupt = true; status = "Inventory delivery requires reconciliation";
                    MutationGate.Report(status + ": " + error); return;
                }
                finally { live.m_onChanged = callbacks; }
                if (capacityChanged) { ParkPending(); if (processedNow) Notify(live); return; }
                Notify(live); retry = 0; Finish();
            }
            private void ParkPending()
            {
                // A completed withdrawal waiting for bag space is not an in-flight
                // mutation. Keep its items with the character without locking deposits.
                pending.Add(receipt);
                if (completed != null)
                {
                    var held = receipt; var originalDelivery = receipt.Delivery; var callback = completed;
                    pendingCallbacks[receipt.Id] = (accepted, reason) =>
                    {
                        // This receipt has left pending storage before its callback runs.
                        // Feedback must count the whole return, not its last drained batch.
                        held.Delivery = originalDelivery; callback(accepted, reason);
                    };
                }
                PersistPending();
                CharacterOperationCheckpoint.SetReceipt(player, "");
                receipt = null; completed = null; finished = false; retry = 0;
                status = "Items are waiting for inventory space. You can deposit other items to make room.";
                player.Message(MessageHud.MessageType.TopLeft, status);
            }
            private static bool AddDelivery(Inventory destination, Inventory delivery, Receipt pending)
            {
                if (pending.Origins == null || pending.Origins.Length == 0) return NativeInventoryCodec.AddAll(destination, delivery);
                var original = NativeInventoryCodec.Load(pending.Escrow, 32, 32);
                var remaining = NativeInventoryCodec.Clone(delivery);
                // Reserve every available original slot before general packing can occupy one.
                // A chest withdrawal has no original offered stack and receives no slot preference.
                foreach (var item in remaining.GetAllItems().ToArray())
                {
                    int index = item.m_gridPos.y * 32 + item.m_gridPos.x;
                    if (item.m_gridPos.x >= 32 || index < 0 || index >= pending.Origins.Length) continue;
                    var offered = original.GetItemAt(item.m_gridPos.x, item.m_gridPos.y);
                    if (offered == null || NativeInventoryCodec.ItemIdentity(offered) != NativeInventoryCodec.ItemIdentity(item)) continue;
                    Vector2i origin = pending.Origins[index];
                    if (origin.x < 0 || origin.y < 0 || origin.x >= destination.GetWidth() || origin.y >= destination.GetHeight()) continue;
                    var existing = destination.GetItemAt(origin.x, origin.y);
                    int amount = Math.Min(item.m_stack, offered.m_stack);
                    if (existing == null)
                    {
                        var restored = item.Clone(); restored.m_stack = amount; restored.m_equipped = false; restored.m_gridPos = origin;
                        destination.GetAllItems().Add(restored);
                    }
                    else
                    {
                        if (NativeInventoryCodec.ItemIdentity(existing) != NativeInventoryCodec.ItemIdentity(item)) continue;
                        amount = Math.Min(amount, Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack));
                        if (amount == 0) continue;
                        existing.m_stack += amount;
                    }
                    item.m_stack -= amount;
                    if (item.m_stack == 0) remaining.GetAllItems().Remove(item);
                }
                return NativeInventoryCodec.AddAll(destination, remaining);
            }
            private void Finish()
            {
                lastCompleted = receipt;
                finished = true; status = "";
                var callback = completed; completed = null;
                callback?.Invoke(receipt.Accepted, receipt.Reason);
            }
            private void PersistPending()
            {
                if (pending.Count == 0) { player.m_customData.Remove(PendingKey); return; }
                var packet = new ZPackage(); packet.Write(1); packet.Write(pending.Count);
                foreach (var held in pending) packet.Write(held.Encode());
                player.m_customData[PendingKey] = Convert.ToBase64String(packet.GetArray());
            }
            private void DrainPending()
            {
                retry = Time.realtimeSinceStartup + .1f;
                if (pending.Count == 0 || ZNet.instance == null) return;
                string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
                var live = player.GetInventory(); bool changed = false;
                var callbacks = new List<Action>();
                Action notify = live.m_onChanged; live.m_onChanged = null;
                try
                {
                    foreach (var held in pending.Where(p => p.World == world).ToArray())
                    {
                        var remaining = NativeInventoryCodec.Load(held.Delivery, 32, 32);
                        bool moved = false;
                        foreach (var item in remaining.GetAllItems().ToArray())
                        {
                            int amount = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(live, item));
                            if (amount == 0) continue;
                            var checkpoint = new NativeInventoryMemoryCheckpoint(live);
                            var copy = item.Clone(); copy.m_stack = amount; copy.m_equipped = false;
                            try
                            {
                                if (!NativeInventoryCodec.AddExact(live, copy)) { checkpoint.Restore(); continue; }
                            }
                            catch { checkpoint.Restore(); throw; }
                            item.m_stack -= amount;
                            if (item.m_stack == 0) remaining.GetAllItems().Remove(item);
                            held.Delivery = NativeInventoryCodec.Save(remaining);
                            PersistPending();
                            moved = changed = true;
                        }
                        if (!moved && remaining.NrOfItems() != 0) continue;
                        if (remaining.NrOfItems() == 0)
                        {
                            pending.Remove(held); held.Stage = Stage.Applied;
                            if (pendingCallbacks.TryGetValue(held.Id, out var callback))
                            {
                                pendingCallbacks.Remove(held.Id);
                                callbacks.Add(() => { lastCompleted = held; callback(held.Accepted, held.Reason); });
                            }
                        }
                        else held.Delivery = NativeInventoryCodec.Save(remaining);
                        // The remaining items and changed bag are saved together by the
                        // ordinary character save. No new chest debit or forced save occurs.
                        PersistPending();
                    }
                }
                finally { live.m_onChanged = notify; }
                if (changed) Notify(live);
                if (pending.Count == 0) status = "";
                foreach (var callback in callbacks) callback();
            }
            private static void Notify(Inventory inventory)
            {
                try { AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }).Invoke(inventory, new object[] { false, false }); }
                catch (Exception error) { MutationGate.Report("inventory callback after operation: " + error.GetType().Name); }
            }
        }
    }
}
