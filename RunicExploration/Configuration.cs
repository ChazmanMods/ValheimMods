using BepInEx.Configuration;

namespace RunicExploration
{
    public enum ExplorationPanelSide
    {
        Left = 0,
        Right = 1
    }

    internal static class ExplorationConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> ShowKnownPinBrowser { get; private set; }
        internal static ConfigEntry<bool> ShowNavigationReadout { get; private set; }
        internal static ConfigEntry<bool> ShowSailingReadout { get; private set; }
        internal static ConfigEntry<bool> IncludeSharedPins { get; private set; }
        internal static ConfigEntry<float> RefreshInterval { get; private set; }
        internal static ConfigEntry<int> MaximumResults { get; private set; }
        internal static ConfigEntry<float> UiScale { get; private set; }
        internal static ConfigEntry<float> ControllerScaleMultiplier { get; private set; }
        internal static ConfigEntry<ExplorationPanelSide> PanelSide { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable the known-world navigation overlay. It never changes map/world data.");
            ShowKnownPinBrowser = config.Bind(
                "Panels",
                "KnownPinBrowser",
                true,
                "Show bounded search/filter results for saved pins on already explored map cells.");
            ShowNavigationReadout = config.Bind(
                "Panels",
                "SelectedPinNavigation",
                true,
                "Show straight-line distance and direction to the selected known pin.");
            ShowSailingReadout = config.Bind(
                "Panels",
                "SailingReadout",
                true,
                "Show current local ship, wind, current biome, and selected-known-pin distance.");
            IncludeSharedPins = config.Bind(
                "KnownMap",
                "IncludeSharedPins",
                true,
                "Include saved shared pins only when their map cell is already explored locally/shared.");
            RefreshInterval = config.Bind(
                "Performance",
                "RefreshIntervalSeconds",
                0.5f,
                new ConfigDescription(
                    "Bounded known-pin fingerprint and local navigation refresh interval.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            MaximumResults = config.Bind(
                "Display",
                "MaximumVisibleResults",
                12,
                new ConfigDescription(
                    "Maximum visible search results. The hard ceiling is 24.",
                    new AcceptableValueRange<int>(5, 24)));
            UiScale = config.Bind(
                "Display",
                "UiScale",
                1f,
                new ConfigDescription(
                    "Known-map panel scale.",
                    new AcceptableValueRange<float>(0.75f, 1.75f)));
            ControllerScaleMultiplier = config.Bind(
                "Display",
                "ControllerScaleMultiplier",
                1.15f,
                new ConfigDescription(
                    "Additional readability scale while Valheim reports a gamepad active.",
                    new AcceptableValueRange<float>(1f, 1.5f)));
            PanelSide = config.Bind(
                "Display",
                "PanelSide",
                ExplorationPanelSide.Left,
                "Place the panel on the left or right safe-area edge.");
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                "Log index rebuild counts only; never logs pin names, coordinates, or hidden state.");
        }
    }
}
