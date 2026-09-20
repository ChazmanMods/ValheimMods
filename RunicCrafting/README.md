# Runic Crafting 1.1.1

Your workshop already contains the wood, stone, metal, and components you need. Moving those materials from a nearby chest into your pockets—and back again—does not make crafting more meaningful; it makes the workshop feel disconnected from its own supplies.

**Runic Crafting lets crafting and building use eligible materials stored nearby.** It also improves requirement information, adds configurable workshop policies, supports carefully controlled stationless building, and provides Repair All.

## Major features

- Craft recipes using carried materials first and nearby containers second.
- Use nearby meat at cooking racks and fetch fuel for fires, refillable lights, ovens and smelters.
- Build from eligible workshop storage without hauling every stack by hand.
- See combined carried and nearby material availability.
- Reuse verified chest material counts, share queries within menu refreshes, and briefly reuse UI availability answers to reduce repeated work in well-stocked bases. Crafting and placement still check fresh materials.
- Configure who may use a station and who may use its nearby supplies.
- Repair all eligible inventory equipment in one action, with optional automatic repair when a station successfully opens.
- Repair nearby hammer structures with a configurable hotkey, radius, and independent enable switch; optionally start the same area repair after a vanilla hammer repair.

## How it feels in-game

Your workshop behaves like one connected workspace. You stock the room, approach the bench, and make what those real supplies allow instead of performing an inventory-transfer ritual before every recipe or wall section.

## Cooking and refueling from chests

Press the object's normal **Use** button. When you are not carrying a suitable item, Runic Crafting
fetches one accepted ingredient or fuel item from an eligible nearby chest and lets Valheim perform
the normal action. No Production links or replenishment examples are needed.

- Cooking racks/ovens use their native ingredient list. Finished food is collected first; fire and
  free-slot checks remain in effect.
- Fires use wood, resin-fueled standing/wall torches use resin, and other supported objects use
  their own fuel definitions. Normal toggle and hold-repeat behavior is preserved.
- Keep inventory space for one fetched item. If the native action declines before consuming it,
  that item stays in your inventory; it is not automatically sent back or refunded after a network request.
- This is manual loading, not background automation. Ordinary crafting at cauldrons/workbenches
  still uses the recipe system. Smelter ore loading and handheld torches are not part of this feature.

Both features default on. Under `[Manual Interactions]`, configure `CookFromContainers`,
`RefuelFromContainers`, and `RangeMeters` (default 20 metres around the object, limited by
`[Materials] RangeCapMeters`). The master Enabled switch disables everything. Accessible chest,
ward, personal-container, synchronization and item-protection checks still apply.

Install RunicCrafting on each player's client, including a listen host. A dedicated server does
not need RunicCrafting for this feature. RunicProduction may remain installed; its linked automation
is independent, while these actions use the normal game interaction.

## Safety and compatibility

Materials are still consumed normally. Containers must be loaded, accessible, synchronized, ward-allowed, and locally owned; uncertain containers are skipped. Crafting uses short local rollback scopes rather than global locks or persistent recovery systems. It is standalone, requires only BepInEx, and supports solo, listen-server, and dedicated-server clients under Valheim's native ownership rules.

## Material ownership

Crafting considers the local player's carried inventory first, then eligible nearby containers in
stable distance and endpoint order. Recipe previews read synchronized inventory snapshots without
taking chest ownership or changing contents. Consuming materials additionally requires current
native ownership. Containers must be loaded, in range, not in use, and accessible to the player
and ward. These checks apply in solo, listen-server, and dedicated-server client sessions.

An exact process-local lease removes the selected material stacks before Valheim creates the output.
The lease commits after Valheim reports output or placement progress and rolls back if the action is
cancelled or throws. The lease blocks only another Runic Crafting material allocation in the same
process. Accessible unused chests can be claimed through Valheim's native ownership mechanism when
another player owns the surrounding zone; synchronized busy chests remain excluded. It never locks
unrelated inventory, tools, or other mods. There are no RPC sagas, persistent
journals, join-time recovery checks, or cross-mod transaction records.

