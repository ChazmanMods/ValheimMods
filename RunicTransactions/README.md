# Runic Transactions

The assembly also carries the engine-neutral, bounded `IContainerQueryService` and
`IContainerTransferService` contracts. Runic Transactions does **not** advertise those
capabilities itself; a gameplay provider such as Runic Storage must implement and register
them. Keeping the types here lets Crafting and Production discover Storage without either
gameplay mod becoming a hard dependency of another.

Runic Transactions is the all-or-nothing resource mutation foundation for the Runic Valheim mod
suite. It gives Storage, Crafting, Production, Building, and other trusted server-side adapters one
deterministic way to reserve exact material amounts across several endpoints and consume them once.

Version 1.0.0 is an infrastructure plugin. It does not add a player-facing panel or automate a
container on its own. It does enforce unresolved durable endpoint claims created by trusted Runic
adapters so players and other modules cannot erase the exact evidence needed for recovery.
It has hard runtime dependencies on Runic Core 1.0.0+ and Runic Persistence 1.0.0+; Persistence
provides the direct-session transport used for ownership return.
The plugin, file, informational, manifest, and package version is 1.0.0; the unsigned assembly
identity intentionally remains 1.0.0.0 so existing consumers of the original contracts keep binary
compatibility. The 1.0.0 release maintains the bounded identity index incrementally, so normal
token publication and lookup use expected constant work after one bounded initial scan. A module
shipping against this coordinated release must require plugin version 1.0.0.

## Guarantees

- Stable ordinal endpoint and resource identifiers; no Unity instance ID is treated as a durable
  identity.
- Explicit transaction, correlation, principal, and principal-scoped idempotency identifiers.
- Duplicate request lines are combined and sorted into one canonical exact allocation.
- Every endpoint lock is acquired in ordinal endpoint-ID order.
- Permission and availability checks complete for the batch before any reservation counter changes.
- Commit revalidates permissions, reservations, and current on-hand balances while all affected
  endpoint locks are held.
- A successful transaction commits at most once. Equivalent idempotent retries replay the admitted
  result instead of reserving again.
- Cancellation releases the entire batch. Failed commit revalidation releases the entire batch and
  consumes nothing.
- Reservations have bounded leases. Expiry is serialized against commit on the same transaction
  gate and releases every endpoint under the canonical lock order before becoming terminal.
- Tracked transactions have a hard capacity, and completed transaction/idempotency tombstones are
  retained for a bounded retry window before they are pruned together.
- Stable denial codes and bounded, sequence-ordered audit records make failures inspectable without
  relying on log-message parsing.
- Endpoint versions advance for on-hand, reservation, commit, rollback, and expiry mutations, so an
  adapter can observe changes to both inventory and availability.
- `RunicMutationGate` is a process-wide, non-reentrant lease for short synchronous external-game
  mutations. Runic adapters share it so an inline inventory callback cannot enter a second module
  while a source/destination operation is only half applied.
- `DurableOperationCoordinator` binds one station intent to one or two exact container endpoints,
  orders claims by canonical Runic world-object token, and hashes the adapter's immutable exact
  journal before any inventory changes.
- Every new durable station and container identity is a canonical Runic token stored on that
  object's ZDO. Valheim's numeric ZDOID is treated only as a current-world cache because Valheim can
  assign a different numeric ID when the world is loaded again.
- Token lookup is globally bounded and proves exactly one tagged ZDO. A missing, malformed,
  duplicate, or over-capacity identity fails closed instead of selecting a nearby object.
- A durable endpoint claim survives restart and blocks unrelated Runic mutations plus vanilla Open,
  Stack All, Take All, auto-destroy-empty, and destruction until exact recovery reaches a proven
  terminal state.
- Server-side synchronization reloads and proves the persisted inventory before use. A container
  owned or opened by a client is deferred and returned cooperatively after it closes.

## Capability surface

The BepInEx plugin declares protocol `1.0` and registers an
`ITransactionCoordinatorFactory` under these Runic Core capabilities:

| Capability | Contract purpose |
|---|---|
| `materials.reserve` | Create a policy-backed coordinator that reserves a complete allocation or nothing. |
| `materials.consume` | Revalidate and commit an admitted allocation exactly once. |
| `transactions.durable-composite` | Use the one Transactions-owned world filesystem WAL for ordered multi-endpoint operations. |
| `transactions.durable-world-object` | Register typed Create, Remove, or Update world-object providers, including exact-owner deferred execution. |

The Core IDs `container.query` and `container.transfer` remain reserved contracts, but Transactions
does not advertise them itself. The current Storage adapter publishes its implemented bounded query
service; crash-safe cross-container transfer remains deliberately unadvertised.

