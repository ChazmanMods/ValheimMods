using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;

namespace RunicProduction.Integration
{
    internal static class RecipePlanBuilder
    {
        internal static bool TryBuild(
            CraftingStation station,
            string stationId,
            string stationPrefabId,
            StoredProductionLink link,
            Inventory destinationInventory,
            Player actor,
            ReplenishmentPlan previous,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ReplenishmentPlan plan,
            out string failure)
        {
            if (actor == null)
            {
                plan = null;
                return Fail(global::Runic.Localization.RunicText.Get("text_85fc7909b6e8"), out failure);
            }
            return TryBuild(
                station,
                stationId,
                stationPrefabId,
                link,
                destinationInventory,
                actor.GetPlayerID(),
                actor.GetPlayerName(),
                previous,
                cookingInputStations,
                fermenterInputStations,
                out plan,
                out failure);
        }

        internal static bool TryBuild(
            CraftingStation station,
            string stationId,
            string stationPrefabId,
            StoredProductionLink link,
            Inventory destinationInventory,
            long actorId,
            string actorName,
            ReplenishmentPlan previous,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ReplenishmentPlan plan,
            out string failure)
        {
            plan = null;
            failure = string.Empty;
            if (station == null || string.IsNullOrEmpty(stationId) ||
                string.IsNullOrEmpty(stationPrefabId) || link == null ||
                destinationInventory == null || actorId == 0L ||
                string.IsNullOrWhiteSpace(actorName))
                return Fail(global::Runic.Localization.RunicText.Get("text_85fc7909b6e8"), out failure);

            bool refresh = previous != null &&
                           previous.AdapterKind == ReplenishmentProducerKind.DirectRecipe &&
                           string.Equals(
                               previous.StationPrefabId,
                               stationPrefabId,
                               StringComparison.Ordinal) &&
                           ReplenishmentPlanStore.MatchesLink(
                               previous, link, stationId);
            if (previous != null && !refresh)
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_304e999ba5fb"),
                    out failure);
            long authorizedPlayerId = refresh
                ? previous.AuthorizedPlayerId
                : actorId;
            string authorizedPlayerName = refresh
                ? previous.AuthorizedPlayerName
                : actorName;
            if (authorizedPlayerId == 0L ||
                authorizedPlayerId != link.OwnerId ||
                string.IsNullOrWhiteSpace(authorizedPlayerName))
                return Fail(
                    global::Runic.Localization.RunicText.Get("text_f90aba4a0270"),
                    out failure);
            var targets = new Dictionary<string, ReplenishmentTargetAuthorization>(
                StringComparer.Ordinal);

            var exemplars = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in destinationInventory.GetAllItems())
            {
                if (item == null || item.m_stack <= 0) continue;
                string prefab = ValheimAccess.PrefabName(item);
                if (!StockDomainValidation.IsExactPrefabId(prefab)) continue;
                exemplars.Add(prefab);
                if (exemplars.Count > ReplenishmentPlan.MaximumTargets)
                    return Fail(
                        global::Runic.Localization.RunicText.Get("text_1e85a617bf58"),
                        out failure);
            }

            foreach (string exemplar in exemplars)
            {
                if (RecipeProducerResolver.TryAuthorize(
                        station,
                        stationPrefabId,
                        exemplar,
                        authorizedPlayerId,
                        authorizedPlayerName,
                        cookingInputStations,
                        fermenterInputStations,
                        out ReplenishmentTargetAuthorization target,
                        out _))
                    targets[exemplar] = target;
            }
            if (targets.Count > ReplenishmentPlan.MaximumTargets)
                return Fail(global::Runic.Localization.RunicText.Get("text_7b7c510bb45f"), out failure);

            List<ReplenishmentTargetAuthorization> ordered = targets.Values
                .OrderBy(value => value.OutputPrefabId, StringComparer.Ordinal)
                .ToList();
            int cursor = 0;
            if (refresh && previous.Targets.Count > 0)
            {
                string current = previous.Targets[previous.Cursor].OutputPrefabId;
                int preserved = ordered.FindIndex(value => string.Equals(
                    value.OutputPrefabId, current, StringComparison.Ordinal));
                if (preserved >= 0) cursor = preserved;
            }
            if (refresh && previous.Revision == int.MaxValue)
                return Fail(global::Runic.Localization.RunicText.Get("text_afe693828ce7"), out failure);
            plan = new ReplenishmentPlan(
                refresh ? previous.PlanId : Guid.NewGuid().ToString("N"),
                refresh ? previous.Revision + 1 : 1,
                cursor,
                stationPrefabId,
                ReplenishmentProducerKind.DirectRecipe,
                ReplenishmentPlanStore.Bind(link, stationId),
                authorizedPlayerId,
                authorizedPlayerName,
                ordered);
            return true;
        }

        private static bool Fail(string value, out string failure)
        {
            failure = value;
            return false;
        }
    }
}
