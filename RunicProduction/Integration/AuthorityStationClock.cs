using System;
using System.Linq;
using RunicAutomation.Operations;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal readonly struct AuthorityStationEnvironment
    {
        internal readonly bool Sheltered, FireLit, SmokeBlocked, BeesHappy, Burning;
        internal readonly float WindPower;
        internal AuthorityStationEnvironment(bool sheltered, bool fireLit, bool smokeBlocked, bool beesHappy, bool burning, float windPower)
        {
            if (float.IsNaN(windPower) || float.IsInfinity(windPower) || windPower < 0 || windPower > 1)
                throw new ArgumentOutOfRangeException(nameof(windPower));
            Sheltered = sheltered; FireLit = fireLit; SmokeBlocked = smokeBlocked; BeesHappy = beesHappy; Burning = burning; WindPower = windPower;
        }
    }
    // Planning uses a detached state. The calling authority transaction persists this state and
    // any chest changes together. Native outputs remain serialized items until routed or collected.
    internal static class AuthorityStationClock
    {
        // Only material starvation is stable until another authority transaction changes it.
        // Wind, roof and smoke can change without a transaction, so they must keep their clocks.
        internal static bool IsMaterialIdle(AuthorityStationState state)
        {
            var smelter = ZNetScene.instance.GetPrefab(state.Prefab)?.GetComponent<Smelter>();
            return smelter != null && IsMaterialIdle(state, smelter);
        }
        private static bool IsMaterialIdle(AuthorityStationState state, Smelter smelter) =>
            smelter.m_maxOre > 0 && state.Integers[ZDOVars.s_queued] == 0 ||
            smelter.m_maxFuel > 0 && state.Floats[ZDOVars.s_fuel] <= 0;

        internal static void RebaseMaterialIdle(AuthorityStationState state, long now)
        {
            if (!IsMaterialIdle(state)) return;
            if (now < 1 || now > DateTime.MaxValue.Ticks) throw new ArgumentOutOfRangeException(nameof(now));
            // Keep the same fractional second as an ordinary idle tick. Never charge the
            // persisted idle interval to fuel or input added by the current interaction.
            state.Floats[ZDOVars.s_accTime] = Math.Min(3600,
                state.Floats[ZDOVars.s_accTime] + (float)Elapsed(state, ZDOVars.s_startTime, now)) % 1;
        }
        internal static void Advance(AuthorityStationState state, long now, AuthorityStationEnvironment environment, bool stationCheated)
        {
            if (now < 1 || now > DateTime.MaxValue.Ticks) throw new ArgumentOutOfRangeException(nameof(now));
            var prefab = ZNetScene.instance.GetPrefab(state.Prefab);
            if (prefab == null) throw new OperationRejectedException("station-prefab-unavailable");
            if (prefab.TryGetComponent<Smelter>(out var smelter)) AdvanceSmelter(state, smelter, now, environment, stationCheated);
            else if (prefab.TryGetComponent<CookingStation>(out var cooking)) AdvanceCooking(state, cooking, now, environment);
            else if (prefab.TryGetComponent<Fermenter>(out var fermenter))
            {
                long started = state.Longs[ZDOVars.s_startTime];
                bool occupied = state.Integers[ZDOVars.s_content] != 0 || state.Strings[ZDOVars.s_content].Length != 0;
                if (occupied && started > 0 && (now - started) / (double)TimeSpan.TicksPerSecond <= fermenter.m_fermentationDuration && !environment.Sheltered)
                    state.Longs[ZDOVars.s_startTime] = now;
            }
            else if (prefab.TryGetComponent<Fireplace>(out var fireplace))
            {
                if (fireplace.m_secPerFuel > 0)
                {
                    double elapsed = Elapsed(state, ZDOVars.s_lastTime, now);
                    if (environment.Burning && !fireplace.m_infiniteFuel && state.Integers[ZDOVars.s_state] == 1)
                        state.Floats[ZDOVars.s_fuel] = Math.Max(0, state.Floats[ZDOVars.s_fuel] - (float)(elapsed / fireplace.m_secPerFuel));
                }
            }
            else if (prefab.TryGetComponent<Beehive>(out var hive) && environment.BeesHappy)
            {
                if (hive.m_secPerUnit <= 0) throw new OperationRejectedException("invalid-beehive-duration");
                float progress = state.Floats[ZDOVars.s_product] + (float)Elapsed(state, ZDOVars.s_lastTime, now);
                if (progress > hive.m_secPerUnit)
                {
                    int added = (int)Math.Min(int.MaxValue - (double)hive.m_maxHoney, progress / hive.m_secPerUnit);
                    state.Integers[ZDOVars.s_level] = Math.Min(hive.m_maxHoney, state.Integers[ZDOVars.s_level] + added); progress = 0;
                }
                state.Floats[ZDOVars.s_product] = progress;
            }
        }
        private static double Elapsed(AuthorityStationState state, int key, long now)
        {
            long before = state.Longs[key]; state.Longs[key] = now;
            return before == 0 ? 0 : Math.Max(0, (now - before) / (double)TimeSpan.TicksPerSecond);
        }
        private static void AdvanceCooking(AuthorityStationState state, CookingStation station, long now, AuthorityStationEnvironment environment)
        {
            bool uncooked = Enumerable.Range(0, station.m_slots.Length).Select(i => state.Strings[("slot" + i).GetStableHashCode()])
                .Any(item => item.Length > 0 && item != station.m_overCookedItem?.name && !station.m_conversion.Any(c => c.m_to?.name == item));
            bool active = station.m_requireFire && environment.FireLit || station.m_useFuel && state.Floats[ZDOVars.s_fuel] > 0 && (station.m_useFueldWhileEmpty || uncooked);
            float elapsed = (float)Elapsed(state, ZDOVars.s_startTime, now);
            if (!active) return;
            if (station.m_useFuel)
            {
                if (station.m_secPerFuel <= 0) throw new OperationRejectedException("invalid-cooking-fuel-duration");
                state.Floats[ZDOVars.s_fuel] = Math.Max(0, state.Floats[ZDOVars.s_fuel] - elapsed / station.m_secPerFuel);
            }
            for (int n = 0; n < station.m_slots.Length; n++)
            {
                int slot = ("slot" + n).GetStableHashCode(), statusKey = ("slotstatus" + n).GetStableHashCode();
                string item = state.Strings[slot]; int status = state.Integers[statusKey];
                if (item.Length == 0 || status == 2) continue;
                var conversion = station.m_conversion.FirstOrDefault(c => c.m_from?.name == item || c.m_to?.name == item);
                if (conversion == null) throw new OperationRejectedException("cooking-conversion-unavailable:" + item);
                float elapsedCooking = state.Floats[slot] + elapsed;
                if (station.m_canOvercookItems && elapsedCooking > conversion.m_cookTime * 2)
                {
                    if (station.m_overCookedItem == null) throw new OperationRejectedException("overcooked-prefab-unavailable");
                    state.Strings[slot] = station.m_overCookedItem.name; state.Integers[statusKey] = 2;
                }
                else if (elapsedCooking > conversion.m_cookTime && item == conversion.m_from.name)
                { state.Strings[slot] = conversion.m_to.name; state.Integers[statusKey] = 1; }
                state.Floats[slot] = elapsedCooking;
            }
        }
        private static void AdvanceSmelter(AuthorityStationState state, Smelter station, long now, AuthorityStationEnvironment environment, bool stationCheated)
        {
            if (state.Integers[ZDOVars.s_queued] < 0 || state.Integers[ZDOVars.s_queued] > station.m_maxOre)
                throw new OperationRejectedException("invalid-smelter-queue-count");
            float accumulator = Math.Min(3600, state.Floats[ZDOVars.s_accTime] + (float)Elapsed(state, ZDOVars.s_startTime, now));
            bool cheated = (stationCheated || state.Booleans[ZDOVars.s_cheatedQueued]) && !PlayerProfile.s_bypassCheatChecks;
            void Emit(string input, int amount)
            {
                var conversion = station.m_conversion.FirstOrDefault(c => c.m_from == null || c.m_from.name == input);
                if (conversion?.m_to == null) throw new OperationRejectedException("smelter-conversion-unavailable:" + input);
                var item = conversion.m_to.m_itemData.Clone(); item.m_dropPrefab = conversion.m_to.gameObject;
                item.m_stack = amount; item.m_worldLevel = (byte)Game.m_worldLevel; item.m_equipped = false; item.m_pickedUp = false;
                item.m_cheated = cheated; item.m_durability = item.GetMaxDurability();
                if (!NativeInventoryCodec.AddExact(state.Output, item)) throw new OperationDeferredException("station-output-buffer-full");
            }
            void Flush()
            {
                int amount = state.Integers[ZDOVars.s_spawnAmount]; if (amount == 0) return;
                Emit(state.Strings[ZDOVars.s_spawnOre], amount); state.Integers[ZDOVars.s_spawnAmount] = 0; state.Strings[ZDOVars.s_spawnOre] = "";
            }
            if (IsMaterialIdle(state, station))
            {
                // Empty stations can retain an old persisted baseline. Their whole elapsed
                // seconds do no work, so drain them without iterating up to an hour each tick.
                if (state.Integers[ZDOVars.s_queued] == 0 || station.m_maxFuel > 0 && state.Floats[ZDOVars.s_fuel] == 0) Flush();
                state.Floats[ZDOVars.s_accTime] = accumulator % 1;
                return;
            }
            while (accumulator >= 1)
            {
                accumulator -= 1;
                float fuel = state.Floats[ZDOVars.s_fuel]; int queued = state.Integers[ZDOVars.s_queued];
                string ore = queued == 0 ? "" : state.Strings[ZDOVars.s_item0];
                if (station.m_maxFuel != 0 && fuel <= 0 || station.m_maxOre != 0 && ore.Length == 0 || station.m_secPerProduct <= 0 ||
                    station.m_requiresRoof && !environment.Sheltered || environment.SmokeBlocked) continue;
                float work = station.m_windmill == null ? 1 : environment.WindPower;
                if (station.m_maxFuel > 0)
                {
                    fuel -= work / (station.m_secPerProduct / station.m_fuelPerProduct);
                    state.Floats[ZDOVars.s_fuel] = fuel < 0.0001f ? 0 : fuel;
                }
                state.Floats[ZDOVars.s_bakeTimer] += work;
                if (state.Floats[ZDOVars.s_bakeTimer] < station.m_secPerProduct) continue;
                state.Floats[ZDOVars.s_bakeTimer] = 0;
                if (queued > 0)
                {
                    for (int n = 0; n < queued - 1; n++) state.Strings[("item" + n).GetStableHashCode()] = state.Strings[("item" + (n + 1)).GetStableHashCode()];
                    state.Strings[("item" + (queued - 1)).GetStableHashCode()] = ""; state.Integers[ZDOVars.s_queued]--;
                }
                if (!station.m_spawnStack) { Emit(ore, 1); continue; }
                if (state.Strings[ZDOVars.s_spawnOre] != ore) Flush();
                state.Strings[ZDOVars.s_spawnOre] = ore; state.Integers[ZDOVars.s_spawnAmount]++;
                var conversion = station.m_conversion.FirstOrDefault(c => c.m_from == null || c.m_from.name == ore);
                if (conversion?.m_to == null || state.Integers[ZDOVars.s_spawnAmount] >= conversion.m_to.m_itemData.m_shared.m_maxStackSize) Flush();
            }
            if (state.Integers[ZDOVars.s_queued] == 0 || station.m_maxFuel > 0 && state.Floats[ZDOVars.s_fuel] == 0) Flush();
            state.Floats[ZDOVars.s_accTime] = accumulator;
        }
    }
}
