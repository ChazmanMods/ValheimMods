using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RunicStorage.Engine;
using UnityEngine;

namespace RunicStorage.Runtime;

internal sealed partial class StorageActions
{
    private sealed class QuickDestination
    {
        internal Container Chest;
        internal Inventory Inventory;
        internal ChestRules Rules;
        internal bool Acquired, Unavailable;
    }

    private void NativeQuickStack()
    {
        long started = Stopwatch.GetTimestamp();
        if (!TryResolveOwnedLocalPlayer("Quick Stack", out var player)) return;
        var inventory = player.GetInventory();
        bool single = _singleStackInventory != null;
        if (single && !ReferenceEquals(inventory, _singleStackInventory)) { ClearQuickStackSelection(); return; }
        var selected = QuickStackSelection.Capture(inventory.GetAllItems(), single, _singleStackItem);
        ClearQuickStackSelection();
        if (!StorageItemProtection.TryCapture(selected, out var protection, out var failure, quickStack: true))
        { LogAction("quick-stack", failure, "moved=0"); return; }
        float range = Mathf.Clamp(PluginConfig.RangeMeters.Value, 1f, 50f);
        var candidates = _index.Nearest(player.transform.position, range,
            Mathf.Clamp(PluginConfig.MaximumCandidates.Value, 1, 256), out _);
        var targets = new List<QuickDestination>();
        foreach (var chest in candidates)
        {
            if (chest == null || (int)chest.m_privacy == 0 ||
                !ValheimContainerService.CanDiscover(chest, player.GetPlayerID(), false,
                    allowCurrentUse: ValheimContainerIdentity.NetworkView(chest)?.IsOwner() == true)) continue;
            if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawer(chest) &&
                !Runic.Compatibility.ModdedContainerCompatibility.Listed(chest, PluginConfig.QuickStackPrefabIds.Value)) continue;
            if (!StorageContainerAuthority.TryReadCurrentInventory(chest, out var destination) ||
                ReferenceEquals(destination, inventory)) continue;
            ChestRules rules = null;
            if (ChestRuleStore.Eligible(chest) && !ChestRuleStore.Read(chest, out rules)) continue;
            targets.Add(new QuickDestination { Chest = chest, Inventory = destination, Rules = rules });
        }
        int moved = 0, eligible = 0, unchanged = 0;
        for (int n = 0; n < selected.Count; n++)
        {
            var item = selected[n];
            if (IsProtected(player, item, protection.StateAt(n)) || item?.m_shared == null || item.m_shared.m_questItem) continue;
            eligible++;
            string resource = ValheimContainerIdentity.ResourceId(item);
            var facts = ChestGroupRuntime.Facts(item);
            // Score only matching destinations. Reading candidates does not claim them.
            var matches = targets.Where(t => !t.Unavailable).Select(t => new {
                Target = t, Score = t.Rules?.RoutingScore(facts, ContainsResource(t.Inventory, resource)) ??
                    (ContainsResource(t.Inventory, resource) ? 9 : int.MaxValue)
            }).Where(t => t.Score <= 9).OrderBy(t => t.Score);
            foreach (var match in matches)
            {
                if (!inventory.ContainsItem(item) || item.m_stack <= 0) break;
                var target = match.Target;
                if (!target.Inventory.CanAddItem(item, 1)) continue;
                if (!target.Acquired)
                {
                    bool own = ValheimContainerIdentity.NetworkView(target.Chest)?.IsOwner() == true;
                    if (!RunicAutomation.ContainerAuthority.TryAcquire(target.Chest, "storage",
                        RunicAutomation.ContainerAuthority.PlayerContext(player), player.GetPlayerID(), allowInUse: own))
                    { target.Unavailable = true; continue; }
                    target.Acquired = true;
                    target.Inventory = target.Chest.GetInventory();
                    if (ChestRuleStore.Eligible(target.Chest))
                    { ChestRuleStore.Learn(target.Chest, nativeOperation: true); if (!ChestRuleStore.Read(target.Chest, out target.Rules)) { target.Unavailable = true; continue; } }
                }
                bool present = ContainsResource(target.Inventory, resource);
                if ((target.Rules?.RoutingScore(facts, present) ?? (present ? 9 : int.MaxValue)) > 9) continue;
                moved += MoveNativeStack(inventory, target.Inventory, item);
            }
            if (inventory.ContainsItem(item)) unchanged += item.m_stack;
        }
        double elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        Message(player, moved > 0 ? $"Runic Storage: moved {moved} item(s)." : "Runic Storage: no matching chest has room.");
        LogAction("quick-stack", moved > 0 ? "ok" : "nothing-moved",
            $"path=native-immediate candidates={candidates.Count} eligibleStacks={eligible} moved={moved} unchanged={unchanged} elapsedMs={elapsed:F2}");
    }

    // Native inventory methods honor installed patches, current stack limits and capacity.
    // Only the selected stack can leave the backpack; no escrow, journal or server reply.
    internal static int MoveNativeStack(Inventory source, Inventory destination, ItemDrop.ItemData item)
    {
        if (source == null || destination == null || ReferenceEquals(source, destination) ||
            item == null || !source.ContainsItem(item) || !destination.CanAddItem(item, 1)) return 0;
        int before = item.m_stack;
        if (Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(source) ||
            Runic.Compatibility.ModdedContainerCompatibility.IsDrawerInventory(destination))
            return ValheimContainerService.MoveUpTo(source, destination, item, before);
        destination.MoveItemToThis(source, item);
        return Math.Max(0, before - (source.ContainsItem(item) ? item.m_stack : 0));
    }
}
