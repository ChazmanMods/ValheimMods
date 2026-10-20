# Better Signs

Better Signs is a modular signage system for Valheim. It enhances every vanilla sign with a
visual editor and adds purpose-built signs for storage, doors, mail, roads, shops, glass, and
large-format modular signboards.

## Version 0.2.0 prototype

- Replaces the vanilla text prompt after Valheim has approved sign editing.
- Font browser using TextMesh Pro fonts already loaded by the game and installed mods.
- Font size, color, alignment, bold, italic, and a curated symbol palette.
- Opaque high-contrast editor, centered responsive layout, live preview, and cursor capture.
- The symbol palette hides glyphs that the selected font cannot render.
- Uses Jötunn's native GUI input blocker so Valheim releases and restores the cursor cleanly.
- Filters out internal TMP assets that cannot render normal Latin text.
- Stores formatting in synchronized ZDO fields instead of embedding metadata in visible text.
- Save remains open and reports an error unless both the ZDO text and rendered text verify.
- Formatting travels through the vanilla synchronized sign text, avoiding separate world files.
- Adds box label, door plaque, mailbox, signpost, hanging, glass, modular, and wide modular signs.
- Modular panels have four edge snap points for assembling larger signboards.

All players on a multiplayer server should install the mod. Unicode characters only render when
the selected font contains their glyphs; a bundled fallback-font atlas is planned for the next
asset milestone.

## Support Chazman's Mods

If you enjoy my Valheim mods and want to support continued development, testing, multiplayer compatibility, and new projects, I would appreciate it.

Support is completely optional. Playing the mods, reporting bugs, sharing feedback, and recommending them to other players helps just as much.

Thanks for supporting Chazman's Mods and the continued development of RunicSuite, ArcaneDecor, and other projects!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
