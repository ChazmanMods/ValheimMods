using System;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicStorage.Runtime;

internal static class MultiAccessChestClicks
{
    internal static bool ProcessDelivery(Player player, byte[] payload, Inventory delivery)
    {
        var request = StorageAuthorityRequest.Decode(payload);
        if ((request.Action != AuthorityStorageAction.Withdraw && request.Action != AuthorityStorageAction.ExchangeWithdraw) ||
            request.Target.x < 0 || request.Target.y < 0 || delivery.NrOfItems() != 1) return true;
        var bag = player.GetInventory();
        if (request.Target.x >= bag.GetWidth() || request.Target.y >= bag.GetHeight()) return true;
        var existing = bag.GetItemAt(request.Target.x, request.Target.y); var item = delivery.GetAllItems()[0];
        if (existing == null)
        {
            var placed = item.Clone(); placed.m_gridPos = request.Target; bag.GetAllItems().Add(placed); delivery.GetAllItems().Clear();
        }
        else if (NativeInventoryCodec.StackIdentity(existing) == NativeInventoryCodec.StackIdentity(item))
        {
            int amount = Math.Min(item.m_stack, Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack));
            existing.m_stack += amount; item.m_stack -= amount;
            if (item.m_stack == 0) delivery.GetAllItems().Clear();
        }
        // A destination changed while the request was in flight. Remaining items use normal
        // delivery capacity; they never overwrite the replacement stack.
        NativeInventoryCodec.Validate(bag); return true;
    }
    internal static bool Move(Container chest, Inventory source, ItemDrop.ItemData item, int amount, Action<ItemDrop.ItemData> afterWithdrawal = null, Vector2i? target = null)
    {
        var player = Player.m_localPlayer;
        if (player == null || item == null || amount < 1 || amount > item.m_stack || item.m_shared.m_questItem) return false;
        if (CharacterOperationEscrow.Busy)
        { player.Message(MessageHud.MessageType.Center, CharacterOperationEscrow.Status); return false; }
        var request = new StorageAuthorityRequest { Chest = RunicAutomation.ContainerAuthority.View(chest).GetZDO().m_uid };
        if (ReferenceEquals(source, chest.GetInventory()))
        {
            request.Action = AuthorityStorageAction.Withdraw; request.Slot = item.m_gridPos;
            request.Identity = NativeInventoryCodec.ItemIdentity(item); request.Amount = amount;
            var offered = Array.Empty<Tuple<ItemDrop.ItemData, int>>();
            if (target.HasValue)
            {
                request.Target = target.Value; var existing = player.GetInventory().GetItemAt(target.Value.x, target.Value.y);
                if (existing != null && NativeInventoryCodec.StackIdentity(existing) == NativeInventoryCodec.StackIdentity(item))
                {
                    request.Amount = Math.Min(amount, Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack));
                    if (request.Amount == 0) return false;
                }
                else if (existing != null)
                {
                    if (amount != item.m_stack || existing.m_equipped || existing.m_shared.m_questItem ||
                        !Engine.StorageItemProtection.TryCapture(new[] { existing }, out var locks, out _) || locks.StateAt(0) == Engine.StorageProtectionState.Locked) return false;
                    request.Action = AuthorityStorageAction.ExchangeWithdraw; offered = new[] { Tuple.Create(existing, existing.m_stack) };
                }
            }
            return StorageOperationClient.Submit(request, offered, (accepted, _) =>
            {
                if (!accepted || afterWithdrawal == null || Player.m_localPlayer == null) return;
                var delivered = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(x => NativeInventoryCodec.StackIdentity(x) == NativeInventoryCodec.StackIdentity(item));
                if (delivered != null) afterWithdrawal(delivered);
            });
        }
        if (!ReferenceEquals(source, player.GetInventory()) || !source.ContainsItem(item)) return false;
        if (!Engine.StorageItemProtection.TryCapture(new[] { item }, out var protection, out _) || protection.StateAt(0) == Engine.StorageProtectionState.Locked)
            return false;
        player.RemoveEquipAction(item); player.UnequipItem(item, false);
        request.Action = AuthorityStorageAction.Deposit;
        if (target.HasValue)
        {
            request.Target = target.Value;
            var existing = chest.GetInventory().GetItemAt(target.Value.x, target.Value.y);
            request.TargetIdentity = existing == null ? "" : NativeInventoryCodec.ItemIdentity(existing);
            request.Amount = amount == item.m_stack ? 1 : 0; // A partial dragged stack cannot swap an incompatible stack.
        }
        return StorageOperationClient.Submit(request, new[] { Tuple.Create(item, amount) });
    }
    internal static void Drop(Container chest, ItemDrop.ItemData item, int amount)
    {
        string identity = NativeInventoryCodec.StackIdentity(item);
        Move(chest, chest.GetInventory(), item, amount, _ =>
        {
            var player = Player.m_localPlayer; if (player == null) return;
            int remaining = amount;
            foreach (var carried in player.GetInventory().GetAllItems().Where(x => NativeInventoryCodec.StackIdentity(x) == identity).ToArray())
            {
                int drop = Math.Min(remaining, carried.m_stack);
                if (!player.DropItem(player.GetInventory(), carried, drop)) break;
                remaining -= drop; if (remaining == 0) break;
            }
        });
    }
}

[HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
internal static class MultiAccessClickPatch
{
    [HarmonyPriority(-100)]
    // Interaction's vanilla transfer guard requires local chest ownership. A Runic
    // view sends this action to its authority instead, after earlier item guards.
    [HarmonyBefore("chazman.RunicInteraction")]
    private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod,
        Container ___m_currentContainer, GameObject ___m_dragGo, Inventory ___m_dragInventory, ItemDrop.ItemData ___m_dragItem,
        int ___m_dragAmount, bool __runOriginal)
    {
        if (!__runOriginal || !MultiAccessChestView.IsRunicView(___m_currentContainer)) return true;
        Inventory chest = ___m_currentContainer.GetInventory();
        if (___m_dragGo != null && (ReferenceEquals(___m_dragInventory, chest) || ReferenceEquals(grid.GetInventory(), chest)))
        {
            if (ReferenceEquals(___m_dragInventory, chest) && ReferenceEquals(grid.GetInventory(), chest))
            {
                if (___m_dragItem != null)
                    StorageOperationClient.Submit(new StorageAuthorityRequest {
                        Action = AuthorityStorageAction.MoveSlot,
                        Chest = RunicAutomation.ContainerAuthority.View(___m_currentContainer).GetZDO().m_uid,
                        Slot = ___m_dragItem.m_gridPos, Identity = NativeInventoryCodec.ItemIdentity(___m_dragItem),
                        Amount = ___m_dragAmount, Target = pos,
                        TargetIdentity = item == null ? "" : NativeInventoryCodec.ItemIdentity(item)
                    }, Array.Empty<Tuple<ItemDrop.ItemData, int>>());
            }
            else MultiAccessChestClicks.Move(___m_currentContainer, ___m_dragInventory, ___m_dragItem, ___m_dragAmount,
                target: pos);
            MultiAccessChestView.ClearDrag(); return false;
        }
        if (item == null) return true;
        if (mod == InventoryGrid.Modifier.Move)
        {
            MultiAccessChestClicks.Move(___m_currentContainer, grid.GetInventory(), item, item.m_stack); return false;
        }
        if (mod == InventoryGrid.Modifier.Drop && ReferenceEquals(grid.GetInventory(), chest))
        { MultiAccessChestClicks.Drop(___m_currentContainer, item, item.m_stack); return false; }
        return true; // Selecting an item and opening the split dialog do not modify inventory.
    }
}
[HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
internal static class MultiAccessUseItemPatch
{
    [HarmonyPriority(-100)]
    private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Container ___m_currentContainer, bool __runOriginal)
    {
        if (!__runOriginal || !MultiAccessChestView.IsRunicView(___m_currentContainer) ||
            !ReferenceEquals(grid.GetInventory(), ___m_currentContainer.GetInventory())) return true;
        MultiAccessChestClicks.Move(___m_currentContainer, grid.GetInventory(), item, 1,
            delivered => Player.m_localPlayer?.UseItem(Player.m_localPlayer.GetInventory(), delivered, true));
        return false;
    }
}
[HarmonyPatch(typeof(InventoryGui), "OnDropOutside")]
internal static class MultiAccessDropOutsidePatch
{
    private static bool Prefix(Container ___m_currentContainer, GameObject ___m_dragGo, Inventory ___m_dragInventory,
        ItemDrop.ItemData ___m_dragItem, int ___m_dragAmount, bool __runOriginal)
    {
        if (!__runOriginal || !MultiAccessChestView.IsRunicView(___m_currentContainer) || ___m_dragGo == null ||
            !ReferenceEquals(___m_dragInventory, ___m_currentContainer.GetInventory())) return true;
        MultiAccessChestClicks.Drop(___m_currentContainer, ___m_dragItem, ___m_dragAmount); MultiAccessChestView.ClearDrag(); return false;
    }
}
