using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RunicProduction.Contracts;

namespace RunicProduction
{
    internal static class ProductionDiagnostics
    {
        private const int MaximumRememberedStations = 2048;
        private static readonly Dictionary<string, ProductionStopCode> LastStopByStation =
            new Dictionary<string, ProductionStopCode>(StringComparer.Ordinal);
        private static ManualLogSource _log;

        internal static bool RuntimeAvailable { get; private set; }
        internal static string DisabledReason { get; private set; }

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
            RuntimeAvailable = false;
            DisabledReason = "startup has not completed";
            LastStopByStation.Clear();
        }

        internal static void MarkAvailable()
        {
            RuntimeAvailable = true;
            DisabledReason = string.Empty;
        }

        internal static void Disable(string reason, Exception exception = null)
        {
            RuntimeAvailable = false;
            DisabledReason = string.IsNullOrWhiteSpace(reason) ? "unknown adapter failure" : reason;
            _log?.LogError(
                "Runic Production disabled its automation; vanilla production remains active. " +
                DisabledReason + (exception == null ? string.Empty :
                    " " + exception.GetType().Name + ": " + exception.Message));
        }

        internal static void StopChanged(string stationId, ProductionStopCode stopCode, string detail)
        {
            if (string.IsNullOrEmpty(stationId)) return;
            if (LastStopByStation.TryGetValue(stationId, out ProductionStopCode previous) && previous == stopCode)
                return;
            if (LastStopByStation.Count >= MaximumRememberedStations) LastStopByStation.Clear();
            LastStopByStation[stationId] = stopCode;
            if (ProductionConfig.VerboseLogging?.Value ?? false)
                _log?.LogInfo($"production.stop station={stationId} code={stopCode} detail={detail}");
        }

        internal static bool TryGetStop(
            string stationId,
            out ProductionStopCode stopCode)
        {
            stopCode = default;
            return !string.IsNullOrEmpty(stationId) &&
                   LastStopByStation.TryGetValue(stationId, out stopCode);
        }

        internal static void Configuration(string source)
        {
            if (ProductionConfig.Enabled == null) return;
            _log?.LogInfo(
                $"production.config source={source ?? "unknown"} enabled={ProductionConfig.Enabled.Value} " +
                $"range={ProductionConfig.MaximumLinkRange.Value:0.##}m " +
                $"movedTolerance={ProductionConfig.MovedTargetTolerance.Value:0.##}m " +
                $"fuelReserve={ProductionConfig.FuelReserve.Value} pullBatch={ProductionConfig.PullBatchSize.Value} " +
                $"cookingAllow='{SafeConfigText(ProductionConfig.CookingStationPrefabAllowList.Value)}' " +
                $"cookingDeny='{SafeConfigText(ProductionConfig.CookingStationPrefabDenyList.Value)}' " +
                $"recipeAllow='{SafeConfigText(ProductionConfig.RecipeStationPrefabAllowList.Value)}' " +
                $"recipeDeny='{SafeConfigText(ProductionConfig.RecipeStationPrefabDenyList.Value)}' " +
                $"recipeNearbyIngredients={ProductionConfig.RecipeNearbyIngredientsEnabled.Value} " +
                $"recipeNearbyMaxSources={ProductionConfig.RecipeNearbyMaximumSourceChests.Value} " +
                $"fermenterAllow='{SafeConfigText(ProductionConfig.FermenterPrefabAllowList.Value)}' " +
                $"fermenterDeny='{SafeConfigText(ProductionConfig.FermenterPrefabDenyList.Value)}' " +
                $"defaultIngredientReserve={ProductionConfig.DefaultIngredientReserve.Value} " +
                $"ingredientReserves='{SafeConfigText(ProductionConfig.IngredientReserveRules.Value)}' " +
                $"maximumChestsPerRole={ProductionConfig.MaximumLinksPerRole.Value} " +
                $"stockInterval={ProductionConfig.StockSchedulerIntervalSeconds.Value:0.##}s " +
                $"stockMaxOps={ProductionConfig.StockSchedulerMaximumOperations.Value} " +
                $"statusOverlay={ProductionConfig.ShowStatusOverlay.Value} " +
                $"controlHints={ProductionConfig.ShowControlHints.Value} verbose={ProductionConfig.VerboseLogging.Value}");
            if (ProductionConfig.Enabled.Value && ProductionConfig.FuelReserve.Value >= 100)
                _log?.LogWarning(
                    $"Runic Production fuel reserve is {ProductionConfig.FuelReserve.Value}; " +
                    "a linked container at or below that amount will intentionally supply no fuel.");
        }

        internal static void Trace(string message)
        {
            if (ProductionConfig.VerboseLogging?.Value ?? false)
                _log?.LogInfo("production.input " + (message ?? "unknown"));
        }

        internal static void Info(string message) => _log?.LogInfo(message);
        internal static void Warning(string message) => _log?.LogWarning(message);

        private static string SafeConfigText(string value)
        {
            string safe = new string((value ?? string.Empty)
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')
                .Take(256).ToArray());
            return safe;
        }
    }
}
