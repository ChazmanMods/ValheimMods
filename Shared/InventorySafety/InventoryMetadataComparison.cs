using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicAutomation.Operations
{
    // Comparison-only keys. Native saves and the items themselves remain untouched.
    internal static class InventoryMetadataComparison
    {
        internal static bool SameItem(ItemDrop.ItemData left, ItemDrop.ItemData right) =>
            left != null && right != null && ForItem(left).SequenceEqual(ForItem(right));

        internal static byte[] ForItem(ItemDrop.ItemData item)
        {
            var key = new ZPackage();
            WriteItem(key, item.m_dropPrefab?.name.GetStableHashCode() ?? 0, item, false);
            return key.GetArray();
        }

        internal static byte[] ForInventory(Inventory inventory)
        {
            var key = new ZPackage(); key.Write(109); key.Write(inventory.GetAllItems().Count);
            foreach (var item in inventory.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x))
                WriteItem(key, item.m_dropPrefab?.name.GetStableHashCode() ?? 0, item, true);
            key.Write(0);
            return key.GetArray();
        }

        internal static byte[] ForPayload(byte[] bytes)
        {
            var source = new ZPackage(bytes);
            int version = source.ReadInt();
            if (version != 109) return (byte[])bytes.Clone();
            int count = source.ReadUShort();
            var items = new List<(int prefabHash, ItemDrop.ItemData itemData)>();
            for (int n = 0; n < count; n++) items.Add(ItemDrop.ItemData.Load(source, (global::Version.Item)version));
            var key = new ZPackage(); key.Write(version); key.Write(count);
            foreach (var item in items.OrderBy(i => i.itemData.m_gridPos.y).ThenBy(i => i.itemData.m_gridPos.x))
                WriteItem(key, item.prefabHash, item.itemData, true);
            // Production drawer snapshots append full stack counts beyond the native records.
            key.Write(bytes.Length - source.GetPos());
            for (int n = source.GetPos(); n < bytes.Length; n++) key.Write(bytes[n]);
            return key.GetArray();
        }

        private static void WriteItem(ZPackage key, int prefab, ItemDrop.ItemData item, bool includeSlot)
        {
            key.Write(prefab);
            if (includeSlot) { key.Write(item.m_gridPos.x); key.Write(item.m_gridPos.y); key.Write(item.m_stack); }
            // These distinguish upgraded gear, appearance, world tier, attribution,
            // achievement-relevant provenance and mod data that a merge would discard.
            key.Write(item.m_quality); key.Write(item.m_variant); key.Write(item.m_worldLevel);
            key.Write(item.m_crafterID); key.Write(item.m_crafterID == 0 ? "" : item.m_crafterName ?? "");
            key.Write(item.m_cheated);
            key.Write(item.m_customData?.Count ?? 0);
            if (item.m_customData != null)
                foreach (var pair in item.m_customData.OrderBy(p => p.Key, StringComparer.Ordinal))
                { key.Write(pair.Key); key.Write(pair.Value); }
        }
    }
}
