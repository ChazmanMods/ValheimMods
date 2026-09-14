using System;
using System.Collections.Generic;
using RunicInventory.Api;

namespace RunicInventory.Core
{
    internal readonly struct DedicatedRowItem
    {
        internal DedicatedRowItem(InventorySlotCoordinate position, InventoryItemCategory category, bool equipped)
        { Position = position; Category = category; Equipped = equipped; }
        internal InventorySlotCoordinate Position { get; }
        internal InventoryItemCategory Category { get; }
        internal bool Equipped { get; }
    }

    // Pure, all-or-nothing coordinate planning. Item objects, quantities and equipment flags
    // are never changed here. A failed shrink leaves the complete expanded inventory intact.
    internal static class DedicatedRowPlan
    {
        internal const string MetadataKey = "runic.inventory.extra-row.v1";
        internal const string QuiverMetadataKey = "runic.inventory.betterarchery.v1";
        internal static bool TryCreate(int nativeRows, bool enabled, int previousRoleRow,
            IReadOnlyList<DedicatedRowItem> items, IReadOnlyList<InventorySlotCoordinate> locks,
            out InventorySlotCoordinate[] positions, out IReadOnlyList<InventorySlotCoordinate> mappedLocks,
            out string reason, bool quiverEnabled = false, int previousQuiverRow = -1)
        {
            positions = null;
            mappedLocks = Array.Empty<InventorySlotCoordinate>();
            reason = "extra-row.invalid-input";
            if (nativeRows < 4 || nativeRows > 9 || items == null || items.Count > 96) return false;
            int roleRow = nativeRows + (quiverEnabled ? 2 : 0);
            int height = roleRow + (enabled ? 1 : 0);
            var result = new InventorySlotCoordinate[items.Count];
            var assigned = new bool[items.Count];
            var occupied = new HashSet<InventorySlotCoordinate>();
            var originals = new HashSet<InventorySlotCoordinate>();
            foreach (DedicatedRowItem item in items)
                if (item.Position.X < 0 || item.Position.X >= 8 || item.Position.Y < 0 ||
                    item.Position.Y >= 12 || !originals.Add(item.Position)) return false;

            // Better Archery owns three ammo cells in its second reserved row. Keep the
            // spacer and other hidden cells unavailable to ordinary items, including on shrink.
            if (quiverEnabled)
                for (int i = 0; i < items.Count; i++)
                {
                    DedicatedRowItem item = items[i];
                    if (item.Position.Y != previousQuiverRow || item.Position.X >= 3 ||
                        item.Category != InventoryItemCategory.Ammunition) continue;
                    var target = new InventorySlotCoordinate(item.Position.X, nativeRows + 1);
                    if (!occupied.Add(target)) return false;
                    result[i] = target;
                    assigned[i] = true;
                }

            // Equipped armor takes precedence over an unequipped spare in its role slot.
            if (enabled)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i].Equipped || !TopologyLayout.TryEquipmentRole(items[i].Category, out InventoryRoleKind role)) continue;
                    var target = new InventorySlotCoordinate((int)role - 1, roleRow);
                    if (!occupied.Add(target)) { reason = "extra-row.duplicate-equipped-role"; return false; }
                    result[i] = target;
                    assigned[i] = true;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    DedicatedRowItem item = items[i];
                    if (assigned[i] || item.Position.Y != previousRoleRow ||
                        !TopologyLayout.Accepts((InventoryRoleKind)(item.Position.X + 1), item.Category)) continue;
                    var target = new InventorySlotCoordinate(item.Position.X, roleRow);
                    if (!occupied.Add(target)) continue;
                    result[i] = target;
                    assigned[i] = true;
                }
            }
            // Preserve ordinary cells first, then relocate displaced items into free ordinary cells.
            for (int i = 0; i < items.Count; i++)
            {
                if (assigned[i] || items[i].Position.Y >= nativeRows || !occupied.Add(items[i].Position)) continue;
                result[i] = items[i].Position;
                assigned[i] = true;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (assigned[i]) continue;
                bool found = false;
                for (int y = 0; y < nativeRows && !found; y++)
                    for (int x = 0; x < 8 && !found; x++)
                    {
                        var target = new InventorySlotCoordinate(x, y);
                        if (!occupied.Add(target)) continue;
                        result[i] = target;
                        found = true;
                    }
                if (!found) { reason = "extra-row.clear-space-before-shrinking"; return false; }
            }
            var lockSet = new HashSet<InventorySlotCoordinate>();
            if (enabled && locks != null)
                foreach (InventorySlotCoordinate cell in locks)
                {
                    InventorySlotCoordinate target = cell;
                    bool itemFound = false;
                    for (int i = 0; i < items.Count; i++)
                        if (items[i].Position.Equals(cell)) { target = result[i]; itemFound = true; break; }
                    if (!itemFound && cell.Y == previousRoleRow) target = new InventorySlotCoordinate(cell.X, roleRow);
                    else if (!itemFound && quiverEnabled && cell.Y == previousQuiverRow && cell.X < 3)
                        target = new InventorySlotCoordinate(cell.X, nativeRows + 1);
                    if (target.X < 0 || target.X >= 8 || target.Y < 0 || target.Y >= height) return false;
                    lockSet.Add(target);
                }
            positions = result;
            mappedLocks = new List<InventorySlotCoordinate>(lockSet).AsReadOnly();
            reason = "ok";
            return true;
        }
    }
}
