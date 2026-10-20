using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class NativeStationWorld
    {
        internal static bool Enabled => true;
        internal static GameObject Object(ZDO zdo) => ZNetScene.instance.FindInstance(zdo.m_uid) ?? ZNetScene.instance.GetPrefab(zdo.GetPrefab());
        internal static void Register() => LiveChestWorld.RegisterNative("station:", AuthorityStationLedger.Endpoint,
            zdo => AuthorityStationState.Capture(zdo).Encode(), Apply, Resolve, bytes => AuthorityStationState.Decode(bytes).Retired);
        internal static byte[] Read(ZDO zdo) => LiveChestWorld.ReadNative(zdo, AuthorityStationLedger.Endpoint(zdo));
        internal static void UpgradeBuffer(AuthorityStationState state)
        {
            if (state.Booleans.TryGetValue(AuthorityStationState.NativeUpgradeKey, out bool complete) && complete) return;
            var saved = SharedOperationHost.ReadStored("station:" + state.Token);
            if (saved != null)
            {
                var old = AuthorityStationState.Decode(FileOperationEndpoints.Inventory(saved));
                if (!old.Retired)
                    foreach (var item in old.Output.GetAllItems())
                        if (!NativeInventoryCodec.AddExact(state.Output, item)) throw new OperationDeferredException("legacy-production-output-needs-space");
            }
            // Migrate only undelivered Runic output, never old chest contents, station
            // timers, queue or fuel. The native flag commits with that output's transfer.
            state.Booleans[AuthorityStationState.NativeUpgradeKey] = true;
        }
        private static ZDO Resolve(byte[] bytes)
        {
            var body = AuthorityStationState.Decode(bytes);
            return NetworkInventoryAccess.Nearby(body.Position, 2).FirstOrDefault(z => z.GetPrefab() == body.Prefab &&
                z.GetString(ProductionEndpointIdentity.TokenStorageKey, "") == body.Token);
        }
        private static string Apply(ZDO zdo, byte[] beforeBytes, byte[] afterBytes)
        {
            var before = AuthorityStationState.Decode(beforeBytes); var after = AuthorityStationState.Decode(afterBytes);
            var current = AuthorityStationState.Capture(zdo);
            var root = ZNetScene.instance.FindInstance(zdo.m_uid);
            if (root == null || before.Token != current.Token || after.Token != current.Token) return "native-station-unavailable";
            var smelter = root.GetComponent<Smelter>();
            var cooking = root.GetComponent<CookingStation>(); var fermenter = root.GetComponent<Fermenter>();
            int addedOre = smelter == null ? 0 : after.Integers[ZDOVars.s_queued] - before.Integers[ZDOVars.s_queued];
            if (addedOre < 0) return "native-station-queue-changed"; // Native extraction/processing owns removals.
            if (smelter != null && ValheimAccess.QueueSize(smelter) + addedOre > smelter.m_maxOre) return "native-station-full";
            var queueKeys = smelter == null ? Array.Empty<int>() : Enumerable.Range(0, Math.Max(smelter.m_maxOre,
                Math.Max(before.Integers[ZDOVars.s_queued], after.Integers[ZDOVars.s_queued]))).Select(n => ("item" + n).GetStableHashCode()).ToArray();
            foreach (var pair in after.Strings.Where(p => before.Strings[p.Key] != p.Value && !queueKeys.Contains(p.Key)))
                if (zdo.GetString(pair.Key, "") != before.Strings[pair.Key]) return "native-station-slot-changed";
            foreach (var pair in after.Integers.Where(p => before.Integers[p.Key] != p.Value && p.Key != ZDOVars.s_queued && p.Key != ZDOVars.s_level))
                if (zdo.GetInt(pair.Key) != before.Integers[pair.Key]) return "native-station-state-changed";
            var output = NativeInventoryCodec.Clone(current.Output);
            string refused = LiveChestWorld.ApplyDelta(output, before.Output, after.Output);
            if (refused != null) return refused;

            var nativeKeys = new HashSet<int>();
            if (cooking != null)
                for (int n = 0; n < cooking.m_slots.Length; n++)
                {
                    int slot = ("slot" + n).GetStableHashCode();
                    if (before.Strings[slot].Length != 0 || after.Strings[slot].Length == 0) continue;
                    string input = after.Strings[slot];
                    int count = current.Strings.Count(p => p.Value == input);
                    AccessTools.Method(typeof(CookingStation), "RPC_AddItem").Invoke(cooking,
                        new object[] { ZDOMan.GetSessionID(), input, after.Booleans[ZDOVars.s_cheatedQueued + n] });
                    if (AuthorityStationState.Capture(zdo).Strings.Count(p => p.Value == input) <= count) return "native-station-input-refused";
                    for (int index = 0; index < cooking.m_slots.Length; index++)
                    { nativeKeys.Add(("slot" + index).GetStableHashCode()); nativeKeys.Add(("slotstatus" + index).GetStableHashCode()); nativeKeys.Add(ZDOVars.s_cheatedQueued + index); }
                    break;
                }
            if (fermenter != null && before.Integers[ZDOVars.s_content] == 0 && after.Integers[ZDOVars.s_content] != 0)
            {
                AccessTools.Method(typeof(Fermenter), "RPC_AddItem").Invoke(fermenter,
                    new object[] { ZDOMan.GetSessionID(), after.Integers[ZDOVars.s_content], after.Booleans[ZDOVars.s_cheatedQueued] });
                if (zdo.GetInt(ZDOVars.s_content) == 0) return "native-station-input-refused";
                nativeKeys.Add(ZDOVars.s_content); nativeKeys.Add(ZDOVars.s_startTime); nativeKeys.Add(ZDOVars.s_cheatedQueued);
            }

            // Queue insertion uses the native station method and its installed patches. It
            // appends to the current queue, even if a native tick consumed an earlier entry.
            if (smelter != null)
                for (int n = 0; n < addedOre; n++)
                    ValheimAccess.QueueOre(smelter, after.Strings[("item" + (before.Integers[ZDOVars.s_queued] + n)).GetStableHashCode()],
                        after.Booleans[ZDOVars.s_cheatedQueued]);
            foreach (var pair in after.Floats.Where(p => before.Floats[p.Key] != p.Value && !nativeKeys.Contains(p.Key)))
            {
                float value = zdo.GetFloat(pair.Key) + pair.Value - before.Floats[pair.Key];
                if (pair.Key == ZDOVars.s_fuel && smelter != null) ValheimAccess.SetFuel(smelter, value);
                else if (pair.Key == ZDOVars.s_fuel && root.TryGetComponent<Fireplace>(out var fire)) ValheimAccess.SetFireplaceFuel(fire, value);
                else zdo.Set(pair.Key, value);
            }
            foreach (var pair in after.Integers.Where(p => before.Integers[p.Key] != p.Value && p.Key != ZDOVars.s_queued && !nativeKeys.Contains(p.Key)))
                zdo.Set(pair.Key, pair.Key == ZDOVars.s_level ? zdo.GetInt(pair.Key) + pair.Value - before.Integers[pair.Key] : pair.Value);
            foreach (var pair in after.Strings.Where(p => before.Strings[p.Key] != p.Value && !queueKeys.Contains(p.Key) && !nativeKeys.Contains(p.Key))) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in after.Longs.Where(p => before.Longs[p.Key] != p.Value && !nativeKeys.Contains(p.Key))) zdo.Set(pair.Key, pair.Value);
            foreach (var pair in after.Booleans.Where(p => before.Booleans[p.Key] != p.Value && !nativeKeys.Contains(p.Key))) zdo.Set(pair.Key, pair.Value);
            zdo.Set(AuthorityStationState.NativeOutputKey, NativeInventoryCodec.Save(output));
            if (after.LastLink != before.LastLink) zdo.Set(AuthorityStationState.NativeLastLinkKey, after.LastLink);
            if (after.LastTarget != before.LastTarget) zdo.Set(AuthorityStationState.NativeLastTargetKey, after.LastTarget);
            return null;
        }
    }
}
