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
            IEnumerable<FermenterConversionDescriptor> conversions)
        {
            PrefabId = prefabId;
            var ordered = conversions.OrderBy(value => value.InputPrefabId, StringComparer.Ordinal).ToList();
            _conversions = ordered.AsReadOnly();
            _byInput = ordered.ToDictionary(value => value.InputPrefabId, StringComparer.Ordinal);
            _byOutput = ordered.ToDictionary(value => value.OutputPrefabId, StringComparer.Ordinal);
        }

        internal string PrefabId { get; }
        internal IReadOnlyList<FermenterConversionDescriptor> Conversions => _conversions;

        internal bool TryFromInput(string prefabId, out FermenterConversionDescriptor conversion) =>
            _byInput.TryGetValue(prefabId ?? string.Empty, out conversion);

        internal bool TryFromOutput(string prefabId, out FermenterConversionDescriptor conversion) =>
            _byOutput.TryGetValue(prefabId ?? string.Empty, out conversion);
    }

    /// <summary>
    /// Fermenter support is narrower than reflection-based compatibility. An exact
    /// registered root must retain vanilla structure, timing, cover controls, and unambiguous
    /// complete-batch conversions before any persisted link may own it.
    /// </summary>
    internal static class FermenterCompatibility
    {
        private const float VanillaFermentationSeconds = 2400f;
        private const float DurationTolerance = 0.01f;
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
                return Fail("The exact registered Fermenter prefab identity is unavailable.", out failure);
            if (requireAllowed && (policy == null || !policy.Allows(prefabId)))
                return Fail("This exact Fermenter prefab ID is not enabled by the local allow list.", out failure);
            if (!RootIsExact(station.gameObject, station, ValheimAccess.View(station)) ||
                !RegisteredRootIsExact(registeredPrefab))
                return Fail(
                    "Fermenter, ZNetView, and WearNTear must be unique components on one registered prefab root.",
                    out failure);
            if (station.gameObject.GetComponent<Smelter>() != null ||
                station.gameObject.GetComponent<CookingStation>() != null ||
                station.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<CraftingStation>(true).Length != 0 ||
                station.GetComponentsInChildren<Container>(true).Length != 0 ||
                registeredPrefab.GetComponentsInChildren<Container>(true).Length != 0)
                return Fail("Hybrid production/container Fermenter roots are ambiguous and unsupported.", out failure);
            ZNetView view = ValheimAccess.View(station);
            if (view == null || !view.IsValid() || ValheimAccess.Zdo(station) == null)
                return Fail("Fermenter network state is unavailable.", out failure);
            if (!IsFinite(station.m_fermentationDuration) ||
                Math.Abs(station.m_fermentationDuration - VanillaFermentationSeconds) > DurationTolerance)
                return Fail("Fermenter duration must retain vanilla's exact 2400-second model.", out failure);
            if (!IsFinite(station.m_tapDelay) || station.m_tapDelay < 0f || station.m_tapDelay > 30f)
                return Fail("Fermenter tap delay is non-finite or outside the supported vanilla range.", out failure);
            if (!ControlInside(station, station.m_addSwitch) ||
                !ControlInside(station, station.m_tapSwitch) ||
                !TransformInside(station, station.m_outputPoint) ||
                !TransformInside(station, station.m_roofCheckPoint) ||
                station.m_addSwitch == null || station.m_tapSwitch == null ||
                station.m_outputPoint == null || station.m_roofCheckPoint == null)
                return Fail("Fermenter controls, output, and cover points must remain inside its exact root.", out failure);
            if (!TryReadConversions(station.m_conversion, out List<FermenterConversionDescriptor> conversions,
                    out failure))
                return false;
            Fermenter registered = registeredPrefab.GetComponent<Fermenter>();
            if (registered == null || !IsFinite(registered.m_fermentationDuration) ||
                Math.Abs(registered.m_fermentationDuration - VanillaFermentationSeconds) > DurationTolerance ||
                !IsFinite(registered.m_tapDelay) || registered.m_tapDelay < 0f ||
                registered.m_tapDelay > 30f || registered.m_addSwitch == null ||
                registered.m_tapSwitch == null || registered.m_outputPoint == null ||
                registered.m_roofCheckPoint == null ||
                !ControlInside(registered, registered.m_addSwitch) ||
                !ControlInside(registered, registered.m_tapSwitch) ||
                !TransformInside(registered, registered.m_outputPoint) ||
                !TransformInside(registered, registered.m_roofCheckPoint) ||
                !TryReadConversions(registered.m_conversion,
                    out List<FermenterConversionDescriptor> registeredConversions, out failure) ||
                !SameConversions(conversions, registeredConversions))
                return Fail(
                    "The live Fermenter no longer matches its exact registered vanilla-compatible definition.",
                    out failure);
            descriptor = new FermenterDescriptor(prefabId, conversions);
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
                return Fail("Fermenter content/start state is non-canonical.", out failure);
            if (!state.IsEmpty && !descriptor.TryFromInput(state.InputPrefabId, out _))
                return Fail("Fermenter content does not match its exact conversion table.", out failure);
            if (ValheimAccess.FermenterDelayedTapActive(station))
                return Fail("A vanilla DelayedTap is active; links and refresh must wait for it to complete.", out failure);
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
                    "The registered Fermenter producer root is structurally ambiguous.",
                    out failure);
            if (!IsFinite(registered.m_fermentationDuration) ||
                Math.Abs(registered.m_fermentationDuration - VanillaFermentationSeconds) >
                    DurationTolerance ||
                !IsFinite(registered.m_tapDelay) || registered.m_tapDelay < 0f ||
                registered.m_tapDelay > 30f || registered.m_addSwitch == null ||
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
            descriptor = new FermenterDescriptor(prefabId, conversions);
            failure = string.Empty;
            return true;
        }

        private static bool TryReadConversions(
            List<Fermenter.ItemConversion> source,
            out List<FermenterConversionDescriptor> descriptors,
            out string failure)
        {
            descriptors = new List<FermenterConversionDescriptor>();
            failure = string.Empty;
            if (source == null || source.Count == 0 || source.Count > MaximumConversions)
                return Fail("Fermenter conversion count is outside the supported 1-256 range.", out failure);
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (Fermenter.ItemConversion conversion in source)
            {
                string input = conversion?.m_from == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_from.gameObject);
                string output = conversion?.m_to == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_to.gameObject);
                int amount = conversion?.m_producedItems ?? 0;
                if (!StockDomainValidation.IsExactPrefabId(input) ||
                    !StockDomainValidation.IsExactPrefabId(output) ||
                    !inputs.Add(input) || !outputs.Add(output) ||
                    (amount != 3 && amount != 6) ||
                    ValheimAccess.RegisteredItemPrefab(input)?.GetComponent<ItemDrop>() == null ||
                    ValheimAccess.RegisteredItemPrefab(output)?.GetComponent<ItemDrop>() == null)
                    return Fail(
                        "Fermenter conversions require unique exact inputs/outputs and complete vanilla 3/6-item batches.",
                        out failure);
                descriptors.Add(new FermenterConversionDescriptor(input, output, amount));
            }
            if (inputs.Overlaps(outputs))
                return Fail("Fermenter input/output prefab IDs collide and are ambiguous.", out failure);
            return true;
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

        private static bool SameConversions(
            IEnumerable<FermenterConversionDescriptor> left,
            IEnumerable<FermenterConversionDescriptor> right)
        {
            FermenterConversionDescriptor[] leftOrdered = left
                .OrderBy(value => value.InputPrefabId, StringComparer.Ordinal).ToArray();
            FermenterConversionDescriptor[] rightOrdered = right
                .OrderBy(value => value.InputPrefabId, StringComparer.Ordinal).ToArray();
            if (leftOrdered.Length != rightOrdered.Length) return false;
            for (int index = 0; index < leftOrdered.Length; index++)
                if (!string.Equals(leftOrdered[index].InputPrefabId,
                        rightOrdered[index].InputPrefabId, StringComparison.Ordinal) ||
                    !string.Equals(leftOrdered[index].OutputPrefabId,
                        rightOrdered[index].OutputPrefabId, StringComparison.Ordinal) ||
                    leftOrdered[index].OutputAmount != rightOrdered[index].OutputAmount)
                    return false;
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
