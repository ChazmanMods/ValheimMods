# ArcaneNaturalWaters 0.9.18

Build rivers, branches, cascades and natural hot springs. ArcaneNaturalWaters is standalone and requires only BepInEx and Jotunn.

Plumbing is now provided by the independent **ArcaneDecor: FlowCraft** mod. Neither mod requires the other.

Rivers borrow Valheim's ocean normal and foam textures for a flowing surface with downstream waves, refraction, reflections and clear shallows. Wave height varies with slope and fades at the shoreline. Existing saved sections receive the updated water when they load.

## River surfaces and boats

Connected sections now share their water-height, flow and surface-normal model. Each triangle at a shared join is drawn once, so steep continuations do not retain separate overlapping end caps. The same joined heights are used for terrain shaping, floating objects and placement previews. Saved river sections remain separate streamed objects.

Nearby overlapping rivers share their surface across different slopes and starting heights. Their banks follow the joined surface, and fixed rock colliders cut openings in the water mesh. Rivers and ArcaneNaturalWaters hot springs use the same outdoor water appearance at their connection.

Loading or editing a section queues the affected river meshes and spreads their construction across frames. Pond changes refresh nearby connections. River notifications no longer trigger a second rebuild of every loaded river.

Steep river bends blend between adjacent reaches, and sloping end caps continue downhill instead of forming flat shelves. River ripples are softer while retaining downstream motion. Boat placement previews recognize river water; normal building costs, permissions, clearance and sufficient channel width/depth still apply.

## Natural hot springs

Hammer → **Arcane Natural Waters** now has **Round, Oval, Kidney and Wild Hot Springs**. Each costs **30 Stone, 2 Surtling Cores and 5 Crystal**. Pools are naturally heated, never consume fuel, and provide the native hot tub's warmth, warm-and-cozy bathing area and 2 comfort.

Use the pool's control stone to open its ArcaneNaturalWaters menu. Pool width is 4–24 m and depth is 0.8–3 m. Individual submerged **white round stones bundled with ArcaneNaturalWaters** provide native sitting interactions, facing into the pool. Seat count grows with perimeter, from 4 to at most 24; stand up before resizing. Stone positions follow the bed contour at the selected depth.

**Bubbling sounds and music** offers five loop choices: Quiet bubbles, Mineral spring, Gentle simmer, Hidden grotto and Deep relaxation. The first three use the three supplied hot-spring recordings; the final two blend them. Adjust bubbling volume, speed and audible distance. Choose any of **AW_HSMusic_001–006**, with a separate music volume. Full stereo songs stream on demand; only the selected song is loaded. Pool music replaces the local Valheim music while inside the pool and restores it when leaving. Select **Valheim music** to retain normal music. The game's music volume remains respected.

**Warded pool** keeps non-player creatures, including tame pets, out of the pool's radius plus a 4 m margin. It supplies native monster avoidance, rejects normal spawn points inside, and moves any locally owned creature that crosses the boundary back outside without damaging it. Special or mod-created spawns are expelled after appearing. Players are unaffected. The ward works while the pool is loaded.

**Pool ambience** offers fireflies, night-only ambience, seven embedded ambient recordings and waterfall audio controls. Pool menus and settings belong to ArcaneNaturalWaters.

Hot springs use ArcaneNaturalWaters' own pool implementation and native hot-tub particles, with capped particle counts and one masked water trigger per pool. Native Valheim fonts, controls and cursor handling retain the existing menu appearance.

**Install ArcaneNaturalWaters 0.9.18 with BepInEx and Jotunn on clients and the server.** No WaterGardens DLL, prefab or installation is required. The DLL is `ArcaneNaturalWaters.dll`. Remove the previous `ArcaneWaterWorks.dll` before installing. Hot-spring prefab IDs, pool settings keys and existing river routes remain readable; endpoint edits use format 5.

## Build and guide

