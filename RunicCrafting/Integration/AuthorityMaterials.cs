using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicAutomation.Operations;
using RunicCrafting.Domain;
using UnityEngine;

namespace RunicCrafting.Integration
{
    internal sealed class AuthorityMaterialRequest
    {
        internal int Mode; // recipe, building, manual cooking/refueling
        internal ZDOID Station;
        internal readonly Dictionary<string, int> Needed = new(StringComparer.Ordinal);
        internal byte[] Escrow;
        internal byte[] Encode()
        {
            var p = new ZPackage(); p.Write(1); p.Write(Mode); p.Write(Station); p.Write(Needed.Count);
            foreach (var need in Needed.OrderBy(x => x.Key, StringComparer.Ordinal)) { p.Write(need.Key); p.Write(need.Value); }
            p.Write(Escrow); return p.GetArray();
        }
        internal static AuthorityMaterialRequest Decode(byte[] bytes)
        {
            var p = new ZPackage(bytes);
            if (p.ReadInt() != 1) throw new InvalidDataException("Unsupported material request");
            var result = new AuthorityMaterialRequest { Mode = p.ReadInt(), Station = p.ReadZDOID() };
            int count = p.ReadInt();
            if (result.Mode < 0 || result.Mode > 2 || count < 1 || count > 128) throw new InvalidDataException("Invalid material request");
            for (int n = 0; n < count; n++)
            {
                string id = p.ReadString(); int amount = p.ReadInt();
                if (id.Length < 1 || id.Length > 128 || amount < 1 || amount > ushort.MaxValue || result.Needed.ContainsKey(id))
                    throw new InvalidDataException("Invalid material quantity");
                result.Needed.Add(id, amount);
            }
            result.Escrow = OperationPacket.Bytes(p); OperationPacket.End(p); return result;
        }
    }

