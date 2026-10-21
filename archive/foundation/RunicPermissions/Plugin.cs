using System;
using BepInEx;
using BepInEx.Configuration;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using RunicPermissions.Contracts;
using RunicPermissions.Evaluation;
using RunicPermissions.Groups;
using RunicPermissions.Integration;

namespace RunicPermissions
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(RunicCoreGuid, "1.0.0")]
    [BepInDependency(RunicPersistenceGuid, "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicPermissions";
        public const string RunicCoreGuid = "chazman.RunicCore";
        public const string RunicPersistenceGuid = "chazman.RunicPersistence";
        public const string ModuleId = "runic.permissions";
        public const string Name = "Runic Permissions";
        public const string Version = "1.0.0";
        public const string ProtocolVersion = PermissionCapabilities.ProtocolVersion;

        private ConfigEntry<bool> _adminBypassEnabled;
        private ModuleRegistration _moduleRegistration;
        private ServiceRegistration _serviceRegistration;
        private ServiceRegistration _groupServiceRegistration;
        private ServiceRegistration _activeGroupServiceRegistration;
        private GroupRuntime _groupRuntime;

        private void Awake()
        {
            if (!RunicCoreApi.IsPluginLoaded)
            {
                Logger.LogError(
                    "Runic Permissions did not register because Runic Core 1.0.0+ did not " +
                    "finish loading. Repair or update Runic Core, then restart.");
                return;
            }

            Runic.Foundation.Core.ProtocolVersion permissionsProtocol =
                Runic.Foundation.Core.ProtocolVersion.Parse(ProtocolVersion);
            if (!permissionsProtocol.IsCompatibleWith(RunicCoreApi.ProtocolVersion))
            {
                Logger.LogError(
                    $"Runic Permissions protocol {ProtocolVersion} is incompatible with " +
                    $"Runic Core protocol {RunicCoreApi.ProtocolVersion}. Update the affected " +
                    "module; no permission service was registered.");
                return;
            }

            _adminBypassEnabled = Config.Bind(
                "Security",
                "AdminBypassEnabled",
                false,
                "Allow deliberate administrator mode to bypass ordinary policies. Every " +
                "successful bypass is audited; explicit denies and uncertain data still deny. " +
                "A restart is required after changing this server-owned setting.");

            var evaluator = new PermissionEvaluator(
                new PermissionEvaluatorOptions(_adminBypassEnabled.Value));
            var service = new PermissionEvaluationService(
                evaluator,
                new BepInExPermissionAuditSink(Logger));
            var descriptor = new ModuleDescriptor(
                ModuleId,
                Name,
                Version,
                ProtocolVersion,
                new[]
                {
                    RunicCapabilityIds.PermissionsEvaluate,
                    GroupCapabilities.Membership,
                    GroupCapabilities.ActiveSelection
                });

            _moduleRegistration = RunicRegistry.Shared.RegisterModule(descriptor);
            try
            {
                _serviceRegistration = RunicRegistry.Shared.RegisterService<IPermissionEvaluationService>(
                    RunicCapabilityIds.PermissionsEvaluate,
                    ModuleId,
                    service);
                FileGroupWorldStore groupStore = GroupRuntime.CreateStore();
                FileGroupActiveSelectionStore activeGroupStore = GroupRuntime.CreateActiveStore();
                var groupService = new FileBackedGroupMembershipService(
                    groupStore,
                    GroupRuntime.CurrentWorldScope);
                var activeGroupService = new GroupActiveSelectionService(
                    groupStore,
                    activeGroupStore,
                    GroupRuntime.CurrentWorldScope);
                _groupServiceRegistration = RunicRegistry.Shared.RegisterService<IGroupMembershipService>(
                    GroupCapabilities.Membership,
                    ModuleId,
                    groupService);
                _activeGroupServiceRegistration =
                    RunicRegistry.Shared.RegisterService<IActiveGroupSelectionService>(
                        GroupCapabilities.ActiveSelection,
                        ModuleId,
                        activeGroupService);
                if (!RunicRegistry.Shared.TryGetService(
                        RunicCapabilityIds.NetworkRpc,
                        out IRunicRpcService rpc) || rpc == null)
                    throw new InvalidOperationException(
                        "Runic Persistence did not publish network.rpc.");
                _groupRuntime = new GroupRuntime(
                    _moduleRegistration,
                    rpc,
                    groupStore,
                    activeGroupService,
                    Logger);
            }
            catch (Exception exception)
            {
                _groupRuntime?.Dispose();
                _groupRuntime = null;
                _groupServiceRegistration?.Dispose();
                _groupServiceRegistration = null;
                _activeGroupServiceRegistration?.Dispose();
                _activeGroupServiceRegistration = null;
                _serviceRegistration?.Dispose();
                _serviceRegistration = null;
                _moduleRegistration.Dispose();
                _moduleRegistration = null;
                Logger.LogError(
                    "Runic Permissions failed closed while registering its evaluator, built-in " +
                    "Group provider, or direct-session Group transport. " +
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            Logger.LogInfo(
                $"{Name} v{Version} registered {RunicCapabilityIds.PermissionsEvaluate} and " +
                $"{GroupCapabilities.Membership}/{GroupCapabilities.ActiveSelection} " +
                $"protocol {ProtocolVersion}; admin bypass is " +
                (_adminBypassEnabled.Value ? "ENABLED (audited)" : "disabled") + ".");
        }

        private void Update()
        {
            _groupRuntime?.Tick();
        }

        private void OnDestroy()
        {
            _groupRuntime?.Dispose();
            _groupRuntime = null;
            _groupServiceRegistration?.Dispose();
            _groupServiceRegistration = null;
            _activeGroupServiceRegistration?.Dispose();
            _activeGroupServiceRegistration = null;
            _serviceRegistration?.Dispose();
            _serviceRegistration = null;
            _moduleRegistration?.Dispose();
            _moduleRegistration = null;
        }
    }
}
