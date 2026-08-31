# Runic Inventory 1.0.0

Runic Inventory is a standalone BepInEx gameplay mod with no Foundation runtime dependency. On
Valheim's native 8×4 player inventory, it reserves the bottom row only after proving an exact
eight-wide topology:

| x | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| role | Helmet | Chest | Legs | Cape | Utility | Quick 1 | Quick 2 | Quick 3 |

That is five equipment roles and three quick roles. Unknown or modded categories remain ordinary
inventory items. While the inventory is open, the native cells are subtly outlined and labeled.
There is no gameplay HUD outside the inventory; the diagnostic status panel remains optional.

Hold Left Alt and right-click any player-inventory slot to lock it; repeat the same gesture to
unlock it. Lock changes are silent: a bright yellow outline is the confirmation and disappears on
unlock. The configured Alt+L shortcut remains available as an optional focused-slot fallback.
Role labels and lock outlines are anchored to Valheim's exact native cells, while the complete
bottom role row remains subtly outlined. Split-stack, variant, skills, and text dialogs always draw
above these overlays. A transient equip/drag state no longer permanently disables that row: as soon
as the native inventory is valid again, the labels, borders, equipment relocation, and protection
proof recover.

Picking up or crafting recognized armor, cape, or utility gear fills an empty exact role. Manually
equipping a replacement moves it to the role and swaps the previous occupant into the replacement's
original native slot. If exact membership and positions cannot be proved, no relocation occurs.

## Native ownership and persistence

Inventory does not add carrying capacity or maintain a second item store. Every item stays in the
same vanilla `Inventory` with full item metadata: prefab, stack, durability, equipped state, quality,
variant, crafter identity, custom data, world level, pickup state, weight, and effects.

- Valheim `Inventory.Save` and `Inventory.Load` remain the item persistence path.
- Death and tombstones remain Valheim's native grave-transfer path.
- Topology and lock metadata remains one small checksummed entry in the owning player's custom data.
- An abrupt uninstall leaves bottom-row items as ordinary vanilla items.
- Sort, lock changes, quick use, and relocation require the exact owning `Player.m_localPlayer`.

Mutations use one short-lived mutation scope owned by this mod and perform exact in-memory rollback
when a position proof fails. There is no global lock, cross-mod transaction coordinator, durable
journal, quarantine, or recovery protocol.

## Optional integration

`RunicInventory.Api.InventoryIntegrationApi.TryGetProtection(object, out int)` is a tiny optional
reflection seam for independently installed mods. It returns state `0` for governed-but-unknown,
`1` for proven unlocked, and `2` for locked. A false return means the supplied object is outside
Inventory's domain. Consumers must treat unknown as fail-closed and must not retain the native item.

Crafting, Storage, and Interaction may use this optional reflection API when Inventory is installed;
none is a hard dependency in either direction. Inventory loads and functions on its own.
The normal workstation repair call receives a narrowly scoped unlocked view, so equipped armor in
the protected bottom row can still be repaired; protection resumes as soon as that repair call ends.

## Authority modes

- `AuthoritativeLocal`: exact owning local player; gameplay features may mutate through native APIs.
- `RemoteDedicatedCompatibility`: a non-local or non-owning player reference is read-only.
- `MigrationSafeCompatibility`: topology or item evidence is incompatible, so special mutations stop.
- `Disabled` / `BatchInert`: ordinary vanilla behavior.

A connected dedicated-server client owns its character inventory and can use Inventory normally. The
headless server process has no local player, so this mod remains inert there.

Install only BepInEx and Runic Inventory. Existing 1.0.0 configuration and topology metadata remain
valid; no data migration or version bump is required.
