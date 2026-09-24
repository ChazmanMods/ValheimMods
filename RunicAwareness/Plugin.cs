using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RunicAwareness.Integration;
using UnityEngine;

namespace RunicAwareness
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicAwareness";
        public const string Name = "Runic Awareness";
        public const string Version = "1.0.4";
        private Harmony _harmony;
        private AwarenessRuntime _runtime;
        private bool _languageSubscribed;

        internal static Plugin Instance { get; private set; }
        internal AwarenessRuntime Runtime => _runtime;

        private void Awake()
        {
            Instance = this;
            AwarenessConfig.Bind(Config);
            Config.SettingChanged += OnSettingChanged;

            try
            {
                ValheimContracts.VerifyInstalledSignatures();
                _runtime = new AwarenessRuntime(Logger, Application.isBatchMode);
                Localization.OnLanguageChange += OnLanguageChange;
                _languageSubscribed = true;

                if (!Application.isBatchMode)
                {
                    _harmony = new Harmony(Guid);
                    _harmony.PatchAll(typeof(Plugin).Assembly);
                }

                Logger.LogInfo(
                    Name + " v" + Version + " ready: bounded local timers, comfort explanation, " +
                    "equipment comparison, and current-context display panels; gameplay state is never changed.");
                if (Application.isBatchMode)
                    Logger.LogInfo("Dedicated/batch process detected; the display runtime is intentionally inert.");
            }
            catch (Exception exception)
            {
                Cleanup();
                Logger.LogError(
                    Name + " failed closed during startup; vanilla UI and gameplay remain unchanged. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void Update()
        {
            try { _runtime?.Update(); }
            catch (Exception exception) { _runtime?.FailClosed("update", exception); }
        }

        private void OnGUI()
        {
            try { _runtime?.Draw(); }
            catch (Exception exception) { _runtime?.FailClosed("draw", exception); }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs arguments)
        {
            try { _runtime?.OnConfigurationChanged(arguments?.ChangedSetting?.Definition); }
            catch (Exception exception)
            {
                Logger.LogWarning("Awareness configuration refresh failed closed: " + exception.Message);
            }
        }

        private void OnLanguageChange()
        {
            try
            {
                ComfortCapture.Reset();
                ContextCapture.Reset();
                HoverItemCapture.Reset();
                _runtime?.OnLocalizationChanged();
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Awareness language refresh failed closed: " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            Cleanup();
            Instance = null;
        }

        private void Cleanup()
        {
            if (_languageSubscribed)
            {
                Localization.OnLanguageChange -= OnLanguageChange;
                _languageSubscribed = false;
            }
            _runtime?.Dispose();
            _runtime = null;
            _harmony?.UnpatchSelf();
            _harmony = null;
            ContextCapture.Reset();
            HoverItemCapture.Reset();
            ComfortCapture.Reset();
        }
    }
}
