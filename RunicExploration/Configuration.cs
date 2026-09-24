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
                global::Runic.Localization.RunicText.Get("text_27bfcb5e763d"));
            ShowKnownPinBrowser = config.Bind(
                "Panels",
                "KnownPinBrowser",
                true,
                global::Runic.Localization.RunicText.Get("text_fb8648ff1e68"));
            ShowNavigationReadout = config.Bind(
                "Panels",
                "SelectedPinNavigation",
                true,
                global::Runic.Localization.RunicText.Get("text_40f42942c4d7"));
            ShowSailingReadout = config.Bind(
                "Panels",
                "SailingReadout",
                true,
                global::Runic.Localization.RunicText.Get("text_813459b949ca"));
            IncludeSharedPins = config.Bind(
                "KnownMap",
                "IncludeSharedPins",
                true,
                global::Runic.Localization.RunicText.Get("text_847e9c7181e0"));
            RefreshInterval = config.Bind(
                "Performance",
                "RefreshIntervalSeconds",
                0.5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_2bb460dc2ad7"),
                    new AcceptableValueRange<float>(0.25f, 3f)));
            MaximumResults = config.Bind(
                "Display",
                "MaximumVisibleResults",
                12,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_6578e81e9ffd"),
                    new AcceptableValueRange<int>(5, 24)));
            UiScale = config.Bind(
                "Display",
                "UiScale",
                1f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_8f2e776ef455"),
                    new AcceptableValueRange<float>(0.75f, 1.75f)));
            ControllerScaleMultiplier = config.Bind(
                "Display",
                "ControllerScaleMultiplier",
                1.15f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_86922a678733"),
                    new AcceptableValueRange<float>(1f, 1.5f)));
            PanelSide = config.Bind(
                "Display",
                "PanelSide",
                ExplorationPanelSide.Left,
                global::Runic.Localization.RunicText.Get("text_2dd15eee2fe1"));
            VerboseLogging = config.Bind(
                "Diagnostics",
                "VerboseLogging",
                false,
                global::Runic.Localization.RunicText.Get("text_997211976d79"));
        }
    }
}
