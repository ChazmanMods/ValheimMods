# Runic Interaction 1.0.4

Not every frustration deserves a separate system. Some are simply small interruptions that happen often enough to wear you down: repeated use clicks, fussy transfers, doors left open, forgotten menu choices, unwanted pickups, and equipment that does not return when expected.

**Runic Interaction polishes those everyday moments.** Each improvement is independently configurable and keeps Valheim's installed action as the final gameplay operation.

## Major features

- Hold Use for supported repeated fuel and input interactions.
- Move a full stack with a convenient modifier gesture.
- Optionally close doors after a safe, obstruction-aware delay.
- Restore valid equipment and remember selected menu choices.
- Improve selected text handling and filter unwanted automatic pickups.

## How it feels in-game

Common actions require fewer repeated clicks and fewer corrections. Nothing announces itself as a new game system; the controls simply behave more like you expected them to in the first place.

## Safety and compatibility

Every feature can be disabled, and the master switch yields completely to vanilla behavior. Enhanced actions revalidate access, ownership, wards, obstructions, and current state before calling Valheim's native path. The mod has no required Runic dependency; optional integrations fail only the enhanced action when their evidence is unavailable.

## Features

### Hold repeat

Holding Use repeats supported fuel and input interactions at Valheim's installed 0.2-second cadence.
Each quantum calls the original station action, so native item, capacity, roof, fire, and other
station checks still decide whether one item is consumed.

### Full-stack transfer

Left Alt or Right Alt plus click moves one full stack through the existing InventoryGrid selection
callback. The controller chord is JoyLTrigger + JoyButtonX. A move requires the open container to be
the native local owner, current chest and ward access, a live non-quest item, and no active drag.

The optional Runic Inventory integration uses its public `InventoryIntegrationApi` when present.
Interaction asks that small reflected API whether the exact native item is protected. Missing Runic
Inventory preserves standalone behavior; an installed but incompatible or indeterminate provider
denies only the enhanced move. There is no hard Crafting, Storage, Inventory, or Foundation
dependency.

`DragTransfer` is an explicit disabled gate because Valheim 1.0.7 has no stable atomic drag-sweep
boundary. Ordinary dragging remains vanilla.

### Session-only door auto-close

Door auto-close is off by default. A successful open by the native local player creates one bounded
in-memory timer. At expiry, the same loaded door must still have the exact identity and prefab, be
open and closeable, pass current ward access, and have no character or movable-body obstruction.
The 32-collider non-alloc scan treats saturation as obstruction.

The close uses native Valheim ownership and the installed door transition. If needed, Valheim's
`ZNetView.ClaimOwnership()` is requested for that loaded door before the built-in door RPC is
invoked. The mod has no custom network RPC, remote-player identity protocol, replay receipt, or
server-side command transport. Timers are capped at 64, checked eight at a time, and expire within
70 seconds. Disconnect, unload, restart, stale identity, denial, or failure drops only that timer.

### Equipment, menus, and text

Equipment restore remembers only a still-legal previous hand item for the current session. It has
no enemy selection, tactical logic, or combat automation. Menu memory retains bounded
craft/upgrade selection context in memory and never unlocks a recipe.

Text polish validates portal, sign, and tame text immediately before the installed receiver commits
it. It enforces current object, permission, range, Unicode, control-character, and configured length
rules, then delegates to vanilla. It does not write a second world record.

### Pickup filters

Up to 128 exact prefab IDs or localized item tokens can be declined before item load, ownership,
inventory mutation, or world destruction. Quest items bypass filtering unless the dangerous opt-in
is enabled. Alt + Use or the configured controller modifier + JoyUse bypasses a match.
A denied drop remains where it was.

The controller modifier is chosen from 14 fixed installed ZInput actions. JoyButtonA and JoyButtonX
are excluded because they can collapse onto JoyUse in supported controller layouts. Invalid values
fall back to JoyRStick.

## Persistence and multiplayer boundaries

Interaction stores no world metadata and has no world migration. It creates no persistent operation,
journal, inventory lock, recovery loop, account state, or quarantine. All temporary door, equipment,
menu, hold, and filter state is bounded to the current process session. Failed actions cannot lock an
unrelated tool, inventory, door, or gameplay mod.

Install the DLL on each client that should receive the local conveniences. A dedicated server may
also load it safely; no protocol handshake is required, and client/server absence or version drift
does not prevent another Runic mod from loading. Auto-close acts only where that process has the
qualifying native local player and loaded door state.

## Configuration defaults

| Section | Setting | Default |
|---|---|---|
| General | Enabled | true |
| Features | HoldToRepeat | true |
| Features | TransferGestures | true |
| Features | DragTransfer | false |
| Features | AutoCloseDoors | false |
| Features | EquipmentRestore | true |
| Features | MenuMemory | true |
| Features | TextEntryPolish | true |
| Features | PickupFilters | true |
| Door Auto-Close | DelaySeconds | 4 |
| Door Auto-Close | RecentUseSafetySeconds | 1.5 |
| Door Auto-Close | ObstructionRadiusMeters | 0.9 |
| Text Entry | PortalCharacterLimit | 10 |
| Text Entry | SignCharacterLimit | 50 |
| Text Entry | TameCharacterLimit | 10 |
| Text Entry | CommitRangeMeters | 6 |
| Pickup Filter | Items | empty |
| Pickup Filter | AllowQuestItemFiltering | false |
| Controller | PickupBypassModifierAction | JoyRStick |
| Diagnostics | VerboseLogging | false |

Install BepInExPack Valheim 5.4.2350 and place `RunicInteraction.dll` under
`BepInEx/plugins/RunicInteraction/`. Remove the DLL and its configuration to uninstall.
No world cleanup is required.

Community: https://discord.gg/7HKHTCdFqY

Runic Interaction is an independent mod and is not affiliated with Iron Gate Studio.
