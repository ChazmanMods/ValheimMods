using System;
using System.Collections.Generic;
using System.Linq;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

internal sealed class TrashSelection
{
    internal sealed class Entry
    {
        internal readonly ItemData Item, Before;
        internal Entry(ItemData item) { Item = item; Before = item.Clone(); }
        internal bool Matches(Inventory inventory) => inventory.GetAllItems().Contains(Item) &&
            Item.m_stack == Before.m_stack && Item.m_gridPos.x == Before.m_gridPos.x && Item.m_gridPos.y == Before.m_gridPos.y &&
            ValheimContainerService.CanMergeExact(Item, Before);
    }
    internal readonly List<Entry> Entries = new();
    internal long Quantity => Entries.Sum(e => (long)e.Before.m_stack);
    internal bool Toggle(Inventory inventory, ItemData item, Func<ItemData, bool> eligible)
    {
        var existing = Entries.FirstOrDefault(e => ReferenceEquals(e.Item, item));
        if (existing != null) { Entries.Remove(existing); return true; }
        if (item == null || !inventory.GetAllItems().Contains(item) || !eligible(item)) return false;
        Entries.Add(new Entry(item)); return true;
    }
    internal void Prune(Inventory inventory, Func<ItemData, bool> eligible) => Entries.RemoveAll(e => !e.Matches(inventory) || !eligible(e.Item));
    internal static bool CellAllowed(int row, int height, bool locked, bool equipped, bool quest, int quantity, int dedicatedRow = -1) =>
        row > 0 && row < height && row != dedicatedRow && !locked && !equipped && !quest && quantity > 0;
}
