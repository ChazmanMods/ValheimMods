using System;
using System.Linq;

namespace RunicCrafting.Integration
{
    // A completed transfer stages a real item in the backpack. The subsequent vanilla
    // interaction owns its consumption; never refund after an unacknowledged native RPC.
    internal static class ManualItemTransfer
    {
        internal static bool TryMoveOne(Inventory source, Inventory destination, string resource,
            Func<bool> eligible, Func<ItemDrop.ItemData, bool> permitted, out string reason, Func<bool> stillWritable = null)
        {
            stillWritable = stillWritable ?? eligible;
            reason = "source-unavailable";
            if (source == null || destination == null || ReferenceEquals(source, destination) ||
                eligible == null || permitted == null || !eligible()) return false;
            if (RunicAutomation.MutationGate.IsBlocked(source) || RunicAutomation.MutationGate.IsBlocked(destination))
            { reason = "endpoint-needs-inspection"; return false; }
            if (!RunicAutomation.MutationGate.TryBegin("crafting-manual-transfer", out var operation))
            { reason = "mutation-busy"; return false; }
            using var operationScope = operation;
            operation.Track(source); operation.Track(destination);
            ItemDrop.ItemData original = source.GetAllItems().FirstOrDefault(item =>
                ValheimReflection.IsUsableRequirementItem(item, resource) && item.m_stack > 0 && permitted(item));
            if (original == null) { reason = "no-unprotected-match"; return false; }
            ItemDrop.ItemData copy = Runic.Compatibility.ModdedContainerCompatibility.TransferTemplate(source, original).Clone();
            copy.m_stack = 1;
            if (!destination.CanAddItem(copy, 1)) { reason = "backpack-space-required"; return false; }
            if (!eligible() || !source.GetAllItems().Contains(original) || original.m_stack < 1 ||
                !permitted(original)) { reason = "source-changed"; return false; }
            var sourceBefore = new CraftingInventorySnapshot(source);
            var destinationBefore = new CraftingInventorySnapshot(destination);
            int sourceCount = ValheimReflection.CountRequirementItems(source, resource);
            int destinationCount = ValheimReflection.CountRequirementItems(destination, resource);
            bool drawer = Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(source);
            Inventory sourceShadow = sourceBefore.CreateShadow(), destinationShadow = destinationBefore.CreateShadow();
            if (!ValheimMaterialSource.Remove(sourceShadow, sourceShadow.GetItemAt(original.m_gridPos.x, original.m_gridPos.y), 1, drawer) ||
                !destinationShadow.AddItem(copy.Clone())) { reason = "staging-failed"; return false; }
            string sourceAfter = ValheimReflection.Fingerprint(sourceShadow);
            string destinationAfter = ValheimReflection.Fingerprint(destinationShadow);
            Action sourceChanged = source.m_onChanged, destinationChanged = destination.m_onChanged;
            try
            {
                source.m_onChanged = null; destination.m_onChanged = null;
                if (!ValheimMaterialSource.Remove(source, original, 1, drawer) || !destination.AddItem(copy) ||
                    ValheimReflection.CountRequirementItems(source, resource) != sourceCount - 1 ||
                    ValheimReflection.CountRequirementItems(destination, resource) != destinationCount + 1)
                    throw new InvalidOperationException("one-item-transfer-not-exact");
                source.m_onChanged = sourceChanged; destination.m_onChanged = destinationChanged;
                ValheimReflection.NotifyInventoryChanged(source); ValheimReflection.NotifyInventoryChanged(destination);
                if (!stillWritable() || ValheimReflection.Fingerprint(source) != sourceAfter ||
                    ValheimReflection.Fingerprint(destination) != destinationAfter)
                    throw new InvalidOperationException("transfer-publication-changed");
                reason = "one-item-staged";
                operation.Complete(RunicAutomation.MutationOutcome.Committed);
                return true;
            }
            catch
            {
                source.m_onChanged = sourceChanged; destination.m_onChanged = destinationChanged;
                bool restored = false;
                // Revalidate before either side is restored. Never reacquire authority during recovery.
                try
                {
                    if (stillWritable())
                    {
                        bool first = RestoreKnown(destination, destinationBefore, destinationAfter);
                        bool second = RestoreKnown(source, sourceBefore, sourceAfter);
                        restored = first && second;
                    }
                }
                catch { }
                operation.Complete(restored ? RunicAutomation.MutationOutcome.RolledBack : RunicAutomation.MutationOutcome.Indeterminate);
                reason = restored ? "transfer-restored" : "recovery-needs-inspection:" + operation.Id;
                return false;
            }
            finally { source.m_onChanged = sourceChanged; destination.m_onChanged = destinationChanged; }
        }

        private static bool RestoreKnown(Inventory inventory, CraftingInventorySnapshot before, string after)
        {
            try
            {
                try { before.Verify(inventory); return true; } catch { }
                if (ValheimReflection.Fingerprint(inventory) != after) return false;
                ValheimReflection.RestoreInventory(inventory, before);
                return true;
            }
            catch { return false; }
        }
    }
}
