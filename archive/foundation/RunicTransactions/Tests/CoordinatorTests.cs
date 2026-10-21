using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RunicTransactions.Contracts;
using RunicTransactions.Coordination;

namespace RunicTransactions.Tests
{
    internal static class CoordinatorTests
    {
        private static readonly EndpointId ChestA = new EndpointId("world/main/container/a");
        private static readonly EndpointId ChestB = new EndpointId("world/main/container/b");
        private static readonly ResourceId Wood = new ResourceId("item/Wood");
        private static readonly ResourceId Stone = new ResourceId("item/Stone");
        private static readonly PrincipalId Player = new PrincipalId("player/1234");

        internal static void DeterministicOrderingAndCanonicalization()
        {
            IReadOnlyList<EndpointId> ordered = DeterministicLockOrder.OrderEndpoints(new[]
            {
                new EndpointId("z"), new EndpointId("a"), new EndpointId("m"), new EndpointId("a")
            });
            TestAssert.Equal(3, ordered.Count, "Lock order must de-duplicate endpoints.");
            TestAssert.Equal("a", ordered[0].Value, "Lock order must be ordinal.");
            TestAssert.Equal("m", ordered[1].Value, "Lock order must be ordinal.");
            TestAssert.Equal("z", ordered[2].Value, "Lock order must be ordinal.");

            ReservationRequest request = Request("order", new[]
            {
                new ReservationLine(ChestB, Wood, 2),
                new ReservationLine(ChestA, Stone, 4),
                new ReservationLine(ChestA, Wood, 1),
                new ReservationLine(ChestB, Wood, 3)
            });
            TestAssert.Equal(3, request.Lines.Count, "Duplicate endpoint/resource lines must be combined.");
            TestAssert.Equal(ChestA, request.Lines[0].Endpoint, "Canonical lines must sort endpoints first.");
            TestAssert.Equal(Stone, request.Lines[0].Resource, "Canonical lines must sort resources ordinally.");
            TestAssert.Equal(5L, request.Lines[2].Amount, "Canonical duplicate amounts must sum exactly.");

            ReservationRequest equivalent = Request("order-equivalent", new[]
            {
                new ReservationLine(ChestA, Wood, 1),
                new ReservationLine(ChestB, Wood, 5),
                new ReservationLine(ChestA, Stone, 4)
            });
            TestAssert.Equal(request.Fingerprint, equivalent.Fingerprint,
                "Equivalent exact allocations must have one deterministic fingerprint.");
        }

