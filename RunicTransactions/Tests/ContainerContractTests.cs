using System;
using System.Collections.Generic;
using RunicTransactions.Contracts;

namespace RunicTransactions.Tests
{
    internal static class ContainerContractTests
    {
        public static void QueryResultsAreBoundedDeterministicAndImmutable()
        {
            var endpointB = new ContainerEndpointSnapshot(
                new EndpointId("zdo:b"), 1, new WorldPosition(2, 0, 0), "chest",
                false, new[] { new ContainerResourceSnapshot(new ResourceId("Wood"), 2) });
            var endpointA = new ContainerEndpointSnapshot(
                new EndpointId("zdo:a"), 1, new WorldPosition(1, 0, 0), "chest",
                false, new[] { new ContainerResourceSnapshot(new ResourceId("Stone"), 3) });
            var source = new List<ContainerEndpointSnapshot> { endpointB, endpointA };
            var result = new ContainerQueryResult(
                ContainerQueryStatus.Succeeded, source, 2, false, "ok");
            source.Clear();

            TestAssert.Equal(2, result.Endpoints.Count, "Result must retain its own endpoint copy.");
            TestAssert.Equal("zdo:a", result.Endpoints[0].EndpointId.Value, "Endpoints must be canonicalized.");
            TestAssert.Equal("zdo:b", result.Endpoints[1].EndpointId.Value, "Endpoints must be canonicalized.");
        }

        public static void FailedQueryCannotDiscloseEndpoints()
        {
            TestAssert.Throws<ArgumentException>(() => new ContainerQueryResult(
                ContainerQueryStatus.Denied,
                new[]
                {
                    new ContainerEndpointSnapshot(
                        new EndpointId("zdo:a"), 0, new WorldPosition(0, 0, 0), "chest", false, null)
                },
                1,
                false,
                "permission.denied"), "Denied queries must not reveal endpoint data.");
        }

        public static void TransferContractsRejectUnsafeShapes()
        {
            TestAssert.Throws<ArgumentException>(() => new ContainerTransferLeg(
                new EndpointId("same"), new EndpointId("same"), new ResourceId("Wood"), 1),
                "Self-transfers are not valid.");

            TestAssert.Throws<ArgumentOutOfRangeException>(() => new ContainerTransferRequest(
                TransactionId.New(), CorrelationId.New(), IdempotencyKey.New(),
                new PrincipalId("player:1"), "quick-stack", Array.Empty<ContainerTransferLeg>()),
                "A transfer requires at least one leg.");

            TestAssert.Throws<ArgumentException>(() => new ContainerTransferResult(
                ContainerTransferStatus.Succeeded, 5, 4, "ok"),
                "Success cannot represent a partial transfer.");
        }

        public static void WorldPositionRejectsNonFiniteCoordinates()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new WorldPosition(float.NaN, 0, 0), "NaN is not a world coordinate.");
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new WorldPosition(0, float.PositiveInfinity, 0), "Infinity is not a world coordinate.");
        }

        public static void ValheimIdentityFormattingRoundTrips()
        {
            PrincipalId principal = ValheimIdentityIds.Player(long.MinValue + 1);
            TestAssert.True(
                ValheimIdentityIds.TryGetPlayerId(principal, out long parsed),
                "Canonical Valheim player identity must parse.");
            TestAssert.Equal(long.MinValue + 1, parsed, "Player identity must round-trip exactly.");
            TestAssert.Equal(
                "valheim.zdo:12:34",
                ValheimIdentityIds.ZdoEndpoint("12:34").Value,
                "ZDO endpoint formatting must remain stable.");
        }
    }
}
