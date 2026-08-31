using System;
using RunicSafety.Api;
using RunicSafety.Services;

namespace RunicSafety.Tests
{
    internal static class RecoveryPlanningTests
    {
        internal static void Register()
        {
            TestRunner.Run("recovery vanilla topology is lossless", VanillaSafe);
            TestRunner.Run("recovery vanilla capacity shortage blocks unsupported topology", VanillaCapacityShortage);
            TestRunner.Run("recovery vanilla serialization failure blocks", VanillaSerializationFailure);
            TestRunner.Run("recovery expanded topology is lossless", ExpandedSafe);
            TestRunner.Run("recovery expanded capacity shortage blocks unsupported topology", ExpandedCapacityShortage);
            TestRunner.Run("recovery topology serialization failure blocks", TopologySerializationFailure);
            TestRunner.Run("recovery incompatible topology protocol blocks", IncompatibleProtocol);
            TestRunner.Run("recovery malformed topology blocks", MalformedTopology);
            TestRunner.Run("recovery invalid dimensions reject", InvalidDimensions);
            TestRunner.Run("recovery capacity multiplication overflow rejects", CapacityOverflow);
            TestRunner.Run("recovery topology capture works through optional provider", ProviderCapture);
            TestRunner.Run("recovery missing topology provider is explicit", MissingProvider);
            TestRunner.Run("recovery throwing provider fails closed", ThrowingProviderFails);
            TestRunner.Run("recovery duplicate topology provider rejected", DuplicateProvider);
            TestRunner.Run("recovery topology provider registrations are bounded", ProvidersBounded);
            TestRunner.Run("recovery provider dispose is idempotent", ProviderDispose);
            TestRunner.Run("recovery remediation code is precise", RemediationPrecise);
        }

        private static RecoveryPlanningService Service() =>
            new RecoveryPlanningService(new CorrelatedDiagnosticBuffer());

        private static RecoveryPlanningRequest Request(
            int width = 8,
            int height = 4,
            int occupied = 20,
            bool serialized = true,
            InventoryTopologySnapshot topology = null) =>
            new RecoveryPlanningRequest(42, width, height, occupied, serialized, topology);

        private static InventoryTopologySnapshot Topology(
            string protocol = "1.0",
            int total = 40,
            int occupied = 36,
            int recovery = 40,
            bool serialized = true,
            string hash = "topology-a") =>
            new InventoryTopologySnapshot("runic.inventory", protocol, total, occupied, recovery, serialized, hash);

        private static void VanillaSafe()
        {
            RecoveryPlan plan = Service().Plan(Request());
            TestAssert.Equal(RecoveryPlanOutcome.SafeVanillaTombstone, plan.Outcome);
            TestAssert.True(plan.IsLosslessPlan);
            TestAssert.Equal(32, plan.PlannedSlots);
        }

        private static void VanillaCapacityShortage()
        {
            RecoveryPlan plan = Service().Plan(Request(width: 2, height: 2, occupied: 5));
            TestAssert.Equal(RecoveryPlanOutcome.BlockedUnsupportedExternalTopology, plan.Outcome);
            TestAssert.False(plan.IsLosslessPlan);
        }

        private static void VanillaSerializationFailure() => TestAssert.Equal(
            RecoveryPlanOutcome.BlockedSerializationFailure,
            Service().Plan(Request(serialized: false)).Outcome);

        private static void ExpandedSafe()
        {
            RecoveryPlan plan = Service().Plan(Request(topology: Topology()));
            TestAssert.Equal(RecoveryPlanOutcome.SafeExpandedTombstone, plan.Outcome);
            TestAssert.Equal(36, plan.RequiredSlots);
            TestAssert.Equal(40, plan.PlannedSlots);
        }

        private static void ExpandedCapacityShortage() => TestAssert.Equal(
            RecoveryPlanOutcome.BlockedUnsupportedExternalTopology,
            Service().Plan(Request(topology: Topology(total: 40, occupied: 36, recovery: 35))).Outcome);

