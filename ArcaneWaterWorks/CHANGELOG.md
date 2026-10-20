## 0.9.18
- Replace the River Source and four hot-spring hammer icons with supplied artwork for each shape.
- Rename Arcane Spring to River Source in the hammer menu, hover text and hoe guidance. Existing placed rivers retain their prefab identity and saved data.

## 0.9.17
- Repair invalid shoreline tangents after extending hot-spring water meshes.
- Give hot springs depth-clipped water and guarded refraction at terrain, snow and rock intersections, retaining calm ripples and level liquid height.
- Apply the corrected appearance to placement previews and rebuilt/connected pools. Existing pools update on reload; saved settings, terrain rules and rocks are unchanged.

## 0.9.16
- Fixed river-bank discontinuities in the five water-height probes used by native boat physics. Boat probes now continue the river surface beneath nearby banks instead of abruptly dropping to ocean level or no water.
- Preserve native collision and water-impact damage, actual river and pond levels, swimming boundaries, visuals, and saved routes. The correction is restricted to Ship physics.

## 0.9.15
- Honor Valheim's native boat water-mask stencil so river water is excluded from hull interiors.
- Smooth the unsaved bank-constrained profile to spread terrace drops upstream and round segment transitions, preserving bank ceilings, downhill flow, saved routes and receiving-water levels.
- Anchored the ocean-wave contribution at river banks so receiving-water waves do not lift shoreline vertices into a floating edge. Liquid-height queries use the same taper.
- Added opaque-depth shoreline clipping and soft intersections; refraction no longer samples foreground banks or objects across the boundary.
- Render river water after native water and crossfade over submerged receiving beds, avoiding hard material boundaries and intersecting wave triangles at the ocean mouth.

## 0.9.14
- Removed the River Trench Size hoe button. Alt + mouse wheel now adjusts trench reach from 4 to 64 metres in 4 metre steps, with a projected reach circle.
- Sizing consumes the wheel to avoid simultaneous zoom or rotation, and is inactive in menus or other tools. Configure River and branch endpoint tools remain available.

## 0.9.13
- Replaced drifting river foam patches with a moving water surface: downstream displaced waves, ocean normal textures, refraction and reflection.
- Wave speed and amplitude respond to slope; shoreline, shallows and receiving-water transitions taper displacement. Buoyancy uses the same wave formula and synchronized clock.
- Whitewater now controls small crest highlights within the shader. Zero whitewater still leaves a moving river; no surface foam sprites are spawned.
- Preserved bank-constrained profiles, reversible terrain, branch endpoints and hoe sizing.
## 0.9.12
- Removed the untextured bathtub droplet material from river currents and splashes. Surface foam now uses the native Karve stationary water-surface effect; splashes use the native textured boat splash.
- Foam follows downstream paths, with strength and speed varying by slope, shallows and solid-obstacle contact. Whitewater zero removes the added foam and spray.
- Removed the water apron that draped over hillsides. A temporary lower-bank-constrained profile drives the surface, terrain excavation and buoyancy together; saved routes remain intact and receiving-water levels are preserved.
- Kept branch and hoe mechanics from 0.9.11.

## 0.9.11
- Exposed river surface aprons descend to planned terrain instead of ending as floating sheets. Bank buildup remains capped and terrain restoration remains reversible.
- Denser, larger downstream foam, faster flow on slopes, and slope-triggered splashes at every river width.
- River Trench Size cycles 4 / 8 / 16 / 32 / 64 metre maximum advance (default 32); Configure River adjusts channel width and other settings at the aimed section.
- New branches receive their own configurable section. End River Branch and Reopen River End persist manual endpoints independently; a natural outlet never closes sibling branches.
- Existing route formats remain readable; endpoint edits are saved using route format 5.

# 0.9.10

- Updated the package icon to the approved vertical oval bronze-and-teal medallion, matching the ArcaneDecor mod family.

# 0.9.9

- Moved all plumbing pieces, items, recipes, assets, controls and effects into standalone ArcaneDecor: FlowCraft.
- ArcaneNaturalWaters now contains rivers, cascades and natural hot springs only. Neither mod depends on the other.

## 0.9.8

