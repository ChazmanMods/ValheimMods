using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    // Only production fields belong to this snapshot. Object ownership, health, transforms and
    // access metadata remain native world state and must never be replaced by an inventory reply.
    internal sealed class AuthorityStationState
    {
        internal int Prefab;
        internal string Token, SourceOrigin = "";
        internal Vector3 Position;
        internal string LastLink = "";
        internal int LastTarget = -1;
        internal bool Retired;
        internal readonly Dictionary<int, float> Floats = new();
        internal readonly Dictionary<int, int> Integers = new();
        internal readonly Dictionary<int, long> Longs = new();
        internal readonly Dictionary<int, string> Strings = new();
        internal readonly Dictionary<int, bool> Booleans = new();
        internal Inventory Output = new Inventory("Pending production output", null, 32, 32);
        internal const string NativeOutputKey = "runic.production.native-output.v1";
        internal const string NativeLastLinkKey = "runic.production.last-link.v1";
        internal const string NativeLastTargetKey = "runic.production.last-target.v1";
        internal static readonly int NativeUpgradeKey = "runic.production.live-upgrade.v1".GetStableHashCode();
        internal static readonly int MaximumFuel = "runic.snapshot.maximum-fuel".GetStableHashCode();
        internal static readonly int MaximumOre = "runic.snapshot.maximum-ore".GetStableHashCode();
        internal static readonly int SlotCount = "runic.snapshot.slots".GetStableHashCode();
        internal static readonly int FuelItem = "runic.snapshot.fuel-item".GetStableHashCode();
        internal static readonly int AllowedInputs = "runic.snapshot.inputs".GetStableHashCode();

        internal static AuthorityStationState Capture(ZDO zdo, bool validate = true)
        {
            if (zdo == null || !ProductionEndpointIdentity.IsCanonicalToken(zdo.GetString(ProductionEndpointIdentity.TokenStorageKey, "")))
                throw new OperationRejectedException("station-identity-unavailable");
            var state = new AuthorityStationState { SourceOrigin = zdo.GetString(WorldInventoryOrigin.Key, ""), Prefab = zdo.GetPrefab(), Token = zdo.GetString(ProductionEndpointIdentity.TokenStorageKey, ""), Position = zdo.GetPosition() };
            var output = zdo.GetByteArray(NativeOutputKey);
            if (output != null) state.Output = NativeInventoryCodec.Load(output, 32, 32);
            state.LastLink = zdo.GetString(NativeLastLinkKey, ""); state.LastTarget = zdo.GetInt(NativeLastTargetKey, -1);
            var prefab = NativeStationWorld.Object(zdo);
            if (prefab == null) throw new OperationRejectedException("station-prefab-unavailable");
            void F(int key) => state.Floats.Add(key, zdo.GetFloat(key));
            void I(int key) => state.Integers.Add(key, zdo.GetInt(key));
            void L(int key) => state.Longs.Add(key, zdo.GetLong(key));
            void S(int key) => state.Strings.Add(key, zdo.GetString(key, ""));
            void B(int key) => state.Booleans.Add(key, zdo.GetBool(key));
            B(NativeUpgradeKey);
            if (prefab.TryGetComponent<Smelter>(out var smelter))
            {
                if (smelter.m_maxOre < 0) throw new OperationRejectedException("invalid-smelter-queue-size");
                F(ZDOVars.s_fuel); F(ZDOVars.s_bakeTimer); F(ZDOVars.s_accTime); L(ZDOVars.s_startTime);
                I(ZDOVars.s_queued); S(ZDOVars.s_spawnOre); I(ZDOVars.s_spawnAmount); B(ZDOVars.s_cheatedQueued);
                for (int n = 0; n < Math.Max(smelter.m_maxOre, zdo.GetInt(ZDOVars.s_queued)); n++) S(("item" + n).GetStableHashCode());
            }
            else if (prefab.TryGetComponent<CookingStation>(out var cooking))
            {
                if (cooking.m_slots == null) throw new OperationRejectedException("invalid-cooking-slot-count");
                F(ZDOVars.s_fuel); L(ZDOVars.s_startTime);
                for (int n = 0; n < cooking.m_slots.Length; n++)
                {
                    int slot = ("slot" + n).GetStableHashCode(); S(slot); F(slot);
                    I(("slotstatus" + n).GetStableHashCode()); B(ZDOVars.s_cheatedQueued + n);
                }
            }
            else if (prefab.GetComponent<Fermenter>() != null)
            {
                I(ZDOVars.s_content); S(ZDOVars.s_content); L(ZDOVars.s_startTime); B(ZDOVars.s_cheatedQueued);
            }
            else if (prefab.GetComponent<Fireplace>() != null)
            { F(ZDOVars.s_fuel); L(ZDOVars.s_lastTime); state.Integers.Add(ZDOVars.s_state, zdo.GetInt(ZDOVars.s_state, 1)); }
            else if (prefab.GetComponent<Beehive>() != null)
            { I(ZDOVars.s_level); F(ZDOVars.s_product); L(ZDOVars.s_lastTime); }
            // These collectors own only the existing native output buffer here.
            // Sap/root progress and fountain spawn clocks remain entirely native.
            else if (prefab.GetComponent<SapCollector>() != null || prefab.GetComponent<WispSpawner>() != null) { }
            else throw new OperationRejectedException("unsupported-timed-production-station");
            // Effective instance settings travel with the native owner's snapshot. A
            // dedicated server's prefab must not silently substitute different limits.
            state.Floats[MaximumFuel] = 0; state.Integers[MaximumOre] = 0; state.Integers[SlotCount] = 0;
            state.Strings[FuelItem] = ""; state.Strings[AllowedInputs] = "";
            if (smelter != null)
            {
                state.Floats[MaximumFuel] = smelter.m_maxFuel; state.Integers[MaximumOre] = smelter.m_maxOre;
                state.Strings[FuelItem] = smelter.m_fuelItem?.name ?? "";
                state.Strings[AllowedInputs] = string.Join("\n", smelter.m_conversion.Where(c => c.m_from != null && c.m_to != null).Select(c => c.m_from.name).Distinct());
            }
            else if (prefab.TryGetComponent<CookingStation>(out var liveCooking))
            {
                state.Floats[MaximumFuel] = liveCooking.m_useFuel ? liveCooking.m_maxFuel : 0; state.Integers[SlotCount] = liveCooking.m_slots.Length;
                state.Strings[FuelItem] = liveCooking.m_fuelItem?.name ?? "";
                state.Strings[AllowedInputs] = string.Join("\n", liveCooking.m_conversion.Where(c => c.m_from != null && c.m_to != null).Select(c => c.m_from.name).Distinct());
            }
            else if (prefab.TryGetComponent<Fireplace>(out var liveFire))
            {
                state.Floats[MaximumFuel] = liveFire.m_canRefill && !liveFire.m_infiniteFuel ? liveFire.m_maxFuel : 0;
                state.Strings[FuelItem] = liveFire.m_fuelItem?.name ?? "";
            }
            else if (prefab.TryGetComponent<Fermenter>(out var liveFermenter))
                state.Strings[AllowedInputs] = string.Join("\n", liveFermenter.m_conversion.Where(c => c.m_from != null && c.m_to != null).Select(c => c.m_from.name).Distinct());
            if (validate) state.ValidateValues(); return state;
        }
        internal byte[] Encode()
        {
            ValidateValues(); var p = new ZPackage(); p.Write(4); p.Write(Prefab); p.Write(Token); p.Write(Position);
            p.Write(LastLink); p.Write(LastTarget); p.Write(Retired); p.Write(SourceOrigin);
            p.Write(Floats.Count); foreach (var pair in Floats.OrderBy(x => x.Key)) { p.Write(pair.Key); p.Write(pair.Value); }
            p.Write(Integers.Count); foreach (var pair in Integers.OrderBy(x => x.Key)) { p.Write(pair.Key); p.Write(pair.Value); }
            p.Write(Longs.Count); foreach (var pair in Longs.OrderBy(x => x.Key)) { p.Write(pair.Key); p.Write(pair.Value); }
            p.Write(Strings.Count); foreach (var pair in Strings.OrderBy(x => x.Key)) { p.Write(pair.Key); p.Write(pair.Value); }
            p.Write(Booleans.Count); foreach (var pair in Booleans.OrderBy(x => x.Key)) { p.Write(pair.Key); p.Write(pair.Value); }
            p.Write(NativeInventoryCodec.Save(Output)); return p.GetArray();
        }
        internal static AuthorityStationState Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 1024 * 1024) throw new InvalidDataException("Invalid station image length");
            var p = new ZPackage(bytes); int schema = p.ReadInt(); if (schema < 1 || schema > 4) throw new InvalidDataException("Unsupported station image");
            var state = new AuthorityStationState { Prefab = p.ReadInt(), Token = p.ReadString(), Position = p.ReadVector3() };
            if (schema >= 2) { state.LastLink = p.ReadString(); state.LastTarget = p.ReadInt(); }
            if (schema >= 3) state.Retired = p.ReadBool();
            if (schema >= 4) state.SourceOrigin = p.ReadString();
            int Count() { int count = p.ReadInt(); if (count < 0 || count > 65535) throw new InvalidDataException("Too many station fields"); return count; }
            for (int n = Count(); n > 0; n--) state.Floats.Add(p.ReadInt(), p.ReadSingle());
            for (int n = Count(); n > 0; n--) state.Integers.Add(p.ReadInt(), p.ReadInt());
            for (int n = Count(); n > 0; n--) state.Longs.Add(p.ReadInt(), p.ReadLong());
            for (int n = Count(); n > 0; n--) state.Strings.Add(p.ReadInt(), p.ReadString());
            for (int n = Count(); n > 0; n--) state.Booleans.Add(p.ReadInt(), p.ReadBool());
            state.Output = NativeInventoryCodec.Load(OperationPacket.Bytes(p, 1024 * 1024), 32, 32);
            OperationPacket.End(p); state.ValidateValues(); return state;
        }
        internal void Project(ZDO zdo)
        {
            ValidateFor(zdo);
            if (SourceOrigin.Length != 0) zdo.Set(WorldInventoryOrigin.Key, SourceOrigin);
            foreach (var pair in Floats) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in Integers) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in Longs) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in Strings) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in Booleans) zdo.Set(pair.Key, pair.Value);
        }
        internal void ValidateFor(ZDO zdo)
        {
            var layout = Capture(zdo, validate: false);
            if (NativeStationWorld.Enabled)
            {
                if (Prefab != layout.Prefab || Token != layout.Token) throw new InvalidDataException("Station identity changed");
                ValidateValues(); return;
            }
            if (Prefab != layout.Prefab || Token != layout.Token ||
                !SameKeys(Floats, layout.Floats) || !SameKeys(Integers, layout.Integers) || !SameKeys(Longs, layout.Longs) ||
                !SameKeys(Strings, layout.Strings) || !SameKeys(Booleans, layout.Booleans))
                throw new InvalidDataException("Station image identity or field layout changed");
            ValidateValues();
        }
        private static bool SameKeys<T>(Dictionary<int, T> a, Dictionary<int, T> b) => a.Count == b.Count && a.Keys.All(b.ContainsKey);
        private void ValidateValues()
        {
            if (Prefab == 0 || !ProductionEndpointIdentity.IsCanonicalToken(Token) || !NetworkInventoryAccess.Finite(Position) ||
                LastLink == null || LastLink.Length > 128 || LastTarget < -1 ||
                Floats.Values.Any(v => float.IsNaN(v) || float.IsInfinity(v)) ||
                Integers.Any(v => v.Key != ZDOVars.s_content && v.Value < 0) || Longs.Values.Any(v => v < 0 || v > DateTime.MaxValue.Ticks) ||
                Strings.Values.Any(v => v == null || v.Length > 65535)) throw new InvalidDataException("Invalid production state");
            NativeInventoryCodec.Validate(Output);
        }
    }
}
