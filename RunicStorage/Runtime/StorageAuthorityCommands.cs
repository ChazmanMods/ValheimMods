using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicAutomation.Operations;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal enum AuthorityStorageAction { Deposit, Withdraw, TakeAll, QuickStack, Restock, Sort, SetRules, Inspect, StackMatching, MoveSlot, PreviewOrganize, Organize, ExchangeWithdraw }

internal sealed class StorageAuthorityRequest
{
    internal AuthorityStorageAction Action;
    internal ZDOID Chest;
    internal Vector2i Slot;
    internal Vector2i Target = new(-1, -1);
    internal string TargetIdentity = "";
    internal string Identity = "", Rules = "", ExpectedRules = "";
    internal int Amount;
    internal readonly Dictionary<string, int> Desired = new(StringComparer.Ordinal);
    internal byte[] Escrow;
    internal byte[] Receiver;
    internal int ReceiverWidth, ReceiverHeight;
    internal int ReceiverEmptySlots = -1;
    internal readonly Dictionary<string, int> ReceiverCapacity = new(StringComparer.Ordinal);
    internal byte[] Encode()
    {
        var packet = new ZPackage(); packet.Write(5); packet.Write((int)Action); packet.Write(Chest);
        packet.Write(Slot.x); packet.Write(Slot.y); packet.Write(Identity); packet.Write(Amount);
        packet.Write(Target.x); packet.Write(Target.y); packet.Write(TargetIdentity);
        packet.Write(Rules); packet.Write(ExpectedRules); packet.Write(Desired.Count);
        foreach (var pair in Desired.OrderBy(x => x.Key, StringComparer.Ordinal)) { packet.Write(pair.Key); packet.Write(pair.Value); }
        packet.Write(Escrow); packet.Write(ReceiverWidth); packet.Write(ReceiverHeight);
        packet.Write(Receiver ?? Array.Empty<byte>()); packet.Write(ReceiverEmptySlots);
        packet.Write(ReceiverCapacity.Count);
        foreach (var pair in ReceiverCapacity.OrderBy(x => x.Key, StringComparer.Ordinal))
        { packet.Write(pair.Key); packet.Write(pair.Value); }
        return packet.GetArray();
    }
    internal static StorageAuthorityRequest Decode(byte[] bytes)
    {
        var packet = new ZPackage(bytes);
        int version = packet.ReadInt();
        if (version < 3 || version > 5) throw new InvalidDataException("Unsupported storage request");
        var request = new StorageAuthorityRequest { Action = (AuthorityStorageAction)packet.ReadInt(), Chest = packet.ReadZDOID(),
            Slot = new Vector2i(packet.ReadInt(), packet.ReadInt()), Identity = packet.ReadString(), Amount = packet.ReadInt(),
            Target = new Vector2i(packet.ReadInt(), packet.ReadInt()), TargetIdentity = packet.ReadString(),
            Rules = packet.ReadString(), ExpectedRules = packet.ReadString() };
        int count = packet.ReadInt();
        if (!Enum.IsDefined(typeof(AuthorityStorageAction), request.Action) || count < 0 || count > 128 ||
            request.Identity.Length > 128 || request.TargetIdentity.Length > 128 || request.Rules.Length > 16384 || request.ExpectedRules.Length > 16384 || request.Amount < 0)
            throw new InvalidDataException("Invalid storage request");
        for (int n = 0; n < count; n++)
        {
            string id = packet.ReadString(); int amount = packet.ReadInt();
            if (id.Length == 0 || id.Length > 128 || amount < 1 || request.Desired.ContainsKey(id))
                throw new InvalidDataException("Invalid restock request");
            request.Desired.Add(id, amount);
        }
        request.Escrow = OperationPacket.Bytes(packet);
        if (version >= 4)
        {
            request.ReceiverWidth = packet.ReadInt(); request.ReceiverHeight = packet.ReadInt();
            request.Receiver = OperationPacket.Bytes(packet);
        }
        if (version >= 5)
        {
            request.ReceiverEmptySlots = packet.ReadInt();
            int capacities = packet.ReadInt();
            if (request.ReceiverEmptySlots < -1 || capacities < 0 || capacities > ushort.MaxValue)
                throw new InvalidDataException("Invalid receiving capacity");
            for (int n = 0; n < capacities; n++)
            {
                string identity = packet.ReadString(); int capacity = packet.ReadInt();
                if (identity.Length == 0 || identity.Length > 128 || capacity < 0 || request.ReceiverCapacity.ContainsKey(identity))
                    throw new InvalidDataException("Invalid receiving capacity");
                request.ReceiverCapacity.Add(identity, capacity);
            }
        }
        OperationPacket.End(packet); return request;
    }
}

