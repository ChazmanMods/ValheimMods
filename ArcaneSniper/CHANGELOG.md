# ArcaneSniper changelog

## 0.3.0 — validation candidate

- Added bounded world loading and creature activity around the flying camera while preserving the player's original streaming area and position.
- Added server-coordinated camera interest and ownership-area recognition using native object synchronization. No forced saves, inventory locks, fake players or player teleportation.
- Added camera presence to ordinary outdoor and dungeon spawners, preserving native spawn conditions, spawn history and limits. Nearby sleeping creatures can wake; raids and noise-only triggers retain their real-player behavior.
- Added E interaction at linked dungeon entrances/exits, including Frost Caves and Infested Mines, and Backspace camera return to the entrance. Dungeon altitude does not consume horizontal tether distance. Boss-exit restrictions apply.
- Restored dungeon shell visibility when a remote camera enters an interior environment volume.
- Added focused geometry/API checks and an isolated native-game harness. Multiplayer, reconnect, persistence and graphical responsiveness gates remain subject to the validation report.
- Retained the verified transparent medallion source icon and added mandatory packaged-icon alpha and byte-match gates to the build script.

## 0.2.0

- Added a bronze runic ArcaneSniper medallion icon with a teal scope reticle and moonlit stag; included as the package's 256x256 icon.png.

- Added hold-right-click scope zoom, starting at 4x; mouse wheel changes magnification, default maximum 20x, configurable up to 40x.
- Scaled scoped mouse sensitivity and restored normal FOV on release, menus and exit. Suppressed the overlapping vanilla right-click screenshot action.
- Required a fresh left-click for each shot. Hovering and holding the button do not fire automatically. Early clicks are not queued.
- Increased camera tether, firing range and pickup range defaults and limits to 1,609.344 meters (one mile). World streaming remains vanilla; only loaded targets and drops are available.
- Added independent built-in auto pickup with capacity, weight, toggle, ownership and item eligibility checks.
- Migrated previous default ranges while preserving custom settings.
- Release build and 322 automated logic/API checks passed. Live game and multiplayer testing remains pending.

## 0.1.0

- Initial flying camera, configurable reticle and bird-death sound.
