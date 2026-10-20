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
        private readonly Func<bool> _acquire;

        internal ValheimMaterialSource(
            string sourceId,
            Inventory inventory,
            MaterialSourceKind kind,
            float distanceSquared,
            IEnumerable<string> resourceIds,
            Func<bool> eligibility, Func<bool> stillWritable = null, Func<bool> acquire = null)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _kind = kind;
            _distanceSquared = distanceSquared;
            _resourceIds = new List<string>(resourceIds ?? throw new ArgumentNullException(nameof(resourceIds))).ToArray();
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
            _stillWritable = stillWritable ?? eligibility;
            _acquire = acquire ?? eligibility;
        }

        public string SourceId { get; }
        internal bool IsEligible => !RunicAutomation.MutationGate.IsBlocked(_inventory) &&
            !RunicAutomation.MutationGate.IsBlocked(SourceId) && _eligibility();

        internal bool TryStageOne(Inventory destination, string resource,
            Func<ItemDrop.ItemData, bool> permitted, out string reason)
        {
            reason = "source-unavailable";
            return _acquire() && ManualItemTransfer.TryMoveOne(_inventory, destination, resource, _eligibility, permitted, out reason, _stillWritable);
        }

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
            if (!_acquire()) return false;
            // Ownership refresh may have replaced item references; reselect just this resource.
            var planned = new List<Removal>();
            int remaining = quantity;
            foreach (var item in _inventory.GetAllItemsInGridOrder())
            {
                if (remaining == 0) break;
                if (!ValheimReflection.IsUsableRequirementItem(item, resourceId)) continue;
                int take = Math.Min(item.m_stack, remaining);
                planned.Add(new Removal(item, take)); remaining -= take;
            }
            if (remaining != 0) return false;
            var token = new InventoryRestoreToken(_inventory);
            bool drawer = Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(_inventory);
            try
            {
                foreach (var removal in planned)
                {
                    if (!_inventory.ContainsItem(removal.Original) || removal.Original.m_stack < removal.Quantity)
                    { token.Restore(); return false; }
                    var copy = Runic.Compatibility.ModdedContainerCompatibility.TransferTemplate(_inventory, removal.Original).Clone();
                    int before = removal.Original.m_stack;
                    bool accepted;
                    try { accepted = Remove(_inventory, removal.Original, removal.Quantity, drawer); }
                    finally
                    {
                        // Native callbacks can throw after removal. Remember only the actual debit.
                        int after = _inventory.ContainsItem(removal.Original) ? removal.Original.m_stack : 0;
                        copy.m_stack = Math.Max(0, before - after);
                        if (copy.m_stack > 0) token.Add(removal.Original, copy);
                    }
                    if (!accepted || copy.m_stack != removal.Quantity)
                    { token.Restore(); return false; }
                }
                if (drawer) ValheimReflection.NotifyInventoryChanged(_inventory);
            }
            catch { token.Restore(); throw; }
            restoreToken = token;
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
            private readonly Inventory inventory;
            private readonly List<Tuple<ItemDrop.ItemData, ItemDrop.ItemData>> removed = new List<Tuple<ItemDrop.ItemData, ItemDrop.ItemData>>();
            internal InventoryRestoreToken(Inventory inventory) { this.inventory = inventory; }
            internal void Add(ItemDrop.ItemData original, ItemDrop.ItemData copy) => removed.Add(Tuple.Create(original, copy));
            public bool Restore()
            {
                bool ok = true;
                foreach (var entry in removed)
                {
                    var original = entry.Item1; var copy = entry.Item2;
                    if (copy.m_stack <= 0) continue;
                    if (inventory.ContainsItem(original))
                    {
                        int fit = Math.Min(copy.m_stack, Math.Max(0, original.m_shared.m_maxStackSize - original.m_stack));
                        original.m_stack += fit;
                        copy.m_stack -= fit;
                        ValheimReflection.NotifyInventoryChanged(inventory);
                    }
                    if (copy.m_stack > 0)
                    {
                        var attempt = copy.Clone();
                        bool restored = inventory.AddItem(attempt, original.m_gridPos);
                        copy.m_stack = restored ? 0 : attempt.m_stack;
                        ok &= restored;
                    }
                }
                return ok;
            }
        }
    }
}
