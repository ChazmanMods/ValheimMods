# Changelog

All notable changes to Runic Permissions are documented here.

## 1.0.0 - 2026-08-25

- Normalized the coordinated Thunderstore release identity and Foundation dependencies to 1.0.0.
- Preserved the accepted permission, group, ward, and transport-bound administration behavior.
- Added complete native chat-based Group management through `/group`, including friendly Group
  names, unique connected-player name resolution, active-Group selection, and private chat output;
  `/runic_group` remains available for backward-compatible exact-ID administration.
- Added server-authoritative per-world active-Group persistence and the typed
  `permissions.groups.active` service. Clients receive a bounded refreshed cache for editor UX,
  while authoritative use always rejoins the exact UUID to current server membership.

## 0.1.1 - 2026-08-22

- Added the built-in world-scoped Group provider, direct-session Group management commands,
  canonical bounded command wire format, exact verified world-player membership, atomic primary publication,
  replay-safe desired-state handling, and typed `permissions.groups` discovery service.
- Made remote Group mutations cross-session durable: a server-issued epoch/sequence token binds the
  actor, canonical request hash, and exact catalog/Group revisions, while the mutation and immutable
  receipt are fsynced in the same primary-file publication. Stale or conflicting reconnect replays deny.
- Made an exact lost prepare/reconnect retry return its original still-current issued token instead
  of consuming another durable sequence or creating a competing outstanding command.
- Made the durable prepare/execute exchange the first published Group wire contract; earlier
  development-only query scaffolding is not retained as a compatible release surface.
- Bound remote Group actors to `valheim.player:<PlayerID>` only after exact `AccountBoundPlayer`
  proof, preserving membership across hosted and dedicated play while retaining the backend account
  solely for transport, replay, and audit identity.
- Added a hard Runic Persistence 0.1.1 dependency for transport-bound Group administration.
- Raised the hard dependency floor to Runic Core 0.1.1 so every coordinated Foundation package
  consumes the same canonical shared-contract release.
- Normalized plugin, assembly, informational, manifest, package, documentation, and example-config
  release identities. Permission evaluation contracts and behavior are unchanged from 0.1.0.

## 0.1.0 - 2026-08-20

- Added stable authority-qualified identities that never authorize by display name.
- Added synchronized owner/builder records with schema, revision, and trust state.
- Added twelve independent least-privilege action scopes with stable wire names.
- Added Everyone, Approved, Owner, Nobody, Ward, Ward + exceptions, and Group policies.
- Added fail-closed ward, group-provider, stale-data, and ambiguity handling.
- Added deterministic denial precedence: server denial, object denial, uncertain security context, then ward and object policy.
- Added deliberate administrative mode with mandatory audit-before-allow behavior.
- Published the typed `permissions.evaluate` service through Runic Core.
- Added a dependency-free deterministic test harness covering contracts and policy precedence.
