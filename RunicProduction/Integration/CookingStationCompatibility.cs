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
                return Fail(global::Runic.Localization.RunicText.Get("text_83029d0196d3"), out failure);
            station = stations[0];
            if (registeredPrefab.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail(global::Runic.Localization.RunicText.Get("text_7e32b115c0e4"), out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail(global::Runic.Localization.RunicText.Get("text_40cb432c298e"), out failure);
            if (station.m_slots == null || station.m_slots.Length < 1 ||
                station.m_slots.Length > 64)
                return Fail(global::Runic.Localization.RunicText.Get("text_38da285cb2e0"), out failure);
            var slots = new HashSet<int>();
            foreach (Transform slot in station.m_slots)
                if (slot == null || !slots.Add(slot.GetInstanceID()) ||
                    !(ReferenceEquals(slot, station.transform) || slot.IsChildOf(station.transform)) ||
                    slot.GetComponent<ParticleSystem>() == null ||
                    slot.GetComponent<AudioSource>() == null)
                    return Fail(global::Runic.Localization.RunicText.Get("text_87bb10a8e106"), out failure);
            if (station.m_donePS == null || station.m_burntPS == null ||
                station.m_donePS.Length > 0 &&
                (station.m_donePS.Length < station.m_slots.Length ||
                 station.m_donePS.Any(value => value == null)) ||
                station.m_burntPS.Length > 0 &&
                (station.m_burntPS.Length < station.m_slots.Length ||
                 station.m_burntPS.Any(value => value == null)))
                return Fail(global::Runic.Localization.RunicText.Get("text_a97f1b03235f"), out failure);
            if (station.m_conversion == null || station.m_conversion.Count < 1 ||
                station.m_conversion.Count > 256 || station.m_overCookedItem == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_9c6ea015cfc6"), out failure);
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
                    return Fail(global::Runic.Localization.RunicText.Get("text_a0f05c52eee3"), out failure);
                outputs.Add(to);
            }
            string burnt = ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            if (burnt.Length == 0 || inputs.Overlaps(outputs) || inputs.Contains(burnt) ||
                outputs.Contains(burnt) ||
                ValheimAccess.RegisteredItemPrefab(burnt)?.GetComponent<ItemDrop>() == null ||
                ValheimAccess.RegisteredItemPrefab(burnt).transform.Find("attach") == null ||
                station.m_requireFire == station.m_useFuel)
                return Fail(global::Runic.Localization.RunicText.Get("text_68198ee9ad9e"), out failure);
            if (station.m_useFuel &&
                (station.m_fuelItem == null || station.m_maxFuel <= 0 ||
                 station.m_maxFuel > 100000 || !IsFinitePositive(station.m_secPerFuel)))
                return Fail(global::Runic.Localization.RunicText.Get("text_cbd19d35a2f9"), out failure);
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
                return Fail(global::Runic.Localization.RunicText.Get("text_0b1aa3175a03"), out failure);
            if (station.gameObject.GetComponent<CookingStation>() != station ||
                station.gameObject.GetComponents<CookingStation>().Length != 1 ||
                station.gameObject.GetComponent<ZNetView>() != ValheimAccess.View(station) ||
                station.gameObject.GetComponent<WearNTear>() == null ||
                registeredPrefab.GetComponents<CookingStation>().Length != 1 ||
                registeredPrefab.GetComponent<ZNetView>() == null ||
                registeredPrefab.GetComponent<WearNTear>() == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_dc52f2f31ced"), out failure);
            if (station.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Smelter>(true).Length != 0 ||
                station.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Fermenter>(true).Length != 0 ||
                station.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_1e2ee20d8969"),
                    out failure);
            if (station.GetComponentsInChildren<Container>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail(global::Runic.Localization.RunicText.Get("text_736f46179b72"), out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail(global::Runic.Localization.RunicText.Get("text_251cbb36b389"), out failure);
            ZNetView view = ValheimAccess.View(station);
            if (view == null || !view.IsValid() || ValheimAccess.Zdo(station) == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_b04e8d7b888d"), out failure);
            if (station.m_slots == null || station.m_slots.Length == 0 || station.m_slots.Length > 64)
                return Fail(global::Runic.Localization.RunicText.Get("text_7014f3de58b7"), out failure);
            var slots = new HashSet<int>();
            foreach (Transform slot in station.m_slots)
                if (slot == null || !slots.Add(slot.GetInstanceID()) ||
                    !(ReferenceEquals(slot, station.transform) || slot.IsChildOf(station.transform)) ||
                    slot.GetComponent<ParticleSystem>() == null || slot.GetComponent<AudioSource>() == null)
                    return Fail(global::Runic.Localization.RunicText.Get("text_fea665ebe752"), out failure);
            if (station.m_donePS == null || station.m_burntPS == null ||
                (station.m_donePS.Length > 0 &&
                 (station.m_donePS.Length < station.m_slots.Length || station.m_donePS.Any(value => value == null))) ||
                (station.m_burntPS.Length > 0 &&
                 (station.m_burntPS.Length < station.m_slots.Length || station.m_burntPS.Any(value => value == null))))
                return Fail(global::Runic.Localization.RunicText.Get("text_068febd4228a"), out failure);
            if (station.m_conversion == null || station.m_conversion.Count == 0 || station.m_conversion.Count > 256)
                return Fail(global::Runic.Localization.RunicText.Get("text_de48c607837a"), out failure);
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
                    return Fail(global::Runic.Localization.RunicText.Get("text_eaa23a7432be"), out failure);
                if (ValheimAccess.RegisteredItemPrefab(from).transform.Find("attach") == null ||
                    ValheimAccess.RegisteredItemPrefab(to).transform.Find("attach") == null)
                    return Fail(global::Runic.Localization.RunicText.Get("text_e66d1a36a8df"), out failure);
                outputs.Add(to);
            }
            if (inputs.Overlaps(outputs))
                return Fail(global::Runic.Localization.RunicText.Get("text_9b6c84b2d14f"), out failure);
            string burnt = station.m_overCookedItem == null
                ? string.Empty
                : ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            if (string.IsNullOrEmpty(burnt) ||
                ValheimAccess.RegisteredItemPrefab(burnt)?.GetComponent<ItemDrop>() == null ||
                ValheimAccess.RegisteredItemPrefab(burnt).transform.Find("attach") == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_af73904ff410"), out failure);
            if (inputs.Contains(burnt) || outputs.Contains(burnt))
                return Fail(global::Runic.Localization.RunicText.Get("text_b9860f47713f"), out failure);
            if (station.m_requireFire == station.m_useFuel)
                return Fail(global::Runic.Localization.RunicText.Get("text_477eaf9a6f4a"), out failure);
            if (station.m_requireFire &&
                (station.m_fireCheckPoints == null || station.m_fireCheckPoints.Length == 0 ||
                 station.m_fireCheckPoints.Length > 64 || !IsFinitePositive(station.m_fireCheckRadius)))
                return Fail(global::Runic.Localization.RunicText.Get("text_700b68743320"), out failure);
            if (station.m_requireFire)
                foreach (Transform firePoint in station.m_fireCheckPoints)
                    if (firePoint == null ||
                        !(ReferenceEquals(firePoint, station.transform) || firePoint.IsChildOf(station.transform)))
                        return Fail(global::Runic.Localization.RunicText.Get("text_9083073e469c"), out failure);
            if (station.m_useFuel &&
                (station.m_fuelItem == null || station.m_maxFuel <= 0 || station.m_maxFuel > 100000 ||
                 station.m_secPerFuel <= 0 || station.m_addFuelSwitch == null ||
                 ValheimAccess.RegisteredItemPrefab(
                     ValheimAccess.PrefabName(station.m_fuelItem.gameObject))?.GetComponent<ItemDrop>() == null))
                return Fail(global::Runic.Localization.RunicText.Get("text_976004c92a8f"), out failure);
            if (!ControlInside(station, station.m_addFoodSwitch) ||
                !ControlInside(station, station.m_addFuelSwitch))
                return Fail(global::Runic.Localization.RunicText.Get("text_1ed85b29f5ab"), out failure);
            return true;
        }

        internal static bool TryValidateState(CookingStation station, out string failure)
        {
            failure = string.Empty;
            if (station == null || station.m_slots == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_16d68dc5a320"), out failure);
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
                    return Fail(global::Runic.Localization.RunicText.Get("text_0564406ad194"), out failure);
                if (string.IsNullOrEmpty(item))
                {
                    if (elapsed != 0f || status != CookingSlotStatus.NotDone)
                        return Fail(global::Runic.Localization.RunicText.Get("text_5a5f5219b433"), out failure);
                    continue;
                }
                bool valid = status == CookingSlotStatus.NotDone && inputs.Contains(item) ||
                             status == CookingSlotStatus.Done && outputs.Contains(item) ||
                             status == CookingSlotStatus.Burnt &&
                             string.Equals(item, burnt, StringComparison.Ordinal);
                if (!valid)
                    return Fail(global::Runic.Localization.RunicText.Get("text_fe889fbfdfe2"), out failure);
            }
            if (station.m_useFuel)
            {
                float fuel = ValheimAccess.CookingFuel(station);
                // Vanilla RPC fuel additions are not an atomic compare-and-add. Two accepted
                // additions can therefore leave a finite value above the configured display
                // capacity. That state still cooks normally; automation must simply refrain
                // from adding more fuel instead of disabling all slot input/output handling.
                if (float.IsNaN(fuel) || float.IsInfinity(fuel) || fuel < 0f)
                    return Fail(global::Runic.Localization.RunicText.Get("text_31dbf03bdbb8"), out failure);
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
