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
        private static bool AuthorityStoreProduct(ZDO station, AuthorityStationState state, Dictionary<ZDOID, AuthorityChest> cache,
            string prefab, int amount, bool cheated, out long owner)
        {
            owner = 0;
            if (amount <= 0) return false;
            var output = new StockOutputDefinition(prefab, amount, 1, 0, 0L, "", cheated && !PlayerProfile.s_bypassCheatChecks);
            var planned = AuthorityTimedTargets(station, state, cache).Where(c => c.Target.OutputPrefabId == prefab)
                .OrderByDescending(c => _replenishmentReserves.TryGetTargetReserve(prefab, out int reserve) ? reserve - c.Stock : 0).ToArray();
            foreach (var candidate in planned)
            {
                if (!ExactStockInventoryMutation.TryPrepareDestination(candidate.Chest.Inventory, output, out var transition, out _)) continue;
                candidate.Chest.Inventory = NativeInventoryCodec.Load(transition.After.CopyPayload(), candidate.Chest.Inventory.GetWidth(), candidate.Chest.Inventory.GetHeight());
                owner = candidate.Record.Link.OwnerId;
                return true;
            }
            foreach (var destination in AuthorityRoleChests(station, ProductionLinkRole.Output, cache))
            {
                if (!ExactStockInventoryMutation.TryPrepareDestination(destination.Inventory, output, out var transition, out _)) continue;
                destination.Inventory = NativeInventoryCodec.Load(transition.After.CopyPayload(), destination.Inventory.GetWidth(), destination.Inventory.GetHeight());
                owner = destination.LinkOwnerId;
                return true;
            }
            return false;
        }
        private static void AuthorityCollectTimed(ZDO station, AuthorityStationState state, long now,
            Dictionary<ZDOID, AuthorityChest> cache)
        {
            var prefab = NativeStationWorld.Object(station);
            if (prefab.TryGetComponent<CookingStation>(out var cooking))
            {
                for (int n = 0; n < cooking.m_slots.Length; n++)
                {
                    int slot = ("slot" + n).GetStableHashCode(), status = ("slotstatus" + n).GetStableHashCode();
                    if (state.Strings[slot].Length == 0 || state.Integers[status] < 1 || state.Integers[status] > 2) continue;
                    if (!AuthorityStoreProduct(station, state, cache, state.Strings[slot], 1, state.Booleans[ZDOVars.s_cheatedQueued + n], out long owner)) continue;
                    AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Output, n, owner);
                    state.Strings[slot] = ""; state.Floats[slot] = 0; state.Integers[status] = 0; state.Booleans[ZDOVars.s_cheatedQueued + n] = false;
                }
            }
            else if (prefab.TryGetComponent<Fermenter>(out var fermenter))
            {
                int content = state.Integers[ZDOVars.s_content]; string legacy = state.Strings[ZDOVars.s_content];
                if (content == 0 && legacy.Length == 0 || state.Longs[ZDOVars.s_startTime] == 0 ||
                    (now - state.Longs[ZDOVars.s_startTime]) / (double)TimeSpan.TicksPerSecond <= fermenter.m_fermentationDuration) return;
                var conversions = fermenter.m_conversion.Where(c => c.m_from != null && c.m_to != null &&
                    (content != 0 ? c.m_from.name.GetStableHashCode() == content : c.m_from.name == legacy)).ToArray();
                if (conversions.Length != 1) throw new OperationRejectedException("fermentation-conversion-ambiguous");
                var conversion = conversions[0];
                if (!AuthorityStoreProduct(station, state, cache, conversion.m_to.name, conversion.m_producedItems,
                    station.GetBool(ZDOVars.s_cheated) || state.Booleans[ZDOVars.s_cheatedQueued], out _)) return;
                AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Output);
                state.Integers[ZDOVars.s_content] = 0; state.Strings[ZDOVars.s_content] = "";
                state.Longs[ZDOVars.s_startTime] = 0; state.Booleans[ZDOVars.s_cheatedQueued] = false;
            }
            else if (prefab.TryGetComponent<Beehive>(out var hive))
            {
                int level = state.Integers[ZDOVars.s_level];
                if (level <= 0 || level > hive.m_maxHoney || hive.m_honeyItem == null || Game.instance == null) return;
                int amount = checked(level * Game.instance.ScaleDrops(hive.m_honeyItem.m_itemData, 1));
                if (AuthorityStoreProduct(station, state, cache, hive.m_honeyItem.name, amount, false, out _))
                { state.Integers[ZDOVars.s_level] = 0; AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Output); }
            }
        }
        private static void AuthorityFillTimed(ZDO station, AuthorityStationState state, long now,
            AuthorityStationEnvironment environment, Dictionary<ZDOID, AuthorityChest> cache)
        {
            // Replenishment admission needs its signature/exemplar checks, never ordinary
            // throughput admission as a fallback when a configured plan is paused or invalid.
            var catalogStatus = MultiReplenishmentRuntimeSupport.ReadOrMigrate(station, state.Token, false, out var catalog, out _);
            if (catalogStatus == StoredRecordState.Invalid) return;
            if (catalogStatus == StoredRecordState.Valid && catalog.Destinations.Count > 0)
            { AuthorityFillPlanned(station, state, now, environment, cache); return; }
            var prefab = NativeStationWorld.Object(station);
            if (prefab.TryGetComponent<CookingStation>(out var cooking))
            {
                if (!_cookingPolicy.Allows(ZNetScene.instance.GetPrefab(state.Prefab).name) || (cooking.m_requireFire && !environment.FireLit)) return;
                int empty = Enumerable.Range(0, state.Integers[AuthorityStationState.SlotCount]).Where(n => state.Strings[("slot" + n).GetStableHashCode()].Length == 0).DefaultIfEmpty(-1).First();
                if (empty < 0) return;
                foreach (var source in AuthorityRoleChests(station, ProductionLinkRole.Input, cache))
                    foreach (string input in source.Inventory.GetAllItems().Select(i => i.m_dropPrefab.name).Distinct().ToArray())
                    {
                        if (!state.Strings[AuthorityStationState.AllowedInputs].Split('\n').Contains(input) ||
                            !AuthorityConsume(source, input, _ingredientReserves, out bool cheated)) continue;
                        int slot = ("slot" + empty).GetStableHashCode(); state.Strings[slot] = input; state.Floats[slot] = 0;
                        state.Integers[("slotstatus" + empty).GetStableHashCode()] = 0;
                        state.Booleans[ZDOVars.s_cheatedQueued + empty] = cheated;
                        AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Input, empty, source.LinkOwnerId); return;
                    }
            }
            else if (prefab.TryGetComponent<Fermenter>(out var fermenter))
            {
                if (!_fermenterPolicy.Allows(ZNetScene.instance.GetPrefab(state.Prefab).name) || !environment.Sheltered || state.Integers[ZDOVars.s_content] != 0 || state.Strings[ZDOVars.s_content].Length != 0) return;
                foreach (var source in AuthorityRoleChests(station, ProductionLinkRole.Input, cache))
                    foreach (string input in source.Inventory.GetAllItems().Select(i => i.m_dropPrefab.name).Distinct().ToArray())
                    {
                        if (!state.Strings[AuthorityStationState.AllowedInputs].Split('\n').Contains(input) ||
                            !AuthorityConsume(source, input, _ingredientReserves, out bool cheated)) continue;
                        state.Integers[ZDOVars.s_content] = input.GetStableHashCode(); state.Strings[ZDOVars.s_content] = "";
                        state.Longs[ZDOVars.s_startTime] = now; state.Booleans[ZDOVars.s_cheatedQueued] = cheated;
                        AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Input); return;
                    }
            }
        }
    }
}
