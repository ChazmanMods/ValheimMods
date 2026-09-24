using System;
using System.Threading;
using BepInEx;
using HarmonyLib;
using RunicSentinel.Core;
using RunicSentinel.Runtime;
using RunicSentinel.Api;

namespace RunicSentinel
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility("server_devcommands")]
    [BepInDependency("chazman.RunicSafety", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("chazman.RunicWorldEngine", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("chazman.RunicSentinelServer")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicSentinel";
        public const string Name = "Runic Sentinel";
        public const string Version = SentinelVersion.Current;
        public const string ModuleId = "runic.sentinel";
        private SentinelRuntime _runtime;
        private SentinelEnforcementRuntime _enforcement;
        private SentinelOperatorCommands _operatorCommands;
        private SentinelManagedPolicyService _managedPolicy;
        private SentinelAdminControl _adminControl;
        private SentinelAdminPanel _adminPanel;
        private SentinelFlightRecorder _flightRecorder;
        private Harmony _harmony;
        private int _refreshRequested;
        private bool _runtimeStarted;

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
                _flightRecorder = new SentinelFlightRecorder(
                    _runtime.Evidence,
                    Logger,
                    Paths.ConfigPath);
                _runtime.AttachNetwork(SentinelConfig.RemoteAdmissionMode);
                _enforcement = new SentinelEnforcementRuntime(_runtime);
                SentinelIntegrationApi.Attach(_enforcement);
                SentinelTransitionBackup.Attach(_runtime, Logger, Paths.ConfigPath);
                _managedPolicy = new SentinelManagedPolicyService(
                    _runtime, Logger, Paths.ConfigPath,
                    reason => SentinelTransitionBackup.CreateVerifiedBackupNow(reason));
                _operatorCommands = new SentinelOperatorCommands(
                    _runtime, Logger, Paths.ConfigPath, _managedPolicy);
                _adminControl = new SentinelAdminControl(
                    _runtime, _managedPolicy, _operatorCommands);
                _adminPanel = new SentinelAdminPanel(_adminControl);
                _harmony = new Harmony(Guid);
                RunicSentinel.Devcommands.Engine.Init(Config,Logger);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                SentinelConfig.Changed += Refresh;
                Logger.LogInfo(Name + " v" + Version + " initialized as a standalone plugin. " +
                    "Signed passports enforce exact plugin profiles, administrators, and banned accounts; " +
                    "Required admission gates the exact direct Valheim connection before its native " +
                    "admission. The complete plugin snapshot starts after chainloader initialization.");
            }
            catch (Exception exception) { Shutdown(); Logger.LogError(Name + " failed closed: " + exception); }
        }

        private void Start()
        {
            if (_runtime == null || _runtimeStarted) return;
            try
            {
                _runtime.Start(Paths.ConfigPath);
                _runtimeStarted = true;
            }
            catch (Exception exception)
            {
                Shutdown();
                Logger.LogError(Name + " snapshot startup failed closed: " + exception);
            }
        }

        private void Refresh() => Interlocked.Exchange(ref _refreshRequested, 1);

        private void Update()
        {
            try { _runtime?.TickNetwork(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel network request stopped: " + exception.Message);
            }
            try { _runtime?.TickIntegrity(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel runtime-integrity check failed closed: " + exception.Message);
            }
            try { _adminControl?.Tick(); if(_adminControl!=null)RunicSentinel.Devcommands.Engine.Tick(); }
            catch (Exception exception) { Logger.LogWarning("Sentinel admin transport stopped safely: " + exception.Message); }
            try { _adminPanel?.Tick(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel administrator panel stopped safely: " + exception.Message);
            }
            try { _operatorCommands?.TickDedicatedConsole(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel dedicated console input stopped safely: " + exception.Message);
            }
            if (Interlocked.Exchange(ref _refreshRequested, 0) == 0 || _runtime == null) return;
            try
            {
                _runtime.Start(Paths.ConfigPath);
            }
            catch (Exception exception)
            {
                Logger.LogError("Sentinel policy refresh failed closed: " + exception.Message);
            }
        }
        private void OnGUI()
        {
            try { _adminPanel?.Draw(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel administrator panel draw failed safely: " + exception.Message);
            }
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            try { RunicSentinel.Devcommands.Engine.Stop(); } catch { }
            SentinelConfig.Changed -= Refresh;
            Interlocked.Exchange(ref _refreshRequested, 0);
            _runtimeStarted = false;
            try { _harmony?.UnpatchSelf(); } catch { }
            _harmony = null;
            SentinelSteamSessions.Reset();
            try { _adminPanel?.Dispose(); } catch { }
            _adminPanel = null;
            try { _adminControl?.Dispose(); } catch { }
            _adminControl = null;
            SentinelTransitionBackup.Detach();
            try { _operatorCommands?.Dispose(); } catch { }
            _operatorCommands = null;
            _managedPolicy = null;
            try { _flightRecorder?.Dispose(); } catch { }
            _flightRecorder = null;
            SentinelIntegrationApi.Detach(_enforcement);
            try { _enforcement?.Dispose(); } catch { }
            _enforcement = null;
            try { _runtime?.Dispose(); } catch { }
            _runtime = null;
        }
    }
}
