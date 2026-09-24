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
        public const string Version = "1.0.5";

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
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_8ea2ea6caed3"));
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
                    global::Runic.Localization.RunicText.Get("text_2f17ce857460"));
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
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_888408df9d8b"));
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
            TryCleanup(RemotePickupRuntime.Shutdown, global::Runic.Localization.RunicText.Get("text_d53b70e83194"));
            TryCleanup(DemisterRuntime.Shutdown, global::Runic.Localization.RunicText.Get("text_480ad81fe2bf"));
            TryCleanup(BuildCameraRuntime.Shutdown, global::Runic.Localization.RunicText.Get("text_e7e1616b5b33"));
            TryCleanup(ValheimAdapter.Shutdown, global::Runic.Localization.RunicText.Get("text_3dad311a77e5"));

            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_a907974ac55d"));
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
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Format("text_35b8f9a59ccc", label));
            }
        }
    }
}
