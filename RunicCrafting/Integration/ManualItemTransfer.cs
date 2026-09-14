using System;
using System.Linq;

namespace RunicCrafting.Integration
{
    // A completed transfer stages a real item in the backpack. The subsequent vanilla
    // interaction owns its consumption; never refund after an unacknowledged native RPC.
    internal static class ManualItemTransfer
    {
        internal static bool TryMoveOne(Inventory source, Inventory destination, string resource,
            Func<bool> eligible, Func<ItemDrop.ItemData, bool> permitted, out string reason)
        {
            reason = "source-unavailable";
            if (source == null || destination == null || ReferenceEquals(source, destination) ||
                eligible == null || permitted == null || !eligible()) return false;
            ItemDrop.ItemData original = source.GetAllItems().FirstOrDefault(item =>
                ValheimReflection.IsUsableRequirementItem(item, resource) && item.m_stack > 0 && permitted(item));
            if (original == null) { reason = "no-unprotected-match"; return false; }
            ItemDrop.ItemData copy = original.Clone();
            copy.m_stack = 1;
            if (!destination.CanAddItem(copy, 1)) { reason = "backpack-space-required"; return false; }
            if (!eligible() || !source.GetAllItems().Contains(original) || original.m_stack < 1 ||
                !permitted(original)) { reason = "source-changed"; return false; }
            var sourceBefore = new CraftingInventorySnapshot(source);
            var destinationBefore = new CraftingInventorySnapshot(destination);
            int sourceCount = ValheimReflection.CountRequirementItems(source, resource);
            int destinationCount = ValheimReflection.CountRequirementItems(destination, resource);
            try
            {
                if (!source.RemoveItem(original, 1) || !destination.AddItem(copy) ||
                    ValheimReflection.CountRequirementItems(source, resource) != sourceCount - 1 ||
                    ValheimReflection.CountRequirementItems(destination, resource) != destinationCount + 1)
                    throw new InvalidOperationException("one-item-transfer-not-exact");
                reason = "one-item-staged";
                return true;
            }
            catch
            {
                // Restore both sides even if a notification fails on one side.
                try { ValheimReflection.RestoreInventory(destination, destinationBefore); }
                finally { ValheimReflection.RestoreInventory(source, sourceBefore); }
                reason = "transfer-restored";
                return false;
            }
        }
    }
}
