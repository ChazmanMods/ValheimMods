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
        internal const string AuthorityLinkKind = "production.link.v2";
        private sealed class LinkRequest
        {
            internal ZDO Station, Chest; internal ProductionLinkRole Role; internal bool Remove;
            internal static LinkRequest Decode(byte[] payload)
            {
                var p = new ZPackage(payload); if (p.ReadInt() != 1) throw new OperationRejectedException("invalid-link-request");
                var r = new LinkRequest { Station = ZDOMan.instance.GetZDO(p.ReadZDOID()), Chest = ZDOMan.instance.GetZDO(p.ReadZDOID()), Role = (ProductionLinkRole)p.ReadInt(), Remove = p.ReadBool() };
                OperationPacket.End(p);
                if (r.Station == null || r.Chest == null || r.Station == r.Chest || !AuthorityLinkLedger.Roles.Contains(r.Role)) throw new OperationRejectedException("link-object-unavailable");
                return r;
            }
        }
        internal static void RegisterAuthorityLinks()
        {
            AuthorityLinkLedger.Register(); SharedOperationHost.Register(AuthorityLinkKind, AuthorizeLink, PlanLink);
        }
        private static ProductionStationKind LinkStationKind(GameObject prefab)
        {
            if (prefab?.GetComponent<Smelter>() != null) return ProductionStationKind.Smelter;
            if (prefab?.GetComponent<CookingStation>() != null && _cookingPolicy.Allows(prefab.name)) return ProductionStationKind.Cooking;
            if (prefab?.GetComponent<CraftingStation>() != null && _recipePolicy.Allows(prefab.name)) return ProductionStationKind.Recipe;
            if (prefab?.GetComponent<Fermenter>() != null && _fermenterPolicy.Allows(prefab.name)) return ProductionStationKind.Fermenter;
            if (prefab?.GetComponent<SapCollector>() != null) return ProductionStationKind.SapExtractor;
            if (prefab?.GetComponent<WispSpawner>() != null) return ProductionStationKind.WispFountain;
            if (prefab?.GetComponent<Beehive>() != null) return ProductionStationKind.Beehive;
            var fireplace = prefab?.GetComponent<Fireplace>();
            if (fireplace != null && fireplace.m_canRefill && !fireplace.m_infiniteFuel) return ProductionStationKind.Fireplace;
            throw new OperationRejectedException("unsupported-production-station");
        }
        private static string AuthorizeLink(string actor, byte[] payload)
        {
            try
            {
                if (!ProductionConfig.Enabled.Value) return "production-disabled";
                var r = LinkRequest.Decode(payload);
                if (!NetworkInventoryAccess.TryPosition(actor, out long principal, out _)) return "actor-unavailable";
                if (!NetworkInventoryAccess.CanAccess(actor, r.Chest, 5f, true, false, out string reason)) return reason;
                if (!NetworkInventoryAccess.WardAllows(r.Station.GetPosition(), principal)) return "station-access-denied";
                if ((r.Station.GetPosition() - r.Chest.GetPosition()).sqrMagnitude > LinkRange * LinkRange) return "link-out-of-range";
                return StationSupportsRole(LinkStationKind(ZNetScene.instance.GetPrefab(r.Station.GetPrefab())), r.Role) ? null : "unsupported-station-role";
            }
            catch (OperationRejectedException error) { return error.Message; }
            catch { return "invalid-link-request"; }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> PlanLink(string actor, byte[] payload, Func<string, byte[]> read)
        {
            string denied = AuthorizeLink(actor, payload); if (denied != null) throw new OperationRejectedException(denied);
            var r = LinkRequest.Decode(payload); NetworkInventoryAccess.TryPosition(actor, out long principal, out _);
            byte[] chest = ManagedChestLedger.Read(r.Chest);
            byte[] before = AuthorityLinkLedger.Read(r.Station);
            var body = AuthorityLinkLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(r.Station, r.Role, out var roles) != StoredRecordState.Valid ||
                MultiReplenishmentCatalogStore.Read(r.Station, out var stock) != StoredRecordState.Valid) throw new OperationRejectedException("invalid-station-links");
            // The owner snapshot carries the identity even before native ZDO replication arrives.
            string token = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(chest)).ProductionToken;
            if (!ProductionEndpointIdentity.IsCanonicalToken(token)) throw new OperationRejectedException("container-link-identity-invalid");
            var previous = roles.FindTarget(token, r.Chest.GetPrefab());
            ProductionRoleLinkCatalog updated;
            string failure;
            if (r.Remove)
            {
                if (previous == null) return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
                if (!ProductionRoleLinkCatalogPolicy.TryRemove(roles, token, r.Chest.GetPrefab(), out updated, out failure)) throw new OperationRejectedException(failure);
                if (r.Role == ProductionLinkRole.Replenishment && stock.TryGetByTargetIdentity(previous, out var current))
                {
                    if (!MultiReplenishmentCatalogPolicy.TryRemove(stock, current.LinkId, out var next, out _, out var change)) throw new OperationRejectedException("remove-stock-plan:" + change);
                    body.Set(next);
                }
            }
            else
            {
                var link = new StoredProductionLink { LinkId = previous?.LinkId ?? Guid.NewGuid().ToString("N"), Revision = checked((previous?.Revision ?? 0) + 1),
                    Role = r.Role, Target = r.Chest.m_uid, TargetToken = token, TargetPrefabHash = r.Chest.GetPrefab(), ExpectedPosition = r.Chest.GetPosition(),
                    OwnerId = principal, StationOwnerId = r.Station.GetLong(ZDOVars.s_creator), TargetOwnerId = r.Chest.GetLong(ZDOVars.s_creator) };
                if (!ProductionRoleLinkCatalogPolicy.TryAddOrRefresh(roles, link, Math.Max(1, Math.Min(ProductionRoleLinkCatalog.HardMaximumLinks, ProductionConfig.MaximumLinksPerRole.Value)),
                    out updated, out var published, out failure)) throw new OperationRejectedException(failure);
                if (r.Role == ProductionLinkRole.Replenishment)
                {
                    var prefab = ZNetScene.instance.GetPrefab(r.Station.GetPrefab());
                    var inventory = ManagedChestLedger.Inventory(chest);
                    string name = Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == principal ? Player.m_localPlayer.GetPlayerName() :
                        ZNet.instance.GetPeers().First(p => NetworkInventoryAccess.TryPeerPrincipal(p, out long peerPrincipal, out _) && peerPrincipal == principal).m_playerName;
                    if (!BuildAuthorityPlan(prefab, body.Token, published, inventory, principal, name, out var plan, out failure)) throw new OperationRejectedException(failure);
                    bool refresh = stock.TryGetByTargetIdentity(published, out var current);
                    MultiReplenishmentCatalog next; ReplenishmentCatalogChangeCode change;
                    bool success = refresh ? MultiReplenishmentCatalogPolicy.TryRefresh(stock, current.LinkId, published, plan, out next, out _, out change) :
                        MultiReplenishmentCatalogPolicy.TryAdd(stock, published, plan, MultiReplenishmentCatalog.HardMaximumDestinations, out next, out _, out change);
                    if (!success) throw new OperationRejectedException("stock-plan:" + change);
                    body.Set(next);
                }
            }
            body.Set(ProductionRoleLinkCatalogStore.StorageKey(r.Role), ProductionRoleLinkCatalogStore.Serialize(updated));
            return Tuple.Create(new[] { Tuple.Create(AuthorityLinkLedger.Endpoint(r.Station), before, FileOperationEndpoints.Next(before, body.Encode())) }, Array.Empty<byte>());
        }
        private static bool BuildAuthorityPlan(GameObject prefab, string token, StoredProductionLink link, Inventory inventory, long actor, string name, out ReplenishmentPlan plan, out string failure)
        {
            plan = null; failure = "unsupported-replenishment-station";
            var cooking = prefab.GetComponent<CookingStation>(); var fermenter = prefab.GetComponent<Fermenter>(); var recipe = prefab.GetComponent<CraftingStation>();
            if (cooking != null)
                return TimedCookingStockModel.TryDescribe(cooking, prefab.name, out var descriptor, out failure) &&
                    TimedCookingPlanBuilder.TryBuild(descriptor, inventory, link, token, actor, name, null, out plan, out failure);
            if (fermenter != null)
                return FermenterCompatibility.TryDescribeRegistered(fermenter, prefab.name, out var descriptor, out failure) &&
                    FermenterPlanBuilder.TryBuild(descriptor, inventory, link, token, actor, name, null, out plan, out failure);
            return recipe != null && RecipePlanBuilder.TryBuild(recipe, token, prefab.name, link, inventory, actor, name, null,
                _cookingInputPolicy, _fermenterInputPolicy, out plan, out failure, requirePhysical: false);
        }
        private static Tuple<string, byte[], byte[]> RefreshAuthorityPlans(ZDO station, Dictionary<ZDOID, AuthorityChest> cache)
        {
            byte[] before = AuthorityLinkLedger.Read(station);
            var body = AuthorityLinkLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
            if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, ProductionLinkRole.Replenishment, out var links) != StoredRecordState.Valid ||
                MultiReplenishmentCatalogStore.Read(station, out var catalog) != StoredRecordState.Valid) throw new OperationRejectedException("invalid-station-links");
            foreach (var link in links.Links)
            {
                var chest = ResolveAuthorityLink(station, link); if (chest == null) continue;
                if (!TryReadAuthorityTransferChest(station, chest, cache, out var available)) continue;
                bool existing = catalog.TryGetByTargetIdentity(link, out var current);
                string name = current?.Plan?.AuthorizedPlayerName ?? ("Production link " + link.OwnerId);
                if (!BuildAuthorityPlan(ZNetScene.instance.GetPrefab(station.GetPrefab()), body.Token, link,
                    available.Inventory, link.OwnerId, name, out var plan, out _)) continue;
                if (existing && current.Link.Revision == link.Revision && SameTargetSet(current.Plan, plan)) continue;
                MultiReplenishmentCatalog next; ReplenishmentCatalogChangeCode change;
                bool success = existing ? MultiReplenishmentCatalogPolicy.TryRefresh(catalog, current.LinkId, link, plan, out next, out _, out change) :
                    MultiReplenishmentCatalogPolicy.TryAdd(catalog, link, plan, MultiReplenishmentCatalog.HardMaximumDestinations, out next, out _, out change);
                if (!success) throw new OperationRejectedException("refresh-stock-plan:" + change);
                body.Set(next);
                // One bounded metadata refresh per station quantum. The caller advances its
                // durable tick revision with this change before attempting any production.
                return Tuple.Create(AuthorityLinkLedger.Endpoint(station), before, FileOperationEndpoints.Next(before, body.Encode()));
            }
            return null;
        }
        private static bool QueueAuthorityLink(Player player, ProductionLinkSelection selection, ZDO station, ZDO chest, bool remove, out string detail)
        {
            detail = "Production link request queued.";
            if (!InventoryOperationNetwork.Ready) { detail = "Runic Production is waiting for the server. Match your installed Runic modules and builds on client and server."; return false; }
            var p = new ZPackage(); p.Write(1); p.Write(station.m_uid); p.Write(chest.m_uid); p.Write((int)selection.Role); p.Write(remove);
            bool sent = InventoryOperationNetwork.Send(ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"), AuthorityLinkKind, p.GetArray(),
                (status, reason, _) => { if (Player.m_localPlayer == player) player.Message(MessageHud.MessageType.Center,
                    status == (int)OperationStatus.Committed ? (remove ? "Production link removed." : "Production link saved.") : "Production link: " + reason); });
            if (!sent) detail = "Production link queue is busy. Retry shortly.";
            return sent;
        }
    }
}
