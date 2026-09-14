using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Splatform;

namespace RunicSmoke.ClientIsolation
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "000.runic.smoke.isolation-guard";
        public const string PluginName = "000 Runic Smoke Isolation Guard";
        public const string PluginVersion = "1.0.0";

        private const string SaveRootVariable = "RUNIC_SMOKE_SAVEDIR";
        private const string LiveDataVariable = "RUNIC_SMOKE_LIVE_VALHEIM_DATA";
        private const string SteamPlatformTypeName = "Splatform.Steam.SteamPlatform, Splatform.Steam";
        private const string HarmonyOwner = PluginGuid + ".harmony";

        private void Awake()
        {
            try
            {
                string saveRoot = RequireAbsoluteDirectory(SaveRootVariable);
                string liveDataRoot = RequireAbsoluteDirectory(LiveDataVariable);
                if (IsSameOrChild(saveRoot, liveDataRoot) || IsSameOrChild(liveDataRoot, saveRoot))
                {
                    throw new InvalidOperationException("The isolated and live save roots overlap.");
                }

                if (PlatformManager.DistributionPlatform != null)
                {
                    throw new InvalidOperationException("Platform initialization began before the isolation guard ran.");
                }

                Utils.SetSaveDataPath(saveRoot);
                FieldInfo saveOverride = AccessTools.Field(typeof(Utils), "m_saveDataOverride");
                if (saveOverride == null)
                {
                    throw new MissingFieldException("Utils.m_saveDataOverride is unavailable.");
                }
                if (!saveOverride.IsStatic || saveOverride.FieldType != typeof(string))
                {
                    throw new InvalidOperationException("Utils.m_saveDataOverride is not the expected static string field.");
                }
                string observedSaveRootValue = saveOverride.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(observedSaveRootValue))
                {
                    throw new InvalidOperationException("Utils did not retain the isolated local save root.");
                }
                string observedSaveRoot = Normalize(observedSaveRootValue);
                if (!string.Equals(saveRoot, observedSaveRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Utils rejected the isolated local save root.");
                }

                SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
                if (!SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything))
                {
                    throw new InvalidOperationException("DontSaveAnything could not be established.");
                }

                Type steamPlatform = Type.GetType(SteamPlatformTypeName, throwOnError: true);
                MethodInfo saveProviderGetter = AccessTools.PropertyGetter(steamPlatform, "SaveDataProvider");
                MethodInfo prefix = AccessTools.Method(typeof(Plugin), nameof(DisableSteamSaveProvider));
                if (saveProviderGetter == null || prefix == null)
                {
                    throw new MissingMethodException("Steam save-provider isolation seam is unavailable.");
                }

                var harmony = new Harmony(HarmonyOwner);
                harmony.Patch(saveProviderGetter, prefix: new HarmonyMethod(prefix));
                Patches patchInfo = Harmony.GetPatchInfo(saveProviderGetter);
                if (patchInfo == null || !patchInfo.Prefixes.Any(patch => patch.owner == HarmonyOwner))
                {
                    throw new InvalidOperationException("Steam save-provider isolation prefix was not installed.");
                }

                Logger.LogMessage(
                    "[RUNIC_SMOKE_ISOLATION_READY] local_save_root=" + saveRoot +
                    " steam_save_provider=null dont_save_anything=true");
            }
            catch (Exception exception)
            {
                Logger.LogError("[RUNIC_SMOKE_ISOLATION_FAILED] " + exception);
                Environment.FailFast("Runic graphical smoke isolation could not be established.", exception);
            }
        }

        private static bool DisableSteamSaveProvider(ref ISaveDataProvider __result)
        {
            __result = null;
            return false;
        }

        private static string RequireAbsoluteDirectory(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new InvalidOperationException(variableName + " must contain an absolute path.");
            }

            string normalized = Normalize(value);
            Directory.CreateDirectory(normalized);
            if (!Directory.Exists(normalized))
            {
                throw new DirectoryNotFoundException(normalized);
            }
            return normalized;
        }

        private static bool IsSameOrChild(string candidate, string parent)
        {
            string candidateWithSeparator = Normalize(candidate) + Path.DirectorySeparatorChar;
            string parentWithSeparator = Normalize(parent) + Path.DirectorySeparatorChar;
            return candidateWithSeparator.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
