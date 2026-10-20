# Runic Crafting 2.3.2

Craft, upgrade, build, and supply supported stations using carried materials and nearby chests. Includes Repair All, area repair, material displays, and workshop access settings.

## Everyday use

- Craft and upgrade through the normal station menu. Carried ingredients are used first.
- Place hammer pieces using supplies from nearby accessible chests.
- Supply supported cooking and refueling interactions from nearby materials.
- Use Repair All and the station material display to manage your workshop.
- Press **semicolon** for the default area-repair shortcut.

Configure material range and other settings in `BepInEx/config/chazman.RunicCrafting.cfg`. The material range cap defaults to 20 metres. Runic reads current chest dimensions and item stack sizes from the live game.

## Workshop access

At a station, run `runiccrafting_access show` to view access settings. Its creator can set `station <policy>` and `materials <policy>`, approve player IDs, and assign a group. Policies include `everyone`, `approved`, `owner`, `nobody`, `ward`, `ward.exceptions`, and `group`. Group access integrates with Runic Portals.

## Installation

Install matching 2.3.2 versions on the server and participating clients. Each Runic module is independently installable. Keep existing configurations and RunicOperations data when updating.

## Languages and support

The mod follows Valheim's selected language. Keep the included `Translations` folder beside the DLL when installing manually.

[Runic Mods Discord](https://discord.gg/7HKHTCdFqY) · [Support My Work](https://buymeacoffee.com/the_artful_engineer)
