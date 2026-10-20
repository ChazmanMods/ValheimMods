# Runic Settlements 0.1.2

Prepare up to five community building sites on the spawn island, connected by paved roads. Default clearings are **120 metres across**, with a 14-metre blended shoulder. Each site has room for community construction, sparse perimeter oaks/pines, and fuel-free standing iron road lights.

## Install manually

1. Close Valheim and disable **RunicPaths** in the selected r2modman profile. The two terrain mods cannot run together.
2. Import `Chazman-RunicSettlements-0.1.0.zip` using local-mod import. Requires BepInExPack Valheim 5.4.2333.
3. A **fresh test world is recommended for this first release**. Keep backups of an existing world before preparing it.
4. Start the world and allow settlement preparation to finish before spawning. Look for the tall, double-sided **SETTLEMENTS** sign flanked by permanent lights near the spawn monument. It gives the compass direction and distance to the road entrance. A matching **SETTLEMENT ROAD / FOLLOW THE LIGHTS** marker identifies the entrance. Follow the paved road to the clearings. No map exploration or pins are added.

### Updating from 0.1.0

Version 0.1.1 adds these markers to existing prepared worlds on their next load; no new world or terrain rebuild is needed. Install the ZIP manually while Valheim is closed. Spawn markers seek a dry, gently sloped position 24–64 metres from the monument, outside existing structures. If no safe space exists, the mod logs the problem and retries on the next load. Signs are 2.5 times normal size, mounted on four-metre posts, readable from both sides, and protected from weather/support decay. Players can remove them; removed pieces stay removed. The four marker lights are additional to the configured road-light budget.

The package does not update your profile automatically. All players and the host need the same mod version; live multiplayer compatibility still needs testing.

## What it does

- Surveys dry land connected to spawn within 2,000 m. It never bridges water or generates communities on other islands.
- Selects separated Meadows sites with full-footprint inspection. Default maximum cut and fill are eight metres each. Flat ground is preferred over heavy earthworks.
- Keeps the full 120 m interior flat, blends the perimeter into existing terrain, and suppresses natural vegetation and decorative clutter inside the clearing and road footprints.
- Connects accepted sites to the spawn road network; tries an additional connection between sites. Unsafe/unreachable candidates are rejected within a fixed attempt limit.
- Uses native paved terrain, not thousands of placed floor pieces.
- May replace small generated ruins/stone formations overlapping the clearings. Player construction, the spawn monument, boss/trader locations and dungeons remain protected. Replaced locations are listed in the report.
- Moves supported existing trees, stones and bushes off prepared terrain instead of destroying unknown/player-planted objects. Objects without a safe destination are retained and counted in the log.
- Adds at most 96 standing iron lamps, normally spaced 40 m apart, and up to two perimeter trees per site. Generated lamps require no fuel and cast no dynamic shadows. Ordinary player lights are unaffected. Lamps remain destructible; removed decorations are remembered.
- Prepares terrain before play, saves the plan and terrain cache, and reuses them on subsequent loads. No ongoing route planner follows the player.

## Configuration

`BepInEx/config/com.chazman.runicsettlements.cfg`

| Section / setting | Default | Purpose |
|---|---:|---|
| Sites / Count | 5 | Maximum sites, 1–5. Five is a target, not a guarantee. |
| Sites / Diameter | 120 | Flat interior diameter, 40–160 m. |
| Sites / MinimumSeparation | 300 | Minimum distance between site centres. |
| Sites / SearchRadius | 2000 | Search distance from spawn on connected land. |
| Sites / ReplaceSmallRuins | true | Allow replacing minor generated ruins inside clearings. |
| Terrain / MaximumRaise | 8 | Maximum fill above original ground. |
| Terrain / MaximumLower | 8 | Maximum cut below original ground. |
| Roads / Width | 5 | Paved-road width. |
| Decorations / LampSpacing | 40 | Desired spacing in metres, also limited by safe placement. |
| Decorations / MaximumLamps | 96 | Total light limit across this world's network. |
| Decorations / TreesPerSite | 2 | Sparse perimeter trees, 0–4 per site. |
| Preparation / MillisecondsPerFrame | 12 | Loading-stage work budget; individual native calls can exceed it. |

**A completed layout is locked to its world.** Changes to geometry/decorations apply to worlds that have not been prepared. They do not expand a clearing through an occupied base. If zero sites fit, the world still loads without changes; adjust settings while logged out and try again.

## Saved data and recovery

`BepInEx/config/RunicSettlements/<world UID>/` contains:

- `settlements.json`: world/seed-bound plan, sites, roads, decoration IDs and removed-decoration records. Saved after each accepted site.
- `surface.bin`: checksummed native terrain targets and surface materials.
- `settlements-report.txt`: site coordinates, selection diagnostics and replaced generated locations.
- `relocations.jsonl`: write-ahead records for moved existing obstacles.

Back up this folder **together with the world**. Plan and cache files are part of the world setup; removing the mod/cache restores the underlying unmodified terrain display, which can leave player buildings unsupported. Replaced ruins and moved objects are world changes and are not automatically undone by uninstalling. Restore the matching pre-install world backup to undo preparation.

## Limits of this test release

Some spawn islands cannot fit five 120 m clearings within the terrain and protection limits. A smaller diameter may fit more sites. The mod preserves player terraform edits, so existing edited terrain can interrupt a perfectly flat platform; a fresh world gives the cleanest result. It does not erase old RunicPaths terrain edits.

Fewer natural objects does not guarantee higher FPS after a large community builds there. Trees, lights, the native terrain engine and players' buildings still cost time. No zero-lag or rendered FPS claim is made. See `VALIDATION.md` for actual tests and remaining gaps.

## Support Chazman's Mods

If you enjoy my Valheim mods and want to support continued development, testing, multiplayer compatibility, and new projects, I would appreciate it.

Support is completely optional. Playing the mods, reporting bugs, sharing feedback, and recommending them to other players helps just as much.

Thanks for supporting Chazman's Mods and the continued development of RunicSuite, ArcaneDecor, and other projects!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
