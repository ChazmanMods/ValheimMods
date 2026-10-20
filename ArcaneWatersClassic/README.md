# ArcaneWaters 0.6.7

This release keeps the river appearance and boat support from 0.6.1 and fixes River Trench continuation. Aim farther ahead and repeat the stroke: each action advances up to 32 metres toward your aim, creating connected sections automatically. Sections also continue when their saved point capacity is reached. There is no total river-length cap. Building permissions, loaded terrain and downhill routing still apply.


Build rivers, branches and cascades, and decorate them using ArcaneDecor: WaterGardens.

Rivers now use animated ocean ripple textures that flow downstream through bends, with softer reflections and sparse foam on wider rivers. Their surface fades into the bank and receiving water. A low terrain shoulder covers the extended outer water edge, tapering into the surrounding ground over four metres. Saved rivers receive the new surface and banks when their section loads; outlets and connected pools remain open.

## River surfaces and boats

Steep river bends blend between adjacent reaches, and sloping end caps continue downhill instead of forming flat shelves. River ripples are softer while retaining downstream motion. Boat placement previews recognize river water; normal building costs, permissions, clearance and sufficient channel width/depth still apply.

## Natural hot springs

Hammer → **Arcane Waters** now has **Round, Oval, Kidney and Wild Hot Springs**. Each costs **30 Stone, 2 Surtling Cores and 5 Crystal**. Pools are naturally heated, never consume fuel, and provide the native hot tub's warmth, warm-and-cozy bathing area and 2 comfort.

Use the pool's control stone to open its ArcaneWaters menu. Pool width is 4–24 m and depth is 0.8–3 m. Individual submerged **WaterGardens white round stones** provide native sitting interactions, facing into the pool. Seat count grows with perimeter, from 4 to at most 24; stand up before resizing. Stone positions follow the bed contour at the selected depth.

**Bubbling sounds and music** offers five loop choices: Quiet bubbles, Mineral spring, Gentle simmer, Hidden grotto and Deep relaxation. The first three use the three supplied hot-spring recordings; the final two blend them. Adjust bubbling volume, speed and audible distance. Choose any of **AW_HSMusic_001–006**, with a separate music volume. Full stereo songs stream on demand; only the selected song is loaded. Pool music replaces the local Valheim music while inside the pool and restores it when leaving. Select **Valheim music** to retain normal music. The game's music volume remains respected.

**Warded pool** keeps non-player creatures, including tame pets, out of the pool's radius plus a 4 m margin. It supplies native monster avoidance, rejects normal spawn points inside, and moves any locally owned creature that crosses the boundary back outside without damaging it. Special or mod-created spawns are expelled after appearing. Players are unaffected. The ward works while the pool is loaded.

**WaterGardens ambience and decorations** retains fireflies, night-only ambience, the seven existing garden sounds, flower scale, floating lantern/fountain/statue controls and waterfall audio controls. Place WaterGardens pieces manually from their existing category; no decorative build pieces are duplicated in ArcaneWaters. Connected pools share the normal WaterGardens decoration settings.

Hot springs use the existing WaterGardens water surface and native hot-tub particles, with capped particle counts and one masked water trigger per pool. Existing WaterGardens menus keep their style. Both mods now keep Valheim's cursor visible while their menus are open.

**Install ArcaneWaters 0.6.1 together with ArcaneDecor: WaterGardens 1.1.3 on clients and the server before building these new pieces.** The internal ArcaneWaters filename remains `ArcaneWaterWorks.dll` to preserve compatibility with existing saves. Audio is embedded and extracted automatically into the BepInEx cache as needed.

## Build and guide

- Hammer → Arcane Waters → Arcane Spring: 20 Stone, 5 Ancient Bark, 1 Surtling Core; no workbench.
- Press E on its stones and select the section for hoe tools. Hoe → Landscaping → River Trench extends it, 1–32 m per stroke.
- There is no fixed total river-length cap. The hoe automatically creates another independently saved and streamed section when needed. Keep working along the river. Small control stones identify sections.
- River Start selects a point on any loaded section for branching, depth changes or undo. Width, depth and automatic-route replacement affect the selected section. Deepen/Shallower affect its bed without lowering the water.
- Removing a section's control stone removes that section and restores its underlying terrain. Connected sections remain independently editable/removable. Continuation stones do not refund source ingredients.
- Automatic downhill generation creates a local section up to 96 m. This is a per-operation choice, not a cap on rivers extended with the hoe. Existing sections up to 192 m remain readable.

## River sound, whitewater and muddy bed