`ContainerQueryPolicy.ConservativeDefault` limits scans to 10 metres, 64 examined candidates, 32
returned endpoints, and 128 resource kinds per endpoint, and requires both authorization and line of
sight. World discovery remains the responsibility of the Valheim-facing adapter; the transaction
library never performs an unbounded scene scan.

## Coordinator use

Resolve `ITransactionCoordinatorFactory` and give `Create` a server-owned
`ITransactionAuthorizer`. The authorizer is called for every exact line during prepare and again
during commit revalidation. Exceptions fail closed as permission denials. Omitting the authorizer
also fails closed through `DenyAllTransactionAuthorizer`; tests and trusted single-player adapters
must opt into `AllowAllTransactionAuthorizer` explicitly.

The intended adapter flow is:

1. Resolve a server-verified principal and locally discover candidate containers within the query
   policy.
2. Check Valheim ownership, ward, and object policy through Runic Permissions.
3. Synchronize authoritative endpoint balances with `RegisterOrReplaceEndpoint`.
4. Submit one `ReservationRequest` containing the exact endpoint/resource allocation.
5. Acquire `RunicMutationGate` before taking the game-side snapshot; fail closed if another Runic
   mutation is active.
6. Perform the gameplay operation only after `ReserveExact` succeeds.
7. Call `CommitOnce` to consume the allocation, or `Rollback` on any cancelled operation, then
   dispose the mutation lease.

Callers should persist their transaction and idempotency IDs across RPC retries. Creating a fresh key
for a retry intentionally represents a new operation.

## Durable Valheim operation use

The durable Valheim layer is separate from the in-memory material reservation coordinator. It is for
an authoritative adapter that already has an exact station journal and deterministic before/after
fingerprints for every container it will mutate. It supports one or two container endpoints per
operation and requires `RunicMutationGate` for every state-changing call.

Before starting new durable work, the authoritative server must call `WorldObjectIdentity.Ensure`
for the station and every endpoint while it owns their exact ZDOs. The resulting 32-character
lowercase GUID tokens are the persisted identities; numeric ZDOIDs remain current-load addresses
only. `WorldObjectIdentity.Resolve` rebuilds a bounded index of tagged ZDOs and succeeds only when
one exact object owns the token. Fresh durable operations reject legacy raw-ID-only endpoints.

The required publication order is:

1. Build the immutable exact journal payload and endpoint before/after fingerprints.
2. Call `TryBegin`; it writes the stable station-token header first, then acquires endpoint claims
   in canonical endpoint-token order while all inventories still match their before fingerprints.
3. Persist that exact journal payload under the adapter's distinct station key.
4. Call `TryMarkJournalPublished`, then apply the exact inventory and station-state mutation.
5. Prove every inventory plus the adapter-owned station, assignment, and cursor successor before
   calling `TryMarkCommittedExact` or `TryMarkRolledBackExact`.
6. Clear the adapter journal exactly.
7. Call `TryReleaseTerminal`; endpoint claims and the station header are released last.

For a complete journal already persisted by a pre-coordinator release, call
`TryAdoptPublishedJournal` instead of rewriting it or calling `TryBegin`. Adoption acquires exact
endpoint claims first, accepts only a journaled before/after fingerprint, and then installs a
`JournalPublished` header. Retrying after a crash reuses only byte-for-byte identical claims; a
different or corrupt claim remains quarantined.

Ordinary schema-1 header and claim recovery remains raw-ID-exact: it proceeds only while every old
numeric ID still resolves to the same local object and all recorded evidence agrees. A raw ID is
never treated as current authority merely because it now addresses some object.

For the narrower case where a save/load renumbered an in-flight schema-1 operation, an owning
adapter may call `TryUpgradeRemappedLegacyOperationIdentity` only after it has located the physical
station and all one or two endpoint candidates from the immutable journal. Transactions then proves
the exact module, operation, journal key and SHA-256 hash, one-to-one old header bindings and
co-located claims, each Before/After fingerprint, synchronized server ownership, and unique station
and endpoint tokens. A bounded non-authoritative retry envelope records the old-ID-to-token mapping;
claims change from schema 1 to schema 2 one at a time with exact compare-and-set semantics, and the
stable schema-2 header is the final authority switch. Any missing, duplicate, mismatched, or
ambiguous evidence remains quarantined without clearing or guessing.

A journal-empty schema-1 orphan uses a still narrower cleanup path. The bounded physical-claim
discovery API scans at most 16,384 claim entries and requires exactly one loaded Container carrying
the complete legacy claim for every old header binding. It does not use position, proximity, or a
current gameplay link to infer an endpoint, and it does not mint identity during discovery. Only an
unpublished intent or a proven terminal operation may be cleared, after synchronized server
ownership and the required exact Before/After fingerprints are rechecked; missing, duplicate,
unloaded, or mismatched claim evidence leaves the orphan retained.

