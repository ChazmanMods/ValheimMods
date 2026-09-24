## 1.0.9

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.0.8 - 2026-09-18

- Replaced the exact game-version allowlist with startup API validation. Compatible future patches no longer require a version-only update.
- Incompatible APIs still fail closed; World Engine retains its exact method-body audit.

## 1.0.7 - 2026-09-18

- Added Valheim 1.0.15 support while retaining exact API checks and support for 1.0.7, 1.0.12, and 1.0.14.

## 1.0.6 - 2026-09-17

- Added Valheim 1.0.14 support while retaining compatibility with 1.0.7 and 1.0.12.
- Retained exact game API checks and rejection of unknown versions.
## 1.0.5 - 2026-09-12

- Added support for RunicInventory 1.1.2's separate item-use protection, allowing locked supplies to be cooked, used as fuel, smelted, or fermented.
- Preserved equipped, quest-item, rare-item, external-provider, and disposal protections.

## 1.0.4 - 2026-09-11

- Fixed safeguards being disabled by the Valheim 1.0.12 version check. Retained Valheim 1.0.7 support.

## 1.0.3 - 2026-09-10

- Fixed optional Inventory protection API discovery remaining unavailable after an early startup lookup.

## 1.0.2 - 2026-09-09

- Added bounded, deterministic directory-source backups for Valheim 1.0's chunked world-save format.
- Rejects reparse points, backup-root overlap, changing members, unsafe paths, and configured file/byte limits before promotion.
## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0

- Kept context-keyed confirmations for occupied containers, vehicles, portal overwrites, and rare
  item sacrifice at the existing Valheim boundaries.
- Kept equipped, quest-item, configured-rare, and optional Inventory lock checks; Inventory is now
  discovered through a small reflection seam and is never a load dependency.
- Kept vanilla tombstone serialization/capacity auditing, recovery planning, bounded diagnostics,
  and atomic SHA-256 migration backups with abort-before-mutation behavior.
- Removed Runic Core, Persistence, and Transactions project, plugin, package, registry, capability,
  and protocol dependencies.
- Removed direct-session compatibility admission/evidence, durable grave markers, token-disposition
  RPC, grave/container custody patches, recovery holds, and related reconciliation enforcement.
- Added `SafetyIntegrationApi` as an optional in-process service seam with no shared runtime DLL.
- Reduced the Thunderstore manifest to BepInEx only and preserved version 1.0.0.
