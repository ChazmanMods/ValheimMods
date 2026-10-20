using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class AuthorityStationRetirement
    {
        internal const string Kind = "production.retire.v2";
        internal static void Register()
        {
            SharedOperationHost.Register(Kind, (actor, _) => actor == "world-destruction" ? null : "internal-world-operation-only", Plan);
            InventoryWorldLifecycle.RegisterAdapter("production", zdo =>
                !NativeStationWorld.Enabled && (zdo.GetString(AuthorityStationLedger.Marker, "").Length != 0 || zdo.GetBool(AuthorityStationLedger.EnrollingKey))
                    ? Tuple.Create(Kind, zdo.GetString(ProductionEndpointIdentity.TokenStorageKey, "")) : null, Recover);
            InventoryWorldLifecycle.RegisterRemovalSnapshot("production", CaptureRemoval);
        }
        private static byte[] CaptureRemoval(ZDO station)
        {
            if (SharedOperationHost.Read(AuthorityStationLedger.Endpoint(station)) != null) return null;
            // A connected owner may have left the area before its enrollment request
            // arrived. Destruction cannot wait for that owner after native refunds ran.
            // Keep the last replicated native contents before ZDOMan releases the ZDO.
            if (station.GetString(AuthorityStationLedger.Marker, "").Length != 0)
                throw new OperationRejectedException("station-ledger-missing");
            var snapshot = AuthorityStationState.Capture(station);
            snapshot.SourceOrigin = WorldInventoryOrigin.Ensure(station);
            return snapshot.Encode();
        }
        private static void Recover()
        {
            foreach (var record in SharedOperationHost.Enumerate("station:"))
            {
                var body = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(record.Item2));
                if (!body.Retired) continue;
                foreach (var zdo in NetworkInventoryAccess.Nearby(body.Position, 1).Where(z => z.GetString(ProductionEndpointIdentity.TokenStorageKey, "") == body.Token))
                    InventoryWorldLifecycle.RecoverRemoved(zdo.m_uid, Kind, body.Token);
            }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] payload, Func<string, byte[]> read)
        {
            var p = new ZPackage(payload); string token = p.ReadString(); OperationPacket.End(p);
            if (!ProductionEndpointIdentity.IsCanonicalToken(token)) throw new OperationRejectedException("invalid-station-token");
            string endpoint = "station:" + token; byte[] before = read(endpoint);
            if (before == null)
            {
                byte[] snapshot = InventoryWorldLifecycle.PendingSnapshot(Kind, token);
                if (snapshot == null) throw new OperationRejectedException("destroyed-station-snapshot-unavailable");
                var captured = AuthorityStationState.Decode(snapshot);
                if (captured.Token != token) throw new OperationRejectedException("destroyed-station-snapshot-identity");
                before = SharedOperationHost.Import(endpoint, snapshot);
            }
            var state = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(before));
            if (state.Retired) return Tuple.Create(Array.Empty<Tuple<string, byte[], byte[]>>(), Array.Empty<byte>());
            var prefab = ZNetScene.instance.GetPrefab(state.Prefab);
            if (prefab == null) throw new OperationRejectedException("station-prefab-unavailable");
            var items = state.Output.GetAllItems().Select(i => i.Clone()).ToList();
            void Add(ItemDrop drop, int count, bool cheated = false)
            {
                if (count == 0) return;
                if (drop == null || count < 0 || count > ushort.MaxValue) throw new OperationRejectedException("station-salvage-conversion-unavailable");
                var item = drop.m_itemData.Clone(); item.m_dropPrefab = drop.gameObject; item.m_stack = count;
                item.m_worldLevel = (byte)Game.m_worldLevel; item.m_cheated = cheated && !PlayerProfile.s_bypassCheatChecks;
                item.m_equipped = false; item.m_pickedUp = false; item.m_durability = item.GetMaxDurability(); items.Add(item);
            }
            if (prefab.TryGetComponent<Smelter>(out var smelter))
            {
                Add(smelter.m_fuelItem, (int)state.Floats[ZDOVars.s_fuel]);
                int queued = state.Integers[ZDOVars.s_queued];
                if (queued > smelter.m_maxOre) throw new OperationRejectedException("invalid-smelter-queue");
                for (int n = 0; n < queued; n++)
                    Add(ObjectDB.instance.GetItemPrefab(state.Strings[("item" + n).GetStableHashCode()])?.GetComponent<ItemDrop>(), 1, state.Booleans[ZDOVars.s_cheatedQueued]);
                int processed = state.Integers[ZDOVars.s_spawnAmount];
                if (processed > 0) Add(smelter.m_conversion.FirstOrDefault(c => c.m_from == null || c.m_from.name == state.Strings[ZDOVars.s_spawnOre])?.m_to, processed, state.Booleans[ZDOVars.s_cheatedQueued]);
            }
            else if (prefab.TryGetComponent<CookingStation>(out var cooking))
            {
                Add(cooking.m_fuelItem, (int)state.Floats[ZDOVars.s_fuel]);
                for (int n = 0; n < cooking.m_slots.Length; n++)
                {
                    string name = state.Strings[("slot" + n).GetStableHashCode()];
                    if (name.Length > 0) Add(ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>(), 1, state.Booleans[ZDOVars.s_cheatedQueued + n]);
                }
            }
            else if (prefab.TryGetComponent<Fermenter>(out var fermenter))
            {
                int hash = state.Integers[ZDOVars.s_content]; string legacy = state.Strings[ZDOVars.s_content];
                if (hash != 0 || legacy.Length > 0)
                {
                    var conversion = fermenter.m_conversion.FirstOrDefault(c => c.m_from != null && (hash != 0 ? c.m_from.name.GetStableHashCode() == hash : c.m_from.name == legacy));
                    bool ready = state.Longs[ZDOVars.s_startTime] > 0 && (ZNet.instance.GetTime().Ticks - state.Longs[ZDOVars.s_startTime]) / (double)TimeSpan.TicksPerSecond > fermenter.m_fermentationDuration;
                    Add(ready ? conversion?.m_to : conversion?.m_from, ready ? conversion?.m_producedItems ?? 0 : 1, state.Booleans[ZDOVars.s_cheatedQueued]);
                }
            }
            else if (prefab.TryGetComponent<Beehive>(out var hive))
                Add(hive.m_honeyItem, checked(state.Integers[ZDOVars.s_level] * Game.instance.ScaleDrops(hive.m_honeyItem.m_itemData, 1)));
            // Native fireplaces do not refund remaining fuel when dismantled.
            var changes = new List<Tuple<string, byte[], byte[]>>();
            var recoveryPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            var chest = recoveryPrefab?.GetComponent<Container>();
            if (chest == null) throw new OperationRejectedException("recovery-container-unavailable");
            int index = 0; var inventory = new Inventory("Recovered station items", null, chest.m_width, chest.m_height);
            void Flush()
            {
                if (inventory.NrOfItems() == 0) return;
                if (index >= 126) throw new OperationRejectedException("station-recovery-too-large");
                string recoveredToken = OperationRequest.Hash("station-salvage:" + token + ":" + index).Substring(0, 32).ToLowerInvariant();
                var empty = new Inventory("empty", null, chest.m_width, chest.m_height);
                var body = new ManagedChestLedger.Image { Prefab = recoveryPrefab.name.GetStableHashCode(), Width = chest.m_width, Height = chest.m_height,
                    Position = state.Position + Vector3.right * (index % 4) * .5f + Vector3.forward * (index / 4) * .5f, Token = recoveredToken, Salvage = true, Retired = true, Items = NativeInventoryCodec.Save(empty) };
                string destination = "chest:" + recoveredToken;
                byte[] prior = read(destination) ?? SharedOperationHost.Import(destination, body.Encode());
                if (FileOperationEndpoints.Revision(prior) != 0) throw new OperationRejectedException("station-salvage-already-used");
                body.Items = NativeInventoryCodec.Save(inventory); changes.Add(Tuple.Create(destination, prior, FileOperationEndpoints.Next(prior, body.Encode())));
                inventory = new Inventory("Recovered station items", null, chest.m_width, chest.m_height); index++;
            }
            foreach (var item in items)
                while (item.m_stack > 0)
                {
                    int amount = Math.Min(item.m_stack, NativeInventoryCodec.CapacityFor(inventory, item));
                    if (amount == 0)
                    {
                        if (inventory.NrOfItems() == 0) throw new OperationRejectedException("station-item-cannot-fit-recovery-container");
                        Flush(); continue;
                    }
                    var part = item.Clone(); part.m_stack = amount;
                    if (!NativeInventoryCodec.AddExact(inventory, part)) throw new OperationRejectedException("station-salvage-capacity-changed");
                    item.m_stack -= amount;
                }
            Flush(); state.Retired = true; state.Output.GetAllItems().Clear();
            foreach (var key in state.Floats.Keys.ToArray()) state.Floats[key] = 0;
            foreach (var key in state.Integers.Keys.ToArray()) state.Integers[key] = 0;
            foreach (var key in state.Longs.Keys.ToArray()) state.Longs[key] = 0;
            foreach (var key in state.Strings.Keys.ToArray()) state.Strings[key] = "";
            foreach (var key in state.Booleans.Keys.ToArray()) state.Booleans[key] = false;
            changes.Add(Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, state.Encode())));
            return Tuple.Create(changes.ToArray(), Array.Empty<byte>());
        }
    }
    internal static class AuthorityStationNativeDestructionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Smelter), "OnDestroyed");
            yield return AccessTools.Method(typeof(CookingStation), "OnDestroyed");
            yield return AccessTools.Method(typeof(Fermenter), "OnDestroyed");
        }
        private static bool Prefix(Component __instance)
        {
            var zdo = ValheimAccess.Zdo(__instance);
            return !InventoryOperationNetwork.Ready || zdo == null ||
                zdo.GetString(AuthorityStationLedger.Marker, "").Length == 0 && !zdo.GetBool(AuthorityStationLedger.EnrollingKey);
        }
    }
}
