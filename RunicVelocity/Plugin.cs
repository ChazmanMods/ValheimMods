using System;
using BepInEx;
using RunicVelocity.Contracts;
using RunicVelocity.Integration;

namespace RunicVelocity
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicVelocity";
        public const string Name = "Runic Velocity";
        public const string Version = "1.0.0";
        private VelocityRuntime _runtime;

        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            VelocityConfig.Bind(Config);
            if (!(VelocityConfig.Enabled?.Value ?? true))
            {
                Logger.LogInfo(Name + " is disabled; no timeline or manifest worker was started.");
                return;
            }
            try
            {
                _runtime = new VelocityRuntime();
                _runtime.Start();
                Logger.LogInfo(Name + " v" + Version + " ready. Manifest hashing runs on a worker and " +
                    "unchanged local files may reuse integrity metadata. Third-party initialization remains untouched.");
            }
            catch (Exception exception)
            {
                Logger.LogError(Name + " failed closed; BepInEx startup remains unchanged. " + exception);
                Shutdown();
            }
        }

        private void Update()
        {
            try { _runtime?.Tick(); }
            catch (Exception exception)
            {
                Logger.LogError("Velocity diagnostics stopped after an unexpected error. " + exception.Message);
                _runtime?.Stop();
                _runtime = null;
            }
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            try { _runtime?.Stop(); } catch { }
            _runtime = null;
        }
    }
}
