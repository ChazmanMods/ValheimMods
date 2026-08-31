# Changelog

## 1.0.0 - standalone runtime architecture

- Removed mandatory Foundation runtime dependencies (Runic Core, Persistence, and Transactions)
  without changing
  the mod version or player topology metadata.
- Replaced the suite-global mutation gate with one short-lived per-mod mutation scope around native
  owner-local operations.
- Removed the durable journal, recovery bridge, quarantine, and cross-mod transaction service.
- Added a tiny optional reflection API for item-protection consumers; Inventory remains independently
  installable and consumers continue without it.
- Kept every gameplay mutation on the native owner-local character inventory path.

- Added subtle in-inventory labels and an outline for the five equipment roles and three quick roles;
  no quick-slot HUD is drawn during gameplay, and the diagnostic panel now defaults off.
- Added empty-role pickup/crafting placement with native equip, plus exact lossless position swapping
  when the player manually equips a replacement for an occupied armor, cape, or utility role.

- Fixed ordinary armor replacement falsely entering `MigrationSafeCompatibility` when Valheim raised
  `Inventory.Changed` after equipping but before the postfix could relocate the new item. Equipment
  changes now use an exception-finalized deferred-refresh boundary, genuine role-content failures name
  the first offending role/item, and a corrected authoritative row reactivates automatically.
- Added Left Alt + right-click as the primary in-inventory lock toggle while keeping the configured
  Alt+L action as a fallback.
- Anchored Helmet, Chest, Legs, Cape, Utility, and Quick 1-3 labels and yellow borders to Valheim's
  exact eight native bottom-row elements.
- Restored authoritative topology automatically after transient belt/equipment drag states and
  corrected carried-item protection proof to compare the fresh native item references rather than
  requiring two separately returned item lists to be the same list object.
- Made slot lock toggles silent and represented each explicit lock with a bright yellow outline.
- Yielded the bottom-row overlays whenever a native inventory modal is visible and allowed protected
  equipment through the exact workstation repair call without weakening protection elsewhere.

## 1.0.0 — 2026-08-22

- Implemented Core's provider-neutral `inventory.durable-operations` contract with an independent
  checksummed atomic local journal, reconnect lock, exact Local-profile current-primary readback,
  provider-owned profile-source classification, and server-led Cloud/Legacy outstanding-operation
  adoption. Cloud completion requires an exact account/world/token/manifest/receipt binding plus a
  later direct session's first pre-mutation provider-recomputed inventory fingerprint; same-session
  and third-state evidence remains quarantined.
- Added bounded cloned exact-manifest recovery for an owning consumer after restart, with explicit
  corrupt, file/mirror-conflict, foreign-owner, mismatched-operation, and terminal denial.
- Replaced Storage's process-only catastrophic rollback flag with a real `Indeterminate` Inventory
  crash journal. The three-argument hold seam now succeeds only after an atomic independent flush,
  exclusive readback, and exact Player mirror; its manifest binds the profile/player identity,
  failure boundary, and captured/current inventory fingerprints. Disposal and restart retain the
  quarantine, and 1.0.0 deliberately provides no automatic clear without independent recovery proof.
- Split startup into an early durable safety plane and a later convenience-feature plane. A
  protocol failure retains native evidence enforcement, while a compatible Core publishes a
  minimal module plus durable-operation recovery service before topology, gameplay-service,
  binding, and UI admission. Any later startup or cleanup failure disables new gameplay admission
  without withdrawing that recovery service, disposing a previously loaded journal runtime,
  unpatching its native Inventory/Player/death-and-grave guards, or stopping recovery ticks. With
  no durable evidence, degraded startup continues to yield ordinary vanilla inventory behavior;
  only final shutdown tears the recovery bridge and safety plane down.
- Added a bounded provider-owned opaque-state forecast seam for cross-domain intents. Forecast bytes
  are defensive and side-effect checked, callback/reentrancy faults fail closed, and forecast never
  substitutes for the separately persisted `TryCaptureLocalPrepared` observation.
- Hardened the independent journal to an exact trusted-root child with reparse-point denial before
  and after publication, exclusive bounded reread, backup-free atomic replacement, exact-delete
  identity, and preserved `.new`/`.bak`/`.del` forensic evidence on every uncertain cut.
