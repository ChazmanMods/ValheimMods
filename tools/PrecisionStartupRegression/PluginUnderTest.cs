using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using QuietBuildRotation.Integration;
using QuietBuildRotation.UI;
using Splatform;
using UnityEngine;

namespace QuietBuildRotation
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(
        CompatibilityGuard.PerfectPlacementGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicPrecisionBuildTool";
        public const string Name = "Runic Precision Build Tool";
        public const string Version = "2.0.9";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private bool _startupPending;
        private string _lastConflictReport;

        private void Awake()
        {
            Log = Logger;
            Diagnostics.Initialize(Logger);
            PluginConfig.Bind(Config);
            PluginConfig.Changed += OnConfigurationChanged;
            ReportBindingConflicts();

            string compatibilityReason;
            try
            {
                if (!CompatibilityGuard.ShouldDisableForPerfectPlacement(out compatibilityReason))
                    compatibilityReason = null;
            }
            catch (Exception exception)
            {
                compatibilityReason =
                    $"PerfectPlacement compatibility state could not be verified: " +
                    $"{exception.GetType().Name}: {exception.Message}";
            }

            if (!string.IsNullOrEmpty(compatibilityReason))
            {
                Diagnostics.DisableForCompatibility(compatibilityReason);
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            if (!PlacementAdapter.Initialize(out string adapterError))
            {
                Diagnostics.DisableAdapter(adapterError);
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception exception)
            {
                Diagnostics.DisableAdapter(
                    $"Harmony patching failed: {exception.GetType().Name}: {exception.Message}");
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            // PatchAll may queue work when StartupAccelerator batches Harmony patches.
            // Keep every gameplay hook gated until Start, after chainloader completion.
            _startupPending = true;
            Logger.LogInfo($"{Name} v{Version} patches requested; verification pending until startup completes.");
        }

        private void Start()
        {
            if (!_startupPending) return;
            _startupPending = false;

            if (!PlacementAdapter.CompleteRuntimeVerification(Guid, out string verificationError))
            {
                Diagnostics.DisableAdapter(verificationError);
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }

            try
            {
                PlacementRuntime.Initialize();
            }
            catch (Exception exception)
            {
                PlacementRuntime.Shutdown();
                Diagnostics.DisableAdapter(
                    $"runtime initialization failed: {exception.GetType().Name}: {exception.Message}");
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }
            Diagnostics.MarkRuntimeAvailable();
            try
            {
                if (Hud.instance != null)
                    OrientationPresenter.Attach(Hud.instance);
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("existing selected-piece panel attachment", exception);
            }

            if (!Diagnostics.RuntimeAvailable)
            {
                PlacementRuntime.Shutdown();
                UnpatchFailedStartup();
                Logger.LogInfo($"{Name} v{Version} loaded with Runic manipulation disabled.");
                return;
            }
            Logger.LogInfo(
                $"{Name} v{Version} ready on Valheim {Diagnostics.GetValheimVersion()}. " +
                "Runic wheel and movement actions use explicit configurable chords and fail closed.");
        }

        private void OnApplicationFocus(bool hasFocus) =>
            PlacementRuntime.OnApplicationFocus(hasFocus);

        private void Update()
        {
            try
            {
                PlacementRuntime.UpdateUtilities();
            }
            catch (Exception exception)
            {
                RunicHookGuard.Disable("bounded utility input", exception);
            }
        }

        private void OnDestroy()
        {
            _startupPending = false;
            PluginConfig.Changed -= OnConfigurationChanged;
            PlacementRuntime.Shutdown();
            OrientationPresenter.Destroy();
            _harmony?.UnpatchSelf();
        }

        private void OnConfigurationChanged()
        {
            ReportBindingConflicts();
            PlacementRuntime.OnConfigurationChanged();
        }

        private void ReportBindingConflicts()
        {
            string conflicts = PluginConfig.FindExactChordConflicts();
            if (string.IsNullOrEmpty(conflicts) || string.Equals(conflicts, _lastConflictReport, StringComparison.Ordinal))
                return;

            _lastConflictReport = conflicts;
            Logger.LogWarning(
                $"Exact Runic input chord conflict detected: {conflicts}. " +
                "Bindings were preserved; the conflicting chord will not be guessed or remapped.");
        }

        private void UnpatchFailedStartup()
        {
            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    $"Runic startup cleanup could not remove every partial patch; all remaining " +
                    $"hooks stay disabled: {exception.GetType().Name}: {exception.Message}");
            }
            _harmony = null;
        }
    }

}
