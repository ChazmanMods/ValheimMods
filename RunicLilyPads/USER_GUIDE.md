# ArcaneDecor: WaterGardens - User Guide

All configuration menus use the same fully opaque panel style. Decorative Yggdrasil trees allow building beside their solid trunks without a canopy-wide exclusion area, including after resizing.

## Placing and removing pieces

Equip the hammer and select **Water Garden**. The hammer shows each piece's recipe and placement preview.

Place ponds and rooted plants on terrain. Place lilies on water. Ivy can attach to walls. Decorative rocks and fixtures can be arranged around the pond; connected pond settings determine whether lanterns, fountains, and statues float or settle onto the bed.

Small foliage and lilies allow you to pass through and brush them aside. Trees retain solid trunks.

Use normal hammer removal to dismantle pieces and recover their materials. A pond is removed through its configuration rock.

## Pond menu

Look at the configuration rock and press **E** at the **[E] Configure garden** prompt. Preview your changes, then choose **Save garden** to keep them. Cancel or Escape discards your edits.

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

The flower-size control affects flowering lilies and supported garden flowers. The four plain lily-pad variations retain their original sizes. Buds remain smaller than blossoms, which remain smaller than open flowers.

When pond groups connect, the most recently saved shared settings take precedence. If a group separates, its ponds retain the last synchronized settings.

## Connecting ponds and waterfalls

Overlap pond footprints to join them. Similar-height ponds form connected still water; elevated overlaps can create a downward-flowing transition. Build on gentle slopes and adjust placement and width to shape the connection.

Waterfall audio appears automatically where an elevated overlap produces a waterfall. Each transition has a positional sound source, so it becomes quieter as you move away. It runs during both day and night and is independent of night ambience and fountain sound.

Use **Waterfall volume** and **Waterfall distance** on the upper pond's rock to adjust its outgoing waterfalls. These controls are independent for each pond, and 100% volume gives a stronger sound. Set it to **0** to mute them. Removing the transition removes its sound source.

## Night Garden Stone

Use this piece to add night recordings and fireflies without building a pond. Open its menu with **E**, adjust the atmosphere, and save. Its coverage can be set from **4 to 24 m**.

Select **Off** to disable the night recording. Set **Fireflies** to **0** to disable the insects. These controls are independent.

## Lanterns, fountains, and the dragon statue

Open the individual object's menu to adjust its supported lighting and glow controls. Fountains also provide water-flow and sound-volume controls.

Each fountain plays its own running-water recording from its basin. Audio fades out within roughly **10 m**, so nearby fountains are heard according to your position. Night-only ambience settings do not silence fountains.

Turning off the fountain's visual water flow does not mute its audio. Set that fountain's sound volume to **0** to silence it.

The dragon statue provides light and orb glow controls; it does not produce running water or fountain audio.

## Yggdrasil tree size

Each of the three decorative tree varieties has its own interaction menu. Look at a tree and use the game's interaction control to open it. Adjust size from **25% to 200%**, preview the result, and save or cancel.

Only that tree changes. Its roots remain anchored and its trunk collision scales with it.

## Troubleshooting

### I cannot hear the night recording

Choose a recording rather than Off, raise Loudness, and try **Test sound** for a short preview. Turn **Night only** off, choose **Save garden**, and stand within the configured audible distance. Check the game's audio settings as well.

For advanced checks, the generated config file is `BepInEx/config/chazman.RunicLilyPads.cfg`. Its `Audio/MasterVolume` setting controls the local night-ambience volume multiplier. This is not the fountain or waterfall volume control. The historical config filename is retained to preserve existing configurations and the mod's stable identity.

### I cannot hear a fountain or waterfall

Check the fountain's own volume, or the upper pond's **Waterfall volume** and **Waterfall distance**, and move closer. Waterfall sound requires an actual elevated pond transition; a flat overlap does not produce a waterfall.

### I want no fireflies

Set **Fireflies** to **0** on each relevant pond or Night Garden Stone and save.

### A pond is flooded or the banks look unusual

Check its position relative to the native ocean and the steepness of the ground. The mod cannot lower or remove ocean water. Gentler terrain gives the most predictable banks and connections.

### The hammer category is missing

Confirm that the game was launched through the modded profile and that BepInEx, Jotunn, and WaterGardens are installed. Check `BepInEx/LogOutput.log` for loading errors and remove duplicate copies of the mod DLL.

### Reporting a problem

Record your Valheim and WaterGardens versions, whether the issue occurs in single-player or multiplayer, and the steps that reproduce it. Include a screenshot where useful and the relevant BepInEx log. Mention other mods that affect terrain, water, placement, or audio.

For support, visit [Chazman Mods on Discord](https://discord.gg/7HKHTCdFqY). The full piece-and-material catalog is also included in the README and CATALOG.md.

## Waterfall sound selection

Waterfall sounds are chosen by the vertical drop between the upper and lower pond water levels: below 1.5 m uses waterfall_001; 1.5 m or more uses waterfall_002. Each transition chooses independently. In `BepInEx/config/chazman.RunicLilyPads.cfg`, `[Audio] TallWaterfallHeight` changes this local threshold from 0.25 to 10 m. It is separate from the upper pond's Waterfall distance slider, which controls how far away the sound can be heard. Use the same threshold on clients if everyone should hear the same selection. Restart after editing the config file manually.

Project and bug reports: [Chazman Mods on GitHub](https://github.com/ChazmanMods/ValheimMods).

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