- Preserved vanilla death during an unresolved operation: `CreateTombStone` is never suppressed;
  an exception-finalized exact system-transfer scope journals the spawned grave identity,
  inventory fingerprint, stable-token evidence when already present, operation-tag counts, and
  current direct-server session digest. Cloud custody completion requires a different later session;
  a missing capture-session proof remains locked for server recovery.
  Added a provider-neutral durable server custody-recovery proof for the crash-before-local-capture
  cut, requiring the exact issued token/account/world/receipt, conservation evidence, one unique
  tagged grave, and a later observation session.

- Added an all-or-nothing native bottom row with five equipment roles and exactly three manual quick roles.
- Kept every item inside Valheim's native inventory; no extra capacity, weight change, stack multiplier, item clone, or secondary item serialization.
- Added bounded digest-protected slot locks, locked-stack routing, and registered high-level drop, deposit, use, exact upgrade, processing, display, and sacrifice guards.
- Added deterministic safe regional sort with hotbar/equipment/quick/lock preservation and exception rollback.
- Added bounded event-driven pickup overflow/encumbrance preview, reserved-row-aware capacity checks, and an exact deny filter with quest-item bypass.
- Published copied immutable `inventory.topology`, legacy coordinate locks, the Core 1.0.0 `IItemProtectionQuery` under `inventory.item-locks`, and truthful status services. The typed query proves exact current local membership, protects the full special row, returns false/`Unknown` only for NotApplicable external evidence or an explicitly disabled provider, and returns true/`Unknown` for unsafe in-domain evidence.
- Added owning-local-player authority for solo, listen-host, and remote dedicated clients. Native player-inventory topology, sort, quick-use, equip relocation, and lock persistence no longer incorrectly require `ZNet.IsServer`; non-owner and headless paths remain read-only.
- Corrected headless-client admission so `Application.isBatchMode` is never treated as gameplay
  authority: an exact owning `Player.m_localPlayer` remains eligible on an automated dedicated
  client, while a true dedicated server with no local Player remains inert and fail closed.
- Added migration-safe dimension/digest/role mismatch behavior with no automatic item movement.
- Added continuous live-dimension/host-authority revalidation, canonical-location proof for already-equipped gear, deterministic peer patch ordering, and pre-vanilla locked armor-source denial without blocking canonical reload re-equip.
- Removed the private-reflection/manual-Harmony Storage guard. Storage, Safety, and Interaction consume the Core-owned typed item-protection contract without Inventory reflecting or patching their internals. Storage 1.0.0 is now truthfully a downstream hard consumer of Inventory's separate durable-operation service; Inventory itself still does not hard-reference Storage.
- Corrected the optional item-protection contract so an explicitly disabled Inventory provider returns NotApplicable before inspecting a player item; Interaction and other optional peers now yield to vanilla transfers while Inventory is disabled, while migration/authority uncertainty remains fail-closed.
- Closed the complementary lifecycle gap: while Inventory is enabled, any native item queried while
  the provider is disposed, loading/rebinding, or missing its Player/Inventory now returns
  in-domain `Unknown`. Only explicit disable or bounded exact membership absence can yield
  NotApplicable, so consumers cannot mutate through a transient unavailable provider.
- Made disabled-topology metadata cleanup sticky across mutation-gate contention, ticks, rebinds,
  configuration changes, and restart. Inventory now stays fail-closed until the owning local player
  commits the exact removal, then publishes clean `Disabled` state without requiring another toggle.
- Separated runtime configuration/disable cleanup from optional keybinding refresh. Cleanup now runs
  first, and failures in either phase are reported and failed closed without suppressing the other.
- Added bounded once-per-reason verbose diagnostics for every typed item-protection outcome and
  indeterminate branch without logging item identity, contents, coordinates, or custom data; added
  direct healthy exact-member `Locked`/`Unlocked` and foreign-item NotApplicable regressions.
- Invalidated cached controller `ButtonDef` paths and active input reservations on Valheim layout-change events, with symmetric shutdown unsubscription.
- Replaced the new-install Quick 1 controller default with layout-independent `JoyMap` and normalize
  only the exact untouched legacy controller set to that route in memory. This closes Valheim
  Alternative 1's `JoyAltKeys`/`JoyLBumper` left-shoulder alias without rewriting customized files.
  Controller validation is now route-local, same-path map/chat aliases remain suppressed, Core
  registers the actual effective legacy route, and cached steady-state input ticks allocate nothing.
- Declared the exact Runic Core 1.0.0, Runic Persistence 1.0.0, and Runic Transactions 1.0.0 release floors used by the compiled contracts.
- Added lossless death, tombstone, logout, disable, upgrade, abrupt-uninstall, installed-IL, crash-cleanup, and 100/1,000/10,000 performance regressions.
