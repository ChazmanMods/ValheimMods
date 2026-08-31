using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal static class CookingStationCompatibility
    {
        internal static bool TryDescribeRegistered(
            GameObject registeredPrefab,
            CookingStationPrefabPolicy policy,
            bool requireAllowed,
            out CookingStation station,
            out string prefabId,
            out string failure)
        {
            station = null;
            prefabId = ValheimAccess.PrefabName(registeredPrefab);
            failure = string.Empty;
            CookingStation[] stations = registeredPrefab == null
                ? Array.Empty<CookingStation>()
                : registeredPrefab.GetComponents<CookingStation>();
            ZNetView[] views = registeredPrefab == null
                ? Array.Empty<ZNetView>()
                : registeredPrefab.GetComponents<ZNetView>();
            if (registeredPrefab == null || prefabId.Length == 0 || stations.Length != 1 ||
                views.Length != 1 || !ReferenceEquals(stations[0].gameObject, registeredPrefab) ||
                !ReferenceEquals(views[0].gameObject, registeredPrefab) ||
                registeredPrefab.GetComponent<WearNTear>() == null)
                return Fail("CookingStation, ZNetView, and WearNTear require one registered root.", out failure);
            station = stations[0];
            if (registeredPrefab.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail("Hybrid registered CookingStation prefabs are unsupported.", out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail("This exact CookingStation prefab ID is not enabled.", out failure);
            if (station.m_slots == null || station.m_slots.Length < 1 ||
                station.m_slots.Length > 64)
                return Fail("CookingStation slot capacity is outside 1-64.", out failure);
            var slots = new HashSet<int>();
            foreach (Transform slot in station.m_slots)
                if (slot == null || !slots.Add(slot.GetInstanceID()) ||
                    !(ReferenceEquals(slot, station.transform) || slot.IsChildOf(station.transform)) ||
                    slot.GetComponent<ParticleSystem>() == null ||
                    slot.GetComponent<AudioSource>() == null)
                    return Fail("CookingStation slot transforms are incomplete or ambiguous.", out failure);
            if (station.m_donePS == null || station.m_burntPS == null ||
                station.m_donePS.Length > 0 &&
                (station.m_donePS.Length < station.m_slots.Length ||
                 station.m_donePS.Any(value => value == null)) ||
                station.m_burntPS.Length > 0 &&
                (station.m_burntPS.Length < station.m_slots.Length ||
                 station.m_burntPS.Any(value => value == null)))
                return Fail("CookingStation visual arrays do not cover every slot.", out failure);
            if (station.m_conversion == null || station.m_conversion.Count < 1 ||
                station.m_conversion.Count > 256 || station.m_overCookedItem == null)
                return Fail("CookingStation conversion metadata is unavailable.", out failure);
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (CookingStation.ItemConversion conversion in station.m_conversion)
            {
                string from = conversion?.m_from == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_from.gameObject);
                string to = conversion?.m_to == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_to.gameObject);
                if (from.Length == 0 || to.Length == 0 ||
                    !IsFinitePositive(conversion.m_cookTime) || !inputs.Add(from) ||
                    ValheimAccess.RegisteredItemPrefab(from)?.GetComponent<ItemDrop>() == null ||
                    ValheimAccess.RegisteredItemPrefab(to)?.GetComponent<ItemDrop>() == null ||
                    ValheimAccess.RegisteredItemPrefab(from).transform.Find("attach") == null ||
                    ValheimAccess.RegisteredItemPrefab(to).transform.Find("attach") == null)
                    return Fail("CookingStation conversion metadata is invalid or ambiguous.", out failure);
                outputs.Add(to);
            }
            string burnt = ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            if (burnt.Length == 0 || inputs.Overlaps(outputs) || inputs.Contains(burnt) ||
                outputs.Contains(burnt) ||
                ValheimAccess.RegisteredItemPrefab(burnt)?.GetComponent<ItemDrop>() == null ||
                ValheimAccess.RegisteredItemPrefab(burnt).transform.Find("attach") == null ||
                station.m_requireFire == station.m_useFuel)
                return Fail("CookingStation output/heat metadata is ambiguous.", out failure);
            if (station.m_useFuel &&
                (station.m_fuelItem == null || station.m_maxFuel <= 0 ||
                 station.m_maxFuel > 100000 || !IsFinitePositive(station.m_secPerFuel)))
                return Fail("CookingStation fuel metadata is invalid.", out failure);
            failure = "ok";
            return true;
        }

        internal static bool TryValidate(
            CookingStation station,
            CookingStationPrefabPolicy policy,
            bool requireAllowed,
            out string prefabId,
            out string failure)
        {
            prefabId = string.Empty;
            failure = string.Empty;
            if (station == null || !ValheimAccess.TryGetCookingStationPrefab(
                    station, out prefabId, out GameObject registeredPrefab))
                return Fail("The exact CookingStation prefab identity is unavailable.", out failure);
            if (station.gameObject.GetComponent<CookingStation>() != station ||
                station.gameObject.GetComponents<CookingStation>().Length != 1 ||
                station.gameObject.GetComponent<ZNetView>() != ValheimAccess.View(station) ||
                station.gameObject.GetComponent<WearNTear>() == null ||
                registeredPrefab.GetComponents<CookingStation>().Length != 1 ||
                registeredPrefab.GetComponent<ZNetView>() == null ||
                registeredPrefab.GetComponent<WearNTear>() == null)
                return Fail("CookingStation, ZNetView, and WearNTear must share one unambiguous registered prefab root.", out failure);
            if (station.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                station.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                station.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0)
                return Fail(
                    "Hybrid Smelter, Fermenter, CraftingStation, or CookingStation prefabs are ambiguous and unsupported.",
                    out failure);
            if (station.GetComponentsInChildren<Container>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail("Hybrid Container/CookingStation prefabs make explicit link gestures ambiguous.", out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail("This exact CookingStation prefab ID is not enabled by the allow list.", out failure);
            ZNetView view = ValheimAccess.View(station);
            if (view == null || !view.IsValid() || ValheimAccess.Zdo(station) == null)
                return Fail("CookingStation network state is unavailable.", out failure);
            if (station.m_slots == null || station.m_slots.Length == 0 || station.m_slots.Length > 64)
                return Fail("CookingStation slot capacity is outside the supported 1-64 range.", out failure);
            var slots = new HashSet<int>();
            foreach (Transform slot in station.m_slots)
                if (slot == null || !slots.Add(slot.GetInstanceID()) ||
                    !(ReferenceEquals(slot, station.transform) || slot.IsChildOf(station.transform)) ||
                    slot.GetComponent<ParticleSystem>() == null || slot.GetComponent<AudioSource>() == null)
                    return Fail("CookingStation slot transforms are missing or ambiguous.", out failure);
            if (station.m_donePS == null || station.m_burntPS == null ||
                (station.m_donePS.Length > 0 &&
                 (station.m_donePS.Length < station.m_slots.Length || station.m_donePS.Any(value => value == null))) ||
                (station.m_burntPS.Length > 0 &&
                 (station.m_burntPS.Length < station.m_slots.Length || station.m_burntPS.Any(value => value == null))))
                return Fail("CookingStation done/burnt visual arrays do not safely cover every slot.", out failure);
            if (station.m_conversion == null || station.m_conversion.Count == 0 || station.m_conversion.Count > 256)
                return Fail("CookingStation conversion count is outside the supported 1-256 range.", out failure);
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (CookingStation.ItemConversion conversion in station.m_conversion)
            {
                string from = conversion?.m_from == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_from.gameObject);
                string to = conversion?.m_to == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_to.gameObject);
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) ||
                    !IsFinitePositive(conversion.m_cookTime) || !inputs.Add(from) ||
                    ValheimAccess.RegisteredItemPrefab(from)?.GetComponent<ItemDrop>() == null ||
                    ValheimAccess.RegisteredItemPrefab(to)?.GetComponent<ItemDrop>() == null)
                    return Fail("CookingStation conversions contain a missing, invalid, or ambiguous input mapping.", out failure);
                if (ValheimAccess.RegisteredItemPrefab(from).transform.Find("attach") == null ||
                    ValheimAccess.RegisteredItemPrefab(to).transform.Find("attach") == null)
                    return Fail("CookingStation item prefabs require vanilla attach visuals.", out failure);
                outputs.Add(to);
            }
            if (inputs.Overlaps(outputs))
                return Fail("CookingStation input/output prefab names collide and make vanilla conversion order ambiguous.", out failure);
            string burnt = station.m_overCookedItem == null
                ? string.Empty
                : ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            if (string.IsNullOrEmpty(burnt) ||
                ValheimAccess.RegisteredItemPrefab(burnt)?.GetComponent<ItemDrop>() == null ||
                ValheimAccess.RegisteredItemPrefab(burnt).transform.Find("attach") == null)
                return Fail("CookingStation has no valid vanilla overcooked item.", out failure);
            if (inputs.Contains(burnt) || outputs.Contains(burnt))
                return Fail("CookingStation overcooked prefab collides with its conversion table.", out failure);
            if (station.m_requireFire == station.m_useFuel)
                return Fail("CookingStation must use exactly one vanilla heat model: external fire XOR internal fuel.", out failure);
            if (station.m_requireFire &&
                (station.m_fireCheckPoints == null || station.m_fireCheckPoints.Length == 0 ||
                 station.m_fireCheckPoints.Length > 64 || !IsFinitePositive(station.m_fireCheckRadius)))
                return Fail("Fire-gated CookingStation fields are incomplete or out of bounds.", out failure);
            if (station.m_requireFire)
                foreach (Transform firePoint in station.m_fireCheckPoints)
                    if (firePoint == null ||
                        !(ReferenceEquals(firePoint, station.transform) || firePoint.IsChildOf(station.transform)))
                        return Fail("CookingStation fire points must remain inside the registered station root.", out failure);
            if (station.m_useFuel &&
                (station.m_fuelItem == null || station.m_maxFuel <= 0 || station.m_maxFuel > 100000 ||
                 station.m_secPerFuel <= 0 || station.m_addFuelSwitch == null ||
                 ValheimAccess.RegisteredItemPrefab(
                     ValheimAccess.PrefabName(station.m_fuelItem.gameObject))?.GetComponent<ItemDrop>() == null))
                return Fail("Fuelled CookingStation fields are incomplete or out of bounds.", out failure);
            if (!ControlInside(station, station.m_addFoodSwitch) ||
                !ControlInside(station, station.m_addFuelSwitch))
                return Fail("CookingStation controls must remain inside the registered station root.", out failure);
            return true;
        }

        internal static bool TryValidateState(CookingStation station, out string failure)
        {
            failure = string.Empty;
            if (station == null || station.m_slots == null)
                return Fail("CookingStation state is unavailable.", out failure);
            var inputs = new HashSet<string>(station.m_conversion.Select(conversion =>
                ValheimAccess.PrefabName(conversion.m_from.gameObject)), StringComparer.Ordinal);
            var outputs = new HashSet<string>(station.m_conversion.Select(conversion =>
                ValheimAccess.PrefabName(conversion.m_to.gameObject)), StringComparer.Ordinal);
            string burnt = ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            for (int slot = 0; slot < station.m_slots.Length; slot++)
            {
                ValheimAccess.GetCookingSlot(station, slot, out string item, out float elapsed,
                    out CookingSlotStatus status);
                if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0f ||
                    !Enum.IsDefined(typeof(CookingSlotStatus), status))
                    return Fail("CookingStation contains non-finite or invalid slot state.", out failure);
                if (string.IsNullOrEmpty(item))
                {
                    if (elapsed != 0f || status != CookingSlotStatus.NotDone)
                        return Fail("CookingStation contains a non-canonical empty slot.", out failure);
                    continue;
                }
                bool valid = status == CookingSlotStatus.NotDone && inputs.Contains(item) ||
                             status == CookingSlotStatus.Done && outputs.Contains(item) ||
                             status == CookingSlotStatus.Burnt &&
                             string.Equals(item, burnt, StringComparison.Ordinal);
                if (!valid)
                    return Fail("CookingStation slot item/status does not match its exact vanilla conversion table.", out failure);
            }
            if (station.m_useFuel)
            {
                float fuel = ValheimAccess.CookingFuel(station);
                // Vanilla RPC fuel additions are not an atomic compare-and-add. Two accepted
                // additions can therefore leave a finite value above the configured display
                // capacity. That state still cooks normally; automation must simply refrain
                // from adding more fuel instead of disabling all slot input/output handling.
                if (float.IsNaN(fuel) || float.IsInfinity(fuel) || fuel < 0f)
                    return Fail("CookingStation fuel state is non-finite or negative.", out failure);
            }
            return true;
        }

        private static bool ControlInside(CookingStation station, Switch control) =>
            control == null || ReferenceEquals(control.transform, station.transform) ||
            control.transform.IsChildOf(station.transform);

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
