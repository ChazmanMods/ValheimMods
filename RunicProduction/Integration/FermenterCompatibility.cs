using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal sealed class FermenterConversionDescriptor
    {
        internal FermenterConversionDescriptor(
            string inputPrefabId,
            string outputPrefabId,
            int outputAmount)
        {
            InputPrefabId = inputPrefabId;
            OutputPrefabId = outputPrefabId;
            OutputAmount = outputAmount;
        }

        internal string InputPrefabId { get; }
        internal string OutputPrefabId { get; }
        internal int OutputAmount { get; }
    }

    internal sealed class FermenterDescriptor
    {
        private readonly ReadOnlyCollection<FermenterConversionDescriptor> _conversions;
        private readonly Dictionary<string, FermenterConversionDescriptor> _byInput;
        private readonly Dictionary<string, FermenterConversionDescriptor> _byOutput;

        internal FermenterDescriptor(
            string prefabId,
            IEnumerable<FermenterConversionDescriptor> conversions,
            float fermentationSeconds = 2400f,
            string skippedRecipes = "")
        {
            PrefabId = prefabId;
            FermentationSeconds = fermentationSeconds;
            SkippedRecipes = skippedRecipes ?? string.Empty;
            var ordered = conversions.OrderBy(value => value.InputPrefabId, StringComparer.Ordinal).ToList();
            _conversions = ordered.AsReadOnly();
            _byInput = ordered.ToDictionary(value => value.InputPrefabId, StringComparer.Ordinal);
            // Ordinary input/output links can use multiple bases producing the same item.
            // Exemplar replenishment must still resolve a single unambiguous ingredient.
            _byOutput = ordered.GroupBy(value => value.OutputPrefabId, StringComparer.Ordinal)
                .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        }

        internal string PrefabId { get; }
        internal float FermentationSeconds { get; }
        internal string SkippedRecipes { get; }
        internal IReadOnlyList<FermenterConversionDescriptor> Conversions => _conversions;

        internal bool TryFromInput(string prefabId, out FermenterConversionDescriptor conversion) =>
            _byInput.TryGetValue(prefabId ?? string.Empty, out conversion);

        internal bool TryFromOutput(string prefabId, out FermenterConversionDescriptor conversion) =>
            _byOutput.TryGetValue(prefabId ?? string.Empty, out conversion);
    }

    /// <summary>
    /// Validate the native station structure and its current owner-side recipes.
    /// Unsupported recipes are isolated; native status determines fermentation timing.
    /// </summary>
    internal static class FermenterCompatibility
    {
        private const int MaximumConversions = 256;

        internal static bool TryValidate(
            Fermenter station,
            FermenterPrefabPolicy policy,
            bool requireAllowed,
            out FermenterDescriptor descriptor,
            out string failure)
        {
            descriptor = null;
            failure = string.Empty;
            if (station == null || !ValheimAccess.TryGetFermenterPrefab(
                    station, out string prefabId, out GameObject registeredPrefab))
                return Fail(global::Runic.Localization.RunicText.Get("text_f925ceda2235"), out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail(global::Runic.Localization.RunicText.Get("text_1a3c654de568"), out failure);
            if (!RootIsExact(station.gameObject, station, ValheimAccess.View(station)) ||
                !RegisteredRootIsExact(registeredPrefab))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_4188f535edf9"),
                    out failure);
            if (station.gameObject.GetComponent<Smelter>() != null ||
                station.gameObject.GetComponent<CookingStation>() != null ||
                station.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                station.GetComponentsInChildren<Container>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail(global::Runic.Localization.RunicText.Get("text_3924d23c568e"), out failure);
            ZNetView view = ValheimAccess.View(station);
            if (view == null || !view.IsValid() || ValheimAccess.Zdo(station) == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_c09b7d512030"), out failure);
            if (!ValidDuration(station.m_fermentationDuration))
                return Fail(global::Runic.Localization.RunicText.Get("fermenter_invalid_duration"), out failure);
            if (!IsFinite(station.m_tapDelay) || station.m_tapDelay < 0f)
                return Fail(global::Runic.Localization.RunicText.Get("fermenter_invalid_tap_delay"), out failure);
            if (!ControlInside(station, station.m_addSwitch) ||
                !ControlInside(station, station.m_tapSwitch) ||
                !TransformInside(station, station.m_outputPoint) ||
                !TransformInside(station, station.m_roofCheckPoint) ||
                station.m_addSwitch == null || station.m_tapSwitch == null ||
                station.m_outputPoint == null || station.m_roofCheckPoint == null)
                return Fail(global::Runic.Localization.RunicText.Get("text_d86a646ee11c"), out failure);
            if (!TryReadConversions(station.m_conversion, out List<FermenterConversionDescriptor> conversions,
                    out failure))
                return false;
            // The live owner-side station is the definition that native fermentation uses.
            // Mods may legitimately change it after the prefab was registered. Stock plans
            // still verify the selected recipe's signature before consuming ingredients.
            descriptor = new FermenterDescriptor(prefabId, conversions, station.m_fermentationDuration, failure);
            failure = string.Empty;
            return true;
        }

        internal static bool TryValidateState(
            Fermenter station,
            FermenterDescriptor descriptor,
            out FermenterStationState state,
            out string failure)
        {
            state = null;
            failure = string.Empty;
            if (station == null || descriptor == null ||
                !FermenterStationState.TryCapture(station, out state))
                return Fail(global::Runic.Localization.RunicText.Get("text_93d43bc6922d"), out failure);
            if (!state.IsEmpty && !descriptor.TryFromInput(state.InputPrefabId, out _))
                return Fail(global::Runic.Localization.RunicText.Get("text_54a0b542fd9b"), out failure);
            if (ValheimAccess.FermenterDelayedTapActive(station))
                return Fail(global::Runic.Localization.RunicText.Get("text_d8bba0e200eb"), out failure);
            return true;
        }

        /// <summary>
        /// Captures the immutable producer definition from the registered prefab. Unlike
        /// TryValidate, this does not assert that a live Fermenter is locally owned or usable;
        /// runtime cover/state checks remain an owner-side obligation.
        /// </summary>
        internal static bool TryDescribeRegistered(
            Fermenter registered,
            string prefabId,
            out FermenterDescriptor descriptor,
            out string failure)
        {
            descriptor = null;
            failure = string.Empty;
            if (registered == null ||
                !StockDomainValidation.IsExactPrefabId(prefabId) ||
                !RegisteredRootIsExact(registered.gameObject) ||
                registered.gameObject.GetComponent<Smelter>() != null ||
                registered.gameObject.GetComponent<CookingStation>() != null ||
                registered.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registered.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_0ad53d0aa4fb"),
                    out failure);
            if (!ValidDuration(registered.m_fermentationDuration) ||
                !IsFinite(registered.m_tapDelay) || registered.m_tapDelay < 0f ||
                registered.m_addSwitch == null ||
                registered.m_tapSwitch == null || registered.m_outputPoint == null ||
                registered.m_roofCheckPoint == null ||
                !ControlInside(registered, registered.m_addSwitch) ||
                !ControlInside(registered, registered.m_tapSwitch) ||
                !TransformInside(registered, registered.m_outputPoint) ||
                !TransformInside(registered, registered.m_roofCheckPoint) ||
                !TryReadConversions(
                    registered.m_conversion,
                    out List<FermenterConversionDescriptor> conversions,
                    out failure))
                return false;
            descriptor = new FermenterDescriptor(prefabId, conversions, registered.m_fermentationDuration, failure);
            failure = string.Empty;
            return true;
        }

        private static bool TryReadConversions(
            List<Fermenter.ItemConversion> source,
            out List<FermenterConversionDescriptor> descriptors,
            out string failure)
        {
            // Bound the native table before allocating its managed snapshot.
            if (source == null || source.Count == 0 || source.Count > MaximumConversions)
            {
                descriptors = new List<FermenterConversionDescriptor>();
                return Fail(global::Runic.Localization.RunicText.Get("text_6586c7afb117"), out failure);
            }
            var definitions = source?.Select(conversion => new FermenterConversionDescriptor(
                conversion?.m_from == null ? string.Empty : ValheimAccess.PrefabName(conversion.m_from.gameObject),
                conversion?.m_to == null ? string.Empty : ValheimAccess.PrefabName(conversion.m_to.gameObject),
                conversion?.m_producedItems ?? 0)).ToList();
            return TryValidateConversions(definitions,
                prefab => ValheimAccess.RegisteredItemPrefab(prefab)?.GetComponent<ItemDrop>() != null,
                out descriptors, out failure);
        }

        internal static bool TryValidateConversions(
            IReadOnlyList<FermenterConversionDescriptor> source,
            Func<string, bool> isRegisteredItem,
            out List<FermenterConversionDescriptor> descriptors,
            out string failure)
        {
            descriptors = new List<FermenterConversionDescriptor>();
            failure = string.Empty;
            if (source == null || source.Count == 0 || source.Count > MaximumConversions)
                return Fail(global::Runic.Localization.RunicText.Get("text_6586c7afb117"), out failure);
            var duplicates = new HashSet<string>(source.Where(value => value != null)
                .GroupBy(value => value.InputPrefabId ?? string.Empty, StringComparer.Ordinal)
                .Where(group => group.Any(value => value.OutputPrefabId != group.First().OutputPrefabId ||
                    value.OutputAmount != group.First().OutputAmount)).Select(group => group.Key), StringComparer.Ordinal);
            var acceptedInputs = new HashSet<string>(StringComparer.Ordinal);
            var skipped = new List<string>();
            foreach (FermenterConversionDescriptor conversion in source)
            {
                string input = conversion?.InputPrefabId ?? string.Empty;
                string output = conversion?.OutputPrefabId ?? string.Empty;
                int amount = conversion?.OutputAmount ?? 0;
                // Use the declared complete batch, bounded by the same limit as stock
                // transfers. Vanilla's historical batch sizes are not a safety invariant.
                if (amount <= 0 || amount > StockDomainValidation.MaximumItemAmount)
                {
                    skipped.Add(global::Runic.Localization.RunicText.Format(
                        "fermenter_invalid_batch", input, output, amount,
                        StockDomainValidation.MaximumItemAmount));
                    continue;
                }
                string reason = null;
                if (!StockDomainValidation.IsExactPrefabId(input)) reason = "invalid-input-id";
                else if (!StockDomainValidation.IsExactPrefabId(output)) reason = "invalid-output-id";
                else if (duplicates.Contains(input)) reason = "duplicate-input";
                else if (!isRegisteredItem(input)) reason = "unregistered-input";
                else if (!isRegisteredItem(output)) reason = "unregistered-output";
                if (reason != null)
                {
                    skipped.Add(global::Runic.Localization.RunicText.Format(
                        "fermenter_invalid_recipe_detail", input, output, amount, reason));
                    continue;
                }
                if (acceptedInputs.Add(input)) descriptors.Add(new FermenterConversionDescriptor(input, output, amount));
            }
            // One unusable mod recipe must not disable unrelated native recipes.
            failure = string.Join(" ", skipped);
            return descriptors.Count > 0;
        }

        private static bool RootIsExact(GameObject root, Fermenter station, ZNetView expectedView) =>
            root != null &&
            root.GetComponentsInChildren<Fermenter>(true).Length == 1 &&
            ReferenceEquals(root.GetComponent<Fermenter>(), station) &&
            root.GetComponentsInChildren<ZNetView>(true).Length == 1 &&
            ReferenceEquals(root.GetComponent<ZNetView>(), expectedView) &&
            root.GetComponentsInChildren<WearNTear>(true).Length == 1 &&
            root.GetComponent<WearNTear>() != null;

        private static bool RegisteredRootIsExact(GameObject root) =>
            root != null &&
            root.GetComponentsInChildren<Fermenter>(true).Length == 1 &&
            root.GetComponent<Fermenter>() != null &&
            root.GetComponentsInChildren<ZNetView>(true).Length == 1 &&
            root.GetComponent<ZNetView>() != null &&
            root.GetComponentsInChildren<WearNTear>(true).Length == 1 &&
            root.GetComponent<WearNTear>() != null;

        private static bool ControlInside(Fermenter station, Switch control) =>
            control != null && TransformInside(station, control.transform);

        private static bool TransformInside(Fermenter station, Transform value) =>
            value != null &&
            (ReferenceEquals(value, station.transform) || value.IsChildOf(station.transform));

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool ValidDuration(float value) => IsFinite(value) && value > 0f;

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
