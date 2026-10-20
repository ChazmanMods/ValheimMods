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
        internal const string AuthorityClockKind = "production.clock.v2";
        private sealed class AuthorityClockRequest
        {
            internal ZDO Station; internal int OwnerRevision; internal long Revision; internal AuthorityStationEnvironment Environment; internal AuthorityStationState Snapshot;
            internal static AuthorityClockRequest Decode(byte[] payload)
            {
                var p = new ZPackage(payload); if (p.ReadInt() != 2) throw new OperationRejectedException("invalid-station-tick");
                var request = new AuthorityClockRequest { Station = ZDOMan.instance.GetZDO(p.ReadZDOID()), OwnerRevision = p.ReadInt(), Revision = p.ReadLong(),
                    Environment = new AuthorityStationEnvironment(p.ReadBool(), p.ReadBool(), p.ReadBool(), p.ReadBool(), p.ReadBool(), p.ReadSingle()),
                    Snapshot = AuthorityStationState.Decode(OperationPacket.Bytes(p)) };
                OperationPacket.End(p); if (request.Station == null) throw new OperationRejectedException("station-unavailable");
                return request;
            }
        }
        internal static void RegisterAuthorityStationClock()
        {
            AuthorityStationLedger.Register(); SharedOperationHost.RegisterBackground(AuthorityClockKind, AuthorizeClock, PlanClock, revisionGuarded: true);
        }
        private static string AuthorizeClock(string actor, byte[] payload)
        {
            try
            {
                var request = AuthorityClockRequest.Decode(payload);
                if (!AuthoritySimulationOwner(actor, request.Station)) return "station-simulation-owner-changed";
                request.Snapshot.ValidateFor(request.Station);
                bool managed = request.Station.GetString(AuthorityStationLedger.Marker, "").Length != 0 || request.Station.GetBool(AuthorityStationLedger.EnrollingKey);
                if (managed) return null;
                if (!ProductionConfig.Enabled.Value) return "production-disabled";
                bool linked = false;
                foreach (var role in new[] { ProductionLinkRole.Input, ProductionLinkRole.Fuel, ProductionLinkRole.Output, ProductionLinkRole.Replenishment })
                {
                    var status = ProductionRoleLinkCatalogStore.ReadWithLegacy(request.Station, role, out var catalog);
                    if (status != StoredRecordState.Valid) return "invalid-station-links";
                    linked |= catalog.Links.Count > 0;
                }
                return linked ? null : "station-has-no-production-links";
            }
            catch (Exception) { return "invalid-station-tick"; }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> PlanClock(string actor, byte[] payload, Func<string, byte[]> read)
        {
            string denied = AuthorizeClock(actor, payload); if (denied != null) throw new OperationRejectedException(denied);
            var request = AuthorityClockRequest.Decode(payload);
            AuthorityLinkLedger.Read(request.Station);
            byte[] before = AuthorityStationLedger.Read(request.Station, request.Snapshot, request.OwnerRevision);
            var chests = new Dictionary<ZDOID, AuthorityChest>();
            var refresh = ProductionConfig.Enabled.Value ? RefreshAuthorityPlans(request.Station, chests) : null;
            if (refresh != null) return Tuple.Create(new[] { refresh,
                Tuple.Create(AuthorityStationLedger.Endpoint(request.Station), before, FileOperationEndpoints.Next(before, FileOperationEndpoints.Inventory(before))) }, Array.Empty<byte>());
            byte[] beforeBody = FileOperationEndpoints.Inventory(before);
            var state = AuthorityStationState.Decode(beforeBody);
            NativeStationWorld.UpgradeBuffer(state);
            bool materialIdle = AuthorityStationClock.IsMaterialIdle(state);
            long idleStarted = materialIdle ? state.Longs[ZDOVars.s_startTime] : 0;
            float idleAccumulator = materialIdle ? state.Floats[ZDOVars.s_accTime] : 0;
            long now = ZNet.instance.GetTime().Ticks;
            AuthorityProductionEvents.Current = new List<Tuple<int, int, long>>();
            try
            {
            // Native station updates already advanced production. Route its current output
            // once; repeating this pass only repeats detached inventory planning and logging.
            if (ProductionConfig.Enabled.Value) AuthorityRouteBuffer(request.Station, state, chests);
            if (ProductionConfig.Enabled.Value)
            {
            if (!NativeStationWorld.Enabled) AuthorityCollectTimed(request.Station, state, now, chests);
            AuthorityRefill(request.Station, state, chests);
            AuthorityFillSmelter(request.Station, state, chests);
            AuthorityFillTimed(request.Station, state, now, request.Environment, chests);
            }
            byte[] next = state.Encode();
            var changes = new List<Tuple<string, byte[], byte[]>>();
            // Unrouted smelter output uses visible native item drops, as it did before 2.0.
            // Creation belongs to the same durable transaction as the consumed station output.
            var nativeSmelter = ZNetScene.instance.GetPrefab(state.Prefab).GetComponent<Smelter>();
            if (!NativeStationWorld.Enabled && nativeSmelter != null && state.Output.NrOfItems() > 0)
            {
                int index = 0;
                foreach (var item in state.Output.GetAllItems().ToArray())
                {
                    string token = OperationRequest.Hash("production-drop:" + state.Token + ":" + request.Revision + ":" + index++).Substring(0, 32).ToLowerInvariant();
                    var offset = nativeSmelter.transform.InverseTransformPoint(nativeSmelter.m_outputPoint.position);
                    var rotation = request.Station.GetRotation();
                    changes.Add(ManagedWorldDrops.Create(token, request.Station.GetPosition() + rotation * offset, item));
                    state.Output.RemoveItem(item);
                }
                next = state.Encode();
            }
            AuthorityAppendChestChanges(chests, changes);
            if (materialIdle)
            {
                // Elide only an otherwise byte-identical idle state. Automatic refueling or
                // input, output routing, link state and every progress change keep the new
                // baseline in the same durable transaction as their semantic change.
                long started = state.Longs[ZDOVars.s_startTime]; float accumulator = state.Floats[ZDOVars.s_accTime];
                state.Longs[ZDOVars.s_startTime] = idleStarted; state.Floats[ZDOVars.s_accTime] = idleAccumulator;
                byte[] withoutClock = state.Encode();
                state.Longs[ZDOVars.s_startTime] = started; state.Floats[ZDOVars.s_accTime] = accumulator;
                if (OwnerOperationQueue.Same(beforeBody, withoutClock)) next = beforeBody;
            }
            if (!OwnerOperationQueue.Same(beforeBody, next))
                changes.Add(Tuple.Create(AuthorityStationLedger.Endpoint(request.Station), before, FileOperationEndpoints.Next(before, next)));
            return Tuple.Create(changes.ToArray(), AuthorityProductionEvents.Encode());
            }
            finally { AuthorityProductionEvents.Current = null; }
        }
    }
}
