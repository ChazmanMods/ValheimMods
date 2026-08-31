# Runic Crafting 1.0.0

Runic Crafting adds nearby-container crafting and building, Workshop Access policies, combined
carried/nearby requirement displays, stationless build allow rules, and Repair All.

The mod is independently installable. Its only package dependency is BepInEx; Runic Inventory,
Runic Storage, Runic Portals, and every former Foundation package are optional or unnecessary.

## Material ownership

Crafting considers the local player's carried inventory first, then eligible nearby containers in
stable distance and endpoint order. A container is eligible only while it is loaded, in range, not
in use, accessible to the player and ward, and owned by the current Valheim process. This uses the
same native ownership rule on solo games, listen servers, and dedicated-server clients. Containers
owned by another peer are ignored for that attempt.

An exact process-local lease removes the selected material stacks before Valheim creates the output.
The lease commits after Valheim reports output or placement progress and rolls back if the action is
cancelled or throws. The lease blocks only another Runic Crafting material allocation in the same
process; it never locks unrelated inventory, tools, or other mods. There are no RPC sagas, persistent
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

Group policy is an optional integration. When Runic Portals exposes the matching preserved group
record, membership is evaluated through its public reflection seam. If that provider is absent or
unavailable, a Group policy denies without preventing Crafting from loading.

## Building and UI

Station-gated pieces use the resolved station's native build range. Explicitly allowed stationless
pieces use Crafting's bounded player-local range. Requirement rows show combined available/required
counts and put the carried, nearby, total, and missing breakdown in the tooltip.

Placement costs are committed only after Valheim creates the piece or reaches its native consumption
boundary. A rejected placement restores every earlier removal.

## Installation

Install `RunicCrafting.dll` on each client that wants the features. Installing it on a dedicated
server is harmless and keeps the mod list uniform, but the material feature acts only for a locally
owned player and locally owned containers.

Configuration is in `BepInEx/config/chazman.RunicCrafting.cfg`. Changes apply at runtime.
