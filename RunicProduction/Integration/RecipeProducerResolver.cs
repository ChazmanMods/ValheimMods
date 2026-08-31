using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;
using UnityEngine;

namespace RunicProduction.Integration
{
    internal enum RecipeCandidateDisposition
    {
        Eligible = 0,
        Missing = 1,
        Ambiguous = 2,
        RequireOnlyOneUnsupported = 3
    }

    internal static class RecipeCandidatePolicy
    {
        internal static RecipeCandidateDisposition Classify(
            int exactCandidateCount,
            bool uniqueCandidateRequiresOnlyOne)
        {
            if (exactCandidateCount < 0) throw new ArgumentOutOfRangeException(nameof(exactCandidateCount));
            if (exactCandidateCount == 0) return RecipeCandidateDisposition.Missing;
            if (exactCandidateCount > 1) return RecipeCandidateDisposition.Ambiguous;
            return uniqueCandidateRequiresOnlyOne
                ? RecipeCandidateDisposition.RequireOnlyOneUnsupported
                : RecipeCandidateDisposition.Eligible;
        }
    }

    internal sealed class ResolvedRecipeProducer
    {
        internal ResolvedRecipeProducer(
            Recipe recipe,
            string outputPrefab,
            byte[] signature,
            IReadOnlyList<ReplenishmentRequirement> requirements)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            OutputPrefab = outputPrefab ?? throw new ArgumentNullException(nameof(outputPrefab));
            Signature = signature == null ? throw new ArgumentNullException(nameof(signature)) :
                (byte[])signature.Clone();
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        }

