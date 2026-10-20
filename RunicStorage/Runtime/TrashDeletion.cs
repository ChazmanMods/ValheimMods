using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

internal static class TrashDeletion
{
    internal static void Apply(Player player, Inventory inventory, IReadOnlyList<TrashSelection.Entry> selected,
        Func<ItemData, bool> eligible, StorageMutationLease lease)
    {
        if (player == null || !player.IsOwner() || player != Player.m_localPlayer ||
            lease == null || !lease.Covers(player, inventory) || RunicAutomation.MutationGate.IsBlocked(inventory))
            throw new InvalidOperationException("Inventory authority changed. Nothing deleted.");
        if (selected.Count == 0 || selected.Select(e => e.Item).Distinct().Count() != selected.Count ||
            selected.Any(e => !e.Matches(inventory) || !eligible(e.Item)))
            throw new InvalidOperationException("Selected items moved, changed, or became protected. Nothing deleted.");
        var snapshot = new StorageInventorySnapshot(inventory);
        var shadow = snapshot.CreateShadow();
        foreach (var entry in selected)
        {
            var match = shadow.GetItemAt(entry.Before.m_gridPos.x, entry.Before.m_gridPos.y);
            if (match == null || !shadow.GetAllItems().Remove(match)) throw new InvalidOperationException("Deletion plan changed.");
        }
        string expected = ValheimContainerService.Fingerprint(shadow);
        var changed = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        if (changed == null) throw new MissingMethodException("Inventory.Changed");
        var operation = RunicAutomation.MutationGate.Current;
        operation?.Track(inventory);
        Action callback = inventory.m_onChanged;
        try
        {
            inventory.m_onChanged = null;
            foreach (var entry in selected) inventory.GetAllItems().Remove(entry.Item);
            inventory.m_onChanged = callback;
            changed.Invoke(inventory, new object[] { false, false });
            if (!player.IsOwner() || ValheimContainerService.Fingerprint(inventory) != expected)
                throw new InvalidOperationException("Inventory changed during deletion.");
            operation?.Complete(RunicAutomation.MutationOutcome.Committed);
        }
        catch (Exception failure)
        {
            inventory.m_onChanged = callback;
            try
            {
                ValheimContainerService.RestoreKnown(inventory, snapshot, expected, () => player == Player.m_localPlayer && player.IsOwner(), player, lease);
                operation?.Complete(RunicAutomation.MutationOutcome.RolledBack);
            }
            catch (Exception recovery)
            {
                RunicAutomation.MutationGate.Block(inventory);
                operation?.Complete(RunicAutomation.MutationOutcome.Indeterminate);
                throw new RunicAutomation.MutationIndeterminateException("Trash deletion recovery needs inspection", new AggregateException(failure, recovery));
            }
            throw;
        }
        finally { inventory.m_onChanged = callback; }
    }
}
