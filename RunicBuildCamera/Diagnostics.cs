using System;
using BepInEx.Logging;

namespace RunicBuildCamera
{
    internal static class Diagnostics
    {
        private static ManualLogSource _log;

        internal static bool IsInitialized => _log != null;

        internal static void Initialize(ManualLogSource log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal static void Verbose(string message)
        {
            if (BuildCameraConfig.VerboseLogging != null && BuildCameraConfig.VerboseLogging.Value)
                _log?.LogInfo(message);
        }

        internal static void Debug(string message)
        {
            if (BuildCameraConfig.VerboseLogging != null && BuildCameraConfig.VerboseLogging.Value)
                _log?.LogDebug(message);
        }

        internal static void Info(string message) => _log?.LogInfo(message);

        internal static void Warn(string message) => _log?.LogWarning(message);

        internal static void Error(string message) => _log?.LogError(message);

        internal static void Error(Exception exception, string context)
        {
            if (exception == null)
            {
                Error(context);
                return;
            }

            _log?.LogError(context + Environment.NewLine + exception);
        }
    }
}
