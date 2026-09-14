using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace RunicDisplayStands
{
    // Named UI roles are saved separately from native physical attachment indices.
    internal static class ArmorStandAdapter
    {
        public static int GetSlotCount(ArmorStand stand) => stand.m_slots.Count;

        public static List<ItemDrop.ItemData> GetPlacedItems(ArmorStand stand)
        {
            var attachments = new List<KeyValuePair<int, ItemDrop.ItemData>>();
            var nview = stand.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return new ItemDrop.ItemData[ArmorStandSlots.Count].ToList();
            var zdo = nview.GetZDO();
            for (int i = 0; i < GetSlotCount(stand); i++)
            {
                int itemKey = ItemKey(i);
                int prefabHash = AttachedItemIdentity.Resolve(
                    stand.GetAttachedItem(i), nview.IsOwner(),
                    () => zdo.GetString(itemKey, string.Empty),
                    legacyName => legacyName.GetStableHashCode(),
                    migratedHash => { zdo.Set(itemKey, migratedHash); zdo.RemoveString(itemKey); });
                if (prefabHash == 0) continue;
                var item = ItemDataSerializer.Deserialize(zdo.GetByteArray(ItemDataKey(i), null));
                if (item != null)
                {
                    if (item.m_dropPrefab == null || item.m_dropPrefab.name.GetStableHashCode() != prefabHash)
                        throw new InvalidOperationException("Saved stand data does not match the displayed equipment; stand left unchanged.");
                }
                else
                {
                    var prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
                    if (prefab == null) throw new InvalidOperationException("A displayed equipment prefab is unavailable.");
                    item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                    ItemDrop.LoadFromZDO(item, zdo, i);
                    item.m_stack = 1;
                }
                int role = zdo.GetInt(RoleKey(i), 0) - 1;
                attachments.Add(new KeyValuePair<int, ItemDrop.ItemData>(role, item));
            }
            return ArmorStandSlots.ArrangeSaved(attachments).ToList();
        }

        internal static StandWriteBatch PrepareWrite(ArmorStand stand, IList<ItemDrop.ItemData> items)
        {
            var mapped = MapItemsToSlots(stand, items);
            var batch = new StandWriteBatch(stand.GetComponent<ZNetView>());
            for (int i = 0; i < mapped.Length; i++)
            {
                batch.Item(mapped[i], i);
                batch.Int(RoleKey(i), mapped[i] == null ? 0 : ArmorStandSlots.Slot(mapped[i]) + 1);
            }
            return batch;
        }

        internal static void PublishVisuals(ArmorStand stand, IList<ItemDrop.ItemData> items)
        {
            var mapped = MapItemsToSlots(stand, items);
            var view = stand.GetComponent<ZNetView>();
            // Clear the cached native visual hashes too: otherwise an unchanged reserve
            // can fail to redraw after removing another item sharing its visual role.
            for (int i = 0; i < mapped.Length; i++)
                view.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", i, 0, 0);
            foreach (int i in Enumerable.Range(0, mapped.Length).Where(i => mapped[i] != null)
                         .OrderBy(i => ArmorStandSlots.IsDisplayHand(ArmorStandSlots.Slot(mapped[i]))))
                view.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", i,
                    mapped[i] == null ? 0 : mapped[i].m_dropPrefab.name.GetStableHashCode(), mapped[i]?.m_variant ?? 0);
        }

        internal static ItemDrop.ItemData[] PlayerLoadout(Player player, Inventory stand)
        {
            var candidates = player.GetInventory().GetEquippedItems().ToList();
            foreach (string field in new[] { "m_hiddenLeftItem", "m_hiddenRightItem" })
            {
                var item = AccessTools.Field(typeof(Humanoid), field).GetValue(player) as ItemDrop.ItemData;
                if (item != null && !candidates.Contains(item)) candidates.Add(item);
            }
            return ArmorStandSlots.SelectPlayerItems(player.GetInventory(), candidates, stand);
        }

        private static int ItemKey(int index) => $"{index}_item".GetStableHashCode();
        private static int ItemDataKey(int index) => $"RunicDisplayStands_itemdata_{index}".GetStableHashCode();
        private static int RoleKey(int index) => $"RunicDisplayStands_role_{index}".GetStableHashCode();

        private static bool MatchesVisual(VisSlot native, int role)
        {
            switch (role)
            {
                case ArmorStandSlots.Helmet: return native == VisSlot.Helmet;
                case ArmorStandSlots.Chest: return native == VisSlot.Chest;
                case ArmorStandSlots.Legs: return native == VisSlot.Legs;
                case ArmorStandSlots.Cape: return native == VisSlot.Shoulder;
                case ArmorStandSlots.Utility: return native == VisSlot.Utility;
                case ArmorStandSlots.RightHand: case ArmorStandSlots.Weapon:
                    return native == VisSlot.HandRight || native == VisSlot.BackRight;
                case ArmorStandSlots.LeftHand: case ArmorStandSlots.Shield:
                    return native == VisSlot.HandLeft || native == VisSlot.BackLeft;
                default: return false;
            }
        }

        public static ItemDrop.ItemData[] MapItemsToSlots(ArmorStand stand, IEnumerable<ItemDrop.ItemData> items)
        {
            return ArmorStandSlots.MapNative(items, GetSlotCount(stand), (i, role) => MatchesVisual(stand.m_slots[i].m_slot, role));
        }
    }
}
