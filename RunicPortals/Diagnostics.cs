using System;
using BepInEx.Logging;

namespace RunicPortals
{
    internal static class Diagnostics
    {
        private static ManualLogSource _log;

        internal static void Initialize(ManualLogSource log) => _log = log;
        internal static void Info(string text) => _log?.LogInfo(text);
        internal static void Warning(string text) => _log?.LogWarning(text);
        internal static void Error(string text) => _log?.LogError(text);
        internal static void Error(Exception exception, string text) =>
            _log?.LogError(text + " " + exception.GetType().Name + ": " + exception.Message);

        internal static void Trace(string text)
        {
            if (PortalConfig.VerboseLogging != null && PortalConfig.VerboseLogging.Value)
                _log?.LogDebug(text);
        }
    }
}
