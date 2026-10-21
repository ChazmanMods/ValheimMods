using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RunicTransactions.Contracts;

namespace RunicTransactions.Coordination
{
    public sealed class InMemoryTransactionCoordinator : ITransactionCoordinator
    {
        private readonly TransactionCoordinatorOptions _options;
        private readonly ConcurrentDictionary<EndpointId, EndpointState> _endpoints =
            new ConcurrentDictionary<EndpointId, EndpointState>();
        private readonly object _identityGate = new object();
        private readonly Dictionary<TransactionId, TransactionEntry> _transactions =
            new Dictionary<TransactionId, TransactionEntry>();
        private readonly Dictionary<ScopedIdempotencyKey, TransactionEntry> _idempotency =
            new Dictionary<ScopedIdempotencyKey, TransactionEntry>();
        private readonly ConcurrentQueue<TransactionAuditRecord> _audit =
            new ConcurrentQueue<TransactionAuditRecord>();
        private static readonly long AutomaticPruneIntervalTicks = TimeSpan.FromSeconds(1).Ticks;
        private long _auditSequence;
        private long _nextAutomaticPruneUtcTicks;

        public InMemoryTransactionCoordinator(TransactionCoordinatorOptions options = null)
        {
            _options = options ?? new TransactionCoordinatorOptions();
            _nextAutomaticPruneUtcTicks = _options.Clock.UtcNow.UtcDateTime.Ticks;
        }

        public ContainerQueryPolicy QueryPolicy => _options.QueryPolicy;

        public int TrackedTransactionCount
        {
            get
            {
                lock (_identityGate) return _transactions.Count;
            }
        }

        public void RegisterOrReplaceEndpoint(EndpointId endpoint, IEnumerable<ResourceBalance> balances)
        {
            if (!endpoint.IsValid) throw new ArgumentException("Endpoint identifier is invalid.", nameof(endpoint));
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            Dictionary<ResourceId, long> replacement = CanonicalBalances(balances);
            EndpointState state = _endpoints.GetOrAdd(endpoint, value => new EndpointState(value));
            lock (state.Gate)
            {
                state.OnHand.Clear();
                foreach (KeyValuePair<ResourceId, long> pair in replacement) state.OnHand.Add(pair.Key, pair.Value);
                state.Version++;
            }
        }

        public bool TryGetEndpointSnapshot(EndpointId endpoint, out EndpointInventorySnapshot snapshot)
        {
            EndpointState state;
            if (!_endpoints.TryGetValue(endpoint, out state))
            {
                snapshot = null;
                return false;
            }

            lock (state.Gate)
            {
                ResourceId[] resources = state.OnHand.Keys
                    .Concat(state.Reserved.Keys)
                    .Distinct()
                    .OrderBy(resource => resource)
                    .ToArray();
                var values = new ResourceInventorySnapshot[resources.Length];
                for (int i = 0; i < resources.Length; i++)
                {
                    ResourceId resource = resources[i];
                    values[i] = new ResourceInventorySnapshot(
                        resource,
                        Read(state.OnHand, resource),
                        Read(state.Reserved, resource));
                }
                snapshot = new EndpointInventorySnapshot(endpoint, state.Version, values);
                return true;
            }
        }