Carried-only actions stay on Valheim's normal path. Special one-ingredient recipes and no-cost mode
also remain vanilla.

## Workshop Access

Station Use and Local Material Use are separate policies stored in the existing station ZDO keys:

- `everyone`
- `approved`
- `owner`
- `nobody`
- `ward`
- `ward.exceptions`
- `group`

Use `runiccrafting_access show` while standing at a station. The station owner may use:

```
runiccrafting_access station <policy>
runiccrafting_access materials <policy>
runiccrafting_access approve <playerId>
runiccrafting_access unapprove <playerId>
runiccrafting_access group <canonicalGroupUuid>
```

New configurations default both policies to `everyone`. Existing configuration values and saved
station restrictions are preserved. To open an existing workshop to shared materials, its creator
can use `runiccrafting_access materials everyone`. For stations without a saved override, set
`[Workshop Access] DefaultLocalMaterialUse = Everyone` in the mod configuration. Ward and personal
chest restrictions still apply. Anyone can inspect the nearby station's policy with `show`;
changing it still requires the station creator and current native ownership.

Group policy is an optional integration. When Runic Portals exposes the matching preserved group
record, membership is evaluated through its public reflection seam. If that provider is absent or
unavailable, a Group policy denies without preventing Crafting from loading.

## Building and UI

Hammer building searches for chests around the player. Station-required pieces use
`[Materials] RangeCapMeters`; the required station must still be nearby and permit access.
Explicitly allowed stationless pieces use their configured player-local range, capped by
`RangeCapMeters`. The preview, requirement rows, and placement use the same search origin and range.
Requirement rows show combined available/required
counts and put the carried, nearby, total, and missing breakdown in the tooltip.

Placement costs are committed only after Valheim creates the piece or reaches its native consumption
boundary. A rejected placement restores every earlier removal.

## Area repair

Press **`;` (semicolon)** to repair damaged, loaded hammer-buildable structures within **50 meters**:
walls, roofs, floors, fences, and other hammer pieces. No hammer needs to be equipped. This shortcut
does **not** repair tools, weapons, or armor, and does not consume equipment durability or stamina.
The existing inventory **Repair All** feature remains separate. Optionally enable **Repair → AutoRepairOnStationOpen** to repair all eligible worn equipment automatically after a successful station open; it is off by default. This uses the same native eligibility and owner checks as Repair All and needs no extra button.

To have a normal vanilla hammer repair start the same area operation, enable **Area Repair → OnHammerRepair**. It is off by default; the semicolon hotkey remains unchanged and still works. The completed vanilla repair is not repeated by the area queue.

In Configuration Manager (F1, if installed), open **Runic Crafting → Area Repair**:

- **Enabled**: turn area repair on or off. Off ignores the hotkey and cancels queued repairs.
- **Hotkey**: choose your preferred key or key combination; default `Semicolon`.
- **RadiusMeters**: choose **1–100 meters**; default `50`. This is separate from chest range.

The same settings are in `[Area Repair]` in `chazman.RunicCrafting.cfg`.
Repairs respect wards, personal-chest access, and the required station's coverage and workshop
permissions at each structure. They use native networked repair requests without claiming ownership.
Unloaded structures are not brought into memory. Work runs in small batches; stay near the starting
spot until it finishes. Moving more than two meters, changing the radius, leaving the session, or
disabling the feature cancels remaining work. The hotkey is ignored while typing or using menus.

## Installation

Install `RunicCrafting.dll` on each client that wants the features. Installing it on a dedicated
server is harmless and keeps the mod list uniform, but the material feature acts only for a locally
owned player and locally owned containers.

Configuration is in `BepInEx/config/chazman.RunicCrafting.cfg`. Changes apply at runtime.

For optional performance diagnostics, set `[Diagnostics] LogCacheStats = true` to log cache
hits, chest loads, and source-query time every five seconds. Leave it off for normal play.

## Credits

Thanks to **Megamos** and **Aedis** for their reports, testing, and optimization suggestions
that helped improve crafting and building performance.
