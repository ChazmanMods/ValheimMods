# 0.3.1 — bounded preparation and in-island recovery

- Replace exhaustive POI pair retries with the union of each POI's eight nearest candidate links; retain every eligible POI in outcomes.
- Share search budgets across grid, gap and approach retries. Switch to short searches after the island's failed-work budget is exhausted, and stop optional loop retries.
- Persist survey, roads, graph anchors, attempt cursors and counters after each completed link attempt. Resume both failed and successful attempts without repeating their searches.
- Show built links, attempted candidates and searched nodes while an island is unfinished.
- Preserve the previous prepared network when changing the preparation signature.
- Add successful/failing checkpoint-resume regressions and a 364-POI adversarial workload.

# 0.3.0 â€” prepared roads test build

- Replace normal player-following planning/painting with loading-screen preparation and cached terrain generation hooks.
- Add world-bound, checksummed surface persistence, resumable island checkpoints, loading status, and bounded multiplayer cache transfer before client spawn.
- Precompute shared elevation targets and continuous paint; remove tile-edge taper from prepared mode. Apply during native terrain generation without a second road-triggered mesh rebuild.
- Consider all selected POIs, record outcomes, share junction anchors, reuse corridors and add optional useful loops.
- Exclude new natural vegetation using prefab footprints and constant-time clearance masks. Relocate existing obstacles before play with write-ahead recovery records.
- Capture existing structure footprints, preserve prepared support terrain on rebuild, and compose legacy journals without double grading. Preserve later player cultivation and grading.
- Settings take effect on the next load. WholeWorld is the default; SpawnIsland and ExistingRoutes are smaller preparation scopes.
- Keep fog of war unchanged. This release still requires rendered-client profiling and live multiplayer validation; ungenerated POI protection remains conservative.
# 0.2.5

- Reduce grading to three terrain samples after selecting the nearest route segment, instead of resampling for every closer candidate.
- Add eight-cell budget checkpoints and move journal serialization before the atomic terrain commit, with ownership/revision/protection revalidation.
- Use native paint-only regeneration when height arrays are unchanged; avoid unnecessary render/collision mesh rebuilding.
- Restrict grass invalidation to the tile footprint, include scheduling in the work timer, and remove duplicate scheduling.
- Log native commit duration. This is a performance mitigation; client stutter is not yet measured or guaranteed eliminated.
# 0.2.4

- Prevent temporary road-induced tile tears by smoothly tapering grading to zero at shared tile edges. Restore unchanged, journal-owned old edge deformations when revisited, including protected edge cells.
- Prioritize nearby loaded tiles and reserve most work time for pending terrain updates; reduce idle scheduling delay.
- Replace broad POI painting/clearing exclusions with loaded generated structure collider bounds plus two meters. Retain player construction protection and solid obstacle checks.
- Remove duplicated width padding from route POI clearance; stop suppressing road clutter cleanup based on the broad POI circle.
- Force existing roads through the repaired terrain pass. Existing road geometry remains preserved.
# 0.2.3

- Paint roads over height-only edited ground while preserving its elevations and nearby grading buffer. Preserve explicit player paint and construction protection.
- Apply terrain regeneration before invalidating grass patches, preventing clutter from rebuilding against stale terrain paint. Refresh cached clutter when route settings change.
- Retry failed land routing with a 4 m grid, shallower diagonal switchback turns and a larger detour allowance before gap fallback. Roadside slope checks use the configured raise/lower limits. Retry alternate POI approaches for unreachable endpoints.
- Revisit terrain stamps and extend existing island networks with the improved planner; preserve established roads.
# 0.2.2

- Fix disappearing roads after background network completion: retain established geometry and protection data when merging overlapping island plans, including partial results. Exact duplicate segments are not accumulated.
- Prefer continuous land routes before trying river/ravine gaps. Each attempt obeys the configured search-node budget.
- Width/height changes still rebuild existing terrain. Planning changes now add connections; they no longer remove old roads.
- Add native network-completion regression and offline route-data recovery helper.
# 0.2.1

- Clear small natural trees using their Destructible component, including Beech_small1/2.
- Include loose stones with trigger/item colliders in road clearing. Suppress decorative pebbles inside painted roads.
- Optional world-specific, one-time natural-object baseline for known untouched worlds; preserve future unknown placements and tagged player objects through relocation.
- Tag supported objects created during player placement, including small trees.
- Reduce excessive POI approach setback while retaining protected structure footprints. Replan saved networks and revisit old terrain stamps.
# 0.2.0

- Bounded native terrain grading with independent MaximumRaise and MaximumLower limits.
- Terrain journals for live widening, narrowing, grading changes and removal of obsolete road sections; preserve detectable later player edits.
- Remove proven natural trees, bushes and supported stones. Relocate player/unknown trees, saplings and supported stones to safe roadside ground.
- Track grown player trees and synchronize relocated static objects through their ZDO positions.
- Split river/ravine connections into separate bank roads, with configurable MaximumGap and no bridges.
- Replan existing activated networks after upgrade or planning-setting changes. Reload config file edits during play; host settings govern multiplayer roads.
# 0.1.1

Fix missing trails: use native physical-obstacle layers so tree visibility volumes cannot suppress terrain paint; avoid Unity ClosestPoint calls on unsupported non-convex rock meshes, which returned zero distance across whole routes. Retained props now exclude only their collider footprint instead of a full trail-width circle around each nearby route sample. Colliders belonging to vegetation selected for removal no longer suppress paint. Existing 0.1.0 completion stamps are revisited once, while existing paint, terraforming, builds and POI protection remain respected. Add per-zone painting diagnostics and an unassisted Testheim spawn regression test.
# 0.1.0

Initial testing release: island discovery, deterministic dry-land route planning, rounded dirt paths, native terrain persistence, nearby route delivery to clients, player/POI protection, and provenance-based vegetation clearance. No map reveal, paving, bridges or terrain leveling.