        internal static void InsufficientResourcesNeverPartiallyReserve()
        {
            var coordinator = Coordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 2) });

            TransactionResult result = coordinator.ReserveExact(Request("short", new[]
            {
                new ReservationLine(ChestA, Wood, 5),
                new ReservationLine(ChestB, Stone, 3)
            }));

            TestAssert.Equal(TransactionOutcome.Denied, result.Outcome, "Short batch must be denied.");
            TestAssert.Equal(TransactionDenialCode.InsufficientResources, result.Denial.Code,
                "Short batch must carry a stable denial code.");
            AssertInventory(coordinator, ChestA, Wood, 5, 0);
            AssertInventory(coordinator, ChestB, Stone, 2, 0);
        }

        internal static void PermissionDenialNeverPartiallyReserve()
        {
            var authorizer = new TestAuthorizer(context => !context.Endpoint.Equals(ChestB));
            var coordinator = Coordinator(authorizer);
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 5) });

            TransactionResult result = coordinator.ReserveExact(Request("denied", new[]
            {
                new ReservationLine(ChestA, Wood, 2),
                new ReservationLine(ChestB, Stone, 2)
            }));

            TestAssert.Equal(TransactionOutcome.Denied, result.Outcome, "Unauthorized batch must be denied.");
            TestAssert.Equal(TransactionDenialCode.PermissionDenied, result.Denial.Code,
                "Authorization failure must be explicit.");
            AssertInventory(coordinator, ChestA, Wood, 5, 0);
            AssertInventory(coordinator, ChestB, Stone, 5, 0);
        }

        internal static void DefaultAuthorizerFailsClosed()
        {
            var coordinator = new InMemoryTransactionCoordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });

            TransactionResult result = coordinator.ReserveExact(Request("default-deny", new[]
            {
                new ReservationLine(ChestA, Wood, 1)
            }));

            TestAssert.Equal(TransactionOutcome.Denied, result.Outcome,
                "A coordinator without an authoritative policy must deny mutation.");
            TestAssert.Equal(TransactionDenialCode.PermissionDenied, result.Denial.Code,
                "Default denial must remain an explicit permission result.");
            AssertInventory(coordinator, ChestA, Wood, 5, 0);
        }

        internal static void LastResourceRaceHasExactlyOneWinner()
        {
            var coordinator = Coordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 1) });
            var start = new ManualResetEventSlim(false);
            var results = new ConcurrentBag<TransactionResult>();
            const int contenderCount = 16;
            Task[] contenders = Enumerable.Range(0, contenderCount).Select(index => Task.Run(() =>
            {
                start.Wait();
                results.Add(coordinator.ReserveExact(Request("race-" + index, new[]
                {
                    new ReservationLine(ChestA, Wood, 1)
                })));
            })).ToArray();

            start.Set();
            Task.WaitAll(contenders);
            TestAssert.Equal(1, results.Count(result => result.Outcome == TransactionOutcome.Reserved),
                "Exactly one contender may reserve the last item.");
            TestAssert.Equal(contenderCount - 1, results.Count(result => result.Outcome == TransactionOutcome.Denied),
                "Every other contender must fail without over-reserving.");
            AssertInventory(coordinator, ChestA, Wood, 1, 1);

            TransactionResult winner = results.Single(result => result.Outcome == TransactionOutcome.Reserved);
            TransactionResult commit = coordinator.CommitOnce(winner.TransactionId, new CorrelationId("race-commit"));
            TestAssert.Equal(TransactionOutcome.Committed, commit.Outcome, "Winning reservation must commit.");
            TransactionResult replay = coordinator.CommitOnce(winner.TransactionId, new CorrelationId("race-commit-retry"));
            TestAssert.True(replay.IsReplay, "A repeated commit must replay the terminal result.");
            AssertInventory(coordinator, ChestA, Wood, 0, 0);
        }

        internal static void ExplicitRollbackReleasesAllEndpoints()
        {
            var coordinator = Coordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 6) });
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 7) });
            ReservationRequest request = Request("rollback", new[]
            {
                new ReservationLine(ChestA, Wood, 3),
                new ReservationLine(ChestB, Stone, 4)
            });
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(request).Outcome,
                "Setup reservation must succeed.");

            TransactionResult rollback = coordinator.Rollback(request.TransactionId,
                new CorrelationId("rollback-call"), "Caller cancelled crafting.");
            TestAssert.Equal(TransactionOutcome.RolledBack, rollback.Outcome, "Rollback must succeed.");
            AssertInventory(coordinator, ChestA, Wood, 6, 0);
            AssertInventory(coordinator, ChestB, Stone, 7, 0);
            TransactionResult replay = coordinator.Rollback(request.TransactionId,
                new CorrelationId("rollback-retry"), "Retry");
            TestAssert.True(replay.IsReplay, "Repeated rollback must not release another transaction's reservation.");
        }

        internal static void RevalidationFailureRollsBackWithoutPartialConsumption()
        {
            var coordinator = Coordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 5) });
            ReservationRequest request = Request("revalidate", new[]
            {
                new ReservationLine(ChestA, Wood, 4),
                new ReservationLine(ChestB, Stone, 4)
            });
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(request).Outcome,
                "Setup reservation must succeed.");

            // Models the authoritative Valheim container changing outside this coordinator.
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 2) });
            TransactionResult result = coordinator.CommitOnce(request.TransactionId,
                new CorrelationId("revalidate-commit"));

            TestAssert.Equal(TransactionOutcome.Failed, result.Outcome, "Changed inventory must fail revalidation.");
            TestAssert.Equal(TransactionDenialCode.RevalidationFailed, result.Denial.Code,
                "Revalidation failure must be distinguishable.");
            AssertInventory(coordinator, ChestA, Wood, 5, 0);
            AssertInventory(coordinator, ChestB, Stone, 2, 0);
        }

        internal static void PermissionIsRevalidatedAtCommit()
        {
            int allow = 1;
            var authorizer = new TestAuthorizer(context => Volatile.Read(ref allow) == 1);
            var coordinator = Coordinator(authorizer);
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 3) });
            ReservationRequest request = Request("permission-recheck", new[]
            {
                new ReservationLine(ChestA, Wood, 2)
            });
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(request).Outcome,
                "Permission must initially allow reservation.");
            Volatile.Write(ref allow, 0);

            TransactionResult result = coordinator.CommitOnce(request.TransactionId,
                new CorrelationId("permission-recheck-commit"));
            TestAssert.Equal(TransactionOutcome.Failed, result.Outcome, "Revoked permission must fail commit.");
            TestAssert.Equal(TransactionDenialCode.PermissionDenied, result.Denial.Code,
                "Revocation must retain the permission denial code.");
            AssertInventory(coordinator, ChestA, Wood, 3, 0);
        }

        internal static void IdempotencyKeyCannotConsumeTwice()
        {
            var coordinator = Coordinator();
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 10) });
            ReservationRequest request = Request("idem", new[] { new ReservationLine(ChestA, Wood, 3) });
            TransactionResult first = coordinator.ReserveExact(request);
            TransactionResult retry = coordinator.ReserveExact(new ReservationRequest(
                new TransactionId("a-different-client-transaction-id"),
                new CorrelationId("idem-retry"),
                request.IdempotencyKey,
                request.Principal,
                request.Lines));
            TestAssert.True(retry.IsReplay, "Same principal/key/payload must replay.");
            TestAssert.Equal(first.TransactionId, retry.TransactionId, "Replay must identify the admitted transaction.");
            AssertInventory(coordinator, ChestA, Wood, 10, 3);
            coordinator.CommitOnce(first.TransactionId, new CorrelationId("idem-commit"));
            coordinator.CommitOnce(first.TransactionId, new CorrelationId("idem-commit-retry"));
            AssertInventory(coordinator, ChestA, Wood, 7, 0);
        }

        internal static void CapacityFailsClosedUntilTerminalPrune()
        {
            var clock = new FakeClock();
            var coordinator = Coordinator(
                clock: clock,
                maximumTrackedTransactions: 2,
                reservationLeaseDuration: TimeSpan.FromHours(1),
                terminalRetentionDuration: TimeSpan.FromMinutes(1));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 10) });

            ReservationRequest first = Request("capacity-a", new[] { new ReservationLine(ChestA, Wood, 1) });
            ReservationRequest second = Request("capacity-b", new[] { new ReservationLine(ChestA, Wood, 1) });
            ReservationRequest third = Request("capacity-c", new[] { new ReservationLine(ChestA, Wood, 1) });
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(first).Outcome,
                "The first bounded entry must reserve.");
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(second).Outcome,
                "The second bounded entry must reserve.");

            TransactionResult full = coordinator.ReserveExact(third);
            TestAssert.Equal(TransactionOutcome.Denied, full.Outcome,
                "A full transaction table must fail closed.");
            TestAssert.Equal(TransactionDenialCode.CoordinatorCapacityReached, full.Denial.Code,
                "Capacity pressure must use a stable denial code.");
            TestAssert.Equal(2, coordinator.TrackedTransactionCount,
                "A rejected admission must not exceed the configured bound.");

            coordinator.Rollback(first.TransactionId, new CorrelationId("capacity-rollback"), "Done");
            TransactionResult retained = coordinator.ReserveExact(third);
            TestAssert.Equal(TransactionDenialCode.CoordinatorCapacityReached, retained.Denial.Code,
                "A terminal entry must retain its retry tombstone for the full retention window.");

            clock.Advance(TimeSpan.FromMinutes(1));
            TransactionPruneResult prune = coordinator.PruneExpired();
            TestAssert.Equal(1, prune.RemovedTerminalTransactions,
                "Exactly the elapsed terminal tombstone must be pruned.");
            TestAssert.Equal(1, prune.TrackedTransactions,
                "The active entry must remain tracked.");
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(third).Outcome,
                "Admission must resume after explicit cleanup creates capacity.");
            TestAssert.Equal(2, coordinator.TrackedTransactionCount,
                "The table must remain at, never above, its configured bound.");
        }

        internal static void ExpiredLeaseAutomaticallyReleasesEntireBatch()
        {
            var clock = new FakeClock();
            var coordinator = Coordinator(
                clock: clock,
                reservationLeaseDuration: TimeSpan.FromSeconds(30),
                terminalRetentionDuration: TimeSpan.FromMinutes(5));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 6) });
            coordinator.RegisterOrReplaceEndpoint(ChestB, new[] { new ResourceBalance(Stone, 7) });
            ReservationRequest request = Request("lease", new[]
            {
                new ReservationLine(ChestA, Wood, 3),
                new ReservationLine(ChestB, Stone, 4)
            });
            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(request).Outcome,
                "Setup reservation must succeed.");

            clock.Advance(TimeSpan.FromSeconds(30));
            TransactionResult commit = coordinator.CommitOnce(
                request.TransactionId,
                new CorrelationId("lease-late-commit"));
            TestAssert.Equal(TransactionOutcome.Failed, commit.Outcome,
                "A commit after automatic expiry must not consume anything.");
            TestAssert.Equal(TransactionDenialCode.ReservationLeaseExpired, commit.Denial.Code,
                "Lease expiry must remain distinguishable after an idempotent replay.");
            AssertInventory(coordinator, ChestA, Wood, 6, 0);
            AssertInventory(coordinator, ChestB, Stone, 7, 0);
            TransactionSnapshot snapshot;
            TestAssert.True(coordinator.TryGetTransaction(request.TransactionId, out snapshot),
                "The expiry tombstone must remain during retention.");
            TestAssert.Equal(TransactionState.Failed, snapshot.State,
                "An expired reservation must be terminal.");
            TestAssert.True(snapshot.TerminalAtUtc.HasValue,
                "An expired reservation must expose its terminal timestamp.");
            TestAssert.Equal(1, coordinator.GetAuditTrail(request.TransactionId)
                .Count(record => record.Action == TransactionAuditAction.Expire),
                "Automatic cleanup must audit one expiry transition.");
        }

        internal static void TerminalRetentionBoundsIdempotencyTombstones()
        {
            var clock = new FakeClock();
            var coordinator = Coordinator(
                clock: clock,
                terminalRetentionDuration: TimeSpan.FromMinutes(2));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 10) });
            ReservationRequest request = Request("retention", new[] { new ReservationLine(ChestA, Wood, 3) });
            TransactionResult reserved = coordinator.ReserveExact(request);
            TestAssert.Equal(TransactionOutcome.Committed, coordinator.CommitOnce(
                reserved.TransactionId,
                new CorrelationId("retention-commit")).Outcome,
                "Setup commit must succeed.");

            ReservationRequest retry = new ReservationRequest(
                new TransactionId("txn-retention-retry"),
                new CorrelationId("corr-retention-retry"),
                request.IdempotencyKey,
                request.Principal,
                request.Lines);
            TestAssert.True(coordinator.ReserveExact(retry).IsReplay,
                "A completed request must replay while its tombstone is retained.");
            AssertInventory(coordinator, ChestA, Wood, 7, 0);

            clock.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromTicks(1));
            TestAssert.Equal(0, coordinator.PruneExpired().RemovedTerminalTransactions,
                "Cleanup must not shorten the configured retry window.");
            TestAssert.True(coordinator.ReserveExact(retry).IsReplay,
                "Idempotency must remain effective until the exact retention boundary.");

            clock.Advance(TimeSpan.FromTicks(1));
            TransactionPruneResult prune = coordinator.PruneExpired();
            TestAssert.Equal(1, prune.RemovedTerminalTransactions,
                "The terminal transaction and its idempotency key must prune together.");
            TransactionSnapshot ignored;
            TestAssert.False(coordinator.TryGetTransaction(request.TransactionId, out ignored),
                "A pruned transaction must no longer be addressable.");
            TransactionResult oldCommit = coordinator.CommitOnce(
                request.TransactionId,
                new CorrelationId("retention-old-commit"));
            TestAssert.Equal(TransactionDenialCode.TransactionNotFound, oldCommit.Denial.Code,
                "Post-retention callers must receive an explicit unknown-transaction result.");

            TestAssert.Equal(TransactionOutcome.Reserved, coordinator.ReserveExact(retry).Outcome,
                "After the documented retry window, the same key represents a new admission.");
            coordinator.CommitOnce(retry.TransactionId, new CorrelationId("retention-second-commit"));
            AssertInventory(coordinator, ChestA, Wood, 4, 0);
        }

        internal static void CommitAndExpirySerializeWithoutDoubleMutation()
        {
            var clock = new FakeClock();
            var commitEntered = new ManualResetEventSlim(false);
            var finishCommit = new ManualResetEventSlim(false);
            var authorizer = new TestAuthorizer(context =>
            {
                if (context.Phase == AuthorizationPhase.Revalidate)
                {
                    commitEntered.Set();
                    finishCommit.Wait();
                }
                return true;
            });
            var coordinator = Coordinator(
                authorizer,
                clock,
                reservationLeaseDuration: TimeSpan.FromSeconds(10));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });
            ReservationRequest request = Request("commit-expiry", new[]
            {
                new ReservationLine(ChestA, Wood, 2)
            });
            coordinator.ReserveExact(request);

            Task<TransactionResult> commitTask = Task.Run(() => coordinator.CommitOnce(
                request.TransactionId,
                new CorrelationId("commit-expiry-commit")));
            TestAssert.True(commitEntered.Wait(TimeSpan.FromSeconds(5)),
                "The commit must reach revalidation before the test advances the lease.");
            clock.Advance(TimeSpan.FromSeconds(10));
            var pruneStarted = new ManualResetEventSlim(false);
            Task<TransactionPruneResult> pruneTask = Task.Run(() =>
            {
                pruneStarted.Set();
                return coordinator.PruneExpired();
            });
            TestAssert.True(pruneStarted.Wait(TimeSpan.FromSeconds(5)),
                "The concurrent expiry attempt must start.");
            finishCommit.Set();
            Task.WaitAll(commitTask, pruneTask);

            TestAssert.Equal(TransactionOutcome.Committed, commitTask.Result.Outcome,
                "A commit already holding the entry gate may finish exactly once.");
            TestAssert.Equal(0, pruneTask.Result.ExpiredReservations,
                "Cleanup must observe the terminal commit instead of releasing it again.");
            AssertInventory(coordinator, ChestA, Wood, 3, 0);

            var expiryClock = new FakeClock();
            var expiryWinner = Coordinator(
                clock: expiryClock,
                reservationLeaseDuration: TimeSpan.FromSeconds(10));
            expiryWinner.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 5) });
            ReservationRequest expiredRequest = Request("expiry-wins", new[]
            {
                new ReservationLine(ChestA, Wood, 2)
            });
            expiryWinner.ReserveExact(expiredRequest);
            expiryClock.Advance(TimeSpan.FromSeconds(10));
            TestAssert.Equal(1, expiryWinner.PruneExpired().ExpiredReservations,
                "Cleanup must win once when it acquires the entry gate after the deadline.");
            TransactionResult lateCommit = expiryWinner.CommitOnce(
                expiredRequest.TransactionId,
                new CorrelationId("expiry-wins-commit"));
            TestAssert.Equal(TransactionDenialCode.ReservationLeaseExpired, lateCommit.Denial.Code,
                "A late commit must replay the expiry instead of consuming.");
            AssertInventory(expiryWinner, ChestA, Wood, 5, 0);
        }

        internal static void ReentrantAuthorizerCannotCommitPreparingEntry()
        {
            InMemoryTransactionCoordinator coordinator = null;
            TransactionResult nestedCommit = null;
            var authorizer = new TestAuthorizer(context =>
            {
                if (context.Phase == AuthorizationPhase.Prepare && nestedCommit == null)
                {
                    nestedCommit = coordinator.CommitOnce(
                        context.TransactionId,
                        new CorrelationId("reentrant-commit"));
                }
                return true;
            });
            coordinator = Coordinator(authorizer);
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 4) });
            ReservationRequest request = Request("reentrant", new[]
            {
                new ReservationLine(ChestA, Wood, 2)
            });

            TransactionResult reservation = coordinator.ReserveExact(request);
            TestAssert.NotNull(nestedCommit, "The authorizer must exercise the reentrant path.");
            TestAssert.Equal(TransactionOutcome.Denied, nestedCommit.Outcome,
                "A reentrant commit must not mutate a transaction still in prepare.");
            TestAssert.Equal(TransactionDenialCode.InvalidState, nestedCommit.Denial.Code,
                "Preparing-state rejection must use the stable invalid-state result.");
            TestAssert.Equal(TransactionOutcome.Reserved, reservation.Outcome,
                "Rejecting reentrant commit must not corrupt the outer reservation.");
            TransactionSnapshot snapshot;
            TestAssert.True(coordinator.TryGetTransaction(request.TransactionId, out snapshot),
                "The outer transaction must remain tracked.");
            TestAssert.Equal(TransactionState.Reserved, snapshot.State,
                "The outer reservation must finish in Reserved, not a mixed terminal state.");
            TestAssert.False(snapshot.TerminalAtUtc.HasValue,
                "A preparing-state rejection must not stamp the live reservation terminal.");
            TestAssert.Equal(TransactionOutcome.Committed, coordinator.CommitOnce(
                request.TransactionId,
                new CorrelationId("reentrant-real-commit")).Outcome,
                "The properly ordered commit must still succeed exactly once.");
            AssertInventory(coordinator, ChestA, Wood, 2, 0);
        }

        internal static void EndpointVersionAdvancesForAvailabilityMutations()
        {
            var clock = new FakeClock();
            var coordinator = Coordinator(
                clock: clock,
                reservationLeaseDuration: TimeSpan.FromSeconds(10));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[]
            {
                new ResourceBalance(Wood, 5),
                new ResourceBalance(Stone, 5)
            });
            EndpointInventorySnapshot snapshot;
            coordinator.TryGetEndpointSnapshot(ChestA, out snapshot);
            long initialVersion = snapshot.Version;

            ReservationRequest rollbackRequest = Request("version-rollback", new[]
            {
                new ReservationLine(ChestA, Wood, 1),
                new ReservationLine(ChestA, Stone, 2)
            });
            coordinator.ReserveExact(rollbackRequest);
            coordinator.TryGetEndpointSnapshot(ChestA, out snapshot);
            TestAssert.Equal(initialVersion + 1, snapshot.Version,
                "A multi-resource reservation must advance its endpoint version exactly once.");
            coordinator.Rollback(
                rollbackRequest.TransactionId,
                new CorrelationId("version-rollback-call"),
                "Version test");
            coordinator.TryGetEndpointSnapshot(ChestA, out snapshot);
            TestAssert.Equal(initialVersion + 2, snapshot.Version,
                "Releasing a rollback must advance its endpoint version exactly once.");

            ReservationRequest expiryRequest = Request("version-expiry", new[]
            {
                new ReservationLine(ChestA, Wood, 1),
                new ReservationLine(ChestA, Stone, 2)
            });
            coordinator.ReserveExact(expiryRequest);
            coordinator.TryGetEndpointSnapshot(ChestA, out snapshot);
            TestAssert.Equal(initialVersion + 3, snapshot.Version,
                "A later reservation must produce another observable version.");
            clock.Advance(TimeSpan.FromSeconds(10));
            TestAssert.Equal(1, coordinator.PruneExpired().ExpiredReservations,
                "The setup reservation must expire.");
            coordinator.TryGetEndpointSnapshot(ChestA, out snapshot);
            TestAssert.Equal(initialVersion + 4, snapshot.Version,
                "Expiry release must produce another observable version.");
        }

        internal static void ExtremeClockSaturatesLeaseBeforeReserving()
        {
            var clock = new FakeClock(DateTimeOffset.MaxValue);
            var coordinator = Coordinator(
                clock: clock,
                reservationLeaseDuration: TimeSpan.FromDays(1));
            coordinator.RegisterOrReplaceEndpoint(ChestA, new[] { new ResourceBalance(Wood, 2) });
            ReservationRequest request = Request("clock-ceiling", new[]
            {
                new ReservationLine(ChestA, Wood, 1)
            });

            TransactionResult result = coordinator.ReserveExact(request);
            TestAssert.Equal(TransactionOutcome.Reserved, result.Outcome,
                "A clock at its representable ceiling must not throw after mutating counters.");
            TransactionSnapshot snapshot;
            coordinator.TryGetTransaction(request.TransactionId, out snapshot);
            TestAssert.Equal(DateTimeOffset.MaxValue, snapshot.ReservationExpiresAtUtc.Value,
                "The lease deadline must saturate at the maximum representable UTC instant.");
            AssertInventory(coordinator, ChestA, Wood, 2, 1);
            TestAssert.Equal(1, coordinator.PruneExpired().ExpiredReservations,
                "A saturated deadline at the current ceiling must safely release on cleanup.");
            AssertInventory(coordinator, ChestA, Wood, 2, 0);
        }

        private static InMemoryTransactionCoordinator Coordinator(
            ITransactionAuthorizer authorizer = null,
            ITransactionClock clock = null,
            int maximumTrackedTransactions = TransactionCoordinatorOptions.DefaultMaximumTrackedTransactions,
            TimeSpan? reservationLeaseDuration = null,
            TimeSpan? terminalRetentionDuration = null) =>
            new InMemoryTransactionCoordinator(new TransactionCoordinatorOptions(
                authorizer ?? AllowAllTransactionAuthorizer.Instance,
                clock,
                maximumTrackedTransactions: maximumTrackedTransactions,
                reservationLeaseDuration: reservationLeaseDuration,
                terminalRetentionDuration: terminalRetentionDuration));

        private static ReservationRequest Request(string suffix, IEnumerable<ReservationLine> lines) =>
            new ReservationRequest(
                new TransactionId("txn-" + suffix),
                new CorrelationId("corr-" + suffix),
                new IdempotencyKey("idem-" + suffix),
                Player,
                lines);

        private static void AssertInventory(
            InMemoryTransactionCoordinator coordinator,
            EndpointId endpoint,
            ResourceId resource,
            long expectedOnHand,
            long expectedReserved)
        {
            EndpointInventorySnapshot snapshot;
            TestAssert.True(coordinator.TryGetEndpointSnapshot(endpoint, out snapshot), "Endpoint snapshot must exist.");
            TestAssert.Equal(expectedOnHand, snapshot.OnHand(resource), "On-hand inventory mismatch.");
            TestAssert.Equal(expectedReserved, snapshot.Reserved(resource), "Reserved inventory mismatch.");
        }

        private sealed class TestAuthorizer : ITransactionAuthorizer
        {
            private readonly Func<TransactionAuthorizationContext, bool> _allow;
            internal TestAuthorizer(Func<TransactionAuthorizationContext, bool> allow) => _allow = allow;
            public AuthorizationDecision Authorize(TransactionAuthorizationContext context) =>
                _allow(context) ? AuthorizationDecision.Allow() : AuthorizationDecision.Deny("Denied by test policy.");
        }

        private sealed class FakeClock : ITransactionClock
        {
            private long _utcTicks;
            internal FakeClock()
                : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { }
            internal FakeClock(DateTimeOffset initialUtc) => _utcTicks = initialUtc.UtcDateTime.Ticks;
            public DateTimeOffset UtcNow => new DateTimeOffset(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
            internal void Advance(TimeSpan duration) => Interlocked.Add(ref _utcTicks, duration.Ticks);
        }
    }
}
