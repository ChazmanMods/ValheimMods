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
                "Enable bounded startup measurement and the integrity-checked local manifest cache. When false, no timeline, worker, module, or service is started.");
            WarmManifest = config.Bind("Manifest", "WarmCache", true,
                "Reuse a cached SHA-256 and plugin metadata only when the exact relative path, size, and UTC modification time are unchanged. A server challenge may still require fresh hashing.");
            MaximumFiles = config.Bind("Manifest", "MaximumFiles", 2048,
                new ConfigDescription("Maximum DLLs considered in one bounded scan.",
                    new AcceptableValueRange<int>(1, Core.ManifestCachePolicy.MaximumFiles)));
            DetailedTracing = config.Bind("Diagnostics", "DetailedTracing", false,
                "Log one bounded manifest summary after the background scan. File paths are not logged.");
        }
    }
}