internal static partial class StorageAuthorityCommands
{
    internal const string Kind = "storage.operation.v2";
    internal const string QueryKind = "storage.query.v2";
    private static bool Organizing(AuthorityStorageAction action) => action == AuthorityStorageAction.PreviewOrganize || action == AuthorityStorageAction.Organize;
    private sealed class Chest
    {
        internal ZDO Zdo;
        internal byte[] Before;
        internal Inventory Inventory;
        internal ChestRules Rules;
        internal ProductionChestPolicy Production;
            internal string OriginalRules;
    }
    private sealed class QuickStackSummary
    {
        internal int Candidates, AccessDenied, DrawerFiltered;
    }
    internal static void Register()
    {
        SharedOperationHost.Register(Kind, Authorize, Plan);
        CharacterOperationEscrow.RegisterDeliveryProcessor(Kind, MultiAccessChestClicks.ProcessDelivery);
        SharedOperationHost.RegisterQuery(QueryKind, (actor, bytes) => {
            try
            {
                var action = StorageAuthorityRequest.Decode(bytes).Action;
                return action != AuthorityStorageAction.Inspect && action != AuthorityStorageAction.PreviewOrganize
                    ? "invalid-read-only-action" : Authorize(actor, bytes);
            }
            catch { return "invalid-storage-query"; }
        }, Plan);
    }
    private static string Authorize(string actor, byte[] payload)
    {
        try
        {
            var request = StorageAuthorityRequest.Decode(payload);
            if (!(PluginConfig.Enabled?.Value ?? false) && request.Action != AuthorityStorageAction.Inspect &&
                request.Action != AuthorityStorageAction.Deposit && request.Action != AuthorityStorageAction.Withdraw &&
                request.Action != AuthorityStorageAction.TakeAll && request.Action != AuthorityStorageAction.StackMatching &&
                request.Action != AuthorityStorageAction.MoveSlot && request.Action != AuthorityStorageAction.ExchangeWithdraw) return "storage-disabled";
            if (Organizing(request.Action) && !SmartSortOrganizer.AuthorityEnabled()) return "organize-disabled-on-server";
            if (!NetworkInventoryAccess.TryPosition(actor, out _, out _)) return "actor-unavailable";
            if (request.Action == AuthorityStorageAction.QuickStack || request.Action == AuthorityStorageAction.Restock || Organizing(request.Action)) return null;
            var chest = ZDOMan.instance.GetZDO(request.Chest);
            return NetworkInventoryAccess.CanAccess(actor, chest, 5f, false, false, out string reason) ? null : reason;
        }
        catch (Exception) { return "invalid-storage-request"; }
    }
    private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] payload, Func<string, byte[]> read)
    {
        var request = StorageAuthorityRequest.Decode(payload);
        Inventory delivery;
        try { delivery = NativeInventoryCodec.Load(request.Escrow, 32, 32); }
        catch (Exception) { throw new OperationRejectedException("unsupported-carried-items"); }
        if (request.Action != AuthorityStorageAction.Deposit && request.Action != AuthorityStorageAction.QuickStack && request.Action != AuthorityStorageAction.StackMatching && request.Action != AuthorityStorageAction.ExchangeWithdraw && delivery.NrOfItems() != 0)
            throw new OperationRejectedException("unexpected-carried-items");
        // This snapshot constrains capacity only. Its items never become a delivery or
        // overwrite a player's inventory; the client applies to its then-current bag.
        Inventory receiver = request.Receiver?.Length > 0
            ? NativeInventoryCodec.Load(request.Receiver, request.ReceiverWidth, request.ReceiverHeight) : null;
        int initialReceiverStacks = receiver?.NrOfItems() ?? 0;
        var received = new Dictionary<string, int>(StringComparer.Ordinal);
        int Fit(ItemDrop.ItemData item, int wanted)
        {
            int amount = Math.Min(wanted, NativeInventoryCodec.CapacityFor(delivery, item));
            if (receiver == null) return amount; // Saved requests from older releases.
            amount = Math.Min(amount, NativeInventoryCodec.CapacityFor(receiver, item));
            if (request.ReceiverEmptySlots < 0) return amount;
            // Detached inventories do not inherit patches restricted to the real player
            // instance (e.g. reserved equipment cells). Budget native available slots and
            // native per-item limits captured on that instance, never its raw rectangle.
            string identity = NativeInventoryCodec.ItemIdentity(item);
            if (!request.ReceiverCapacity.TryGetValue(identity, out int capacity)) return 0;
            received.TryGetValue(identity, out int already);
            int empty = Math.Max(0, request.ReceiverEmptySlots - (receiver.NrOfItems() - initialReceiverStacks));
            long space = (long)empty * Math.Max(0, item.m_shared.m_maxStackSize);
            string stackIdentity = NativeInventoryCodec.StackIdentity(item);
            foreach (var stack in receiver.GetAllItems())
                if (NativeInventoryCodec.StackIdentity(stack) == stackIdentity)
                    space += Math.Max(0, stack.m_shared.m_maxStackSize - stack.m_stack);
            return (int)Math.Min(amount, Math.Min(Math.Max(0, capacity - already), space));
        }
        void Receive(ItemDrop.ItemData item, int amount)
        {
            if (receiver == null) return;
            var copy = item.Clone(); copy.m_stack = amount;
            if (!NativeInventoryCodec.AddExact(receiver, copy)) throw new OperationRejectedException("inventory-full");
            string identity = NativeInventoryCodec.ItemIdentity(item);
            received.TryGetValue(identity, out int already); received[identity] = checked(already + amount);
        }
        var quickStack = request.Action == AuthorityStorageAction.QuickStack ? new QuickStackSummary() : null;
        var chests = Resolve(actor, request, quickStack);
        if (Organizing(request.Action)) return PlanOrganize(request, chests);
        if (request.Action == AuthorityStorageAction.Inspect)
        {
            var snapshot = new ZPackage(); snapshot.Write(chests[0].Inventory.GetWidth()); snapshot.Write(chests[0].Inventory.GetHeight());
            snapshot.Write(NativeInventoryCodec.Save(chests[0].Inventory));
            return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), snapshot.GetArray());
        }
        switch (request.Action)
        {
            case AuthorityStorageAction.MoveSlot:
                if (!NativeInventoryCodec.MoveSlot(chests[0].Inventory, request.Slot, request.Identity,
                    request.Amount, request.Target, request.TargetIdentity))
                    throw new OperationRejectedException("selected-stack-or-destination-changed");
                break;
            case AuthorityStorageAction.Deposit:
                if (request.Target.x >= 0 || request.Target.y >= 0) DepositAt(request, delivery, chests[0].Inventory);
                else Deposit(delivery, chests[0].Inventory, _ => true);
                break;
            case AuthorityStorageAction.StackMatching:
                Deposit(delivery, chests[0].Inventory, item => chests[0].Inventory.GetAllItems().Any(x => Resource(x) == Resource(item))); break;
            case AuthorityStorageAction.QuickStack:
                PlanQuickStack(delivery, chests, quickStack);
                break;
            case AuthorityStorageAction.Withdraw:
                var withdrawal = chests[0].Inventory.GetItemAt(request.Slot.x, request.Slot.y);
                if (withdrawal == null || withdrawal.m_stack < request.Amount || NativeInventoryCodec.ItemIdentity(withdrawal) != request.Identity)
                    throw new OperationRejectedException("selected-stack-changed");
                request.Amount = Fit(withdrawal, request.Amount);
                if (request.Amount == 0) throw new OperationRejectedException("inventory-full");
                if (!NativeInventoryCodec.Transfer(chests[0].Inventory, delivery, request.Slot, request.Identity, request.Amount))
                    throw new OperationRejectedException("selected-stack-changed");
                break;
            case AuthorityStorageAction.ExchangeWithdraw:
                var selected = chests[0].Inventory.GetItemAt(request.Slot.x, request.Slot.y);
                if (selected == null || selected.m_stack != request.Amount || NativeInventoryCodec.ItemIdentity(selected) != request.Identity || delivery.NrOfItems() != 1)
                    throw new OperationRejectedException("selected-stack-changed");
                var offered = delivery.GetAllItems()[0].Clone(); offered.m_gridPos = request.Slot;
                chests[0].Inventory.RemoveItem(selected); chests[0].Inventory.GetAllItems().Add(offered);
                delivery.GetAllItems().Clear(); selected.m_gridPos = new Vector2i(0, 0); delivery.GetAllItems().Add(selected);
                NativeInventoryCodec.Validate(chests[0].Inventory); break;
            case AuthorityStorageAction.TakeAll:
                foreach (var item in chests[0].Inventory.GetAllItems().ToArray())
                {
                    int amount = Fit(item, item.m_stack);
                    if (amount == 0) continue;
                    Receive(item, amount);
                    if (!NativeInventoryCodec.Transfer(chests[0].Inventory, delivery, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), amount))
                        throw new OperationRejectedException("selected-stack-changed");
                }
                break;
            case AuthorityStorageAction.Restock:
                foreach (var wanted in request.Desired)
                {
                    int remaining = wanted.Value;
                    foreach (var chest in chests)
                    {
                        foreach (var item in chest.Inventory.GetAllItems().Where(x => StorageActions.Matches(x, wanted.Key)).ToArray())
                        {
                            int amount = Fit(item, Math.Min(remaining, item.m_stack));
                            if (amount == 0) continue;
                            Receive(item, amount);
                            if (!NativeInventoryCodec.Transfer(chest.Inventory, delivery, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), amount))
                                throw new OperationRejectedException("restock-source-changed");
                            remaining -= amount;
                            if (remaining == 0) break;
                        }
                        if (remaining == 0) break;
                    }
                }
                break;
            case AuthorityStorageAction.Sort:
                StorageActions.ApplySortExact(chests[0].Inventory, StorageActions.ParseLockedSlots(PluginConfig.LockedContainerSlots.Value,
                    chests[0].Inventory.GetWidth(), chests[0].Inventory.GetHeight()));
                break;
            case AuthorityStorageAction.SetRules:
                if (chests[0].OriginalRules != request.ExpectedRules || !ChestRules.TryDecode(request.Rules, out var replacement))
                    throw new OperationRejectedException("chest-rules-changed");
                chests[0].Rules = replacement; break;
        }
        var changes = new List<Tuple<string, byte[], byte[]>>();
        foreach (Chest chest in chests)
        {
            if (chest.Rules.Remember) chest.Rules.Learn(chest.Inventory.GetAllItems().Select(Resource));
            var old = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(chest.Before));
            byte[] items = NativeInventoryCodec.Save(chest.Inventory); string rules = chest.Rules.Encode();
            if (OwnerOperationQueue.Same(items, old.Items) && rules == old.Rules) continue;
            changes.Add(Tuple.Create(ManagedChestLedger.Endpoint(chest.Zdo.m_uid), chest.Before, ManagedChestLedger.Next(chest.Before, chest.Inventory, rules)));
        }
        byte[] result = NativeInventoryCodec.Save(delivery);
        if (result.Length > 65536) throw new OperationRejectedException("delivery-too-large");
        return Tuple.Create(changes.ToArray(), result);
    }
    private static void PlanQuickStack(Inventory delivery, List<Chest> chests, QuickStackSummary summary)
    {
        int offered = delivery.GetAllItems().Sum(item => item.m_stack);
        int offeredStacks = delivery.NrOfItems(), matchingStacks = 0, routingPairs = 0, fullPairs = 0, incompatiblePairs = 0;
        foreach (var item in delivery.GetAllItems().ToArray())
        {
            var facts = ChestGroupRuntime.Facts(item); bool matched = false;
            foreach (var chest in chests.OrderBy(x => x.Rules.RoutingScore(facts, ContainsResource(x.Inventory, item))))
            {
                if (!delivery.ContainsItem(item)) break;
                if (chest.Rules.RoutingScore(facts, ContainsResource(chest.Inventory, item)) == int.MaxValue) continue;
                matched = true; routingPairs++;
                int amount = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(chest.Inventory, item));
                if (amount == 0)
                {
                    // A partial stack of this resource may still be unable to merge because
                    // exact quality or other persistent item metadata differs.
                    if (chest.Inventory.GetAllItems().Any(existing => Resource(existing) == Resource(item) &&
                        existing.m_stack < existing.m_shared.m_maxStackSize)) incompatiblePairs++;
                    else fullPairs++;
                    continue;
                }
                if (!NativeInventoryCodec.Transfer(delivery, chest.Inventory, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), amount))
                    throw new OperationRejectedException("inventory-plan-changed");
            }
            if (matched) matchingStacks++;
        }
        int returned = delivery.GetAllItems().Sum(item => item.m_stack), moved = offered - returned;
        string reason = moved > 0 ? "planned" : chests.Count == 0 ? "quick-stack-no-destinations" :
            routingPairs == 0 ? "quick-stack-no-routing-match" : incompatiblePairs > 0 ?
            "quick-stack-destinations-incompatible" : "quick-stack-destinations-full";
        Plugin.Log?.LogInfo($"Quick Stack authority plan: result={reason} offered={offered} moved={moved} returned={returned} " +
            $"stacks={offeredStacks} matchingStacks={matchingStacks} candidates={summary.Candidates} accessible={chests.Count} " +
            $"accessDenied={summary.AccessDenied} drawerFiltered={summary.DrawerFiltered} routingPairs={routingPairs} " +
            $"fullPairs={fullPairs} incompatiblePairs={incompatiblePairs}");
        // Reject before committing any inferred memory/rule changes. The durable denied
        // receipt returns the original escrow; a partial transfer still commits normally.
        if (moved == 0) throw new OperationRejectedException(reason);
    }
    private static List<Chest> Resolve(string actor, StorageAuthorityRequest request, QuickStackSummary quickStack = null)
    {
        NetworkInventoryAccess.TryPosition(actor, out _, out Vector3 position);
        bool nearby = request.Action == AuthorityStorageAction.QuickStack || request.Action == AuthorityStorageAction.Restock || Organizing(request.Action);
        float range = nearby ? Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f) : 5f;
        if (Organizing(request.Action)) range = Math.Min(Math.Min(range, SmartSortOrganizer.AuthorityRadius()), Mathf.Clamp(request.Amount, 2, 30));
        var candidates = nearby ? NetworkInventoryAccess.Nearby(position, range)
            .Where(x => NetworkInventoryAccess.Descriptor(x) != null).OrderBy(x => (x.GetPosition() - position).sqrMagnitude)
            .ThenBy(x => x.m_uid.ToString(), StringComparer.Ordinal).Take(Math.Max(1, Math.Min(128, PluginConfig.MaximumCandidates.Value))).ToArray() : new[] { ZDOMan.instance.GetZDO(request.Chest) };
        if (quickStack != null) quickStack.Candidates = candidates.Length;
        var production = Organizing(request.Action) ? ProductionChestPolicy.Read(candidates, position, range) : null;
        var chests = new List<Chest>(); bool waiting = false, unavailable = false;
        foreach (ZDO candidate in candidates)
        {
            ProductionChestPolicy productionPolicy = null;
            if (production != null && production.TryGetValue(candidate.m_uid, out productionPolicy) && productionPolicy.Excluded) continue;
            if (ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(candidate)) && nearby &&
                (Organizing(request.Action) || !ManagedDrawerInventory.Listed(candidate, request.Action == AuthorityStorageAction.QuickStack ? PluginConfig.QuickStackPrefabIds.Value : PluginConfig.PullPrefabIds.Value)))
            { if (quickStack != null) quickStack.DrawerFiltered++; continue; }
            if (!NetworkInventoryAccess.CanAccess(actor, candidate, range, false, false, out _))
            { if (quickStack != null) quickStack.AccessDenied++; continue; }
            byte[] before;
            try { before = ManagedChestLedger.Read(candidate); }
            catch (Exception error) when ((string)error.Data["Runic.OperationDisposition"] == "deferred")
            {
                waiting = true;
                // A live owner snapshot is an ordinary in-flight read, not an unavailable
                // chest. Retry this request when its capture arrives.
                unavailable |= error.Message != "container-enrollment-pending" && error.Message != "container-live-state-requested";
                continue;
            }
            catch (Exception error) when ((request.Action == AuthorityStorageAction.QuickStack || request.Action == AuthorityStorageAction.Restock) &&
                (error.Message == "container-native-snapshot-unavailable" || error.Message == "container-native-inventory-invalid") &&
                (string)error.Data["Runic.OperationDisposition"] == "rejected")
            {
                waiting = true; unavailable = true;
                continue;
            }
            var body = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
            if (!ChestRules.TryDecode(body.Rules, out var rules)) throw new OperationRejectedException("invalid-chest-rules");
            Inventory inventory;
            try { inventory = NativeInventoryCodec.Load(body.Items, body.Width, body.Height); }
            catch (Exception) { throw new OperationRejectedException("unsupported-chest-items"); }
            if (rules.Remember) rules.Learn(inventory.GetAllItems().Select(Resource));
            chests.Add(new Chest { Zdo = candidate, Before = before, Inventory = inventory, Rules = rules, OriginalRules = body.Rules, Production = productionPolicy });
        }
        if (waiting && (chests.Count == 0 || !nearby || Organizing(request.Action)))
        {
            if (unavailable) throw new OperationRejectedException("chest-unavailable");
            throw new OperationDeferredException("container-live-state-requested");
        }
        // Quick Stack and Restock use the ready destinations now. An unrelated chest
        // still enrolling cannot block their whole plan or the character's next action.
        if (!nearby && chests.Count != 1) throw new OperationRejectedException("chest-access-changed");
        return chests;
    }
    private static string Resource(ItemDrop.ItemData item) => ValheimContainerIdentity.ResourceId(item);
    private static bool ContainsResource(Inventory inventory, ItemDrop.ItemData item) =>
        ManagedDrawerInventory.Assignment(inventory) == item.m_dropPrefab?.name || inventory.GetAllItems().Any(i => Resource(i) == Resource(item));
    private static void DepositAt(StorageAuthorityRequest request, Inventory escrow, Inventory chest)
    {
        if (escrow.NrOfItems() != 1 || request.Target.x < 0 || request.Target.y < 0 ||
            request.Target.x >= chest.GetWidth() || request.Target.y >= chest.GetHeight())
            throw new OperationRejectedException("invalid-drag-destination");
        var incoming = escrow.GetAllItems()[0]; var target = chest.GetItemAt(request.Target.x, request.Target.y);
        if ((target == null ? "" : NativeInventoryCodec.ItemIdentity(target)) != request.TargetIdentity)
            throw new OperationRejectedException("drag-destination-changed");
        if (target == null)
        {
            escrow.GetAllItems().Clear(); incoming.m_gridPos = request.Target; chest.GetAllItems().Add(incoming);
        }
        else if (NativeInventoryCodec.StackIdentity(incoming) == NativeInventoryCodec.StackIdentity(target))
        {
            int moved = Math.Min(incoming.m_stack, target.m_shared.m_maxStackSize - target.m_stack);
            if (moved < 1) throw new OperationRejectedException("drag-destination-full");
            target.m_stack += moved; incoming.m_stack -= moved;
            if (incoming.m_stack == 0) escrow.GetAllItems().Clear();
        }
        else
        {
            if (request.Amount != 1) throw new OperationRejectedException("partial-stack-cannot-swap");
            chest.GetAllItems().Remove(target); escrow.GetAllItems().Clear();
            target.m_gridPos = new Vector2i(0, 0); escrow.GetAllItems().Add(target);
            incoming.m_gridPos = request.Target; chest.GetAllItems().Add(incoming);
        }
        NativeInventoryCodec.Validate(chest); NativeInventoryCodec.Validate(escrow);
    }
    private static void Deposit(Inventory source, Inventory target, Func<ItemDrop.ItemData, bool> eligible)
    {
        foreach (var item in source.GetAllItems().Where(eligible).ToArray())
        {
            int amount = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(target, item));
            if (amount > 0 && !NativeInventoryCodec.Transfer(source, target, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), amount))
                throw new OperationRejectedException("inventory-plan-changed");
        }
    }
}
