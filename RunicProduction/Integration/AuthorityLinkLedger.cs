using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;
using RunicProduction.Contracts;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    // Links and their signed replenishment plans are one durable endpoint. Native ZDO
    // fields are projections only, just like chest contents and timed station state.
    internal static class AuthorityLinkLedger
    {
        internal const string Marker = "runic.production.links-authority.v2";
        internal static readonly ProductionLinkRole[] Roles = { ProductionLinkRole.Input, ProductionLinkRole.Fuel, ProductionLinkRole.Output, ProductionLinkRole.Replenishment };
        internal static readonly string[] Keys = Roles.Select(ProductionRoleLinkCatalogStore.StorageKey)
            .Concat(new[] { MultiReplenishmentCatalogStore.IndexStorageKey }).Concat(MultiReplenishmentCatalogStore.FixedSlotStorageKeys).ToArray();
        private static readonly Dictionary<string, ZDOID> ids = new();
        private static bool projecting;
        internal sealed class Image
        {
            internal string Token, SourceOrigin = ""; internal int Prefab; internal Vector3 Position; internal string[] Values;
            internal byte[] Encode()
            {
                var p = new ZPackage(); p.Write(2); p.Write(Token); p.Write(Prefab); p.Write(Position); p.Write(SourceOrigin);
                foreach (string value in Values) p.Write(value);
                return p.GetArray();
            }
            internal static Image Decode(byte[] data)
            {
                var p = new ZPackage(data); int version = p.ReadInt(); if (version < 1 || version > 2) throw new OperationRejectedException("invalid-link-ledger");
                var body = new Image { Token = p.ReadString(), Prefab = p.ReadInt(), Position = p.ReadVector3(), Values = new string[Keys.Length] };
                if (version >= 2) body.SourceOrigin = p.ReadString();
                for (int i = 0; i < Keys.Length; i++) body.Values[i] = p.ReadString();
                OperationPacket.End(p);
                if (!ProductionEndpointIdentity.IsCanonicalToken(body.Token) || !NetworkInventoryAccess.Finite(body.Position)) throw new OperationRejectedException("invalid-link-ledger");
                return body;
            }
            internal void Set(string key, string value) => Values[Array.IndexOf(Keys, key)] = value;
            internal void Set(MultiReplenishmentCatalog catalog)
            {
                var publication = MultiReplenishmentCatalogStore.Prepare(catalog);
                Set(MultiReplenishmentCatalogStore.IndexStorageKey, publication.FinalIndexRecord);
                for (int i = 0; i < publication.SlotRecords.Count; i++) Set(MultiReplenishmentCatalogStore.FixedSlotStorageKeys[i], publication.SlotRecord(i));
            }
        }
        internal static void Register()
        {
            SharedOperationHost.RegisterProjection("production.links:", Project);
            WorldInventoryOrigin.Register("production.links:", bytes => { var body = Image.Decode(bytes); return Tuple.Create(body.SourceOrigin, body.Prefab, body.Position); },
                (zdo, endpoint, image) => { ids[endpoint] = zdo.m_uid; Project(endpoint, image); });
        }
        internal static void Clear() { ids.Clear(); projecting = false; }
        internal static string Endpoint(ZDO station) => "production.links:" + station.GetString(ProductionEndpointIdentity.TokenStorageKey, "");
        internal static byte[] Read(ZDO station)
        {
            if (!SharedOperationHost.Active || !ZNet.instance.IsServer()) throw new OperationDeferredException("link-authority-starting");
            string token = station.GetString(ProductionEndpointIdentity.TokenStorageKey, "");
            if (!ProductionEndpointIdentity.IsCanonicalToken(token))
            {
                if (station.GetString(Marker, "").Length != 0) throw new OperationRejectedException("station-link-identity-missing");
                token = Guid.NewGuid().ToString("N"); station.Set(ProductionEndpointIdentity.TokenStorageKey, token); station.Set(ProductionEndpointIdentity.PresenceStorageKey, true);
            }
            string endpoint = Endpoint(station); ids[endpoint] = station.m_uid;
            byte[] image = SharedOperationHost.Read(endpoint);
            if (image == null)
            {
                if (station.GetString(Marker, "").Length != 0) throw new OperationRejectedException("station-link-ledger-missing");
                // Import legacy publications once. Migration normalizes existing records but
                // does not acquire station or chest ownership or move any inventory.
                var body = new Image { Token = token, SourceOrigin = WorldInventoryOrigin.Ensure(station), Prefab = station.GetPrefab(), Position = station.GetPosition(), Values = new string[Keys.Length] };
                foreach (var role in Roles)
                {
                    if (ProductionRoleLinkCatalogStore.ReadWithLegacy(station, role, out var catalog) != StoredRecordState.Valid) throw new OperationRejectedException("invalid-station-links");
                    body.Set(ProductionRoleLinkCatalogStore.StorageKey(role), ProductionRoleLinkCatalogStore.Serialize(catalog));
                }
                if (MultiReplenishmentRuntimeSupport.ReadOrMigrate(station, token, true, out var stock, out string reason) != StoredRecordState.Valid)
                    throw new OperationRejectedException(reason);
                body.Set(stock); image = SharedOperationHost.Import(endpoint, body.Encode());
            }
            Project(endpoint, image); return image;
        }
        private static void Project(string endpoint, byte[] image)
        {
            var body = Image.Decode(FileOperationEndpoints.Inventory(image));
            ZDO station = ids.TryGetValue(endpoint, out var id) ? ZDOMan.instance.GetZDO(id) : null;
            if (station == null)
            {
                var found = NetworkInventoryAccess.Nearby(body.Position, 1f).Where(z => z.GetString(ProductionEndpointIdentity.TokenStorageKey, "") == body.Token).Take(2).ToArray();
                if (found.Length != 1) throw new InvalidOperationException("Link station identity is unavailable or ambiguous");
                station = found[0]; ids[endpoint] = station.m_uid;
            }
            if (station.GetPrefab() != body.Prefab) throw new InvalidOperationException("Link station prefab changed");
            if (projecting) return;
            projecting = true;
            try
            {
                if (body.SourceOrigin.Length != 0) station.Set(WorldInventoryOrigin.Key, body.SourceOrigin);
                station.Set(ProductionEndpointIdentity.TokenStorageKey, body.Token); station.Set(ProductionEndpointIdentity.PresenceStorageKey, true);
                for (int i = 0; i < Keys.Length; i++) station.Set(Keys[i], body.Values[i]);
                MultiReplenishmentRuntimeSupport.ClearLegacySingleton(station);
                station.Set(Marker, body.Token); ZDOMan.instance.ForceSendZDO(station.m_uid);
            }
            finally { projecting = false; }
        }
        internal static void ProjectIncoming(ZDO station)
        {
            if (projecting || !InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer() || station == null) return;
            string endpoint = ids.FirstOrDefault(x => x.Value == station.m_uid).Key;
            if (endpoint == null && station.GetString(Marker, "").Length == 0) return;
            endpoint ??= "production.links:" + station.GetString(Marker, "");
            byte[] image = SharedOperationHost.Read(endpoint);
            if (image == null) throw new InvalidOperationException("Managed station links have no durable ledger");
            ids[endpoint] = station.m_uid; Project(endpoint, image);
        }
    }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class AuthorityLinkProjectionPatch
    {
        private static void Postfix(ZDO __instance)
        {
            try { AuthorityLinkLedger.ProjectIncoming(__instance); }
            catch (Exception error) { RunicAutomation.MutationGate.Report("Link projection failed: " + error); }
        }
    }
}
