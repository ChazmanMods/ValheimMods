using System;
using System.Collections.Generic;
using System.Linq;
using RunicAutomation.Operations;
using RunicStorage.Engine;

namespace RunicStorage.Runtime;

internal sealed partial class StorageActions
{
    private void AuthorityStoreAll()
    {
        if (!TryResolveOwnedLocalPlayer("Store All", out var player)) return;
        var chest = InventoryGui.instance == null ? null : CurrentContainerField.GetValue(InventoryGui.instance) as Container;
        if (chest == null) { Message(player, "Open a chest first."); return; }
        var items = player.GetInventory().GetAllItems().ToList(); items.Sort(CompareGridPosition);
        if (!TryCaptureProtection(items, out var protection, out _)) { Message(player, "Storage could not verify the item locks."); return; }
        var take = new List<Tuple<ItemDrop.ItemData, int>>();
        for (int n = 0; n < items.Count; n++)
            if (!IsProtected(player, items[n], protection.StateAt(n)) && !items[n].m_shared.m_questItem)
                take.Add(Tuple.Create(items[n], items[n].m_stack));
        if (take.Count == 0) return;
        StorageOperationClient.Submit(new StorageAuthorityRequest { Action = AuthorityStorageAction.Deposit,
            Chest = ValheimContainerIdentity.NetworkView(chest).GetZDO().m_uid }, take.ToArray());
    }
    private void AuthorityRestock()
    {
        if (!TryResolveOwnedLocalPlayer("Restock", out var player)) return;
        var targets = ParseTargets(PluginConfig.RestockTargets.Value);
        var items = player.GetInventory().GetAllItems().ToList();
        if (!TryCaptureProtection(items, out var protection, out _)) { Message(player, "Storage could not verify the item locks."); return; }
        var request = new StorageAuthorityRequest { Action = AuthorityStorageAction.Restock };
        foreach (var target in targets)
        {
            int missing = Math.Max(0, target.Value - CountMatching(player.GetInventory(), target.Key));
            if (missing == 0) continue;
            if (HasProtectedPartialMatch(player, items, in protection, target.Key))
            { Message(player, "A protected partial stack matches a restock target."); return; }
            request.Desired.Add(target.Key, missing);
        }
        if (request.Desired.Count == 0) { Message(player, "Your restock targets are already met."); return; }
        StorageOperationClient.Submit(request, Array.Empty<Tuple<ItemDrop.ItemData, int>>());
    }
    private void AuthoritySort()
    {
        if (!TryResolveOwnedLocalPlayer("Sort", out var player)) return;
        var chest = InventoryGui.instance == null ? null : CurrentContainerField.GetValue(InventoryGui.instance) as Container;
        if (chest == null) { Message(player, "Open a chest first."); return; }
        StorageOperationClient.Submit(new StorageAuthorityRequest { Action = AuthorityStorageAction.Sort,
            Chest = ValheimContainerIdentity.NetworkView(chest).GetZDO().m_uid }, Array.Empty<Tuple<ItemDrop.ItemData, int>>());
    }
}

internal static class StorageOperationClient
{
    internal static bool Submit(StorageAuthorityRequest request, Tuple<ItemDrop.ItemData, int>[] take, Action<bool, string> finished = null)
    {
        var player = Player.m_localPlayer;
        if (player == null) return false;
        if (CharacterOperationEscrow.Busy)
        { player.Message(MessageHud.MessageType.Center, CharacterOperationEscrow.Status); return false; }
        if (request.Action == AuthorityStorageAction.Withdraw || request.Action == AuthorityStorageAction.TakeAll || request.Action == AuthorityStorageAction.Restock)
        {
            var inventory = player.GetInventory();
            request.ReceiverWidth = inventory.GetWidth(); request.ReceiverHeight = inventory.GetHeight();
            request.Receiver = NativeInventoryCodec.Save(inventory);
            request.ReceiverEmptySlots = Math.Max(0, inventory.GetEmptySlots());
            request.ReceiverCapacity.Clear();
            var sources = UnityEngine.Object.FindObjectsByType<Container>(UnityEngine.FindObjectsSortMode.None).Where(chest =>
                chest != null && ValheimContainerIdentity.NetworkView(chest)?.GetZDO() != null &&
                (request.Action == AuthorityStorageAction.Restock
                    ? UnityEngine.Vector3.Distance(player.transform.position, chest.transform.position) <= Math.Max(1f, PluginConfig.RangeMeters.Value)
                    : ValheimContainerIdentity.NetworkView(chest).GetZDO().m_uid == request.Chest));
            foreach (var item in sources.SelectMany(chest => chest.GetInventory().GetAllItems()))
            {
                string identity = NativeInventoryCodec.ItemIdentity(item);
                if (!request.ReceiverCapacity.ContainsKey(identity))
                    request.ReceiverCapacity.Add(identity, NativeInventoryCodec.CapacityFor(inventory, item));
            }
        }
        bool started = CharacterOperationEscrow.Begin(StorageAuthorityCommands.Kind,
            escrow => { request.Escrow = escrow; return request.Encode(); }, take,
            (accepted, reason) =>
            {
                if (!accepted && Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, FailureMessage(request.Action, reason));
                finished?.Invoke(accepted, reason);
            });
        if (!started) player.Message(MessageHud.MessageType.Center, "Storage is not ready to start this action.");
        return started;
    }

    private static string FailureMessage(AuthorityStorageAction action, string reason)
    {
        if (action == AuthorityStorageAction.QuickStack)
        {
            switch (reason)
            {
                case "chest-unavailable":
                    return "Runic Storage: no nearby chest is available yet. Your items stayed in your inventory.";
                case "quick-stack-no-destinations":
                    return "Runic Storage: no accessible chests were found within Quick Stack range. Your items stayed in your inventory.";
                case "quick-stack-no-routing-match":
                    return "Runic Storage: nearby chests have no matching items or accepting Quick Stack rules. Your items stayed in your inventory.";
                case "quick-stack-destinations-full":
                    return "Runic Storage: matching chests are full. Your items stayed in your inventory.";
                case "quick-stack-destinations-incompatible":
                    return "Runic Storage: matching chests are full or their remaining stacks have different item data. Your items stayed in your inventory.";
            }
        }
        if (reason == "chest-unavailable")
            return "This chest is not available yet. Any offered items have been returned.";
        return "Storage could not complete the action: " + (reason ?? "unknown reason").Replace('-', ' ');
    }
}
