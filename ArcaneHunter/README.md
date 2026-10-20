# ArcaneHunter

**Read the wilderness. Choose your quarry. Write your legend.**

ArcaneHunter adds a personal hunting journal, magical tracking, daily hunting oaths, species mastery, collectible achievement seals, and individually configurable ambient creature spawns to Valheim.

Start with the [player guide](PLAYER-GUIDE.md) for a first-hunt walkthrough and an explanation of every activity.

## Controls

| Key | Action |
| --- | --- |
| **F6** | Open or close the field journal |
| **F7** | Hunter's Sense: spend stamina to read nearby wildlife signs |
| **F8** | Focus the creature closest to the crosshair, or clear focus when none is in view |
| **Escape** | Close the journal |

Keys and interface scale can be changed in the configuration file. Mouse controls work in the journal. Keyboard/mouse input is currently required for these features.

## The hunt

**Hunter's Sense** reveals up to eight loaded wild creatures within 100 metres. A pulse costs 15 stamina and has a 20-second cooldown by default. Signs last 30 seconds and show species, compass bearing, distance, freshness, alertness at observation, and wind direction relative to the quarry. Signs mark the **last known position**, so an animal can move away while you follow its trail. Tracking can sense creatures through terrain; it does not load distant areas or spawn prey. Wind is navigational information, not a new scent or detection mechanic.

**Choose your quarry** with the journal's Track button. Your next pulse then reads only that species. Focus also works with previously unknown or modded creatures. First observations are entered into your field journal along with their biome.

**Field journal:** track kills, clean opening kills, longest ranged kill, highest star count, and species mastery. Mastery becomes Apprentice at 1 kill, Tracker at 10, Expert at 30, and Legend at 100. Records travel with your character and persist through normal game saves.

**Three daily oaths:** your first hunt, pulse, or journal visit each world day selects quarry from your current biome. The board stays fixed for that world day even if you travel elsewhere:

- **Provisioner's Oath:** three kills; 40 renown.
- **Silent Arrow:** one clean opening kill; 60 renown. Any damage type can qualify; the quarry must have full health and be unalerted before the killing hit.
- **Farshot:** a bow or crossbow kill at 40 metres or more; 60 renown. Distance is measured between hunter and quarry when the hit lands, not the projectile's launch point.

Commissions award automatically. They reward personal **renown**, not items or currency. Unknown biomes use an observed species if available; a newly explored region may require an observation before a board can be generated. A target with ambient spawns disabled may still appear on a board. Commission objectives do not guarantee nearby prey.

**Hunter titles:** Wayfarer, Pathfinder (100 renown), Trailwarden (400), Arcane Ranger (1,000), Wildspeaker (2,500), and Mythic Huntsman (5,000). First observations earn 5 renown; kills earn 10 plus star, clean-kill, and longshot bonuses. Titles do not modify damage, loot, or player skills.

**The Hunter's Constellation:** eight permanent achievement seals, including Ghost of the Grove, Stormchaser, Beyond the Bowstring, The Living Atlas, and Keeper of Three Oaths.

Tamed creatures, players, bosses, and kills flagged by Valheim as cheated do not award hunting credit. The creature's network owner sends the final hitter a journal receipt. Damage-over-time kills or ownership changes may record an ordinary kill without an opening-hit/distance bonus if the matching hit information is unavailable. Renown and achievements are personal records, not an anti-cheat-protected server economy.

## Individual spawn controls

On first launch, the mod creates **`BepInEx/config/chazman.ArcaneHunter.cfg`**. All spawn multipliers default to **1**, so installing ArcaneHunter does not automatically increase creature populations.

There are individual sections for 39 familiar creatures across Meadows, Black Forest, Swamp, Mountain, Plains, Ocean, Mistlands and Ashlands. Each offers:

| Setting | Meaning |
| --- | --- |
| `ChanceMultiplier` | Multiply each eligible spawn roll; 0 disables the entry, 1 preserves it. Range 0–20; final chance cannot exceed 100%. |
| `FrequencyMultiplier` | Attempt spawns this many times as often. 2 halves the interval. Range 0.1–20; adjusted intervals cannot fall below 1 second. |
| `PopulationMultiplier` | Scale the existing local cap, rounded up. Range 0.1–10; adjusted finite caps stop at 200. Existing unlimited caps stay unlimited. |
| `IgnoreTimeRestrictions` | Opt into both day and night spawning. This can also alter vanilla daytime despawning. |
| `IgnoreWeatherRestrictions` | Opt into spawning regardless of the entry's required weather. |

