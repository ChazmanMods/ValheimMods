using BepInEx.Configuration;

namespace RunicVelocity
{
    internal static class VelocityConfig
    {
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> WarmManifest { get; private set; }
        internal static ConfigEntry<int> MaximumFiles { get; private set; }
        internal static ConfigEntry<bool> DetailedTracing { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                global::Runic.Localization.RunicText.Get("text_7f024d5fe1d9"));
            WarmManifest = config.Bind("Manifest", "WarmCache", true,
                global::Runic.Localization.RunicText.Get("text_c73467f88e3b"));
            MaximumFiles = config.Bind("Manifest", "MaximumFiles", 2048,
                new ConfigDescription(global::Runic.Localization.RunicText.Get("text_733b536750f8"),
                    new AcceptableValueRange<int>(1, Core.ManifestCachePolicy.MaximumFiles)));
            DetailedTracing = config.Bind("Diagnostics", "DetailedTracing", false,
                global::Runic.Localization.RunicText.Get("text_83d5af6a112c"));
        }
    }
}
