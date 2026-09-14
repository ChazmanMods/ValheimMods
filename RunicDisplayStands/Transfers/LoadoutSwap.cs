#nullable disable
using System;
using System.Collections.Generic;

namespace RunicDisplayStands
{
    // Runs entirely inside ContainerBridge's snapshot/commit boundary, including equip failures.
    internal static class LoadoutSwap
    {
        internal static void Apply(Inventory player, Inventory stand,
            ItemDrop.ItemData[] outgoing, Action<ItemDrop.ItemData> unequip,
            Func<ItemDrop.ItemData, bool> equip, IReadOnlyDictionary<int, Vector2i> equipmentSlots = null)
        {
            if (outgoing.Length != ArmorStandSlots.Count) throw new ArgumentException("Expected named loadout slots.");
            var incoming = new ItemDrop.ItemData[ArmorStandSlots.Count];
            var vacated = new Dictionary<int, Vector2i>();
            for (int slot = 0; slot < ArmorStandSlots.Count; slot++)
            {
                if (ArmorStandSlots.IsDisplayHand(slot)) continue;
                var item = ArmorStandSlots.GetItem(stand, slot);
                // Empty optional roles must not strip the player's cape or utility.
                if ((slot == ArmorStandSlots.Cape || slot == ArmorStandSlots.Utility) && item == null) continue;
                if ((slot == ArmorStandSlots.Weapon || slot == ArmorStandSlots.Shield) &&
                    (item == null || outgoing[slot] == null)) continue;
                incoming[slot] = item;
                var worn = outgoing[slot];
                if (worn != null)
                {
                    vacated[slot] = worn.m_gridPos;
                    unequip(worn);
                    if (!player.RemoveItem(worn))
                        throw new InvalidOperationException("An equipped item could not be moved.");
                }
                if (item != null) stand.GetAllItems().Remove(item);
                if (worn != null)
                {
                    var copy = worn.Clone();
                    copy.m_gridPos = ArmorStandSlots.Position(slot);
                    copy.m_equipped = false;
                    stand.GetAllItems().Add(copy);
                }
            }
            for (int slot = 0; slot < incoming.Length; slot++)
            {
                var item = incoming[slot];
                if (item == null) continue;
                item.m_equipped = false;
                // Reuse a freed protected armor role directly; normal empty-slot
                // searches deliberately exclude RunicInventory's protected row.
                Vector2i position = default;
                bool positioned = equipmentSlots != null && equipmentSlots.TryGetValue(slot, out position);
                if (!positioned) positioned = vacated.TryGetValue(slot, out position);
                bool added = positioned &&
                    player.GetItemAt(position.x, position.y) == null
                    ? player.AddItem(item, position) : player.AddItem(item);
                if (!added)
                    throw new InvalidOperationException("Make room in your inventory before swapping this loadout.");
                // Each successful equip moves armor to its protected role and frees
                // temporary ordinary space before the next incoming item is added.
                if (!equip(item))
                    throw new InvalidOperationException("The replacement equipment could not be equipped; the swap was canceled.");
            }
            var retainedUtility = outgoing[ArmorStandSlots.Utility];
            if (incoming[ArmorStandSlots.Utility] == null && retainedUtility != null && !retainedUtility.m_equipped &&
                !equip(retainedUtility))
                throw new InvalidOperationException("Your carried utility item could not be equipped; the swap was canceled.");
        }
    }
}