    // Nearby materials are reserved by the same serialized authority as chest clicks. The reply
    // is a durable delivery, so a lost reply cannot debit the chest twice on the next attempt.
    internal static class AuthorityMaterials
    {
        internal const string Kind = "crafting.materials.v2";
        internal static void Register()
        {
            SharedOperationHost.Register(Kind, Authorize, Plan);
            NearbyInteractionMaterials.Register(fuel => Configuration.Enabled.Value &&
                (fuel ? Configuration.RefuelFromContainers.Value : Configuration.CookFromContainers.Value), PlanInteraction);
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> PlanInteraction(string actor, ZDOID station, bool fuel,
            string[] items, Func<string, byte[]> read)
        {
            if (!Configuration.Enabled.Value || !(fuel ? Configuration.RefuelFromContainers.Value : Configuration.CookFromContainers.Value))
                throw new OperationRejectedException("nearby-interaction-disabled");
            foreach (string item in items.Distinct(StringComparer.Ordinal))
            {
                var request = new AuthorityMaterialRequest { Mode = 2, Station = station,
                    Escrow = NativeInventoryCodec.Save(new Inventory("Nearby input", null, 32, 32)) };
                request.Needed[item] = 1;
                try { return Plan(actor, request.Encode(), read); }
                catch (OperationRejectedException error) when (error.Message.StartsWith("insufficient-materials:", StringComparison.Ordinal)) { }
            }
            throw new OperationRejectedException("no-suitable-nearby-item");
        }
        private static string Scope(string actor, AuthorityMaterialRequest request, out Vector3 origin, out float range)
        {
            origin = Vector3.zero; range = 0;
            if (!Configuration.Enabled.Value || !NetworkInventoryAccess.TryPosition(actor, out long principal, out var playerPosition)) return "actor-unavailable";
            if ((request.Mode == 0 && !Configuration.CraftFromContainers.Value) ||
                (request.Mode == 1 && !Configuration.BuildFromContainers.Value) ||
                (request.Mode == 2 && !Configuration.CookFromContainers.Value && !Configuration.RefuelFromContainers.Value)) return "nearby-materials-disabled";
            origin = playerPosition; range = Configuration.SafeRangeCap;
            var station = request.Station.IsNone() ? null : ZDOMan.instance.GetZDO(request.Station);
            if (request.Mode == 0 && station == null) return "crafting-station-unavailable";
            if (!request.Station.IsNone() && station == null) return "station-unavailable";
            if (station != null)
            {
                var prefab = ZNetScene.instance.GetPrefab(station.GetPrefab());
                var crafting = prefab == null ? null : prefab.GetComponent<CraftingStation>();
                // Building reaches this reservation through native station/placement checks,
                // including Build Camera and other mods' effective build range. Rebuilding
                // that range from server prefab data rejects otherwise valid placements.
                // Material sources remain player-centered and server access-checked below.
                if (request.Mode != 1 && (station.GetPosition() - playerPosition).sqrMagnitude > 100f)
                    return "station-out-of-range";
                if (request.Mode != 2 && crafting == null) return "invalid-crafting-station";
                if (crafting != null)
                    foreach (var action in new[] { WorkshopAction.StationUse, WorkshopAction.LocalMaterialUse })
                    {
                        var access = WorkshopAccessRuntime.EvaluateAuthority(station, actor, action);
                        if (!access.Allowed) return access.ReasonCode;
                    }
                else if (!NetworkInventoryAccess.WardAllows(station.GetPosition(), principal)) return "station-ward-denied";
                if (request.Mode != 1) origin = station.GetPosition();
                if (request.Mode == 0) range = Math.Max(1f, Math.Min(range, AuthorityStationRange.Resolve(station, crafting)));
            }
            else if (request.Mode == 1)
            {
                if (!Configuration.StationlessBuildFromContainers.Value) return "stationless-building-disabled";
                range = Configuration.SafeStationlessBuildRange;
            }
            if (request.Mode == 2) range = Configuration.SafeInteractionRange;
            return null;
        }
        private static string Authorize(string actor, byte[] payload)
        {
            try { return Scope(actor, AuthorityMaterialRequest.Decode(payload), out _, out _); }
            catch (Exception) { return "invalid-material-request"; }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] payload, Func<string, byte[]> read)
        {
            var request = AuthorityMaterialRequest.Decode(payload);
            string denied = Scope(actor, request, out Vector3 origin, out float range);
            if (denied != null) throw new OperationRejectedException(denied);
            var delivery = NativeInventoryCodec.Load(request.Escrow, 32, 32);
            if (delivery.NrOfItems() != 0) throw new OperationRejectedException("unexpected-carried-items");
            NetworkInventoryAccess.TryPosition(actor, out _, out var playerPosition);
            var candidates = NetworkInventoryAccess.Nearby(origin, range)
                .Where(z => NetworkInventoryAccess.Descriptor(z) != null)
                .OrderBy(z => (z.GetPosition() - origin).sqrMagnitude).ThenBy(z => z.m_uid.ToString(), StringComparer.Ordinal)
                .Take(Configuration.SafeMaximumCandidates).ToArray();
            var sources = new List<Tuple<ZDO, byte[], Inventory>>(); bool waiting = false, snapshotRequested = false;
            foreach (var chest in candidates)
            {
                if (ManagedDrawerInventory.IsDrawer(NetworkInventoryAccess.Descriptor(chest)) &&
                    !ManagedDrawerInventory.Listed(chest, Runic.Compatibility.ModdedContainerCompatibility.PullIds(Configuration.PullPrefabIds.Value))) continue;
                float actorRange = Math.Min(100f, range + Vector3.Distance(origin, playerPosition));
                if (!NetworkInventoryAccess.CanAccess(actor, chest, actorRange, Configuration.RequireWardAccess.Value,
                    Configuration.ExcludePersonalContainers.Value, out _)) continue;
                try
                {
                    byte[] before = ManagedChestLedger.Read(chest);
                    var snapshot = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(before));
                    sources.Add(Tuple.Create(chest, before, NativeInventoryCodec.Load(snapshot.Items, snapshot.Width, snapshot.Height)));
                    if (sources.Count >= Configuration.SafeMaximumReturned) break;
                }
                catch (Exception e) when ((e.Data["Runic.OperationDisposition"] as string) == "deferred")
                {
                    waiting = true;
                    snapshotRequested |= e.Message == "container-live-state-requested";
                }
            }
            foreach (var need in request.Needed)
            {
                int remaining = need.Value;
                foreach (var source in sources)
                    foreach (var item in source.Item3.GetAllItems().Where(x => ValheimReflection.IsUsableRequirementItem(x, need.Key)).ToArray())
                    {
                        int amount = Math.Min(remaining, item.m_stack);
                        if (amount == 0) break;
                        if (!NativeInventoryCodec.Transfer(source.Item3, delivery, item.m_gridPos, NativeInventoryCodec.ItemIdentity(item), amount))
                            throw new OperationRejectedException("material-delivery-too-large");
                        remaining -= amount;
                    }
                if (remaining > 0)
                {
                    // Preserve the live snapshot's fast retry classification. Unavailable
                    // owners keep ordinary backoff; no request, debit or receipt is recreated.
                    if (waiting) throw new OperationDeferredException(snapshotRequested
                        ? "container-live-state-requested" : "enrolling-nearby-inventory");
                    throw new OperationRejectedException("insufficient-materials:" + need.Key);
                }
            }
            var changes = new List<Tuple<string, byte[], byte[]>>();
            foreach (var source in sources)
            {
                var snapshot = ManagedChestLedger.Image.Decode(FileOperationEndpoints.Inventory(source.Item2));
                if (!OwnerOperationQueue.Same(snapshot.Items, NativeInventoryCodec.Save(source.Item3)))
                    changes.Add(Tuple.Create(ManagedChestLedger.Endpoint(source.Item1.m_uid), source.Item2,
                        ManagedChestLedger.Next(source.Item2, source.Item3, snapshot.Rules)));
            }
            byte[] result = NativeInventoryCodec.Save(delivery);
            if (result.Length > 65536) throw new OperationRejectedException("material-delivery-too-large");
            return Tuple.Create(changes.ToArray(), result);
        }
    }
}
