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
                        $"{name ?? BuildCameraCheGuid} is installed. Both plugins own the detached " +
                        "build-camera transform, so Runic Build Camera was disabled. Remove or disable one.";
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
                    "Another Build Camera runtime was detected. Runic Build Camera was disabled " +
                    "to prevent two plugins from controlling the same camera.";
                return true;
            }

            return false;
        }
    }
}
