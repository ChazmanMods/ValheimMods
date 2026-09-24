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
        private readonly Func<bool> _stillWritable;

        internal ValheimMaterialSource(
            string sourceId,
            Inventory inventory,
            MaterialSourceKind kind,
            float distanceSquared,
            IEnumerable<string> resourceIds,
            Func<bool> eligibility, Func<bool> stillWritable = null)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _kind = kind;
            _distanceSquared = distanceSquared;
            _resourceIds = new List<string>(resourceIds ?? throw new ArgumentNullException(nameof(resourceIds))).ToArray();
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
            _stillWritable = stillWritable ?? eligibility;
        }

        public string SourceId { get; }
        internal bool IsEligible => !RunicAutomation.MutationGate.IsBlocked(_inventory) &&
            !RunicAutomation.MutationGate.IsBlocked(SourceId) && _eligibility();

        internal bool TryStageOne(Inventory destination, string resource,
            Func<ItemDrop.ItemData, bool> permitted, out string reason) =>
            ManualItemTransfer.TryMoveOne(_inventory, destination, resource, _eligibility, permitted, out reason, _stillWritable);

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
            RunicAutomation.MutationGate.Current?.Track(_inventory);
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
            CraftingInventorySnapshot inventoryBackup;
            try { inventoryBackup = new CraftingInventorySnapshot(_inventory); }
            catch { return false; }
            string expected = null;
            bool drawer = Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(_inventory);
            Action changed = _inventory.m_onChanged;
            try
            {
                // Stage the exact successor without firing foreign callbacks between removals.
                Inventory shadow = inventoryBackup.CreateShadow();
                foreach (Removal removal in planned)
                {
                    var item = shadow.GetItemAt(removal.Original.m_gridPos.x, removal.Original.m_gridPos.y);
                    if (item == null || !Remove(shadow, item, removal.Quantity, drawer)) return false;
                }
                expected = ValheimReflection.Fingerprint(shadow);
                _inventory.m_onChanged = null;
                foreach (Removal removal in planned)
                {
                    if (!Remove(_inventory, removal.Original, removal.Quantity, drawer))
                        throw new InvalidOperationException("Crafting removal did not match the staged successor.");
                }
                _inventory.m_onChanged = changed;
                ValheimReflection.NotifyInventoryChanged(_inventory);
                if (!_stillWritable() || ValheimReflection.Fingerprint(_inventory) != expected)
                    throw new InvalidOperationException("Crafting endpoint changed during publication.");
            }
            catch (Exception error)
            {
                _inventory.m_onChanged = changed;
                var recovery = new InventoryRestoreToken(_inventory, inventoryBackup, expected, _stillWritable, SourceId);
                if (!recovery.Restore())
                    throw new RunicAutomation.MutationIndeterminateException(SourceId, error);
                throw;
            }
            finally { _inventory.m_onChanged = changed; }

            restoreToken = new InventoryRestoreToken(_inventory, inventoryBackup, expected, _stillWritable, SourceId);
            return true;
        }

        internal static bool Remove(Inventory inventory, ItemDrop.ItemData item, int quantity, bool drawer)
        {
            if (!drawer) return inventory.RemoveItem(item, quantity);
            if (item == null || !inventory.GetAllItems().Contains(item) || item.m_stack < quantity) return false;
            item.m_stack -= quantity;
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
            private readonly CraftingInventorySnapshot _snapshot;
            private bool _active = true;
            private readonly string _expected, _sourceId;
            private readonly Func<bool> _authority;

            internal InventoryRestoreToken(
                Inventory inventory,
                CraftingInventorySnapshot snapshot, string expected, Func<bool> authority, string sourceId)
            {
                _inventory = inventory;
                _snapshot = snapshot;
                _expected = expected; _authority = authority; _sourceId = sourceId;
            }

            public bool Restore()
            {
                if (!_active) return true;
                try
                {
                    if (!_authority()) throw new InvalidOperationException("Recovery authority lost.");
                    // An already-restored endpoint needs no publication. Never overwrite unknown state.
                    try { _snapshot.Verify(_inventory); _active = false; return true; } catch { }
                    if (_expected == null || ValheimReflection.Fingerprint(_inventory) != _expected)
                        throw new InvalidOperationException("Recovery state is not the expected successor.");
                    ValheimReflection.RestoreInventory(_inventory, _snapshot);
                    _active = false;
                    return true;
                }
                catch
                {
                    RunicAutomation.MutationGate.Block(_inventory);
                    RunicAutomation.MutationGate.Block(_sourceId);
                    return false;
                }
            }
        }
    }
}
