using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RunicAutomation.Operations
{
    // A retired record can retain undelivered contents. Each existing authority
    // transaction moves at most 127 native stacks into ground-item records and
    // debits those exact contents together. No character save or world save waits.
    internal static class RetiredChestContents
    {
        private const string Kind = "inventory.recover-retired-contents.v1";
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.retired-contents.v1", () => new Recovery().Routes());
        internal static void Tick() => ((Action)Routes["tick"])();
        internal static void Clear() => ((Action)Routes["clear"])();
        internal static void Enqueue(ManagedChestLedger.Image body)
        {
            // A schema-109 empty inventory is six bytes. Other native/drawer
            // representations are decoded by the planner rather than guessed here.
            if (body.Retired && body.Items.Length > 6 && NativeInventoryCodec.Load(body.Items, body.Width, body.Height).NrOfItems() > 0)
                ((Action<string>)Routes["enqueue"])(body.Token);
        }

        private sealed class Recovery
        {
            private readonly HashSet<string> pending = new(), inFlight = new();
            private bool registered, indexed;
            internal Dictionary<string, Delegate> Routes() => new()
            {
                ["tick"] = new Action(Tick),
                ["clear"] = new Action(() => { pending.Clear(); inFlight.Clear(); indexed = false; }),
                ["enqueue"] = new Action<string>(token => pending.Add(token))
            };
            private void Tick()
            {
                if (!InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer() || !SharedOperationHost.Active) return;
                if (!registered)
                {
                    SharedOperationHost.Register(Kind, (actor, _) => actor == "world-destruction" ? null : "internal-world-operation-only", Plan);
                    registered = true;
                }
                if (!indexed)
                {
                    foreach (var entry in SharedOperationHost.Enumerate("chest:"))
                        Enqueue(ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(entry.Item2)));
                    indexed = true;
                }
                foreach (string token in pending.Where(t => !inFlight.Contains(t)).Take(8).ToArray())
                {
                    byte[] before = SharedOperationHost.Read("chest:" + token);
                    if (before == null) { pending.Remove(token); continue; }
                    long revision = FileOperationEndpoints.Revision(before);
                    var packet = new ZPackage(); packet.Write(token);
                    inFlight.Add(token);
                    void Finish(int status, string reason, byte[] response)
                    {
                        if (status == (int)OperationStatus.Committed)
                        {
                            inFlight.Remove(token); pending.Remove(token);
                            var current = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(SharedOperationHost.Read("chest:" + token)));
                            Enqueue(current);
                        }
                        else if (status == (int)OperationStatus.Denied || status == (int)OperationStatus.IdConflict)
                            MutationGate.Report("Retired chest contents retained for recovery: " + reason);
                        else inFlight.Remove(token);
                    }
                    var result = SharedOperationHost.Submit(ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), "world-destruction",
                        "recover-contents:" + token + ":" + revision.ToString(CultureInfo.InvariantCulture), Kind, packet.GetArray(), Finish);
                    if (result.Item1 != (int)OperationStatus.Queued) Finish(result.Item1, result.Item2, result.Item3);
                }
            }
            private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] payload, Func<string, byte[]> read)
            {
                var packet = new ZPackage(payload); string token = packet.ReadString(); OperationPacket.End(packet);
                if (!Guid.TryParseExact(token, "N", out _)) throw new OperationRejectedException("invalid-chest-identity");
                string endpoint = "chest:" + token;
                byte[] before = read(endpoint) ?? throw new OperationRejectedException("retired-chest-ledger-missing");
                var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
                if (!body.Retired) throw new OperationRejectedException("chest-is-not-retired");
                var inventory = ManagedChestLedger.Inventory(before);
                var changes = new List<Tuple<string, byte[], byte[]>>();
                long revision = FileOperationEndpoints.Revision(before);
                foreach (var item in inventory.GetAllItems().ToArray())
                {
                    while (item.m_stack > 0 && changes.Count < 127)
                    {
                        // Drawers expand their inventory's stack limit; ground items
                        // must use the actual item's native stack limit instead.
                        var native = item.m_dropPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                        int amount = Math.Min(item.m_stack, Math.Min(ushort.MaxValue, native.m_maxStackSize));
                        if (amount <= 0) throw new OperationRejectedException("invalid-recovery-stack-size");
                        var drop = item.Clone(); drop.m_shared = native; drop.m_stack = amount;
                        string identity = OperationRequest.Hash("retired-contents:" + token + ":" + revision.ToString(CultureInfo.InvariantCulture) + ":" + changes.Count.ToString(CultureInfo.InvariantCulture)).Substring(0, 32).ToLowerInvariant();
                        changes.Add(ManagedWorldDrops.Create(identity, body.Position + Vector3.up * .35f, drop));
                        if (!inventory.RemoveItem(item, amount)) throw new InvalidOperationException("Retired inventory changed during recovery");
                        if (!inventory.GetAllItems().Contains(item)) break;
                    }
                    if (changes.Count == 127) break;
                }
                if (changes.Count == 0) return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
                body.Items = NativeInventoryCodec.Save(inventory);
                changes.Add(Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, body.Encode())));
                return Tuple.Create(changes.ToArray(), Array.Empty<byte>());
            }
        }
    }
}
