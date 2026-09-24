using System;
using BepInEx.Configuration;
using RunicPortals.Api;

namespace RunicPortals
{
    internal static class PortalConfig
    {
        internal static event Action Changed;

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> UniversalRouting { get; private set; }
        internal static ConfigEntry<bool> ShowSetupPanel { get; private set; }
        internal static ConfigEntry<float> PanelScale { get; private set; }
        internal static ConfigEntry<float> EditRangeMeters { get; private set; }
        internal static ConfigEntry<float> IndexRefreshSeconds { get; private set; }
        internal static ConfigEntry<int> MaximumEndpoints { get; private set; }
        internal static ConfigEntry<int> DirectoryPageSize { get; private set; }
        internal static ConfigEntry<int> ReturnRouteMinutes { get; private set; }
        internal static ConfigEntry<int> OneWayAcknowledgementSeconds { get; private set; }
        internal static ConfigEntry<bool> VerboseLogging { get; private set; }

        private static ConfigFile _file;

        internal static void Bind(ConfigFile file)
        {
            _file = file ?? throw new ArgumentNullException(nameof(file));
            Enabled = file.Bind("General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_41f8794dcbce"));
            UniversalRouting = file.Bind("Features", "UniversalRouting", true,
                global::Runic.Localization.RunicText.Get("text_7563ca021fa0"));
            ShowSetupPanel = file.Bind("Display", "ShowSetupPanel", true,
                global::Runic.Localization.RunicText.Get("text_666fd723aa69"));
            PanelScale = file.Bind("Display", "PanelScale", 1f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_792354c9e7fd"),
                    new AcceptableValueRange<float>(0.75f, 1.5f)));
            EditRangeMeters = file.Bind("Authority", "EditRangeMeters", 5f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_c8d47208aa3f"),
                    new AcceptableValueRange<float>(2f, 5f)));
            IndexRefreshSeconds = file.Bind("Performance", "IndexRefreshSeconds", 2f,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_12ebdbc8d8b4"),
                    new AcceptableValueRange<float>(0.5f, 30f)));
            MaximumEndpoints = file.Bind("Performance", "MaximumNetworkEndpoints", 1024,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_da6edd40c9f6"),
                    new AcceptableValueRange<int>(16, PortalContractLimits.MaximumGraphEndpoints)));
            DirectoryPageSize = file.Bind("Directory", "CyclePageSize", 32,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_85e5ebb9bd6f"),
                    new AcceptableValueRange<int>(1, PortalContractLimits.MaximumDirectoryResults)));
            ReturnRouteMinutes = file.Bind("Routes", "ReturnRouteMinutes", 15,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_a1f25b9024fa"),
                    new AcceptableValueRange<int>(1, 120)));
            OneWayAcknowledgementSeconds = file.Bind("Routes", "OneWayAcknowledgementSeconds", 10,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_24e0a2ebde06"),
                    new AcceptableValueRange<int>(3, 30)));
            VerboseLogging = file.Bind("Diagnostics", "VerboseLogging", false,
                global::Runic.Localization.RunicText.Get("text_436a7f4e0e28"));
            file.SettingChanged += OnSettingChanged;
        }

        internal static void Unbind()
        {
            if (_file != null) _file.SettingChanged -= OnSettingChanged;
            _file = null;
            Enabled = null;
            UniversalRouting = null;
            ShowSetupPanel = null;
            PanelScale = null;
            EditRangeMeters = null;
            IndexRefreshSeconds = null;
            MaximumEndpoints = null;
            DirectoryPageSize = null;
            ReturnRouteMinutes = null;
            OneWayAcknowledgementSeconds = null;
            VerboseLogging = null;
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs args) =>
            Changed?.Invoke();
    }
}
