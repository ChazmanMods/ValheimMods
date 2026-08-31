# Runic Foundation icon sources

## Selected medallion set (2026-08-22)

The user requested four distinct Foundation icons matching the closed Nordic medallion style of
Runic Precision Build Tool and the selected Runic Suite 14 icon family. The former shared Foundation
icon is preserved at
`artifacts/RunicFoundationIconAlternatives/original/Foundation-shared-original.png` with SHA-256
`DB67FE011B15F35C088913BD02A2B07C5AE011C5F391E540059F551FE6FA94F9`.

All four selected release assets are exact 256x256 opaque RGBA PNGs with pure black corners.

| Foundation module | Meaning | Preserved generated source | Selected release/canonical asset | SHA-256 |
|---|---|---|---|---|
| Runic Core | central service and capability hub | `artifacts/RunicFoundationIconAlternatives/sources/Core-v3.png` | `RunicCore/icon.png` | `63C37D84B730A81572CF19E362260EEFB8853B4EAB3A3B545F3B6D71ABAD0E4E` |
| Runic Permissions | guarded access and balanced authorization | `artifacts/RunicFoundationIconAlternatives/sources/Permissions-v2.png` | `RunicPermissions/icon.png` | `EACDD55557A662CB4A3F98B74A0CA0125F10CAB88C1336F79FD0A4B8BD73B3CC` |
| Runic Transactions | atomic paired commit and durable claim | `artifacts/RunicFoundationIconAlternatives/sources/Transactions-v2.png` | `RunicTransactions/icon.png` | `391E0570BC071FA15DE7B0867296F8710D76CFA44BA3FACA441B11F0E2A7768C` |
| Runic Persistence | durable layered records and safe migration | `artifacts/RunicFoundationIconAlternatives/sources/Persistence-v3.png` | `RunicPersistence/icon.png` | `AE0040E7584DC2340CA94C5370B0112E2013F83100A666003B683A111CAC32D9` |

The four-up review sheet is
`artifacts/RunicFoundationIconAlternatives/Foundation-medallions-contact-sheet.png`, SHA-256
`8BC8EDE637B1A892BF8353E503F95FB8C11B72F5BF20E111A36D001FC3B8CD0B`.

## Generation method

Each icon used one built-in `image_gen` call. The 1254x1254 generated outputs were preserved as
sources. Each release image was mechanically resampled to 256x256 and composited onto opaque black;
no semantic redraw or additional image-generation call was used during export.

### Runic Core exact prompt

```text
Use case: style-transfer
Asset type: square Valheim mod UI icon, high-resolution source for a 256x256 thumbnail
Input images: Image 1 is the authoritative style and composition reference only. Match its closed circular medallion, straight-on camera, substantial segmented rim, tactile Nordic dark-fantasy materials, cyan-and-amber illumination, bold thumbnail silhouette, and polished painted 3D finish. Do not copy its hammer, crates, arrows, or construction subject.
Primary request: Create a new “Runic Core” icon communicating a central suite backbone, typed service registry, and several modules connecting through one trusted nexus, without any text.
Scene/backdrop: one closed round Nordic medallion, centered and fully contained; pure solid black everywhere outside the circular medallion.
Subject: one large central faceted rune-core crystal held inside a compact iron-and-stone socket. Exactly four short, heavy radial conduits connect that core to exactly four small, clearly separated socket nodes at the cardinal directions. The central core must dominate; the nodes are secondary. A restrained cyan energy circuit visibly links all four nodes through the central core, with tiny amber status accents.
Style/medium: tactile stylized 3D painted game UI icon matching Image 1; hand-crafted Nordic dark fantasy, not flat vector art and not photorealistic.
Composition/framing: near-orthographic straight-on view; centered radial symmetry; same medallion scale and rim weight as Image 1; full outer circle visible with modest breathing room; large simple shapes readable at 256px.
Lighting/mood: deep charcoal interior, luminous cyan primary glow, restrained warm amber secondary light, controlled highlights, dependable and foundational mood.
Color palette: charcoal iron, aged brown oak, cold carved stone, luminous cyan, very limited amber-gold.
Materials/textures: hammered dark iron, aged carved wood, chipped stone, rivets, soot, scratches, shallow abstract rune-like geometric marks that are not readable characters.
Constraints: exactly one central faceted core, exactly four radial conduits, exactly four small socket nodes, one complete closed medallion, opaque black exterior, no transparency, no text, no readable letters, no numbers, no logos, no watermark, no signature.
Avoid: hammer, anvil, construction blocks, portals, world globe, shields, scales, books, scrolls, open scenery, extra nodes, cropped circle, tilted perspective, excessive particles, thin fussy circuitry, neon wash, excessive orange or gold.
```

