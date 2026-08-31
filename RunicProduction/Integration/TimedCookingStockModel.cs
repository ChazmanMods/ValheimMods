using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal enum TimedCookingDestinationMode
    {
        None = 0,
        Throughput = 1,
        Replenishment = 2,
        Invalid = 3
    }

    internal static class TimedCookingDestinationPolicy
    {
        internal static TimedCookingDestinationMode Resolve(
            bool hasOutput,
            bool hasReplenishment) =>
            hasReplenishment
                    ? TimedCookingDestinationMode.Replenishment
                    : hasOutput
                        ? TimedCookingDestinationMode.Throughput
                        : TimedCookingDestinationMode.None;

        internal static bool IsExactRefresh(
            StoredProductionLink current,
            ZDOID selectedTarget) =>
            current != null && current.Role == Contracts.ProductionLinkRole.Replenishment &&
            current.Target == selectedTarget;
    }

    internal sealed class TimedCookingConversionDescriptor
    {
        internal TimedCookingConversionDescriptor(
            int index,
            string inputPrefabId,
            string outputPrefabId,
            float cookTime)
        {
            Index = index;
            InputPrefabId = inputPrefabId;
            OutputPrefabId = outputPrefabId;
            CookTime = cookTime;
        }

        internal int Index { get; }
        internal string InputPrefabId { get; }
        internal string OutputPrefabId { get; }
        internal float CookTime { get; }
    }

    /// <summary>An immutable description of the exact vanilla timing/conversion surface we sign.</summary>
    internal sealed class TimedCookingStationDescriptor
    {
        private readonly IReadOnlyList<TimedCookingConversionDescriptor> _conversions;

        internal TimedCookingStationDescriptor(
            string prefabId,
            int slotCount,
            bool requiresFire,
            bool usesFuel,
            string fuelPrefabId,
            int maximumFuel,
            int secondsPerFuel,
            string overcookedPrefabId,
            IReadOnlyList<TimedCookingConversionDescriptor> conversions)
        {
            PrefabId = prefabId;
            SlotCount = slotCount;
            RequiresFire = requiresFire;
            UsesFuel = usesFuel;
            FuelPrefabId = fuelPrefabId ?? string.Empty;
            MaximumFuel = maximumFuel;
            SecondsPerFuel = secondsPerFuel;
            OvercookedPrefabId = overcookedPrefabId;
            _conversions = conversions ?? throw new ArgumentNullException(nameof(conversions));
        }

        internal string PrefabId { get; }
        internal int SlotCount { get; }
        internal bool RequiresFire { get; }
        internal bool UsesFuel { get; }
        internal string FuelPrefabId { get; }
        internal int MaximumFuel { get; }
        internal int SecondsPerFuel { get; }
        internal string OvercookedPrefabId { get; }
        internal IReadOnlyList<TimedCookingConversionDescriptor> Conversions => _conversions;

        internal bool TryFromInput(
            string inputPrefabId,
            out TimedCookingConversionDescriptor conversion)
        {
            conversion = _conversions.FirstOrDefault(value => string.Equals(
                value.InputPrefabId, inputPrefabId, StringComparison.Ordinal));
            return conversion != null;
        }

        internal bool TryFromUniqueOutput(
            string outputPrefabId,
            out TimedCookingConversionDescriptor conversion)
        {
            conversion = null;
            int matches = 0;
            foreach (TimedCookingConversionDescriptor candidate in _conversions)
            {
                if (!string.Equals(
                        candidate.OutputPrefabId, outputPrefabId, StringComparison.Ordinal)) continue;
                conversion = candidate;
                matches++;
                if (matches > 1)
                {
                    conversion = null;
                    return false;
                }
            }
            return matches == 1;
        }

        internal int OutputMatchCount(string outputPrefabId) => _conversions.Count(value =>
            string.Equals(value.OutputPrefabId, outputPrefabId, StringComparison.Ordinal));
    }

    internal static class TimedCookingStockModel
    {
        internal static bool TryDescribe(
            CookingStation station,
            string prefabId,
            out TimedCookingStationDescriptor descriptor,
            out string failure)
        {
            descriptor = null;
            failure = string.Empty;
            if (station == null || !StockDomainValidation.IsExactPrefabId(prefabId) ||
                station.m_slots == null || station.m_conversion == null ||
                station.m_overCookedItem == null)
                return Fail("Timed cooking producer state is unavailable.", out failure);
            var conversions = new List<TimedCookingConversionDescriptor>(station.m_conversion.Count);
            for (int index = 0; index < station.m_conversion.Count; index++)
            {
                CookingStation.ItemConversion conversion = station.m_conversion[index];
                string input = conversion?.m_from == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_from.gameObject);
                string output = conversion?.m_to == null
                    ? string.Empty
                    : ValheimAccess.PrefabName(conversion.m_to.gameObject);
                if (!StockDomainValidation.IsExactPrefabId(input) ||
                    !StockDomainValidation.IsExactPrefabId(output) ||
                    !(conversion.m_cookTime > 0f) || float.IsNaN(conversion.m_cookTime) ||
                    float.IsInfinity(conversion.m_cookTime))
                    return Fail("Timed cooking conversion state is invalid.", out failure);
                conversions.Add(new TimedCookingConversionDescriptor(
                    index, input, output, conversion.m_cookTime));
            }
            string fuel = station.m_useFuel && station.m_fuelItem != null
                ? ValheimAccess.PrefabName(station.m_fuelItem.gameObject)
                : string.Empty;
            string burnt = ValheimAccess.PrefabName(station.m_overCookedItem.gameObject);
            if (station.m_useFuel && !StockDomainValidation.IsExactPrefabId(fuel) ||
                !StockDomainValidation.IsExactPrefabId(burnt))
                return Fail("Timed cooking fuel/overcooked identity is invalid.", out failure);
            descriptor = new TimedCookingStationDescriptor(
                prefabId,
                station.m_slots.Length,
                station.m_requireFire,
                station.m_useFuel,
                fuel,
                station.m_maxFuel,
                station.m_secPerFuel,
                burnt,
                conversions.AsReadOnly());
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }

    internal static class TimedCookingProducerSignature
    {
        private const int SchemaVersion = 1;

        internal static string ProducerId(
            TimedCookingStationDescriptor station,
            TimedCookingConversionDescriptor conversion)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (conversion == null) throw new ArgumentNullException(nameof(conversion));
            return "timed:" + station.PrefabId + ":" + conversion.Index;
        }

        internal static byte[] Create(
            TimedCookingStationDescriptor station,
            TimedCookingConversionDescriptor conversion)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            if (conversion == null) throw new ArgumentNullException(nameof(conversion));
            return StockProducerSignature.Create(writer =>
            {
                writer.WriteString("runic.production.timed-cooking");
                writer.WriteInt32(SchemaVersion);
                writer.WriteString(station.PrefabId);
                writer.WriteInt32(station.SlotCount);
                writer.WriteBoolean(station.RequiresFire);
                writer.WriteBoolean(station.UsesFuel);
                writer.WriteString(station.FuelPrefabId);
                writer.WriteInt32(station.MaximumFuel);
                writer.WriteInt32(station.SecondsPerFuel);
                writer.WriteString(station.OvercookedPrefabId);
                writer.WriteInt32(station.Conversions.Count);
                writer.WriteInt32(conversion.Index);
                writer.WriteString(conversion.InputPrefabId);
                writer.WriteString(conversion.OutputPrefabId);
                writer.WriteSingle(conversion.CookTime);
            });
        }

        internal static string BurntProducerId(TimedCookingStationDescriptor station)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            return "timed-burnt:" + station.PrefabId;
        }

        internal static byte[] CreateBurnt(TimedCookingStationDescriptor station)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            return StockProducerSignature.Create(writer =>
            {
                writer.WriteString("runic.production.timed-cooking-burnt");
                writer.WriteInt32(SchemaVersion);
                writer.WriteString(station.PrefabId);
                writer.WriteString(station.OvercookedPrefabId);
                writer.WriteInt32(station.SlotCount);
                writer.WriteBoolean(station.RequiresFire);
                writer.WriteBoolean(station.UsesFuel);
            });
        }

        internal static string CompletionProducerId(
            TimedCookingStationDescriptor station,
            string outputPrefabId)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            return "timed-completion:" + station.PrefabId;
        }

        internal static byte[] CreateCompletion(
            TimedCookingStationDescriptor station,
            string outputPrefabId)
        {
            if (station == null) throw new ArgumentNullException(nameof(station));
            return StockProducerSignature.Create(writer =>
            {
                writer.WriteString("runic.production.timed-cooking-completion");
                writer.WriteInt32(SchemaVersion);
                writer.WriteString(station.PrefabId);
                writer.WriteString(outputPrefabId);
                writer.WriteInt32(station.OutputMatchCount(outputPrefabId));
                foreach (TimedCookingConversionDescriptor conversion in station.Conversions)
                {
                    if (!string.Equals(
                            conversion.OutputPrefabId, outputPrefabId,
                            StringComparison.Ordinal)) continue;
                    writer.WriteInt32(conversion.Index);
                    writer.WriteString(conversion.InputPrefabId);
                    writer.WriteSingle(conversion.CookTime);
                }
            });
        }
    }

    internal static class TimedCookingPlanBuilder
    {
        internal static bool TryBuild(
            TimedCookingStationDescriptor descriptor,
            Inventory replenishmentInventory,
            StoredProductionLink link,
            string stationId,
            long actorId,
            string actorName,
            ReplenishmentPlan previous,
            out ReplenishmentPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (descriptor == null || replenishmentInventory == null || link == null ||
                string.IsNullOrEmpty(stationId) || actorId == 0L || string.IsNullOrWhiteSpace(actorName))
                return Fail("Timed cooking plan inputs are unavailable.", out failure);
            bool refresh = previous != null &&
                           previous.AdapterKind == ReplenishmentProducerKind.TimedCooking &&
                           string.Equals(
                               previous.StationPrefabId,
                               descriptor.PrefabId,
                               StringComparison.Ordinal) &&
                           ReplenishmentPlanStore.MatchesLink(previous, link, stationId);
            if (previous != null && !refresh)
                return Fail(
                    "The prior timed cooking plan is not bound to this exact station and link.",
                    out failure);
            long authorizedPlayerId = refresh ? previous.AuthorizedPlayerId : actorId;
            string authorizedPlayerName = refresh
                ? previous.AuthorizedPlayerName
                : actorName;
            if (authorizedPlayerId == 0L || authorizedPlayerId != link.OwnerId ||
                string.IsNullOrWhiteSpace(authorizedPlayerName))
                return Fail(
                    "The timed cooking plan principal does not match the exact link owner.",
                    out failure);
            var exemplars = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in replenishmentInventory.GetAllItems())
            {
                if (item == null || item.m_stack <= 0) continue;
                string output = ValheimAccess.PrefabName(item);
                int matches = descriptor.OutputMatchCount(output);
                if (matches > 1)
                    return Fail(
                        "An exemplar is produced by more than one timed conversion; that target is ambiguous.",
                        out failure);
                if (matches != 1) continue;
                exemplars.Add(output);
                if (exemplars.Count > ReplenishmentPlan.MaximumTargets)
                    return Fail("The Replenishment chest contains too many timed cooking exemplars.", out failure);
            }
            var targets = new List<ReplenishmentTargetAuthorization>(exemplars.Count);
            foreach (string output in exemplars)
            {
                if (!descriptor.TryFromUniqueOutput(output, out TimedCookingConversionDescriptor conversion))
                    return Fail("A timed cooking exemplar became ambiguous.", out failure);
                targets.Add(new ReplenishmentTargetAuthorization(
                    output,
                    ReplenishmentProducerKind.TimedCooking,
                    TimedCookingProducerSignature.ProducerId(descriptor, conversion),
                    TimedCookingProducerSignature.Create(descriptor, conversion),
                    1,
                    string.Empty,
                    0,
                    authorizedPlayerId,
                    authorizedPlayerName,
                    new[] { new ReplenishmentRequirement(conversion.InputPrefabId, 1) }));
            }
            if (refresh && previous.Revision == int.MaxValue)
                return Fail("The Replenishment plan revision is exhausted; relink the destination.", out failure);
            int cursor = 0;
            if (refresh && previous.Targets.Count > 0 && targets.Count > 0)
            {
                string current = previous.Targets[previous.Cursor].OutputPrefabId;
                int preserved = targets.FindIndex(value => string.Equals(
                    value.OutputPrefabId,
                    current,
                    StringComparison.Ordinal));
                if (preserved >= 0) cursor = preserved;
            }
            plan = new ReplenishmentPlan(
                refresh ? previous.PlanId : Guid.NewGuid().ToString("N"),
                NextRevision(previous, refresh),
                cursor,
                descriptor.PrefabId,
                ReplenishmentProducerKind.TimedCooking,
                ReplenishmentPlanStore.Bind(link, stationId),
                authorizedPlayerId,
                authorizedPlayerName,
                targets);
            return true;
        }

        internal static int NextRevision(ReplenishmentPlan previous, bool exactRefresh)
        {
            if (!exactRefresh) return 1;
            if (previous == null || previous.Revision == int.MaxValue)
                throw new InvalidOperationException("A valid non-exhausted prior plan is required.");
            return previous.Revision + 1;
        }

        internal static bool TryValidate(
            ReplenishmentPlan plan,
            TimedCookingStationDescriptor descriptor,
            StoredProductionLink link,
            string stationId,
            out string failure)
        {
            failure = string.Empty;
            if (plan == null || descriptor == null || link == null ||
                plan.AdapterKind != ReplenishmentProducerKind.TimedCooking ||
                !string.Equals(plan.StationPrefabId, descriptor.PrefabId, StringComparison.Ordinal) ||
                !ReplenishmentPlanStore.MatchesLink(plan, link, stationId) ||
                plan.AuthorizedPlayerId != link.OwnerId)
                return Fail("The persisted timed cooking plan is not bound to this exact station/link.", out failure);
            foreach (ReplenishmentTargetAuthorization target in plan.Targets)
                if (!TryResolveAuthorized(descriptor, target, out _, out failure)) return false;
            return true;
        }

        internal static bool TryResolveAuthorized(
            TimedCookingStationDescriptor descriptor,
            ReplenishmentTargetAuthorization target,
            out TimedCookingConversionDescriptor conversion,
            out string failure)
        {
            conversion = null;
            failure = string.Empty;
            if (descriptor == null || target == null ||
                target.ProducerKind != ReplenishmentProducerKind.TimedCooking ||
                target.OutputAmount != 1 || target.Requirements.Count != 1 ||
                target.Requirements[0].Amount != 1 ||
                !descriptor.TryFromUniqueOutput(
                    target.OutputPrefabId, out TimedCookingConversionDescriptor resolved) ||
                !string.Equals(
                    target.Requirements[0].PrefabId, resolved.InputPrefabId, StringComparison.Ordinal) ||
                !string.Equals(
                    target.ProducerId,
                    TimedCookingProducerSignature.ProducerId(descriptor, resolved),
                    StringComparison.Ordinal) ||
                !target.ProducerSignatureMatches(
                    TimedCookingProducerSignature.Create(descriptor, resolved)))
                return Fail(
                    "A signed timed cooking producer changed; explicitly refresh targets to authorize it again.",
                    out failure);
            conversion = resolved;
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
