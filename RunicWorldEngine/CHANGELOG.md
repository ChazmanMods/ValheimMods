## 1.2.3

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.2.2 - 2026-09-18

- Replaced the exact game-version allowlist with startup API validation. Compatible future patches no longer require a version-only update.
- Incompatible APIs still fail closed; World Engine retains its exact method-body audit.

## 1.2.1 - 2026-09-17

- Fixed the optional player-cap override rejecting Valheim 1.0.14 and blocking hosting or new admissions.
- Retained Valheim 1.0.12 support and all method-body, loaded-IL, conflict, and admission-integrity checks. Unknown builds remain rejected.

## 1.2.0 - 2026-09-12

- Added an optional, configurable 2–64 player cap with coordinated Steam, PlayFab, and admission limits, including the dedicated host's transport slot.
- Added startup validation and admission integrity checks for the audited Valheim 1.0.12 hosting-limit patches.
- Added `runicworld_status`, peer counts, Steam/PlayFab RTT and traffic rates, queue diagnostics, ownership-transfer counters, and possible synchronization-starvation indicators.
- Added configurable, sustained server-health warnings with recovery messages and rate limiting.
- Preserved existing save smoothing and read-only world diagnostics.

## 1.1.2 - 2026-09-09

- Migrated save timing to Valheim 1.0.7's exact `ZDOMan.SaveChunks` and `LoadChunks` contracts while
  retaining a separate legacy-world load observation seam.
- Added the required `assembly_utils` reference for the new file-source parameter and verified all
  observatory targets against both installed client and dedicated-server assemblies.
- Updated the release dependency floor to BepInExPack Valheim 5.4.2350.

## 1.1.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.1.0.

## 1.1.0 - 2026-08-31

- Added bounded main-thread save preparation smoothing for asynchronous periodic saves.
- Coalesces overlapping requests, never defers synchronous shutdown saves, and never accesses Unity
  objects from a background worker.

## 1.0.0

- Kept constant-time aggregate ZDO observability and interval event counters.
- Kept installed-Valheim-verified save/load timing hooks with exception-preserving finalizers.
- Made `Enabled = false` startup-inert and deferred Harmony construction until after the setting gate.
- Verified exact installed field shapes and kept load completion within the one-second sample ceiling.
- Removed the Core registry, capability, protocol, service, and world-data ownership declaration
  architecture; BepInExPack Valheim is the only runtime dependency.
- Preserved all unknown data and retained no deletion, compaction, or sync-scheduling path.
