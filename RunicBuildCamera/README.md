# Runic Build Camera

Runic Build Camera is a standalone client-side Valheim mod that lets a player move a detached
camera while using the ordinary building workflow. Version 1.0.0 keeps the feature deliberately
bounded: the camera stays near the player, remote actions have a separate distance limit, and all
limits have finite configuration ranges.

The plugin GUID is chazman.RunicBuildCamera. It depends only on BepInEx, Harmony, and assemblies
shipped with Valheim. It does not depend on Runic Core, Runic Precision Build Tool, or another
Runic foundation package.

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

Version 1.0.0 introduces no custom RPC, synchronized configuration, or networked data type. Install
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
