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
            OverridePlayerCap = config.Bind("Player Capacity", "Enabled", false, global::Runic.Localization.RunicText.Get("text_f99a4834649c"));
            MaximumPlayers = config.Bind("Player Capacity", "MaximumPlayers", 10, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_c1c6835d0205"), new AcceptableValueRange<int>(2, 64)));
            HealthEnabled = config.Bind("Server Health", "Enabled", true, global::Runic.Localization.RunicText.Get("text_00308931ac1d"));
            HealthWarnings = config.Bind("Server Health", "Warnings", true, global::Runic.Localization.RunicText.Get("text_651d517dc40d"));
            LogPeerDetails = config.Bind("Diagnostics", "LogPeerDetails", false, global::Runic.Localization.RunicText.Get("text_42308444810e"));
            RttWarningMilliseconds = Threshold(config, "RttWarningMilliseconds", 500, 50, 10000);
            BacklogWarningKiB = Threshold(config, "BacklogWarningKiB", 256, 1, 65536);
            QueueWarningMessages = config.Bind("Server Health", "QueueWarningMessages", 1000, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_26b4a8c272de"), new AcceptableValueRange<int>(1, 100000)));
            StarvationSeconds = Threshold(config, "StarvationSeconds", 10, 2, 120);
            HeartbeatWarningSeconds = Threshold(config, "HeartbeatWarningSeconds", 5, 2, 25);
            FrameWarningMilliseconds = Threshold(config, "FrameWarningMilliseconds", 50, 16, 1000);
            OwnershipWarningPerSecond = Threshold(config, "OwnershipWarningPerSecond", 100, 1, 100000);
            WarningHoldSeconds = Threshold(config, "WarningHoldSeconds", 5, 1, 60);
            WarningCooldownSeconds = Threshold(config, "WarningCooldownSeconds", 60, 10, 600);
            Enabled = config.Bind(
                "General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_9c9d086955ae"));
            LogPeriodicSummary = config.Bind(
                "Diagnostics", "LogPeriodicSummary", false,
                global::Runic.Localization.RunicText.Get("text_4902dc8c1cb9"));
            SummaryIntervalSeconds = config.Bind(
                "Diagnostics", "SummaryIntervalSeconds", 30f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_5c17b1f1dcb6"),
                    new AcceptableValueRange<float>(5f, 600f)));
            SmoothWorldSaves = config.Bind(
                "Save Smoothing", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_062cf05eea1f") +
                global::Runic.Localization.RunicText.Get("text_92cbf9361d05"));
            SaveFrameBudgetMilliseconds = config.Bind(
                "Save Smoothing", "FrameBudgetMilliseconds", 24f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_b7bcb46277a4"),
                    new AcceptableValueRange<float>(8f, 100f)));
            MaximumSaveDeferralSeconds = config.Bind(
                "Save Smoothing", "MaximumDeferralSeconds", 5f,
                new ConfigDescription(
                    global::Runic.Localization.RunicText.Get("text_d2a955847a6c"),
                    new AcceptableValueRange<float>(0f, 30f)));
        }

        private static ConfigEntry<float> Threshold(ConfigFile config, string name, float initial, float minimum, float maximum) =>
            config.Bind("Server Health", name, initial, new ConfigDescription(global::Runic.Localization.RunicText.Get("text_b5d5241509a4") + name + global::Runic.Localization.RunicText.Get("text_af43bb1eed0a"), new AcceptableValueRange<float>(minimum, maximum)));
    }
}
