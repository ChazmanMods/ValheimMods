# Runic Agriculture

Farming should feel like building a thriving homestead, not clicking the same patch of ground hundreds of times. Planting straight rows, checking spacing, harvesting every crop, and starting over can turn a useful system into a repetitive chore.

**Runic Agriculture turns field work into an organized operation.** Preview clean planting patterns before committing them, harvest mature crops across an area, and quickly replant what you gathered.

## Major features

- Plant rows, grids, circles, stars, triangles, half-circles, and trapezoids.
- See live placement, spacing, terrain, biome, access, and resource feedback.
- Snap planting patterns to existing crop rows and match their spacing.
- Harvest matching mature crops across a configurable area.
- Confirm a replant layout using the crop positions you just harvested.
- Use configurable keyboard, mouse, and controller controls.

## How it feels in-game

You spend your time laying out the field you intended instead of walking backward while clicking every individual seed. Harvests become a satisfying sweep, and the next crop can go into the ground while the layout is still fresh.

## Safety and compatibility

Seeds, stamina, cultivator durability, crop spacing, biome rules, cultivated ground, wards, and ordinary placement checks still matter. Agriculture uses Valheim's normal local planting and interaction paths, does not create free resources, and is independently installable with BepInEx.

## Installation

Install `RunicAgriculture.dll` in `BepInEx/plugins` on each client that wants the features.
BepInEx is the only package dependency. Runic Core, Persistence, Permissions, Transactions, and
the other Runic gameplay mods are not required.

The mod is client-owned and uses Valheim's native mutation paths:

- planting calls the local owning player's normal `Player.PlacePiece` path, consumes each cell's
  normal resources from personal inventory first and then eligible nearby chests, and charges one
  normal stamina/cultivator action for the one accepted batch click;
- every position is validated again immediately before placement;
- area harvest checks current ward access and invokes the exact loaded `Pickable` objects through
  their normal interaction path;
- the directly aimed Pickable remains first in the bounded candidate order;
- replant offers exist only in memory and are consumed by an explicit confirmation.

There are no operation journals, recovery scans, account quarantine states, remote Agriculture
protocols, or suite-wide inventory locks. A failed or interrupted batch releases Agriculture's
small process-local exclusion scope and cannot block another mod's inventory or tools.

## Planting patterns

### Snap to existing rows

Snapping is on by default. Aim near at least two matching seedlings or mature crops: the preview
aligns its planting cells with that row and measures its crop spacing. When a parallel row is
visible, it also matches the gap between rows, even if that differs from the crop spacing.
This works for extending a row or adding aligned rows beside it. The HUD shows **Snapped** and
the measured crop/row spacing. Existing plants remain untouched; occupied or invalid cells still
go through the normal placement checks and the configured skip/block policy.

Hold **Ctrl** for free placement with your configured spacing and wheel rotation. To turn automatic
snapping off, set `Planting Pattern -> SnapToExistingRows = false` and choose PlayerHeading or
WorldAxes alignment. ExistingCropRow explicitly enables snapping. With no usable row nearby,
the preview falls back to your configured spacing and heading/world alignment.

Matching samples loaded crops within 6m. A single crop cannot establish spacing; crowded scans
or rows tighter than the selected crop's safe spacing fall back to free placement. An isolated
pair supplies its measured gap; additional plants help recover spacing across missing plants.
Replant confirmation keeps the exact harvested positions.

### Shapes and footprint

Select a cultivator crop to display a bounded preview. The ordinary/default shape is the basic
rectangular Grid. Existing 1.0.0 configurations receive a one-time reset to Grid so a previously
saved shaped pattern cannot make the row/column footprint look incomplete; later deliberate shape
selections are preserved. Supported shapes are Row, Grid, Circle,
Star, Right Triangle, Half Circle, and Trapezoid. Rows, columns, spacing, orientation, mirror state,
and trapezoid taper are configurable. Rows and columns independently adjust from 1 upward. The
absolute safety boundary is 1,600 live cells, so every valid configured footprint through a 40×40
Grid is previewed without a separate carried-seed cap. Rows and columns define the requested
footprint; available resources only decide which otherwise-valid cells can plant. Grid and Row
shortfalls fill from the player's right toward the left (each column front-to-back). Circle, Star,
Right Triangle, Half Circle, and Trapezoid shortfalls fill from the centre outward. Invalid early
cells are skipped before resources are allocated, so later valid cells backfill them.

Confirmation plants only positions allowed by current terrain, slope, biome, cultivation, crop
spacing, distance, wards, no-build zones, players, and water. One batch must have enough stamina
and cultivator durability to start, while every planted position still consumes its own normal
resource requirements unless free-build or no-cost mode is active.
The invalid-position and resource-shortfall policies can either block the batch or process a
predictable valid subset.

