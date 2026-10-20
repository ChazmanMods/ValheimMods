# Runic Paths 0.3.1 — prepared roads test build

Roads are planned and their terrain surfaces are compiled **before the player spawns**. During play, Valheim reads the cached road heights and dirt mask while generating its normal terrain meshes. The old player-following planner, terrain painter, repeated terrain saves and grass-reset loop are no longer used in normal operation.

## Manual installation

1. Save and close Valheim. Keep a copy of your test world and its existing `BepInEx/config/RunicPaths` folder.
2. Import `Chazman-RunicPaths-0.3.1.zip` with r2modman's local-mod import, replacing the previous version. Do not leave two RunicPaths DLLs installed.
3. Start modded Valheim. The loading screen displays preparation status. First-time **WholeWorld** preparation can be lengthy; it deliberately happens before combat or exploration. Subsequent loads reuse the cache.

This package does not install itself or change an r2modman profile. Built against Valheim 1.0.12 and BepInExPack 5.4.2333. Host and all clients need this version; multiplayer transfer is implemented but has not had a live multi-client test.

## Preparation retry and resume changes in 0.3.1

Each POI contributes its eight nearest candidate connections. All POIs remain in the coverage report, but a bounded search cannot guarantee a route to every one. A link shares a budget of three times MaximumRouteSearchNodes across all grids, gap searches and alternate approaches. Once failed searches on an island consume twelve times that setting, remaining candidates receive short searches (at most 768 nodes each); expensive optional loops stop. This prevents a few unreachable locations from causing an exhaustive all-pairs retry tail.

The surveyed island, successful roads, junctions, failed attempts and search counters are saved after every completed connection attempt. Quitting during a search repeats only that unfinished attempt. Quitting during the initial land survey still repeats the survey. The loading display now distinguishes completed landmasses, built links, attempted links and search nodes.

Version 0.3.0 did not save unfinished islands: work lost when quitting that version cannot be recovered. Full-world preparation still takes time and has no fixed completion-time guarantee. Increase Preparation / Revision while logged out to explicitly retry deferred connections.

## What changed

- Full eligible-landmass preparation is the default. Roads are already planned on undiscovered islands, without exploring the map, creating map pins, or loading every island's scenery.
- Every matching POI is considered. The old per-island POI cap is ignored. Coverage reports record connection status, including isolated and unreachable POIs. Geometry limits can still prevent a connection.
- Routes share POI junctions, prefer existing corridors, and may add short loops that avoid long detours. Coarse searches retry on a finer grid for contour routes and switchbacks. River/ravine gaps remain unbridged.
- Road targets are computed in world coordinates. Neighboring tiles use the same elevation; roads no longer taper back to the original terrain at every tile boundary.
- Dirt paint is applied before the native texture upload. Cultivation, paving and recorded player paint take precedence. Decorative clutter is filtered on cleared roads.
- New natural vegetation is excluded before spawning, including trunk/rock footprint clearance. Existing supported trees/stones are relocated during preparation, preserving their ZDO identity and data. Objects without a safe destination remain and are counted in the log. No deletion fallback is used.
- Existing structure colliders receive two meters of protection; player buildings use PlayerBuildBuffer. Ungenerated POIs still use conservative location footprints because their final randomized layout does not yet exist.
- Old journaled road edits are composed through their baseline instead of added twice. The old journal is retired before the first subsequent player terrain operation, with a recovery copy retained.

## Settings

Edit `BepInEx/config/com.chazman.runicpaths.cfg` while Valheim is closed. Road geometry/surface settings apply on the **next world load**, not beneath an active player.

