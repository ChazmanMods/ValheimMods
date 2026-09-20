# Changelog

## 1.1.1

- Added opt-in automatic repair of eligible worn inventory equipment after a successful crafting-station open.
- Added opt-in area repair after a successful vanilla hammer repair while preserving the existing hotkey.
- Kept both features owner-directed, guarded against repeated interaction and area-repair recursion, and disabled by default.

## 1.1.0 - 2026-09-12

- Fixed hammer-building chest searches to use the player's position and configured material range, including station-required pieces such as the forge.
- Kept building previews, material counts, and placement consumption on the same search scope while preserving station and chest access checks.
- Added optional area repair for hammer-built structures: configurable hotkey (default semicolon) and radius (default 50m, up to 100m).
- Added an independent area-repair enable switch, paced networked repairs, ward/personal-chest checks, and menu/typing protection. Inventory equipment is unaffected.
- Added piece-specific building diagnostics showing the material search range and required station.

## 1.0.8 - 2026-09-11

- Reduced repeated crafting-menu work with a short-lived availability cache, combined with verified chest material counts and shared menu-refresh queries.
- Shared nearby-material queries across building pieces during moving hammer previews, while preserving exact search positions and ranges.
- Added targeted cache invalidation and fresh checks for crafting and placement, plus optional performance counters.
- Thanks to **Megamos** and **Aedis** for their reports, testing, and optimization suggestions.

## 1.0.7 - 2026-09-11

- Improved crafting and hammer-menu performance near large storage areas by reusing unchanged chest material counts.
- Shared nearby-container queries across recipes and building pieces within each menu refresh.
- Preserved access restrictions and normal material consumption, with cache invalidation when relevant state changes.
- Thanks to **Megamos** and **Aedis** for their reports, testing, and optimization suggestions.

## 1.0.6 - 2026-09-10

- Added cooking from nearby chests through the normal Use interaction on cooking racks and ovens.
- Added nearby fuel fetching for fires, refillable torches/lamps, ovens and smelters, using each object's native fuel type.
- Added separate cooking/refueling switches and a configurable interaction search range; no Production links are required.
- Preserved carried-item priority, finished-food collection, fire requirements, fuel capacity and light-toggle behavior.
- Added one-item staging with exact transfer checks, inventory-capacity feedback and protected-item checks.

## 1.0.5 - 2026-09-10

- Fixed weapon and armor recipes remaining uncraftable despite enough materials in nearby chests.
- Fixed recipe previews unnecessarily taking ownership of nearby chests just to count materials.
- Enabled shared workshop materials by default for new configurations while preserving existing settings.
- Fixed workshop access inspection requiring ownership of the station's network record.
- Fixed worn equipment incorrectly blocking material consumption; rollback preserves item references, durability, and custom data.

## 1.0.4 - 2026-09-10

- Fixed ordinary workbench recipes incorrectly requiring hidden upgrader-only ingredients, leaving Craft disabled despite sufficient visible materials.
- Recipe availability and consumption now select the same station-specific ingredient set as vanilla. Native upgrader stations still require their special ingredients; ordinary upgrade costs, batch multipliers, and building costs remain intact.
- Replaced lossy inventory save/reload preflight and rollback with exact in-memory snapshots so worn equipment cannot falsely block material consumption. Rollback preserves original item references, full durability precision, and custom data.
- Chest synchronization accepts only the exact durability conversion made by one native load/save, without ignoring altered quantities or other item data.
- Existing workshop policies, ward checks, personal-container exclusions, ownership checks, and capacity safeguards remain unchanged.

## 1.0.3 - 2026-09-10

- Fixed craft/build-from-nearby-container discovery failing merely because another player owned the
  surrounding zone.
- Accessible unused containers now use Valheim's native ownership claim before mutation.
- Containers whose synchronized ZDO state says they are in use remain excluded, preventing an
  active chest editor from being overwritten.

## 1.0.2 - 2026-09-09

- Updated output insertion, build placement, and inventory-change integration to Valheim 1.0's exact signatures.
- Re-audited the installed Valheim 1.0.7 client assemblies and updated the BepInEx dependency to 5.4.2350.

## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0

- Removed the mandatory Runic Core, Persistence, Permissions, Transactions, and Inventory runtime
  dependencies without changing the published version.
- Deleted capability registration, protocol coupling, the durable remote crafting saga, composite
  container claims, player journals, recovery enforcement, and the suite-wide mutation gate.
- Kept exact carried-first material allocation and rollback as a Crafting-owned process-local lease.
- Enabled the same native local-player and container-owner path for solo, listen-server, and
  dedicated-client processes; non-owned containers are excluded from the attempt.
- Preserved Workshop Access ZDO keys and added optional group membership lookup through Runic
  Portals without making Portals a load requirement.
- Kept combined requirement displays, stationless build rules, placement rollback, and Repair All.
