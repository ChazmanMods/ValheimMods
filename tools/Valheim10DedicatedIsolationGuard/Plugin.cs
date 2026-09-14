using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Splatform;

namespace RunicSmoke.DedicatedIsolation
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "000.runic.smoke.dedicated-isolation-guard";
        public const string PluginName = "000 Runic Dedicated Smoke Isolation Guard";
        public const string PluginVersion = "1.0.0";

        private const string SaveRootVariable = "RUNIC_SMOKE_SAVEDIR";
        private const string LiveDataVariable = "RUNIC_SMOKE_LIVE_VALHEIM_DATA";
        private const string ProcessModeVariable = "RUNIC_SMOKE_PROCESS_MODE";
        private const string ExpectedManagedRootVariable = "RUNIC_SMOKE_EXPECTED_MANAGED_ROOT";
        private const string DedicatedServerMode = "dedicated-server";
        private const string SteamPlatformTypeName = "Splatform.Steam.SteamPlatform, Splatform.Steam";

        private void Awake()
        {
            try
            {
                string saveRoot = RequireAbsoluteDirectory(SaveRootVariable);
                string liveDataRoot = RequireAbsoluteExistingDirectory(LiveDataVariable);
                string expectedManagedRoot = RequireAbsoluteExistingDirectory(ExpectedManagedRootVariable);
                string processMode = Environment.GetEnvironmentVariable(ProcessModeVariable);
                if (!string.Equals(processMode, DedicatedServerMode, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(ProcessModeVariable + " must equal " + DedicatedServerMode + ".");
                }
                if (IsSameOrChild(saveRoot, liveDataRoot) || IsSameOrChild(liveDataRoot, saveRoot))
                {
                    throw new InvalidOperationException("The isolated and live save roots overlap.");
                }

                string observedManagedRoot = Normalize(Path.Combine(UnityEngine.Application.dataPath, "Managed"));
                if (!string.Equals(observedManagedRoot, expectedManagedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The observed and expected dedicated Managed roots differ.");
                }

                FieldInfo platformInstance = typeof(PlatformManager).GetField(
                    "s_instance",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (platformInstance == null || platformInstance.FieldType != typeof(PlatformManager))
                {
                    throw new MissingFieldException("PlatformManager.s_instance is unavailable or has drifted.");
                }
                if (platformInstance.GetValue(null) != null || PlatformManager.DistributionPlatform != null)
                {
                    throw new InvalidOperationException("Platform initialization began before the isolation guard ran.");
                }

                string steamPlatformAssembly = Path.Combine(observedManagedRoot, "Splatform.Steam.dll");
                if (File.Exists(steamPlatformAssembly))
                {
                    throw new InvalidOperationException("The dedicated runtime unexpectedly contains Splatform.Steam.dll.");
                }
                if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, "Splatform.Steam", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("The dedicated runtime unexpectedly loaded Splatform.Steam.");
                }
                if (Type.GetType(SteamPlatformTypeName, throwOnError: false) != null)
                {
                    throw new InvalidOperationException("The dedicated runtime unexpectedly resolves SteamPlatform.");
                }
                if (FileHelpers.CloudStorageSupported)
                {
                    throw new InvalidOperationException("The dedicated runtime unexpectedly reports cloud storage support.");
                }

                Utils.SetSaveDataPath(saveRoot);
                FieldInfo saveOverride = typeof(Utils).GetField(
                    "m_saveDataOverride",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (saveOverride == null || !saveOverride.IsStatic || saveOverride.FieldType != typeof(string))
                {
                    throw new MissingFieldException("Utils.m_saveDataOverride is unavailable or has drifted.");
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

                Logger.LogMessage(
                    "[RUNIC_SMOKE_ISOLATION_READY] mode=" + DedicatedServerMode +
                    " local_save_root=" + saveRoot +
                    " managed_root=" + observedManagedRoot +
                    " platform_manager_instance=null distribution_platform=null" +
                    " steam_platform_assembly=absent steam_platform_type=absent" +
                    " cloud_storage_supported=false dont_save_anything=true");
            }
            catch (Exception exception)
            {
                string detail = "[RUNIC_SMOKE_ISOLATION_FAILED] " + exception;
                Logger.LogError(detail);
                try
                {
                    System.Console.Error.WriteLine(detail);
                    System.Console.Error.Flush();
                }
                catch
                {
                }
                Environment.FailFast("Runic dedicated smoke isolation could not be established.", exception);
            }
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

        private static string RequireAbsoluteExistingDirectory(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new InvalidOperationException(variableName + " must contain an absolute path.");
            }

            string normalized = Normalize(value);
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
