using System;

namespace RunicCrafting.Domain
{
    public sealed class NearbyCraftingFeatureState
    {
        public NearbyCraftingFeatureState(bool isReady, string reasonCode, string displayLabel)
        {
            IsReady = isReady;
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "unavailable" : reasonCode;
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? "N:OFF" : displayLabel;
        }

        public bool IsReady { get; }
        public string ReasonCode { get; }
        public string DisplayLabel { get; }
    }

    /// <summary>
    /// Produces one deterministic explanation for the first gate that prevents nearby crafting.
    /// It is kept free of Valheim types so the status shown to players is regression-testable.
    /// </summary>
    public static class NearbyCraftingFeatureStateEvaluator
    {
        public static NearbyCraftingFeatureState Evaluate(
            bool masterEnabled,
            bool craftFromContainersEnabled,
            bool runtimeAvailable,
            bool localPlayerOwner,
            bool recipeAvailable,
            bool specialOneIngredientRecipe,
            bool noCostMode,
            bool stationPresent,
            bool stationUseAllowed,
            string stationDenialReason,
            bool localMaterialsAllowed,
            string localMaterialsDenialReason)
        {
            if (!masterEnabled)
                return Blocked("mod-disabled", "N:OFF(mod)");
            if (!craftFromContainersEnabled)
                return Blocked("craft-from-containers-disabled", "N:OFF(config)");
            if (!runtimeAvailable)
                return Blocked("runtime-unavailable", "N:OFF(startup)");
            if (!localPlayerOwner)
                return Blocked("local-player-owner-required", "N:OFF(owner)");
            if (!recipeAvailable)
                return Blocked("recipe-unavailable", "N:OFF(recipe)");
            if (specialOneIngredientRecipe)
                return Blocked("special-one-ingredient-recipe", "N:OFF(recipe)");
            if (noCostMode)
                return Blocked("no-cost-mode", "N:OFF(no-cost)");
            if (!stationPresent)
                return Blocked("crafting-station-required", "N:OFF(station)");
            if (!stationUseAllowed)
                return Blocked(
                    "station-use-denied:" + NormalizeReason(stationDenialReason),
                    "N:OFF(access)");
            if (!localMaterialsAllowed)
                return Blocked(
                    "local-material-use-denied:" + NormalizeReason(localMaterialsDenialReason),
                    "N:OFF(access)");
            return new NearbyCraftingFeatureState(true, "ready", "N:ON");
        }

        private static NearbyCraftingFeatureState Blocked(string reason, string label) =>
            new NearbyCraftingFeatureState(false, reason, label);

        private static string NormalizeReason(string value) =>
            string.IsNullOrWhiteSpace(value) ? "denied" : value.Trim();
    }

    public static class CraftingRequirementDisplay
    {
        public static string Format(
            int required,
            int carried,
            int nearby,
            NearbyCraftingFeatureState state)
        {
            if (required < 0) throw new ArgumentOutOfRangeException(nameof(required));
            if (carried < 0) throw new ArgumentOutOfRangeException(nameof(carried));
            if (nearby < 0) throw new ArgumentOutOfRangeException(nameof(nearby));
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.IsReady)
            {
                int carriedMissing = Math.Max(0, required - carried);
                return required + global::Runic.Localization.RunicText.Get("text_05ea4adbf842") + carried + " " + state.DisplayLabel + global::Runic.Localization.RunicText.Get("text_7f12d05bb8e9") + carriedMissing;
            }

            int total = carried > int.MaxValue - nearby ? int.MaxValue : carried + nearby;
            int missing = Math.Max(0, required - total);
            return required + global::Runic.Localization.RunicText.Get("text_05ea4adbf842") + carried + global::Runic.Localization.RunicText.Get("text_32b92abd3124") + nearby + global::Runic.Localization.RunicText.Get("text_60c3c36a1516") + total + global::Runic.Localization.RunicText.Get("text_7f12d05bb8e9") + missing;
        }

        /// <summary>
        /// The vanilla amount label is a compact, single-line numeric field. Display the amount
        /// currently available over the amount required there; put the full C/N/T/M explanation
        /// in the row tooltip instead of forcing a long multiline string into the small label.
        /// </summary>
        public static string FormatCompactAmount(
            int required,
            int carried,
            int nearby,
            NearbyCraftingFeatureState state)
        {
            Validate(required, carried, nearby, state);
            int available = state.IsReady ? AddSaturated(carried, nearby) : carried;
            return available + "/" + required;
        }

        public static string FormatTooltipBreakdown(
            int required,
            int carried,
            int nearby,
            NearbyCraftingFeatureState state)
        {
            Validate(required, carried, nearby, state);
            if (!state.IsReady)
                return global::Runic.Localization.RunicText.Get("text_7c92f6f8705f") + carried + " | " +
                       state.DisplayLabel + global::Runic.Localization.RunicText.Get("text_5dd09a8f2ef1") + required + global::Runic.Localization.RunicText.Get("text_2b424dbfbb5a") +
                       Math.Max(0, required - carried);

            int total = AddSaturated(carried, nearby);
            return global::Runic.Localization.RunicText.Get("text_7c92f6f8705f") + carried + global::Runic.Localization.RunicText.Get("text_5baa162b70f4") + nearby +
                   global::Runic.Localization.RunicText.Get("text_fb434ab77a6d") + total + global::Runic.Localization.RunicText.Get("text_5dd09a8f2ef1") + required + global::Runic.Localization.RunicText.Get("text_2b424dbfbb5a") +
                   Math.Max(0, required - total);
        }

        public static bool CombinedTotalSatisfies(
            int required,
            int carried,
            int nearby,
            NearbyCraftingFeatureState state)
        {
            Validate(required, carried, nearby, state);
            return state.IsReady && AddSaturated(carried, nearby) >= required;
        }

        private static int AddSaturated(int left, int right) =>
            left > int.MaxValue - right ? int.MaxValue : left + right;

        private static void Validate(
            int required,
            int carried,
            int nearby,
            NearbyCraftingFeatureState state)
        {
            if (required < 0) throw new ArgumentOutOfRangeException(nameof(required));
            if (carried < 0) throw new ArgumentOutOfRangeException(nameof(carried));
            if (nearby < 0) throw new ArgumentOutOfRangeException(nameof(nearby));
            if (state == null) throw new ArgumentNullException(nameof(state));
        }
    }
}
