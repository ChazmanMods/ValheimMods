using System;
using BepInEx.Logging;

namespace RunicInteraction
{
    internal static class Diagnostics
    {
        private static ManualLogSource _log;

        internal static void Initialize(ManualLogSource log) => _log = log;
        internal static void Info(string message) => _log?.LogInfo(message);
        internal static void Warn(string message) => _log?.LogWarning(message);
        internal static void Error(string message) => _log?.LogError(message);
        internal static void Error(Exception exception, string message) =>
            _log?.LogError(message + " " + exception.GetType().Name + ": " + exception.Message);

        internal static void Trace(string message)
        {
            if (InteractionConfig.VerboseLogging != null && InteractionConfig.VerboseLogging.Value)
                _log?.LogDebug(message);
        }
    }
}
