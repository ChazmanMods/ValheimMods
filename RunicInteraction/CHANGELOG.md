# Changelog

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
