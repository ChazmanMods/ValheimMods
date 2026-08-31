using System;
using BepInEx;
using HarmonyLib;
using RunicSafety.Api;
using RunicSafety.Integration;
using RunicSafety.Services;

namespace RunicSafety
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicSafety";
        public const string Name = "Runic Safety";
        public const string Version = "1.0.0";
        public const string ModuleId = "runic.safety";
        public const string ProtocolVersion = "1.0";

        private Harmony _harmony;
        private CorrelatedDiagnosticBuffer _diagnostics;
        private bool _configurationSubscribed;
        private bool _shuttingDown;

        internal static bool RuntimeReady { get; private set; }
        internal static SafetyRuntime CurrentRuntime { get; private set; }

        private void Awake()
        {
            SafetyConfig.Bind(Config);
            SafetyConfig.Changed += OnConfigurationChanged;
            _configurationSubscribed = true;
            _diagnostics = new CorrelatedDiagnosticBuffer(log: Logger);

            try
            {
                if (!ValheimContracts.Initialize(out string targetProblem))
                    throw new MissingMethodException(targetProblem);

                CurrentRuntime = new SafetyRuntime(_diagnostics);
                CurrentRuntime.Initialize();
                SafetyIntegrationApi.Attach(CurrentRuntime);

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                RuntimeReady = true;

                Logger.LogInfo(
                    Name + " v" + Version + " ready for Valheim " +
                    ValheimContracts.AuditedGameVersion + ". Confirmations, protected destinations, " +
                    "vanilla tombstone audits, and migration backups are standalone.");
            }
            catch (Exception exception)
            {
                Logger.LogError(
                    Name + " startup failed closed; all patched actions remain vanilla. " +
                    exception.GetType().Name + ": " + exception.Message);
                ShutdownRuntime();
            }
        }

        private void OnConfigurationChanged()
        {
            if (!RuntimeReady || CurrentRuntime == null) return;
            try { CurrentRuntime.OnConfigurationChanged(); }
            catch (Exception exception)
            {
                Logger.LogError(
                    "Safety configuration refresh failed; the next action will use bounded defaults. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            ShutdownRuntime();
            if (_configurationSubscribed)
            {
                SafetyConfig.Changed -= OnConfigurationChanged;
                _configurationSubscribed = false;
            }
            SafetyConfig.Unbind();
            _diagnostics?.SetLog(null);
            _diagnostics = null;
        }

        private void ShutdownRuntime()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            RuntimeReady = false;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Safety patch cleanup failed: " + exception.Message);
            }
            _harmony = null;
            SafetyRuntime runtime = CurrentRuntime;
            SafetyIntegrationApi.Detach(runtime);
            try { runtime?.Shutdown(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Safety runtime cleanup failed: " + exception.Message);
            }
            CurrentRuntime = null;
            _shuttingDown = false;
        }
    }
}