### Runic Permissions exact prompt

```text
Use case: stylized-concept
Asset type: square Valheim mod UI icon, designed to remain bold and legible at exactly 256x256 pixels
Input images: Image 1 is the authoritative style reference only. Match its closed circular medallion construction, front-facing camera, tactile depth, rim weight, material language, cyan-and-amber illumination, and overall polish. Do not copy its hammer, arrows, or building blocks.
Primary request: Create one new "Runic Permissions" icon that communicates fail-closed permissions, ownership, and guarded access without any text.
Scene/backdrop: one closed round Nordic medallion, centered and fully contained in frame; pure solid RGB black (#000000) everywhere outside the circular medallion.
Subject: a central weathered Nordic shield functioning as a heavy ward seal. On the shield face is a small, unmistakable, perfectly balanced two-pan scale motif. Integrate exactly two restrained access nodes, one on each side of the central shield, like secured ward locks; connect them subtly inward to the seal. The construction must feel locked, guarded, and fail-closed.
Style/medium: tactile stylized 3D painted game UI icon matching Image 1; Nordic dark-fantasy material realism; sculpted and hand-crafted, not flat vector art and not photorealistic.
Composition/framing: near-orthographic straight-on view; same medallion scale, circular silhouette, and substantial segmented rim as Image 1; centered radial balance; full outer circle visible with modest breathing room; strong readable shapes at thumbnail size.
Lighting/mood: dark forged interior; cyan runic light is the primary accent; restrained warm amber light is secondary; controlled highlights and glow that never obscure the forms; vigilant, secure mood.
Color palette: charcoal iron, aged brown oak, cold gray stone, luminous cyan, very limited amber.
Materials/textures: weathered dark iron, aged carved wood, rough stone inset, hammered metal, scratches, soot, chipped edges, tiny engraved runic-like marks that are abstract and not readable characters.
Text: none.
Constraints: one single complete circular medallion only; pure opaque black outside the circle; exactly one central shield, exactly one balanced two-pan scale motif, exactly two small access nodes; preserve a bold silhouette and clear focal hierarchy for 256px use; no transparency; no readable letters, words, numbers, logos, watermark, or signature.
Avoid: hammer, anvil, construction blocks, directional arrows, open doors, open gates, keys, padlocks dominating the design, extra nodes, extra shields, excessive gold, excessive orange, bright background, scenery, cropped circle, tilted perspective, clutter, thin fragile details.
```

### Runic Transactions exact prompt

