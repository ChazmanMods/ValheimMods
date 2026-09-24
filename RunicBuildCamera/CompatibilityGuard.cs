using System;
using BepInEx.Bootstrap;

namespace RunicBuildCamera
{
    internal static class CompatibilityGuard
    {
        internal const string BuildCameraCheGuid = "Azumatt.BuildCameraCHE";

        internal static bool TryFindHardConflict(out string reason)
        {
            reason = null;

            foreach (var pair in Chainloader.PluginInfos)
            {
                string guid = pair.Value?.Metadata?.GUID;
                string name = pair.Value?.Metadata?.Name;
                if (string.Equals(guid, BuildCameraCheGuid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "BuildCameraCHE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Build Camera Custom Hammers Edition", StringComparison.OrdinalIgnoreCase))
                {
                    reason =
                        global::Runic.Localization.RunicText.Format("text_acac6a5e4a61", name ?? BuildCameraCheGuid) +
                        global::Runic.Localization.RunicText.Get("text_ba927d542b6f");
                    return true;
                }
            }

            // The type check also catches manually renamed assemblies whose metadata was altered.
            // Search only assemblies that are already loaded: AccessTools.TypeByName probes by
            // name and can emit noisy loader diagnostics for the normal "not installed" case.
            bool conflictingRuntimeLoaded = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(
                            "Valheim_Build_Camera.Valheim_Build_CameraPlugin",
                            throwOnError: false,
                            ignoreCase: false) == null) continue;
                    conflictingRuntimeLoaded = true;
                    break;
                }
                catch (Exception)
                {
                    // An unrelated partially loaded assembly cannot authorize camera coexistence.
                }
            }
            if (conflictingRuntimeLoaded)
            {
                reason =
                    global::Runic.Localization.RunicText.Get("text_a34f6de98a9a") +
                    global::Runic.Localization.RunicText.Get("text_f57f78aa3dfa");
                return true;
            }

            return false;
        }
    }
}
