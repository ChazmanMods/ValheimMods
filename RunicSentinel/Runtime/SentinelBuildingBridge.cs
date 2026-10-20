using System;
using BepInEx.Configuration;
using RunicSentinel.Core;

namespace RunicSentinel.Runtime
{
    internal static class SentinelBuildingBridge
    {
        private static readonly object Gate = new object();
        private static ConfigFile Host()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) throw new InvalidOperationException("Only the host may save building settings.");
            return SentinelBuildingLimits.HostConfig ?? throw new InvalidOperationException("Building settings are unavailable on this host.");
        }
        internal static void Populate(SentinelAdminDocument document)
        {
            try
            {
                lock (Gate)
                {
                    var saved = BuildingSettings.Read(Host().ConfigFilePath);
                    var active = SentinelBuildingLimits.Active;
                    document.BuildingSaved = Convert.ToBase64String(saved.Encode());
                    document.BuildingActive = "Support ×" + BuildingSettings.Number(active.SupportMultiplier) + " | Raise " + BuildingSettings.Number(active.RaiseHeight) + " m | Dig " + BuildingSettings.Number(active.DigDepth) + " m";
                    var next = saved.Enabled ? saved.Limits : BuildingLimits.Vanilla;
                    document.BuildingStatus = Convert.ToBase64String(next.Encode()) == Convert.ToBase64String(active.Encode()) ? "Saved limits match the running world." : "Restart required to apply saved limits.";
                }
            }
            catch (Exception e) { document.BuildingSaved = ""; document.BuildingStatus = "Building settings unavailable: " + e.Message; }
        }
        internal static string Save(byte[] payload)
        {
            lock (Gate)
            {
                var config = Host();
                var request = BuildingSettings.Decode(payload);
                BuildingSettings.Save(config.ConfigFilePath, request);
                bool automatic = config.SaveOnConfigSet;
                config.SaveOnConfigSet = false;
                try
                {
                    config["Building and Terrain", "Enabled"].BoxedValue = request.Enabled;
                    config["Building and Terrain", "VerticalSupportMultiplier"].BoxedValue = request.Limits.SupportMultiplier;
                    config["Building and Terrain", "MaximumTerrainRaise"].BoxedValue = request.Limits.RaiseHeight;
                    config["Building and Terrain", "MaximumDigDepth"].BoxedValue = request.Limits.DigDepth;
                }
                finally { config.SaveOnConfigSet = automatic; }
                return "Building limits saved. Restart the host to apply. Previous config backed up.";
            }
        }
    }
}
