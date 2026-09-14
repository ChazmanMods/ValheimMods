using BepInEx.Configuration;

namespace RunicWorldEngine
{
    internal static class WorldEngineConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> OverridePlayerCap, HealthEnabled, HealthWarnings, LogPeerDetails;
        internal static ConfigEntry<int> MaximumPlayers, QueueWarningMessages;
        internal static ConfigEntry<float> RttWarningMilliseconds, BacklogWarningKiB, StarvationSeconds,
            HeartbeatWarningSeconds, FrameWarningMilliseconds, OwnershipWarningPerSecond, WarningHoldSeconds, WarningCooldownSeconds;
        internal static ConfigEntry<bool> LogPeriodicSummary { get; private set; }
        internal static ConfigEntry<float> SummaryIntervalSeconds { get; private set; }
        internal static ConfigEntry<bool> SmoothWorldSaves { get; private set; }
        internal static ConfigEntry<float> SaveFrameBudgetMilliseconds { get; private set; }
        internal static ConfigEntry<float> MaximumSaveDeferralSeconds { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            OverridePlayerCap = config.Bind("Player Capacity", "Enabled", false, "Opt in to the audited hosting-limit override. Restart required. Conflicts or unknown limits block hosting/new admissions rather than partially applying a cap.");
            MaximumPlayers = config.Bind("Player Capacity", "MaximumPlayers", 10, new ConfigDescription("Human player cap (2-64); dedicated PlayFab host slot is added separately. Frozen until process restart. Higher caps require adequate server/network capacity.", new AcceptableValueRange<int>(2, 64)));
            HealthEnabled = config.Bind("Server Health", "Enabled", true, "Sample this process's peers once per second. Clients see their own connection, not remote server telemetry.");
            HealthWarnings = config.Bind("Server Health", "Warnings", true, "Write sustained server health warnings and recovery messages to BepInEx logs.");
            LogPeerDetails = config.Bind("Diagnostics", "LogPeerDetails", false, "Include names, peer IDs, RTT and queue/rate details in periodic summaries. No IP addresses or credentials are logged.");
            RttWarningMilliseconds = Threshold(config, "RttWarningMilliseconds", 500, 50, 10000);
            BacklogWarningKiB = Threshold(config, "BacklogWarningKiB", 256, 1, 65536);
            QueueWarningMessages = config.Bind("Server Health", "QueueWarningMessages", 1000, new ConfigDescription("Warning threshold for queued messages or priority/invalid ZDO work.", new AcceptableValueRange<int>(1, 100000)));
            StarvationSeconds = Threshold(config, "StarvationSeconds", 10, 2, 120);
            HeartbeatWarningSeconds = Threshold(config, "HeartbeatWarningSeconds", 5, 2, 25);
            FrameWarningMilliseconds = Threshold(config, "FrameWarningMilliseconds", 50, 16, 1000);
            OwnershipWarningPerSecond = Threshold(config, "OwnershipWarningPerSecond", 100, 1, 100000);
            WarningHoldSeconds = Threshold(config, "WarningHoldSeconds", 5, 1, 60);
            WarningCooldownSeconds = Threshold(config, "WarningCooldownSeconds", 60, 10, 600);
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Startup gate for world/network diagnostics, optional player-cap override, and save smoothing. Restart to change. When false, no Harmony patches or runtime state are created. This never enables deletion or network sync rescheduling.");
            LogPeriodicSummary = config.Bind(
                "Diagnostics", "LogPeriodicSummary", false,
                "Write rate-limited aggregate ZDO counts and traffic to the BepInEx log.");
            SummaryIntervalSeconds = config.Bind(
                "Diagnostics", "SummaryIntervalSeconds", 30f,
                new ConfigDescription(
                    "Seconds between optional aggregate summaries.",
                    new AcceptableValueRange<float>(5f, 600f)));
            SmoothWorldSaves = config.Bind(
                "Save Smoothing", "Enabled", true,
                "Coalesce overlapping asynchronous world saves and defer the main-thread PrepareSave capture " +
                "until a stable frame. Synchronous shutdown saves are never deferred.");
            SaveFrameBudgetMilliseconds = config.Bind(
                "Save Smoothing", "FrameBudgetMilliseconds", 24f,
                new ConfigDescription(
                    "Prefer to start asynchronous PrepareSave only when the previous unscaled frame was at or below this duration.",
                    new AcceptableValueRange<float>(8f, 100f)));
            MaximumSaveDeferralSeconds = config.Bind(
                "Save Smoothing", "MaximumDeferralSeconds", 5f,
                new ConfigDescription(
                    "Maximum time an asynchronous save may wait for a stable frame. The save then starts even if frames remain busy.",
                    new AcceptableValueRange<float>(0f, 30f)));
        }

        private static ConfigEntry<float> Threshold(ConfigFile config, string name, float initial, float minimum, float maximum) =>
            config.Bind("Server Health", name, initial, new ConfigDescription("Server health " + name + "; sustained conditions only, after a 15-second peer warm-up for network warnings.", new AcceptableValueRange<float>(minimum, maximum)));
    }
}
