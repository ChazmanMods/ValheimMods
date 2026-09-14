using System;
using BepInEx;
using HarmonyLib;
using RunicBuildCamera.Integration;

namespace RunicBuildCamera
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(
        CompatibilityGuard.BuildCameraCheGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicBuildCamera";
        public const string Name = "Runic Build Camera";
        public const string Version = "1.0.3";

        private Harmony _harmony;
        private bool _runtimeReady;
        private bool _runtimeFailureReported;

        private void Awake()
        {
            Diagnostics.Initialize(Logger);
            BuildCameraConfig.Bind(Config);
            BuildCameraConfig.Changed += OnConfigurationChanged;

            try
            {
                if (CompatibilityGuard.TryFindHardConflict(out string conflict))
                {
                    Diagnostics.Warn(conflict);
                    Diagnostics.Info($"{Name} v{Version} loaded with its camera disabled.");
                    return;
                }

                if (!ValheimAdapter.Initialize(out string adapterError))
                {
                    Diagnostics.Error(adapterError);
                    Diagnostics.Info($"{Name} v{Version} loaded with its camera disabled.");
                    return;
                }

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                BuildCameraRuntime.Initialize();
                _runtimeReady = true;
                Diagnostics.Info(
                    $"{Name} v{Version} ready. Precision Build Tool remains an independent " +
                    "plugin and is not required.");
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, "Runic Build Camera startup failed; vanilla camera behavior was preserved.");
                DisableRuntime();
            }
        }

        private void Update()
        {
            if (!_runtimeReady) return;

            try
            {
                BuildCameraRuntime.Tick();
            }
            catch (Exception exception)
            {
                BuildCameraRuntime.ForceStop();
                RemotePickupRuntime.Reset();
                DemisterRuntime.OnCameraExit();
                if (_runtimeFailureReported) return;

                _runtimeFailureReported = true;
                Diagnostics.Error(
                    exception,
                    "Runic Build Camera stopped after an update failure; normal Valheim camera controls remain available.");
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!_runtimeReady) return;
            BuildCameraRuntime.OnApplicationFocus(focused);
            if (focused) return;

            RemotePickupRuntime.Reset();
            DemisterRuntime.OnCameraExit();
        }

        private void OnConfigurationChanged()
        {
            if (!_runtimeReady) return;

            try
            {
                BuildCameraRuntime.OnConfigurationChanged();
            }
            catch (Exception exception)
            {
                BuildCameraRuntime.ForceStop();
                Diagnostics.Error(exception, "A configuration change stopped the detached camera safely.");
            }
        }

        private void OnDestroy()
        {
            BuildCameraConfig.Changed -= OnConfigurationChanged;
            DisableRuntime();
        }

        private void DisableRuntime()
        {
            _runtimeReady = false;

            // Cleanup remains best-effort per subsystem. In particular, a presentation-effect
            // failure must never prevent restoration of the player's scoped placement range.
            TryCleanup(RemotePickupRuntime.Shutdown, "remote pickup cleanup");
            TryCleanup(DemisterRuntime.Shutdown, "mist-effect cleanup");
            TryCleanup(BuildCameraRuntime.Shutdown, "camera-session cleanup");
            TryCleanup(ValheimAdapter.Shutdown, "Valheim adapter cleanup");

            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, "Runic Build Camera could not remove every Harmony patch during cleanup.");
            }
            finally
            {
                _harmony = null;
            }
        }

        private static void TryCleanup(Action cleanup, string label)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, $"Runic Build Camera {label} encountered an error.");
            }
        }
    }
}
