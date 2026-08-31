# Changelog

## 1.0.0 - 2026-08-25

- Normalized the coordinated Thunderstore release identity and Foundation dependencies to 1.0.0.
- Preserved the accepted durable transaction, stable-identity, recovery, and checkpoint behavior.
- Defined deferred current-owner execution as handler-durable: an immutable replay key must be backed
  by persistent exact identity/lock/desired-state CAS evidence, `NotReady` is pre-write only, and
  ambiguous publication/readback returns a retryable handler failure rather than inventing success.

## 0.3.1 - 2026-08-22

- Migrated container ownership-return messages from routed sender IDs to Runic Persistence's exact
  direct-session RPC and added an exact current-server session proof on clients.
- Bounded ownership-return retry history to 4,096 entries with retry-window pruning and
  deterministic oldest eviction.
- Replaced repeated full identity-index rebuilds during sequential token publication with one
  bounded initial scan plus expected O(1) mutation updates and lookups.
- Added exact identity-field mutation tracking, world-reset invalidation, marker-capacity
  serialization, and immediate duplicate-token detection without trusting a stale cached owner.
- Raised the hard dependency floor to Runic Core 0.1.1 and normalized all coordinated release
  surfaces while retaining assembly identity `0.1.1.0` for existing binary consumers.

## 0.3.0 - 2026-08-21

- Added canonical 32-character Runic world-object tokens stored on authoritative station and
  container ZDOs. These tokens remain stable when Valheim assigns different numeric ZDOIDs while
  loading a saved world; a ZDOID is now only a current-load address and cache.
- Added bounded global token resolution with exact-owner verification, duplicate detection, an
  explicit 16,384-tag safety limit, and fail-closed missing, malformed, ambiguous, or unavailable
  outcomes.
- Upgraded durable station headers and endpoint claims to schema 2 so fresh operations persist
  stable station and endpoint tokens, order endpoints by token, and verify each resolved current ZDO
  before mutation or recovery.
- Retained raw-ID-exact schema-1 parsing for ordinary guarded recovery and added an explicit
  remapped-operation identity upgrade. The upgrade requires an exact immutable journal/hash,
  one-to-one physical header and claim bindings, synchronized server-owned endpoints, and live
  Before/After fingerprints. A bounded non-authoritative retry envelope preserves the proven
  old-ID-to-token mapping while claims upgrade idempotently; the stable schema-2 header is published
  last. Missing, conflicting, duplicate, or ambiguous evidence remains quarantined.
- Added bounded physical-claim discovery for journal-empty schema-1 orphan cleanup. It accepts only
  exact one-to-one loaded claim coverage for every old header binding and re-proves the required
  safe-state fingerprints; it never selects an endpoint by position or proximity. Only unpublished
  or proven terminal orphan states may clear, and incomplete evidence remains retained.
- Fresh durable operations reject raw-ID-only endpoints. Stable tokens are now required by adapters
  using the 0.3.0 durable identity APIs.
- Kept assembly identity `0.1.1.0` for binary compatibility with existing consumers. The plugin,
  file, informational, manifest, and package version is `0.3.0`.

## 0.2.0 - 2026-08-21

- Added `DurableOperationCoordinator`, a fail-closed station-header protocol for one- or two-container
  mutations with canonical numeric ZDO ordering, exact before/after fingerprints, SHA-256-bound
  journals, explicit journal-publication state, exact terminal proofs, and restart recovery.
- Added claims-first adoption for exact journals persisted by pre-coordinator releases. Exact
  partial claims are restart-idempotent, and each endpoint must still match its journaled before or
  after fingerprint before the published operation header is installed.
- Extended `DurableContainerSafety` with persistent endpoint claims that block unrelated Runic
  mutations while allowing only the matching recovery operation.
- Added cooperative client-to-server container ownership return plus authoritative persisted
  inventory synchronization; an active client-owned or open chest is deferred, never stolen.
- Guarded vanilla Open, Stack All, Take All, auto-destroy-empty, and destruction paths while an
  endpoint claim is unresolved, preserving the evidence required for exact crash reconciliation.
- Kept assembly identity `0.1.1.0` for binary compatibility with consumers of the original
  contracts and mutation gate. The plugin, file, informational, manifest, and package version is
  `0.2.0`; modules using the new durable APIs require that plugin floor explicitly.

## 0.1.1

- Added a process-wide non-reentrant external mutation lease shared by gameplay adapters, preventing
  synchronous inventory callbacks from entering another Runic mutation mid-transaction.

## 0.1.0

- Added bounded, engine-neutral container query and atomic transfer contracts for optional gameplay providers.
- Default coordinators now deny authorization unless a server-owned policy is supplied; permissive
  behavior requires an explicit `AllowAllTransactionAuthorizer`.
- Publish a policy-requiring coordinator factory for material reservations and stop advertising
  container query/transfer capabilities until those operations exist.
- Bound tracked transactions and fail closed with a stable capacity denial when the table is full.
- Add configurable reservation leases that atomically release the complete batch on expiry.
- Retain terminal transaction and principal-scoped idempotency tombstones for a bounded retry window,
  then prune both together through explicit or throttled automatic cleanup.
- Serialize expiry and commit on the transaction gate and reject reentrant commit while admission is
  still preparing.
- Advance endpoint versions for reservation and release mutations, including rollback and expiry.
- Saturate lease deadlines safely at the maximum representable UTC instant before changing counters.
- Add deterministic fake-clock, capacity, retention, expiry/commit race, reentrancy, version, and
  clock-ceiling tests.
- Add stable endpoint, resource, principal, transaction, correlation, and idempotency identifiers.
- Add a hard-bounded conservative container-query policy.
- Canonicalize exact multi-endpoint reservation lines and fingerprint equivalent requests
  deterministically.
- Add ordinal endpoint lock ordering and a concurrency-safe in-memory coordinator.
- Reserve complete batches or nothing, with fail-closed permission checks before availability is
  changed.
- Revalidate permission, authoritative on-hand counts, and reservation ownership at commit time.
- Make successful commits and rollbacks replay-safe and prevent a transaction from consuming twice.
- Release every reservation after cancellation or failed revalidation without partially consuming
  another endpoint.
- Add stable denial, result, snapshot, and bounded audit contracts.
- Publish `materials.reserve` and `materials.consume` capability services through Runic Core.
- Add pure tests for deterministic ordering, insufficient batches, permission denial, a concurrent
  last-resource race, explicit rollback, revalidation rollback, permission revocation, and
  idempotent consumption.
