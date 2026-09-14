# RunicSigns

A simple editor for Valheim's wooden signs. Customize captions, colors, backgrounds and text size, then make the whole sign larger or smaller to suit your build.

RunicSigns works independently and can be used alongside RunicStorage. It requires only BepInEx.

## Use

Build or approach a normal wooden sign and press **Use**. The RunicSigns editor replaces the text prompt after Valheim approves the interaction.

- **Caption:** up to 256 characters, including line breaks. This limit stays the same at every sign size. Text is literal; formatting tags are not executed.
- **Sign size:** 0.25×–4× in 0.25 steps. The board, lettering and collision geometry scale together without stretching.
- **Text size:** 0.3×–3× independently of the board. Long text automatically shrinks to fit.
- **Color:** type a color name or cycle through RunicStorage's named palette. Hex and approximate names resolve to the nearest supported palette entry, which is shown in the editor.
- **Background:** transparent, white or black behind the caption. Transparent leaves the wooden board visible.
- **Alignment and bold:** left, center or right, with optional bold lettering.
- **Text position:** move the caption in 5% steps, up to 40% of its text area in each direction; Center resets the offset.
- **Save sign:** apply and save changes. **Cancel**, Escape or controller Cancel discards the draft. Movement, camera controls and gameplay shortcuts are blocked only while the editor is open. Version 1.0.2 fixes the persistent input lock caused by running alongside RunicStorage.

The preview shows text styling; inspect the placed sign after saving to judge its physical size and position. New and existing vanilla signs are supported. Modded sign prefabs are intentionally outside this release's scope.

## Install

Install **BepInExPack Valheim 5.4.2350** and put `RunicSigns.dll` under `BepInEx/plugins/RunicSigns` on **every player and the server**, using the same RunicSigns version. Restart those processes after installation. RunicStorage can remain installed.

BetterSigns must be disabled because both replace the same sign editor; BepInEx prevents RunicSigns from loading alongside it. Other mods that replace sign editing or change sign scale may also conflict.

## Multiplayer and saves

Each sign's appearance is saved with the world and synchronized between participating installations. Captions and authorship use Valheim's normal sign data and retain the game's text visibility and filtering behavior.

Stay within five metres of the sign and make sure you have ward access. If another player changes the sign while you are editing, your stale save is refused and your draft remains open. Copy your caption before reopening the sign. Cancelled or timed-out requests do not later apply the discarded draft.

All participants need the mod for consistent dimensions and save coordination. If saving repeatedly times out, check that every player and the server have the same RunicSigns version installed.

World saving follows normal Valheim save timing. Uninstalling retains the vanilla signs and captions; custom appearance stops being applied. RunicSigns fields remain available if the mod is reinstalled. Literal tags in a caption may be interpreted by vanilla after uninstalling.

## Release notes

Version **1.0.3** fixes the text-position arrows moving captions off the board. Each nudge now accounts for the text object's scale and rotation. Existing saved offsets use the corrected placement automatically; use **Center** to reset a sign's text position.

Version **1.0.2** fixes the input-hook conflict with RunicStorage that could prevent movement and camera control after closing an editor. In-game operation was confirmed by the author following this fix.

Automated checks cover sign settings, simulated multiplayer saves, compatibility with the installed game APIs, and input recovery with both RunicStorage and RunicSigns loaded. Dedicated-server and broader multiplayer edge-case testing remain limited.

## Support

Report issues through [the Runic mods Discord](https://discord.gg/7HKHTCdFqY). Include your RunicSigns version, whether you are playing solo or on a server, and the relevant BepInEx log messages.
