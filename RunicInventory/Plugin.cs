using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Runic.Foundation.Core;
using RunicInventory.Api;
using RunicInventory.Core;
using RunicInventory.Integration;
using UnityEngine;

namespace RunicInventory
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(BetterArcheryCompatibility.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicInventory";
        public const string Name = "Runic Inventory";
        public const string Version = "1.1.10";
        public const string ModuleId = "runic.inventory";
        public const string ProtocolVersion = "1.0";

        private readonly List<KeybindingRegistration> _bindings =
            new List<KeybindingRegistration>();
        private readonly KeybindingConflictRegistry _keybindings =
            new KeybindingConflictRegistry();
        private Harmony _harmony;
        private InventoryRuntime _runtime;
        private bool _configurationSubscribed;
        private bool _inputLayoutSubscribed;
        private bool _shuttingDown;

        internal static Plugin Instance { get; private set; }
        internal static bool RuntimeReady { get; private set; }
        internal InventoryRuntime Runtime => _runtime;

        private void Awake()
        {
            Instance = this;
            Diagnostics.Initialize(Logger);
            InventoryIntegrationApi.BeginInitialization();
            try
            {
                InventoryConfig.Bind(Config);
                Config.SettingChanged += OnSettingChanged;
                _configurationSubscribed = true;
                ZInput.OnInputLayoutChanged += OnInputLayoutChanged;
                _inputLayoutSubscribed = true;

                if (!ValheimContracts.Initialize(out string contractProblem))
                    throw new MissingMethodException(contractProblem);

                _runtime = new InventoryRuntime(batch: false);
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                BetterArcheryCompatibility.Initialize(_harmony);
                RegisterBindings();
                _runtime.Initialize();
                InventoryIntegrationApi.Attach(_runtime);
                RuntimeReady = true;
                Logger.LogInfo(
                    Name + " v" + Version + " ready. Native owner-local topology, locks, sort, " +
                    "quick use, equipment relocation, saves, and tombstones remain Valheim-owned.");
                if (Application.isBatchMode)
                    Logger.LogInfo(
                        "Batch transport detected: only an exact owning local Player can activate Inventory; " +
                        "a true dedicated server remains inert.");
            }
            catch (Exception exception)
            {
                Logger.LogError(Name + " startup failed: " + exception);
                ShutdownRuntime();
                InventoryIntegrationApi.MarkStartupFailed();
            }
        }

        private void Update()
        {
            if (!RuntimeReady || _runtime == null) return;
            try { _runtime.Tick(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_6586951ebeea"));
                _runtime.FailClosed("runtime.exception");
            }
        }

        private void OnGUI()
        {
            if (!RuntimeReady || _runtime == null) return;
            try { _runtime.Draw(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_634f4fd622aa"));
            }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs arguments)
        {
            if (!RuntimeReady || _runtime == null) return;
            // The display hook reads this setting on the next UI refresh; no inventory
            // migration or keybinding registration is needed for a spacing change.
            if (ReferenceEquals(arguments?.ChangedSetting, InventoryConfig.CompactQuiverLayout)) return;
            try { _runtime.OnConfigurationChanged(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_c0e41f8a2d26"));
                _runtime.FailClosed("config.refresh-failed");
            }
            try { RefreshBindings(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_7c7aad401442"));
            }
        }

        private void OnInputLayoutChanged()
        {
            ControllerChordSession.Reset();
            Diagnostics.Trace(
                "Controller input layout changed; Inventory bindings will be re-resolved.");
        }

        private void OnDestroy()
        {
            ShutdownRuntime();
            Diagnostics.Initialize(null);
            Instance = null;
        }

        private void RegisterBindings()
        {
            if (!(InventoryConfig.Enabled?.Value ?? false)) return;
            RegisterKeyboard("quick-1", "Use quick slot 1", InventoryConfig.Quick1.Value, "gameplay");
            RegisterKeyboard("quick-2", "Use quick slot 2", InventoryConfig.Quick2.Value, "gameplay");
            RegisterKeyboard("quick-3", "Use quick slot 3", InventoryConfig.Quick3.Value, "gameplay");
            RegisterKeyboard("sort", "Sort selected safe inventory rows", InventoryConfig.Sort.Value, "inventory");
            RegisterKeyboard("lock", "Toggle focused inventory slot lock", InventoryConfig.ToggleLock.Value, "inventory");
            if (!(InventoryConfig.ControllerEnabled?.Value ?? false)) return;

            string modifier = BoundControllerAction(InventoryConfig.ControllerModifier.Value);
            string quick1 = BoundControllerAction(InventoryConfig.ControllerQuick1.Value);
            string quick2 = BoundControllerAction(InventoryConfig.ControllerQuick2.Value);
            string quick3 = BoundControllerAction(InventoryConfig.ControllerQuick3.Value);
            string sort = BoundControllerAction(InventoryConfig.ControllerSort.Value);
            string toggleLock = BoundControllerAction(InventoryConfig.ControllerToggleLock.Value);
            quick1 = ControllerBindingPolicy.EffectiveQuick1Action(
                modifier, quick1, quick2, quick3, sort, toggleLock, out _);
            RegisterController("controller-quick-1", "Use quick slot 1", quick1, modifier, "gameplay");
            RegisterController("controller-quick-2", "Use quick slot 2", quick2, modifier, "gameplay");
            RegisterController("controller-quick-3", "Use quick slot 3", quick3, modifier, "gameplay");
            RegisterController("controller-sort", "Sort selected safe inventory rows", sort, modifier, "inventory");
            RegisterController("controller-lock", "Toggle focused inventory slot lock", toggleLock, modifier, "inventory");
        }

        private void RefreshBindings()
        {
            DisposeBindings();
            try { RegisterBindings(); }
            catch
            {
                DisposeBindings();
                throw;
            }
        }

        private void DisposeBindings()
        {
            for (int index = _bindings.Count - 1; index >= 0; index--)
            {
                try { _bindings[index].Dispose(); }
                catch (Exception exception)
                {
                    Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_bee5ee3f3cbd"));
                }
            }
            _bindings.Clear();
        }

        private void RegisterKeyboard(
            string id,
            string display,
            KeyboardShortcut shortcut,
            string context)
        {
            if (shortcut.MainKey == KeyCode.None) return;
            var modifiers = new List<string>();
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifiers.Count >= 8)
                    throw new InvalidOperationException(
                        "A keyboard chord may contain at most eight modifiers.");
                modifiers.Add(modifier.ToString());
            }
            _bindings.Add(_keybindings.Register(new KeybindingDescriptor(
                ModuleId,
                id,
                display,
                new InputChord("keyboard", shortcut.MainKey.ToString(), modifiers),
                context)));
        }

        private void RegisterController(
            string id,
            string display,
            string action,
            string modifier,
            string context)
        {
            if (action.Length == 0 || modifier.Length == 0) return;
            try
            {
                _bindings.Add(_keybindings.Register(new KeybindingDescriptor(
                    ModuleId,
                    id,
                    display,
                    new InputChord("controller", action, new[] { modifier }),
                    context)));
            }
            catch (ArgumentException)
            {
                Diagnostics.Warn("Inventory skipped invalid controller route: " + id + ".");
            }
        }

        private static string BoundControllerAction(string value)
        {
            string raw = value ?? string.Empty;
            if (raw.Length == 0 || raw.Length > 64) return string.Empty;
            string action = raw.Trim();
            if (!action.StartsWith("Joy", StringComparison.Ordinal)) return string.Empty;
            for (int index = 0; index < action.Length; index++)
                if (!(char.IsLetterOrDigit(action[index]) || action[index] == '_' ||
                      action[index] == '-')) return string.Empty;
            return action;
        }

        private void ShutdownRuntime()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            RuntimeReady = false;
            InventoryRuntime runtime = _runtime;
            InventoryIntegrationApi.Detach(runtime);
            if (_configurationSubscribed)
            {
                Config.SettingChanged -= OnSettingChanged;
                _configurationSubscribed = false;
            }
            if (_inputLayoutSubscribed)
            {
                ZInput.OnInputLayoutChanged -= OnInputLayoutChanged;
                _inputLayoutSubscribed = false;
            }
            ControllerChordSession.Reset();
            DisposeBindings();
            try { runtime?.Dispose(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_bf7b364b87ab"));
            }
            _runtime = null;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_749140dc00e4"));
            }
            _harmony = null;
            BetterArcheryCompatibility.Reset();
        }
    }
}
