# Runic Hall of Heroes

Runic Hall of Heroes turns equipment into a persistent record of a server's adventures.
Weapons and worn armor remember victories, boss falls, deaths, and their latest exploit.
As their renown grows, they earn names that appear in tooltips and on nearby chronicle plaques.

## Support and community

Need help, found a bug, or have an idea?

[Join the Chazman Mods Discord](https://discord.gg/7HKHTCdFqY)

- Report bugs in **#bug-reports**.
- Request features in **#feature-requests**.
- Get installation help in **#installation-help**.
- Follow new releases in **#releases**.

## First playable milestone

- A dedicated **Hall of Heroes** tab in the Hammer containing every piece from this mod.
- Three coordinated original building-material families with roughly fifty pieces:
  - Rune-stacked and monumental carved stone: full and half walls, single and double
    bricks, floors, stairs, arches, columns, plinths, cornices, corners, and spires.
  - Majestic mahogany: full and half walls, single and double blocks, 1x1 and 2x2
    floors, horizontal and upright beams in multiple lengths, angled beams, stairs,
    an arch, a carved divider, columns, and a gate.
- Restrained structural snap grids replace the inherited decorative snap points so pieces align squarely.
- A six-slot Runic Hero Display based on Valheim's armor stand.
- A separate wall-mounted Runic Display Light can be placed above or beside a display.
- Interact with the light to cycle through six heroic colors.
- A Heroic Chronicle Plaque automatically recounts the nearest display's greatest history.
- Weapons record killing blows and boss kills.
- Equipped armor records witnessed victories, boss falls, and deaths.
- Equipment earns initial titles at meaningful renown thresholds.
- Full item history is stored in `ItemData.m_customData` and is preserved by RunicDisplayStands.

## Echo Runestone prototype

- The mod continuously retains roughly the last 10 seconds of the local player's rendered
  viewpoint as a small rolling frame buffer.
- On death, those captured frames are sealed as the current saga.
- Build an **Echo Runestone** from the Hall of Heroes Hammer tab and interact with it.
- The runestone immediately creates an in-world projection and plays what the player actually
  saw—including enemies, combat effects, terrain, weather, teammates, and UI.
- Version 0.3.0 is an in-memory single-player proof of concept. It captures at 480x270 and five
  frames per second to bound memory and capture cost. Persistent clips, higher-quality encoding,
  audio, manual fight markers, and multiplayer sharing are not implemented yet.

## Installation

Install on the server and every client. Requires BepInEx, Jotunn, and
RunicDisplayStands 1.2.6 or newer.

Copy `RunicHallOfHeroes.dll`, `runic_stone_facade.png`, `monumental_carved_stone.png`,
`runic_stone_floor_clean.png`, and `majestic_mahogany.png` into the same folder under
`BepInEx/plugins/RunicHallOfHeroes/`.

## Status

Version 0.3.0 replaces the failed animated-character reenactment with rolling player-viewpoint
capture and immediate in-world video projection. Version 0.2.2 replaces the rough, wavy stone-floor silhouette with a straight-edged floor mesh
and a dedicated orthogonal tile texture. Version 0.2.1 replaces the primitive placeholder echo with the fallen player's actual rendered
appearance and recorded animation states. Version 0.2.0 added the first playable Echo Runestone
death-saga prototype. Version 0.1.7
expanded the architectural catalog, used true timber bases for all
mahogany pieces, restores the independent wall light, retains restrained structural
snap points, and integrates the hero display with RunicDisplayStands. Planned expansions
include manual dedication ceremonies, richer deed categories, item-specific
name families, plaque styles, memorial records, and audiovisual naming events.

## Support Chazman's Mods

If you enjoy my Valheim mods and want to support continued development, testing, multiplayer compatibility, and new projects, I would appreciate it.

Support is completely optional. Playing the mods, reporting bugs, sharing feedback, and recommending them to other players helps just as much.

Thanks for supporting Chazman's Mods and the continued development of RunicSuite, ArcaneDecor, and other projects!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