On restart, `ReadHeader` and `TryRecoverOrphan` distinguish an unpublished intent, a published
journal requiring reconciliation, a proven terminal state requiring cleanup, missing endpoints, and
corrupt evidence. There is no timeout that guesses an outcome. A mismatch remains quarantined for
the owning adapter to reconcile exactly.

## Bounded lifecycle defaults

`TransactionCoordinatorOptions` and the published factory expose the server-owned lifecycle bounds:

| Setting | Default | Enforced range |
|---|---:|---:|
| Maximum tracked transactions | 10,000 | 1 to 1,000,000 |
| Reservation lease | 2 minutes | greater than zero to 1 day |
| Terminal/idempotency retention | 10 minutes | greater than zero to 7 days |
| Audit records | 10,000 | 100 to 1,000,000 |

At capacity, a new admission fails closed with `CoordinatorCapacityReached`; an existing tracked
transaction or idempotency key can still replay. `ReserveExact` performs a throttled automatic
lifecycle sweep, while `CommitOnce` and `Rollback` always check their own reservation deadline under
the transaction gate. Authoritative server adapters should also call `PruneExpired` from their
bounded maintenance tick. It returns counts for expired reservations, removed terminal entries, and
remaining tracked entries, which makes cleanup deterministic with an injected `ITransactionClock`.

An expired reservation returns `ReservationLeaseExpired`. Terminal results and their
principal-scoped idempotency keys replay until the retention boundary. Once cleanup removes that
tombstone, the old transaction returns `TransactionNotFound` and the same idempotency key may be
admitted as a new operation; callers that require a longer retry guarantee must configure a longer
retention window.

## Multiplayer and persistence limits in 1.0.0

The engine-neutral material reservation coordinator remains process-local and memory-only. It does
**not** discover Valheim containers, write container inventories, persist
its reservation table through a server restart, or coordinate two different server processes.
Installing it on clients does not make client requests authoritative.

The Valheim ownership-return adapter consumes Runic Persistence's direct session RPC. The server
targets an exact current `RpcPeerSnapshot`; the client accepts only its exact current
`valheim.server` connection/session and never a routed sender claim. Return retries use a
deterministically evicted/pruned 4,096-entry cache, so a long-running world cannot grow request
history without bound.

A multiplayer feature must therefore host its coordinator on the authoritative server, resolve and
recheck permissions there, and translate successful commits to Valheim inventory mutations while
holding the correct game-side ownership guard. The 1.0.0 durable layer supplies persistent station
legacy station headers, endpoint claims, ownership handoff, and exact transition sequencing, but an
adapter using that legacy surface must still write its own hash-bound journal, apply inventory
changes, and prove every
adapter-specific station/assignment/cursor transition. Calling the durable API around an incomplete
journal cannot make that adapter atomic.

The plugin publishes a factory, not an unconfigured mutable singleton. Features with richer Runic
Permissions context construct an isolated coordinator with their permission-backed authorizer
rather than treating an unverified client principal as sufficient authority.

## Shared composite WAL and remote-owner execution

New remote gameplay work must use `transactions.durable-composite` instead of creating another
feature WAL. Its authoritative root lives in the server filesystem and is flushed, atomically
replaced, exclusively reread, checksummed, and world-scoped before endpoint claims. ZDO claims and
markers are replicated exclusion/evidence only; `ZDO.Set` is never a disk acknowledgement.

The service durably issues a server-owned epoch/admission token before a client journal or inventory
tag is created. Generic domains retain the 1–32 endpoint bound. The registered typed world-object
domain deliberately supports 1–64 exact endpoints for one bounded area operation and accepts
Create-from-Absent, Remove-to-Absent, and Present-to-Present Update intents. Every root binds the
exact BackendAccount, request hash, immutable intent, stable sorted endpoints, semantic before/after
fingerprints, and one commit sequence with per-endpoint predecessor links. Recovery uses the world
epoch/commit marker, not timestamps or fingerprint equality alone, so X→Y→X is not mistaken for an
unapplied operation.

Handler-durable consumers use the two-hash issue overload. `durableRequestKeyHash` is a lowercase
SHA-256 over a module-specific domain separator followed by the exact accepted
`RpcRequestContext.IdempotencyKey` UTF-8 bytes; `requestHash` is a separate lowercase SHA-256 over
the transport-bound BackendAccount plus the canonical request payload. `LookupRequest(...)` is a
read-only owner/account/key/request lookup across retained issued, unresolved, and terminal evidence.
It returns `Conflict` when one durable key is replayed with different request bytes and never mints,
cancels, or changes capacity. The legacy issue overload remains equivalent to key=request hash.
Schema-4 catalogs read with that legacy equivalence and migrate to schema 5 only on the next normal
durable write. Once a verified checkpoint deliberately retires a terminal record, lookup is
`NotFound`; this surface does not promise infinite replay history.

