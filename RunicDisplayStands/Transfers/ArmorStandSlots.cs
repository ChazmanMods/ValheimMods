#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicDisplayStands
{
    // These are player-facing roles, independent of native attachment indices.
    internal static class ArmorStandSlots
    {
        internal const int Count = 9, Width = 5, Height = 2;
        internal const int Helmet = 0, Chest = 1, Legs = 2, Cape = 3, Utility = 4,
            RightHand = 5, LeftHand = 6, Shield = 7, Weapon = 8;
        internal static readonly string[] Labels =
            { "Helmet", "Chest", "Legs", "Cape", "Utility", "Right hand", "Left hand", "Shield", "Weapon" };

        internal static bool IsWeapon(ItemDrop.ItemData item) => item != null &&
            (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow);
        internal static bool IsHandItem(ItemDrop.ItemData item) => IsWeapon(item) || item != null &&
            (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ||
             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool ||
             item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch);
        internal static bool Accepts(int slot, ItemDrop.ItemData item)
        {
            if (item == null || item.m_stack != 1) return false;
            var type = item.m_shared.m_itemType;
            switch (slot)
            {
                case Helmet: return type == ItemDrop.ItemData.ItemType.Helmet;
                case Chest: return type == ItemDrop.ItemData.ItemType.Chest;
                case Legs: return type == ItemDrop.ItemData.ItemType.Legs;
                case Cape: return type == ItemDrop.ItemData.ItemType.Shoulder;
                case Utility: return type == ItemDrop.ItemData.ItemType.Utility;
                case RightHand: case LeftHand: return IsHandItem(item);
                case Shield: return type == ItemDrop.ItemData.ItemType.Shield;
                case Weapon: return IsWeapon(item);
                default: return false;
            }
        }
        internal static int DefaultSlot(ItemDrop.ItemData item)
        {
            foreach (int slot in new[] { Helmet, Chest, Legs, Cape, Utility, Shield, Weapon, RightHand, LeftHand })
                if (Accepts(slot, item)) return slot;
            return -1;
        }
        internal static int Slot(ItemDrop.ItemData item) => item.m_gridPos.x >= 0 && item.m_gridPos.x < Width &&
            item.m_gridPos.y >= 0 && item.m_gridPos.y < Height ? item.m_gridPos.y * Width + item.m_gridPos.x : -1;
        internal static Vector2i Position(int slot) => new Vector2i(slot % Width, slot / Width);
        internal static ItemDrop.ItemData GetItem(Inventory inventory, int slot)
        {
            var position = Position(slot);
            return inventory.GetItemAt(position.x, position.y);
        }
        internal static bool IsDisplayHand(int slot) => slot == RightHand || slot == LeftHand;

        // Older saves did not name roles. Favor armor/weapon/shield, then use
        // display hands for extra hand items; never discard unrepresentable items.
        internal static int LegacySlot(ItemDrop.ItemData item, ItemDrop.ItemData[] roles)
        {
            int primary = DefaultSlot(item);
            if (primary >= 0 && roles[primary] == null) return primary;
            foreach (int slot in new[] { RightHand, LeftHand })
                if (roles[slot] == null && Accepts(slot, item)) return slot;
            throw new InvalidOperationException("This older stand has extra equipment that does not fit the named slots. Its saved items were left unchanged.");
        }

        internal static ItemDrop.ItemData[] ArrangeSaved(IEnumerable<KeyValuePair<int, ItemDrop.ItemData>> attachments)
        {
            var roles = new ItemDrop.ItemData[Count];
            var legacy = new List<ItemDrop.ItemData>();
            foreach (var entry in attachments)
            {
                if (entry.Key < 0) { legacy.Add(entry.Value); continue; }
                if (!Accepts(entry.Key, entry.Value) || roles[entry.Key] != null)
                    throw new InvalidOperationException("Saved stand role is invalid or duplicated; stand left unchanged.");
                roles[entry.Key] = entry.Value;
            }
            foreach (var item in legacy) roles[LegacySlot(item, roles)] = item;
            for (int role = 0; role < roles.Length; role++)
                if (roles[role] != null)
                {
                    roles[role].m_gridPos = Position(role);
                    roles[role].m_equipped = false;
                }
            return roles;
        }

        internal static ItemDrop.ItemData[] MapNative(IEnumerable<ItemDrop.ItemData> items, int count,
            Func<int, int, bool> matchesVisual)
        {
            var mapped = new ItemDrop.ItemData[count];
            var usedRoles = new HashSet<int>();
            foreach (var item in items.Where(i => i != null).OrderBy(i => IsDisplayHand(Slot(i))))
            {
                int role = Slot(item);
                if (!Accepts(role, item) || !usedRoles.Add(role))
                    throw new InvalidOperationException("That item does not fit the selected named stand slot.");
                int target = -1;
                for (int i = 0; i < count; i++)
                    if (mapped[i] == null && matchesVisual(i, role)) { target = i; break; }
                if (target < 0)
                    throw new InvalidOperationException("This stand has no available attachment point for that slot; items were left unchanged.");
                mapped[target] = item;
            }
            return mapped;
        }

        internal static ItemDrop.ItemData[] SelectPlayerItems(Inventory player,
            IEnumerable<ItemDrop.ItemData> selected, Inventory stand)
        {
            var candidates = selected.Concat(player.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x))
                .Distinct().ToArray();
            var result = new ItemDrop.ItemData[Count];
            foreach (int slot in new[] { Helmet, Chest, Legs, Cape, Utility, Shield, Weapon })
            {
                bool combat = slot == Shield || slot == Weapon;
                // Both sides must have one for weapon/shield. Armor swaps remain
                // valid when a role is empty on either side.
                if (combat && GetItem(stand, slot) == null) continue;
                result[slot] = candidates.FirstOrDefault(i => player.GetAllItems().Contains(i) &&
                    Accepts(slot, i) && (combat || slot == Utility || i.m_equipped));
            }
            return result;
        }
    }
}
