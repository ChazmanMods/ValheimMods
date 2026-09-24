# ArcaneDecor: WaterGardens 1.0.2

Build a quiet corner of Valheim with flowering water lilies, flowing ponds, glowing lanterns, fountains, and fireflies.

WaterGardens adds **50 placeable garden pieces** to the hammer's **Water Garden** category. Combine ponds into a larger garden, decorate their banks, and give each space its own atmosphere with adjustable lighting and sound.

## A garden that moves with the world

- **Four pond shapes:** Round, Oval, Kidney, and Wild. Adjust width from 4 to 24 metres and depth from 0.4 to 3 metres.
- **Connected water:** Overlapping ponds join together. Elevated overlaps can form downward-flowing waterfalls with their own positional sound.
- **Floating lilies:** Four leafy lily-pad variations and six blue or white flowering varieties, from small buds to open flowers. Pads follow the water, with stems below the surface, and brush aside when you walk or swim through them.
- **Living greenery:** Ferns, a red fern, reeds, cattails, grasses, non-berry bushes, ivy, flowers, and glowing mushrooms. Plants use Valheim's wind shading for movement.
- **Yggdrasil garden trees:** Three decorative varieties, each with its own size menu.
- **Stone and light:** Garden rocks, stepping stones, a lantern, three fountains, and a dragon statue. Lantern lighting and luminous orbs have adjustable settings.
- **Evening ambience:** Seven selectable recordings, bright yellow fireflies, and controls for volume, playback speed, distance, and nighttime activity.

Each piece has a matching hammer-menu icon. All configuration menus share the same opaque panel styling.

## Installation

### With a mod manager

Install ArcaneDecor: WaterGardens through a Thunderstore-compatible mod manager and allow it to install the listed dependencies. Launch Valheim using the modded profile.

### Manual installation

1. Install **BepInExPack Valheim** and **Jotunn**.
2. Create `BepInEx/plugins/ArcaneDecorWaterGardens/` inside your Valheim installation.
3. Copy `ArcaneDecorWaterGardens.dll` into that folder.
4. Start the game through BepInEx.

Models, icons, and audio are embedded in the DLL; no separate audio folder is needed. Configuration Manager is not required.

For multiplayer, install the same WaterGardens version and its dependencies on the server and every participating client.

## Your first pond

1. Equip the hammer and open **Water Garden**.
2. Place a pond on suitable ground. Its configuration rock settles onto the terrain.
3. Look at the rock and press **E** when you see **[E] Configure garden**.
4. Adjust the pond and its atmosphere, then select **Save garden**. Cancel or Escape discards menu edits.
5. Add lilies to the water and plants around the edges.

Overlap another pond to connect it. A suitable height difference creates a flowing transition and automatically adds waterfall sound. Use **Waterfall volume** to adjust it; set it to **0** to mute it.

The pond's rock remains its control point. Remove that rock with the hammer to remove the pond. Normal hammer removal returns the piece's materials.

## Shape the atmosphere

Ponds and the **Night Garden Stone** provide seven recordings named `night_ambience_001` through `night_ambience_007`, plus an Off selection. Adjust loudness, playback speed, and audible distance. Changing playback speed also changes pitch.

**Night only** limits night ambience and fireflies to nighttime. Turn it off and save to use them during the day. Set **Fireflies** to **0** to turn them off.

Fountains have individual controls and their own positional running-water audio. Their sound fades with distance and is independent of the selected night recording. The dragon statue has an adjustable glowing orb, without fountain water or audio.

## Connected pond controls

Some settings apply to the entire connected group of overlapping ponds:

- Flower and bud size, from **15% to 150%**.
- Separate floating controls for lanterns, fountains, and statues.

Pond width, depth, waterfall volume and distance, night ambience, and fireflies remain individual pond settings. The upper pond controls each waterfall flowing out of it. Set Waterfall distance from 5 to 100 metres; the 100% volume setting provides a stronger sound with softened peaks. Rocks sit halfway into the water. Fixtures with floating disabled settle onto the terrain or pond bed.

