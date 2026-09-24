## 1.1.10

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.1.9 - 2026-09-18

- Replaced the exact game-version allowlist with startup API validation. Compatible future patches no longer require a version-only update.
- Incompatible APIs still fail closed; World Engine retains its exact method-body audit.

## 1.1.8 - 2026-09-18

- Added Valheim 1.0.15 compatibility, including its restored three-argument native stack-search contract.
- Retained support for 1.0.7, 1.0.12, and 1.0.14 and exact API validation.

## 1.1.7 - 2026-09-17

- Updated the native stack-search contract for the new cheated-item argument; native stacking retains the game's cheated/ordinary item distinction.
- Added Valheim 1.0.14 support while retaining compatibility with 1.0.7 and 1.0.12.
- Retained exact game API checks and rejection of unknown versions.
## 1.1.6 - 2026-09-14

- Changed player Locked Slots to exclude their contents from Runic Quick Stack only. Requires RunicStorage 1.2.5 or newer for Quick Stack integration; update both mods together.
- Locks no longer prevent manual moves, drops, replacement equipment or tool swaps, crafting upgrades, or sorting. Matching stacks can increase and ammunition, food, and other items remain usable.
- Store All, Restock, consolidation, and general protection consumers ignore player slot exclusions. Dedicated equipment/quiver row protections and independent Safety rules remain in effect.
- Existing saved exclusions stay on their cells, including after the item is replaced or the inventory is sorted.

## 1.1.5 - 2026-09-14

This release brings the Better Archery fixes since 1.1.2 into the release package.
Better Archery could resize the inventory after Runic added its equipment row, leaving items
outside the resulting grid and triggering automatic drops. The compatibility guards preserve
the separate quiver and equipment rows, and the compact layout removes the unused visual gap
without changing saved item positions or capacity.

- Added a compact Better Archery inventory layout that removes unused space above Runic's equipment row.
- Adjusted the panel height to match, with extra space collapsed when no quiver is equipped.
- Added the optional UI setting `CompactQuiverLayout`, enabled by default. Inventory capacity, saved item positions, and existing keybindings are unchanged.

## 1.1.4 - 2026-09-12

- Fixed Better Archery resizing the inventory after Runic prepared its equipment row, causing items to be dropped during character loading.
- Fixed overlapping empty-slot and capacity checks allowing ordinary items into reserved quiver rows.
- Added an item-retention guard before automatic invalid-position cleanup following an unexpected inventory resize.

## 1.1.3 - 2026-09-12

- Added Better Archery 1.9.99 quiver compatibility, keeping quiver ammunition and Runic equipment/quick slots in separate reserved rows.
- Coordinated inventory resizing, slot visibility, and grave-recovery cleanup for the combined layout.
- Kept quiver cells out of ordinary sorting and automatic storage transfers.
- Prevented overlapping quiver and quick-slot shortcuts from triggering both actions.

## 1.1.2 - 2026-09-12

- Fixed locked slots blocking normal item use, including ammunition selection and quick-slot food, potions, and tools.
- Allowed matching pickups to replenish locked stacks while keeping their slots protected from sorting and storage transfers.
- Separated normal cooking, refueling, and processing from slot-retention protection. Update RunicSafety to 1.0.5 when using both mods.
- Kept transfer, drop, display, and disposal guards in place.

## 1.1.1 - 2026-09-11

- Fixed the Valheim 1.0.12 startup check so equipment slots, quick slots, slot locks, and inventory protection initialize again. Retained Valheim 1.0.7 support.

## 1.1.0 - 2026-09-10

- Added an automatic extra inventory row whenever RunicInventory is enabled, preserving all normal backpack slots.
- Reserved its five equipment slots for their matching armor or utility type and its three quick slots for consumables, tools, or utility items.
- Blocked incompatible items entering the dedicated row through dragging, swapping, and direct-position transfers.
- Migrated equipped armor, existing role items, and slot locks into the added row while keeping native pocket upgrades separate.
- Grayed out the in-game Enabled control when extra-row items cannot fit in normal inventory; unsafe disable attempts are refused with a clear explanation.

## 1.0.5 - 2026-09-10

- Fixed a failed Inventory startup leaving cooking and other protected actions permanently blocked by an unavailable protection provider.
- Kept slot locks available when equipment-row contents require vanilla-compatible layout, while preserving existing items and lock metadata.
- Made Alt-right-click target the exact clicked cell and added clear lock/unlock confirmation.
- Fixed slot-protection queries rejecting valid negative character IDs.

## 1.0.4 - 2026-09-10

- Fixed Linux startup rejecting Valheim 1.0.7 because its displayed version includes a platform prefix.

## 1.0.3 - 2026-09-09

- Updated native inventory resizing and notification paths for Valheim 1.0, including safe preservation and expansion of the new pocket rows.
- Updated right-click, RPC, and cheated-state contracts to the installed Valheim 1.0.7 client and dedicated-server assemblies.
- Updated the BepInEx dependency to 5.4.2350.

## 1.0.2 - 2026-09-08

- Fixed Runic Safety blocking ordinary cooking when an installed Runic Inventory had deliberately
  fallen back to vanilla behavior, including migration-safe existing characters, remote non-owner
  contexts, and headless dedicated servers.
- Made the optional item-lock query report stable inactive modes as NotApplicable, matching Runic
  Inventory's own guards, while retaining fail-closed Unknown results during live load/rebind,
  shutdown, malformed evidence, and exceptions that can still affect an owning local inventory.
  Proven remote non-owner and headless paths remain NotApplicable because they cannot enforce locks.
- Kept authoritative unlocked, explicitly locked, and special-row results unchanged.
- Added one bounded, deduplicated warning for each indeterminate protection reason so future player
  logs identify the actual topology or lifecycle state without exposing item or player data.

## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

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

- Invalidated cached controller `ButtonDef` paths and active input reservations on Valheim layout-change events, with symmetric shutdown unsubscription.
- Replaced the new-install Quick 1 controller default with layout-independent `JoyMap` and normalize
  only the exact untouched legacy controller set to that route in memory. This closes Valheim
  Alternative 1's `JoyAltKeys`/`JoyLBumper` left-shoulder alias without rewriting customized files.
  Controller validation is now route-local, same-path map/chat aliases remain suppressed, Core
  registers the actual effective legacy route, and cached steady-state input ticks allocate nothing.
