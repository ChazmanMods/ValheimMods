using System;
using System.Collections.Generic;

namespace RunicTransactions.Capabilities
{
    public static class TransactionCapabilityIds
    {
        public const string ContainerQuery = "container.query";
        public const string ContainerTransfer = "container.transfer";
        public const string MaterialsReserve = "materials.reserve";
        public const string MaterialsConsume = "materials.consume";
        public const string DurableCompositeOperations = "transactions.durable-composite";
        public const string DurableWorldObjectOperations =
            "transactions.durable-world-object";
    }

    public sealed class TransactionCapabilityDescriptor
    {
        public TransactionCapabilityDescriptor(string id, int protocolVersion)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Capability identifier is required.", nameof(id));
            if (protocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            Id = id;
            ProtocolVersion = protocolVersion;
        }

        public string Id { get; }
        public int ProtocolVersion { get; }
    }

    public static class TransactionCapabilityCatalog
    {
        private static readonly IReadOnlyList<TransactionCapabilityDescriptor> Descriptors = Array.AsReadOnly(new[]
        {
            new TransactionCapabilityDescriptor(TransactionCapabilityIds.MaterialsReserve, 1),
            new TransactionCapabilityDescriptor(TransactionCapabilityIds.MaterialsConsume, 1)
            ,new TransactionCapabilityDescriptor(TransactionCapabilityIds.DurableCompositeOperations, 1)
            ,new TransactionCapabilityDescriptor(TransactionCapabilityIds.DurableWorldObjectOperations, 1)
        });

        public static IReadOnlyList<TransactionCapabilityDescriptor> All => Descriptors;
    }
}