- Hammer → Arcane Natural Waters → River Source: 20 Stone, 5 Ancient Bark, 1 Surtling Core; no workbench.
- Press E on its stones and select the section for hoe tools. Hoe → Landscaping → River Trench extends it, 1–32 m per stroke.
- There is no fixed total river-length cap. The hoe automatically creates another independently saved and streamed section when needed. Keep working along the river. Small control stones identify sections.
- River Start selects a point on any loaded section for branching, depth changes or undo. Width, depth and automatic-route replacement affect the selected section. Deepen/Shallower affect its bed without lowering the water.
- Removing a section's control stone removes that section and restores its underlying terrain. Connected sections remain independently editable/removable. Continuation stones do not refund source ingredients.
- Automatic downhill generation creates a local section up to 96 m. This is a per-operation choice, not a cap on rivers extended with the hoe. Existing sections up to 192 m remain readable.

## River sound, whitewater and muddy bed

Press E on a section's control stone, then **River sound, whitewater and muddy bed**. Leave **River sound** on **Automatic by width**, or choose **Brook**, **Mid-Size River**, or **Large River** manually; set **River volume**, and save. Automatic selects Brook below 4 m, Mid-Size from 4 m to under 8 m, and Large at 8-12 m. Zero mutes the section. All three use distinct embedded recordings; defaults are substantially quieter than 0.3.x. The local BepInEx Audio / Volume setting and any River Ambience Stone's river volume multiply this section volume. The three recordings are the supplied AW_Brook_001.mp3, AW_FlowingRiver_001.mp3 and AW_LargeRiver_001.mp3, converted to mono with a half-second loop crossfade and restrained loudness.

**River whitewater** controls subtle crest foam within the moving surface, increasing with slope and shallow contact. Zero removes this added foam while waves and downstream ripples keep moving. This is a surface and buoyancy model, not a fluid simulation.

**Native dirt riverbed** uses Valheim's own terrain dirt texture, lighting and biome blending, with a soft transition at the bank. It adds no separate brown mesh or custom ground material. Turn it off to retain the underlying terrain paint. Existing river settings remain compatible. New continuation sections inherit sound and appearance choices. Existing pre-0.4 sections migrate to 50% section volume and automatic sound by width.

## Smoothing banks and joins

Select the section with E or **River Start**, then use **Hoe > Landscaping > Smooth River Banks** on its bank. Each stroke softens an 8 m radius around the aimed point, widens the transition into surrounding terrain (up to 6 m), and lowers the raised lip while preserving water height and the deep channel. Work along a merge; select and smooth each adjoining section. This rounds the generated bank profile rather than flattening the entire river or excavating unrelated terrain. Extremely steep or very deep narrow channels may also need a wider channel.

**Undo Bank Smoothing** reverses the latest smoothing stroke in that section. Up to 64 local strokes are saved per section; strokes within 1 m of an existing stroke are ignored. Normal **Undo River** still removes route segments. Removing the section restores its underlying terrain and restores its terrain paint. Ward checks cover the expanded bank footprint.

Only terrain/route edits refresh the affected terrain tiles. Volume and sound-profile changes do not rebuild terrain or geometry. Native dirt paint adds no render meshes; the moving surface uses no foam particles. There is still one box water trigger per section and no river mesh colliders.

## River ambience

Place **Hammer → Arcane Natural Waters → River Ambience Stone** beside the channel (4 Stone). Press E to adjust river sound volume and distance, seven ambience tracks, playback speed, firefly count and night-only ambience. The menu includes a sound preview.

The stone controls 10 m along the connected river in each direction from its nearest point, following bends and adjoining sections. At a branch it covers channel points within 10 m along the graph. Where areas overlap, the most recently saved settings win. Moving or replacing a river updates nearby stone coverage.

The new stone is an ArcaneNaturalWaters piece with its own prefab ID. Older **River Garden Stones** belong to WaterGardens and are not converted or registered by ArcaneNaturalWaters; use the new River Ambience Stone for standalone control. WaterGardens decorations and their controls stay in WaterGardens. This release does not share pool groups or decoration settings between the two mods.

## Outlets and terrain

