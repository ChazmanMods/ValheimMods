# Runic Build Camera

Move your view around a project while your Viking stays put. Place, repair, and remove building pieces from the angle you need, using Valheim's familiar building controls.

## Features

- Detached building camera with adjustable movement speed and range.
- Remote placement, repair, and removal using normal building requirements and costs.
- Optional pickup of loose materials near the camera, including underwater drops.
- Player ward protection for camera pickups.
- Optional Wisplight mist clearing that follows the camera.
- Mouse and controller support with separate look-inversion settings.

## Controls

Equip a hammer or another building tool and press **B** to toggle the camera. Close the piece-selection menu to move freely.

- **Movement and look:** your normal Valheim inputs.
- **Up and down:** jump and crouch.
- **Faster movement:** hold run.
- **Exit:** press B, put away your hammer, or select a hotbar item.

Your character stays in place and remains vulnerable to damage while you use the camera.

## Picking up items

Enable your character's auto-pickup toggle and leave `PickupEnabled` on. Eligible loose items are collected within `PickupRange` of the camera. Distance includes depth; move the camera closer to submerged items if needed.

Inventory space, carry weight, item eligibility, and other mods' normal pickup restrictions still apply. Fish are excluded, including anglerfish and pufferfish. The camera does not collect from chests or other containers, pick up placed objects, or free items trapped in tar.

**Player wards:** enabled player-built wards block camera pickup unless you own the ward or have permission. Every overlapping player ward must allow access. Disabled wards do not block pickup.

**Game-created wards:** do not block camera pickup. Building actions continue to follow their normal ward restrictions.

## Wisplight

When you have an active Wisplight, its mist-clearing effect can follow the detached camera. Adjust `DemisterRangeMultiplier` to change its range. The effect returns to your character when the camera closes.

## Installation

Install with a Thunderstore-compatible mod manager, or install BepInExPack for Valheim and copy `RunicBuildCamera.dll` and the `Translations` folder into `BepInEx/plugins/RunicBuildCamera`.

Close Valheim before updating. Replace the previous version instead of keeping duplicate copies. Launch once to generate `BepInEx/config/chazman.RunicBuildCamera.cfg`.

**Client-only:** each player who wants the camera installs it locally. The dedicated server does not need this mod. Runic Build Camera is standalone and requires only BepInEx; no other Runic mods are required.

## Compatibility

Do not use Runic Build Camera alongside Build Camera Custom Hammers Edition. Disable or remove that mod first. Runic Precision Build Tool can be used alongside this mod.

## Configuration

Edit `BepInEx/config/chazman.RunicBuildCamera.cfg` while the game is closed. A commented example configuration is included.

| Section | Setting | Default | Accepted range or meaning |
|---|---|---:|---|
| General | Enabled | true | Master client-side switch |
| Controls | ToggleShortcut | B | BepInEx keyboard shortcut |
| Camera | CameraRange | 60 | 1 to 100 metres |
| Camera | MoveSpeed | 10 | 0.5 to 50 metres per second |
| Camera | FastMoveMultiplier | 3 | 1 to 10 |
| Camera | WorldRelativeMovement | false | false uses camera-relative movement |
| Remote Actions | RemoteActionDistance | 100 | 1 to 100 metres from avatar to target; camera ray is 50 metres |
| Pickup | PickupEnabled | true | Enable loose-world-item pickup checks; containers are excluded |
| Pickup | PickupRange | 10 | 1 to 50 metres around the camera |
| Pickup | PickupIntervalSeconds | 0.25 | 0.05 to 2 seconds |
| Mist | DemisterFollowCamera | true | Move the player's active Wisplight effect with the camera |
| Mist | DemisterRangeMultiplier | 2 | 0.25 to 5 |
| Controls | InvertMouseHorizontal | false | Mouse horizontal look |
| Controls | InvertMouseVertical | false | Mouse vertical look |
| Controls | InvertControllerHorizontal | false | Controller horizontal look |
| Controls | InvertControllerVertical | false | Controller vertical look |
| Diagnostics | VerboseLogging | false | Additional transition logging, never per-frame logging |

## Languages

The mod follows Valheim's selected language, with English fallback. Language files are in `Translations/RunicBuildCamera` beside the DLL. To add a translation, copy `English.json`, name the copy for the selected language, and translate its values.

## Version 1.0.8

Camera pickups respect player-built wards while allowing collection near game-created wards. Owners and permitted players retain access. Fish remain excluded.

## Support

[Get help on Discord](https://discord.gg/7HKHTCdFqY)

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
