using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RunicDisplayStands
{
    internal sealed class InventorySnapshot
    {
        private static readonly FieldInfo[] Fields = typeof(ItemDrop.ItemData).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly Inventory _inventory;
        private readonly ItemDrop.ItemData[] _originals;
        private readonly ItemDrop.ItemData[] _copies;

        internal InventorySnapshot(Inventory inventory)
        {
            _inventory = inventory;
            _originals = inventory.GetAllItems().ToArray();
            _copies = _originals.Select(item => item.Clone()).ToArray();
        }

        internal void Restore()
        {
            for (int i = 0; i < _originals.Length; i++)
            {
                var copy = _copies[i].Clone();
                foreach (var field in Fields) field.SetValue(_originals[i], field.GetValue(copy));
            }
            // Keep equipped/dragged references and metadata; do not round-trip through Load.
            _inventory.GetAllItems().Clear();
            _inventory.GetAllItems().AddRange(_originals);
        }

        internal static string Contents(IEnumerable<ItemDrop.ItemData> items)
        {
            var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in items.Where(item => item != null))
            {
                if (item.m_stack <= 0) throw new InvalidOperationException("Invalid item stack.");
                var copy = item.Clone();
                copy.m_stack = 1;
                copy.m_gridPos = new Vector2i(0, 0);
                copy.m_equipped = false;
                var pkg = new ZPackage();
                copy.Save(pkg);
                string key = item.m_dropPrefab.name + ":" + pkg.GetBase64();
                counts.TryGetValue(key, out long count);
                counts[key] = checked(count + item.m_stack);
            }
            return string.Join("\n", counts.Select(pair => pair.Key + ":" + pair.Value));
        }
    }
}
