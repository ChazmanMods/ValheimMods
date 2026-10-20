# Core development checkpoint

Risk level 3 for eventual inventory consumers; this checkpoint changes only new,
isolated, game-independent code. No playing profile, world, baseline, or public
package is modified. Native evidence in VALIDATION-REPORT.md remains applicable
only to its diagnostic paths.

## Reproduction and scope

The preserved 2.1.2 ExactMaterialPlanner rejects more than 129 sources before
examining available stock. Its prefab/count-only snapshot cannot represent
quality, variant, protection, exact instances, or overlapping alternatives.
Callers include crafting, building, and manual-input preparation; eventual shared
state includes source revisions, selected upgrade items, and material custody.
The new planner must not carry these assumptions into the client-only design.

Twelve meaningful deterministic groups are planned before testing:

1. Baseline source-limit reproduction and wider-source planning.
2. Restrictive and overlapping alternative costs, deterministic carried-first allocation.
3. Quality/variant/world matching, protection, exact selection and transport identity.
4. Truthful denied/busy/incomplete previews and bounded discovery widening.
5. Checked arithmetic, immutable inputs, malformed/duplicate identities.
6. Fresh commit validation after source, stock, identity, and permission changes.
7. One-action lifecycle; unknown outcomes cannot replay or authorize refund.
8. Exact custody, partial outcomes, blocked and uncertain returns.
9. Duplicate/payload-mismatch operation handling and bounded unresolved evidence.
10. Intent cancellation, exact upgrade identity, live building pose.
11. Per-action batches, native-condition stop, no prepayment or hidden queue.
12. Pre-spend fallback, bounded replans, session isolation and unrelated work.

These target the new code's accounting and state-transition boundaries. They are
not network, Unity, persistence, or gameplay acceptance evidence. No general
gameplay suite will be repeated for pure core changes. Release remains incomplete
until the live bindings and the specification's required evidence exist.

## Implemented boundaries

- MaterialPlanner uses residual min-cost allocation, allowing earlier alternative
  choices to move when a restrictive requirement needs the same stack. Carried
  units outrank nearby units; source preference, distance, source ID and item ID
  produce a deterministic plan. Counts and quantities use checked arithmetic.
- ItemIdentity retains prefab, quality, variant, world level, durability, crafter,
  canonical custom data and extension fields. ItemStack separately retains exact
  instance, quantity and protection. ExactSelection requires an adapter-verified
  action selection; unknown protection remains excluded even through selection.
- Denied/unsupported source contents are discarded. Busy amounts are separate.
  Incomplete discovery or exhausted planning work cannot report definitive loss
  of supplies. SourceIndex filters 3D range and widens its nearest-first set by
  128 sources. Generation changes invalidate old search results.
- ActionCoordinator captures immutable intent, checks upgrade instance/location
  generations, accepts the current build pose only at execution, permits at most
  two pre-spend replans within 1,200 ms, and latches debit/effect issuance.
- MaterialCustody partitions each confirmed exact slice into consumed, held and
  returned quantities. Correlated partial results are explicit. Blocked returns
  retain custody; unknown returns cannot be retried under a new operation.
- ActionRegistry bounds active obligations and rejects duplicate control
  activation. An unresolved source is isolated while another source remains
  usable. It rechecks isolation both at readiness and immediately before spend.
- OperationJournal separates source/phase/payload identities. The adapter uses
  a monotonic numeric action ID per authenticated session; the sequence must
  match that ID. Explicit final-receipt acknowledgement advances a retirement
  fence, never a lease timeout. Unresolved records survive; retired messages
  cannot execute again, including attempts to relabel an old ID with a new
  sequence. After retirement, any needed new recovery operation uses a new action
  ID with its exact original custody reference in the immutable payload.
- BatchCoordinator starts one ordinary craft at a time, preserves completed
  progress, and stops on cancellation, invalid native conditions or uncertainty.
  Max considers the material plan and adapter-supplied native output capacity.
  Default cap is 100; pure arithmetic/control bounds 1–1,000 are checked. This is
  not evidence that a 1,000-craft gameplay batch has been exercised.

## Findings addressed during review

The initial bounded journal could fill during ordinary completed work and require
a new session. Explicit acknowledgement/retirement fixes that without losing
unknown obligations. Review also found that independent per-action coordinators
needed a shared source-isolation boundary. ActionRegistry supplies it. A further
retirement review identified possible old-ID resurrection under a different
sequence; canonical numeric action IDs now prevent that. These findings justified
the guard revision and targeted replay check, not broader gameplay testing.

## Required adapter work before gameplay use

The core is trusted in-process logic, not an authenticated RPC boundary. Validate
wire sizes, peer/player/world association, capability versions and payload hashes
in actual adapters. No adapter is advertised by SourceAdapters.cs. Capabilities
are explicit contracts, not proof conferred by a boolean or component name.

Source identity/revision must bind the actual authority. Native snapshots must
map audited quality/world/alternative rules and protection integrations. Immediately
before debit, use actual permission, exclusion and freshness evidence, IsFresh,
fresh native conditions, then ActionRegistry.BeginSpend on the game thread.
Callbacks route to the original action/path. Do not call direct coordinator
mutation transitions around the registry's source-isolation checks.

Debits and returns must use exact slices through the authority. A local flag,
snapshot hash or this ledger is not sufficient proof of exclusion. Effect receipts
must correlate to the actual native effect and exact accepted quantity. Never
turn a timeout or unrelated inventory/target delta into a receipt. Retain unresolved
session objects for status reconciliation; closing them cancels new execution.

The index stores immutable snapshots; game events still need to publish and
invalidate them. 350/150 ms discovery/preview scheduling, measured frame budgets,
warm-path allocation targets, localized feedback, repair, gameplay hooks, optional
server coordination and integrations are not implemented by this core checkpoint.
The library's in-memory evidence does not survive a process crash.
