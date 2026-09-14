using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Runic.Foundation.Core;
using RunicAgriculture.Core;
using RunicAgriculture.Integration;
using UnityEngine;

namespace RunicAgriculture
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicAgriculture";
        public const string Name = "Runic Agriculture";
        public const string Version = "1.0.3";
        public const string ModuleId = "runic.agriculture";
        public const string ProtocolVersion = "1.0";

        private readonly List<KeybindingRegistration> _keybindingRegistrations =
            new List<KeybindingRegistration>();
        private readonly KeybindingConflictRegistry _keybindings =
            new KeybindingConflictRegistry();
        private Harmony _harmony;
        private AgricultureRuntime _runtime;

        internal static Plugin Instance { get; private set; }
        internal AgricultureRuntime Runtime => _runtime;
        internal ManualLogSource Log => Logger;

        private void Awake()
        {
            Instance = this;
            AgricultureConfig.Bind(Config);
            Config.SettingChanged += OnSettingChanged;
            try
            {
                var patternService = new AgriculturePatternService();
                _runtime = new AgricultureRuntime(patternService, Logger);
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                RegisterKeybindings();
                Logger.LogInfo(
                    Name + " v" + Version + " ready: bounded pattern preview, native owner-local " +
                    "planting, exact Pickable area harvest, replant offers, and live controls.");
                Logger.LogInfo("Runic Agriculture configuration: " + _runtime.ConfigurationSummary());
                Logger.LogInfo("Runic Agriculture controls: " + _runtime.ControlSummary());
            }
            catch (Exception exception)
            {
                ShutdownRuntime();
                Logger.LogError(
                    Name + " startup failed; vanilla agriculture remains unchanged. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            ShutdownRuntime();
            Instance = null;
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs arguments)
        {
            try
            {
                ConfigDefinition definition = arguments?.ChangedSetting?.Definition;
                if (definition == null || definition.Section == "Controls" ||
                    definition.Section == "Pattern Editing Controls" ||
                    definition.Section == "Controller Controls" ||
                    definition.Section == "Controller Pattern Editor")
                    RegisterKeybindings();
                string changed = definition == null
                    ? "unknown setting"
                    : definition.Section + "/" + definition.Key;
                _runtime?.OnConfigurationChanged(changed);
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Agriculture configuration refresh failed: " + exception.Message);
            }
        }

        private void RegisterKeybindings()
        {
            DisposeKeybindings();
            RegisterKeybinding("confirm-pattern", "Confirm planting pattern", AgricultureConfig.ConfirmPattern.Value);
            RegisterKeybinding("cycle-pattern", "Cycle planting pattern", AgricultureConfig.CyclePattern.Value);
            RegisterKeybinding("area-harvest", "Area harvest", AgricultureConfig.AreaHarvest.Value);
            RegisterKeybinding("confirm-replant", "Confirm replant offer", AgricultureConfig.ConfirmReplant.Value);
            RegisterKeybinding("increase-rows", "Increase planting rows", AgricultureConfig.IncreaseRows.Value);
            RegisterKeybinding("decrease-rows", "Decrease planting rows", AgricultureConfig.DecreaseRows.Value);
            RegisterKeybinding("increase-columns", "Increase planting columns", AgricultureConfig.IncreaseColumns.Value);
            RegisterKeybinding("decrease-columns", "Decrease planting columns", AgricultureConfig.DecreaseColumns.Value);
            RegisterKeybinding("toggle-shape-side", "Switch planting shape side", AgricultureConfig.ToggleShapeSide.Value);
            RegisterKeybinding("decrease-left-pinch", "Widen trapezoid left edge", AgricultureConfig.DecreaseLeftPinch.Value);
            RegisterKeybinding("increase-left-pinch", "Pinch trapezoid left edge", AgricultureConfig.IncreaseLeftPinch.Value);
            RegisterKeybinding("decrease-right-pinch", "Widen trapezoid right edge", AgricultureConfig.DecreaseRightPinch.Value);
            RegisterKeybinding("increase-right-pinch", "Pinch trapezoid right edge", AgricultureConfig.IncreaseRightPinch.Value);
            if (!(AgricultureConfig.ControllerEnabled?.Value ?? false)) return;
            AgricultureControllerBindings controller = AgricultureConfig.CurrentControllerBindings();
            if (!controller.TryValidate(out string problem))
            {
                Logger.LogWarning("Agriculture controller bindings were not registered: " + problem + ".");
                return;
            }
            RegisterControllerBinding("controller-confirm", "Confirm planting/replant preview (controller)",
                controller.Confirm, controller.Modifier);
            RegisterControllerBinding("controller-cycle", "Cycle planting pattern (controller)",
                controller.Cycle, controller.Modifier);
            RegisterControllerBinding("controller-area-harvest", "Area harvest (controller)",
                controller.AreaHarvest, controller.Modifier);
            RegisterControllerControl("controller-editor-previous", "Previous planting setting (controller)",
                controller.PreviousEditorField);
            RegisterControllerControl("controller-editor-next", "Next planting setting (controller)",
                controller.NextEditorField);
            RegisterControllerControl("controller-editor-decrease", "Decrease planting setting (controller)",
                controller.DecreaseEditorValue);
            RegisterControllerControl("controller-editor-increase", "Increase planting setting (controller)",
                controller.IncreaseEditorValue);
        }

        private void RegisterKeybinding(string id, string name, KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return;
            var modifiers = new List<string>();
            IEnumerable<KeyCode> keys = shortcut.Modifiers;
            if (keys != null)
                foreach (KeyCode key in keys) modifiers.Add(key.ToString());
            _keybindingRegistrations.Add(_keybindings.Register(new KeybindingDescriptor(
                ModuleId,
                id,
                name,
                new InputChord("keyboard", shortcut.MainKey.ToString(), modifiers),
                "agriculture")));
        }

        private void RegisterControllerBinding(
            string id,
            string name,
            ValheimControllerAction primary,
            ValheimControllerAction modifier)
        {
            _keybindingRegistrations.Add(_keybindings.Register(new KeybindingDescriptor(
                ModuleId,
                id,
                name,
                new InputChord("controller", primary.ToString(), new[] { modifier.ToString() }),
                "agriculture")));
        }

        private void RegisterControllerControl(
            string id,
            string name,
            ValheimControllerAction primary)
        {
            _keybindingRegistrations.Add(_keybindings.Register(new KeybindingDescriptor(
                ModuleId,
                id,
                name,
                new InputChord("controller", primary.ToString(), Array.Empty<string>()),
                "agriculture")));
        }

        private void ShutdownRuntime()
        {
            DisposeKeybindings();
            try { _runtime?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Agriculture runtime cleanup failed: " + exception.Message);
            }
            _runtime = null;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Agriculture Harmony cleanup failed: " + exception.Message);
            }
            _harmony = null;
        }

        private void DisposeKeybindings()
        {
            for (int index = _keybindingRegistrations.Count - 1; index >= 0; index--)
            {
                try { _keybindingRegistrations[index].Dispose(); }
                catch (Exception exception)
                {
                    Logger.LogWarning("Keybinding cleanup failed: " + exception.Message);
                }
            }
            _keybindingRegistrations.Clear();
        }
    }
}
