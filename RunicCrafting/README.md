# Runic Crafting 1.1.7

This release improves how Runic mods share chest supplies. No extra Runic mod is required. Update the server/host and participating clients together for the multiplayer improvements.

Craft and build using materials in nearby chests, without hauling every stack into your backpack first. Runic Crafting also adds clearer material counts, workshop access settings, cooking and refueling from chests, Repair All, and area repair.

## Features

- Craft and build using carried materials first, then accessible nearby storage.
- See combined carried and nearby material counts, with a detailed breakdown on hover.
- Browse crafting and building menus with less repeated work near large storage areas, including Show All.
- Load cooking racks and ovens, and refuel supported fires, lights, ovens, and smelters from nearby chests.
- Choose who can use a workshop and its stored materials.
- Repair eligible tools, weapons, and armor with Repair All.
- Repair nearby structures with a configurable shortcut.

## Installation and settings

Requires BepInExPack Valheim. Install Runic Crafting on each player's client and on the dedicated server or game host for cooperative chest access. Solo and multiplayer are supported.

Settings are in `BepInEx/config/chazman.RunicCrafting.cfg`. You can also use Configuration Manager if installed. Changes apply without restarting.

The nearby material range defaults to **20 meters**. Adjust `[Materials] RangeCapMeters` to change the maximum range. Crafting searches around the station; hammer building searches around your character. Required crafting stations must still be within reach.

Pieces that normally need no station use the settings under `[Stationless Building]`. You can enable the feature separately, adjust its range, and choose which pieces may use nearby materials.

## Materials and multiplayer

Materials are consumed normally, and carried supplies take priority. Nearby chests must be loaded, within range, and accessible to you. Chests currently in use are excluded, and personal-chest and applicable ward restrictions still apply.

Browsing a menu does not remove materials. Crafting and placement check the available supplies again before spending them. If an operation fails, the mod restores materials when it can do so safely. If it cannot confirm what happened, it stops further use of the affected supplies for that session. Check the item counts and the log before continuing; restarting alone does not repair a failed transfer.

The displayed material counts can change when another player uses the same supplies. Having enough materials does not bypass station requirements, workshop permissions, or inventory capacity.

## Cooking and refueling

Use the object's normal **Use** button. If you are not carrying a suitable item, the mod fetches one accepted ingredient or fuel item from a nearby chest.

- Cooking racks and ovens still need a free slot and any required fire. Finished food is collected first.
- Fires and supported lights use their usual fuel, such as wood or resin.
- Keep backpack space for one fetched item. If the object declines it, the item may remain in your backpack.
- Smelter ore loading and handheld torches are not included.

Under `[Manual Interactions]`, use `CookFromContainers`, `RefuelFromContainers`, and `RangeMeters` to configure these features. Both default on. These are manual interactions; Runic Production's automation is separate.

## Workshop access

Station use and permission to draw nearby materials can be configured independently. Both default to `everyone` for new configurations; existing restrictions are preserved.

Stand at a station and use `runiccrafting_access show` to inspect its settings. The station creator can use:

```text
runiccrafting_access station <policy>
runiccrafting_access materials <policy>
runiccrafting_access approve <playerId>
runiccrafting_access unapprove <playerId>
runiccrafting_access group <groupId>
```

Available policies: `everyone`, `approved`, `owner`, `nobody`, `ward`, `ward.exceptions`, and `group`.

To allow shared supplies at an existing workshop, use `runiccrafting_access materials everyone`. For stations without a saved override, change `[Workshop Access] DefaultLocalMaterialUse`.

Group access requires the optional Runic Portals group integration. If group membership cannot be checked, group access is denied. Ward and personal-chest restrictions still apply.

## Area repair

Press **`;` (semicolon)** to repair damaged, loaded hammer-built structures within **50 meters**. No hammer needs to be equipped. This does not repair inventory equipment or consume equipment durability or stamina.

Under `[Area Repair]`:

- `Enabled`: turn area repair on or off.
- `Hotkey`: change the shortcut; the default is `Semicolon`.
- `RadiusMeters`: choose 1-100 meters. This is separate from the material search range.

Repairs respect access restrictions and required crafting stations. Stay near your starting position while the repair finishes. Moving more than two meters, changing the radius, leaving the session, or disabling the feature cancels remaining repairs. The shortcut is ignored while typing or using menus.

## Modded containers and ItemDrawers

Ordinary modded chests that use Valheim's standard inventories are supported automatically. Makail's original ItemDrawers is also supported and enabled by default:

```ini
[Modded Containers]
PullPrefabIds = piece_drawer
```

This is Runic Crafting's own setting, independent of Runic Storage. If upgrading from 1.1.2 with a customized pull list in Runic Storage, copy that list into Runic Crafting's settings.

Assign an item to a drawer in ItemDrawers before using it. Use exact prefab names separated by commas or semicolons; an empty list disables custom-container support. Adding a name does not make an otherwise unsupported storage mod compatible. Other drawer implementations, including KGvalheim's, are not covered by this integration.

Runic Storage is only needed if you also want its QuickStack and Restock features.

## Troubleshooting

For support, visit the [Runic Mods Discord](https://discord.gg/7HKHTCdFqY).

For performance reports, enable `[Diagnostics] LogCacheStats` and optionally `DetailedLogging`, reproduce the issue, and share your complete `BepInEx/LogOutput.log`. Leave these settings off during normal play.

If you installed a separate Runic Crafting Lag Fix plugin, disable it when using this release so its patches do not overlap the built-in changes.

## Credits

Thank you to **Megamos** and **Aedis** for reports, testing, and earlier optimization suggestions, and to **Phoenixf** for profiling and reporting the crafting-completion and Show All performance issues.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicCrafting` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
