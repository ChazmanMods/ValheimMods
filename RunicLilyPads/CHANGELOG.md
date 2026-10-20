## 1.2.1
- Use supplied round, oval, kidney-shaped and wild pool artwork in the hammer menu.
- Embed pool icons in the plugin and load them at registration; retain other decor thumbnails and the dedicated-server icon path.

## 1.2.0

- Give ponds Valheim's outdoor water appearance while retaining animated ripples, existing pond shapes and waterfalls.
- Use a shared fine ripple scale with rivers so pond water does not become a smooth reflective sheet at a connection.
- Match the water appearance used by ArcaneWaters 0.7.0 at pond and river connections.
- Refresh nearby water connections when ponds load, change size or are removed, without rebuilding every river.
- Update water materials when terrain water becomes available during scene loading.
- Preserve Infinity Hammer invulnerability compatibility.

## 1.1.5

- Restore the original animated pond water, fine surface mesh and pond height when ArcaneWaters is installed.
- Keep river connection adjustments on the river side, preserving existing ponds and their waterfalls.
- Retain Infinity Hammer invulnerability compatibility.

## 1.1.4

- Join pond water heights and terrain with overlapping ArcaneWaters 0.6.4 rivers.
- Match river water materials, flow and lighting, and extend pond liquid volumes to cover sloping connections.
- Refresh joined surfaces when a pond or river changes or is removed.

## 1.1.3

- Fix Water Garden pieces disappearing after placement with Infinity Hammer's invulnerability enabled.
- Preserve negative-health invulnerable pieces while retaining cleanup of old zero-health remnants and normal hammer removal and refunds.

## 1.1.2

Keep Valheim's native cursor and the hardware pointer visible while any WaterGardens or shared ArcaneWaters menu is open. Preserve existing menu colors, panels, fonts and controls. Expose an optional pool-controller integration for ArcaneWaters hot springs: custom defaults, editor, saved controller settings and a single masked liquid volume. Standalone WaterGardens pools and build-piece categories remain unchanged.

## 1.1.1

Restore the original WaterGardens menu styling verbatim and make ArcaneWaters use it. Preserve the river integration and all saved settings.

## 1.1.0

- Add River Garden Stone (4 Stone) for 20 m ArcaneWaters sections. It controls manually placed pieces only.
- Add optional river water/section API, river flower and floating-object settings, and fireflies following channel geometry.
- Share native Valheim wood panels, fonts and buttons with ArcaneWaters.
- Preserve standalone WaterGardens use without ArcaneWaters; a river stone creates no water itself.

## 1.0.2

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.
- Removed inherited floor/building usage tags from garden prefabs; retained the Water Garden category.
- Added native localization tokens for garden piece names and descriptions.

# Changelog

## 1.0.1

- All configuration menus now share the pond menu styling, with fully opaque backgrounds. Lantern, fountain, and statue controls use the same buttons, sliders, and font.
- Removed the oversized invisible tree-canopy collider. Build beside decorative Yggdrasil trunks at any supported tree size; trunk collision and tree interaction remain intact.

## 1.0.0 - Initial release

Bring ponds, flowers, light, and nature sounds to your Valheim builds.

- **50 hammer pieces** with matching icons: lily pads, flowering water lilies, ferns, reeds, cattails, grasses, bushes, ivy, mushrooms, rocks, lanterns, fountains, a dragon statue, and decorative Yggdrasil trees.
- **Four pond shapes** with widths from 4 to 24 metres and depths from 0.4 to 3 metres. Overlapping ponds connect, with flowing waterfalls between different elevations.
- **Floating lilies and wind-swaying foliage.** Walk and swim through lilies and small plants as they brush aside.
- **Garden controls** for connected-group flower size and floating fixtures, plus individual size menus for decorative trees.
- **Adjustable lighting and glowing orbs** for lanterns, fountains, and the dragon statue.
- **Seven night-ambience recordings** with loudness, speed, distance, and nighttime controls, alongside adjustable yellow fireflies.
- **Positional fountain and waterfall audio.** Each pond controls its outgoing waterfalls' volume and audible distance. Small and tall waterfalls select different recordings according to their vertical drop, with an adjustable threshold.
