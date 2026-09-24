# Shared label emojis

Both mods compile these internal classes and embed `atlas.png` and the graphics license. They remain independently installable. No global TMP font or sprite settings are changed. A dedicated server never creates the GPU atlas.

The picker contains 64 single Unicode scalars in four groups. Existing caption strings and save formats remain unchanged. Existing limits count UTF-16 units (two per bundled emoji); insertion refuses overflow and repairs partial pairs after TMP input processing. Flags, skin tones and joined sequences are outside the supported set. TMP handles optional variation selectors on pasted supported characters.

## Artwork attribution

Twemoji graphics copyright Twitter, Inc. and other contributors, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Source: [jdecked/twemoji](https://github.com/jdecked/twemoji). The exact source commit and icon order are recorded in `atlas.json`. The 72-pixel PNGs are arranged in an 8-by-8 atlas with transparent padding; the artwork itself is unchanged. The license is provided in `LICENSE-GRAPHICS.txt` and embedded in each DLL.

Run `python BuildAtlas.py` (Pillow required) to reproduce the atlas and generated catalog using the pinned commit. Network access is needed only for this developer build step. Normal .NET builds use the checked-in files.

## Verification

The two mod test suites exercise the shared Unicode selection/limit tests. Sign tests save an emoji caption through simulated ownership transfer and reload. Storage tests round-trip every catalog emoji through the production rules codec and reject malformed Unicode.

The author confirmed successful in-game testing of RunicStorage 1.3.0 and RunicSigns 1.1.0 with emojis on September 17, 2026.
