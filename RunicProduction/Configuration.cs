using BepInEx.Configuration;

namespace RunicProduction
{
    internal static class ProductionConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<float> MaximumLinkRange { get; private set; }
        internal static ConfigEntry<float> MovedTargetTolerance { get; private set; }
        internal static ConfigEntry<int> FuelReserve { get; private set; }
        internal static ConfigEntry<int> PullBatchSize { get; private set; }
        internal static ConfigEntry<string> CookingStationPrefabAllowList { get; private set; }
        internal static ConfigEntry<string> CookingStationPrefabDenyList { get; private set; }
        internal static ConfigEntry<string> RecipeStationPrefabAllowList { get; private set; }
        internal static ConfigEntry<string> RecipeStationPrefabDenyList { get; private set; }
        internal static ConfigEntry<bool> RecipeNearbyIngredientsEnabled { get; private set; }
        internal static ConfigEntry<int> RecipeNearbyMaximumSourceChests { get; private set; }
        internal static ConfigEntry<string> FermenterPrefabAllowList { get; private set; }
        internal static ConfigEntry<string> FermenterPrefabDenyList { get; private set; }
        internal static ConfigEntry<int> DefaultIngredientReserve { get; private set; }
        internal static ConfigEntry<string> IngredientReserveRules { get; private set; }
        internal static ConfigEntry<int> MaximumLinksPerRole { get; private set; }
        internal static ConfigEntry<int> DefaultReplenishmentReserve { get; private set; }
        internal static ConfigEntry<string> ReplenishmentReserveRules { get; private set; }
        internal static ConfigEntry<float> StockSchedulerIntervalSeconds { get; private set; }
        internal static ConfigEntry<int> StockSchedulerMaximumOperations { get; private set; }
        internal static ConfigEntry<bool> ShowStatusOverlay { get; private set; }
        internal static ConfigEntry<bool> ShowControlHints { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable explicit Runic Input, Output, and Replenishment links. " +
                "Disabling immediately stops automation without leaving operation state behind.");
            MaximumLinkRange = config.Bind(
                "Links",
                "MaximumLinkRange",
                12f,
                new ConfigDescription(
                    "Maximum station-to-container distance in meters.",
                    new AcceptableValueRange<float>(2f, 30f)));
            MovedTargetTolerance = config.Bind(
                "Links",
                "MovedTargetTolerance",
                0.75f,
                new ConfigDescription(
                    "Distance a linked target may move from its recorded ZDO-root position before automation pauses. The relation remains available for an explicit refresh.",
                    new AcceptableValueRange<float>(0.1f, 3f)));
            FuelReserve = config.Bind(
                "Fuel",
                "ProtectedReserve",
                10,
                new ConfigDescription(
                    "Minimum fuel items retained in every linked fuel container.",
                    new AcceptableValueRange<int>(0, 1000)));
            PullBatchSize = config.Bind(
                "Transfers",
                "PullBatchSize",
                1,
                new ConfigDescription(
                    "Maximum items pulled per station update. Capacity and authorization are checked per item.",
                    new AcceptableValueRange<int>(1, 10)));
            CookingStationPrefabAllowList = config.Bind(
                "Cooking Stations",
                "AllowedPrefabIds",
                "piece_cookingstation,piece_cookingstation_iron,piece_oven",
                "Comma-separated exact prefab IDs allowed to use CookingStation automation. " +
                "A listed prefab must still pass strict runtime CookingStation compatibility checks.");
            CookingStationPrefabDenyList = config.Bind(
                "Cooking Stations",
                "DeniedPrefabIds",
                string.Empty,
                "Comma-separated exact prefab IDs denied CookingStation automation. Deny entries override the allow list.");
            RecipeStationPrefabAllowList = config.Bind(
                "Recipe Stations",
                "AllowedPrefabIds",
                "piece_cauldron,piece_MeadCauldron,piece_preptable",
                "Comma-separated exact prefab IDs allowed to perform signed replenishment recipes. " +
                "Every station and recipe must still pass strict runtime compatibility and progression checks.");
            RecipeStationPrefabDenyList = config.Bind(
                "Recipe Stations",
                "DeniedPrefabIds",
                string.Empty,
                "Comma-separated exact recipe-station prefab IDs denied replenishment. Deny entries override the allow list.");
            RecipeNearbyIngredientsEnabled = config.Bind(
                "Recipe Nearby Ingredients",
                "Enabled",
                true,
                "Use bounded, station-centered discovery of eligible nearby ingredient chests for replenishment recipes, cooking, and fermenting. " +
                "False preserves exact designated-Input-only behavior. The search radius is Links.MaximumLinkRange. " +
                    "Only loaded, static, accessible chests owned by the same local process qualify; Output, Fuel-compatibility, and Replenishment destinations are excluded unless explicitly linked as Input. " +
                "Discovery alone never authorizes or mutates a chest.");
            RecipeNearbyMaximumSourceChests = config.Bind(
                "Recipe Nearby Ingredients",
                "MaximumSourceChests",
                32,
                new ConfigDescription(
                    "Maximum eligible nearby ingredient source chests accepted by one replenishment operation. " +
                    "The loaded-container spatial query is deterministic; exceeding this limit or the hard 64-candidate bound pauses production rather than using a partial source set.",
                    new AcceptableValueRange<int>(1, 64)));
            FermenterPrefabAllowList = config.Bind(
                "Fermenters",
                "AllowedPrefabIds",
                "fermenter",
                "Comma-separated exact Fermenter prefab IDs allowed to participate in replenishment production.");
            FermenterPrefabDenyList = config.Bind(
                "Fermenters",
                "DeniedPrefabIds",
                string.Empty,
                "Comma-separated exact Fermenter prefab IDs denied replenishment. Deny entries override the allow list.");
            DefaultIngredientReserve = config.Bind(
                "Ingredient Reserves",
                "DefaultProtectedReserve",
                0,
                new ConfigDescription(
                    "Default count retained for every exact ingredient prefab in a linked Input chest and independently in every eligible nearby donor chest.",
                    new AcceptableValueRange<int>(0, 1000000)));
            IngredientReserveRules = config.Bind(
                "Ingredient Reserves",
                "ProtectedPrefabAmounts",
                string.Empty,
                "Semicolon-separated, case-sensitive exact prefab reserves, for example DeerMeat=10;RawMeat=20. " +
                "Nearby production preserves the configured amount separately in every donor; direct recipes may combine one exact batch across several donors. " +
                "Invalid or duplicate rules fail stock input consumption closed.");
            MaximumLinksPerRole = config.Bind(
                "Links",
                "MaximumChestsPerRole",
                8,
                new ConfigDescription(
                    "Single soft limit for Input, Output, and Replenishment chests per station and role. Existing links are retained if this is lowered. The hard safety limit is 16.",
                    new AcceptableValueRange<int>(1, 16)));
            DefaultReplenishmentReserve = config.Bind(
                "Replenishment Stock",
                "DefaultReserve",
                10,
                new ConfigDescription(
                    "Default target count maintained for each exemplar item in a linked Replenishment chest. Production runs only while the exact item count is below this reserve.",
                    new AcceptableValueRange<int>(1, 1000000)));
            ReplenishmentReserveRules = config.Bind(
                "Replenishment Stock",
                "PrefabReserves",
                string.Empty,
                "Semicolon-separated exact prefab reserve overrides, for example QueensJam=20;MeadHealthMinor=10. Invalid or duplicate rules pause replenishment safely.");
            StockSchedulerIntervalSeconds = config.Bind(
                "Stock Scheduler",
                "IntervalSeconds",
                2f,
                new ConfigDescription(
                    "Native-owner fire/lamp and replenishment service quantum. Unloaded time never creates catch-up recipe crafts.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            StockSchedulerMaximumOperations = config.Bind(
                "Stock Scheduler",
                "MaximumOperationsPerQuantum",
                64,
                new ConfigDescription(
                    "Global cap shared by fire/lamp, direct-recipe, timed-cooking, and Fermenter operations in one scheduler quantum. Each loaded station receives at most one operation per quantum.",
                    new AcceptableValueRange<int>(1, 256)));
            ShowStatusOverlay = config.Bind(
                "UI",
                "ShowStatusOverlay",
                true,
                "Append the most recent compact automation result to station hover text.");
            ShowControlHints = config.Bind(
                "UI",
                "ShowControlHints",
                true,
                "Show the two-step Alt+mouse link workflow, Shift+Alt+mouse unlink workflow, current link counts, and empty roles.");
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Log link and transfer transitions. Per-frame and container-scan logging is never emitted.");
        }
    }
}
