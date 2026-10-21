# Changelog

## 1.1.0 - 2026-08-31

- Added the client-visible admission-rejection event used by Raven's Gate denial explanations.
- Added central reporting of server-observed Runic RPC denials, replay conflicts, and protocol
  violations to Sentinel's bounded enforcement service.
- Signed Sentinel administrators now override the vanilla admin list while Sentinel is active.

## 1.0.0 - 2026-08-25

- Normalized the coordinated Thunderstore release identity and Core dependency to 1.0.0.
- Preserved the accepted persistence, direct-RPC, admission, and actor-binding behavior.
- Fixed the dedicated-server `runic_bind peers`, `status`, and `list` query handler so its
  byte-backed query kind is validated without throwing before an administrator can enroll a player.

## 0.1.1 - 2026-08-22

- Preserve exact direct-RPC request identity bytes and reject noncanonical leading/trailing
  idempotency whitespace on both send and receive; module, endpoint, correlation, and reason fields
  are likewise validated rather than normalized.
- Reject leading/trailing handshake-claim whitespace at construction and decode instead of
  normalizing attacker-controlled bytes before compatibility evaluation.
- Bind the evidence-visible hello nonce to the exact binary per-connection nonce at both encode and
  decode, preventing a peer from publishing an unrelated nonce alongside otherwise valid claims.
- Added direct connection-bound `ZRpc` request/response transport, pre-`PeerInfo` compatibility
  admission, immutable bounded handshake claims, session identity, exact-target server responses,
  replay/rate/capacity limits, timeout/disconnect handling, and client-visible current-server
  snapshots.
- Added server-side transport-owned actor resolution and an exact canonical Steam-only admin fact;
  PlayFab, display names, malformed IDs, unauthenticated sockets, and connection-only fallbacks do
  not elevate.
- Added a world-scoped checksum-protected reverse-unique account/player binding store with atomic
  replacement, explicit enrollment/revocation/list commands, no resolve-time enrollment, and no
  automatic trust of backup data.
- Added direct Steam-admin dedicated-server binding endpoints that recheck requester and target
  sessions immediately before commit; the Steam admin may bind an exact current Steam or PlayFab
  BackendAccount target, while PlayFab cannot authorize the action.
- Require strict two-segment persistence module IDs and collision-free keys.
- Add module-scoped stores, compare-and-swap migration commits, foreign-key ownership enforcement,
  and tokenized disposable migration registrations.
- Bound RPC inputs/idempotency memory and make exposed payload/snapshot data immutable.
- Select a compatible capability provider before applying deterministic provider ordering.
- Raise the hard dependency floor to Runic Core 0.1.1 and normalize plugin, assembly, product,
  informational, manifest, package, documentation, and example-config release identities.

## 0.1.0

- Added canonical Runic persistent-key namespacing.
- Added atomic, backup-aware, forward-only schema migrations.
- Added record metadata contracts and in-memory store adapter.
- Added capability-level protocol negotiation.
- Added RPC correlation and idempotency primitives.