- Rename the mod to ArcaneNaturalWaters, including its package, DLL, BepInEx identity, hammer category and user-facing text.
- Remove the old ArcaneWaterWorks.dll when installing; the old plugin identity is marked incompatible to prevent duplicate loading.
- This release performs the rename. Plumbing extraction into ArcaneDecor: FlowCraft is still pending.

## 0.9.7

- Remove raised terrain rims along the entire submerged tail of a natural-water outlet, including shallow receiving water. Taper the cleanup at the edge of the river footprint while preserving higher land and deeper existing beds.
- Apply the corrected mouth profile to existing saved rivers when terrain regenerates; no retrenching is required.

## 0.9.6

- Prevent River Trench from raising low terrain into tall walls: bank buildup is capped at 0.35 m above the underlying terrain, including smoothed banks.
- Prevent steep surface extrapolation from creating water above the river source. Downstream slope and receiving-water handling remain intact.
- Apply these safeguards when existing saved routes regenerate. River terrain remains a reversible overlay; no world-save migration is required.

## 0.9.5

- Enable native metal structural integrity for all six plumbing pieces. Supported joints transfer strength, and unsupported pipes collapse using normal Valheim wear and material drops.
- Existing pipes acquire these rules on reload; brace floating runs before updating. Includes the native-style hoe icons from 0.9.4.

## 0.9.4

- Replace the seven bright river-tool tiles with native Valheim dirt, bronze pickaxe and hoe artwork, using muted gold action marks for start, extension, depth and undo.
- Build these icons from the installed game's sprites, preserving its textures and transparent silhouettes.

## 0.9.3

- Fix corrupted dash characters in all four hot-spring build names and the hot-spring settings description. Use plain hyphens for these separators.

## 0.9.2

- Render inward-facing copper surfaces so pipe and elbow interiors no longer disappear through back-face culling. Open sockets remain open.
- Animate outlet water with travelling translucent streaks, moving surface ripples and a longer ballistic trajectory.
- Add continuous impact spray with droplets and gravity, oriented to terrain, walls and other solid surfaces. Spray stops when the outlet stops.
- Replace repeated medallion and dirt fallback icons with distinct river-source, ambience, four pool-shape and seven hoe-action icons; embed and assign the artwork in the released DLL.
- Preserve pipe lengths, snapping, crafting costs, pump flow and standalone dependencies.

## 0.9.1

- Fix "Invalid placement" when aiming the hammer at any plumbing piece: pipes now permit attached construction while retaining disabled support decay.
- Add native hammer placement regressions for every combination of plumbing preview and target, including reproduction of the previous failure.
- Existing placed pipes inherit the fix when the world reloads; prefab IDs and recipes are unchanged.

## 0.9.0

- Add the supplied copper straight pipe, elbow, intake and Surtling powered pipe models, with endpoint snapping and actual-model hammer icons.
- Offer 2 m and 4 m copper pipes, costing 1 and 2 Copper respectively; elbow and intake cost 1 Copper each.
- Craft 2 powered 1 m pipes from 1 copper 2 m pipe and 1 Surtling Core. A copper 4 m pipe plus 2 cores yields either 4 powered 1 m pipes or 2 powered 2 m pipes.
- Add 5 gallons/sec pumping capacity per connected powered piece, independent of its length. Intakes draw from natural water, rivers and hot springs; ordinary lines use downhill gravity.
- Pour from the last exposed end, automatically moving the outlet as the line is extended. Dry intakes and disconnected lines stop flow; enclosed or buried outlets are blocked.
- Support floor, wall, ceiling and buried pipe runs, with tilt/roll controls and no structural-support decay. Powered pieces glow and water-stream size follows flow.
- Keep the implementation standalone: only BepInEx and Jotunn are required. See PLUMBING.md for recipes, controls and current flow limits.

## 0.8.0

- Remove the WaterGardens plugin dependency and build reference. Only BepInEx and Jotunn are required.
- Own the pool geometry, terrain, native water rendering, menus, cursor handling and ambience code inside the ArcaneWaters assembly under a separate namespace.
- Construct all four hot springs from vanilla prefabs, preserving ArcaneWaters prefab IDs and persisted pool/controller settings.
- Embed the existing white sitting stone model, ambience recordings and pool cascade assets; no WaterGardens runtime assets are needed.
- Add an independent River Ambience Stone under Hammer → Arcane Waters for river audio and firefly control.
- Keep WaterGardens decorations, decoration controls and pool groups separate. Previous WaterGardens River Garden Stones remain owned by that mod.

