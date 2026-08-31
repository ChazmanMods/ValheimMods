using BepInEx.Configuration;

namespace RunicWorldEngine
{
    internal static class WorldEngineConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> LogPeriodicSummary { get; private set; }
        internal static ConfigEntry<float> SummaryIntervalSeconds { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Enable bounded read-only world-state observation. When false, no Harmony patches or observatory state are created. This never enables deletion or sync rescheduling.");
            LogPeriodicSummary = config.Bind(
                "Diagnostics", "LogPeriodicSummary", false,
                "Write rate-limited aggregate ZDO counts and traffic to the BepInEx log.");
            SummaryIntervalSeconds = config.Bind(
                "Diagnostics", "SummaryIntervalSeconds", 30f,
                new ConfigDescription(
                    "Seconds between optional aggregate summaries.",
                    new AcceptableValueRange<float>(5f, 600f)));
        }
    }
}
