using System;
using System.Collections.Generic;
using RunicStorage.Engine;

namespace RunicStorage.Runtime;

internal sealed partial class StorageActions
{
    internal void SortCarriedInventory()
    {
        if (!TryResolveOwnedLocalPlayer("Sort inventory", out var player)) return;
        var inventory = player.GetInventory();
        var items = inventory.GetAllItems();
        if (!StorageItemProtection.TryCapture(items, out var protection, out var failure, quickStack: true))
        {
            Message(player, "Runic Storage: item protection is unavailable; inventory unchanged.");
            LogAction("sort-inventory", failure, "changed=false");
            return;
        }
        if (!CarriedSlotLocks.TryRead(player.m_customData, inventory.GetWidth(), inventory.GetHeight(), out var locked))
        {
            Message(player, "Runic Storage: protected slot data is unavailable; inventory unchanged.");
            return;
        }
        for (int y = 0; y < inventory.GetHeight(); y++)
        for (int x = 0; x < inventory.GetWidth(); x++)
            if ((PluginConfig.ProtectHotbar.Value && y == 0) || StoragePlayerProtection.IsReservedSlot(player, x, y))
                locked.Add(SlotKey(x, y));
        for (int n = 0; n < items.Count; n++)
            if (IsProtected(player, items[n], protection.StateAt(n)) || items[n].m_shared?.m_questItem == true)
                locked.Add(SlotKey(items[n].m_gridPos.x, items[n].m_gridPos.y));
        // Rearrange existing objects only. Counts, metadata, equipment and all
        // protected positions remain intact; no chest or network operation is involved.
        bool changed = ApplySortExact(inventory, locked);
        if (changed) InventoryChangedMethod.Invoke(inventory, new object[] { false, false });
        Message(player, changed ? "Runic Storage: inventory sorted." : "Runic Storage: inventory already sorted.");
        LogAction("sort-inventory", "ok", "changed=" + changed);
    }
}
