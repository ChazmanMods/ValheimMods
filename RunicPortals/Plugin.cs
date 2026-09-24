using System;
using BepInEx;
using HarmonyLib;
using RunicPortals.Api;
using RunicPortals.Core;
using RunicPortals.Integration;
using UnityEngine;

namespace RunicPortals
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicPortals";
        public const string Name = "Runic Portals";
        public const string Version = "1.2.10";

        private Harmony _harmony;
        private CorrelatedDiagnosticBuffer _diagnostics;
        private PortalGroupRuntime _groups;
        private bool _configurationSubscribed;
        private bool _shuttingDown;

        internal static bool RuntimeReady { get; private set; }
        internal static PortalRuntime CurrentRuntime { get; private set; }

        internal static void DisableAfterPatchFault(Exception exception, string context)
        {
            if (!RuntimeReady) return;
            RuntimeReady = false;
            Diagnostics.Error(exception,
                global::Runic.Localization.RunicText.Get("text_11ba77847647") + context + global::Runic.Localization.RunicText.Get("text_f6268d75e29f"));
            try { CurrentRuntime?.Shutdown(); }
            catch (Exception cleanup)
            {
                Diagnostics.Error(cleanup, global::Runic.Localization.RunicText.Get("text_3b6ad9d7470b"));
            }
        }

        private void Awake()
        {
            Diagnostics.Initialize(Logger);
            PortalConfig.Bind(Config);
            PortalConfig.Changed += OnConfigurationChanged;
            _configurationSubscribed = true;

            try
            {
                if (!ValheimContracts.Initialize(out string targetProblem))
                    throw new MissingMethodException(targetProblem);

                _diagnostics = new CorrelatedDiagnosticBuffer(PortalContractLimits.MaximumDiagnostics);
                _groups = new PortalGroupRuntime(Logger);
                _groups.Initialize();
                GroupIntegrationApi.Attach(_groups);
                CurrentRuntime = new PortalRuntime(
                    _groups,
                    new PortalAuthorityGate(),
                    _diagnostics,
                    new PortalOverwriteConfirmationGate());
                CurrentRuntime.Initialize();

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                RuntimeReady = true;
                Logger.LogInfo(
                    Name + " v" + Version + " ready for Valheim " +
                    ValheimContracts.ReadGameVersion() +
                    ". Network portals use native local ownership; Group commands use one bounded session channel.");
            }
            catch (Exception exception)
            {
                Logger.LogError(
                    Name + " startup failed closed; vanilla portals remain available. " +
                    exception.GetType().Name + ": " + exception.Message);
                ShutdownRuntime();
            }
        }

        private void Update()
        {
            if (!RuntimeReady || CurrentRuntime == null) return;
            try
            {
                _groups?.Tick();
                CurrentRuntime.Tick();
            }
            catch (Exception exception)
            {
                Logger.LogError(
                    "Runic Portals was disabled for this session after a runtime fault: " +
                    exception.GetType().Name + ": " + exception.Message);
                ShutdownRuntime();
            }
        }

        private void OnGUI()
        {
            if (!RuntimeReady || CurrentRuntime == null || Application.isBatchMode) return;
            try { CurrentRuntime.DrawHoverPanel(); }
            catch (Exception exception) { CurrentRuntime.DisableHoverPanel(exception); }
            try { CurrentRuntime.DrawMapPickerOverlay(); }
            catch (Exception exception) { CurrentRuntime.FailMapPickerUi(exception); }
            try { CurrentRuntime.DrawPortalEditor(); }
            catch (Exception exception)
            {
                Logger.LogError(
                    "Portal editor drawing failed and the runtime was disabled safely: " +
                    exception.GetType().Name + ": " + exception.Message);
                DisableAfterPatchFault(exception, "editor-draw");
            }
        }

        private void OnConfigurationChanged()
        {
            if (!RuntimeReady || CurrentRuntime == null) return;
            try { CurrentRuntime.OnConfigurationChanged(); }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Portal configuration refresh failed; the previous bounded settings remain active. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            ShutdownRuntime();
            if (_configurationSubscribed)
            {
                PortalConfig.Changed -= OnConfigurationChanged;
                _configurationSubscribed = false;
            }
            PortalConfig.Unbind();
            Diagnostics.Initialize(null);
        }

        private void ShutdownRuntime()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            RuntimeReady = false;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception) { Logger.LogWarning("Portal patch cleanup failed: " + exception.Message); }
            _harmony = null;
            try { CurrentRuntime?.Shutdown(); }
            catch (Exception exception) { Logger.LogWarning("Portal runtime cleanup failed: " + exception.Message); }
            CurrentRuntime = null;
            GroupIntegrationApi.Detach(_groups);
            try { _groups?.Dispose(); }
            catch (Exception exception) { Logger.LogWarning("Group runtime cleanup failed: " + exception.Message); }
            _groups = null;
            _diagnostics = null;
            _shuttingDown = false;
        }
    }
}
