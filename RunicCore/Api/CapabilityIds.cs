namespace Runic.Foundation.Core
{
    /// <summary>
    /// Canonical, stable identifiers for suite-wide contracts. Feature-specific contracts may
    /// extend these with additional dot-separated segments without changing these roots.
    /// </summary>
    public static class RunicCapabilityIds
    {
        public const string FoundationModules = "foundation.modules";
        public const string FoundationServices = "foundation.services";
        public const string PermissionsEvaluate = "permissions.evaluate";
        public const string ContainerQuery = "container.query";
        public const string ContainerTransfer = "container.transfer";
        public const string ContainerOwnershipReturn = "container.ownership-return";
        public const string MaterialsReserve = "materials.reserve";
        public const string MaterialsConsume = "materials.consume";
        /// <summary>
        /// World-scoped, filesystem-WAL-authoritative 1..32 endpoint composite mutations with one
        /// global cross-module commit order and restart reconciliation.
        /// </summary>
        public const string DurableCompositeOperations = "transactions.durable-composite";
        /// <summary>
        /// Typed desired-state world-object transitions, including a server-reserved
        /// create-from-absence identity and mixed existing-object removal/create operations.
        /// Durability and ordering are provided by <see cref="DurableCompositeOperations"/>.
        /// </summary>
        public const string DurableWorldObjectOperations =
            "transactions.durable-world-object";
        public const string PersistenceMigrate = "persistence.migrate";
        public const string NetworkProtocol = "network.protocol";
        /// <summary>
        /// Authenticated-connection-bound, bounded request/response transport. Implementations
        /// must bind the actor to the underlying peer connection rather than trusting a routed
        /// sender identifier supplied inside a client-controlled packet.
        /// </summary>
        public const string NetworkRpc = "network.rpc";
        /// <summary>
        /// Optional pre-PeerInfo evaluators receiving transport-derived direct-session identity.
        /// This is distinct from self-reported compatibility claims and is server-only authority.
        /// </summary>
        public const string NetworkPeerAdmission = "network.peer-admission";
        public const string ActorIdentityBinding = "network.actor-identity-binding";
        public const string NotificationPublish = "notification.publish";
        public const string KeybindingsRegistry = "keybindings.registry";
        public const string StartupMeasure = "startup.measure";
        public const string StartupCache = "startup.cache";
        public const string SecurityAttest = "security.attest";
        public const string SecurityEvidence = "security.evidence";
        public const string SecurityAdmission = "security.admission";
        public const string InventoryItemLocks = "inventory.item-locks";
        /// <summary>
        /// Provider-neutral local-owner inventory crash journal, reconciliation lock, profile
        /// readback proof, and external-custody evidence.
        /// </summary>
        public const string InventoryDurableOperations = "inventory.durable-operations";
        public const string SafetyConfirmation = "safety.confirmation";
        public const string ZdoObserve = "zdo.observe";
        public const string ZdoOwnership = "zdo.ownership";
    }

    public static class RunicModuleIds
    {
        public const string Core = "runic.core";
        public const string Valheim = "valheim";
    }
}
