using System;
using System.Collections.Generic;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal sealed class PreviewMaterialCounts
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal PreviewMaterialCounts(Inventory inventory, int worldLevel)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item == null || item.m_worldLevel < worldLevel) continue;
                string resource = ValheimReflection.ResourceId(item);
                if (string.IsNullOrEmpty(resource)) continue;
                _counts.TryGetValue(resource, out int count);
                int stack = Math.Max(0, item.m_stack);
                _counts[resource] = count > int.MaxValue - stack ? int.MaxValue : count + stack;
            }
        }

        internal int Count(string resource) =>
            !string.IsNullOrEmpty(resource) && _counts.TryGetValue(resource, out int count) ? count : 0;

        internal MaterialSourceSnapshot ToSnapshot(string sourceId, MaterialSourceKind kind, float distanceSquared) =>
            new MaterialSourceSnapshot(sourceId, kind, distanceSquared, _counts);
    }
}
