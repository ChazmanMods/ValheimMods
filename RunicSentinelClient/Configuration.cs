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
                global::Runic.Localization.RunicText.Get("text_c2c47dd903fc") +
                global::Runic.Localization.RunicText.Get("text_3fffa17a165a"));
        }
    }
}
