using System;
using System.Collections.Generic;
using System.Linq;
using RunicAutomation.Operations;
using RunicStorage.Engine;

namespace RunicStorage.Runtime;

internal static partial class StorageAuthorityCommands
{
    private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> PlanOrganize(StorageAuthorityRequest request, List<Chest> chests)
    {
        if (!string.IsNullOrWhiteSpace(PluginConfig.LockedContainerSlots.Value))
            throw new OperationRejectedException("organize-unavailable-with-locked-slots");
        if (chests.Count < 2 || chests.Count > 64 || chests.Sum(x => x.Inventory.NrOfItems()) > 2048)
            throw new OperationRejectedException("organize-requires-two-to-sixty-four-chests-with-at-most-2048-stacks");
        var bodies = chests.Select(c => ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(c.Before))).ToArray();
        var manual = chests.Select((c, i) => SmartSortCategories.HasManualRules(c.Rules) && bodies[i].AutoRules != c.OriginalRules).ToArray();
        var policies = chests.Select((c, i) => !manual[i] && c.Production == null ? null : new Func<ItemDrop.ItemData, int>(item =>
            c.Production != null && c.Production.Rank(item) == int.MaxValue ? int.MaxValue : !manual[i] ? 0 :
            c.Rules.RoutingScore(ChestGroupRuntime.Facts(item), c.Inventory.GetAllItems().Any(existing => Resource(existing) == Resource(item))))).ToArray();
        var planned = SmartSortPlanner.Build(chests.Select(c => c.Inventory).ToArray(),
            item => SmartSortCategories.For(ChestGroupRuntime.Facts(item)), policies);
        var changes = new List<Tuple<string, byte[], byte[]>>(); var labels = new List<string>();
        for (int n = 0; n < chests.Count; n++)
        {
            var chest = chests[n];
            string auto = bodies[n].AutoRules;
            if (!manual[n] && chest.Production == null)
            {
                chest.Rules.ShowLabel = true; chest.Rules.Label = SmartSortOrganizer.Label(planned[n]);
                if (chest.Rules.Remember) chest.Rules.Learn(planned[n].GetAllItems().Select(Resource));
                auto = chest.Rules.Encode();
            }
            string rules = chest.Rules.Encode();
            labels.Add((chest.Production != null ? "[production input] " : manual[n] ? "[destination] " : "") + chest.Rules.Label + " — " + planned[n].NrOfItems() + " stacks");
            changes.Add(Tuple.Create(ManagedChestLedger.Endpoint(chest.Zdo.m_uid), chest.Before,
                ManagedChestLedger.Next(chest.Before, planned[n], rules, auto)));
        }
        // The confirmation binds exact before/after inventories and policies, not just counts.
        string signature = OperationRequest.Hash(string.Join("\n", changes.Select(x => x.Item1 + ":" +
            Convert.ToBase64String(x.Item2) + ":" + Convert.ToBase64String(x.Item3))) + "\n" +
            string.Join("\n", chests.Select(c => c.Zdo.m_uid + ":" + (c.Production?.Signature ?? "general"))));
        if (request.Action == AuthorityStorageAction.Organize)
        {
            if (request.Rules != signature) throw new OperationRejectedException("organize-preview-changed-preview-again");
            return Tuple.Create(changes.ToArray(), NativeInventoryCodec.Save(new Inventory("empty", null, 32, 32)));
        }
        var p = new ZPackage(); p.Write(signature); p.Write(labels.Count);
        foreach (string label in labels) p.Write(label);
        return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), p.GetArray());
    }
}
