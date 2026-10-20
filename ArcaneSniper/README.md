# ArcaneSniper 0.2.0

Fly a detached camera, aim at a creature, and left-click to kill it with the original Valheim bird-death sound (`crow_splatt`, obtained from `sfx_crow_death` at runtime). Camera travel, firing and built-in auto pickup default to one mile (1,609.344 meters).

Version 0.3.0 adds an active area around the camera: terrain and creatures load there, normal spawners recognize camera presence, and nearby sleeping creatures can wake. Your body stays in place, vulnerable, with its original area loaded. Newly visited terrain still needs time to generate and objects to arrive; activating an unloaded area cannot be instantaneous.

Fly within three meters of a Frost Cave, Infested Mine or another linked dungeon passage and press **E** to move the camera through it. Use **E** at the interior exit to leave, **Backspace** to return to the entrance you used, or **F9/Esc** to end camera mode. Passage keys are configurable. Only the camera travels; it does not teleport your character, discover a location for the character, unlock doors, or reset a cleared dungeon. Normal boss-exit restrictions apply to passage and return controls. Your horizontal camera range remains measured from your body; remote interiors have a separate vertical origin.

For multiplayer distant activation, install the same version on the **server and each client using ArcaneSniper**. Camera activity is an additional interest area; it does not move the player's network reference or force saves. Other players retain normal concurrent interaction and existing ownership is respected. Without server support, distant activation and remote dungeon passage are unavailable; the camera still operates over normally loaded objects.

## Install and controls

Copy `plugins/ArcaneSniper/ArcaneSniper.dll` into your profile's `BepInEx/plugins/ArcaneSniper` folder. Requires BepInExPack Valheim. No Jotunn or ArcaneHunter dependency. Restart the game.

- **F9:** enter/exit sniper mode. **Escape:** exit.
- **Mouse:** aim. **WASD:** fly. **Space/Ctrl:** up/down. **Shift:** boost.
- **Left mouse click:** fire one shot. Aiming alone never fires; holding the button does not repeat. There is a 0.3-second entry delay.
- **Hold right mouse button:** scope zoom. **Scroll wheel while scoped:** increase/decrease magnification in 1x steps. Starts at 4x, up to 20x by default. Release to restore normal view. Left-click fires while scoped. The reticle stays centered and mouse sensitivity scales down with zoom.
- Players are excluded. Tamed creatures are excluded by default; the config can enable them.
- Your body remains where you started and remains vulnerable. Camera exits on death, teleport or logout. Menus/chat and loss of focus suspend targeting and flight.

## Configuration

The first launch creates `BepInEx/config/chazman.ArcaneSniper.cfg`. Edit with the game closed, or use a BepInEx-compatible configuration manager.

| Section | Setting | Default |
|---|---|---|
| General | ToggleKey | F9 |
| Camera | Speed / SprintMultiplier | 15 m/s / 3x |
| Camera | MaxDistanceFromPlayer | 1,609.344 m (5–1,609.344) |
| Camera | MouseSensitivity / InvertY | 2 / false |
| Targeting | Range | 1,609.344 m (1–1,609.344) |
| Targeting | AimDelay / Cooldown | 0 / 0.15 seconds |
| Targeting | KillTamedCreatures | false |
| Reticle | Style | CrossAndDot (also Cross, Dot, Circle) |
| Reticle | Size / Gap / Thickness | 24 / 5 / 2 pixels |
| Reticle | Color / KillColor | teal / red RGBA |
| Audio | Volume | 0.8 |
| Auto Pickup | Enabled / Range | true / 1,609.344 m (2–1,609.344) |
| Scope | Enabled / ZoomKey | true / Mouse1 (hold right mouse) |
| Scope | Magnification / MaximumMagnification | 4x / 20x (configurable to 40x) |
| Scope | SensitivityMultiplier | 1 (divided by magnification) |

Solid scenery blocks aiming. Distant activation loads a bounded area around the camera and retains the normal area around your body; it does not load the entire one-mile corridor between them. Shots require loaded collision geometry. The player's world-streaming reference is unchanged. The camera's far clipping plane is extended during sniper mode and restored on exit. Set `[World] EnableDistantActivation = false` to retain the previous streaming behavior.

Camera areas load two neighboring rings of zones and activate one neighboring ring. New terrain is requested incrementally, with at most one additional successfully generated zone per 0.1-second update across camera areas. Normal spawn conditions, maximum counts, one-time dungeon spawns, ownership and respawn timers remain in use. Camera presence does not trigger raids or fabricate player noise. Creatures do not treat the camera as an attackable player; their actual combat targets remain governed by Valheim. Moving camera states are coalesced to at most four updates per second, with a one-second idle heartbeat; expired or disconnected remote camera activity is discarded. No shots or dungeon interactions are queued for later execution.

Damage uses the normal game network-owner path and preserves normal loot/death handling. Protected/cutscene creatures and mods that reject damage may prevent a kill. A successful death flashes the reticle and plays the bird sound locally through the game's sound-effects mixer. Birds also retain their natural death effect.

Auto pickup is built into ArcaneSniper and requires no ArcaneHunter installation. Eligible loaded loose loot transfers directly to inventory, across terrain, within the configured radius of the camera while active or your body otherwise. It honors the game's auto-pickup toggle, inventory slots, carry weight, unique-item restrictions, placed items, tar and network ownership. It does not harvest plants, empty containers or loot tombstones. Scans run in batches four times per second. Set `[Auto Pickup] Enabled = false` to disable it.

Upgrading preserves cosmetic settings and custom ranges. The old default camera tether of 100 meters and firing range of 150 meters migrate to one mile on the first 0.2.0 launch. A nonzero AimDelay requires steady aim before clicking; an early click is discarded, never queued.

This is an intentionally overpowered utility. Valheim may mark its high-damage kills as cheated; ArcaneHunter journal rewards may exclude them. If ArcaneHunter is also installed, each pickup feature has its own enable setting; disable either one if you want a single pickup range.

Scope zoom changes optical field of view, not world loading or firing distance. Zoom resets to the configured starting magnification each time you enter sniper mode. Menus suspend scope zoom; exit restores the normal camera view. Vanilla free-camera right-click screenshots are suppressed while right-click is assigned to the scope; F11 still works normally.

Turn off other build/free-camera modes before entering ArcaneSniper. The mod refuses entry when vanilla free fly is already active. Other mods that replace the camera may conflict.

## Validation

Built against the installed Valheim assemblies. Automated checks cover camera-area geometry, dungeon altitude/range, malformed focus coordinates, click-only firing, no held-button repeat, discarded early clicks, aim delay, target switching, cooldowns, pickup range/filters/capacity/ownership/batching, patched method signatures and production game API references. See `VALIDATION-0.3.0.md` for the actual runtime evidence and outstanding release gates. The isolated runtime harness in `Tests/Runtime` creates a disposable native world with saving disabled; it does not constitute joined-client or reconnect testing. Do not treat a successful build or harness run as a multiplayer release verdict.