        public TransactionResult ReserveExact(ReservationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            PruneExpiredIfDue();

            // Identity admission is serialized. Endpoint work remains independently
            // concurrent, while a key can never be admitted twice between lookup and insertion.
            lock (_identityGate)
            {
                TransactionEntry replay;
                var scopedKey = new ScopedIdempotencyKey(request.Principal, request.IdempotencyKey);
                if (_idempotency.TryGetValue(scopedKey, out replay))
                {
                    lock (replay.Gate)
                    {
                        if (!string.Equals(replay.Request.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
                            return IdentityConflict(request, TransactionDenialCode.IdempotencyConflict,
                                "The idempotency key is already bound to a different reservation payload.");
                        if (replay.LastResult == null)
                            return IdentityConflict(request, TransactionDenialCode.TransactionInProgress,
                                "The idempotent reservation is still being prepared; retry later.");
                        return replay.LastResult.AsReplay(request.CorrelationId);
                    }
                }

                if (_transactions.TryGetValue(request.TransactionId, out replay))
                {
                    lock (replay.Gate)
                    {
                        if (!string.Equals(replay.Request.Fingerprint, request.Fingerprint, StringComparison.Ordinal) ||
                            !replay.Request.Principal.Equals(request.Principal) ||
                            !replay.Request.IdempotencyKey.Equals(request.IdempotencyKey))
                            return IdentityConflict(request, TransactionDenialCode.TransactionConflict,
                                "The transaction identifier is already bound to another request.");
                        if (replay.LastResult == null)
                            return IdentityConflict(request, TransactionDenialCode.TransactionInProgress,
                                "The transaction is still being prepared; retry later.");
                        return replay.LastResult.AsReplay(request.CorrelationId);
                    }
                }

                if (_transactions.Count >= _options.MaximumTrackedTransactions)
                    return IdentityConflict(request, TransactionDenialCode.CoordinatorCapacityReached,
                        "The coordinator has reached its bounded tracked-transaction capacity; retry after cleanup.");

                var entry = new TransactionEntry(request);
                _transactions.Add(request.TransactionId, entry);
                _idempotency.Add(scopedKey, entry);
                lock (entry.Gate) ReserveAdmitted(entry);
                return entry.LastResult;
            }
        }

        public TransactionResult CommitOnce(TransactionId transactionId, CorrelationId correlationId)
        {
            TransactionEntry entry;
            lock (_identityGate)
                _transactions.TryGetValue(transactionId, out entry);

            if (entry == null)
                return UnknownTransaction(transactionId, correlationId, TransactionAuditAction.Commit);

            lock (entry.Gate)
            {
                TransactionResult expired = ExpireReservationLocked(entry, correlationId, _options.Clock.UtcNow);
                if (expired != null) return expired;
                if (entry.State == TransactionState.Committed)
                    return entry.LastResult.AsReplay(correlationId);
                if (IsLeaseExpiry(entry))
                    return entry.LastResult.AsReplay(correlationId);
                if (entry.State == TransactionState.RolledBack || entry.State == TransactionState.Failed)
                    return InvalidState(entry, correlationId, TransactionAuditAction.Commit,
                        $"Cannot commit a transaction in state {entry.State}.");
                if (entry.State != TransactionState.Reserved)
                    return InvalidState(entry, correlationId, TransactionAuditAction.Commit,
                        $"Cannot commit a transaction in state {entry.State}.");

                List<EndpointState> states;
                TransactionDenial lookupDenial;
                if (!TryResolveStates(entry.Request.Lines, out states, out lookupDenial))
                    return FailReservedEntry(entry, correlationId, lookupDenial, states, TransactionAuditAction.Commit);

                return WithEndpointLocks(states, () => CommitLocked(entry, correlationId, states));
            }
        }

        public TransactionResult Rollback(TransactionId transactionId, CorrelationId correlationId, string reason)
        {
            TransactionEntry entry;
            lock (_identityGate)
                _transactions.TryGetValue(transactionId, out entry);

            if (entry == null)
                return UnknownTransaction(transactionId, correlationId, TransactionAuditAction.Rollback);

            lock (entry.Gate)
            {
                TransactionResult expired = ExpireReservationLocked(entry, correlationId, _options.Clock.UtcNow);
                if (expired != null) return expired;
                if (entry.State == TransactionState.RolledBack)
                    return entry.LastResult.AsReplay(correlationId);
                if (entry.State == TransactionState.Committed)
                    return InvalidState(entry, correlationId, TransactionAuditAction.Rollback,
                        "Committed consumption cannot be rolled back by the in-memory reservation coordinator.");
                if (entry.State == TransactionState.Failed)
                    return entry.LastResult.AsReplay(correlationId);
                if (entry.State != TransactionState.Reserved)
                    return InvalidState(entry, correlationId, TransactionAuditAction.Rollback,
                        $"Cannot roll back a transaction in state {entry.State}.");

                List<EndpointState> states;
                TransactionDenial denial;
                if (!TryResolveStates(entry.Request.Lines, out states, out denial))
                    return FailReservedEntry(entry, correlationId, denial, states, TransactionAuditAction.Rollback);

                return WithEndpointLocks(states, () =>
                {
                    ReleaseReservations(entry, states);
                    entry.State = TransactionState.RolledBack;
                    entry.TerminalAtUtc = _options.Clock.UtcNow;
                    string detail = string.IsNullOrWhiteSpace(reason) ? "Reservation rolled back." : reason;
                    long sequence = AddAudit(entry, correlationId, TransactionAuditAction.Rollback,
                        TransactionOutcome.RolledBack, TransactionDenialCode.None, detail);
                    entry.LastResult = new TransactionResult(entry.Request.TransactionId, correlationId,
                        TransactionOutcome.RolledBack, entry.State, entry.Request.Lines, null, false, sequence);
                    return entry.LastResult;
                });
            }
        }

        public bool TryGetTransaction(TransactionId transactionId, out TransactionSnapshot snapshot)
        {
            TransactionEntry entry;
            lock (_identityGate)
                _transactions.TryGetValue(transactionId, out entry);
            if (entry == null)
            {
                snapshot = null;
                return false;
            }

            lock (entry.Gate)
            {
                snapshot = new TransactionSnapshot(
                    entry.Request.TransactionId,
                    entry.Request.CorrelationId,
                    entry.Request.IdempotencyKey,
                    entry.Request.Principal,
                    entry.State,
                    entry.Request.Lines,
                    entry.ReservationExpiresAtUtc,
                    entry.TerminalAtUtc);
                return true;
            }
        }

        public IReadOnlyList<TransactionAuditRecord> GetAuditTrail(TransactionId transactionId)
        {
            return _audit.Where(record => record.TransactionId.Equals(transactionId))
                .OrderBy(record => record.Sequence)
                .ToArray();
        }

        public TransactionPruneResult PruneExpired()
        {
            int expiredReservations = 0;
            int removedTransactions = 0;
            DateTimeOffset now = _options.Clock.UtcNow;

            lock (_identityGate)
            {
                TransactionEntry[] ordered = _transactions
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value)
                    .ToArray();

                foreach (TransactionEntry entry in ordered)
                {
                    lock (entry.Gate)
                    {
                        TransactionResult expiration = ExpireReservationLocked(
                            entry,
                            entry.Request.CorrelationId,
                            now);
                        if (expiration != null && IsLeaseExpiry(entry))
                            expiredReservations++;
                    }
                }

                foreach (TransactionEntry entry in ordered)
                {
                    bool remove;
                    lock (entry.Gate)
                    {
                        remove = IsTerminalState(entry.State) &&
                                 entry.TerminalAtUtc.HasValue &&
                                 now - entry.TerminalAtUtc.Value >= _options.TerminalRetentionDuration;
                    }
                    if (!remove) continue;

                    if (_transactions.Remove(entry.Request.TransactionId))
                    {
                        var scopedKey = new ScopedIdempotencyKey(
                            entry.Request.Principal,
                            entry.Request.IdempotencyKey);
                        TransactionEntry mapped;
                        if (_idempotency.TryGetValue(scopedKey, out mapped) && ReferenceEquals(mapped, entry))
                            _idempotency.Remove(scopedKey);
                        removedTransactions++;
                    }
                }

                Interlocked.Exchange(
                    ref _nextAutomaticPruneUtcTicks,
                    AddTicksSaturated(now.UtcDateTime.Ticks, AutomaticPruneIntervalTicks));
                return new TransactionPruneResult(
                    expiredReservations,
                    removedTransactions,
                    _transactions.Count);
            }
        }

        private void PruneExpiredIfDue()
        {
            long nowTicks = _options.Clock.UtcNow.UtcDateTime.Ticks;
            while (true)
            {
                long nextTicks = Interlocked.Read(ref _nextAutomaticPruneUtcTicks);
                if (nowTicks < nextTicks) return;
                long replacement = AddTicksSaturated(nowTicks, AutomaticPruneIntervalTicks);
                if (Interlocked.CompareExchange(
                        ref _nextAutomaticPruneUtcTicks,
                        replacement,
                        nextTicks) == nextTicks)
                {
                    PruneExpired();
                    return;
                }
            }
        }

        private void ReserveAdmitted(TransactionEntry entry)
        {
            DateTimeOffset reservationDeadline = AddDurationSaturated(
                _options.Clock.UtcNow,
                _options.ReservationLeaseDuration);
            List<EndpointState> states;
            TransactionDenial lookupDenial;
            if (!TryResolveStates(entry.Request.Lines, out states, out lookupDenial))
            {
                DenyReservation(entry, lookupDenial);
                return;
            }

            WithEndpointLocks(states, () =>
            {
                foreach (ReservationLine line in entry.Request.Lines)
                {
                    TransactionDenial permission = Authorize(entry, line, entry.Request.CorrelationId,
                        AuthorizationPhase.Prepare, TransactionOperation.ReserveMaterials);
                    if (permission != null)
                    {
                        DenyReservation(entry, permission);
                        return 0;
                    }
                    EndpointState state = StateFor(states, line.Endpoint);
                    long onHand = Read(state.OnHand, line.Resource);
                    long reserved = Read(state.Reserved, line.Resource);
                    if (reserved > onHand || line.Amount > onHand - reserved)
                    {
                        DenyReservation(entry, new TransactionDenial(
                            TransactionDenialCode.InsufficientResources,
                            "The authorized endpoint does not have the exact requested amount available.",
                            line.Endpoint,
                            line.Resource));
                        return 0;
                    }
                }

                var applied = new List<ReservationLine>();
                try
                {
                    foreach (ReservationLine line in entry.Request.Lines)
                    {
                        EndpointState state = StateFor(states, line.Endpoint);
                        state.Reserved[line.Resource] = checked(Read(state.Reserved, line.Resource) + line.Amount);
                        applied.Add(line);
                    }
                }
                catch (Exception exception)
                {
                    foreach (ReservationLine line in applied)
                    {
                        EndpointState state = StateFor(states, line.Endpoint);
                        state.Reserved[line.Resource] = Math.Max(0L, Read(state.Reserved, line.Resource) - line.Amount);
                    }
                    DenyReservation(entry, new TransactionDenial(
                        TransactionDenialCode.InternalFailure,
                        $"Reservation application failed safely: {exception.GetType().Name}."));
                    return 0;
                }

                entry.State = TransactionState.Reserved;
                entry.ReservationExpiresAtUtc = reservationDeadline;
                foreach (EndpointState state in states) BumpVersion(state);
                long sequence = AddAudit(entry, entry.Request.CorrelationId, TransactionAuditAction.Reserve,
                    TransactionOutcome.Reserved, TransactionDenialCode.None,
                    $"Reserved {entry.Request.Lines.Count} canonical resource line(s).");
                entry.LastResult = new TransactionResult(entry.Request.TransactionId, entry.Request.CorrelationId,
                    TransactionOutcome.Reserved, entry.State, entry.Request.Lines, null, false, sequence);
                return 0;
            });
        }

        private TransactionResult CommitLocked(TransactionEntry entry, CorrelationId correlationId, List<EndpointState> states)
        {
            foreach (ReservationLine line in entry.Request.Lines)
            {
                TransactionDenial permission = Authorize(entry, line, correlationId,
                    AuthorizationPhase.Revalidate, TransactionOperation.ConsumeMaterials);
                if (permission != null)
                    return FailAndReleaseLocked(entry, correlationId, permission, states);

                EndpointState state = StateFor(states, line.Endpoint);
                if (Read(state.Reserved, line.Resource) < line.Amount || Read(state.OnHand, line.Resource) < line.Amount)
                {
                    return FailAndReleaseLocked(entry, correlationId, new TransactionDenial(
                        TransactionDenialCode.RevalidationFailed,
                        "Authoritative inventory changed after reservation; nothing was consumed.",
                        line.Endpoint,
                        line.Resource), states);
                }
            }

            var snapshots = new List<MutationSnapshot>(entry.Request.Lines.Count);
            var versions = states.ToDictionary(state => state, state => state.Version);
            try
            {
                foreach (ReservationLine line in entry.Request.Lines)
                {
                    EndpointState state = StateFor(states, line.Endpoint);
                    long oldOnHand = Read(state.OnHand, line.Resource);
                    long oldReserved = Read(state.Reserved, line.Resource);
                    snapshots.Add(new MutationSnapshot(state, line.Resource, oldOnHand, oldReserved));
                    state.OnHand[line.Resource] = checked(oldOnHand - line.Amount);
                    state.Reserved[line.Resource] = checked(oldReserved - line.Amount);
                }
                foreach (EndpointState state in states) BumpVersion(state);
            }
            catch (Exception exception)
            {
                foreach (MutationSnapshot snapshot in snapshots)
                {
                    snapshot.State.OnHand[snapshot.Resource] = snapshot.OnHand;
                    snapshot.State.Reserved[snapshot.Resource] = snapshot.Reserved;
                }
                foreach (KeyValuePair<EndpointState, long> pair in versions) pair.Key.Version = pair.Value;
                return FailAndReleaseLocked(entry, correlationId, new TransactionDenial(
                    TransactionDenialCode.InternalFailure,
                    $"Commit failed and was rolled back before returning: {exception.GetType().Name}."), states);
            }

            entry.State = TransactionState.Committed;
            entry.TerminalAtUtc = _options.Clock.UtcNow;
            long sequence = AddAudit(entry, correlationId, TransactionAuditAction.Commit,
                TransactionOutcome.Committed, TransactionDenialCode.None,
                $"Committed {entry.Request.Lines.Count} canonical resource line(s) exactly once.");
            entry.LastResult = new TransactionResult(entry.Request.TransactionId, correlationId,
                TransactionOutcome.Committed, entry.State, entry.Request.Lines, null, false, sequence);
            return entry.LastResult;
        }

        private TransactionResult FailReservedEntry(
            TransactionEntry entry,
            CorrelationId correlationId,
            TransactionDenial denial,
            List<EndpointState> resolvedStates,
            TransactionAuditAction action)
        {
            // Endpoints are never removed by this implementation, so this is defensive. Release
            // every resolvable reservation and fail closed if a future adapter violates that rule.
            if (resolvedStates != null && resolvedStates.Count > 0)
                return WithEndpointLocks(resolvedStates, () => FailAndReleaseLocked(entry, correlationId, denial, resolvedStates, action));

            entry.State = TransactionState.Failed;
            entry.TerminalAtUtc = _options.Clock.UtcNow;
            long sequence = AddAudit(entry, correlationId, action, TransactionOutcome.Failed,
                denial.Code, denial.Reason);
            entry.LastResult = new TransactionResult(entry.Request.TransactionId, correlationId,
                TransactionOutcome.Failed, entry.State, entry.Request.Lines, denial, false, sequence);
            return entry.LastResult;
        }

        private TransactionResult FailAndReleaseLocked(
            TransactionEntry entry,
            CorrelationId correlationId,
            TransactionDenial denial,
            List<EndpointState> states,
            TransactionAuditAction action = TransactionAuditAction.Commit)
        {
            ReleaseReservations(entry, states);
            entry.State = TransactionState.Failed;
            entry.TerminalAtUtc = _options.Clock.UtcNow;
            long sequence = AddAudit(entry, correlationId, action, TransactionOutcome.Failed, denial.Code, denial.Reason);
            entry.LastResult = new TransactionResult(entry.Request.TransactionId, correlationId,
                TransactionOutcome.Failed, entry.State, entry.Request.Lines, denial, false, sequence);
            return entry.LastResult;
        }

        private void ReleaseReservations(TransactionEntry entry, List<EndpointState> states)
        {
            foreach (ReservationLine line in entry.Request.Lines)
            {
                EndpointState state = StateFor(states, line.Endpoint);
                long current = Read(state.Reserved, line.Resource);
                state.Reserved[line.Resource] = current >= line.Amount ? current - line.Amount : 0L;
            }
            foreach (EndpointState state in states) BumpVersion(state);
        }

        private void DenyReservation(TransactionEntry entry, TransactionDenial denial)
        {
            entry.State = TransactionState.Failed;
            entry.TerminalAtUtc = _options.Clock.UtcNow;
            long sequence = AddAudit(entry, entry.Request.CorrelationId, TransactionAuditAction.Reserve,
                TransactionOutcome.Denied, denial.Code, denial.Reason);
            entry.LastResult = new TransactionResult(entry.Request.TransactionId, entry.Request.CorrelationId,
                TransactionOutcome.Denied, entry.State, entry.Request.Lines, denial, false, sequence);
        }

        private TransactionResult ExpireReservationLocked(
            TransactionEntry entry,
            CorrelationId correlationId,
            DateTimeOffset now)
        {
            if (entry.State != TransactionState.Reserved ||
                !entry.ReservationExpiresAtUtc.HasValue ||
                now < entry.ReservationExpiresAtUtc.Value)
                return null;

            List<EndpointState> states;
            TransactionDenial ignored;
            if (!TryResolveStates(entry.Request.Lines, out states, out ignored))
            {
                // Never make a Reserved entry terminal until every reservation can be released
                // under its canonical endpoint locks. Endpoints are not removed by this adapter,
                // but a future adapter that violates that invariant must remain fail-closed.
                var defensiveDenial = new TransactionDenial(
                    TransactionDenialCode.InternalFailure,
                    "Lease release was deferred because an endpoint adapter was unavailable.");
                long defensiveSequence = AddAudit(entry, correlationId, TransactionAuditAction.Expire,
                    TransactionOutcome.Failed, defensiveDenial.Code, defensiveDenial.Reason);
                return new TransactionResult(entry.Request.TransactionId, correlationId,
                    TransactionOutcome.Failed, entry.State, entry.Request.Lines,
                    defensiveDenial, false, defensiveSequence);
            }

            return WithEndpointLocks(states, () =>
            {
                ReleaseReservations(entry, states);
                var denial = new TransactionDenial(
                    TransactionDenialCode.ReservationLeaseExpired,
                    "The reservation lease expired before commit; all reserved resources were released.");
                entry.State = TransactionState.Failed;
                entry.TerminalAtUtc = now;
                long sequence = AddAudit(entry, correlationId, TransactionAuditAction.Expire,
                    TransactionOutcome.Failed, denial.Code, denial.Reason);
                entry.LastResult = new TransactionResult(entry.Request.TransactionId, correlationId,
                    TransactionOutcome.Failed, entry.State, entry.Request.Lines, denial, false, sequence);
                return entry.LastResult;
            });
        }

        private static bool IsLeaseExpiry(TransactionEntry entry) =>
            entry.State == TransactionState.Failed &&
            entry.LastResult != null &&
            entry.LastResult.Denial != null &&
            entry.LastResult.Denial.Code == TransactionDenialCode.ReservationLeaseExpired;

        private static bool IsTerminalState(TransactionState state) =>
            state == TransactionState.Committed ||
            state == TransactionState.RolledBack ||
            state == TransactionState.Failed;

        private static long AddTicksSaturated(long value, long addition) =>
            value > DateTimeOffset.MaxValue.UtcDateTime.Ticks - addition
                ? DateTimeOffset.MaxValue.UtcDateTime.Ticks
                : value + addition;

        private static DateTimeOffset AddDurationSaturated(DateTimeOffset value, TimeSpan duration) =>
            new DateTimeOffset(
                AddTicksSaturated(value.UtcDateTime.Ticks, duration.Ticks),
                TimeSpan.Zero);

        private static void BumpVersion(EndpointState state)
        {
            if (state.Version < long.MaxValue) state.Version++;
        }

        private TransactionDenial Authorize(
            TransactionEntry entry,
            ReservationLine line,
            CorrelationId correlationId,
            AuthorizationPhase phase,
            TransactionOperation operation)
        {
            try
            {
                AuthorizationDecision decision = _options.Authorizer.Authorize(new TransactionAuthorizationContext(
                    entry.Request.TransactionId,
                    correlationId,
                    entry.Request.Principal,
                    line.Endpoint,
                    line.Resource,
                    line.Amount,
                    operation,
                    phase));
                return decision.Allowed ? null : new TransactionDenial(
                    TransactionDenialCode.PermissionDenied,
                    decision.Reason,
                    line.Endpoint,
                    line.Resource);
            }
            catch (Exception exception)
            {
                return new TransactionDenial(
                    TransactionDenialCode.PermissionDenied,
                    $"Authorization failed closed: {exception.GetType().Name}.",
                    line.Endpoint,
                    line.Resource);
            }
        }

        private bool TryResolveStates(
            IReadOnlyList<ReservationLine> lines,
            out List<EndpointState> states,
            out TransactionDenial denial)
        {
            IReadOnlyList<EndpointId> ordered = DeterministicLockOrder.OrderEndpoints(lines);
            states = new List<EndpointState>(ordered.Count);
            foreach (EndpointId endpoint in ordered)
            {
                EndpointState state;
                if (!_endpoints.TryGetValue(endpoint, out state))
                {
                    denial = new TransactionDenial(TransactionDenialCode.EndpointNotFound,
                        "A requested endpoint is not registered.", endpoint);
                    return false;
                }
                states.Add(state);
            }
            denial = null;
            return true;
        }

        private TransactionResult IdentityConflict(ReservationRequest request, TransactionDenialCode code, string reason)
        {
            var denial = new TransactionDenial(code, reason);
            long sequence = AddAudit(request.TransactionId, request.CorrelationId, request.Principal,
                TransactionAuditAction.Reserve, TransactionOutcome.Denied, code, reason);
            return new TransactionResult(request.TransactionId, request.CorrelationId,
                TransactionOutcome.Denied, TransactionState.Failed, request.Lines, denial, false, sequence);
        }

        private TransactionResult UnknownTransaction(
            TransactionId transactionId,
            CorrelationId correlationId,
            TransactionAuditAction action)
        {
            var denial = new TransactionDenial(TransactionDenialCode.TransactionNotFound,
                "Transaction is not known to this coordinator instance.");
            long sequence = AddAudit(transactionId, correlationId, default(PrincipalId), action,
                TransactionOutcome.Denied, denial.Code, denial.Reason);
            return new TransactionResult(transactionId, correlationId, TransactionOutcome.Denied,
                TransactionState.Unknown, null, denial, false, sequence);
        }

        private TransactionResult InvalidState(
            TransactionEntry entry,
            CorrelationId correlationId,
            TransactionAuditAction action,
            string reason)
        {
            var denial = new TransactionDenial(TransactionDenialCode.InvalidState, reason);
            long sequence = AddAudit(entry, correlationId, action, TransactionOutcome.Denied, denial.Code, reason);
            return new TransactionResult(entry.Request.TransactionId, correlationId,
                TransactionOutcome.Denied, entry.State, entry.Request.Lines, denial, false, sequence);
        }

        private long AddAudit(
            TransactionEntry entry,
            CorrelationId correlationId,
            TransactionAuditAction action,
            TransactionOutcome outcome,
            TransactionDenialCode denialCode,
            string detail) => AddAudit(entry.Request.TransactionId, correlationId, entry.Request.Principal,
                action, outcome, denialCode, detail);

        private long AddAudit(
            TransactionId transactionId,
            CorrelationId correlationId,
            PrincipalId principal,
            TransactionAuditAction action,
            TransactionOutcome outcome,
            TransactionDenialCode denialCode,
            string detail)
        {
            long sequence = Interlocked.Increment(ref _auditSequence);
            _audit.Enqueue(new TransactionAuditRecord(sequence, _options.Clock.UtcNow, transactionId,
                correlationId, principal, action, outcome, denialCode, detail));
            while (_audit.Count > _options.MaximumAuditRecords)
            {
                TransactionAuditRecord ignored;
                if (!_audit.TryDequeue(out ignored)) break;
            }
            return sequence;
        }

        private static T WithEndpointLocks<T>(IReadOnlyList<EndpointState> states, Func<T> action)
        {
            int acquired = 0;
            try
            {
                for (; acquired < states.Count; acquired++) Monitor.Enter(states[acquired].Gate);
                return action();
            }
            finally
            {
                for (int i = acquired - 1; i >= 0; i--) Monitor.Exit(states[i].Gate);
            }
        }

        private static EndpointState StateFor(List<EndpointState> states, EndpointId endpoint)
        {
            // States are in canonical endpoint order. Binary search keeps behavior deterministic
            // even for a large exact reservation batch.
            int low = 0;
            int high = states.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = states[middle].Id.CompareTo(endpoint);
                if (comparison == 0) return states[middle];
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            throw new InvalidOperationException("Resolved endpoint state was unexpectedly absent.");
        }

        private static long Read(Dictionary<ResourceId, long> values, ResourceId resource)
        {
            long value;
            return values.TryGetValue(resource, out value) ? value : 0L;
        }

        private static Dictionary<ResourceId, long> CanonicalBalances(IEnumerable<ResourceBalance> balances)
        {
            var result = new Dictionary<ResourceId, long>();
            foreach (ResourceBalance balance in balances)
            {
                if (!balance.Resource.IsValid || balance.Amount < 0)
                    throw new ArgumentException("Every resource balance must have a valid identifier and non-negative amount.", nameof(balances));
                if (result.ContainsKey(balance.Resource))
                    throw new ArgumentException($"Duplicate resource balance: {balance.Resource}.", nameof(balances));
                result.Add(balance.Resource, balance.Amount);
            }
            return result;
        }

        private sealed class EndpointState
        {
            internal EndpointState(EndpointId id) => Id = id;
            internal EndpointId Id { get; }
            internal object Gate { get; } = new object();
            internal Dictionary<ResourceId, long> OnHand { get; } = new Dictionary<ResourceId, long>();
            internal Dictionary<ResourceId, long> Reserved { get; } = new Dictionary<ResourceId, long>();
            internal long Version { get; set; }
        }

        private sealed class TransactionEntry
        {
            internal TransactionEntry(ReservationRequest request)
            {
                Request = request;
                State = TransactionState.Preparing;
            }
            internal object Gate { get; } = new object();
            internal ReservationRequest Request { get; }
            internal TransactionState State { get; set; }
            internal TransactionResult LastResult { get; set; }
            internal DateTimeOffset? ReservationExpiresAtUtc { get; set; }
            internal DateTimeOffset? TerminalAtUtc { get; set; }
        }

        private readonly struct ScopedIdempotencyKey : IEquatable<ScopedIdempotencyKey>
        {
            internal ScopedIdempotencyKey(PrincipalId principal, IdempotencyKey key)
            {
                Principal = principal;
                Key = key;
            }
            private PrincipalId Principal { get; }
            private IdempotencyKey Key { get; }
            public bool Equals(ScopedIdempotencyKey other) => Principal.Equals(other.Principal) && Key.Equals(other.Key);
            public override bool Equals(object obj) => obj is ScopedIdempotencyKey other && Equals(other);
            public override int GetHashCode() => unchecked((Principal.GetHashCode() * 397) ^ Key.GetHashCode());
        }

        private readonly struct MutationSnapshot
        {
            internal MutationSnapshot(EndpointState state, ResourceId resource, long onHand, long reserved)
            {
                State = state;
                Resource = resource;
                OnHand = onHand;
                Reserved = reserved;
            }
            internal EndpointState State { get; }
            internal ResourceId Resource { get; }
            internal long OnHand { get; }
            internal long Reserved { get; }
        }
    }
}
