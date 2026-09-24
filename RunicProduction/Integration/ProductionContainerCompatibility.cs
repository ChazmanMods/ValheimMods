using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Runic.Compatibility;

namespace RunicProduction.Integration
{
    // Each live drawer and its transaction-local shadows retain their assigned item and
    // capacity. Never load its oversized single stack through vanilla Inventory.Load.
    internal static class ProductionContainerCompatibility
    {
        private sealed class DrawerShape
        {
            internal ItemDrop.ItemData Template;
        }
        private static readonly ConditionalWeakTable<Inventory, DrawerShape> Shapes = new();

        internal static bool Allowed(Container container) =>
            !ModdedContainerCompatibility.IsDrawer(container) ||
            ModdedContainerCompatibility.Listed(container, ProductionConfig.ContainerPrefabIds.Value);

        internal static bool TryRefresh(Container container, out Inventory inventory)
        {
            inventory = null;
            if (!Allowed(container) || !ModdedContainerCompatibility.TryRefresh(container, out inventory)) return false;
            var items = inventory.GetAllItems();
            // Assign the drawer using ItemDrawers first, including a zero-count assignment.
            if (items.Count != 1 || items[0].m_shared == null || items[0].m_shared.m_maxStackSize <= 0 ||
                items[0].m_stack < 0 || items[0].m_stack > items[0].m_shared.m_maxStackSize) return false;
            Shapes.Remove(inventory);
            Shapes.Add(inventory, new DrawerShape { Template = items[0].Clone() });
            return true;
        }

        internal static bool IsDrawer(Inventory inventory) => inventory != null && Shapes.TryGetValue(inventory, out _);
        internal static void CopyShape(Inventory source, Inventory shadow)
        {
            if (Shapes.TryGetValue(source, out var shape)) Shapes.Add(shadow, shape);
        }
        internal static ItemDrop.ItemData[] CaptureItems(Inventory inventory) =>
            IsDrawer(inventory) ? inventory.GetAllItems().Select(item => item.Clone()).ToArray() : null;

        internal static void Load(Inventory inventory, StockInventoryState state)
        {
            if (!Shapes.TryGetValue(inventory, out var shape)) {
                inventory.Load(new ZPackage(state.CopyPayload())); return;
            }
            var items = state.CopyDrawerItems();
            if (items == null || items.Length != 1 || items[0].m_dropPrefab != shape.Template.m_dropPrefab ||
                items[0].m_stack < 0 || items[0].m_stack > shape.Template.m_shared.m_maxStackSize)
                throw new InvalidOperationException("Drawer snapshot no longer matches its assigned item/capacity.");
            inventory.GetAllItems().Clear();
            inventory.GetAllItems().AddRange(items);
        }

        internal static bool Remove(Inventory inventory, ItemDrop.ItemData item, int amount)
        {
            if (!IsDrawer(inventory)) return inventory.RemoveItem(item, amount);
            if (amount <= 0 || !inventory.GetAllItems().Contains(item) || item.m_stack < amount) return false;
            item.m_stack -= amount; // Retain the assigned zero-count item.
            return true;
        }

        internal static ItemDrop.ItemData TransferTemplate(Inventory source, ItemDrop.ItemData item) =>
            IsDrawer(source) ? ModdedContainerCompatibility.NativeTemplate(item) : item;

        internal static bool TryAdd(Inventory inventory, ItemDrop.ItemData item, int amount)
        {
            if (!Shapes.TryGetValue(inventory, out var shape) || amount <= 0 || item == null ||
                inventory.GetAllItems().Count != 1) return false;
            var target = inventory.GetAllItems()[0];
            var canonical = item.m_dropPrefab?.GetComponent<ItemDrop>()?.m_itemData;
            if (canonical == null || item.m_dropPrefab != shape.Template.m_dropPrefab ||
                target.m_dropPrefab != item.m_dropPrefab || target.m_stack < 0 ||
                (long)target.m_stack + amount > shape.Template.m_shared.m_maxStackSize ||
                item.m_quality != canonical.m_quality || item.m_variant != canonical.m_variant ||
                item.m_worldLevel != canonical.m_worldLevel || item.m_crafterID != canonical.m_crafterID ||
                !string.Equals(item.m_crafterName ?? "", canonical.m_crafterName ?? "", StringComparison.Ordinal) ||
                item.m_equipped || item.m_cheated || item.m_shared.m_questItem ||
                !item.m_durability.Equals(canonical.m_durability) ||
                (item.m_customData?.Count ?? 0) != (canonical.m_customData?.Count ?? 0) ||
                (item.m_customData != null && item.m_customData.Any(pair => canonical.m_customData == null ||
                    !canonical.m_customData.TryGetValue(pair.Key, out string value) || value != pair.Value))) return false;
            target.m_stack += amount;
            return true;
        }
    }
}
