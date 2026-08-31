# Runic Exploration

Runic Exploration 1.0.0 is a client-side, display/navigation-only known-world browser for
Valheim 0.221.12. Its only runtime requirement is BepInExPack Valheim 5.4.2333. It never explores
terrain, discovers locations, scans entities, changes pins, tracks remote players, requests network
ownership, sends RPCs, or mutates world/gameplay state. If its installed-game contracts do not
match, it stays hidden and Valheim's map remains available unchanged.

## What it does

- Searches and filters saved pins whose exact map pixel is already marked explored in the local
  Minimap arrays. Optional shared-map coverage is applied only to pin data Valheim already placed
  in this client's saved-pin list.
- Groups exact one-metre/name/type duplicates for display while retaining the underlying vanilla
  pins and their authorship unchanged. Results distinguish personal, shared, and mixed sources.
- Recognizes tombstones, beds, bosses, and explicit known-asset name tags: `[boat]`, `[cart]`,
  `[animal]`/`[tame]`, `[portal]`, `[bed]`, and `[tombstone]`/`[grave]`.
- Revalidates the selected source pin and explored-map evidence before asking Valheim to center its
  existing large map on that point. It shows straight-line distance, eight-way direction, and
  elevation. Tombstones include an explicit warning that topology, shores, and passability are not
  inferred and that no remote recovery occurs.
- While the local player is actually controlling a ship, shows the current local ship speed/sail,
  wind power/intensity, current biome, and approximate straight-line distance to the selected
  known pin.

Every pin row is labeled `LAST KNOWN`. This release does not claim that a boat, cart, animal,
portal, bed, tombstone, or other pin still exists. The sailing panel alone is labeled
`LIVE LOCAL SHIP`, because it comes from the player's current controlled ship.

This MVP does not auto-create, live-confirm, or remove asset pins and does not offer a free-text
author filter. Assets appear only through a vanilla/modded saved pin already known to the client;
destroyed or moved assets remain `LAST KNOWN` until their underlying pin is updated or removed.
Personal/shared source and category filters provide the bounded merge-quality controls in 1.0.0.

## Privacy and known-state boundary

The source is only `Minimap.m_pins` plus the already-populated `m_explored` and, when enabled,
`m_exploredOthers` arrays on the local client. For each pin, Exploration reads only position and
the saved flag until the exact explored-cell gate passes. Names, types, checked state, and owner
metadata are not inspected for an unknown or unsaved pin. It never consults `WorldGenerator`,
`ZoneSystem` locations, loaded characters, ships, tameables, pieces, ZDO collections, scene scans,
physics/radar queries, or peer map state.

No-map worlds are checked before Minimap, pin, or optional-integration access. Opening no-map mode,
disabling the module, losing the local player/map, or encountering incompatible/oversized state
clears all cached names, coordinates, selection, navigation, and optional status. Dedicated/batch
processes install no Harmony hooks and remain inert.

Shared pins are only pins already present in the local saved-pin list and on already known map
pixels. `IncludeSharedPins=false` suppresses both shared coverage and records whose vanilla owner ID
does not identify the local player. Numeric owner IDs, pin coordinates, and names are never logged.

## Authority, multiplayer, and server boundary

Runic Exploration is strictly a local-client display and map-navigation layer. It has no server authority,
sends no multiplayer RPC, requests no ZDO ownership, and cannot ask a server or another
player to reveal, confirm, move, remove, or create anything. Shared-map rows are limited to saved
pins and explored coverage already replicated into this client's vanilla `Minimap`; they are not a
live server directory. On a dedicated server/batch process the module is inert. Centering the map is
the only requested outcome and remains Valheim's local UI operation—never a world or server mutation.

## Bounds and refresh behavior

- Pin-list hard ceiling: 10,000. A larger list is rejected before per-pin inspection.
- Map texture hard ceiling: 4,096 x 4,096 with exact coverage-array length validation.
- Raw third-party pin-name inspection: 256 UTF-16 characters; retained label: 64 characters.
- Search query: 48 characters; displayed results: 5–24, with a hard ceiling of 24.
- Index order: deterministic normalized name, pin type, quantized X/Z/Y, bounded raw-name hash,
  then source index. Search preserves this order.
- Refresh: configurable 0.25–3 seconds (0.5 default). The reusable capture list is fingerprinted;
  index strings and GUI content rebuild only when bounded known evidence changes. A stable changed
  fingerprint waits for a 0.75-second quiet period; continuously changing evidence is coalesced to
  at most one rebuild every two seconds, so the 10,000-pin adversarial case cannot allocate a new
  index every 0.25-second sample. Unknown/unsaved pin counts, indices, and coordinates do not enter
  this retained fingerprint. Drawing uses the cached index/search/readout and does not enumerate map pins.

Repeated-change regression profiles over four seconds perform at most two builds: approximately
0.16 MB at 100 pins, 1.59 MB at 1,000 pins, and 15.91 MB total at 10,000 pins on the audited test
runtime. A single 10,000-pin build remains about 7.96 MB/12.5 ms; the gate reduces sustained churn
frequency rather than claiming an allocation-free immutable index.

## Optional integration

Runic Portals and Runic Awareness are not project, assembly, package, or manifest dependencies.
When enabled Runic Portals metadata is runtime-discovered through BepInEx, Exploration may state
only that its directory remains available in portal context. It does not use a registry, capability,
service, or protocol and deliberately does not query directory entries, counts, tags, networks,
destinations, or private portal metadata. Missing or incompatible metadata simply removes that
status line.

## UI and controller behavior

The panel appears only while Valheim's large map is open. Search, category, and source filters are
mouse/keyboard controls. Text focus and pointer input over the panel are isolated from the vanilla
map; all other map input falls through. A controller continues to use Valheim's normal map controls
and receives an additional configurable 1.0–1.5 readability scale for the read-only panel. This MVP
does not add a controller cursor or custom gamepad binding. On a safe area too small for the bounded
panel, the panel hides and the vanilla map remains usable.

## Configuration

`General.Enabled` controls the module. `Panels` independently controls the browser, selected-pin
navigation, and sailing readout. `KnownMap.IncludeSharedPins` controls local shared coverage/source
rows. `Performance.RefreshIntervalSeconds` controls sampling. `Display` controls result count, UI
scale, controller multiplier, and side. `Diagnostics.VerboseLogging` logs counts/config changes only.
