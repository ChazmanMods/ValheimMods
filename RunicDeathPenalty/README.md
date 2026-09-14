# RunicDeathPenalty 0.1.0

Group progression determines where normal death protection ends and which resources can be harvested or used. This is a first testing release; it has passed isolated dedicated-server runtime checks, but a real two-client playtest is still required before production deployment.

## Install

Install BepInExPack Valheim 5.4.2350 or later on the server and every client. Place `RunicDeathPenalty.dll` in `BepInEx/plugins/RunicDeathPenalty/`. The matching version is required on both sides. There is no required dependency on other Runic mods.

Start once to generate `BepInEx/config/chazman.RunicDeathPenalty.cfg`. Edit the SERVER copy. Clients receive the server policy and cannot override it in their local files. Use `rdp reload` in the host/server console after editing, or restart the server. Do not replace an existing administrator configuration with the example file.

## Defaults

- Normal rules through the group's current biome. The very next biome is restricted.
- Restricted death: **2x the world's normal skill-loss rate**, no No skill drain period, and no Corpse Run from the resulting grave. Existing recovery protections cannot protect a player inside restricted biomes.
- Harvesting in restricted biomes and using above-tier items anywhere: **blocked**, with independent switches.
- Ocean: normal rules. Entry warnings: enabled. Optional recovery cap: disabled.
- Boss progression: automatic, sequential, shared across the world. No additional biome lead.

| Boss defeated | Normal rules through | Restricted from |
|---|---|---|
| None | Meadows | Black Forest |
| Eikthyr | Black Forest | Swamp |
| The Elder | Swamp | Mountains |
| Bonemass | Mountains | Plains |
| Moder | Plains | Mistlands |
| Yagluth | Mistlands | Ashlands |
| The Queen | Ashlands | Deep North |
| Fader | Deep North | No later built-in tier |

The installed game contains Deep North equipment and an actual `defeated_fader` progression key; this release includes that tier. Boss kills out of sequence do not skip missing earlier milestones.

## Administrator settings

Tier numbers: `0 Meadows`, `1 BlackForest`, `2 Swamp`, `3 Mountains`, `4 Plains`, `5 Mistlands`, `6 Ashlands`, `7 DeepNorth`.

| Setting | Default | Purpose |
|---|---|---|
| General.Enabled | true | Enable gameplay restrictions; connection version agreement remains required. |
| General.EntryWarnings | true | Explain consequences on entering restricted territory. |
| Progression.Mode | Automatic | Automatic boss milestones, or Manual. |
| Progression.ManualTier | 0 | Allowed tier in Manual mode. |
| Progression.MaximumTier | 7 | Admin ceiling on allowed progression. |
| Progression.AllowedBiomeLead | 0 | Extra allowed tiers, still subject to MaximumTier. |
| Progression.OceanRequiredTier | -1 | -1 exempts Ocean; 0–7 sets a required tier. |
| Death.SkillLossMultiplier | 2 | Multiplier 1–20; final skill loss never exceeds 100%. |
| Restrictions.BlockHarvesting | true | Resource-location checks and fresh acquisition restrictions. |
| Restrictions.BlockItemUse | true | Item-tier restrictions worldwide. |
| Restrictions.BlockUnmappedItems | false | Strict option to deny unmapped items pending an explicit tier assignment. |
| Recovery.CapExtraLoss | false | Optional fixed-window extra-loss budget. |
| Recovery.WindowMinutes | 10 | Duration from the first penalized death. |
| Recovery.ExtraDeathBudget | 1 | Budget measured in the first death's extra skill-level loss. |
| Catalog.ItemTierOverrides | empty | Exact item/piece prefab assignments: `CustomOre=3;CustomSword=3`. |
| Catalog.BiomeTierOverrides | empty | Native biome enum overrides: `Ocean=2;DeepNorth=7`. |

To hold the group at Swamp even if somebody defeats Bonemass early, set `MaximumTier = 2`. To unlock a stage manually without its boss milestones, select `Mode = Manual` and the desired `ManualTier` (and ensure MaximumTier is high enough).

An item override of `-1` always allows that prefab. Native biome spellings include `Mountain` and `AshLands`; table labels above are player-facing names. Bad configurations are rejected and the last valid policy remains active.

## Death details

With a normal loss rate of 5% and multiplier 2, a level-50 skill becomes level 45. Every restricted death applies that multiplier, including recovery attempts, unless the optional cap is enabled. Partial progress toward the next level resets as in vanilla. Zero normal loss remains zero. A world's complete skill-reset or inventory-deletion rules remain in force; this mod does not override those world modifiers.

The optional cap limits only EXTRA loss. With its defaults, the first death costs 10%, and later deaths in the same ten-minute window still cost the normal 5%. The fixed per-skill budget and expiry are stored in the character's world-specific custom data; repeated deaths and reconnects do not replenish or extend it. The window uses elapsed real time, including time offline.

Graves remain recoverable. Restricted graves carry a persistent ZDO marker and do not grant Corpse Run even after progression later advances. When an existing normal Corpse Run is suppressed upon entry, its original expiration is retained; returning before expiration restores only the remaining time. Death cancels that saved buff. A restricted death does not grant a protection period after respawning in a safe biome.

## Resource and item restrictions

Mining, chopping, pickables, crop harvesting, honey collection and fishing pickups check the resource's biome. Standard dungeons use the saved entrance biome for player deaths. World-drop origin markers prevent moving a fresh drop across a border to bypass its original biome restriction. Advanced enemy loot, merchant purchases and native world-chest withdrawals check item tiers.

Item use is checked everywhere: equipment, attacks, ammunition selection, food and mead, crafting and upgrading, building, cooking, smelting, fermenting, planting, offerings and supported fuel interactions. Importing a character or receiving a gift does not unlock advanced gear. Invalid equipped gear is safely unequipped when policy changes. Already-consumed food/effects are allowed to expire naturally so a policy change does not abruptly remove health.

Recovering tombstones, transferring existing inventory, storing and dropping items remain available. Player-dropped items can be picked back up; their use is still restricted. A native world-chest bulk withdrawal containing a locked item is declined as a whole; allowed individual stacks can still be retrieved. Existing structures are not deleted.

The catalog seeds explicit material tiers and progression exceptions, then classifies crafted pieces/items and processing results using loaded game recipes. It includes the installed game's upgrade tokens. `rdp unmapped` exports remaining names (including NPC attacks, unused/developer items and custom content). Unknown items are allowed by default and reported, as agreed in the specification. A server requiring exhaustive denial should enable `BlockUnmappedItems` and assign tiers or `-1` exemptions for its custom content.

## Commands and compatibility

- `rdp status`: effective progression, switches and sync status.
- `rdp reload`: reload configuration from the SERVER/HOST console.
- `rdp item SwordSilver`: show a prefab's tier and eligibility.
- `rdp unmapped`: write the unmapped-prefab report beside the config.

RunicCrafting and RunicProduction are optional integrations. This mod guards their direct preparation methods because HarmonyX may execute their prefixes even when another prefix has already declined a vanilla action. Third-party mods performing their own direct inventory/skill mutations need integration testing. Other mods can call `RunicDeathPenalty.ProgressionApi.CanUseItem(prefab)` before committing resource use.

Server-owned policy and matching-version admission prevent ordinary client configuration bypass. They are not an anti-cheat system against deliberately altered clients; Valheim runs player skills and many interactions on the owning client. Use the server's existing character/admission controls where stronger enforcement is required.

See TESTING.md for actual verification and the remaining multiplayer acceptance pass. The package does not deploy itself or modify existing worlds.
