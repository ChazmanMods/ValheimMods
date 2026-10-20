# ArcaneHunter player guide

ArcaneHunter adds a hunting activity loop to Valheim: choose an animal, follow magical clues, make a careful kill, and build your hunter's record. It also lets the host adjust individual creature spawn rates.

The current version is **0.1.0**, a test release. It has passed automated checks but has not yet been playtested in the game or in multiplayer.

## Your first hunt

1. Head into the Meadows with a bow, arrows and enough stamina.
2. Press **F6** to open your journal. Find **Deer** and click **Track**. This filters tracking to deer; it does not spawn one.
3. Close the journal and press **F7**. Hunter's Sense searches for nearby loaded deer and shows their last-known bearings and distances.
4. Follow a clue. The deer can move after the observation, so pulse again when needed.
5. Try to kill an unalerted, full-health deer with one opening hit. This earns a clean-kill bonus.
6. Open the journal again to see your kill count, mastery, renown and any commission progress.

The daily commission may select a different species. Check its target before trying to complete its objectives.

## Controls

| Control | What it does |
| --- | --- |
| F6 | Opens/closes the journal |
| F7 | Activates Hunter's Sense |
| F8 | Focuses the creature nearest your crosshair within tracking range; clears focus if none is in view |
| Escape | Closes the journal |
| Track button | Filters future tracking pulses to that species |
| All tracks button | Removes the species filter |

You can change keybinds in the configuration. These features currently require keyboard/mouse controls.

## Hunter's Sense

By default, each pulse costs **15 stamina**, searches **100 metres**, reveals up to **eight creatures**, and has a **20-second cooldown**. Clues last **30 seconds**. The host can change these values.

Each clue includes a species name, compass direction, distance to its observed position, freshness, whether it was calm or alerted, and the wind's direction relative to it.

These are snapshots. A fading clue can lead to a spot the animal has already left. Tracking can sense through terrain, but only sees creatures the game has already loaded. Empty searches still use stamina and start the cooldown. Wind information does not add scent simulation or change enemy detection.

## What the journal remembers

For each species, it records observations, biomes, kills, clean kills, longest bow/crossbow kill, and highest star count hunted. Your journal saves with your character.

Species mastery progresses at these kill counts:

| Kills | Mastery |
| --- | --- |
| 1 | Apprentice |
| 10 | Tracker |
| 30 | Expert |
| 100 | Legend |

Tamed creatures, players, bosses and kills that Valheim flags as cheated do not give hunting credit. Credit goes to the final hitter. Damage-over-time or creature ownership changes can prevent clean-kill/distance bonuses when matching hit information is unavailable.

## Daily commissions

Your first hunt, pulse or journal visit each **in-game world day** chooses a target from your current biome. All three commissions use that target. Travelling to another biome does not reroll the current day's board.

| Commission | Requirement | Reward |
| --- | --- | --- |
| Provisioner's Oath | Kill three of the target species | 40 renown |
| Silent Arrow | Kill one full-health, unalerted target with the opening hit | 60 renown |
| Farshot | Kill one target with a bow/crossbow from at least 40m | 60 renown |

Silent Arrow can be completed with any damage type. Farshot measures hunter-to-quarry distance **when the hit lands**. Rewards are automatic; you do not visit an NPC or claim them manually. The board does not guarantee nearby prey and can select a species whose ambient spawning you have disabled.

## Renown, titles and seals

A first species observation earns **5 renown**. A qualifying kill earns **10**, plus **5 per star** (up to nine bonus stars), **10 for a clean kill**, and **10 for a ranged kill at 40m or more**.

| Renown | Title |
| --- | --- |
| 0 | Wayfarer |
| 100 | Pathfinder |
| 400 | Trailwarden |
| 1,000 | Arcane Ranger |
| 2,500 | Wildspeaker |
| 5,000 | Mythic Huntsman |

The Seals tab holds eight permanent achievements:

| Seal | Requirement |
| --- | --- |
| First Blood | First qualifying wild kill |
| Ghost of the Grove | Ten clean opening kills |
| Beyond the Bowstring | An 80m bow/crossbow kill |
| Stormchaser | Five serpent kills |
| The Living Atlas | Observe twenty species |
| Crown of the Wild | Kill a two-star creature |
| Keeper of Three Oaths | Complete all three commissions on one daily board |
| A Hundred Footprints | Reach 100 kills of one species |

Renown, titles and seals are personal progression records. They do not grant damage buffs, extra loot, items, currency or vanilla skill levels.

## Making creatures more common

After the first launch, edit `BepInEx/config/chazman.ArcaneHunter.cfg` in your profile or server. Stop the game/server before editing the file, then restart. A compatible configuration manager can apply live changes. The in-game Spawns tab displays current settings; it is not a spawn editor.

**All spawn multipliers start at 1.** Installing the mod alone leaves spawn rates unchanged.

There are named sections for 39 creatures. Each has three separate controls:

- **ChanceMultiplier:** how likely an eligible spawn attempt is to succeed. `3` turns a hypothetical 5% roll into 15%. `0` disables that ambient entry.
- **FrequencyMultiplier:** how often attempts happen. `2` halves the usual interval.
- **PopulationMultiplier:** how many are allowed locally. `2` doubles an existing finite cap.

To make serpents more common, change the existing section to:

```ini
[Creature Serpent]
ChanceMultiplier = 3
FrequencyMultiplier = 2
PopulationMultiplier = 1
IgnoreTimeRestrictions = false
IgnoreWeatherRestrictions = false
```

This improves the rolls and makes attempts more frequent while preserving time and weather restrictions. Actual encounter frequency still depends on valid habitat, population limits and other game checks. Changing either restriction flag to `true` lifts that particular restriction; lifting time restrictions can also affect daytime despawning.

For additional or modded creatures, use their prefab names:

```ini
[Spawns]
CustomOverrides = Serpent:3:2:1;Hare:2:1.5:2
```

The format is `Prefab:chance:frequency:population`. Custom entries override named sections and retain vanilla time/weather restrictions. The console command `arcanehunter_prefabs` lists available names. A custom rule only works if that creature has an ambient spawn entry; it does not create a new spawn source.

Spawn controls do not change raids, breeding, dungeon/location spawners, nests, placed creatures or boss summoning. Lowering a rate does not remove animals already in the world.

## Multiplayer and installation

Install the package with BepInExPack Valheim 5.4.2333 and Jötunn 2.29.2. Put the **same ArcaneHunter version on the server and every client**. The host controls gameplay settings; your keybinds, interface scale and notifications stay local.

For all settings and technical limits, see [README.md](README.md). For the outstanding playtest checklist, see [TESTING.md](TESTING.md).
## Long-range automatic pickup (0.2.0)

Loose loot within **500 meters** of your character now goes straight to inventory automatically, even when you are flying ArcaneSniper's camera. Your normal auto-pickup toggle pauses this feature.

In `BepInEx/config/chazman.ArcaneHunter.cfg`, set `[Auto Pickup] Range` to any value from 2 to **2,000 meters**, or set `Enabled = false` in that section to restore normal pickup. These settings are controlled by the host in multiplayer. Restart after editing the file directly.

The range applies to loaded drops only. Your inventory must have room and enough carry weight. Placed/displayed items, tar-stuck loot, excluded drops, plants, containers and tombstones are not collected. This picks up eligible loose resources as well as hunting loot, across terrain, without requiring the camera to face them.
