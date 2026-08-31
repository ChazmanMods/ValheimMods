using System;
using System.Threading;
using BepInEx;
using RunicSentinel.Contracts;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicSentinel";
        public const string Name = "Runic Sentinel";
        public const string Version = SentinelNetworkCompatibility.PluginVersion;
        private SentinelRuntime _runtime;
        private int _refreshRequested;

        private void Awake()
        {
            SentinelConfig.Bind(Config);
            if (!(SentinelConfig.Enabled?.Value ?? true))
            {
                Logger.LogInfo(Name + " is disabled; no worker or network handlers were created.");
                return;
            }
            try
            {
                _runtime = new SentinelRuntime();
                _runtime.Start(Paths.ConfigPath);
                _runtime.AttachNetwork(SentinelConfig.RemoteAdmissionMode);
                SentinelConfig.Changed += Refresh;
                Logger.LogInfo(Name + " v" + Version + " ready. Local plugin snapshot hashing is bounded/background; " +
                    "policies require a pinned RSA-3072 public key. Private post-connect compatibility admission is " +
                    SentinelConfig.RemoteAdmissionMode.ToString().ToLowerInvariant() + ". Client hash claims remain " +
                    "self-reported compatibility evidence, never cryptographic client attestation.");
            }
            catch (Exception exception) { Shutdown(); Logger.LogError(Name + " failed closed: " + exception); }
        }

        private void Refresh() => Interlocked.Exchange(ref _refreshRequested, 1);

        private void Update()
        {
            try { _runtime?.TickNetwork(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel network request stopped: " + exception.Message);
            }
            if (Interlocked.Exchange(ref _refreshRequested, 0) == 0 || _runtime == null) return;
            try { _runtime.Start(Paths.ConfigPath); }
            catch (Exception exception)
            {
                Logger.LogError("Sentinel policy refresh failed closed: " + exception.Message);
            }
        }
        private void OnDestroy() => Shutdown();
        private void Shutdown()
        {
            SentinelConfig.Changed -= Refresh;
            Interlocked.Exchange(ref _refreshRequested, 0);
            try { _runtime?.Dispose(); } catch { }
            _runtime = null;
        }
    }
}
