using System;
using System.Collections.Generic;
using RunicCrafting.Domain;

namespace RunicCrafting.Integration
{
    internal sealed class ValheimMaterialSource : IMutableMaterialSource
    {
        private readonly Inventory _inventory;
        private readonly MaterialSourceKind _kind;
        private readonly float _distanceSquared;
        private readonly string[] _resourceIds;
        private readonly Func<bool> _eligibility;

        internal ValheimMaterialSource(
            string sourceId,
            Inventory inventory,
            MaterialSourceKind kind,
            float distanceSquared,
            IEnumerable<string> resourceIds,
            Func<bool> eligibility)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _kind = kind;
            _distanceSquared = distanceSquared;
            _resourceIds = new List<string>(resourceIds ?? throw new ArgumentNullException(nameof(resourceIds))).ToArray();
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        }

        public string SourceId { get; }
        internal bool IsEligible => _eligibility();

        public MaterialSourceSnapshot Snapshot()
        {
            var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
            if (IsEligible)
            {
                foreach (string resourceId in _resourceIds)
                    quantities[resourceId] = ValheimReflection.CountRequirementItems(_inventory, resourceId);
            }
            return new MaterialSourceSnapshot(SourceId, _kind, _distanceSquared, quantities);
        }

        public bool TryTake(string resourceId, int quantity, out IMaterialRestoreToken restoreToken)
        {
            restoreToken = null;
            if (!IsEligible || quantity <= 0) return false;
            List<ItemDrop.ItemData> items = _inventory.GetAllItemsInGridOrder();
            var planned = new List<Removal>();
            int remaining = quantity;
            for (int index = 0; index < items.Count && remaining > 0; index++)
            {
                ItemDrop.ItemData item = items[index];
                if (!ValheimReflection.IsUsableRequirementItem(item, resourceId)) continue;
                int take = Math.Min(item.m_stack, remaining);
                planned.Add(new Removal(item, take));
                remaining -= take;
            }
            if (remaining != 0) return false;
            if (!ValheimReflection.CanRoundTripInventory(_inventory)) return false;

            ZPackage inventoryBackup = ValheimReflection.SaveInventory(_inventory);
            try
            {
                foreach (Removal removal in planned)
                {
                    if (!_inventory.RemoveItem(removal.Original, removal.Quantity))
                    {
                        ValheimReflection.RestoreInventory(_inventory, inventoryBackup);
                        return false;
                    }
                }
            }
            catch
            {
                ValheimReflection.RestoreInventory(_inventory, inventoryBackup);
                throw;
            }

            restoreToken = new InventoryRestoreToken(_inventory, inventoryBackup);
            return true;
        }

        private readonly struct Removal
        {
            internal Removal(ItemDrop.ItemData original, int quantity)
            {
                Original = original;
                Quantity = quantity;
            }

            internal ItemDrop.ItemData Original { get; }
            internal int Quantity { get; }
        }

        private sealed class InventoryRestoreToken : IMaterialRestoreToken
        {
            private readonly Inventory _inventory;
            private readonly ZPackage _snapshot;
            private bool _active = true;

            internal InventoryRestoreToken(
                Inventory inventory,
                ZPackage snapshot)
            {
                _inventory = inventory;
                _snapshot = snapshot;
            }

            public bool Restore()
            {
                if (!_active) return true;
                try
                {
                    ValheimReflection.RestoreInventory(_inventory, _snapshot);
                    _active = false;
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
