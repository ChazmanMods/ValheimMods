using System;
using System.Collections.Generic;
using System.Linq;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        private sealed class AuthorityTimedTarget
        {
            internal ReplenishmentDestinationRecord Record;
            internal ReplenishmentTargetAuthorization Target;
            internal int Index, Stock;
            internal AuthorityChest Chest;
        }
        private static IEnumerable<AuthorityTimedTarget> AuthorityTimedTargets(ZDO station, AuthorityStationState state,
            Dictionary<ZDOID, AuthorityChest> cache)
        {
            if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(station, state.Token, false, out var catalog, out _) != StoredRecordState.Valid) yield break;
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, ProductionLinkRole.Replenishment, out var links) != StoredRecordState.Valid) yield break;
            var prefab = NativeStationWorld.Object(station);
            var cooking = prefab.GetComponent<CookingStation>(); var fermenter = prefab.GetComponent<Fermenter>();
            TimedCookingStationDescriptor cookingDescriptor = null; FermenterDescriptor fermenterDescriptor = null;
            if (cooking != null && (!_cookingPolicy.Allows(ZNetScene.instance.GetPrefab(state.Prefab).name) || !TimedCookingStockModel.TryDescribe(cooking, ZNetScene.instance.GetPrefab(state.Prefab).name, out cookingDescriptor, out _))) yield break;
            if (fermenter != null && (!_fermenterPolicy.Allows(ZNetScene.instance.GetPrefab(state.Prefab).name) || !FermenterCompatibility.TryDescribeRegistered(fermenter, ZNetScene.instance.GetPrefab(state.Prefab).name, out fermenterDescriptor, out _))) yield break;
            var records = catalog.ActiveRoundRobinOrder().ToArray();
            int prior = Array.FindIndex(records, r => r.LinkId == state.LastLink);
            foreach (var record in records.Skip(prior + 1).Concat(records.Take(prior + 1)))
            {
                if (record.State != ReplenishmentDestinationState.Active) continue;
                var link = links.FindTarget(record.Link.TargetToken, record.Link.TargetPrefabHash);
                if (link == null || link.LinkId != record.LinkId || link.Revision != record.Link.Revision ||
                    !ReplenishmentPlanStore.MatchesLink(record.Plan, link, state.Token) || record.Plan.AuthorizedPlayerId != link.OwnerId) continue;
                var zdo = ResolveAuthorityLink(station, link); if (zdo == null) continue;
                if (!TryReadAuthorityTransferChest(station, zdo, cache, out var chest)) continue;
                for (int offset = 0; offset < record.Plan.Targets.Count; offset++)
                {
                    int start = record.LinkId == state.LastLink ? state.LastTarget + 1 : record.Plan.Cursor;
                    int index = (Math.Max(0, start) + offset) % record.Plan.Targets.Count;
                    var target = record.Plan.Targets[index];
                    if (target.AuthorizedPlayerId != link.OwnerId) continue;
                    bool valid = cookingDescriptor != null && TimedCookingPlanBuilder.TryResolveAuthorized(cookingDescriptor, target, out _, out _);
                    if (fermenterDescriptor != null)
                        valid = FermenterPlanBuilder.TryValidate(record.Plan, fermenterDescriptor, link, state.Token, chest.Inventory, out _);
                    if (!valid) continue;
                    int stock = ExactStockInventoryMutation.CountExact(chest.Inventory, target.OutputPrefabId, matchWorldLevel: false);
                    if (stock > 0) yield return new AuthorityTimedTarget { Record = record, Target = target, Index = index, Stock = stock, Chest = chest };
                }
            }
        }
        private static void AuthorityFillPlanned(ZDO station, AuthorityStationState state, long now,
            AuthorityStationEnvironment environment, Dictionary<ZDOID, AuthorityChest> cache)
        {
            var prefab = NativeStationWorld.Object(station);
            var cooking = prefab.GetComponent<CookingStation>(); var fermenter = prefab.GetComponent<Fermenter>();
            // Preserve the existing conservative admission rule: one unbound timed batch.
            if (cooking != null && ((cooking.m_requireFire && !environment.FireLit) || Enumerable.Range(0, state.Integers[AuthorityStationState.SlotCount]).Any(n => state.Strings[("slot" + n).GetStableHashCode()].Length != 0))) return;
            if (fermenter != null && (!environment.Sheltered || state.Integers[ZDOVars.s_content] != 0 || state.Strings[ZDOVars.s_content].Length != 0)) return;
            if (cooking == null && fermenter == null) return;
            foreach (var candidate in AuthorityTimedTargets(station, state, cache))
            {
                var target = candidate.Target;
                if (target.Requirements.Count != 1 || target.Requirements[0].Amount != 1 || !ReplenishmentNeedsStock(target.OutputPrefabId, candidate.Stock, 0)) continue;
                var output = new StockOutputDefinition(target.OutputPrefabId, target.OutputAmount, 1, 0, 0L, "");
                if (!ExactStockInventoryMutation.TryPrepareDestination(candidate.Chest.Inventory, output, out _, out _)) continue;
                foreach (var zdo in AuthorityRecipeInputs(station, candidate.Chest.Zdo, target.AuthorizedPlayerId))
                {
                    string input = target.Requirements[0].PrefabId;
                    if (!TryReadAuthorityTransferChest(station, zdo, cache, out var source) ||
                        !AuthorityConsume(source, input, _ingredientReserves, out bool cheated)) continue;
                    if (cooking != null)
                    {
                        int slot = "slot0".GetStableHashCode(); state.Strings[slot] = input; state.Floats[slot] = 0;
                        state.Integers["slotstatus0".GetStableHashCode()] = 0; state.Booleans[ZDOVars.s_cheatedQueued] = cheated;
                    }
                    else
                    {
                        state.Integers[ZDOVars.s_content] = input.GetStableHashCode(); state.Strings[ZDOVars.s_content] = "";
                        state.Longs[ZDOVars.s_startTime] = now; state.Booleans[ZDOVars.s_cheatedQueued] = cheated;
                    }
                    state.LastLink = candidate.Record.LinkId; state.LastTarget = candidate.Index;
                    AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Input, 0, target.AuthorizedPlayerId); return;
                }
            }
        }
    }
}
