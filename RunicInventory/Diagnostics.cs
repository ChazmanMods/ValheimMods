using System;
using BepInEx.Logging;

namespace RunicInventory
{
    internal static class Diagnostics
    {
        private static ManualLogSource _log;

        internal static void Initialize(ManualLogSource log) => _log = log;
        internal static void Info(string value) => _log?.LogInfo(Bound(value));
        internal static void Warn(string value) => _log?.LogWarning(Bound(value));
        internal static void Error(Exception exception, string context) =>
            _log?.LogError(Bound(context) + " " + (exception == null ? "unknown" : exception.GetType().Name + ": " + Bound(exception.Message)));
        internal static void Trace(string value)
        {
            if (InventoryConfig.VerboseDiagnostics?.Value ?? false) _log?.LogInfo(Bound(value));
        }

        private static string Bound(string value)
        {
            string text = value ?? string.Empty;
            return text.Length <= 512 ? text : text.Substring(0, 512);
        }
    }
}