Press E on a section's control stone, then **River sound, whitewater and muddy bed**. Leave **River sound** on **Automatic by width**, or choose **Brook**, **Mid-Size River**, or **Large River** manually; set **River volume**, and save. Automatic selects Brook below 4 m, Mid-Size from 4 m to under 8 m, and Large at 8-12 m. Zero mutes the section. All three use distinct embedded recordings; defaults are substantially quieter than 0.3.x. The local BepInEx Audio / Volume setting and any River Garden Stone's river volume multiply this section volume. The three recordings are the supplied AW_Brook_001.mp3, AW_FlowingRiver_001.mp3 and AW_LargeRiver_001.mp3, converted to mono with a half-second loop crossfade and restrained loudness.

**Wide-river whitewater** controls additional animated foam, increasing above 6 m width to full strength at 12 m. It fades out at marked lake/ocean mouths. It is a surface effect, not simulated rapids or additional collision geometry.

**Native dirt riverbed** uses Valheim's own terrain dirt texture, lighting and biome blending, with a soft transition at the bank. It adds no separate brown mesh or custom ground material. Turn it off to retain the underlying terrain paint. Existing river settings remain compatible. New continuation sections inherit sound and appearance choices. Existing pre-0.4 sections migrate to 50% section volume and automatic sound by width.

## Smoothing banks and joins

Select the section with E or **River Start**, then use **Hoe > Landscaping > Smooth River Banks** on its bank. Each stroke softens an 8 m radius around the aimed point, widens the transition into surrounding terrain (up to 6 m), and lowers the raised lip while preserving water height and the deep channel. Work along a merge; select and smooth each adjoining section. This rounds the generated bank profile rather than flattening the entire river or excavating unrelated terrain. Extremely steep or very deep narrow channels may also need a wider channel.

**Undo Bank Smoothing** reverses the latest smoothing stroke in that section. Up to 64 local strokes are saved per section; strokes within 1 m of an existing stroke are ignored. Normal **Undo River** still removes route segments. Removing the section restores its underlying terrain and restores its terrain paint. Ward checks cover the expanded bank footprint.

Only terrain/route edits refresh the affected terrain tiles. Volume and sound-profile changes do not rebuild terrain or geometry. Native dirt paint adds no render meshes; the current uses at most 64 small native foam particles per visible section. There is still one box water trigger per section and no river mesh colliders.

## Garden controls and decorations

Requires **ArcaneDecor: WaterGardens 1.1.3**.

Place **Hammer → Water Garden → River Garden Stone** beside the channel (4 Stone). Press E for the same menu controls used by WaterGardens:

- Flower/bud scale; floating or grounded lanterns, fountains and statues.
- River sound volume and distance.
- Seven ambience tracks, volume, playback speed, audible distance and a sound preview.
- Firefly count and night-only ambience.

The stone controls 10 m along the connected river in each direction from its nearest point, following bends and reaching adjoining sections. At a branch, it covers channel points within 10 m along the graph. It never spawns plants, statues or other build pieces. Place lily pads, water lilies, cattails, reeds, statues, lanterns and fountains manually from Water Garden. Surface pieces follow the water; planted pieces use the riverbed or banks. Each feature retains its own E menu for its individual light/flow controls.

Place more stones for additional stretches. Where stone areas overlap, the most recently saved settings win. Without a nearby river, the stone does not create water or a decorative pond. Moving/replacing a river updates its nearby stone coverage.

ArcaneWaters uses the original WaterGardens panel, fonts, sliders, buttons and toggles. WaterGardens styling is restored. The spring's menu also offers a shortcut to a nearby River Garden Stone.

## Outlets and terrain

Guide a river into a lake, ocean or an elevated WaterGardens pond. The route recognizes the receiving water level, extends underwater where terrain permits, and tapers its surface beneath that water. The outlet's submerged terrain is not raised into a closing bank. Matching-height river sections share surface ownership to avoid overlapping planes.

WaterGardens ponds supply their actual water height, rather than assuming every destination is at sea level. A lower river cannot flow uphill into a higher pond. Severe ocean waves can still reveal differences between moving ocean water and the river's steady surface; this is not a fluid simulation.

Channels are 2–12 m wide, with 0.4–8 m excavation depth. Routing requires loaded, unprotected terrain. Each independently loaded section has one box water trigger; the river stone adds no water colliders. Floating decoration placement uses an analytic ray intersection, not more surface mesh colliders. Terrain work occurs on edits/load changes, not continuously.

## Install together

Update **both mods on the server and every client**:

- ArcaneWaters 0.6.1 — `ArcaneWaterWorks.dll` (legacy filename retained).
- ArcaneDecor: WaterGardens 1.1.3 — `ArcaneDecorWaterGardens.dll`.

Replace existing DLLs; avoid duplicate copies under renamed plugin folders. Existing source IDs and configuration IDs remain unchanged. Versions 1, 2 and 3 of saved river data are read; edits use version 4 to preserve sound, appearance, bank strokes and multiple outlets. Older ArcaneWaters builds cannot read newly edited routes, so retain a world backup when upgrading.

