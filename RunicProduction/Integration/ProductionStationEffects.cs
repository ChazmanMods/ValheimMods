using System;
using UnityEngine;

namespace RunicProduction.Integration
{
    // Use the station's native effect prefabs, including their networked sound objects.
    // Call only after a confirmed commit: cosmetic failures must never retry an item transfer.
    internal static class ProductionStationEffects
    {
        private enum Kind { Input, Fuel, Output }

        internal static void InputAdded(Smelter station) => Play(station, Kind.Input);
        internal static void FuelAdded(Smelter station) => Play(station, Kind.Fuel);
        internal static void OutputProduced(Smelter station) => Play(station, Kind.Output);

        internal static void InputAdded(CookingStation station, int slot) => Safely(() =>
            station.m_addEffect?.Create(station.m_slots[slot].position, Quaternion.identity));

        internal static void FuelAdded(CookingStation station) => Safely(() =>
            station.m_fuelAddedEffects?.Create(
                station.transform.position, station.transform.rotation, station.transform));

        internal static void OutputCollected(CookingStation station, int slot) => Safely(() =>
            station.m_pickEffector?.Create(
                station.m_spawnPoint != null
                    ? station.m_spawnPoint.position
                    : station.m_slots[slot].position,
                Quaternion.identity));

        internal static void InputAdded(Fermenter station) => Safely(() =>
            station.m_addedEffects?.Create(station.transform.position, station.transform.rotation));

        internal static void OutputCollected(Fermenter station)
        {
            // Automation commits the whole batch immediately. Play both native tap/output
            // effects here without scheduling DelayedTap, which would spawn duplicate items.
            Safely(() => station.m_tapEffects?.Create(
                station.transform.position, station.transform.rotation));
            Safely(() => station.m_spawnEffects?.Create(
                station.m_outputPoint.position, Quaternion.identity));
        }

        internal static void RecipeCrafted(CraftingStation station)
        {
            // Automated recipes have no player or UI craft timer. Locate their native effects
            // at the station and emit them only after the ingredient/output transaction commits.
            Safely(() => station.m_craftItemEffects?.Create(
                station.transform.position, Quaternion.identity));
            Safely(() => station.m_craftItemDoneEffects?.Create(
                station.transform.position, Quaternion.identity));
        }

        private static void Safely(Action effect)
        {
            try { effect(); }
            catch (Exception)
            {
                // A missing/broken cosmetic prefab must never retry a committed transfer.
            }
        }

        private static void Play(Smelter station, Kind kind)
        {
            try
            {
                if (station == null) return;
                Transform origin = station.transform;
                switch (kind)
                {
                    case Kind.Input:
                        station.m_oreAddedEffects?.Create(origin.position, origin.rotation);
                        break;
                    case Kind.Fuel:
                        station.m_fuelAddedEffects?.Create(origin.position, origin.rotation, origin);
                        break;
                    case Kind.Output:
                        station.m_produceEffects?.Create(origin.position, origin.rotation);
                        break;
                }
            }
            catch (Exception)
            {
                // The transfer is already committed. Do not propagate into rollback/fallback.
            }
        }
    }
}
