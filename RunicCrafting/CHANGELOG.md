## 1.1.7

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.1.6 - 2026-09-23

- Reduced repeated chest searches while crafting, browsing building pieces and using Show All.
- Reuse short-lived building previews to reduce repeated work while holding the hammer; actual crafting and building still check materials before spending them.
- Refresh recipe availability after materials are spent or restored, without interrupting normal crafting input.
- Improve coordination with Storage and Production when they use the same chests, and wait for chest contents to synchronize before taking materials.
- Handle interrupted crafting more carefully and stop further use of affected supplies if a failed transfer needs inspection.
- Preserve an ItemDrawer's assigned item when its last materials are consumed.
- No extra Runic mod is required.
- Thanks to **Phoenixf** for profiling and reporting the crafting and building-menu issues.

## 1.1.4 - 2026-09-22

- Reduced repeated nearby-chest searches while completing crafts and browsing building pieces, including Show All.
- Refresh recipe availability after nearby materials have been spent or restored, without interrupting normal crafting input.
- Preserve fresh material checks, workshop permissions, and multiplayer access restrictions.
- Thanks to **Phoenixf** for profiling and reporting these performance issues.

## 1.1.3 - 2026-09-19

- Runic Crafting now uses its own `PullPrefabIds` setting for custom containers, independently of Runic Storage.
- Makail ItemDrawers remains enabled by default with `piece_drawer`.

## 1.1.2 - 2026-09-19

- Added optional Makail ItemDrawers support for crafting, building, cooking, and refueling.
- Materials withdrawn from drawers follow normal backpack stack limits.

## 1.1.1 - 2026-09-14

- Added a wooden repair sound after successful area repairs.
- Empty, denied, or failed repair attempts remain silent.

## 1.1.0 - 2026-09-12

- Fixed building-material searches to use the player's position and configured range, including pieces that require a station.
- Added area repair with a configurable shortcut and a radius of up to 100 meters.
- Area repair respects access restrictions and is separate from inventory equipment repair.

## 1.0.8 - 2026-09-11

- Further improved crafting and moving hammer-preview performance near storage areas.
- Added optional performance logging.
- Thanks to **Megamos** and **Aedis** for their reports and testing.

## 1.0.7 - 2026-09-11

- Reduced repeated chest checks in crafting and building menus.
- Preserved normal material costs and access restrictions.
- Thanks to **Megamos** and **Aedis** for their optimization suggestions.

## 1.0.6 - 2026-09-10

- Added cooking and refueling from nearby chests through normal interactions.
- Added separate feature switches and an adjustable interaction range.
- Preserved carried-item priority, finished-food collection, fire requirements, and fuel limits.

## 1.0.5 - 2026-09-10

- Fixed weapon and armor recipes remaining unavailable despite enough nearby materials.
- Improved chest previews and workshop access checks in multiplayer.
- Enabled shared workshop supplies by default for new configurations.
- Fixed worn equipment incorrectly preventing material use.

## 1.0.4 - 2026-09-10

- Fixed ordinary workbench recipes incorrectly requiring ingredients meant only for upgrader stations.
- Improved restoration of materials after cancelled actions while preserving equipment durability and item details.

## 1.0.3 - 2026-09-10

- Fixed nearby crafting and building failing when another player owned the surrounding area.
- Chests currently being used by another player remain excluded.

## 1.0.2 - 2026-09-09

- Updated compatibility for Valheim 1.0.7 and BepInExPack 5.4.2350.

## 1.0.1 - 2026-09-05

- Updated the mod description and documentation. Gameplay was unchanged.

## 1.0.0

- Standalone crafting and building from nearby storage, requiring only BepInEx.
- Includes combined material counts, workshop access policies, stationless building options, and Repair All.
