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
                "Master switch. Standard Pair portals always yield to vanilla behavior.");
            UniversalRouting = file.Bind("Features", "UniversalRouting", true,
                "Enable authenticated public/private/Group network routing for solo, local-host, and compatible dedicated-server sessions.");
            ShowSetupPanel = file.Bind("Display", "ShowSetupPanel", true,
                "Show the complete state-aware setup guide while aiming at a portal and while the Runic editor is open.");
            PanelScale = file.Bind("Display", "PanelScale", 1f,
                new ConfigDescription("Scale of the portal setup guide.",
                    new AcceptableValueRange<float>(0.75f, 1.5f)));
            EditRangeMeters = file.Bind("Authority", "EditRangeMeters", 5f,
                new ConfigDescription("Maximum range for a portal-mode mutation.",
                    new AcceptableValueRange<float>(2f, 5f)));
            IndexRefreshSeconds = file.Bind("Performance", "IndexRefreshSeconds", 2f,
                new ConfigDescription("Server-side interval for a bounded portal snapshot refresh.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            MaximumEndpoints = file.Bind("Performance", "MaximumNetworkEndpoints", 1024,
                new ConfigDescription("Fail-closed graph cap. No partial graph is published when exceeded.",
                    new AcceptableValueRange<int>(16, PortalContractLimits.MaximumGraphEndpoints)));
            DirectoryPageSize = file.Bind("Directory", "CyclePageSize", 32,
                new ConfigDescription("Maximum authorized destinations returned by a bounded non-map directory request; the walk-in map picker uses the protocol cap.",
                    new AcceptableValueRange<int>(1, PortalContractLimits.MaximumDirectoryResults)));
            ReturnRouteMinutes = file.Bind("Routes", "ReturnRouteMinutes", 15,
                new ConfigDescription("Session-only per-traveler Return option lifetime.",
                    new AcceptableValueRange<int>(1, 120)));
            OneWayAcknowledgementSeconds = file.Bind("Routes", "OneWayAcknowledgementSeconds", 10,
                new ConfigDescription("Fallback one-way confirmation window. Clicking a one-way destination marker acknowledges it immediately.",
                    new AcceptableValueRange<int>(3, 30)));
            VerboseLogging = file.Bind("Diagnostics", "VerboseLogging", false,
                "Log bounded decision codes. Portal names, coordinates, and inventory contents are never logged.");
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
