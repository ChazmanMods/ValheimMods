using System;
using System.Linq;
using System.Collections.Generic;
using RunicAutomation.Operations;

namespace RunicProduction.Integration
{
    internal enum AuthorityStationAction { Fuel, Input, Collect, Toggle }
    internal sealed class AuthorityStationInteractionRequest
    {
        internal ZDOID Station; internal AuthorityStationAction Action; internal byte[] Escrow; internal bool Nearby;
        internal byte[] Encode()
        { var p = new ZPackage(); p.Write(2); p.Write(Station); p.Write((int)Action); p.Write(Escrow); p.Write(Nearby); return p.GetArray(); }
        internal static AuthorityStationInteractionRequest Decode(byte[] bytes)
        {
            var p = new ZPackage(bytes); int version = p.ReadInt(); if (version != 1 && version != 2) throw new OperationRejectedException("invalid-station-interaction");
            var r = new AuthorityStationInteractionRequest { Station = p.ReadZDOID(), Action = (AuthorityStationAction)p.ReadInt(), Escrow = OperationPacket.Bytes(p) };
            r.Nearby = version >= 2 && p.ReadBool();
            OperationPacket.End(p); if (!Enum.IsDefined(typeof(AuthorityStationAction), r.Action)) throw new OperationRejectedException("invalid-station-action"); return r;
        }
    }
    internal static class AuthorityStationInteraction
    {
        internal const string Kind = "production.interact.v2";
        internal static void Register()
        {
            SharedOperationHost.Register(Kind, Authorize, Plan);
            CharacterOperationEscrow.RegisterDeliveryProcessor(Kind, AuthorityStationInteractionClient.ProcessDelivery);
        }
        private static string Authorize(string actor, byte[] bytes)
        {
            try
            {
                var request = AuthorityStationInteractionRequest.Decode(bytes); var station = ZDOMan.instance.GetZDO(request.Station);
                if (station == null || !NetworkInventoryAccess.TryPosition(actor, out long principal, out var position)) return "station-unavailable";
                if ((station.GetPosition() - position).sqrMagnitude > 25 || !NetworkInventoryAccess.WardAllows(station.GetPosition(), principal)) return "station-access-denied";
                return station.GetString(AuthorityStationLedger.Marker, "").Length == 0 ? "station-enrollment-pending" : null;
            }
            catch { return "invalid-station-interaction"; }
        }
        private static Tuple<Tuple<string, byte[], byte[]>[], byte[]> Plan(string actor, byte[] bytes, Func<string, byte[]> read)
        {
            string denied = Authorize(actor, bytes); if (denied != null) throw new OperationRejectedException(denied);
            var request = AuthorityStationInteractionRequest.Decode(bytes); var station = ZDOMan.instance.GetZDO(request.Station);
            string endpoint = AuthorityStationLedger.Endpoint(station); byte[] before = read(endpoint);
            if (before == null) throw new OperationRejectedException("station-ledger-missing");
            var state = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(before)); state.ValidateFor(station);
            if (state.Retired) throw new OperationRejectedException("station-was-destroyed");
            var offered = NativeInventoryCodec.Load(request.Escrow, 32, 32);
            var delivery = new Inventory("Station delivery", null, 32, 32);
            var prefab = ZNetScene.instance.GetPrefab(state.Prefab);
            var smelter = prefab.GetComponent<Smelter>(); var cooking = prefab.GetComponent<CookingStation>();
            var fermenter = prefab.GetComponent<Fermenter>(); var fire = prefab.GetComponent<Fireplace>(); var hive = prefab.GetComponent<Beehive>();
            var changes = new List<Tuple<string, byte[], byte[]>>();
            if (request.Nearby)
            {
                if (offered.NrOfItems() != 0 || (request.Action != AuthorityStationAction.Fuel && request.Action != AuthorityStationAction.Input))
                    throw new OperationRejectedException("invalid-nearby-station-input");
                bool fuelAction = request.Action == AuthorityStationAction.Fuel;
                ItemDrop fuelItem = smelter != null ? smelter.m_fuelItem : cooking != null ? cooking.m_fuelItem : fire != null ? fire.m_fuelItem : null;
                // Preserve Crafting's original scope: cooking inputs and station fuel.
                string[] candidates = fuelAction ? (fuelItem == null ? Array.Empty<string>() : new[] { fuelItem.name }) :
                    cooking != null ? cooking.m_conversion.Where(c => c.m_from != null && c.m_to != null).Select(c => c.m_from.name).ToArray() : Array.Empty<string>();
                var nearby = NearbyInteractionMaterials.Plan(actor, request.Station, fuelAction, candidates, read);
                changes.AddRange(nearby.Item1);
                offered = NativeInventoryCodec.Load(nearby.Item2, 32, 32);
            }
            if (request.Action == AuthorityStationAction.Fuel || request.Action == AuthorityStationAction.Input)
            {
                if (offered.NrOfItems() != 1 || offered.GetAllItems()[0].m_stack != 1) throw new OperationRejectedException("station-requires-one-item");
                var item = offered.GetAllItems()[0]; string name = item.m_dropPrefab.name;
                AuthorityStationClock.RebaseMaterialIdle(state, ZNet.instance.GetTime().Ticks);
                if (request.Action == AuthorityStationAction.Fuel)
                {
                    ItemDrop fuel = smelter != null ? smelter.m_fuelItem : cooking != null && cooking.m_useFuel ? cooking.m_fuelItem : fire != null && fire.m_canRefill && !fire.m_infiniteFuel ? fire.m_fuelItem : null;
                    float max = smelter != null ? smelter.m_maxFuel : cooking != null ? cooking.m_maxFuel : fire != null ? fire.m_maxFuel : 0;
                    if (fuel == null || fuel.name != name || Math.Ceiling(state.Floats[ZDOVars.s_fuel]) >= max) throw new OperationRejectedException("station-fuel-full-or-incompatible");
                    state.Floats[ZDOVars.s_fuel] = Math.Min(max, state.Floats[ZDOVars.s_fuel] + 1);
                }
                else if (smelter != null)
                {
                    int count = state.Integers[ZDOVars.s_queued];
                    if (count >= smelter.m_maxOre || !smelter.m_conversion.Any(c => c.m_from != null && c.m_from.name == name && c.m_to != null)) throw new OperationRejectedException("station-input-full-or-incompatible");
                    state.Strings[("item" + count).GetStableHashCode()] = name; state.Integers[ZDOVars.s_queued]++;
                    state.Booleans[ZDOVars.s_cheatedQueued] |= item.m_cheated;
                }
                else if (cooking != null)
                {
                    int slot = Enumerable.Range(0, cooking.m_slots.Length).Where(n => state.Strings[("slot" + n).GetStableHashCode()].Length == 0).DefaultIfEmpty(-1).First();
                    if (slot < 0 || !cooking.m_conversion.Any(c => c.m_from != null && c.m_to != null && c.m_from.name == name)) throw new OperationRejectedException("station-input-full-or-incompatible");
                    int key = ("slot" + slot).GetStableHashCode(); state.Strings[key] = name; state.Floats[key] = 0;
                    state.Integers[("slotstatus" + slot).GetStableHashCode()] = 0; state.Booleans[ZDOVars.s_cheatedQueued + slot] = item.m_cheated;
                }
                else if (fermenter != null)
                {
                    if (state.Integers[ZDOVars.s_content] != 0 || state.Strings[ZDOVars.s_content].Length != 0 ||
                        !fermenter.m_conversion.Any(c => c.m_from != null && c.m_to != null && c.m_from.name == name)) throw new OperationRejectedException("station-input-full-or-incompatible");
                    state.Integers[ZDOVars.s_content] = name.GetStableHashCode(); state.Strings[ZDOVars.s_content] = "";
                    state.Longs[ZDOVars.s_startTime] = ZNet.instance.GetTime().Ticks; state.Booleans[ZDOVars.s_cheatedQueued] = item.m_cheated;
                }
                else throw new OperationRejectedException("station-does-not-accept-input");
            }
            else
            {
                if (offered.NrOfItems() != 0) throw new OperationRejectedException("unexpected-carried-items");
                void Deliver(ItemDrop product, int amount, bool cheated)
                {
                    if (product == null || amount < 1) throw new OperationRejectedException("station-output-unavailable");
                    var item = product.m_itemData.Clone(); item.m_dropPrefab = product.gameObject; item.m_stack = amount;
                    item.m_worldLevel = (byte)Game.m_worldLevel; item.m_cheated = (cheated || station.GetBool(ZDOVars.s_cheated)) && !PlayerProfile.s_bypassCheatChecks;
                    item.m_equipped = false; item.m_pickedUp = false; item.m_durability = item.GetMaxDurability();
                    if (!NativeInventoryCodec.AddExact(delivery, item)) throw new OperationRejectedException("station-delivery-too-large");
                }
                if (request.Action == AuthorityStationAction.Toggle)
                {
                    if (fire == null || !fire.m_canTurnOff) throw new OperationRejectedException("station-cannot-toggle");
                    state.Integers[ZDOVars.s_state] = state.Integers[ZDOVars.s_state] == 1 ? 2 : 1;
                }
                else if (smelter != null)
                {
                    int amount = state.Integers[ZDOVars.s_spawnAmount];
                    if (amount > 0)
                    {
                        string input = state.Strings[ZDOVars.s_spawnOre];
                        Deliver(smelter.m_conversion.FirstOrDefault(c => c.m_from == null || c.m_from.name == input)?.m_to, amount, state.Booleans[ZDOVars.s_cheatedQueued]);
                        state.Integers[ZDOVars.s_spawnAmount] = 0; state.Strings[ZDOVars.s_spawnOre] = "";
                    }
                    if (!NativeInventoryCodec.AddAll(delivery, state.Output)) throw new OperationRejectedException("station-delivery-too-large");
                    state.Output.GetAllItems().Clear();
                }
                else if (cooking != null)
                {
                    int slot = Enumerable.Range(0, cooking.m_slots.Length).Where(n => state.Strings[("slot" + n).GetStableHashCode()].Length > 0 && state.Integers[("slotstatus" + n).GetStableHashCode()] > 0).DefaultIfEmpty(-1).First();
                    if (slot < 0) throw new OperationRejectedException("station-output-not-ready");
                    int key = ("slot" + slot).GetStableHashCode(); var product = ObjectDB.instance.GetItemPrefab(state.Strings[key])?.GetComponent<ItemDrop>();
                    Deliver(product, 1, state.Booleans[ZDOVars.s_cheatedQueued + slot]);
                    state.Strings[key] = ""; state.Floats[key] = 0; state.Integers[("slotstatus" + slot).GetStableHashCode()] = 0; state.Booleans[ZDOVars.s_cheatedQueued + slot] = false;
                }
                else if (fermenter != null)
                {
                    long started = state.Longs[ZDOVars.s_startTime];
                    if (started == 0 || (ZNet.instance.GetTime().Ticks - started) / (double)TimeSpan.TicksPerSecond <= fermenter.m_fermentationDuration) throw new OperationRejectedException("station-output-not-ready");
                    var conversion = fermenter.m_conversion.FirstOrDefault(c => c.m_from != null && (state.Integers[ZDOVars.s_content] != 0 ? c.m_from.name.GetStableHashCode() == state.Integers[ZDOVars.s_content] : c.m_from.name == state.Strings[ZDOVars.s_content]));
                    Deliver(conversion?.m_to, conversion?.m_producedItems ?? 0, state.Booleans[ZDOVars.s_cheatedQueued]);
                    state.Integers[ZDOVars.s_content] = 0; state.Strings[ZDOVars.s_content] = ""; state.Longs[ZDOVars.s_startTime] = 0; state.Booleans[ZDOVars.s_cheatedQueued] = false;
                }
                else if (hive != null)
                {
                    int level = state.Integers[ZDOVars.s_level];
                    if (level < 1 || level > hive.m_maxHoney) throw new OperationRejectedException("station-output-not-ready");
                    Deliver(hive.m_honeyItem, checked(level * Game.instance.ScaleDrops(hive.m_honeyItem.m_itemData, 1)), false); state.Integers[ZDOVars.s_level] = 0;
                }
                else throw new OperationRejectedException("station-has-no-collectable-output");
            }
            changes.Add(Tuple.Create(endpoint, before, FileOperationEndpoints.Next(before, state.Encode())));
            return Tuple.Create(changes.ToArray(), NativeInventoryCodec.Save(delivery));
        }
    }
}