| Setting | Default | Effect |
|---|---|---|
| Preparation / Scope | WholeWorld | All eligible landmasses. SpawnIsland is a shorter trial. ExistingRoutes only compiles saved geometry and does not improve coverage. |
| Preparation / POIs | Structure/dungeon patterns | Comma-separated exact names or trailing `*` prefixes. Use `*` to include all locations in supported biomes. Boss/trader routes may then be included. |
| Preparation / ExtraLoopsPerIsland | 2 | Up to this many useful extra connections. Set 0 for a branching network. |
| Preparation / MillisecondsPerFrame | 12 | Loading-screen work budget. Individual native calls and checkpoint writes may exceed it. |
| Preparation / Revision | 1 | Increase to explicitly retry/reprepare a network, including previous routing failures. |
| Paths / Width | 3 m | 2–12 m. Existing saved surfaces are rebuilt before the next spawn. |
| Paths / MaximumSlope | 0.55 | Maximum rise/run used for new route searches. |
| Paths / MaximumGap | 32 m | Maximum river/ravine break; never creates a bridge. |
| Terrain / MaximumRaise / MaximumLower | 1.5 m each | Maximum change from original terrain. 0 disables that direction of grading. |
| Protection / PlayerBuildBuffer | 12 m | Extra protection around existing player construction. |
| Protection / MaximumRelocationDistance | 24 m | Maximum search for a safe roadside destination. |
| Performance / MaximumRouteSearchNodes | 16000 | Search limit per route attempt; unsuccessful POIs are reported. |
| Performance / MaximumIslandCells | 80000 | Landmass survey limit; oversized surveys are reported. |

`MaximumPOIsPerIsland`, `WorkMilliseconds`, the older `Paths / POIs` and `ConfirmExistingNaturalWorld` entries are legacy settings, unused by prepared mode. Preparation has its own POI selector. The legacy code is retained solely for regression testing.

Changing width or earthwork limits recompiles saved roads before play. Existing route geometry is retained; narrowing a road does not move relocated objects back. Changing routing limits extends the established network rather than deleting old roads. Previously prepared support terrain under captured structures is retained when rebuilding.

## Preparation files and recovery

Files live under `BepInEx/config/RunicPaths/Prepared/<world UID>/`:

- `surface.bin`: world/seed-bound, checksummed road terrain cache. Keep it with the world backup.
- `surface.bin.bak`: previous prepared surface after a rebuild.
- `network-<signature>.json` and `completed-<signature>.txt`: resumable network checkpoints. Interrupting preparation may repeat the current island and surface compilation; completed island routing is reused.
- `coverage.txt`: eligible POIs and connection outcomes. This is an administrator report containing undiscovered coordinates, not an in-game map feature.
- `relocations.jsonl`: original and destination positions written and flushed before each object move. This is a recovery record, not an automatic world rollback tool.

Restarting resumes preparation. A corrupt or mismatched surface fails closed with a loading-screen error instead of silently replacing roads. With the game closed, preserve the failed file for diagnosis; rename it to allow recompilation from network checkpoints.

The cache supplies the road surface at runtime. Uninstalling this version removes that overlay; world vegetation exclusions, relocations, player edits and older native terrain changes can remain. Back up the world **and** the RunicPaths directory together. This release does not automatically back up Steam Cloud world files.

## Testing limits

This is an architectural test build, not a claim that all stutter is solved. Native mesh generation, normal world streaming, vegetation placement and world saving still cost time. No rendered-client frame-time guarantee has been measured.

The automated native scenario covers an existing 169-tile network, repeated terrain generation, a shared seam, legacy conversion, later player edits and object relocation. Whole-world preparation duration/memory, long-term multiplayer behavior, mod conflicts and final visual quality still need broader testing. Ashlands and Deep North routing, door-level POI entrances, bridges and paving are not implemented. Ungenerated POI footprints remain conservative.

Run `Build-Package.ps1` to build and execute the pure behavior tests. See `VALIDATION.md` for measured results.

## Support Chazman's Mods

If you enjoy my Valheim mods and want to support continued development, testing, multiplayer compatibility, and new projects, I would appreciate it.

Support is completely optional. Playing the mods, reporting bugs, sharing feedback, and recommending them to other players helps just as much.

Thanks for supporting Chazman's Mods and the continued development of RunicSuite, ArcaneDecor, and other projects!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
