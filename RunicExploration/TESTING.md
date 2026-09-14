# Runic Exploration 1.0.2 maintainer verification record

This is an internal release record, not a player installation checklist.

## Automated contract

The focused suite verifies identity/version/manifest alignment, a standalone BepInEx-only runtime
dependency, exact Valheim 1.0.7 Minimap/pin/player/ship/wind/gamepad signatures, the installed
world-to-pixel/explored-map formulas, and that `ShowPointOnMap` changes only existing map view state.
It verifies unknown pins are rejected before name/type/owner reads, no-map mode gates all Minimap and
optional-peer access, dedicated processes patch nothing, selected pins are revalidated, and no
world generator, zone location, entity, ZDO, RPC, ownership, pin/map-data, inventory, physics, or
scene mutator/scan is called.

Core tests cover bounded text, bidi/rich-text removal, explicit asset tags, personal/shared/mixed
scope, deterministic order/merge without source mutation, filters, result caps, directions,
distances, sailing/tombstone truth labels, oversize rejection, and 100/1,000/10,000 profiles.
Packaging tests verify the exact 256 x 256 PNG, documentation/config limits, and absence of hard
Runic Portals/Awareness references. The optional adapter may inspect only BepInEx plugin metadata
and the enabled setting; it is forbidden from querying portal entries, counts, tags, networks, or
destinations.

## Normal-play compatibility observations

Open a normally mapped world with personal and shared pins; search/filter and center a known pin.
Confirm an unknown-area saved/modded pin never appears, shared suppression works, exact duplicates
group only in the custom list, and vanilla pins remain unchanged. Test a tombstone across water or
mountain terrain and confirm only straight-line guidance plus the topology warning. Test current
ship/wind/biome context, no-map worlds, small/ultrawide safe areas, mouse text entry, controller
readability, configuration changes, and Runic Portals absent/present.

Automated tests cannot reproduce a full Unity map/render/input frame, every modded pin provider, or
multiplayer map synchronization timing. Those are bounded normal-play observations; the module's
failure mode is to hide its panel and retain vanilla behavior.
