# Changelog

## 2.0.4 - 2026-09-11

- Updated the placement compatibility audit for Valheim 1.0.12. Startup and error messages now report the actual game version instead of a hard-coded version.

## 2.0.3 - 2026-09-09

- Updated build categories, piece-selection visibility, placement, and zone contracts to Valheim 1.0.
- Updated area-repair inventory notifications to Valheim 1.0's exact two-flag `Inventory.Changed` contract.
- Re-audited the installed Valheim 1.0.7 client assemblies and updated the BepInEx dependency to 5.4.2350.

## 2.0.2 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 2.0.1.

## 2.0.1 - 2026-08-29

- Made the plugin independently installable with BepInEx as its only runtime dependency.
- Removed Core registry/capability registration, Persistence networking, Transactions composites,
  remote mutation protocols, world journals, recovery, reconciliation, custody, and quarantine.
- Moved F11 undo and F12 area repair to Valheim's native loaded local-owner path for solo, listen
  host, and dedicated-client play without ownership claiming or mutation RPCs.
- Kept conservative fresh creator, ward, range, no-build, pose, health, structural, tool, and native
  policy checks; undo clears its one in-memory receipt before native removal starts.
- Kept fixed-capacity deterministic repair discovery and one hammer durability charge per successful
  repair, with no inventory-wide lock or gameplay journal.
- Changed precision yaw, pitch, and roll increments to the pending piece's current local axes, so
  pitch follows a yawed beam's own X axis and the visual guides rotate with the piece. The resulting
  quaternion remains locked against later candidate-surface drift.
- Restricted P to the exact equipped Hammer build mode, so ordinary gameplay and cultivator or
  other tool placement modes cannot activate Precision.
- Made F6 cycle every currently unlocked build piece when the catalog search query is empty instead
  of displaying the missing-query error.
- Kept the explicit P precision mode, same-frame rotation input, six-axis movement/reset controls,
  transform and snap-side matching, two-placement repeat history, bounded search/favorites/recents,
  readout, and axis guides.
- Kept optional Harmony ordering with Build Camera, Crafting, and Agriculture plus PerfectPlacement
  conflict detection and exact pre-1.0 Valheim 0.221.12 seam verification.
- Replaced obsolete durable/dedicated protocol tests with focused native-ownership and standalone
  architecture assertions.

## 2.0.0

- Rebuilt the tool as a compact six-degree-of-freedom extension of Valheim's ordinary hammer
  placement workflow.
- Added configurable quaternion rotation, local/world translation, matching, native snap
  composition, readout, and fail-closed adapter verification.
