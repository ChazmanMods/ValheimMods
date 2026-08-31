# Changelog

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