Each Yggdrasil tree has its own menu with a **25% to 200%** size slider. Its roots stay in place as it scales. These are decorative trees, with solid trunks and no harvesting drops. You can build right beside the trunk, including under the canopy, at every supported size.

## Controls at a glance

Open a pond rock with **[E] Configure garden**, then choose **Save garden** after changing its settings. Cancel or Escape leaves saved settings unchanged.

| Control | Range or behavior | Applies to |
| --- | --- | --- |
| Width | 4-24 m | This pond |
| Depth | 0.4-3 m | This pond |
| Flowers and buds | 15-150%; default 50% | Connected pond group |
| Floating lanterns, fountains, statues | Separate on/off controls | Connected pond group |
| Waterfall volume | 0-100%; default 35%; 0 mutes | Waterfalls flowing out of this pond |
| Waterfall distance | 5-100 m; default 16 m | Waterfalls flowing out of this pond |
| Night recording | night_ambience_001 through 007, or Off | This pond |
| Loudness | 0-100% | This pond's night ambience |
| Playback speed | 0.5-1.5x; changes pitch too | This pond's night ambience |
| Audible distance | 5-40 m | This pond's night ambience |
| Fireflies | 0-100; 0 turns them off | This pond |
| Night only | Restrict night ambience and fireflies to nighttime | This pond |

For waterfall sound, edit the **upper pond's** rock. Fountain sound is configured on each fountain and plays independently of waterfall and night ambience. Set its volume to 0 to mute it; disabling its visual water flow does not mute the recording.

Each lantern, fountain, and dragon statue has its own lighting/glow menu. Each Yggdrasil tree has its own **25-200%** size slider. The Night Garden Stone supplies ambience and fireflies without a pond, with **4-24 m** coverage.

## Placement and compatibility notes

Start on gentle terrain above the ocean waterline. WaterGardens does not remove Valheim's ocean, so ponds below sea level may be flooded by it. Very steep ground can expose the limits of the game's terrain grid. Existing buildings and large vegetation are not automatically cleared.

Ponds apply a reversible terrain overlay. Removing a pond reveals the saved terrain underneath, including any underlying terrain edits.

Use the same WaterGardens version on the server and every client. Mismatched versions are unsupported and can cause missing pieces, incompatible saved settings, or connection problems. Do not rely on an automatic version-mismatch warning to protect your world.

## Piece catalog and progression

All 50 pieces are built with the **Hammer**, in **Water Garden**, within range of a **Workbench**. They use early-game Wood, Stone, and Dandelions; no biome visit, boss defeat, or upgraded station is added as a requirement. The decorative Yggdrasil trees do not require reaching the Mistlands. Normal material-discovery rules still apply.

These are the recipes in 1.0.1, including the differences between flower variants. Normal hammer dismantling returns their recipe materials.

