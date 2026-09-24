# Runic Build Camera

The hardest part of an ambitious Valheim build is often getting your Viking—and the camera—into the one position where the next piece can be seen and placed. Roofs, tall walls, tight interiors, and awkward corners turn perspective into the real construction challenge.

**Runic Build Camera lets your view move around the project while your Viking stays put.** Fly to the useful angle and keep using Valheim's familiar building workflow from there.

## Major features

- Toggle a detached camera while building.
- Place, repair, and remove pieces from the remote view.
- Configure movement speed, range, inversion, and reference behavior.
- Optionally collect eligible loose materials near the camera.
- Optionally carry an equipped Wisplight's mist clearing with the view.

## How it feels in-game

You can inspect the far side of a wall, rise above a roofline, or reach a cramped detail without repeatedly rebuilding scaffolding or repositioning your character. The hammer still feels like the hammer; you simply get a better seat.

## Safety and compatibility

The camera is client-side and range-limited. Normal costs, placement validation, required station types, wards, and removal paths remain in control. Your Viking remains at the original position and stays vulnerable while the camera is away. The mod is standalone and requires only BepInEx and Valheim's shipped assemblies.

## Feature scope

- Enter or leave the detached build camera with a configurable shortcut. The default is B.
- Move and look from the detached camera while the player remains in the normal building state.
- Limit camera travel to 60 metres by default, with a non-configurable hard ceiling of 100 metres.
- Keep placement, repair, and removal targeting available from the camera. The separate 100-metre
  setting caps avatar-to-target distance; Valheim also limits the ray to 50 metres from the camera.
- Optionally collect eligible loose world-item drops near the camera on a bounded interval. This
  does not search, open, or transfer items from chests or other containers.
- When the player has an active Wisplight demister, optionally move that existing mist-clearing
  effect to the camera and apply a configurable range multiplier while the camera is active.
- Configure camera speed, a fast-move multiplier, view-relative or world-relative movement, and
  independent horizontal and vertical inversion for mouse and controller look.
- Restore camera-related state when the mode exits instead of leaving a detached effect behind.

Runic Build Camera does not add free materials, bypass recipe costs, replace ward checks, or define
a custom building protocol. Placement and removal still travel through Valheim's normal gameplay
paths. During those scoped calls only, loaded workbench, forge, and other build-station ranges are
treated as at least the configured remote-action distance; their stored range values are never
changed. The required station type and the piece's other ordinary rules still apply. The avatar
remains at the original world position and remains vulnerable to creatures, damage, weather, and
other ordinary gameplay while the camera is detached.

## Controls

Press B while building to toggle the camera with the default configuration. Normal movement and
look inputs drive the detached camera. The usual jump and crouch inputs move vertically, and the
usual run input applies the fast-move multiplier. Remapped Valheim inputs remain the source of
those actions.

To put away your hammer or switch items, use the normal hotbar action; the detached camera
exits automatically. Keyboard put-away also exits the camera and keeps the same keypress.
Controller hotbar use follows the same exit behavior. Compatible with Valheim 1.0.14.

The toggle can be replaced with any BepInEx keyboard shortcut in
BepInEx/config/chazman.RunicBuildCamera.cfg.

## Configuration

The hard ceilings on camera range and remote action distance are part of the plugin, not merely
suggested defaults. BepInEx clamps those two settings to 100 metres.

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

RunicBuildCamera.cfg.example contains the complete commented configuration.

## Compatibility

Do not install Runic Build Camera alongside Build Camera Custom Hammers Edition. If that plugin is
detected, Runic Build Camera deliberately disables its detached mode and writes one actionable log
warning so two camera runtimes cannot fight over the same transform. Remove or disable the older
Build Camera package before testing this one. Runic Precision Build Tool is not a conflict: it may
remain installed and continues to own only its precision pose controls.

## Installation

1. Install BepInExPack for Valheim.
2. Copy RunicBuildCamera.dll into BepInEx/plugins/RunicBuildCamera.
3. Start Valheim once to generate BepInEx/config/chazman.RunicBuildCamera.cfg.
4. Close the game before editing configuration values.

The manifest, README, changelog, and example configuration may be kept beside the DLL but are not
loaded by the game.

## Multiplayer

Version 1.0.5 introduces no custom RPC, synchronized configuration, or networked data type. Install
it on each client that wants to use the camera; a dedicated server does not need a matching copy
for the client feature to operate. Installing this DLL on a dedicated server does not turn the
client-side camera and range settings into an enforcement system.

Camera position, look inversion, pickup scanning, and mist-effect movement are local concerns.
Successful placement, removal, inventory changes, and world ownership still use Valheim's existing
network behavior. However, an unmodified dedicated server does not independently attest the
camera's origin or enforce Runic Build Camera's configured 100-metre caps. Those caps are local
safety limits, not a server trust boundary. Server operators who require authoritative distance or
camera-policy enforcement need a separate server-side policy implementation. This mod is not an
anti-cheat or permission system.

## Clean-room provenance and reuse

Runic Build Camera is an independently authored plugin built from a clean behavior specification.
No source code, binaries, documentation text, artwork, or other assets from Build Camera Custom
Hammers Edition are included.

The upstream Build Camera Custom Hammers Edition repository publishes its implementation under
GPL-3.0. Copying or adapting that implementation would require satisfying the GPL terms; observing
public behavior does not grant permission to copy protected implementation details. The official
upstream repository and license are:

- https://github.com/AzumattDev/Build_Camera_Custom_Hammers_Edition
- https://github.com/AzumattDev/Build_Camera_Custom_Hammers_Edition/blob/github/LICENSE

Runic Build Camera does not currently include a LICENSE file, so this repository does not state a
general third-party reuse grant for its own code. Add an explicit project license before inviting
reuse or redistribution of source.

## Version 1.0.5 update

Restores activation when a stale focus state would otherwise block the shortcut. The camera now shows activation and blocked-shortcut feedback and logs camera-update failures. Confirmed working in-game by the author on September 17, 2026.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicBuildCamera` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
