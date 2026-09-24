# RunicSigns 1.2.7

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

## Multiplayer and saved designs

Install the same RunicSigns version on the server and participating clients. Normal sign ownership and ward permissions still apply. Save commits an existing sign design; Cancel restores its saved appearance.

## Compatibility

Outline/shadow use the font shader; bundled emoji sprites bend with the caption but retain their own artwork material. Very strong depth curves can intersect nearby scenery.

Existing version-1, version-2 and version-3 styles are readable. Saving a design writes version-4 style data. Earlier RunicSigns releases cannot display/edit that new styling; ordinary caption text remains in the native sign data. Placement drafts are session-local, not config templates.

Before downgrading or uninstalling, back up your world. Older releases cannot render the newer style metadata, although native caption text remains available.

## Artwork

Twemoji graphics copyright Twitter, Inc. and other contributors, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), from [jdecked/twemoji](https://github.com/jdecked/twemoji). Icons are arranged in a padded atlas without changing the artwork. The graphics license is embedded in the DLL.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicSigns` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support development

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
