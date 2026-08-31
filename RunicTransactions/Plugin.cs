using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicTransactions.Capabilities;
using RunicTransactions.Coordination;
using RunicTransactions.Valheim;
using Runic.Foundation.Transactions;

namespace RunicTransactions
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(CorePluginGuid, "1.0.0")]
    [BepInDependency(PersistencePluginGuid, "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicTransactions";
        public const string Name = "Runic Transactions";
        public const string Version = "1.0.0";
        public const string ModuleId = "runic.transactions";
        public const string ProtocolVersion = "1.0";
        public const string CorePluginGuid = "chazman.RunicCore";
        public const string PersistencePluginGuid = "chazman.RunicPersistence";

        private readonly List<ServiceRegistration> _serviceRegistrations = new List<ServiceRegistration>();
        private ModuleRegistration _moduleRegistration;
        private IDisposable _rpcEndpointRegistration;
        private IDisposable _rpcRequirementRegistration;
        private IDisposable _reconciliationAdmissionRegistration;
        private IDisposable _compositeDomainRegistration;
        private DurableWorldObjectCompositeService _worldObjectService;
        private Harmony _harmony;

        public ITransactionCoordinatorFactory Factory { get; private set; }
        public IDurableCompositeOperationCoordinatorFactory CompositeFactory { get; private set; }
        public IDurableWorldObjectCompositeService WorldObjectFactory => _worldObjectService;

        private void Awake()
        {
            if (!RunicCoreApi.IsPluginLoaded)
            {
                Logger.LogError(
                    "Runic Transactions did not register because Runic Core 1.0.0+ did not " +
                    "finish loading. Repair or update Runic Core, then restart.");
                return;
            }

            Runic.Foundation.Core.ProtocolVersion transactionProtocol =
                Runic.Foundation.Core.ProtocolVersion.Parse(ProtocolVersion);
            if (!transactionProtocol.IsCompatibleWith(RunicCoreApi.ProtocolVersion))
            {
                Logger.LogError(
                    $"Runic Transactions protocol {ProtocolVersion} is incompatible with " +
                    $"Runic Core protocol {RunicCoreApi.ProtocolVersion}. No transaction factory was registered.");
                return;
            }

            try
            {
                DurableContainerSafety.Initialize(Logger);
                _harmony = new Harmony(Guid);
                _harmony.PatchAll();
                Factory = new TransactionCoordinatorFactory();
                var compositeFactory = new DurableCompositeOperationCoordinatorFactory();
                CompositeFactory = compositeFactory;
                var descriptor = new ModuleDescriptor(
                    ModuleId,
                    Name,
                    Version,
                    ProtocolVersion,
                    new[]
                    {
                        RunicCapabilityIds.MaterialsReserve,
                        RunicCapabilityIds.MaterialsConsume,
                        RunicCapabilityIds.ContainerOwnershipReturn,
                        RunicCapabilityIds.DurableCompositeOperations,
                        RunicCapabilityIds.DurableWorldObjectOperations
                    });

                RunicRegistry registry = RunicRegistry.Shared;
                _moduleRegistration = registry.RegisterModule(descriptor);
                RegisterService(registry, RunicCapabilityIds.MaterialsReserve);
                RegisterService(registry, RunicCapabilityIds.MaterialsConsume);
                _serviceRegistrations.Add(registry.RegisterService(
                    RunicCapabilityIds.DurableCompositeOperations,
                    ModuleId,
                    CompositeFactory));
                _compositeDomainRegistration = CompositeFactory.RegisterEndpointDomain(
                    _moduleRegistration,
                    DurableValheimContainerComposite.DomainDescriptor,
                    new ValheimContainerCompositeEndpointStore());
                _worldObjectService = new DurableWorldObjectCompositeService(
                    registry,
                    CompositeFactory,
                    _moduleRegistration);
                _serviceRegistrations.Add(registry.RegisterService(
                    RunicCapabilityIds.DurableWorldObjectOperations,
                    ModuleId,
                    (IDurableWorldObjectCompositeService)_worldObjectService));
                CompositeWorldSaveCheckpoint.Attach(compositeFactory);
                if (!registry.TryGetService<IRunicRpcService>(
                        RunicCapabilityIds.NetworkRpc,
                        out IRunicRpcService rpc))
                    throw new InvalidOperationException(
                        "Runic Persistence did not publish the required direct-peer RPC service.");
                _rpcEndpointRegistration = DurableContainerSafety.ConfigureRpc(
                    _moduleRegistration,
                    rpc);
                _rpcRequirementRegistration = rpc.RegisterPeerRequirement(
                    _moduleRegistration,
                    new RpcPeerRequirement(
                        ModuleId,
                        Version,
                        Version,
                        transactionProtocol.Major,
                        transactionProtocol.Major));
                if (!registry.TryGetService<IRunicRpcPeerAdmissionService>(
                        RunicCapabilityIds.NetworkPeerAdmission,
                        out IRunicRpcPeerAdmissionService peerAdmission))
                    throw new InvalidOperationException(
                        "Runic Persistence did not publish transport-bound peer admission.");
                _reconciliationAdmissionRegistration =
                    peerAdmission.RegisterHandshakePeerEvaluator(
                        _moduleRegistration,
                        ModuleId + ".outstanding-reconciliation",
                        EvaluateOutstandingReconciliation);

                ValidateCapabilityConstants();
                Logger.LogInfo(
                    $"{Name} v{Version} registered protocol {ProtocolVersion}: " +
                    "materials.reserve and materials.consume coordinator factory.");
            }
            catch (Exception exception)
            {
                CompositeWorldSaveCheckpoint.Detach();
                DurableContainerSafety.Shutdown();
                _harmony?.UnpatchSelf();
                _harmony = null;
                DisposeRegistrations();
                Factory = null;
                CompositeFactory = null;
                _worldObjectService = null;
                Logger.LogError($"{Name} failed closed during registration: {exception}");
            }
        }

        private void OnDestroy()
        {
            CompositeWorldSaveCheckpoint.Detach();
            DurableContainerSafety.Shutdown();
            _harmony?.UnpatchSelf();
            _harmony = null;
            _rpcEndpointRegistration?.Dispose();
            _rpcEndpointRegistration = null;
            _rpcRequirementRegistration?.Dispose();
            _rpcRequirementRegistration = null;
            _reconciliationAdmissionRegistration?.Dispose();
            _reconciliationAdmissionRegistration = null;
            DisposeRegistrations();
            Factory = null;
            CompositeFactory = null;
            _worldObjectService = null;
        }

        private void Update()
        {
            DurableContainerSafety.Update();
            CompositeWorldSaveCheckpoint.Update();
        }

        private void RegisterService(RunicRegistry registry, string capabilityId) =>
            _serviceRegistrations.Add(
                registry.RegisterService<ITransactionCoordinatorFactory>(capabilityId, ModuleId, Factory));

        private void DisposeRegistrations()
        {
            try { _worldObjectService?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not dispose the world-object composite service cleanly: {exception.Message}");
            }
            _worldObjectService = null;
            try { _compositeDomainRegistration?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not unregister composite endpoint domain cleanly: {exception.Message}");
            }
            _compositeDomainRegistration = null;
            try { _rpcEndpointRegistration?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not unregister ownership-return RPC cleanly: {exception.Message}");
            }
            _rpcEndpointRegistration = null;
            try { _rpcRequirementRegistration?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not unregister transaction peer requirement cleanly: {exception.Message}");
            }
            _rpcRequirementRegistration = null;
            try { _reconciliationAdmissionRegistration?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not unregister reconciliation admission cleanly: {exception.Message}");
            }
            _reconciliationAdmissionRegistration = null;
            for (int index = _serviceRegistrations.Count - 1; index >= 0; index--)
            {
                try { _serviceRegistrations[index].Dispose(); }
                catch (Exception exception)
                {
                    Logger.LogWarning($"Could not unregister transaction service cleanly: {exception.Message}");
                }
            }
            _serviceRegistrations.Clear();

            try { (CompositeFactory as IDisposable)?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not dispose the composite journal cleanly: {exception.Message}");
            }

            try { _moduleRegistration?.Dispose(); }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not unregister {ModuleId} cleanly: {exception.Message}");
            }
            _moduleRegistration = null;
        }

        private static void ValidateCapabilityConstants()
        {
            if (!string.Equals(TransactionCapabilityIds.ContainerQuery, RunicCapabilityIds.ContainerQuery, StringComparison.Ordinal) ||
                !string.Equals(TransactionCapabilityIds.ContainerTransfer, RunicCapabilityIds.ContainerTransfer, StringComparison.Ordinal) ||
                !string.Equals(TransactionCapabilityIds.MaterialsReserve, RunicCapabilityIds.MaterialsReserve, StringComparison.Ordinal) ||
                !string.Equals(TransactionCapabilityIds.MaterialsConsume, RunicCapabilityIds.MaterialsConsume, StringComparison.Ordinal) ||
                !string.Equals(TransactionCapabilityIds.DurableCompositeOperations, RunicCapabilityIds.DurableCompositeOperations, StringComparison.Ordinal) ||
                !string.Equals(TransactionCapabilityIds.DurableWorldObjectOperations, RunicCapabilityIds.DurableWorldObjectOperations, StringComparison.Ordinal))
                throw new InvalidOperationException("Runic Core and Transactions capability identifiers do not match.");
        }

        private RpcHandshakeClaimEvaluation EvaluateOutstandingReconciliation(
            RpcHandshakePeerContext context)
            => DurableCompositePeerAdmissionPolicy.Evaluate(CompositeFactory, context);
    }
}
