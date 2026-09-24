using BepInEx.Configuration;

namespace RunicProduction
{
    internal static class ProductionConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<string> ContainerPrefabIds { get; private set; }
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
        internal static ConfigEntry<int> StockSchedulerMaximumExaminations { get; private set; }
        internal static ConfigEntry<float> StockSchedulerMaximumMilliseconds { get; private set; }
        internal static ConfigEntry<int> StockSchedulerMaximumOperations { get; private set; }
        internal static ConfigEntry<bool> ShowStatusOverlay { get; private set; }
        internal static ConfigEntry<bool> ShowControlHints { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            ContainerPrefabIds = config.Bind("Modded Containers", "AllowedPrefabIds", "piece_drawer",
                global::Runic.Localization.RunicText.Get("text_204394c804c5"));
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                global::Runic.Localization.RunicText.Get("text_39206eef7ec9") +
                global::Runic.Localization.RunicText.Get("text_1cd26a4e89ac"));
            MaximumLinkRange = config.Bind(
                "Links",
                "MaximumLinkRange",
                12f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_673632d137d7"),
                    new AcceptableValueRange<float>(2f, 30f)));
            MovedTargetTolerance = config.Bind(
                "Links",
                "MovedTargetTolerance",
                0.75f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_e9cec0a9c69d"),
                    new AcceptableValueRange<float>(0.1f, 3f)));
            FuelReserve = config.Bind(
                "Fuel",
                "ProtectedReserve",
                10,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_37a856c5c05d"),
                    new AcceptableValueRange<int>(0, 1000)));
            PullBatchSize = config.Bind(
                "Transfers",
                "PullBatchSize",
                1,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_9aee0df2c8aa"),
                    new AcceptableValueRange<int>(1, 10)));
            CookingStationPrefabAllowList = config.Bind(
                "Cooking Stations",
                "AllowedPrefabIds",
                "piece_cookingstation,piece_cookingstation_iron,piece_oven",
                global::Runic.Localization.RunicText.Get("text_9847fcc16532") +
                global::Runic.Localization.RunicText.Get("text_34972eb1dc79"));
            CookingStationPrefabDenyList = config.Bind(
                "Cooking Stations",
                "DeniedPrefabIds",
                string.Empty,
                global::Runic.Localization.RunicText.Get("text_4a9e0fd45e55"));
            RecipeStationPrefabAllowList = config.Bind(
                "Recipe Stations",
                "AllowedPrefabIds",
                "piece_cauldron,piece_MeadCauldron,piece_preptable",
                global::Runic.Localization.RunicText.Get("text_0529b54ac8ec") +
                global::Runic.Localization.RunicText.Get("text_97f91c42a42f"));
            RecipeStationPrefabDenyList = config.Bind(
                "Recipe Stations",
                "DeniedPrefabIds",
                string.Empty,
                global::Runic.Localization.RunicText.Get("text_310b71d8a3e5"));
            RecipeNearbyIngredientsEnabled = config.Bind(
                "Recipe Nearby Ingredients",
                "Enabled",
                true,
                global::Runic.Localization.RunicText.Get("text_727618a98ce7") +
                global::Runic.Localization.RunicText.Get("text_d1d98e137445") +
                    global::Runic.Localization.RunicText.Get("text_cbf55ee44da8") +
                global::Runic.Localization.RunicText.Get("text_d033d83f2fb7"));
            RecipeNearbyMaximumSourceChests = config.Bind(
                "Recipe Nearby Ingredients",
                "MaximumSourceChests",
                32,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_129cd70f0581") +
                    global::Runic.Localization.RunicText.Get("text_bad246e5026f"),
                    new AcceptableValueRange<int>(1, 64)));
            FermenterPrefabAllowList = config.Bind(
                "Fermenters",
                "AllowedPrefabIds",
                "fermenter",
                global::Runic.Localization.RunicText.Get("text_753e29d964f3"));
            FermenterPrefabDenyList = config.Bind(
                "Fermenters",
                "DeniedPrefabIds",
                string.Empty,
                global::Runic.Localization.RunicText.Get("text_6d168ff49b8d"));
            DefaultIngredientReserve = config.Bind(
                "Ingredient Reserves",
                "DefaultProtectedReserve",
                0,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_ce8b9ed57c78"),
                    new AcceptableValueRange<int>(0, 1000000)));
            IngredientReserveRules = config.Bind(
                "Ingredient Reserves",
                "ProtectedPrefabAmounts",
                string.Empty,
                global::Runic.Localization.RunicText.Get("text_5c214851981e") +
                global::Runic.Localization.RunicText.Get("text_bdddca18f701") +
                global::Runic.Localization.RunicText.Get("text_842f9a3e70cc"));
            MaximumLinksPerRole = config.Bind(
                "Links",
                "MaximumChestsPerRole",
                8,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_e90379aba3fb"),
                    new AcceptableValueRange<int>(1, 16)));
            DefaultReplenishmentReserve = config.Bind(
                "Replenishment Stock",
                "DefaultReserve",
                10,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_f73d0b94eda4"),
                    new AcceptableValueRange<int>(1, 1000000)));
            ReplenishmentReserveRules = config.Bind(
                "Replenishment Stock",
                "PrefabReserves",
                string.Empty,
                global::Runic.Localization.RunicText.Get("text_64b3bc863c89"));
            StockSchedulerIntervalSeconds = config.Bind(
                "Stock Scheduler",
                "IntervalSeconds",
                2f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_a1468524e86d"),
                    new AcceptableValueRange<float>(0.5f, 10f)));
            StockSchedulerMaximumExaminations = config.Bind("Stock Scheduler", "MaximumExaminationsPerQuantum", 128,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_89cd275fab0d"), new AcceptableValueRange<int>(1, 4096)));
            StockSchedulerMaximumMilliseconds = config.Bind("Stock Scheduler", "MaximumMillisecondsPerQuantum", 4f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_b742263852e4"), new AcceptableValueRange<float>(0.5f, 50f)));
            StockSchedulerMaximumOperations = config.Bind(
                "Stock Scheduler",
                "MaximumOperationsPerQuantum",
                64,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_068b1732652e"),
                    new AcceptableValueRange<int>(1, 256)));
            ShowStatusOverlay = config.Bind(
                "UI",
                "ShowStatusOverlay",
                true,
                global::Runic.Localization.RunicText.Get("text_cd1e98bd2869"));
            ShowControlHints = config.Bind(
                "UI",
                "ShowControlHints",
                true,
                global::Runic.Localization.RunicText.Get("text_cacc8f89d5aa"));
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                global::Runic.Localization.RunicText.Get("text_76ee087725ff"));
        }
    }
}
