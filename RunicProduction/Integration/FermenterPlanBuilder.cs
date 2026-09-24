using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    /// <summary>Builds immutable Fermenter targets only from exact output exemplars already in the linked chest.</summary>
    internal static class FermenterPlanBuilder
    {
        internal static bool TryBuild(
            FermenterDescriptor descriptor,
            Inventory replenishmentInventory,
            StoredProductionLink link,
            string stationId,
            long actorId,
            string actorName,
            out ReplenishmentPlan plan,
            out string failure) =>
            TryBuild(
                descriptor,
                replenishmentInventory,
                link,
                stationId,
                actorId,
                actorName,
                previous: null,
                out plan,
                out failure);

        internal static bool TryBuild(
            FermenterDescriptor descriptor,
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
                return Fail(global::Runic.Localization.RunicText.Get("text_92e7dc4fb87e"), out failure);
            bool refresh = previous != null &&
                           previous.AdapterKind == ReplenishmentProducerKind.Fermenter &&
                           string.Equals(
                               previous.StationPrefabId,
                               descriptor.PrefabId,
                               StringComparison.Ordinal) &&
                           ReplenishmentPlanStore.MatchesLink(previous, link, stationId);
            if (previous != null && !refresh)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_d7a6bd105da0"),
                    out failure);
            long authorizedPlayerId = refresh ? previous.AuthorizedPlayerId : actorId;
            string authorizedPlayerName = refresh
                ? previous.AuthorizedPlayerName
                : actorName;
            if (authorizedPlayerId == 0L || authorizedPlayerId != link.OwnerId ||
                string.IsNullOrWhiteSpace(authorizedPlayerName))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_f8fdf85c045f"),
                    out failure);
            var exemplars = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in replenishmentInventory.GetAllItems())
            {
                string prefabId = ValheimAccess.PrefabName(item);
                if (item == null || item.m_stack <= 0 || !descriptor.TryFromOutput(prefabId, out _))
                    continue;
                exemplars.Add(prefabId);
                if (exemplars.Count > ReplenishmentPlan.MaximumTargets)
                    return Fail(global::Runic.Localization.RunicText.Get("text_0beed712358c"), out failure);
            }
            if (refresh && previous.Revision == int.MaxValue)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_b7ed2c0aa56f"),
                    out failure);
            var targets = new List<ReplenishmentTargetAuthorization>(exemplars.Count);
            foreach (string output in exemplars)
            {
                if (!descriptor.TryFromOutput(output, out FermenterConversionDescriptor conversion))
                    return Fail(global::Runic.Localization.RunicText.Get("text_4250cac66027"), out failure);
                targets.Add(new ReplenishmentTargetAuthorization(
                    conversion.OutputPrefabId,
                    ReplenishmentProducerKind.Fermenter,
                    FermenterProducerSignature.ProducerId(descriptor, conversion),
                    FermenterProducerSignature.Create(descriptor, conversion),
                    conversion.OutputAmount,
                    string.Empty,
                    0,
                    authorizedPlayerId,
                    authorizedPlayerName,
                    new[] { new ReplenishmentRequirement(conversion.InputPrefabId, 1) }));
            }
            string priorTarget = refresh && previous.Targets.Count > 0
                ? previous.Targets[previous.Cursor].OutputPrefabId
                : string.Empty;
            int cursor = 0;
            if (priorTarget.Length != 0)
            {
                int preserved = targets.FindIndex(value =>
                    string.Equals(value.OutputPrefabId, priorTarget, StringComparison.Ordinal));
                if (preserved >= 0) cursor = preserved;
            }
            plan = new ReplenishmentPlan(
                refresh ? previous.PlanId : Guid.NewGuid().ToString("N"),
                revision: refresh ? previous.Revision + 1 : 1,
                cursor,
                descriptor.PrefabId,
                ReplenishmentProducerKind.Fermenter,
                ReplenishmentPlanStore.Bind(link, stationId),
                authorizedPlayerId,
                authorizedPlayerName,
                targets);
            return true;
        }

        internal static bool TryValidate(
            ReplenishmentPlan plan,
            FermenterDescriptor descriptor,
            StoredProductionLink link,
            string stationId,
            Inventory replenishmentInventory,
            out string failure)
        {
            failure = string.Empty;
            if (plan == null || descriptor == null || link == null || replenishmentInventory == null ||
                plan.AdapterKind != ReplenishmentProducerKind.Fermenter ||
                !string.Equals(plan.StationPrefabId, descriptor.PrefabId, StringComparison.Ordinal) ||
                !ReplenishmentPlanStore.MatchesLink(plan, link, stationId) ||
                plan.AuthorizedPlayerId != link.OwnerId)
                return Fail(global::Runic.Localization.RunicText.Get("text_0fa5264c5885"), out failure);
            foreach (ReplenishmentTargetAuthorization target in plan.Targets)
            {
                if (target.ProducerKind != ReplenishmentProducerKind.Fermenter ||
                    target.AuthorizedPlayerId != link.OwnerId ||
                    target.Requirements.Count != 1 || target.Requirements[0].Amount != 1 ||
                    !descriptor.TryFromOutput(target.OutputPrefabId, out FermenterConversionDescriptor conversion) ||
                    !string.Equals(target.Requirements[0].PrefabId, conversion.InputPrefabId, StringComparison.Ordinal) ||
                    target.OutputAmount != conversion.OutputAmount ||
                    !string.Equals(target.ProducerId,
                        FermenterProducerSignature.ProducerId(descriptor, conversion),
                        StringComparison.Ordinal) ||
                    !target.ProducerSignatureMatches(FermenterProducerSignature.Create(descriptor, conversion)))
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_6c725a19f071"),
                        out failure);
            }
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
