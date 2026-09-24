## 1.0.3

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.0.2 - 2026-09-09

- Updated explored-map storage, rounding, and pin-removal integration to Valheim 1.0's exact APIs.
## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0

- Fixed the large-map browser permanently disappearing when a typing or filter change narrowed the
  result set during the same IMGUI draw; row rendering now uses the post-input result bounds.
- Added deterministic, bounded search/category/source filtering for saved pins on already explored
  local/shared map pixels, with display-only duplicate grouping and preserved authorship.
- Added explicit known-asset tags and truthful `LAST KNOWN` status for pin-based boats, carts,
  tameables, portals, beds, and tombstones without entity, radar, or world-location scans.
- Added revalidated map centering, straight-line direction/distance/elevation, tombstone topology
  warnings, and current local controlled-ship sailing context.
- Added no-map/dedicated fail-closed boundaries, input isolation, safe-area UI/controller scaling,
  bounded text and caches, low-frequency state fingerprints, and vanilla fallback.
- Added optional Runic Portals enabled-state detection through BepInEx metadata without a registry,
  capability, protocol, directory query, or hard optional dependency.
- Added focused deterministic, pre-1.0 Valheim 0.221.12 signature/IL, privacy, mutation,
  dependency, documentation, icon, and 100/1,000/10,000 work-profile tests.
- Added 0.75-second evidence quieting and a two-second maximum continuous-churn rebuild cadence,
  with repeated-change allocation/time budgets at 100, 1,000, and 10,000 pins.
- Clarified the local-client-only authority and multiplayer/server boundary: no RPC, ownership,
  remote disclosure, or server/world mutation.
- Made the package independently installable with BepInExPack Valheim as its only dependency.