## 0.7.0

- Use Valheim's outdoor water appearance for rivers, including animated ripples, reflections, shallow-water clarity and shoreline foam.
- Keep fine surface ripples visible without adding large ocean swells to elevated rivers and ponds.
- Join nearby overlapping sections with different slopes and starting heights through a shared water surface.
- Match WaterGardens ponds at their receiving level without an overlapping transition sheet.
- End river meshes at native sea-level water and follow wave height along the approach, including buoyancy and placement previews.
- Clip water against fixed rock colliders using cached contact samples.
- Spread affected river mesh rebuilds across frames and remove repeated global rebuilds during loading.
- Requires ArcaneDecor: WaterGardens 1.2.0.

## 0.6.6

- Blend depth tint, opacity and whitewater across adjoining river sections with different saved settings.
- Use consistent environment lighting across separate river meshes.
- Blend ripple flow across adjoining rivers and keep internal joins from fading like shorelines.
- Remove the extra overlapping water strip where independently placed rivers meet at the same height.
- Correct river transparency when no WaterGardens pond is nearby.

## 0.6.5

- Preserve WaterGardens' original animated pond surfaces instead of replacing them with the river shader.
- Adjust the river approach to the receiving pond level without reshaping the pond.
- Blend the river appearance into native pond water along the river-side approach.
- Requires ArcaneDecor: WaterGardens 1.1.5.

## 0.6.4

- Connect overlapping WaterGardens ponds and rivers through a shared water-height field.
- Remove river surface overlap inside ponds and align the mesh grid, flow and lighting at their join.
- Keep river banks below the pond connection and restore river geometry when ponds are removed.
- Requires ArcaneDecor: WaterGardens 1.1.4.

## 0.6.3

- Join connected river sections into a shared water-height and flow model, removing conflicting end-cap heights on steep continuations.
- Align section meshes to the same world grid and remove duplicate water surfaces across their full shared footprint.
- Assign each shared surface triangle to one section, avoiding clipping gaps, and use matching normals across joins.
- Keep terrain shaping, boat buoyancy, decorations and placement previews aligned with joined river heights.
- Rebuild shared joins as sections load, change or are removed. Existing saved routes retain their format.

## 0.6.2

- Increase river opacity slightly while preserving soft shoreline and outlet fades.
- Add sparse animated foam where river water meets the terrain, without outlining unsupported waterfall edges.
- Add small, spaced shoreline splashes with limited particles and distance culling.
- Show sparse downstream-moving foam on narrow rivers as well as wide rivers, controlled by the existing Whitewater setting.
- Preserve downstream flow, boat placement and native buoyancy support.

## 0.6.1

- Blend river heights across steep bends and continue the downhill slope through river end caps, reducing abrupt seams and flat shelves.
- Use the same blended heights for river terrain, floating objects, decorations and placement previews.
- Allow boat placement previews to find river water while retaining normal building requirements and obstruction checks.
- Soften river ripple strength while preserving downstream flow.

## 0.6.0

- Replace the glassy river surface with downstream-moving ocean ripple textures, blended continuously through bends, softer reflections and restrained broken foam on wide rivers. Reuse installed Valheim textures at runtime.
- Fade water into the shoreline and receiving water, and extend the single river mesh beneath a raised, tapered terrain bank to hide exposed outside edges. Preserve open outlets and WaterGardens pool connections.
- Keep one water trigger per river section and bounded current particles. Flow is computed on mesh rebuild and animated by the GPU; terrain changes occur only on existing terrain rebuilds. No changes to WaterGardens menus or hot spring water.

## 0.5.1

- Raise a smooth, low terrain shoulder outside hot springs (approximately 20 cm above the water at its crest), retaining the deep basin and sitting stones. Extend the existing water mesh beneath the bank to conceal its cut edge on Valheim's coarse terrain grid. Leave connected pools and river openings unobstructed.
- Increase steam emission sixfold at the same slider setting, with larger wisps, longer lifetimes and more upward movement. Keep soft transparent edges, distance culling and a 160-particle cap. Existing steam settings still apply; zero remains off.
- No changes to ordinary WaterGardens pools, menus, recipes, saved settings or water textures. Requires the existing WaterGardens 1.1.2.

## 0.5.0