| Piece | Materials |
| --- | --- |
| Shoreline Reeds | 2 Stone |
| Garden Ferns | 2 Stone |
| Marsh Ferns | 2 Stone |
| Low Garden Shrub | 2 Stone |
| Meadow Flower Patch | 2 Stone |
| Garden Dandelions | 2 Stone |
| Green Ivy Panel | 2 Stone |
| Garden Boulder | 2 Stone |
| Stepping Stone | 2 Stone |
| Small Garden Rock | 2 Stone |
| Red Garden Fern | 2 Stone |
| Garden Fern I | 2 Stone |
| Garden Fern II | 2 Stone |
| Garden Flora Cluster | 2 Stone |
| Garden Glow Mushrooms | 2 Stone |
| Garden Reeds VI | 2 Stone |
| Garden Cattails | 2 Stone |
| Garden Rock I | 2 Stone |
| Garden Rock II | 2 Stone |
| Garden Rock III | 2 Stone |
| Mossy Garden Rock I | 2 Stone |
| Mossy Garden Rock II | 2 Stone |
| Garden Lantern | 10 Stone |
| Garden Fountain 1 | 20 Stone |
| Garden Fountain 2 | 20 Stone |
| Garden Dragon Statue | 20 Stone |
| Garden Fountain 5 | 20 Stone |
| Tall Grass 1 | 2 Stone |
| Tall Grass 2 | 2 Stone |
| Tall Grass 3 | 2 Stone |
| Bush 1 | 2 Stone |
| Bush 2 | 2 Stone |
| Yggdrasil Garden Tree I | 10 Stone |
| Yggdrasil Garden Tree II | 10 Stone |
| Yggdrasil Garden Tree III | 10 Stone |
| Night Garden Stone | 4 Stone |
| Round Pond | 20 Stone |
| Oval Pond | 20 Stone |
| Kidney Pond | 20 Stone |
| Wild Pond | 20 Stone |
| Lily Pad - Small | 2 Wood |
| Lily Pad - Broad | 2 Wood |
| Lily Pads - Twin Patch | 2 Wood + 1 Dandelion |
| Lily Pads - Natural Cluster | 4 Wood + 1 Dandelion |
| Blue Lily Bud | 2 Wood |
| Blue Lily Blossom | 2 Wood |
| White Lily Blossom | 2 Wood + 1 Dandelion |
| Blue Water Lily | 4 Wood + 1 Dandelion |
| White Lily Bud | 2 Wood + 1 Dandelion |
| White Water Lily | 2 Wood + 1 Dandelion |

## Updating or removing the mod

Back up your world before changing its mod setup. Close the game before replacing the DLL, and keep only one installed copy of WaterGardens. Update servers and clients together.

Before uninstalling, remove placed WaterGardens pieces while the mod is still installed. Worlds containing custom pieces need the mod to load those pieces correctly.

## Credits

Created by **Chazman**, who created all custom models except those referenced from Valheim itself. Native Valheim assets are loaded from the installed game. Fountain and waterfall recordings are from **Pixabay**; the seven night recordings are from **Mixkit**. The oval package medallion is **AI-generated artwork**. See the enclosed `THIRD_PARTY.md` for audio entries and license links.

## Help and troubleshooting

For support and bug reports, visit [Chazman Mods on Discord](https://discord.gg/7HKHTCdFqY) or [Chazman Mods on GitHub](https://github.com/ChazmanMods/ValheimMods). Include your game/mod versions, whether you use a dedicated server, steps to reproduce the issue, and the relevant BepInEx log.

- **No night sound:** Select a recording, increase Loudness, use Test sound, and disable Night only temporarily. Save, then stand within the audible distance.
- **No waterfall sound:** Edit the upper pond's volume and distance. Flat overlaps do not create waterfalls.
- **No fireflies wanted:** Set Fireflies to 0 on each relevant pond or Night Garden Stone and save.
- **Missing configuration rock:** Resizing moves the rock outward on the same side of its pond; overlaps can leave it on an underwater bed. Search the perimeter for the [E] prompt.
- **Missing hammer category:** Launch the modded profile, check that dependencies loaded, and remove duplicate mod DLLs.

Advanced settings are in `BepInEx/config/chazman.RunicLilyPads.cfg`. The historical filename is retained to preserve existing configurations and the mod's stable identity.

## Waterfall sound selection

Waterfall sounds are chosen by the vertical drop between the upper and lower pond water levels: below 1.5 m uses waterfall_001; 1.5 m or more uses waterfall_002. Each transition chooses independently. In `BepInEx/config/chazman.RunicLilyPads.cfg`, `[Audio] TallWaterfallHeight` changes this local threshold from 0.25 to 10 m. It is separate from the upper pond's Waterfall distance slider, which controls how far away the sound can be heard. Use the same threshold on clients if everyone should hear the same selection. Restart after editing the config file manually.

## Language files

This version follows Valheim's selected language using files in `Translations/ArcaneDecorWaterGardens` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