Green ghosts are ready. Red ghosts have valid ground but lack enough combined planting resources
for the requested configuration. Muted amber ghosts failed a terrain, spacing, access, biome, or
crop rule. There are no blue resource ghosts. Current stamina and tool durability do not recolor otherwise valid ghosts; the
compact HUD reports those as a single batch-action problem. Having enough
seeds does not override spacing, cultivation, slope, biome, range, water, ward, or obstruction
rules. The bottom native hint panel reports up to three exact amber-cell causes, such as
`7 too close, 2 too steep`, alongside the ground-valid count. It also names the active pattern,
rows, columns, combined-resource readiness, and hard safety cap so a Half Circle cannot be
mistaken for an incomplete Grid. Agriculture automatically raises too-tight spacing to the next safe tenth of a
meter for the selected crop, avoiding the 0.5 m boundary that can touch another plant collider.

Bare mouse wheel rotates the complete ground-plane pattern by Valheim's current build rotation
increment. The spacing editor clamps at the selected crop's native minimum distance, so scrolling
cannot bunch previews closer than Valheim permits.

## Planting resources

Normal planting uses the player's inventory first, then loaded nearby chests in deterministic
distance/ZDO order. `Planting Resources/NearbyChestRangeMeters` is configurable from 1–30 metres
and defaults to 30. A chest is eligible only while it is static, unused, accessible to the local
player, outside any denying ward, locally owned through its exact native network endpoint, and
byte-for-byte synchronized with that endpoint. Agriculture never claims chest ownership and sends
no custom inventory RPC. Every requirement on the selected cultivator piece is budgeted, including
pieces that require more than one resource type. Free-build and no-cost modes remain resource-free.

## Area harvest and replant

Area harvest works on every ready, permitted Valheim `Pickable`, whether or not it has a matching
plant prefab. `Harvest/OfferConfirmedReplant` controls only the optional replant offer. The
cultivator does not need to be equipped while harvesting. A replant offer appears only when that
setting is enabled, the harvested Pickable maps to a registered grown Plant, and the player has
planting access at that location. Unmapped wild Pickables are harvested without a replant warning.

An accepted area harvest considers only loaded objects with the exact same registered prefab name
and hash. Picked, unavailable, tar-blocked, out-of-radius, or ward-denied objects are skipped.
Destructive `PickableItem` fixtures are excluded. Harvested positions become a bounded in-memory
offer; select the matching cultivator crop afterward and confirm the offer through the native
planting path.

## Default controls

| Action | Keyboard/mouse |
|---|---|
| Plant visible pattern | `Left Click` |
| Cycle pattern | `LeftAlt + Build Menu` or `LeftAlt + O` |
| Select Row through Trapezoid | Numpad `1` through `7` |
| Rotate complete pattern | `Mouse Wheel` |
| Change rows | `LeftAlt + Mouse Wheel` |
| Change columns | `LeftShift + Mouse Wheel` |
| Change spacing | `LeftAlt + LeftShift + Mouse Wheel` |
| Mirror/switch side | `LeftAlt + LeftShift + L` |
| Adjust left taper | `LeftAlt + LeftShift + [` / `]` |
| Adjust right taper | `LeftAlt + LeftShift + ;` / `'` |
| Area harvest | `LeftAlt + E` |
| Confirm replant offer | `LeftAlt + T` |

Set Rows and Columns to 1×1 when only one crop should be planted. Controller defaults use
Controller Alt + Place to confirm, Controller Alt + Left Stick Click to cycle, and Controller Alt + Use for area harvest. During an active crop preview, unmodified D-pad
Up/Down chooses an editable setting and Left/Right changes it. All bindings remain configurable and
follow Valheim's active controller layout and rebinding.

Plain Build Menu and Ctrl+wheel remain available to Valheim and other mods; bare wheel is owned by
Agriculture only while its cultivator pattern preview is active. Agriculture consumes only an
accepted gesture in its active context. Disable
`Status/ShowContextualControls` to restore the ordinary bottom hints and disable the contextual
wheel, numpad, Alt+Build Menu, and D-pad editor routes; explicit configured shortcuts remain.
When enabled, Agriculture leases Valheim's own build-hint rows at the bottom of the screen. The
compact native panel shows `Rows −/+` and `Columns −/+` as separate controls, the current Grid
dimensions, rotation, spacing, readiness, and the direct Numpad 2 Grid choice. Its configured scale
is applied relative to the game's native scale and restored with the original text when the crop
preview closes; Agriculture does not draw a separate black menu.

## Compatibility

Disabling the mod clears its previews and leaves vanilla agriculture unchanged. It does not own
crop growth time, yield, biome rules, seed generation, inventory topology, crafting material
sourcing, or unattended automation. Runtime member checks target Valheim 1.0.7 and fail closed
if required signatures are unavailable.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicAgriculture` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
