# Runic Precision Build Tool 2.0.9

Valheim's building system makes extraordinary structures possible, but serious builders eventually fight the placement controls: a beam is almost aligned, a roof needs one more axis, or one bad click means dismantling work that was already right.

**Runic Precision Build Tool gives the hammer full six-axis control.** Move and rotate pending pieces exactly, match existing geometry, repeat useful transforms, undo placements, and repair bounded areas while keeping Valheim's familiar building workflow.

## New in 2.0.9

The bottom panel places Precision on the left, the piece description in the middle, and Target on the right, all on the same level. Precision and Target share the same fitted font size. The shorter panel leaves more of your build visible, fits the screen width, and restores its original size when hidden.

## New in 2.0.8

Fixed compatibility with StartupAccelerator so precision building activates correctly when both mods are installed.

Tap **F4** for Local rotation axes that follow your piece; tap again for fixed World axes. Your choice stays selected, and the HUD and axis guides show the active mode.

## Major features

- Move and rotate pending pieces across all six degrees of freedom.
- Tap **F4** to switch between **Local** and **World** rotation axes; no holding or modifier keys.
- Switch between normal and fine movement or rotation steps.
- Match complete or individual transforms from existing pieces.
- Repeat placements, search the catalog, and inspect precise readouts.
- Undo eligible placements and repair a configurable bounded area.

## How it feels in-game

The structure in your head is no longer limited by the angle the vanilla preview happens to offer. Small corrections stay small, complicated geometry becomes repeatable, and mistakes cost a click instead of an afternoon.

## Building rules

Normal resource costs, workbench requirements, wards, build restrictions, and structural support still apply. The mod requires only BepInEx.

## Precision placement

Equip the Hammer, enter its build mode, select a build piece, and press P to enter precision mode
for the current placement session. P is ignored everywhere else, including ordinary gameplay and
other tools' placement modes. Press P again or leave Hammer placement to restore ordinary controls.
When precision mode is off, the mod does not change the placement transform.

| Default control | Action |
|---|---|
| Wheel / Alt + Wheel | Yaw / pitch |
| Shift + Wheel or Alt + Shift + Wheel | Roll |
| Add V to a wheel chord | Fine rotation |
| Alt + Left/Right | Sway |
| Alt + Up/Down | Heave |
| Alt + PageUp/PageDown | Surge |
| Add V to a movement chord | Fine movement |
| F4 | Toggle World / Local rotation axes (saved) |
| Hold G | Rotation axis guides |
| F10 | Reset the complete Runic transform |

Normal rotation is 22.5 degrees and fine rotation is 1 degree by default. **F4 toggles Local / World rotation** without holding a key or changing the current piece pose. Local is the default and follows the piece's X/Y/Z axes; World uses fixed world X/Y/Z axes. The HUD and G guides show the selected mode. The choice is saved as `Rotation.ReferenceFrame` and is independent of movement.

For an arch, aim at the preceding beam and press **Keypad0** to match its complete orientation. Select **LOCAL** with F4, then bend around the transverse axis shown by G. For the horizontal wood beam whose yellow X axis runs along its length, use **Shift + Wheel** (pink Z/roll) to bend; add **V** for one-degree steps. Snap the joining ends before placing. Alt + Wheel rotates about yellow X, which twists a beam running along X. The displayed pitch/roll/yaw are world orientation readings and may change together during a single local-axis turn.

Normal movement is 0.25 m and fine movement is 0.05 m.
`Movement.ReferenceFrame=World` keeps new movement steps on world axes; `Local` rotates each new
movement step by the pending piece orientation. Earlier movement never orbits when the piece rotates
later.

An untouched preview follows Valheim's candidate and surface orientation. An explicit Runic
rotation or rotation match holds the resulting world rotation. Valheim's ordinary yaw input still
composes from that held orientation.

## Match, repeat, search, and readout

| Default control | Action |
|---|---|
| Keypad0 | Match exact rotation |
| Keypad1/2/3 | Match pitch/roll/yaw |
| Keypad4/5/6 | Match world X/Y/Z position |
| Keypad7 | Match complete position |
| Keypad8 | Match position and rotation |
| Keypad9 | Match the last current-generation completed snap side |
| KeypadPeriod | Repeat the last two-placement relative transform |
| Shift + Keypad1-Keypad6 | Reset one rotation or movement axis |

Matching copies position and rotation. Piece ownership and normal building rules remain unchanged.

While the piece menu is open, F6 selects the next unlocked search result, F7 toggles a favorite,
F8 selects the next unlocked favorite, and F9 selects the next unlocked recent piece. When
`Build Catalog.SearchQuery` is empty, F6 cycles all currently unlocked pieces instead of showing an
error. Search and favorites use the pieces you have unlocked.

The selected-piece panel adds Runic world position, movement delta/reference frame, pitch/roll/yaw,
increment, target distance, snap host, and short match feedback. Its own rows hide while the piece
menu is open; Valheim retains its normal panel and lifecycle.

## Undo and area repair

F11 and F12 are available while precision mode is active and the piece menu is closed.

- **F11:** Undo the most recent eligible placement in the current Hammer session. The piece must still be yours, unchanged, undamaged, and not supporting another structure.
- **F12:** Repair nearby eligible pieces you created, up to the configured limit (16 by default). Normal Hammer range, durability, and build permissions apply.

In multiplayer, pieces currently controlled by another player or the server are skipped.

## Multiplayer and compatibility

Precision placement works with Valheim's normal multiplayer building. Runic Build Camera, Runic Crafting, and Runic Agriculture are optional companion mods. StartupAccelerator is supported. Disable PerfectPlacement's free rotation when using Runic Precision Build Tool.

## Installation

Install BepInExPack Valheim 5.4.2350, then place `RunicPrecisionBuildTool.dll` under
`BepInEx/plugins/RunicPrecisionBuildTool/`. Configuration is generated at
`BepInEx/config/chazman.RunicPrecisionBuildTool.cfg`.

Default controls use keyboard and mouse.

Community: https://discord.gg/7HKHTCdFqY

## Language files

This version follows Valheim's selected language using files in `Translations/RunicPrecisionBuildTool` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