        internal Recipe Recipe { get; }
        internal string OutputPrefab { get; }
        internal byte[] Signature { get; }
        internal IReadOnlyList<ReplenishmentRequirement> Requirements { get; }
    }

    internal static class RecipeProducerResolver
    {
        internal static bool TryAuthorize(
            CraftingStation station,
            string stationPrefabId,
            string outputPrefabId,
            Player actor,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ReplenishmentTargetAuthorization authorization,
            out string failure)
        {
            if (actor == null)
            {
                authorization = null;
                failure = "An online player is required to authorize replenishment targets.";
                return false;
            }
            return TryAuthorize(
                station,
                stationPrefabId,
                outputPrefabId,
                actor.GetPlayerID(),
                actor.GetPlayerName(),
                cookingInputStations,
                fermenterInputStations,
                out authorization,
                out failure);
        }

        internal static bool TryAuthorize(
            CraftingStation station,
            string stationPrefabId,
            string outputPrefabId,
            long authorizedPlayerId,
            string authorizedPlayerName,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ReplenishmentTargetAuthorization authorization,
            out string failure)
        {
            authorization = null;
            if (authorizedPlayerId == 0L || string.IsNullOrWhiteSpace(authorizedPlayerName))
            {
                failure = "An exact player principal is required to authorize replenishment targets.";
                return false;
            }
            if (!TryResolve(
                    station,
                    stationPrefabId,
                    outputPrefabId,
                    cookingInputStations,
                    fermenterInputStations,
                    out ResolvedRecipeProducer producer,
                    out failure)) return false;
            Recipe recipe = producer.Recipe;
            // The physical exemplar is the explicit stock target. Enabled recipe, DLC, producer
            // identity, station level, fire/roof, and signature are still revalidated locally.
            if (!RecipeStationCompatibility.IsPhysicallyUsable(
                    station, recipe.GetRequiredStationLevel(1), out failure)) return false;
            authorization = new ReplenishmentTargetAuthorization(
                producer.OutputPrefab,
                ReplenishmentProducerKind.DirectRecipe,
                recipe.name,
                producer.Signature,
                recipe.m_amount,
                recipe.m_craftingStation.m_name,
                recipe.GetRequiredStationLevel(1),
                authorizedPlayerId,
                authorizedPlayerName,
                producer.Requirements);
            return true;
        }

        internal static bool TryResolveAuthorized(
            CraftingStation station,
            string stationPrefabId,
            ReplenishmentTargetAuthorization authorization,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ResolvedRecipeProducer producer,
            out string failure)
        {
            producer = null;
            if (authorization == null ||
                authorization.ProducerKind != ReplenishmentProducerKind.DirectRecipe)
            {
                failure = "The target is not a direct recipe authorization.";
                return false;
            }
            if (!TryResolve(
                    station,
                    stationPrefabId,
                    authorization.OutputPrefabId,
                    cookingInputStations,
                    fermenterInputStations,
                    out producer,
                    out failure)) return false;
            if (!string.Equals(
                    authorization.ProducerId, producer.Recipe.name, StringComparison.Ordinal) ||
                !authorization.ProducerSignatureMatches(producer.Signature) ||
                authorization.OutputAmount != producer.Recipe.m_amount ||
                !string.Equals(
                    authorization.RequiredStationName,
                    producer.Recipe.m_craftingStation.m_name,
                    StringComparison.Ordinal) ||
                authorization.RequiredStationLevel !=
                    producer.Recipe.GetRequiredStationLevel(1) ||
                !RequirementsMatch(authorization.Requirements, producer.Requirements))
            {
                producer = null;
                failure = "The signed recipe definition changed; refresh targets to authorize it again.";
                return false;
            }
            if (!RecipeStationCompatibility.IsPhysicallyUsable(
                    station, authorization.RequiredStationLevel, out failure))
            {
                producer = null;
                return false;
            }
            return true;
        }

        internal static byte[] Signature(
            Recipe recipe,
            string stationPrefabId,
            string outputPrefabId)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            return StockProducerSignature.Create(writer =>
            {
                CraftingStation registeredStation = ZNetScene.instance?
                    .GetPrefab(stationPrefabId)?.GetComponent<CraftingStation>();
                writer.WriteString("runic.production.recipe.v1");
                writer.WriteString(stationPrefabId);
                writer.WriteString(registeredStation?.m_name ?? string.Empty);
                writer.WriteBoolean(registeredStation?.m_craftRequireRoof ?? false);
                writer.WriteBoolean(registeredStation?.m_craftRequireFire ?? false);
                writer.WriteString(recipe.m_craftingStation?.m_name ?? string.Empty);
                writer.WriteString(recipe.name ?? string.Empty);
                writer.WriteBoolean(recipe.m_enabled);
                writer.WriteString(outputPrefabId);
                writer.WriteInt32(recipe.m_amount);
                writer.WriteString(recipe.m_item?.m_itemData?.m_shared?.m_dlc ?? string.Empty);
                writer.WriteInt32(recipe.m_minStationLevel);
                writer.WriteBoolean(recipe.m_requireOnlyOneIngredient);
                writer.WriteSingle(recipe.m_qualityResultAmountMultiplier);
                ItemDrop.ItemData.SharedData shared = recipe.m_item?.m_itemData?.m_shared;
                writer.WriteInt32(shared == null ? 0 : (int)shared.m_itemType);
                writer.WriteInt32(shared?.m_maxStackSize ?? 0);
                writer.WriteInt32(shared?.m_maxQuality ?? 0);
                Piece.Requirement[] resources = recipe.m_resources ?? Array.Empty<Piece.Requirement>();
                writer.WriteInt32(resources.Length);
                foreach (Piece.Requirement requirement in resources)
                {
                    writer.WriteString(requirement?.m_resItem == null
                        ? string.Empty
                        : ValheimAccess.PrefabName(requirement.m_resItem.gameObject));
                    writer.WriteInt32(requirement?.m_amount ?? 0);
                    writer.WriteInt32(requirement?.m_extraAmountOnlyOneIngredient ?? 0);
                    writer.WriteInt32(requirement?.m_amountPerLevel ?? 0);
                }
            });
        }

        private static bool TryResolve(
            CraftingStation station,
            string stationPrefabId,
            string outputPrefabId,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations,
            out ResolvedRecipeProducer producer,
            out string failure)
        {
            producer = null;
            failure = string.Empty;
            if (station == null || !StockDomainValidation.IsExactPrefabId(stationPrefabId) ||
                !StockDomainValidation.IsExactPrefabId(outputPrefabId) || ObjectDB.instance == null)
            {
                failure = "Recipe producer state is unavailable.";
                return false;
            }
            List<Recipe> candidates = ObjectDB.instance.m_recipes
                .Where(recipe => IsCandidate(recipe, station, outputPrefabId))
                .ToList();
            RecipeCandidateDisposition disposition = RecipeCandidatePolicy.Classify(
                candidates.Count,
                candidates.Count == 1 && candidates[0].m_requireOnlyOneIngredient);
            if (disposition == RecipeCandidateDisposition.Missing)
            {
                failure = "No enabled exact recipe at this station produces the exemplar.";
                return false;
            }
            if (disposition == RecipeCandidateDisposition.Ambiguous)
            {
                failure = "More than one exact recipe at this station produces the exemplar.";
                return false;
            }
            Recipe recipe = candidates[0];
            if (disposition == RecipeCandidateDisposition.RequireOnlyOneUnsupported)
            {
                failure = "Recipes that accept one of several ingredients are unsupported in v0.3.";
                return false;
            }
            if (recipe.m_amount <= 0 || recipe.m_amount > StockDomainValidation.MaximumItemAmount)
            {
                failure = "The recipe output batch is outside the supported bound.";
                return false;
            }
            if (!DlcInstalled(recipe.m_item.m_itemData.m_shared.m_dlc))
            {
                failure = "The recipe output requires unavailable DLC.";
                return false;
            }
            if (!IsSupportedOutput(
                    recipe.m_item.gameObject,
                    cookingInputStations,
                    fermenterInputStations))
            {
                failure = "The recipe output is neither consumable nor an input to an enabled allowed cooking producer.";
                return false;
            }
            if (!TryRequirements(recipe, out List<ReplenishmentRequirement> requirements, out failure))
                return false;
            producer = new ResolvedRecipeProducer(
                recipe,
                outputPrefabId,
                Signature(recipe, stationPrefabId, outputPrefabId),
                requirements.AsReadOnly());
            return true;
        }

        private static bool IsCandidate(
            Recipe recipe,
            CraftingStation station,
            string outputPrefabId) =>
            recipe != null && recipe.m_enabled && recipe.m_item != null &&
            recipe.m_craftingStation != null &&
            string.Equals(recipe.m_craftingStation.m_name, station.m_name, StringComparison.Ordinal) &&
            string.Equals(
                ValheimAccess.PrefabName(recipe.m_item.gameObject),
                outputPrefabId,
                StringComparison.Ordinal);

        private static bool TryRequirements(
            Recipe recipe,
            out List<ReplenishmentRequirement> requirements,
            out string failure)
        {
            requirements = new List<ReplenishmentRequirement>();
            failure = string.Empty;
            Piece.Requirement[] resources = recipe.m_resources ?? Array.Empty<Piece.Requirement>();
            if (resources.Length > ReplenishmentTargetAuthorization.MaximumRequirements)
            {
                failure = "The recipe has too many resource rows.";
                return false;
            }
            foreach (Piece.Requirement resource in resources)
            {
                if (resource?.m_resItem == null || resource.GetAmount(1) <= 0) continue;
                string prefab = ValheimAccess.PrefabName(resource.m_resItem.gameObject);
                if (!StockDomainValidation.IsExactPrefabId(prefab) ||
                    resource.GetAmount(1) > StockDomainValidation.MaximumItemAmount)
                {
                    failure = "The recipe contains an invalid exact resource requirement.";
                    return false;
                }
                requirements.Add(new ReplenishmentRequirement(prefab, resource.GetAmount(1)));
            }
            if (requirements.Count == 0)
            {
                failure = "Zero-cost replenishment recipes are not supported.";
                return false;
            }
            return true;
        }

        private static bool IsSupportedOutput(
            GameObject outputPrefab,
            StockStationPrefabPolicy cookingInputStations,
            StockStationPrefabPolicy fermenterInputStations)
        {
            ItemDrop item = outputPrefab?.GetComponent<ItemDrop>();
            if (item == null) return false;
            if (item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                return true;
            string outputId = ValheimAccess.PrefabName(outputPrefab);
            if (AcceptedByCookingStation(outputId, cookingInputStations)) return true;
            return AcceptedByFermenter(outputId, fermenterInputStations);
        }

        private static bool AcceptedByCookingStation(
            string outputId,
            StockStationPrefabPolicy policy)
        {
            if (policy == null || ZNetScene.instance == null) return false;
            foreach (string stationId in policy.EffectiveAllowedIds)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(stationId);
                CookingStation station = prefab?.GetComponent<CookingStation>();
                if (station == null || !station.enabled ||
                    !string.Equals(ValheimAccess.PrefabName(prefab), stationId, StringComparison.Ordinal)) continue;
                int matches = 0;
                foreach (CookingStation.ItemConversion conversion in
                         station.m_conversion ?? new List<CookingStation.ItemConversion>())
                    if (conversion?.m_from != null &&
                        string.Equals(
                            ValheimAccess.PrefabName(conversion.m_from.gameObject),
                            outputId,
                            StringComparison.Ordinal)) matches++;
                if (matches == 1) return true;
                if (matches > 1) return false;
            }
            return false;
        }

        private static bool AcceptedByFermenter(
            string outputId,
            StockStationPrefabPolicy policy)
        {
            if (policy == null || ZNetScene.instance == null) return false;
            foreach (string stationId in policy.EffectiveAllowedIds)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(stationId);
                Fermenter station = prefab?.GetComponent<Fermenter>();
                if (station == null || !station.enabled ||
                    !string.Equals(ValheimAccess.PrefabName(prefab), stationId, StringComparison.Ordinal)) continue;
                int matches = 0;
                foreach (Fermenter.ItemConversion conversion in
                         station.m_conversion ?? new List<Fermenter.ItemConversion>())
                    if (conversion?.m_from != null &&
                        string.Equals(
                            ValheimAccess.PrefabName(conversion.m_from.gameObject),
                            outputId,
                            StringComparison.Ordinal)) matches++;
                if (matches == 1) return true;
                if (matches > 1) return false;
            }
            return false;
        }

        private static bool DlcInstalled(string dlc) =>
            string.IsNullOrEmpty(dlc) || DLCMan.instance != null && DLCMan.instance.IsDLCInstalled(dlc);

        private static bool RequirementsMatch(
            IReadOnlyList<ReplenishmentRequirement> left,
            IReadOnlyList<ReplenishmentRequirement> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (!string.Equals(left[index].PrefabId, right[index].PrefabId, StringComparison.Ordinal) ||
                    left[index].Amount != right[index].Amount) return false;
            return true;
        }
    }
}
