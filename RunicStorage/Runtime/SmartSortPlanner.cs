using System;
using System.Collections.Generic;
using System.Linq;
using ItemData = ItemDrop.ItemData;

namespace RunicStorage.Runtime;

// Pure planning against clones. No live callbacks, network writes, or temporary ground drops.
internal static class SmartSortPlanner
{
    internal static List<Inventory> Build(IReadOnlyList<Inventory> source, Func<ItemData, string> category,
        IReadOnlyList<Func<ItemData, int>> destinations = null)
    {
        List<Inventory> completed = null;
        foreach (var step in BuildSteps(source, category, destinations, result => completed = result)) { }
        return completed;
    }

    internal static IEnumerable<int> BuildSteps(IReadOnlyList<Inventory> source, Func<ItemData, string> category,
        IReadOnlyList<Func<ItemData, int>> destinations, Action<List<Inventory>> completed)
    {
        if (destinations != null && destinations.Count != source.Count) throw new ArgumentException("Destination policies must match chests.");
        var result = source.Select(i => new Inventory(i.GetName(), null, i.GetWidth(), i.GetHeight())).ToList();
        var pool = new List<ItemData>();
        var affinity = source.Select(i => i.GetAllItems().GroupBy(category)
            .ToDictionary(g => g.Key, g => g.Count())).ToList();
        for (int chestIndex = 0; chestIndex < source.Count; chestIndex++)
        {
            var inventory = source[chestIndex];
            var occupied = new HashSet<string>();
            foreach (var original in inventory.GetAllItems())
            {
                if (original == null || original.m_shared == null || original.m_stack <= 0 ||
                    original.m_stack > original.m_shared.m_maxStackSize || original.m_gridPos.x < 0 ||
                    original.m_gridPos.y < 0 || original.m_gridPos.x >= inventory.GetWidth() ||
                    original.m_gridPos.y >= inventory.GetHeight() ||
                    !occupied.Add(original.m_gridPos.x + ":" + original.m_gridPos.y))
                    throw new InvalidOperationException("Unsupported or overlapping inventory stacks; nothing changed.");
                yield return 0; // Give Unity a frame between bounded planning steps.
                var item = original.Clone();
                // Manually configured chests only receive: retain original slots and contents.
                if (destinations?[chestIndex] != null) { result[chestIndex].GetAllItems().Add(item); continue; }
                foreach (var target in pool)
                {
                    if (!ValheimContainerService.CanMergeExact(item, target)) continue;
                    int moved = Math.Min(item.m_stack, target.m_shared.m_maxStackSize - target.m_stack);
                    target.m_stack += moved; item.m_stack -= moved;
                    if (item.m_stack == 0) break;
                }
                if (item.m_stack > 0) pool.Add(item);
            }
        }
        if (destinations != null)
        foreach (var item in pool)
        {
            foreach (int index in Enumerable.Range(0, result.Count)
                .Where(i => destinations[i] != null)
                .Select(i => new { Index = i, Rank = destinations[i](item) })
                .Where(x => x.Rank < int.MaxValue).OrderBy(x => x.Rank).ThenBy(x => x.Index).Select(x => x.Index))
            {
                Deposit(result[index], item);
                if (item.m_stack == 0) break;
            }
        }
        pool.RemoveAll(i => i.m_stack == 0);
        // Largest groups claim their best existing home first, with stable ties in input order.
        foreach (var group in pool.GroupBy(category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var queue = new Queue<ItemData>(group.OrderBy(ValheimContainerIdentity.ResourceId, StringComparer.Ordinal)
                .ThenBy(i => i.m_quality).ThenBy(i => i.m_variant).ThenByDescending(i => i.m_stack));
            while (queue.Count > 0)
            {
                int target = Enumerable.Range(0, result.Count)
                    .Where(i => destinations?[i] == null && result[i].GetAllItems().Count < source[i].GetWidth() * source[i].GetHeight())
                    .OrderByDescending(i => result[i].GetAllItems().Any(x => category(x) == group.Key))
                    .ThenByDescending(i => result[i].GetAllItems().Count == 0)
                    .ThenByDescending(i => affinity[i].TryGetValue(group.Key, out int count) ? count : 0)
                    .ThenBy(i => i).DefaultIfEmpty(-1).First();
                if (target < 0) throw new InvalidOperationException("Insufficient space; nothing changed.");
                var inventory = result[target];
                int capacity = inventory.GetWidth() * inventory.GetHeight();
                while (queue.Count > 0 && inventory.GetAllItems().Count < capacity)
                {
                    var item = queue.Dequeue();
                    int slot = inventory.GetAllItems().Count;
                    item.m_gridPos = new Vector2i(slot % inventory.GetWidth(), slot / inventory.GetWidth());
                    inventory.GetAllItems().Add(item);
                }
            }
        }
        yield return 0;
        VerifyConservation(source, result);
        completed(result);
    }

    private static void Deposit(Inventory target, ItemData source)
    {
        foreach (var stack in target.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x))
        {
            if (!ValheimContainerService.CanMergeExact(source, stack)) continue;
            int moved = Math.Min(source.m_stack, stack.m_shared.m_maxStackSize - stack.m_stack);
            source.m_stack -= moved; stack.m_stack += moved;
            if (source.m_stack == 0) return;
        }
        for (int y = 0; y < target.GetHeight() && source.m_stack > 0; y++)
        for (int x = 0; x < target.GetWidth() && source.m_stack > 0; x++)
        {
            if (target.GetItemAt(x, y) != null) continue;
            var added = source.Clone(); added.m_gridPos = new Vector2i(x, y);
            added.m_stack = Math.Min(source.m_stack, source.m_shared.m_maxStackSize);
            source.m_stack -= added.m_stack; target.GetAllItems().Add(added);
        }
    }

    internal static void VerifyConservation(IEnumerable<Inventory> before, IEnumerable<Inventory> after)
    {
        var left = Totals(before); var right = Totals(after);
        if (left.Count != right.Count || left.Any(p => !right.TryGetValue(p.Key, out long n) || n != p.Value))
            throw new InvalidOperationException("Item/metadata conservation failed; nothing changed.");
    }

    private static Dictionary<string, long> Totals(IEnumerable<Inventory> inventories)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in inventories.SelectMany(i => i.GetAllItems()))
        {
            var clone = item.Clone(); clone.m_stack = 1; clone.m_gridPos = new Vector2i(0, 0);
            var single = new Inventory("signature", null, 1, 1); single.GetAllItems().Add(clone);
            string key = ValheimContainerService.Fingerprint(single);
            totals.TryGetValue(key, out long n); totals[key] = checked(n + item.m_stack);
        }
        return totals;
    }
}
