using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RunicAutomation.Operations;

namespace RunicProduction.Integration
{
    internal static class AuthorityStationLedger
    {
        internal const string Marker = "runic.production.authority.v2";
        internal const string RevisionKey = "runic.production.revision.v2";
        internal const string EnrollingKey = "runic.production.enrolling.v2";
        private static readonly Dictionary<string, ZDOID> ids = new();
        private static bool projecting;
        internal static string Endpoint(ZDO station) => "station:" + station.GetString(ProductionEndpointIdentity.TokenStorageKey, "");
        internal static void Register()
        {
            if (NativeStationWorld.Enabled) { NativeStationWorld.Register(); return; }
            SharedOperationHost.RegisterProjection("station:", ProjectImage);
            WorldInventoryOrigin.Register("station:", bytes => { var body = AuthorityStationState.Decode(bytes); return Tuple.Create(body.SourceOrigin, body.Prefab, body.Position); },
                (zdo, endpoint, image) => {
                    var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(image));
                    zdo.Set(ProductionEndpointIdentity.TokenStorageKey, body.Token); zdo.Set(Marker, body.Token);
                    ids[endpoint] = zdo.m_uid; ProjectImage(endpoint, image);
                }, (endpoint, image) => {
                    var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(image));
                    if (body.Retired || InventoryWorldLifecycle.RemovalPending(AuthorityStationRetirement.Kind, body.Token)) return;
                    var missing = ZDOMan.instance.CreateNewZDO(body.Position, body.Prefab);
                    missing.SetPrefab(body.Prefab); missing.Set(ProductionEndpointIdentity.TokenStorageKey, body.Token);
                    missing.Set(Marker, body.Token); missing.Set(WorldInventoryOrigin.Key, body.SourceOrigin);
                    missing.SetOwner(ZDOMan.GetSessionID()); ids[endpoint] = missing.m_uid;
                    InventoryWorldLifecycle.RecoverRemoved(missing.m_uid, AuthorityStationRetirement.Kind, body.Token);
                });
        }
        internal static void Clear() { ids.Clear(); projecting = false; }
        internal static byte[] Read(ZDO station, AuthorityStationState enrollment, int ownerRevision)
        {
            if (NativeStationWorld.Enabled) return NativeStationWorld.Read(station);
            string endpoint = Endpoint(station); ids[endpoint] = station.m_uid;
            byte[] current = SharedOperationHost.Read(endpoint);
            if (current == null)
            {
                if (station.GetString(Marker, "").Length != 0) throw new OperationRejectedException("station-ledger-missing");
                if (station.OwnerRevision != ownerRevision) throw new OperationRejectedException("station-owner-changed-before-enrollment");
                enrollment.ValidateFor(station);
                enrollment.SourceOrigin = WorldInventoryOrigin.Ensure(station);
                if (enrollment.Output.NrOfItems() != 0) throw new OperationRejectedException("station-enrollment-cannot-create-buffered-output");
                current = SharedOperationHost.Import(endpoint, enrollment.Encode());
            }
            var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(current)); body.ValidateFor(station);
            if (body.Retired) throw new OperationRejectedException("station-was-destroyed");
            ProjectImage(endpoint, current); return current;
        }
        private static void ProjectImage(string endpoint, byte[] image)
        {
            var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(image));
            if (body.Retired) return;
            ZDO station = ids.TryGetValue(endpoint, out ZDOID id) ? ZDOMan.instance.GetZDO(id) : null;
            if (station == null)
            {
                var matches = NetworkInventoryAccess.Nearby(body.Position, 1f)
                    .Where(z => z.GetString(ProductionEndpointIdentity.TokenStorageKey, "") == body.Token).Take(2).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Timed station projection identity is unavailable or ambiguous");
                station = matches[0]; ids[endpoint] = station.m_uid;
            }
            if (projecting) return;
            projecting = true;
            try { body.Project(station); station.Set(Marker, body.Token); station.Set(EnrollingKey, false); station.Set(RevisionKey, FileOperationEndpoints.Revision(image)); ZDOMan.instance.ForceSendZDO(station.m_uid); }
            finally { projecting = false; }
        }
        internal static void ProjectIncoming(ZDO station)
        {
            if (NativeStationWorld.Enabled) return;
            if (projecting || !InventoryOperationNetwork.Ready || ZNet.instance == null || !ZNet.instance.IsServer() ||
                station == null) return;
            string endpoint = ids.FirstOrDefault(p => p.Value == station.m_uid).Key;
            if (endpoint == null && station.GetString(Marker, "").Length == 0) return;
            endpoint ??= Endpoint(station); byte[] image = SharedOperationHost.Read(endpoint);
            if (image == null) throw new InvalidOperationException("Timed station has no authoritative ledger");
            // An older owner packet can predate the enrollment marker and persistent token.
            // Restore identity only for a world object already bound by this authority session.
            var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(image));
            if (station.GetPrefab() != body.Prefab) throw new InvalidOperationException("Timed station prefab changed");
            station.Set(ProductionEndpointIdentity.TokenStorageKey, body.Token);
            ids[endpoint] = station.m_uid; ProjectImage(endpoint, image);
        }
    }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class AuthorityStationProjectionPatch
    {
        private static void Postfix(ZDO __instance)
        {
            try { AuthorityStationLedger.ProjectIncoming(__instance); }
            catch (Exception error) { RunicAutomation.MutationGate.Report("Timed station projection failed: " + error); }
        }
    }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class AuthorityStationEndWorldPatch
    { private static void Postfix() { AuthorityStationLedger.Clear(); AuthorityLinkLedger.Clear(); ProductionRuntime.ClearAuthorityPending(); } }
}
