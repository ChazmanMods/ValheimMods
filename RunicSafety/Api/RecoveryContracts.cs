using System;

namespace RunicSafety.Api
{
    public sealed class InventoryTopologySnapshot
    {
        public InventoryTopologySnapshot(
            string providerId,
            string protocolVersion,
            int totalSlots,
            int occupiedSlots,
            int recoveryCapacitySlots,
            bool serializationVerified,
            string topologyHash)
        {
            ProviderId = providerId ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
            TotalSlots = totalSlots;
            OccupiedSlots = occupiedSlots;
            RecoveryCapacitySlots = recoveryCapacitySlots;
            SerializationVerified = serializationVerified;
            TopologyHash = topologyHash ?? string.Empty;
        }

        public string ProviderId { get; }
        public string ProtocolVersion { get; }
        public int TotalSlots { get; }
        public int OccupiedSlots { get; }
        public int RecoveryCapacitySlots { get; }
        public bool SerializationVerified { get; }
        public string TopologyHash { get; }
    }

    public interface IInventoryTopologyProvider
    {
        string ProviderId { get; }
        bool TryCapture(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode);
    }

    public sealed class RecoveryPlanningRequest
    {
        public RecoveryPlanningRequest(
            long playerId,
            int vanillaWidth,
            int vanillaHeight,
            int vanillaOccupiedSlots,
            bool vanillaSerializationVerified,
            InventoryTopologySnapshot topology = null)
        {
            PlayerId = playerId;
            VanillaWidth = vanillaWidth;
            VanillaHeight = vanillaHeight;
            VanillaOccupiedSlots = vanillaOccupiedSlots;
            VanillaSerializationVerified = vanillaSerializationVerified;
            Topology = topology;
        }

        public long PlayerId { get; }
        public int VanillaWidth { get; }
        public int VanillaHeight { get; }
        public int VanillaOccupiedSlots { get; }
        public bool VanillaSerializationVerified { get; }
        public InventoryTopologySnapshot Topology { get; }
    }

    public enum RecoveryPlanOutcome
    {
        SafeVanillaTombstone = 0,
        SafeExpandedTombstone = 1,
        BlockedUnsupportedExternalTopology = 2,
        BlockedIncompatibleTopology = 3,
        BlockedSerializationFailure = 4,
        Invalid = 5
    }

    public sealed class RecoveryPlan
    {
        internal RecoveryPlan(
            RecoveryPlanOutcome outcome,
            int requiredSlots,
            int plannedSlots,
            string correlationId,
            string remediationCode)
        {
            Outcome = outcome;
            RequiredSlots = requiredSlots;
            PlannedSlots = plannedSlots;
            CorrelationId = correlationId ?? string.Empty;
            RemediationCode = remediationCode ?? string.Empty;
        }

        public RecoveryPlanOutcome Outcome { get; }
        public int RequiredSlots { get; }
        public int PlannedSlots { get; }
        public string CorrelationId { get; }
        public string RemediationCode { get; }
        public bool IsLosslessPlan => Outcome == RecoveryPlanOutcome.SafeVanillaTombstone ||
                                      Outcome == RecoveryPlanOutcome.SafeExpandedTombstone;
    }

    public interface IRecoveryPlanningService
    {
        RecoveryPlan Plan(RecoveryPlanningRequest request);
        IDisposable RegisterTopologyProvider(IInventoryTopologyProvider provider);
        bool TryCaptureTopology(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode);
        bool HasTopologyProvider { get; }
    }
}
