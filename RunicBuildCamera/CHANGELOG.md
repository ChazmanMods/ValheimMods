# Changelog

## 1.0.3 - 2026-09-11

- Fixed the detached camera blocking the hammer's hotbar unequip action and switching items.
- Fixed intermittent missed put-away input by releasing the camera before Valheim processes the same keypress.
- Controller hotbar use now exits the detached camera before using the selected item.
- Preserved Valheim's menu, chat, and controller radial input rules on Valheim 1.0.12.

## 1.0.2 - 2026-09-09

- Re-audited every camera, placement, pickup, and input contract against the installed Valheim 1.0.7 assemblies.
- Updated version-type handling for Valheim 1.0 and the BepInEx dependency to 5.4.2350.

## 1.0.1 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.0.0.

## 1.0.0

- Promote the completed standalone Runic Build Camera feature set to its first stable release.
- Preserve the bounded detached-camera, remote-action, loose-item pickup, and Wisplight behavior
  established in the initial preview without adding a Runic foundation dependency.

## 0.1.0

- Establish the standalone chazman.RunicBuildCamera plugin identity with no dependency on Runic
  Precision Build Tool or the Runic foundation packages.
- Add a detached camera for Valheim's normal building workflow, with configurable movement speed,
  fast movement, world-relative movement, and independent mouse and controller look inversion.
- Bound camera travel to 60 metres by default and 100 metres at the hard maximum.
- Support placement and removal targeting through the camera with a separate 100-metre default and
  hard maximum.
- Keep only build inputs live during a camera session so camera look/movement cannot trigger avatar
  use, hotbar, walk-toggle, auto-pickup-toggle, or body-look actions.
- Raise effective crafting-station range only inside the scoped remote call without changing any
  station field, while exact player reach is restored through success, exception, and unload.
- Add optional, interval-limited pickup checks for loose world items around the detached camera;
  chests and other containers are outside the feature.
- Add optional follow behavior for the player's existing Wisplight demister with a configurable
  range multiplier.
- Add concise diagnostics with opt-in verbose state-transition logging.
- Document that the avatar remains vulnerable, client-side limits are not server enforcement, and
  the implementation has an explicit clean-room boundary.
