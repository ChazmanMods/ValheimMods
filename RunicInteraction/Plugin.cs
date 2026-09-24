using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using RunicInteraction.Core;
using RunicInteraction.Integration;

namespace RunicInteraction
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicInteraction";
        public const string Name = "Runic Interaction";
        public const string Version = "1.0.10";
        public const string ModuleId = "runic.interaction";
        private readonly List<KeybindingDescriptor> _keybindings =
            new List<KeybindingDescriptor>();
        private Harmony _harmony;
        private InteractionRuntime _runtime;
        private bool _configurationSubscribed;
        private bool _shuttingDown;

        internal static bool RuntimeReady { get; private set; }

        private void Awake()
        {
            Diagnostics.Initialize(Logger);
            InteractionConfig.Bind(Config);
            InteractionConfig.Changed += OnConfigurationChanged;
            _configurationSubscribed = true;

            try
            {
                if (!ValheimAccess.Initialize(out string targetProblem))
                    throw new MissingMethodException(targetProblem);

                _runtime = new InteractionRuntime();
                _runtime.Initialize();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);

                DoorAutoCloseRuntime.Initialize();
                RegisterKeybindings();
                RuntimeReady = true;

                Logger.LogInfo(
                    Name + " v" + Version + " ready for Valheim " +
                    ValheimAccess.ReadGameVersion() + ". Hold-repeat uses " +
                    "Valheim's 0.2 s cadence and every repeated item still passes the original station callback.");
                Logger.LogInfo(
                    "Door auto-close is off by default. Drag-sweep and generic filter memory are explicit " +
                    "disabled gates; ordinary vanilla dragging and menu behavior remain available.");
            }
            catch (Exception exception)
            {
                Logger.LogError(
                    Name + " startup failed closed; all interactions remain vanilla. " +
                    exception.GetType().Name + ": " + exception.Message);
                ShutdownRuntime();
            }
        }

        private void Update()
        {
            if (!RuntimeReady || _runtime == null) return;
            try { _runtime.Tick(); }
            catch (Exception exception)
            {
                Logger.LogError(
                    Name + " runtime faulted and was disabled for this session; vanilla behavior was restored. " +
                    exception.GetType().Name + ": " + exception.Message);
                ShutdownRuntime();
            }
        }

        private void OnConfigurationChanged(BepInEx.Configuration.ConfigDefinition definition)
        {
            if (!RuntimeReady || _runtime == null) return;
            try
            {
                if (definition == null ||
                    definition.Section == "Controller" &&
                    definition.Key == "PickupBypassModifierAction")
                    RefreshKeybindings();
                _runtime.OnConfigurationChanged();
                Diagnostics.Info("Interaction configuration refreshed; no restart is required.");
            }
            catch (Exception exception)
            {
                Diagnostics.Error(exception, global::Runic.Localization.RunicText.Get("text_5d38524af925"));
            }
        }

        private void OnDestroy()
        {
            ShutdownRuntime();
            Diagnostics.Initialize(null);
        }

        private void RegisterKeybindings()
        {
            ReplaceKeybindings(InteractionInputBindings.CreateDescriptors(
                InteractionConfig.PickupBypassControllerModifier.Value));
        }

        private void RefreshKeybindings()
        {
            IReadOnlyList<KeybindingDescriptor> next = InteractionInputBindings.CreateDescriptors(
                InteractionConfig.PickupBypassControllerModifier.Value);
            if (BindingsMatch(next)) return;
            ReplaceKeybindings(next);
        }

        private bool BindingsMatch(IReadOnlyList<KeybindingDescriptor> candidates)
        {
            if (candidates == null || candidates.Count != _keybindings.Count) return false;
            for (int index = 0; index < candidates.Count; index++)
            {
                KeybindingDescriptor active = _keybindings[index];
                KeybindingDescriptor candidate = candidates[index];
                if (!string.Equals(active.QualifiedId, candidate.QualifiedId, StringComparison.Ordinal) ||
                    !string.Equals(active.Context, candidate.Context, StringComparison.Ordinal) ||
                    !active.Chord.Equals(candidate.Chord)) return false;
            }
            return true;
        }

        private void ReplaceKeybindings(IReadOnlyList<KeybindingDescriptor> replacements)
        {
            if (replacements == null || replacements.Count != InteractionInputBindings.BindingCount)
                throw new InvalidOperationException("Interaction keybinding catalog is incomplete.");
            var next = new List<KeybindingDescriptor>(replacements.Count);
            for (int index = 0; index < replacements.Count; index++)
                next.Add(replacements[index] ??
                         throw new InvalidOperationException(
                             "Interaction keybinding catalog contains an empty entry."));
            _keybindings.Clear();
            _keybindings.AddRange(next);
        }

        private void DisposeKeybindings()
        {
            _keybindings.Clear();
        }

        private void ShutdownRuntime()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            RuntimeReady = false;
            if (_configurationSubscribed)
            {
                InteractionConfig.Changed -= OnConfigurationChanged;
                _configurationSubscribed = false;
            }
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Diagnostics.Warn("Could not remove every interaction patch: " + exception.Message);
            }
            _harmony = null;
            try { _runtime?.Shutdown(); }
            catch (Exception exception)
            {
                Diagnostics.Warn("Runtime cleanup was incomplete: " + exception.Message);
            }
            _runtime = null;

            DisposeKeybindings();
            _shuttingDown = false;
        }
    }
}
