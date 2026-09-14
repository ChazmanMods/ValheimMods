# Changelog

## 1.0.4 - 2026-09-11

- Fixed startup being disabled after the Valheim 1.0.12 update. Retained Windows and Linux support for Valheim 1.0.7.

## 1.0.3 - 2026-09-10

- Fixed Linux startup rejecting Valheim 1.0.7 because its displayed version includes a platform prefix.

## 1.0.2 - 2026-09-09

- Updated ZDO sector and controller contracts to Valheim 1.0 and re-audited the installed 1.0.7 client assemblies.
- Updated the BepInEx dependency to 5.4.2350 and aligned the package, plugin, and assembly versions.

## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0 - 2026-08-29

- Made `chazman.RunicInteraction` independently installable with BepInEx as its only runtime
  dependency and kept version 1.0.0.
- Removed Core registration, published capability/status services, protocol coupling, Persistence
  door transport, account binding, remote-owner dispatch, and the obsolete authoritative ward index.
- Replaced the shared keybinding registry with the six-entry local Interaction catalog.
- Rebuilt door auto-close as a bounded, session-only native Valheim path for a door opened by the
  local player. It keeps current ward, obstruction, identity, prefab, and closeability checks and
  writes no world data.
- Kept drag-sweep transfer as an explicit disabled gate; ordinary vanilla dragging is unchanged.
- Kept hold repeat, full-stack transfer, legal equipment restore, bounded menu memory, text
  validation, and exact pickup filters on their installed vanilla outcome paths.
- Converted item protection to an optional reflected Runic Inventory API. Missing Inventory no
  longer prevents Interaction from loading.
- Removed persistent operations, journals, recovery, global locks, and quarantine from the feature
  set; a failed action releases only its own current in-memory state.