```text
Use case: stylized-concept
Asset type: square Valheim mod game UI icon, designed to remain bold and legible at 256 x 256 pixels
Primary request: Create a distinct "Runic Transactions" medallion icon that communicates atomic all-or-nothing transactions and durable resource claims without any text.
Scene/backdrop: One complete closed circular medallion centered on the square canvas; the area outside the medallion must be uniform pure black (#000000), with no scenery, haze, particles, or border beyond the circle.
Subject: At dead center, a heavy forged-iron locking clasp mechanically joins two identical opposing resource channels / compact chest-like wooden blocks, one on the left and one on the right. The clasp is visibly closed and secure. Surrounding and connecting the paired channels is exactly one complete, unbroken circular exchange path: a clean cyan runic-energy arc balanced with a restrained amber arc, meeting seamlessly as a single closed loop. The entire mechanism is perfectly mirrored left-to-right and balanced top-to-bottom. The locked center plus continuous loop should read as "both sides commit together, or neither does" and "the claim persists," not as ordinary commerce.
Style/medium: Tactile stylized 3D, hand-painted fantasy game icon. Match the established Runic Precision Build Tool family: a thick closed wood/iron/stone medallion, chunky forged construction, carved relief, crisp beveled edges, rivets, subtle non-linguistic rune-like scoring, and polished readable forms. Do not copy its hammer or build-tool subject.
Composition/framing: Straight-on orthographic emblem view, centered with exact balanced symmetry. The round medallion fills about 94% of the square, with a bold circular silhouette and no cropped rim. Central iron clasp is the clearest focal point; identical resource blocks sit at 9 and 3 o'clock; the single exchange loop is unmistakably continuous and uncluttered. Favor a few large shapes and thick glowing strokes that survive reduction to 256 pixels.
Lighting/mood: Dramatic cool edge lighting and recessed cyan glow, restrained warm amber highlights, deep controlled shadows, authoritative and durable rather than magical-chaotic.
Color palette: charcoal forged iron, dark slate stone, aged walnut wood, vivid electric cyan, small restrained amber-gold accents, pure black outside the circle.
Materials/textures: hammered iron with wear on bevels, carved dark stone, visible warm wood grain, inset luminous energy channels; high-relief tactile depth.
Text: none.
Constraints: exactly two opposing resource blocks/channels; exactly one central closed iron locking clasp; exactly one unbroken circular cyan-and-amber exchange path; exact bilateral balance; complete closed medallion rim; pure opaque black outside the circle; high contrast; no readable letters, words, numbers, logos, signatures, or watermark.
Avoid: extra loops, multiple orbit rings, loose arrows, coins, currency symbols, hands, scales, scrolls, padlocks floating separately, weapons, tools, buildings, characters, asymmetry, open/broken rim, transparent/checkerboard background, glow spilling into the black outer corners, tiny decorative clutter.
```

### Runic Persistence exact prompt

```text
Use case: style-transfer
Asset type: square Valheim mod UI icon, high-resolution source for a 256x256 thumbnail
Input images: Image 1 is the authoritative style and composition reference only. Match its closed circular medallion, straight-on camera, substantial segmented rim, tactile Nordic dark-fantasy materials, cyan-and-amber illumination, bold thumbnail silhouette, and polished painted 3D finish. Do not copy its hammer, crates, arrows, or construction subject.
Primary request: Create a new “Runic Persistence” icon communicating durable versioned records, atomic migration, and preserved history without any text.
Scene/backdrop: one closed round Nordic medallion, centered and fully contained; pure solid black everywhere outside the circular medallion.
Subject: one central heavy stone archive tablet sealed into an iron vault cradle. Behind it, exactly two offset stone record layers are visibly stacked, making three total broad layers. A single thick cyan continuity ring wraps around the archive and passes through exactly two restrained amber checkpoint clasps, one above and one below. The front tablet bears one simple abstract branching rune-shaped groove with no readable character.
Style/medium: tactile stylized 3D painted game UI icon matching Image 1; hand-crafted Nordic dark fantasy, not flat vector art and not photorealistic.
Composition/framing: near-orthographic straight-on view; centered vertical symmetry; same medallion scale and rim weight as Image 1; full outer circle visible with modest breathing room; broad simple forms readable at 256px.
Lighting/mood: deep charcoal interior, luminous cyan primary glow, restrained warm amber secondary light, controlled highlights; durable, archival, protected, and calm.
Color palette: charcoal iron, aged brown oak, cold carved stone, luminous cyan, very limited amber-gold.
Materials/textures: hammered dark iron, aged carved wood, layered chipped slate, rivets, soot, scratches, shallow abstract rune-like geometric marks that are not readable letters.
Constraints: exactly three stacked archive-tablet layers, exactly one front tablet, exactly one continuous cyan ring, exactly two amber checkpoint clasps, one complete closed medallion, opaque black exterior, no transparency, no text, no readable letters, no numbers, no logos, no watermark, no signature.
Avoid: hammer, anvil, construction blocks, portals, world globe, shield, scales, books with paper pages, scrolls, hourglass, loose files, open scenery, extra tablets, cropped circle, tilted perspective, excessive particles, thin fussy details, neon wash, excessive orange or gold.
```
