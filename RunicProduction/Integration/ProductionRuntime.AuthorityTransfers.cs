using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        private static float nextUnavailableAuthorityChestWarning;

        private static bool TryReadAuthorityTransferChest(ZDO station, ZDO chest,
            Dictionary<ZDOID, AuthorityChest> cache, out AuthorityChest source)
        {
            try { source = AuthorityRead(chest, cache); return true; }
            catch (Exception error) when ((string)error.Data["Runic.OperationDisposition"] == "deferred" ||
                (string)error.Data["Runic.OperationDisposition"] == "rejected" || error is InvalidDataException)
            {
                // A linked chest is an optional transfer endpoint, not the station's clock.
                // Never roll back otherwise valid fuel consumption/production merely because
                // this endpoint is enrolling, unavailable or cannot supply a usable image.
                // The failed chest has not entered the detached changeset and is untouched.
                source = null;
                // An ordinary asynchronous read is expected; it is not a player warning.
                if (error.Message == "container-live-state-requested") return false;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now >= nextUnavailableAuthorityChestWarning)
                {
                    nextUnavailableAuthorityChestWarning = now + 30f;
                    ProductionDiagnostics.Warning("Production skipped unavailable linked chest " + chest.m_uid +
                        " for station " + station.m_uid + ": " + error.Message + ". Station processing continues.");
                }
                return false;
            }
        }

        // Every inventory here is a detached shadow. The caller includes all changed shadows
        // and the station image in the same durable operation, including partially routed output.
        private static IEnumerable<AuthorityChest> AuthorityRoleChests(ZDO station, ProductionLinkRole role,
            Dictionary<ZDOID, AuthorityChest> cache)
        {
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, role, out var catalog) != StoredRecordState.Valid)
                throw new OperationRejectedException("invalid-station-links");
            foreach (var link in catalog.Links)
            {
                var chest = ResolveAuthorityLink(station, link);
                if (chest != null && TryReadAuthorityTransferChest(station, chest, cache, out var source))
                {
                    source.LinkOwnerId = link.OwnerId; yield return source;
                }
            }
        }
        private static bool AuthorityConsume(AuthorityChest source, string prefab, IngredientReservePolicy reserves, out bool cheated)
        {
            cheated = false;
            if (!ExactStockInventoryMutation.TryPrepareSource(source.Inventory,
                new[] { new ReplenishmentRequirement(prefab, 1) }, reserves, out var transition, out _, out _)) return false;
            source.Inventory = NativeInventoryCodec.Load(transition.After.CopyPayload(), source.Inventory.GetWidth(), source.Inventory.GetHeight());
            cheated = transition.ConsumedCheated; return true;
        }
        private static void AuthorityRefill(ZDO station, AuthorityStationState state, Dictionary<ZDOID, AuthorityChest> cache)
        {
            var prefab = NativeStationWorld.Object(station);
            string fuelName = state.Strings[AuthorityStationState.FuelItem];
            ItemDrop fuel = fuelName.Length == 0 ? null : ObjectDB.instance.GetItemPrefab(fuelName)?.GetComponent<ItemDrop>();
            float maximum = state.Floats[AuthorityStationState.MaximumFuel];
            if (fuel == null || maximum <= 0 || Math.Ceiling(state.Floats[ZDOVars.s_fuel]) >= maximum) return;
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, ProductionLinkRole.Fuel, out var fuelLinks) != StoredRecordState.Valid)
                throw new OperationRejectedException("invalid-station-fuel-links");
            var role = fuelLinks.Links.Count > 0 ? ProductionLinkRole.Fuel : ProductionLinkRole.Input;
            foreach (var source in AuthorityRoleChests(station, role, cache))
                if (AuthorityConsume(source, fuel.name, _fuelReserves, out _))
                { state.Floats[ZDOVars.s_fuel] = Math.Min(maximum, state.Floats[ZDOVars.s_fuel] + 1); AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Fuel); break; }
        }
        private static void AuthorityFillSmelter(ZDO station, AuthorityStationState state, Dictionary<ZDOID, AuthorityChest> cache)
        {
            var smelter = NativeStationWorld.Object(station).GetComponent<Smelter>();
            if (smelter == null || state.Integers[ZDOVars.s_queued] >= state.Integers[AuthorityStationState.MaximumOre]) return;
            foreach (var source in AuthorityRoleChests(station, ProductionLinkRole.Input, cache))
                foreach (string input in source.Inventory.GetAllItems().Select(i => i.m_dropPrefab.name).Distinct().ToArray())
                {
                    if (!state.Strings[AuthorityStationState.AllowedInputs].Split('\n').Contains(input) ||
                        !AuthorityConsume(source, input, _ingredientReserves, out bool cheated)) continue;
                    int count = state.Integers[ZDOVars.s_queued];
                    state.Strings[("item" + count).GetStableHashCode()] = input;
                    state.Integers[ZDOVars.s_queued] = count + 1;
                    state.Booleans[ZDOVars.s_cheatedQueued] |= cheated;
                    AuthorityProductionEvents.Add(AuthorityProductionEvents.Kind.Input);
                    return;
                }
        }
        private static void AuthorityRouteBuffer(ZDO station, AuthorityStationState state, Dictionary<ZDOID, AuthorityChest> cache)
        {
            if (state.Output.NrOfItems() == 0) return;
            foreach (var target in AuthorityTimedTargets(station, state, cache))
                foreach (var item in state.Output.GetAllItems().Where(i => i.m_dropPrefab.name == target.Target.OutputPrefabId).ToArray())
                {
                    int amount = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(target.Chest.Inventory, item));
                    if (amount <= 0) continue;
                    var moved = item.Clone(); moved.m_stack = amount;
                    if (!NativeInventoryCodec.AddExact(target.Chest.Inventory, moved)) throw new OperationRejectedException("native-output-capacity-changed");
                    state.Output.RemoveItem(item, amount);
                }
            foreach (var destination in AuthorityRoleChests(station, ProductionLinkRole.Output, cache))
            {
                if (state.Output.NrOfItems() == 0) break;
                foreach (var item in state.Output.GetAllItems().ToArray())
                {
                    int count = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(destination.Inventory, item));
                    if (count <= 0) continue;
                    var moved = item.Clone(); moved.m_stack = count;
                    if (!NativeInventoryCodec.AddExact(destination.Inventory, moved))
                        throw new InvalidOperationException("Output capacity changed inside a detached plan");
                    if (count == item.m_stack) state.Output.RemoveItem(item);
                    else item.m_stack -= count;
                }
            }
        }
        private static void AuthorityAppendChestChanges(Dictionary<ZDOID, AuthorityChest> cache, List<Tuple<string, byte[], byte[]>> changes)
        {
            foreach (var chest in cache.Values)
            {
                byte[] next = ManagedChestLedger.Next(chest.Before, chest.Inventory);
                // Compare the body, not the envelope: Next increments the revision.
                if (!OwnerOperationQueue.Same(FileOperationEndpoints.Inventory(chest.Before), FileOperationEndpoints.Inventory(next)))
                    changes.Add(Tuple.Create(ManagedChestLedger.Endpoint(chest.Zdo.m_uid), chest.Before, next));
            }
        }
    }
}
