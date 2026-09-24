using System;
using System.Reflection;
using System.Threading;
using BepInEx;
using HarmonyLib;
using RunicSentinel.Api;
using RunicSentinel.Core;
using RunicSentinel.Runtime;

namespace RunicSentinel
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility("server_devcommands")]
    [BepInDependency("chazman.RunicSafety", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("chazman.RunicWorldEngine", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("chazman.RunicSentinel")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicSentinelServer";
        public const string Name = "Runic Sentinel Server";
        public const string Version = SentinelVersion.Current;
        public const string ModuleId = "runic.sentinel.server";

        private static Plugin _instance;

        private SentinelRuntime _runtime;
        private SentinelEnforcementRuntime _enforcement;
        private SentinelOperatorCommands _operatorCommands;
        private SentinelManagedPolicyService _managedPolicy;
        private SentinelAdminControl _adminControl;
        private SentinelFlightRecorder _flightRecorder;
        private Harmony _failClosedHarmony;
        private Harmony _roleHarmony;
        private Harmony _authorityHarmony;
        private ZNet _authorityNetwork;
        private ZNet _destroyedAuthorityNetwork;
        private int _refreshRequested;
        private bool _authorityStarted;
        private bool _activationFailed;
        private bool _configuredRoleKnown;
        private bool _serverRoleConfigured;
        private bool _unavailableGateLogged;
        private bool _clientNoticeLogged;

        private void Awake()
        {
            SentinelConfig.Bind(Config);
            if (!(SentinelConfig.Enabled?.Value ?? true))
            {
                Logger.LogInfo(Name + " is disabled; no worker or network handlers were created.");
                return;
            }

            _instance = this;
            try
            {
                InstallPermanentFailClosedGate();
            }
            catch (Exception exception)
            {
                _activationFailed = true;
                Shutdown();
                Logger.LogError(
                    Name + " could not install its Required-mode safety gate and is inactive: " +
                    exception);
                return;
            }

            try
            {
                InstallRoleObservers();
                TryActivateAuthority(ZNet.instance);
                Logger.LogInfo(
                    Name + " v" + Version +
                    " installed. Authority services remain inert until Valheim selects a " +
                    "dedicated server or listen host role.");
            }
            catch (Exception exception)
            {
                _activationFailed = true;
                ShutdownAuthority();
                try { _roleHarmony?.UnpatchSelf(); }
                catch { }
                _roleHarmony = null;
                Logger.LogError(
                    Name + " role bootstrap failed. Required mode remains fail closed through " +
                    "the permanent world-load and connection gates: " + exception);
            }
        }

        private void InstallPermanentFailClosedGate()
        {
            MethodInfo serverHandshake = RequireInstanceVoid(
                "RPC_ServerHandshake", typeof(ZRpc), typeof(string));
            MethodInfo loadWorld = RequireInstanceVoid("ServerLoadWorld");
            _failClosedHarmony = new Harmony(Guid + ".fail-closed");
            try
            {
                _failClosedHarmony.Patch(
                    serverHandshake,
                    prefix: new HarmonyMethod(
                        typeof(SentinelServerRoleBootstrap),
                        nameof(SentinelServerRoleBootstrap.BeforeUnavailableServerHandshake)));
                _failClosedHarmony.Patch(
                    loadWorld,
                    prefix: new HarmonyMethod(
                        typeof(SentinelServerRoleBootstrap),
                        nameof(SentinelServerRoleBootstrap.BeforeUnavailableWorldLoad)));
            }
            catch
            {
                try { _failClosedHarmony.UnpatchSelf(); }
                catch { }
                _failClosedHarmony = null;
                throw;
            }
        }

        private void InstallRoleObservers()
        {
            MethodInfo setServer = AccessTools.DeclaredMethod(
                typeof(ZNet),
                "SetServer",
                new[]
                {
                    typeof(bool), typeof(bool), typeof(bool), typeof(string),
                    typeof(string), typeof(World)
                });
            if (setServer == null || !setServer.IsStatic ||
                setServer.ReturnType != typeof(void))
                throw new MissingMethodException(typeof(ZNet).FullName, "SetServer");
            MethodInfo znetAwake = RequireInstanceVoid("Awake");
            MethodInfo onNewConnection = RequireInstanceVoid(
                "OnNewConnection", typeof(ZNetPeer));
            MethodInfo znetDestroy = RequireInstanceVoid("OnDestroy");

            _roleHarmony = new Harmony(Guid + ".authority-role");
            _roleHarmony.Patch(
                setServer,
                postfix: new HarmonyMethod(
                    typeof(SentinelServerRoleBootstrap),
                    nameof(SentinelServerRoleBootstrap.AfterSetServer)));
            _roleHarmony.Patch(
                znetAwake,
                postfix: new HarmonyMethod(
                    typeof(SentinelServerRoleBootstrap),
                    nameof(SentinelServerRoleBootstrap.AfterZNetAwake)));
            _roleHarmony.Patch(
                onNewConnection,
                postfix: new HarmonyMethod(
                    typeof(SentinelServerRoleBootstrap),
                    nameof(SentinelServerRoleBootstrap.AfterNewConnection)));
            _roleHarmony.Patch(
                znetDestroy,
                postfix: new HarmonyMethod(
                    typeof(SentinelServerRoleBootstrap),
                    nameof(SentinelServerRoleBootstrap.AfterZNetDestroy)));
        }

        private static MethodInfo RequireInstanceVoid(
            string name,
            params Type[] parameters)
        {
            MethodInfo method = AccessTools.DeclaredMethod(
                typeof(ZNet), name, parameters ?? Type.EmptyTypes);
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                throw new MissingMethodException(typeof(ZNet).FullName, name);
            return method;
        }

        internal static void ObserveConfiguredRole(bool server)
        {
            Plugin instance = _instance;
            if (instance == null) return;
            instance._configuredRoleKnown = true;
            instance._serverRoleConfigured = server;
            instance._destroyedAuthorityNetwork = null;
            if (server)
            {
                // SetServer runs before the authoritative ZNet necessarily exists. Prepare the
                // policy/runtime now, then bind the exact instance in Awake or the first update.
                instance.TryActivateAuthority(null);
                return;
            }

            instance.ShutdownAuthority();
            instance.LogClientInertOnce();
        }

        internal static void ObserveNetwork(ZNet network)
        {
            Plugin instance = _instance;
            instance?.TryActivateAuthority(network);
        }

        internal static void ObserveConnection(ZNet network, ZNetPeer peer)
        {
            Plugin instance = _instance;
            if (instance == null) return;
            instance.TryActivateAuthority(network);
            if (instance._authorityStarted && network != null && network.IsServer())
            {
                instance._authorityNetwork = network;
                SentinelNetworkCompatibility.ObserveConnection(network, peer);
            }
            else
            {
                instance.BlockUnavailableConnection(network, peer, null);
            }
        }

        internal static void ObserveNetworkDestroyed(ZNet network)
        {
            Plugin instance = _instance;
            if (instance != null && instance._authorityStarted &&
                ReferenceEquals(network, instance._authorityNetwork))
            {
                instance._destroyedAuthorityNetwork = network;
                instance.ShutdownAuthority();
            }
        }

        internal static bool AllowUnavailableServerHandshake(ZNet network, ZRpc rpc)
        {
            Plugin instance = _instance;
            return instance == null ||
                   !instance.BlockUnavailableConnection(network, null, rpc);
        }

        internal static void GuardUnavailableWorldLoad(ZNet network)
        {
            Plugin instance = _instance;
            if (instance == null || !instance.ShouldBlockUnavailable(network)) return;
            instance.LogUnavailableGateOnce();
            throw new InvalidOperationException(
                "Runic Sentinel Server Required authority is unavailable; world load was blocked.");
        }

        private void TryActivateAuthority(ZNet network)
        {
            if (_authorityStarted)
            {
                if (network != null && network.IsServer())
                {
                    _authorityNetwork = network;
                    _destroyedAuthorityNetwork = null;
                }
                return;
            }
            if (_activationFailed || ReferenceEquals(network, _destroyedAuthorityNetwork)) return;

            bool authoritative = _configuredRoleKnown && _serverRoleConfigured;
            if (network != null)
            {
                try { authoritative |= network.IsServer(); }
                catch { }
            }
            if (!authoritative)
            {
                if (network != null || _configuredRoleKnown) LogClientInertOnce();
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
                    _runtime,
                    Logger,
                    Paths.ConfigPath,
                    reason => SentinelTransitionBackup.CreateVerifiedBackupNow(reason));
                _operatorCommands = new SentinelOperatorCommands(
                    _runtime,
                    Logger,
                    Paths.ConfigPath,
                    _managedPolicy);
                _adminControl = new SentinelAdminControl(
                    _runtime,
                    _managedPolicy,
                    _operatorCommands);
                _authorityHarmony = new Harmony(Guid + ".authority");
                RunicSentinel.Devcommands.Engine.Init(Config,Logger);
                _authorityHarmony.PatchAll(typeof(Plugin).Assembly);
                SentinelConfig.Changed += Refresh;
                _runtime.Start(Paths.ConfigPath);
                _authorityNetwork = network != null && network.IsServer() ? network : null;
                _authorityStarted = true;
                Logger.LogInfo(
                    Name + " authority services started. Required admission now gates the exact " +
                    "direct connection and the dedicated console/admin backend is available.");
            }
            catch (Exception exception)
            {
                _activationFailed = true;
                ShutdownAuthority();
                Logger.LogError(Name + " authority startup failed closed: " + exception);
            }
        }

        internal static bool UnavailableAuthorityFailsClosed(
            SentinelRemoteAdmissionMode mode,
            bool authoritative,
            bool authorityStarted) =>
            authoritative && !authorityStarted &&
            mode == SentinelRemoteAdmissionMode.Required;

        private bool ShouldBlockUnavailable(ZNet network)
        {
            bool authoritative = false;
            try { authoritative = network != null && network.IsServer(); }
            catch { }
            return UnavailableAuthorityFailsClosed(
                SentinelConfig.RemoteAdmissionMode,
                authoritative,
                _authorityStarted);
        }

        private bool BlockUnavailableConnection(
            ZNet network,
            ZNetPeer peer,
            ZRpc rpc)
        {
            if (!ShouldBlockUnavailable(network)) return false;
            LogUnavailableGateOnce();
            try
            {
                ZNetPeer exact = peer;
                if (exact == null && rpc != null)
                {
                    foreach (ZNetPeer candidate in network.GetPeers())
                    {
                        if (candidate != null && ReferenceEquals(candidate.m_rpc, rpc))
                        {
                            exact = candidate;
                            break;
                        }
                    }
                }
                if (exact != null) network.Disconnect(exact);
            }
            catch { }
            return true;
        }

        private void LogUnavailableGateOnce()
        {
            if (_unavailableGateLogged) return;
            _unavailableGateLogged = true;
            Logger.LogError(
                Name + " Required authority is unavailable. World loading and native client " +
                "admission remain blocked until the server is restarted successfully.");
        }

        private void LogClientInertOnce()
        {
            if (_clientNoticeLogged) return;
            _clientNoticeLogged = true;
            Logger.LogInfo(
                Name + " detected a non-authoritative client and remains fully inert. " +
                "Install Runic Sentinel Client in player-only profiles.");
        }

        private void Refresh() => Interlocked.Exchange(ref _refreshRequested, 1);

        private void Update()
        {
            ZNet network = ZNet.instance;
            if (_authorityStarted && network != null && !network.IsServer())
            {
                ShutdownAuthority();
                LogClientInertOnce();
                return;
            }
            if (_authorityStarted && network != null && network.IsServer())
            {
                _authorityNetwork = network;
                _destroyedAuthorityNetwork = null;
            }
            if (!_authorityStarted)
            {
                if (network != null) TryActivateAuthority(network);
                return;
            }
            try { _runtime?.TickNetwork(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel server admission tick stopped safely: " + exception.Message);
            }
            try { _runtime?.TickIntegrity(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel server integrity check failed closed: " + exception.Message);
            }
            try { _adminControl?.Tick(); if(_adminControl!=null)RunicSentinel.Devcommands.Engine.Tick(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel server administrator transport stopped safely: " + exception.Message);
            }
            try { _operatorCommands?.TickDedicatedConsole(); }
            catch (Exception exception)
            {
                Logger.LogWarning("Sentinel server console input stopped safely: " + exception.Message);
            }
            if (Interlocked.Exchange(ref _refreshRequested, 0) == 0 || _runtime == null) return;
            try { _runtime.Start(Paths.ConfigPath); }
            catch (Exception exception)
            {
                Logger.LogError("Sentinel server policy refresh failed closed: " + exception.Message);
            }
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            ShutdownAuthority();
            try { _roleHarmony?.UnpatchSelf(); }
            catch { }
            _roleHarmony = null;
            try { _failClosedHarmony?.UnpatchSelf(); }
            catch { }
            _failClosedHarmony = null;
        }

        private void ShutdownAuthority()
        {
            try { RunicSentinel.Devcommands.Engine.Stop(); } catch { }
            SentinelConfig.Changed -= Refresh;
            Interlocked.Exchange(ref _refreshRequested, 0);
            _authorityStarted = false;
            _authorityNetwork = null;
            try { _authorityHarmony?.UnpatchSelf(); }
            catch { }
            _authorityHarmony = null;
            SentinelSteamSessions.Reset();
            try { _adminControl?.Dispose(); }
            catch { }
            _adminControl = null;
            SentinelTransitionBackup.Detach();
            try { _operatorCommands?.Dispose(); }
            catch { }
            _operatorCommands = null;
            _managedPolicy = null;
            try { _flightRecorder?.Dispose(); }
            catch { }
            _flightRecorder = null;
            SentinelIntegrationApi.Detach(_enforcement);
            try { _enforcement?.Dispose(); }
            catch { }
            _enforcement = null;
            try { _runtime?.Dispose(); }
            catch { }
            _runtime = null;
        }
    }

    internal static class SentinelServerRoleBootstrap
    {
        internal static void AfterSetServer(
            [HarmonyArgument(0)] bool server) =>
            Plugin.ObserveConfiguredRole(server);

        internal static void AfterZNetAwake(ZNet __instance) =>
            Plugin.ObserveNetwork(__instance);

        internal static void AfterNewConnection(
            ZNet __instance,
            [HarmonyArgument(0)] ZNetPeer peer) =>
            Plugin.ObserveConnection(__instance, peer);

        internal static void AfterZNetDestroy(ZNet __instance) =>
            Plugin.ObserveNetworkDestroyed(__instance);

        [HarmonyPriority(Priority.First)]
        internal static bool BeforeUnavailableServerHandshake(
            ZNet __instance,
            [HarmonyArgument(0)] ZRpc rpc) =>
            Plugin.AllowUnavailableServerHandshake(__instance, rpc);

        [HarmonyPriority(Priority.First)]
        internal static void BeforeUnavailableWorldLoad(ZNet __instance) =>
            Plugin.GuardUnavailableWorldLoad(__instance);
    }
}
