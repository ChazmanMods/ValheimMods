using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Runic.Foundation.Core;

namespace Runic.Foundation.Persistence
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RunicCoreGuid, "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "chazman.RunicPersistence";
        public const string RunicCoreGuid = "chazman.RunicCore";
        public const string ModuleId = "runic.persistence";
        public const string PluginName = "Runic Persistence";
        public const string PluginVersion = "1.0.0";
        public const string ProtocolVersion = "1.0";

        private ModuleRegistration _moduleRegistration;
        private ServiceRegistration _migrationRegistration;
        private ServiceRegistration _protocolRegistration;
        private ServiceRegistration _rpcRegistration;
        private ServiceRegistration _peerAdmissionRegistration;
        private IDisposable _bindingResolverRegistration;
        private IDisposable _bindingRpcRegistration;
        private IDisposable _coreRequirement;
        private IDisposable _persistenceRequirement;
        private PlayerIdentityBindingStore _bindingStore;
        private Harmony _harmony;

        internal static ManualLogSource Log { get; private set; }

        public static PersistenceService Service { get; private set; }
        public static RunicRpcService Rpc { get; private set; }

        private void Awake()
        {
            Log = Logger;
            if (!RunicCoreApi.IsPluginLoaded)
            {
                Logger.LogError(
                    "Runic Persistence did not register because Runic Core 1.0.0+ did not " +
                    "finish loading. Repair or update Runic Core, then restart.");
                return;
            }

            Runic.Foundation.Core.ProtocolVersion persistenceProtocol =
                Runic.Foundation.Core.ProtocolVersion.Parse(ProtocolVersion);
            if (!persistenceProtocol.IsCompatibleWith(RunicCoreApi.ProtocolVersion))
            {
                Logger.LogError(
                    $"Runic Persistence protocol {ProtocolVersion} is incompatible with " +
                    $"Runic Core protocol {RunicCoreApi.ProtocolVersion}. No services were registered.");
                return;
            }

            try
            {
                Service = new PersistenceService();
                Rpc = new RunicRpcService(Logger);
                _bindingStore = PlayerIdentityBindingRuntime.Create(Logger);
                var descriptor = new ModuleDescriptor(
                    ModuleId,
                    PluginName,
                    PluginVersion,
                    ProtocolVersion,
                    new[]
                    {
                        RunicCapabilityIds.PersistenceMigrate,
                        RunicCapabilityIds.NetworkProtocol,
                        RunicCapabilityIds.NetworkRpc,
                        RunicCapabilityIds.NetworkPeerAdmission,
                        RunicCapabilityIds.ActorIdentityBinding
                    });

                _moduleRegistration = RunicRegistry.Shared.RegisterModule(descriptor);
                _migrationRegistration = RunicRegistry.Shared.RegisterService<IMigrationService>(
                    RunicCapabilityIds.PersistenceMigrate,
                    ModuleId,
                    Service);
                _protocolRegistration = RunicRegistry.Shared.RegisterService<IProtocolNegotiationService>(
                    RunicCapabilityIds.NetworkProtocol,
                    ModuleId,
                    Service);
                _rpcRegistration = RunicRegistry.Shared.RegisterService<IRunicRpcService>(
                    RunicCapabilityIds.NetworkRpc,
                    ModuleId,
                    Rpc);
                _peerAdmissionRegistration =
                    RunicRegistry.Shared.RegisterService<IRunicRpcPeerAdmissionService>(
                        RunicCapabilityIds.NetworkPeerAdmission,
                        ModuleId,
                        Rpc);
                _bindingResolverRegistration = Rpc.RegisterPlayerBindingResolver(
                    _moduleRegistration,
                    _bindingStore);
                _bindingRpcRegistration = PlayerIdentityBindingCommands.ConfigureRpc(
                    _moduleRegistration,
                    Rpc,
                    _bindingStore,
                    Logger);
                _coreRequirement = Rpc.RegisterPeerRequirement(
                    _moduleRegistration,
                    new RpcPeerRequirement(
                        RunicModuleIds.Core,
                        RunicCoreMetadata.SemanticVersion.ToString(),
                        RunicCoreMetadata.SemanticVersion.ToString(),
                        RunicCoreMetadata.ProtocolVersion.Major,
                        RunicCoreMetadata.ProtocolVersion.Major));
                _persistenceRequirement = Rpc.RegisterPeerRequirement(
                    _moduleRegistration,
                    new RpcPeerRequirement(
                        ModuleId,
                        PluginVersion,
                        PluginVersion,
                        persistenceProtocol.Major,
                        persistenceProtocol.Major));
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(TransportPatches));
                _bindingStore.Tick();
                PlayerIdentityBindingCommands.Attach(
                    _bindingStore,
                    Rpc,
                    _moduleRegistration,
                    Logger);
            }
            catch (Exception exception)
            {
                _harmony?.UnpatchSelf();
                _harmony = null;
                PlayerIdentityBindingCommands.Detach(_bindingStore);
                Rpc?.Dispose();
                DisposeRegistrations();
                _bindingStore?.Dispose();
                _bindingStore = null;
                Service = null;
                Rpc = null;
                Logger.LogError(
                    "Runic Persistence failed closed during service registration. " +
                    "No migration or protocol capability was published. " +
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            Logger.LogInfo(
                $"{PluginName} v{PluginVersion} registered migration and protocol services " +
                $"plus bounded direct-peer RPC at protocol {ProtocolVersion}.");
        }

        private void Update()
        {
            _bindingStore?.Tick();
            Rpc?.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            PlayerIdentityBindingCommands.Detach(_bindingStore);
            Rpc?.Dispose();
            DisposeRegistrations();
            _bindingStore?.Dispose();
            _bindingStore = null;
            Service = null;
            Rpc = null;
            Log = null;
        }

        private void DisposeRegistrations()
        {
            _bindingRpcRegistration?.Dispose();
            _bindingRpcRegistration = null;
            _bindingResolverRegistration?.Dispose();
            _bindingResolverRegistration = null;
            _persistenceRequirement?.Dispose();
            _persistenceRequirement = null;
            _coreRequirement?.Dispose();
            _coreRequirement = null;
            _rpcRegistration?.Dispose();
            _rpcRegistration = null;
            _peerAdmissionRegistration?.Dispose();
            _peerAdmissionRegistration = null;
            _protocolRegistration?.Dispose();
            _protocolRegistration = null;
            _migrationRegistration?.Dispose();
            _migrationRegistration = null;
            _moduleRegistration?.Dispose();
            _moduleRegistration = null;
        }
    }
}
