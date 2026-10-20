# Credits

ArcaneWaters is a Chazman mod. Its independent pool and menu implementation was adapted from Chazman's ArcaneDecor: WaterGardens source and is compiled into ArcaneWaters.

## Supplied river recordings

Chazman supplied these recordings for incorporation into ArcaneWaters 0.4.0:

- Brook: AW_Brook_001.mp3
- Mid-Size River: AW_FlowingRiver_001.mp3
- Large River: AW_LargeRiver_001.mp3

The embedded versions are mono, normalized conservatively, and loop-crossfaded over half a second. The source recordings' original authorship and license were not specified with these files; no third-party origin or license is inferred. This document grants no separate rights to redistribute the recordings as standalone audio.

Version 0.8.0 embeds the seven existing pool ambience recordings and two waterfall recordings for standalone playback. Their credits are included below.

## Native assets and dependencies

Version 0.5.0 also incorporates the user-supplied AW_HotSpring_001–003.mp3 recordings as five mono, loop-crossfaded Ogg files (including two blends), and AW_HSMusic_001–006.mp3 unchanged as full stereo music choices. Source hashes and processing measurements are recorded in Work/hot-spring-audio/report.json. The same supplied-audio attribution and rights limitations above apply. Version 0.8.0 embeds Chazman’s existing rock_001 model, texture and icon for the sitting stones, and the existing custom cascade shader bundle. Materials still use native Valheim resources at runtime.

Stone meshes, water/particle/rock materials and piece templates are obtained from the installed Valheim game at runtime; native game assets are not redistributed. Valheim belongs to its respective owners; this is an unofficial mod. The river shader is original code and borrows native ocean normal and foam textures at runtime. BepInEx and Jotunn are the only external mod dependencies and are not included in this package.

The ArcaneWaters oval medallion is existing AI-generated artwork created for Chazman using the Arcane mod family as references. Its original prompt and artwork are preserved under Art/.


## Waterfall recordings - Pixabay

The following recordings were supplied by Chazman from **Pixabay** and adapted for embedded playback in the mod:

| Embedded sound | Supplied recording | Use |
| --- | --- | --- |
| waterfall_001 | WG_Waterfall_001 | Waterfalls below the configured vertical-drop threshold |
| waterfall_002 | WG_Waterfall_002 | Waterfalls at or above the configured vertical-drop threshold |

Both recordings are converted to mono, loop-crossfaded, and integrated into the waterfall behavior. The default switch height is 1.5 metres. These sounds are components of the mod, not a standalone sound-effects collection.

Source: [Pixabay Sound Effects](https://pixabay.com/sound-effects/). License: [Pixabay Content License](https://pixabay.com/service/license-summary/) and [full terms](https://pixabay.com/service/terms/). Attribution is not required under those terms; the source is acknowledged here voluntarily. The license's restrictions continue to apply to the underlying recordings.

## Night ambience - Mixkit

Seven recordings supplied from **Mixkit** are embedded under the following neutral names:

| Embedded sound | Mixkit recording title | Asset number |
| --- | --- | --- |
| night_ambience_001 | River shore with crickets ambience | 1784 |
| night_ambience_002 | Summer crickets loop | 1788 |
| night_ambience_003 | River shore insect noise | 1785 |
| night_ambience_004 | Pond ambience with crickets | 1783 |
| night_ambience_005 | Summer night crickets loop | 1789 |
| night_ambience_006 | Summer night in the forest | 1227 |
| night_ambience_007 | Night crickets near the swamp | 1782 |

Source: [Mixkit Sound Effects](https://mixkit.co/free-sound-effects/). License: [Mixkit Sound Effects Free License](https://mixkit.co/license/modal/sfxFree/). The recordings are incorporated into the mod's ambience system and are not offered as standalone sound assets. Original recording ownership remains with the respective rights holders.


