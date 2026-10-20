# RunicSigns 1.2.9

Create and edit sign designs directly in Valheim with live previews, styled text, emojis, effects, and precise placement controls. Requires BepInExPack Valheim 5.4.2350. Install one copy of RunicSigns; remove BetterSigns before installing.

## Getting started

1. Install RunicSigns through your mod manager, or import `Chazman-RunicSigns-1.2.6.zip` as a local mod. For manual installation, place RunicSigns.dll in BepInEx/plugins/RunicSigns.
2. Equip a hammer and select the wooden sign. Aim where you want it, then press **F8**. The sign preview stays in place while the designer is open.
3. Enter a caption. Select words to apply bold, italic, relative size, exact color or opacity to those words. Click **Text Color...** to see all 22 named colors with swatches; choosing one applies the proper formatting automatically. Selection stays captured while using the controls. **Select all** explicitly targets the full caption. With no selection, formatting changes the whole-caption defaults; individual word overrides are preserved. Click **RESET TEXT** to clear all word overrides and restore regular, white, fully opaque text at 1×, without changing the caption, layout, or effects. Color also accepts `#RRGGBB` and `#RRGGBBAA`.
4. Use **Symbols** for the 64 bundled emojis. Choose alignment, background (automatically sized around the rendered caption), and Fit text to sign or Free text size. **Free text size disables automatic wrapping** so larger captions extend horizontally beyond the board; Enter still adds an intentional line break. Select words for relative sizing, or leave nothing selected to change the base text size. Click **Effects…** for independent Outline and Shadow switches and two curve strengths: up/down and forward/backward. Use both switches and both axes together, or any subset. Each axis has **− / +** buttons and a numeric field. Each click changes by 1 in the new fine scale: **1 now equals the old 0.01 bend**. Start at 1 or −1; type decimals for smaller adjustments. The display range is −100 to +100, and 0 is flat. Existing saved signs retain their appearance; an old value of 0.01 now displays as 1. Positive values curve the center up or forward, negative values down or backward. Effects apply to the whole caption; ordinary formatting targets the selection, or the caption defaults when no text is selected.
5. Enter horizontal/vertical/depth offsets directly. **Depth** moves the whole caption away from or toward the sign: positive is Forward, negative is Back, along the text’s own facing direction. Back/Forward buttons use the fine/coarse step, and Center text resets all three offsets. The background follows depth movement. In sign-fraction mode, one depth unit equals the text-box width. **Text units** supports ±2000 and uses the same scaling as plain numeric TMP offset values. The alternate mode uses sign-width/height fractions, up to ±20. Fine/coarse arrows and Center are available.
6. Click **Use for placement**, resume aiming and place the sign normally. **[ / ]** still change whole-sign size outside the editor. The design remains available for subsequent placements during this game session.
7. Use an existing sign to open the same live designer. **Save sign** commits changes. **Cancel/Escape** restores the saved appearance. Escape closes an open effects, color or symbols picker first. Effects preview live; Done closes the effects panel, while Cancel in the main designer restores the saved design.

F8 and bracket shortcuts can be rebound in the RunicSigns BepInEx config under Placement. Text size ranges from 0.1× to 20×, whole-sign size from 0.25× to 4×. Captions allow 1024 UTF-16 units (two per bundled emoji) and 128 independently styled sections. No HTML/tag entry is needed; typed tags remain literal text.

Cyan and other exact colors now use a private neutral font-face material so the original sign tint does not multiply the chosen color.

## Vanilla-compatible signs (1.2.9 test build)

In the designer, click **Viewers: RunicSigns players** to switch to **Viewers: vanilla players (ON)**, then save or use the design for placement. Existing signs keep their original mode until you change it.

This mode writes built-in formatting tags into the ordinary synchronized sign caption. Only the designer needs RunicSigns; viewers and the server do not need it for these signs. This is a client authoring tool, not a server-only visual editor.

Vanilla players can see text colors/opacity, sizes, bold/italic, individual word styling, alignment, explicit line breaks, and horizontal/vertical positioning. Colors retain vanilla's font tint. Horizontal positioning uses native indentation and vertical positioning uses baseline offsets, so switching an existing design can change its layout; adjust it in the vanilla preview. Normal sign culling and font glyph availability still apply.

Board resizing, depth movement, background panels, outline/shadow effects, curves, and bundled emoji artwork are not applied in this mode. Those settings stay in the design for switching back; the board remains its ordinary size. Hide it behind scenery for floating portal labels. Text uses native layout; long captions can still wrap to the board width, and Enter adds a line break. Automatic Fit is unavailable. Vertical offsets keep line spacing fixed so the caption moves together.

Formatting counts toward a 4096-character saved-caption budget; the editor refuses an oversized export rather than truncating it. Vanilla's small text editor can truncate/rewrite tags if someone edits the sign without RunicSigns. Use RunicSigns to edit these designs. Normal ward access, authorship, filtering, and muted-player restrictions still apply. Vanilla saves use the game's ownership path, with a local stale-edit check; they do not provide the custom multiplayer edit lease used by full-feature signs.

Full-feature signs still require compatible RunicSigns installations on participating clients and the server. This test build passes automated checks but still requires a two-client in-game visual/multiplayer test before release.

## Compatibility

Outline/shadow use the font shader; bundled emoji sprites bend with the caption but retain their own artwork material. Very strong depth curves can intersect nearby scenery.

Existing version-1, version-2 and version-3 styles are readable. Full-feature designs continue to write version-4 style data; vanilla-compatible designs write version-5 editor metadata alongside the portable caption. Earlier RunicSigns releases cannot display/edit that new styling; ordinary caption text remains in the native sign data. Placement drafts are session-local, not config templates.

Before downgrading or uninstalling, back up your world. Older releases cannot render the newer style metadata, although native caption text remains available.

## Artwork

Twemoji graphics copyright Twitter, Inc. and other contributors, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), from [jdecked/twemoji](https://github.com/jdecked/twemoji). Icons are arranged in a padded atlas without changing the artwork. The graphics license is embedded in the DLL.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicSigns` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support development

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
