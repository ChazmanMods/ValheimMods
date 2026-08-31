# Changelog

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