        private static void TopologySerializationFailure() => TestAssert.Equal(
            RecoveryPlanOutcome.BlockedSerializationFailure,
            Service().Plan(Request(topology: Topology(serialized: false))).Outcome);

        private static void IncompatibleProtocol() => TestAssert.Equal(
            RecoveryPlanOutcome.BlockedIncompatibleTopology,
            Service().Plan(Request(topology: Topology(protocol: "2.0"))).Outcome);

        private static void MalformedTopology() => TestAssert.Equal(
            RecoveryPlanOutcome.BlockedIncompatibleTopology,
            Service().Plan(Request(topology: Topology(total: 4, occupied: 5))).Outcome);

        private static void InvalidDimensions() => TestAssert.Equal(
            RecoveryPlanOutcome.Invalid,
            Service().Plan(Request(width: 0)).Outcome);

        private static void CapacityOverflow() => TestAssert.Equal(
            RecoveryPlanOutcome.Invalid,
            Service().Plan(Request(width: int.MaxValue, height: int.MaxValue)).Outcome);

        private static void ProviderCapture()
        {
            RecoveryPlanningService service = Service();
            service.RegisterTopologyProvider(new FixedTopologyProvider("provider.a"));
            TestAssert.True(service.TryCaptureTopology(42, out InventoryTopologySnapshot snapshot, out string failure));
            TestAssert.NotNull(snapshot);
            TestAssert.Equal(string.Empty, failure);
        }

        private static void MissingProvider()
        {
            TestAssert.False(Service().TryCaptureTopology(42, out InventoryTopologySnapshot snapshot, out string failure));
            TestAssert.Equal(null, snapshot);
            TestAssert.Equal("no-topology-provider", failure);
        }

        private static void ThrowingProviderFails()
        {
            RecoveryPlanningService service = Service();
            service.RegisterTopologyProvider(new ThrowingTopologyProvider());
            TestAssert.False(service.TryCaptureTopology(42, out _, out string failure));
            TestAssert.Equal("provider-threw", failure);
        }

        private static void DuplicateProvider()
        {
            RecoveryPlanningService service = Service();
            service.RegisterTopologyProvider(new FixedTopologyProvider("provider.same"));
            TestAssert.Throws<InvalidOperationException>(() =>
                service.RegisterTopologyProvider(new FixedTopologyProvider("provider.same")));
        }

        private static void ProvidersBounded()
        {
            RecoveryPlanningService service = Service();
            for (int index = 0; index < 8; index++)
                service.RegisterTopologyProvider(new FixedTopologyProvider("provider." + index));
            TestAssert.Throws<InvalidOperationException>(() =>
                service.RegisterTopologyProvider(new FixedTopologyProvider("provider.overflow")));
        }

        private static void ProviderDispose()
        {
            RecoveryPlanningService service = Service();
            IDisposable registration = service.RegisterTopologyProvider(new FixedTopologyProvider("provider.dispose"));
            TestAssert.True(service.HasTopologyProvider);
            registration.Dispose();
            registration.Dispose();
            TestAssert.False(service.HasTopologyProvider);
        }

        private static void RemediationPrecise()
        {
            RecoveryPlan plan = Service().Plan(Request(topology: Topology(protocol: "2.0")));
            TestAssert.Equal("update-inventory-topology-provider", plan.RemediationCode);
            TestAssert.False(string.IsNullOrWhiteSpace(plan.CorrelationId));
        }

        private sealed class FixedTopologyProvider : IInventoryTopologyProvider
        {
            internal FixedTopologyProvider(string id) { ProviderId = id; }
            public string ProviderId { get; }
            public bool TryCapture(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode)
            {
                snapshot = Topology();
                failureCode = string.Empty;
                return true;
            }
        }

        private sealed class ThrowingTopologyProvider : IInventoryTopologyProvider
        {
            public string ProviderId => "provider.throw";
            public bool TryCapture(long playerId, out InventoryTopologySnapshot snapshot, out string failureCode) =>
                throw new InvalidOperationException("fault injection");
        }
    }
}
