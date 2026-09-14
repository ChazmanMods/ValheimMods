# Runic Display Stands 1.3.8

Display stands make equipment look at home in your base, but the moment you want to use that equipment you are back to opening chests and moving every piece by hand.

**Runic Display Stands turns supported item and armor stands into functional storage.** Store items through familiar inventory controls and use an armor stand as a ready-made loadout station.

## Major features

- Open supported item and armor stands through Valheim's container interface.
- Store any item on item stands and valid equipment in armor-stand roles.
- Swap a complete supported armor and weapon loadout with your character.
- Use quick Take all, Take one, and Use/equip actions.
- Preserve quality, durability, crafter data, variants, and modded custom data.

## How it feels in-game

The equipment displayed around your hall becomes part of how you play. Walk up to the stand prepared for sailing, mining, or battle, swap what you are wearing, and leave without assembling the loadout one inventory slot at a time.

## Safety and compatibility

Invalid equipment is rejected without changing either inventory. Transfers recheck stand access and contents, verify the saved result, and restore inventory items if the move fails. A swap that cannot fit the incoming gear is canceled instead of dropping equipment; available protected equipment cells can still receive replacements when ordinary inventory is full. Scripted stands such as Forsaken stones, offering holders, and quest props keep their original behavior. The host synchronizes the configured stand list through Valheim's routed networking, and the mod requires no Jotunn, ServerSync, or shared Runic library.

Items stored on a managed stand persist with the world in that stand's saved data and are removed when the stand is destroyed.

## Support and community

Need help, found a bug, or have an idea?

[Join the Chazman Mods Discord](https://discord.gg/TQadeq2nD)

- Report bugs and unexpected behavior.
- Ask installation, configuration, and compatibility questions.
- Request features and follow mod updates.
- When reporting a bug, include the mod version, Valheim version, reproduction steps,
  screenshots or errors, and your BepInEx `LogOutput.log` when available.
- Remove passwords, tokens, and other personal information before posting logs.

## Features

- Open item and armor stands through Valheim's native container interface.
- Item stands accept any item, one unit per stand. Split larger stacks before placing them.
- Hold the configured Take One key (Left Alt by default) while interacting to remove one item.
- Interact with an armor stand switch to open its nine named slots in two rows:
  Helmet, Chest, Legs, Cape, Utility; then Right hand, Left hand, Shield, Weapon.
  The four lower slots are centered beneath the five upper slots.
- Right hand and Left hand accept hand equipment for display/storage and stay on the stand during **Use/equip**.
- **Use/equip** swaps your worn armor with the corresponding stand slots and equips the incoming pieces. Cape swaps keep your worn cape when the stand has none, equip the stand cape when you are not wearing one, and exchange capes when both sides have one. Unequipped spare capes in your inventory are left alone.
- Weapon and Shield each swap only when both your inventory and that named stand slot contain one.
  Selection prefers equipped gear, then sheathed gear, then carried items in inventory order.
- Utility swaps when both sides have one. If only the stand has one, you receive and equip it.
  If only you have one, it stays with you and is equipped; an empty stand never strips your utility.
- Replacements reuse freed equipment slots, including RunicInventory's protected row.
  Empty protected equipment roles can receive incoming gear even when ordinary inventory slots are full.
- Drag items into their named slots; quick transfers choose a compatible empty slot.
  Incompatible items and duplicate occupancy are rejected without moving items.
- Use the panel's **Take all** and **Use/equip** buttons for quick stand actions.
- Additional incoming items need available space. Failed transfers or equips restore the original loadout.
- Existing stand attachments are assigned to the named roles by equipment type on opening. Saved roles
  preserve the difference between display-hand items and Weapon/Shield after reopening.
- To drop a displayed item on the ground, first take it into your inventory.
- Compact stand grids safely ignore out-of-range hover requests from inventory utility mods
  such as AzuAutoStore.
- Drag items between the player inventory and stand using the normal inventory controls.
- Preserves item quality, durability, crafter information, variants, and modded custom data
  such as enchantments while items are stored on either kind of stand.
- The host synchronizes the admin-controlled stand prefab list to modded clients using
  Valheim's built-in routed RPC system.
- Forsaken stones, boss-offering holders, quest props, and other location-scripted item
  stands retain their original interactions and never open as containers.

## Installation

Copy `RunicDisplayStands.dll` into:

`BepInEx/plugins/RunicDisplayStands/`

All players who interact with these stands should install the mod. The only declared package
dependency is BepInExPack Valheim 5.4.2350.

When updating manually, replace the existing DLL instead of keeping multiple copies, then restart
Valheim. Use the same release on clients that interact with the stands. Existing configuration and
saved attachments are retained; older attachments are assigned to the named slots by item type.

## Configuration

The generated config is:

`BepInEx/config/chazman.RunicDisplayStands.cfg`

- **Admin / Stand Prefabs**: comma-separated prefab names handled by the mod.
- **General / Take One Item Key**: modifier used for single-item take and armor container access.
- **General / Gamepad Support**: reserved controller-prompt preference.

## Building

The project defaults to:

`E:\SteamLibrary\steamapps\common\Valheim`

Override `VALHEIM_INSTALL` when building elsewhere:

```powershell
dotnet build -c Release -p:VALHEIM_INSTALL="C:\path\to\Valheim"
```

The project references the installed Valheim and BepInEx assemblies directly and has no NuGet
package dependencies.

## Compatibility

Version 1.3.8 targets Valheim 1.0.12 and retains the audited 1.0.7 API contracts. Game updates that change
`ItemStand`, `ArmorStand`, `Container`, or `Switch` internals may require a rebuild.
