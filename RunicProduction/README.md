# Runic Production 2.4.0

Link stations to chests for ingredients, fuel, finished goods, and stock replenishment.

## Link a station

Point at the station, use the gesture below, then point at the chest and repeat it within 30 seconds. Hold Shift during both gestures to unlink.

| Gesture | Link |
|---|---|
| Alt + left mouse on ingredient control | Input |
| Alt + left mouse on fuel control | Fuel input |
| Alt + right mouse | Output |
| Alt + middle mouse | Replenishment |

Use multiple chests for supported roles. One station's output chest can supply another station.

## Replenishment

Put an example item into a replenishment chest to set a supported stock target. Adjust reserves and production settings to suit your base.

## Supported work

Smelters, cooking stations, ovens, supported recipe stations, and fermenters use their available input, fuel, output, and replenishment roles. Beehives, sap extractors and wisp fountains support output; refillable fires and lamps support fuel. Stations use their normal recipes, processing times, and environmental conditions.

Stations retain their normal manual controls.

Configure settings in `BepInEx/config/chazman.RunicProduction.cfg`.

## Installation

Install matching 2.4.0 versions on the server and participating clients. Each Runic module is independently installable. Keep existing configurations and RunicOperations data when updating.

## Languages and support

The mod follows Valheim's selected language. Keep the included `Translations` folder beside the DLL when installing manually.

[Runic Mods Discord](https://discord.gg/7HKHTCdFqY) Â· [Support My Work](https://buymeacoffee.com/the_artful_engineer)


## Sap extractors and wisp fountains

Sap extractors and wisp fountains accept Output links. Aim at the structure and press Alt+Right Mouse, then use the same gesture on a chest. Add Shift at both steps to unlink.

Extractors keep Valheim's normal root connection, extraction speed and root depletion/recovery. Fountains keep normal night, cover, spawn chance and nearby-wisp limits. Automation collects wisps after they reach the fountain.

Output follows the linked chest settings and world drop modifiers. Manual collection remains available.