Guide a river into a lake, ocean or an elevated ArcaneNaturalWaters hot spring. At sea level, the custom river mesh ends and native water takes over; the approach follows native wave height. The outlet's submerged terrain remains open. Joined river sections share surface ownership to avoid overlapping planes.

ArcaneNaturalWaters hot springs supply their actual water height, and the river approach meets that level. Pond animation continues across the connection. Water height follows the saved river route; width and depth shape its channel in the terrain.

Channels are 2–12 m wide, with 0.4–8 m excavation depth. Routing requires loaded, unprotected terrain. Each independently loaded section has one box water trigger; the river stone adds no water colliders. Floating decoration placement uses an analytic ray intersection, not more surface mesh colliders. Terrain work occurs on edits/load changes, not continuously.

## Install

Install the same ArcaneNaturalWaters version on the server and every client. The only required mods are:

- BepInExPack Valheim 5.4.2350 or later.
- Jotunn 2.30.1 or later.

Remove the old `ArcaneWaterWorks.dll` and install `ArcaneNaturalWaters.dll`; avoid duplicate copies under renamed plugin folders. ArcaneNaturalWaters 0.9.16 contains its own pool/menu code and all required custom pool assets. WaterGardens is independently installed and is not needed to build or run ArcaneNaturalWaters.

Existing ArcaneNaturalWaters hot springs and river routes retain their prefab IDs and settings keys. Versions 1 through 4 of saved river data remain readable; edits use version 5. Keep a world backup when upgrading or removing mods. Removing WaterGardens removes access to its own saved pieces; it is not necessary to remove it to use standalone ArcaneNaturalWaters.



### River tools in 0.9.11

Use **River Start** at a junction or endpoint, then **River Trench** toward the desired destination. Starting from an existing junction creates a separate branch section with its own width, depth, sound and whitewater settings. Use **Configure River** while aiming along that branch to open its settings. Older branches saved within one section retain their shared width/depth/appearance; their endpoints are independently editable.

With **River Trench** selected, hold **Alt + mouse wheel** to shrink or enlarge the maximum advance in 4 m steps, from **4 to 64 metres** (default 32 m). The projected circle shows reach from the selected branch start. The separate sizing button has been removed. Channel width remains adjustable from 2-12 m in Configure River. Terrain targeting still uses the normal hoe placement checks.

Use **End River Branch** at a tip to close only that endpoint, and **Reopen River End** to resume a manually ended branch. Natural-water outlets stay connected; select another tip or an upstream junction to continue elsewhere. All-open-tip selection prefers the most recently created open tip; when every tip is closed it selects the source for a new branch.

Existing rivers receive the surface and foam corrections below when they reload or rebuild.


### Flowing river surface in 0.9.16

Water uses a temporary profile constrained by the lower bank, with the same profile used for the excavated bed and boat buoyancy. This replaces the stretched outer water skirt that looked like water draped over a hillside. Existing saved routes are not rewritten. Lake and ocean connections keep their receiving-water elevation.

The river surface now moves: downstream waves displace its geometry and advect the ocean's normal textures. Reflections and refraction change with those waves; buoyancy uses the same wave formula and synchronized clock. Speed increases on slopes, and waves fade into banks and receiving waters. Small crest foam is part of the water shader. The previous drifting boat-wake patches have been removed; boats retain their own native wake.

River surfaces respect Valheim's native boat hull masks. Bank-constrained height profiles spread short terrace drops into longer descents, and round the transitions while keeping the original route saved. At river mouths, native waves taper at the bank and the two water surfaces crossfade over the submerged bed.

Boat shoreline stability in 0.9.16: ship buoyancy probes continue the river surface beneath nearby banks, avoiding sudden drops to ocean level at the channel boundary. Native collision and water-impact damage remain enabled. Install the same version on the server and all clients; the client owning a boat runs its physics.

Hot springs in 0.9.17 use depth-clipped water at rock and snowy-bank intersections, with repaired shoreline mesh tangents. Existing pools receive the rendering correction when reloaded. Rocks remain solid, and pool settings, liquid height and terrain rules are unchanged.