For a livelier ocean, start with:

```ini
[Creature Serpent]
ChanceMultiplier = 3
FrequencyMultiplier = 2
PopulationMultiplier = 1
IgnoreTimeRestrictions = false
IgnoreWeatherRestrictions = false
```

This raises eligible serpent rolls and makes attempts more frequent while preserving weather and time restrictions. It does **not** guarantee six times the encounters: habitat, population, distance, progression and other vanilla checks still apply.

### Any other creature, including modded and newer-game creatures

Use the synchronized custom list. Names must be **prefab names**, not displayed names:

```ini
[Spawns]
CustomOverrides = Serpent:3:2:1;Hare:2:1.5:2
```

Format: `Prefab:chance:frequency:population`, with semicolons separating creatures. Entries override the three multipliers in named sections; custom overrides preserve vanilla time/weather restrictions. Decimal numbers use a dot. Duplicate names use the final entry; malformed rows are logged and ignored.

With Valheim's console enabled, `arcanehunter_prefabs` prints available creature prefab names and logs them to `BepInEx/LogOutput.log`. Not every creature has an ambient spawn entry. The same custom syntax works for any future or modded creature **if it uses Valheim's ambient spawn system**.

These controls affect ambient world spawn lists, including alternate biome lists. They do not change raids, breeding, dungeon/location spawners, destructible nest spawners, placed creatures, or boss summoning. No existing creatures are removed when lowering rates. No new spawn habitat is invented for a creature that lacks an ambient entry.

Config changes are read on each spawn attempt. Use a compatible configuration manager for live changes, or edit the host's file while the game/server is stopped and restart. Manual file edits are not watched automatically. Other spawn mods can also alter rates; avoid configuring the same creature in multiple mods unless you intend their effects to combine.

## Installation

Use the local ZIP import in r2modman/Thunderstore, or place `ArcaneHunter.dll` in `BepInEx/plugins/ArcaneHunter/`.

Dependencies:

- BepInExPack Valheim **5.4.2333**
- Jötunn **2.29.2**

Install the same ArcaneHunter version on **every client and the server**. Jötunn enforces compatibility and synchronizes host gameplay settings. Each player's keybinds, notifications and interface scale remain local. A server-only installation is insufficient because clients own the active spawn areas.

The package contains no replacement game assemblies and no preconfigured `.cfg` that would overwrite your preferences. Removing the DLL stops its gameplay changes; the character's stored journal remains available if you reinstall.

## Build and validation

Version **0.1.0 is a test release**. It builds against the local Valheim assemblies available on September 18, 2026 with no compiler warnings. Automated tests exercise spawn isolation, default/no-op behavior, raid exclusion, custom parsing, commission rewards, save roundtrips, duplicate receipts, mastery/seals and compiled game API references. These tests do not run the Unity game, render the journal, or simulate real multiplayer. See `TESTING.md` for the remaining playtest checklist.

From this source directory:

```powershell
dotnet build ArcaneHunter.csproj -c Release
dotnet run --project Tests/ArcaneHunter.Tests.csproj -c Release
./build.ps1
```

Override the `VALHEIM_INSTALL` and `BEPINEX_PROFILE` MSBuild properties for a different installation. The regression harness defaults to the development machine's installed assemblies.

## Support Chazman's Mods

If you enjoy my Valheim mods and want to support continued development, testing, multiplayer compatibility, and new projects, I would appreciate it.

Support is completely optional. Playing the mods, reporting bugs, sharing feedback, and recommending them to other players helps just as much.

Thanks for supporting Chazman's Mods and the continued development of RunicSuite, ArcaneDecor, and other projects!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
# Long-range auto pickup (0.2.0)

Automatically collects eligible loose items within **500 meters** of your character, including while using ArcaneSniper. Change `[Auto Pickup] Range` in `BepInEx/config/chazman.ArcaneHunter.cfg` from 2 to **2,000 meters**. `[Auto Pickup] Enabled = false` restores vanilla pickup. The normal game auto-pickup toggle also pauses collection.

Items transfer directly into inventory, including across terrain. Inventory space, carry weight, tar restrictions, excluded drops and placed items remain respected. This covers loose resources as well as creature loot; it does not harvest plants, empty containers or loot tombstones. Only currently loaded drops exist for collection: increasing the radius does not load distant areas. Dense fields are processed in batches. The host controls these settings; update ArcaneHunter on the server and all clients together.
