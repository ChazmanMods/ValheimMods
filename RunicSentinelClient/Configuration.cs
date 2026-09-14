using BepInEx.Configuration;

namespace RunicSentinelClient
{
    internal static class ClientConfiguration
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable client-only Runic Sentinel admission profile reporting. " +
                "The setting is sampled at startup; the plugin remains inert on servers.");
        }
    }
}
