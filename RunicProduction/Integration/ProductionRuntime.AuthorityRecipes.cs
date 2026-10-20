using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static partial class ProductionRuntime
    {
        internal const string AuthorityRecipeKind = "production.recipe.v2";
        private static readonly HashSet<ZDOID> PendingAuthorityRecipes = new HashSet<ZDOID>();
        private const string RecipeRevisionKey = "runic.production.recipe-revision.v2";
        private static readonly Dictionary<string, ZDOID> RecipeClockStations = new();
        private sealed class AuthorityChest
        {
            internal ZDO Zdo; internal byte[] Before; internal Inventory Inventory; internal long LinkOwnerId;
        }
        internal static void RegisterAuthorityRecipes()
        {
            SharedOperationHost.RegisterBackground(AuthorityRecipeKind, AuthorizeRecipe, PlanRecipe, revisionGuarded: true);
            SharedOperationHost.RegisterProjection("production.recipe.clock:", (endpoint, image) => {
                if (RecipeClockStations.TryGetValue(endpoint, out var id))
                {
                    var station = ZDOMan.instance.GetZDO(id);
                    if (station != null) { station.Set(RecipeRevisionKey, FileOperationEndpoints.Revision(image)); ZDOMan.instance.ForceSendZDO(id); }
                }
            });
        }
        private static Tuple<ZDO, int, long> DecodeRecipeTick(byte[] payload)
        {
            var p = new ZPackage(payload);
            if (p.ReadInt() != 2) throw new OperationRejectedException("invalid-production-request");
            var station = ZDOMan.instance.GetZDO(p.ReadZDOID()); int level = p.ReadInt(); long revision = p.ReadLong(); OperationPacket.End(p);
            if (station == null || level < 1 || level > 100) throw new OperationRejectedException("invalid-production-station");
            return Tuple.Create(station, level, revision);
        }
        private static string AuthorizeRecipe(string actor, byte[] payload)
        {
            try
            {
                if (!ProductionConfig.Enabled.Value) return "production-disabled";
                var station = DecodeRecipeTick(payload).Item1;
                if (!AuthoritySimulationOwner(actor, station)) return "station-simulation-owner-changed";
                var prefab = ZNetScene.instance.GetPrefab(station.GetPrefab());
                if (prefab?.GetComponent<CraftingStation>() == null || !_recipePolicy.Allows(prefab.name) ||
                    !ProductionEndpointIdentity.IsCanonicalToken(station.GetString(ProductionEndpointIdentity.TokenStorageKey, "")))
                    return "unsupported-recipe-station";
                return null;
            }
            catch (Exception) { return "invalid-production-request"; }
        }
        private static bool AuthoritySimulationOwner(string actor, ZDO station)
        {
            long owner = station.GetOwner();
            if (ZNet.instance.IsServer() && owner == ZDOMan.GetSessionID() &&
                actor == "server-station:" + station.GetString(ProductionEndpointIdentity.TokenStorageKey, "")) return true;
            if (!NetworkInventoryAccess.TryPosition(actor, out long principal, out _)) return false;
            return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == principal && owner == ZDOMan.GetSessionID() ||
                ZNet.instance.GetPeers().Any(p => NetworkInventoryAccess.TryPeerPrincipal(p, out long peerPrincipal, out _) && peerPrincipal == principal && p.m_uid == owner);
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> PlanRecipe(string actor, byte[] payload, Func<string, byte[]> read)
        {
            string denied = AuthorizeRecipe(actor, payload); if (denied != null) throw new OperationRejectedException(denied);
            var request = DecodeRecipeTick(payload); var station = request.Item1;
            AuthorityLinkLedger.Read(station);
            string token = station.GetString(ProductionEndpointIdentity.TokenStorageKey, "");
            string clockId = "production.recipe.clock:" + token;
            var initialClock = new ZPackage(); initialClock.Write(0L); initialClock.Write(""); initialClock.Write(-1);
            byte[] clock = read(clockId) ?? SharedOperationHost.Import(clockId, initialClock.GetArray());
            RecipeClockStations[clockId] = station.m_uid;
            station.Set(RecipeRevisionKey, FileOperationEndpoints.Revision(clock)); ZDOMan.instance.ForceSendZDO(station.m_uid);
            if (request.Item3 != FileOperationEndpoints.Revision(clock)) throw new OperationRejectedException("station-revision-advanced");
            var clockPacket = new ZPackage(FileOperationEndpoints.Inventory(clock));
            long now = DateTime.UtcNow.Ticks, previous = clockPacket.ReadLong();
            string lastLink = clockPacket.ReadString(); int lastTarget = clockPacket.ReadInt(); OperationPacket.End(clockPacket);
            if (now - previous < TimeSpan.TicksPerSecond * Math.Max(0.1f, ProductionConfig.StockSchedulerIntervalSeconds.Value))
                throw new OperationDeferredException("production-interval");
            var cache = new Dictionary<ZDOID, AuthorityChest>();
            var refresh = RefreshAuthorityPlans(station, cache);
            if (refresh != null)
            {
                var refreshedClock = new ZPackage(); refreshedClock.Write(now); refreshedClock.Write(lastLink); refreshedClock.Write(lastTarget);
                return Tuple.Create(new[] { refresh, Tuple.Create(clockId, clock, FileOperationEndpoints.Next(clock, refreshedClock.GetArray())) }, Array.Empty<byte>());
            }
            if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(station, token, false, out MultiReplenishmentCatalog catalog, out _) != StoredRecordState.Valid)
                throw new OperationRejectedException("production-plan-unavailable");
            var prefab = ZNetScene.instance.GetPrefab(station.GetPrefab()); var descriptor = prefab.GetComponent<CraftingStation>();
            var ordered = catalog.ActiveRoundRobinOrder().ToArray();
            int previousDestination = Array.FindIndex(ordered, r => r.LinkId == lastLink);
            foreach (var record in ordered.Skip(previousDestination + 1).Concat(ordered.Take(previousDestination + 1)))
            {
                if (record.State != ReplenishmentDestinationState.Active || record.Plan.AdapterKind != ReplenishmentProducerKind.DirectRecipe) continue;
                if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, ProductionLinkRole.Replenishment, out var links) != StoredRecordState.Valid) continue;
                var active = links.FindTarget(record.Link.TargetToken, record.Link.TargetPrefabHash);
                if (active == null || active.LinkId != record.Link.LinkId || active.Revision != record.Link.Revision) continue;
                var destinationZdo = ResolveAuthorityLink(station, active); if (destinationZdo == null) continue;
                if (!TryReadAuthorityTransferChest(station, destinationZdo, cache, out var destination)) continue;
                for (int offset = 0; offset < record.Plan.Targets.Count; offset++)
                {
                    int start = record.LinkId == lastLink ? lastTarget + 1 : record.Plan.Cursor;
                    int index = (Math.Max(0, start) + offset) % record.Plan.Targets.Count;
                    var target = record.Plan.Targets[index];
                    if (target.RequiredStationLevel > request.Item2 || !RecipeProducerResolver.TryResolveAuthorized(descriptor, prefab.name, target,
                        _cookingInputPolicy, _fermenterInputPolicy, out _, out _, requirePhysical: false)) continue;
                    int stock = ExactStockInventoryMutation.CountExact(destination.Inventory, target.OutputPrefabId, matchWorldLevel: false);
                    if (stock <= 0 || !ReplenishmentNeedsStock(target.OutputPrefabId, stock, 0)) continue;
                    var inputs = new List<AuthorityChest>();
                    foreach (var zdo in AuthorityRecipeInputs(station, destinationZdo, target.AuthorizedPlayerId))
                        if (TryReadAuthorityTransferChest(station, zdo, cache, out var input)) inputs.Add(input);
                    if (inputs.Count == 0 || !ExactStockInventoryMutation.TryPrepareCompositeSources(inputs.Select(x => x.Inventory).ToList(),
                        target.Requirements, _ingredientReserves, out var sourcePlan, out _)) continue;
                    var output = new StockOutputDefinition(target.OutputPrefabId, target.OutputAmount, 1, 0,
                        target.AuthorizedPlayerId, target.AuthorizedPlayerName, sourcePlan.ConsumedCheated && !ValheimAccess.BypassCheatChecks);
                    if (!ExactStockInventoryMutation.TryPrepareDestination(destination.Inventory, output, out var result, out _)) continue;
                    var changes = new List<Tuple<string, byte[], byte[]>>();
                    foreach (var entry in sourcePlan.Entries) changes.Add(AuthorityChange(inputs[entry.SourceIndex], entry.Transition));
                    changes.Add(AuthorityChange(destination, result));
                    var nextClock = new ZPackage(); nextClock.Write(now); nextClock.Write(record.LinkId); nextClock.Write(index);
                    changes.Add(Tuple.Create(clockId, clock, FileOperationEndpoints.Next(clock, nextClock.GetArray())));
                    return Tuple.Create(changes.ToArray(), Array.Empty<byte>());
                }
            }
            throw new OperationRejectedException("no-production-batch-needed");
        }
        private static bool QueueAuthorityRecipe(CraftingStation station)
        {
            var zdo = ValheimAccess.Zdo(station);
            if (zdo == null || !ValheimAccess.IsNativeOwner(station) || PendingAuthorityRecipes.Contains(zdo.m_uid) ||
                !RecipeStationCompatibility.IsPhysicallyUsable(station, 1, out _)) return false;
            // The native simulation owner checks live roof/fire/extension geometry. The server
            // independently validates the producer definition, links, permissions and item costs.
            var p = new ZPackage(); p.Write(2); p.Write(zdo.m_uid); p.Write(station.GetLevel()); p.Write(zdo.GetLong(RecipeRevisionKey));
            ZDOID id = zdo.m_uid; PendingAuthorityRecipes.Add(id);
            Action<int, string, byte[]> completed = (status, reason, _) => {
                PendingAuthorityRecipes.Remove(id);
                if (station == null) return;
                if (status == (int)OperationStatus.Committed) ProductionStationEffects.RecipeCrafted(station);
                Stop(station, ProductionStopCode.Ready, status == (int)OperationStatus.Committed ? "batch committed" : reason);
            };
            string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), requestId = Guid.NewGuid().ToString("N");
            if (ZNet.instance.IsServer() && Player.m_localPlayer == null)
            {
                var submitted = SharedOperationHost.Submit(world, "server-station:" + zdo.GetString(ProductionEndpointIdentity.TokenStorageKey, ""),
                    requestId, AuthorityRecipeKind, p.GetArray(), completed);
                if (submitted.Item1 != (int)OperationStatus.Queued) completed(submitted.Item1, submitted.Item2, submitted.Item3);
                return submitted.Item1 == (int)OperationStatus.Queued;
            }
            bool sent = InventoryOperationNetwork.Send(world, requestId, AuthorityRecipeKind, p.GetArray(), completed);
            if (!sent) PendingAuthorityRecipes.Remove(id);
            return sent;
        }
        private static AuthorityChest AuthorityRead(ZDO zdo, Dictionary<ZDOID, AuthorityChest> cache)
        {
            if (cache.TryGetValue(zdo.m_uid, out var found)) return found;
            byte[] before = LiveChestWorld.ReadAvailable(zdo);
            var entry = new AuthorityChest { Zdo = zdo, Before = before, Inventory = ManagedChestLedger.Inventory(before) };
            cache.Add(zdo.m_uid, entry); return entry;
        }
        private static Tuple<string, byte[], byte[]> AuthorityChange(AuthorityChest chest, StockInventoryTransition transition) =>
            Tuple.Create(ManagedChestLedger.Endpoint(chest.Zdo.m_uid), chest.Before, ManagedChestLedger.Next(chest.Before,
                NativeInventoryCodec.Load(transition.After.CopyPayload(), chest.Inventory.GetWidth(), chest.Inventory.GetHeight())));
        private static ZDO ResolveAuthorityLink(ZDO station, StoredProductionLink link)
        {
            if (link == null || link.OwnerId == 0 || !ProductionEndpointIdentity.IsCanonicalToken(link.TargetToken) ||
                station.GetLong(ZDOVars.s_creator) != link.StationOwnerId) return null;
            var candidates = NetworkInventoryAccess.Nearby(link.ExpectedPosition, 3f).Where(z => z.GetPrefab() == link.TargetPrefabHash &&
                z.GetString(ProductionEndpointIdentity.TokenStorageKey, "") == link.TargetToken).Take(2).ToArray();
            if (candidates.Length != 1) return null;
            var chest = candidates[0]; float tolerance = Mathf.Clamp(ProductionConfig.MovedTargetTolerance.Value, 0.1f, 3f);
            if (ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(chest)) && !ManagedDrawerInventory.Listed(chest, ProductionConfig.ContainerPrefabIds.Value)) return null;
            if (chest.GetLong(ZDOVars.s_creator) != link.TargetOwnerId || (chest.GetPosition() - link.ExpectedPosition).sqrMagnitude > tolerance * tolerance ||
                !NetworkInventoryAccess.WardAllows(station.GetPosition(), link.OwnerId) ||
                !NetworkInventoryAccess.CanAccessPrincipal(link.OwnerId, station.GetPosition(), chest, LinkRange, true, false, out _)) return null;
            return chest;
        }
        private static IEnumerable<ZDO> AuthorityRecipeInputs(ZDO station, ZDO destination, long principal)
        {
            var selected = new HashSet<ZDOID>(); var protectedChests = new HashSet<ZDOID>();
            foreach (var role in new[] { ProductionLinkRole.Fuel, ProductionLinkRole.Output, ProductionLinkRole.Replenishment })
                if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, role, out var catalog) == StoredRecordState.Valid)
                    foreach (var link in catalog.Links) { var chest = ResolveAuthorityLink(station, link); if (chest != null) protectedChests.Add(chest.m_uid); }
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, ProductionLinkRole.Input, out var inputLinks) == StoredRecordState.Valid)
                foreach (var link in inputLinks.Links)
                {
                    var chest = ResolveAuthorityLink(station, link);
                    if (chest != null && chest != destination && selected.Add(chest.m_uid)) yield return chest;
                }
            if (!ProductionConfig.RecipeNearbyIngredientsEnabled.Value || !NetworkInventoryAccess.WardAllows(station.GetPosition(), principal)) yield break;
            int maximum = Mathf.Clamp(ProductionConfig.RecipeNearbyMaximumSourceChests.Value, 1, NearbyIngredientContainerIndex.HardMaximumSourceChests);
            foreach (var chest in NetworkInventoryAccess.Nearby(station.GetPosition(), LinkRange).OrderBy(z => (z.GetPosition() - station.GetPosition()).sqrMagnitude).ThenBy(z => z.m_uid.ToString(), StringComparer.Ordinal))
            {
                if (ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(chest)) && !ManagedDrawerInventory.Listed(chest, ProductionConfig.ContainerPrefabIds.Value)) continue;
                if (selected.Count >= maximum) yield break;
                if (chest == destination || protectedChests.Contains(chest.m_uid) || selected.Contains(chest.m_uid) ||
                    !NetworkInventoryAccess.CanAccessPrincipal(principal, station.GetPosition(), chest, LinkRange, true, false, out _)) continue;
                selected.Add(chest.m_uid); yield return chest;
            }
        }
    }
}
