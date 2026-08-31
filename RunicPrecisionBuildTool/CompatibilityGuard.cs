using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using PluginInfo = BepInEx.PluginInfo;

namespace QuietBuildRotation
{
    internal static class CompatibilityGuard
    {
        internal const string PerfectPlacementGuid = "Azumatt_and_ValheimPlusDevs.PerfectPlacement";
        private const string PerfectPlacementSection = "1 - General";
        private const string PerfectPlacementKey = "Enable Free Placement Rotation";

        internal static bool ShouldDisableForPerfectPlacement(out string reason)
        {
            reason = null;
            if (!Chainloader.PluginInfos.TryGetValue(PerfectPlacementGuid, out PluginInfo pluginInfo))
                return false;

            if (pluginInfo?.Instance == null)
            {
                reason = "PerfectPlacement is installed, but its free-placement state could not be verified.";
                return true;
            }

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in pluginInfo.Instance.Config)
            {
                if (!string.Equals(entry.Key.Section, PerfectPlacementSection, StringComparison.Ordinal) ||
                    !string.Equals(entry.Key.Key, PerfectPlacementKey, StringComparison.Ordinal))
                    continue;

                string value = entry.Value.BoxedValue?.ToString();
                if (IsEnabled(value))
                {
                    reason = "PerfectPlacement free-placement rotation is enabled. Disable one rotation implementation before building.";
                    return true;
                }

                return false;
            }

            reason = "PerfectPlacement is installed, but its free-placement configuration entry was not found.";
            return true;
        }

        private static bool IsEnabled(string value) =>
            string.Equals(value, "On", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Enabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }
}
