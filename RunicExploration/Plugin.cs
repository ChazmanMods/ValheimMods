using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RunicExploration.Integration;
using UnityEngine;

namespace RunicExploration
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicExploration";
        public const string Name = "Runic Exploration";
        public const string Version = "1.0.3";
        private Harmony _harmony;
        private ExplorationRuntime _runtime;

        internal static Plugin Instance { get; private set; }
        internal ExplorationRuntime Runtime => _runtime;

        private void Awake()
        {
            Instance = this;
            ExplorationConfig.Bind(Config);
            Config.SettingChanged += OnSettingChanged;
            try
            {
                ValheimContracts.VerifyInstalledSignatures();
                _runtime = new ExplorationRuntime(Logger, Application.isBatchMode);
                if (!Application.isBatchMode)
                {
                    _harmony = new Harmony(Guid);
                    _harmony.PatchAll(typeof(Plugin).Assembly);
                }
                Logger.LogInfo(
                    Name + " v" + Version +
                    " ready: bounded search and navigation for already-explored saved pins only.");
                if (Application.isBatchMode)
                    Logger.LogInfo(
                        "Dedicated/batch process detected; Exploration is intentionally inert and unpatched.");
            }
            catch (Exception exception)
            {
                Cleanup();
                Logger.LogError(
                    Name + " failed closed during startup; the vanilla map and world remain unchanged. " +
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
                Logger.LogWarning(
                    "Exploration configuration refresh failed closed: " + exception.Message);
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
            _runtime?.Dispose();
            _runtime = null;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