Ordinary vanilla objects do not already have a Runic token. Remove and Update therefore expose
`EnrollExisting(...)`: the stable object UUID is derived only from the fsynced operation token,
provider mutation kind, and endpoint ordinal. `IDurableWorldObjectIdentityEnrollmentProvider`
resolves one exact untagged target from the immutable provider evidence in the root and publishes
that derived tag with a missing-or-same CAS. If only the current client owner can write it, the
provider returns `Pending` after a direct-session owner command. The root remains `Claiming`, and no
gameplay mutation or `Prepared` publication is permitted until the server uniquely reads back every
tag and exact claim. Restart before the tag redispatches the same reservation; restart after the tag
reuses it. Stale target evidence, ZDO reuse, a conflicting tag, duplicate resolution, or owner loss
never causes a best-effort rebind.

For a newly appearing custody object such as a grave, the shared supported registry is the same main
operation root, not a second WAL/root or an optional Create endpoint. Its immutable exact intent and
reconciliation requirement must bind the custody schema and conservation manifest before the first
client-local preparation. Synchronous native grave creation writes that already-fsynced operation
token; restart discovery joins the unique tag back to the same account/root/manifest. An issued token
by itself is only a durable reservation, and a typed Create endpoint is appropriate only when Present
is a mandatory desired outcome. Open/stack/take-all/destruction paths remain closed while the tagged
custody obligation is outstanding.

For dedicated-server objects whose native `GameObject` exists only on the current client owner, a
provider may implement `IDurableWorldObjectDeferredMutationProvider`. Prepare performs no gameplay
mutation (an existing-object enrollment may publish identity metadata while still Claiming). Commit
durably assigns its sequence before `TryBeginOrResumeDesiredState`; `Pending` keeps
the root Committing and all exact claims. The provider can send a HandlerDurable execution command
to the exact current owner, whose client rechecks the live instance and native policy and publishes
the exact token/epoch/sequence with its native action. Every same-key replay must be recoverable from
persistent exact CAS evidence (identity and operation lock, desired bytes and applied marker, or
exact-token lock absence). `NotReady` is valid only before any write; an ambiguous publication or
readback is `HandlerFailed`, which releases the HandlerDurable reservation so the identical command
can re-evaluate that persistent evidence. Immutable target or evidence substitution is terminal. A
later server call terminalizes only after authoritative replicated readback. A C→S request that
advances durable state must likewise remain HandlerDurable and session/idempotency bound. Payload
peer IDs, routed sender UIDs, display names, and unmarked after bytes are never authority.

Committed history is retained until a local-world database snapshot is proven across two successful
generations: the first flushed/replaced primary is recorded, and only a later exact replacement whose
fallback hashes to that first generation can advance/compact the WAL. The mutation gate remains held
from checkpoint eligibility through `ZDOMan.PrepareSave` cloning. `WorldSaveFinished` is ignored
because vanilla catches save-thread failures. Cloud/LegacyCloud checkpoint compaction is unsupported
and remains fail-closed; outstanding reconciliation records and unconsumed issued tokens are never
guessed abandoned by checkpoint cleanup.

Before `RPC_PeerInfo`, the peer-aware admission evaluator uses only the exact transport-bound
BackendAccount to query outstanding issued, journaled, and committed operations. A reconnect with
pending work must provide the exact recorded consumer version/protocol and required durable provider
capability. Another account is isolated even if its handshake includes text naming the pending
account. Claims are compatibility evidence, not authenticated client attestation; corrupt or
unavailable WAL state denies admission.

## Development and verification

Build the plugin:

```powershell
dotnet build .\RunicTransactions.csproj -c Release
```

Run the pure test harness (it does not load Valheim or BepInEx):

```powershell
dotnet run --project .\Tests\RunicTransactions.Tests.csproj -c Release
```

The pure test suite covers the simultaneous last-item race, shortage without partial reservation,
permission denial, explicit rollback, changed-inventory rollback, commit-time permission revocation,
deterministic allocation/lock order, bounded capacity, lease expiry across multiple endpoints,
terminal/idempotency retention, commit-versus-expiry serialization, reentrant authorization,
endpoint-version semantics, extreme clock saturation, commit/idempotency replay safety, canonical
world-object tokens, duplicate detection, bounded identity indexing, schema-2 durable headers and
claims, and fail-closed schema-1 recovery.
Installed-Valheim structural and crash-cut coverage for durable headers, endpoint claims, ownership
handoff, vanilla guards, and exact recovery lives in the consuming Production and gameplay
integration suites.
