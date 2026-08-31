using System;
using BepInEx.Logging;
using UnityEngine;

namespace QuietBuildRotation
{
    internal static class Diagnostics
    {
        internal const string AuditedValheimVersion = "0.221.12";
        private static ManualLogSource _log;
        private static bool _adapterFailureLogged;
        private static bool _compatibilityWarningLogged;

        internal static bool RuntimeAvailable { get; private set; }
        internal static string DisabledReason { get; private set; }

        internal static bool CanRun => RuntimeAvailable && PluginConfig.Enabled != null && PluginConfig.Enabled.Value;

        internal static void Initialize(ManualLogSource log)
        {
            _log = log;
            RuntimeAvailable = false;
            DisabledReason = "startup self-test has not completed";
            _adapterFailureLogged = false;
            _compatibilityWarningLogged = false;
        }

        internal static void MarkRuntimeAvailable()
        {
            RuntimeAvailable = true;
            DisabledReason = null;
        }

        internal static void DisableAdapter(string detail)
        {
            RuntimeAvailable = false;
            DisabledReason = detail;
            if (_adapterFailureLogged) return;
            _adapterFailureLogged = true;
            _log?.LogError(
                $"Runic manipulation was disabled; vanilla placement remains active. " +
                $"Valheim {GetValheimVersion()}: {detail}");
        }

        internal static void DisableForCompatibility(string detail)
        {
            RuntimeAvailable = false;
            DisabledReason = detail;
            if (_compatibilityWarningLogged) return;
            _compatibilityWarningLogged = true;
            _log?.LogWarning(
                $"Runic manipulation was disabled to prevent a placement conflict; vanilla placement remains active. {detail}");
        }

        internal static void Verbose(string message)
        {
            if (PluginConfig.VerboseLogging != null && PluginConfig.VerboseLogging.Value)
                _log?.LogInfo(message);
        }

        internal static void Warn(string message) => _log?.LogWarning(message);

        internal static void Info(string message) => _log?.LogInfo(message);

        internal static string GetValheimVersion()
        {
            return AuditedValheimVersion;
        }
    }
}