- Four fuel-free hot spring pools with native steam, bubbles, bathing warmth and comfort.
- Individual submerged WaterGardens white round stone seats scale with pool size and follow its bed contour. Occupied pools cannot be resized.
- Five crossfaded bubbling loops from the supplied recordings; six full stereo music choices replace local Valheim music while bathing, with separate volume controls.
- Optional creature ward, native spawn/monster exclusion and non-damaging boundary relocation, including pets.
- Original-style ArcaneWaters settings pages retain WaterGardens ambience and manually placed decoration compatibility.
- One shape-masked water trigger per hot spring, analytic water placement and bounded particles.
- Shared native cursor handling fixes disappearing pointers in both mods; requires WaterGardens 1.1.2.

## 0.4.4

Remove the full-surface scrolling foam mesh responsible for pale lines and discontinuous texture joins. Average normals at duplicated surface vertices to remove hard triangle lighting creases. Show downstream movement with small native foam flecks on precomputed wet tracks, at most 64 per visible section, with no terrain queries during animation. Reject tracks that would rise uphill. Preserve native water textures, native dirt, audio and the medallion.

## 0.4.3

- Fix backward/uphill-looking flow: texture coordinates follow each segment from parent to child, rather than mixing route distance with a fixed world-Z direction. End caps use the same downstream direction; branches start at their parent's path distance.
- Disable unrelated native water texture drift and restore the quieter pre-0.4 surface detail. Remove the dense white patches and bands; wide-river highlights remain subtle.
- Retain the native dirt riverbed correction, supplied sounds, volume control and oval medallion. Existing saves are unchanged.

## 0.4.2

Remove the separate brown silt mesh that produced a smooth, hard-edged strip. Riverbeds now use Valheim's native dirt terrain paint with a softened bank transition and preserved vegetation mask. The existing bed toggle controls that native paint and refreshes it when changed. Saved rivers retain their routes, depth, sound and settings; the medallion remains included.

## 0.4.1

Use the completed ArcaneWaters oval medallion for the package icon and embedded Arcane Spring build-menu sprite. No gameplay, sound or save-format changes from 0.4.0.

## 0.4.0

- Add a WaterGardens-style volume slider and the supplied AW_Brook_001, AW_FlowingRiver_001 and AW_LargeRiver_001 recordings with seamless loop preparation and restrained playback levels.
- Select sounds automatically by width: Brook below 4 m, Mid-Size from 4 m to under 8 m, Large at 8-12 m; retain a manual override.
- Add width-dependent animated whitewater with strength control and outlet fade.
- Add optional brown, terrain-following silt beneath the river without extra colliders.
- Add Smooth River Banks and Undo Bank Smoothing hoe tools with local, reversible, permission-checked strokes.
- Preserve sound/appearance on continuation sections and keep original WaterGardens controls and styling.
- Introduce backward-readable v4 route settings; update server and all clients together.
- Skip geometry/terrain rebuilds for sound-only changes.

## 0.3.1

Restore the original WaterGardens menu styling verbatim and make ArcaneWaters use it. Preserve the river integration and all saved settings.

## 0.3.0

- Remove the gameplay-wide 192 m cap through automatic streamed continuation sections.
- Preserve legacy sources and introduce v3 persistence for multiple branch outlets.
- Extend outlets beneath receiving water; suppress submerged endpoint berms and taper the rendered surface.
- Integrate elevated WaterGardens pond levels and a shared native menu implementation.
- Add 20 m WaterGardens river stones for manually placed decorations, floating/grounded settings, flowers, ambience, fireflies and running-water sound.
- Add analytic water placement rays without mesh colliders; arbitrate matching-level surface and terrain overlaps between sections.

# Changelog

## 0.2.0 - ArcaneWaters

- Corrected the product name while preserving save and configuration identifiers.
- One box water volume per spring; spatially indexed channel queries.
- Terrain processing limited to intersecting tiles, coalesced refreshes, and no repeating stone-collider repositioning.
- Water below bank tops, clipped shore geometry, and reversible containing banks on slopes.
- Native Valheim panel and five hoe tools: River Start, River Trench, Deepen River, Shallower River, Undo River.
- Connected branches and deeper beds without lowering the water surface.
- New sources start with short channels; removed inherited workbench requirement.

## 0.1.0

Initial spring-fed river prototype.
