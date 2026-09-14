# RunicDeathPenalty — feature specification

Status: implemented as the RunicDeathPenalty 0.1.0 testing release. See ../RunicDeathPenalty/README.md and TESTING.md for the shipped behavior and verified limits; no production deployment has been performed.

## Purpose and progression

Keep a multiplayer group on its agreed biome progression. Enhanced death rules begin ONE biome beyond the current allowed biome. Players may still enter restricted biomes.

| Latest sequential boss defeated | Allowed through | Restricted from |
| --- | --- | --- |
| None | Meadows | Black Forest |
| Eikthyr | Black Forest | Swamp |
| The Elder | Swamp | Mountains |
| Bonemass | Mountains | Plains |
| Moder | Plains | Mistlands |
| Yagluth | Mistlands | Ashlands |
| The Queen | Ashlands | Deep North |
| Fader | Deep North | Explicitly configured future/custom tiers |

Read world progression, not individual character boss history. Automatic progression advances only through contiguous boss milestones, so an out-of-order kill cannot skip a missing milestone. The installed runtime confirmed Deep North recipes/equipment and Fader's actual defeat key, so the implementation includes Deep North as tier 7.

Support automatic progression and an administrator-selected maximum biome. The effective allowed biome is the lower of automatic progression and the configured cap. A cap does not automatically unlock an undefeated stage. An explicit manual progression mode may select an allowed biome independently for events or custom worlds. Only trusted server configuration controls these values.

Default allowed biome lead: zero. A configurable lead permits administrators to relax the rule later without changing the boss table.

## Death and recovery

- Classify the death by the player's authoritative death location and the progression policy at that moment.
- In allowed biomes, preserve normal death and recovery behavior, including the world's normal skill-loss setting.
- In restricted biomes, bypass existing No skill drain protection, grant no new protection, and apply the configured multiplier to the normal skill-level loss rate exactly once.
- Default proposed multiplier: 2.0. Clamp the resulting loss fraction to 0–100%; never produce negative skill levels. A zero base rate remains zero. Preserve vanilla handling of partial progress rather than accidentally multiplying it twice.
- Mark restricted-death tombstones with persistent metadata so recovering them never grants Corpse Run. Preserve inventory, ownership, and normal retrieval access. Classification survives logout and server restart; moving the grave or later unlocking the biome does not change its historical marker.
- Suppress existing Corpse Run and No skill drain benefits while the player is in a restricted biome, including buffs obtained elsewhere. Preserve their original expiration; moving across a border must not refresh them. Returning to an allowed biome restores only any genuinely remaining normal protection.
- Restricted deaths with no tombstone still receive the death policy.
- Explain the applied multiplier and protection changes in a concise death message.

### Optional recovery safeguard

Cap only the EXTRA skill loss above the normal death loss during a fixed recovery window. Ordinary loss still applies on each restricted death; the safeguard does not restore No skill drain or Corpse Run.

Proposed defaults: safeguard disabled; when enabled, a 10-minute window and an extra-loss budget equivalent to one enhanced death. Snapshot each skill's baseline at window start; count actual extra skill levels removed against its budget. Do not extend/reset the window on another death, tombstone pickup, biome crossing, or reconnect. Persist the window and budget by world and character. A multiplier of 1 produces no extra-loss budget consumption. Server administrators can configure the window and budget.

## Warnings and location rules

- On entry into a restricted biome, show its name, the current allowed biome, the death multiplier, disabled protections, and active resource restrictions.
- Warn once per entry with a short cooldown to prevent border spam. Reevaluate when progression/configuration changes while the player remains inside.
- Use the resource position for harvesting restrictions, so standing in an allowed biome while striking a restricted resource cannot bypass the policy.
- Use dungeon entrance/surface biome metadata for interiors, not the interior's artificial height or default biome. Capture and persist entrance context across reconnects. Unknown/custom interiors need explicit mappings and a visible diagnostic.
- Ocean is a separate policy: default exempt from biome-based penalties and harvesting restrictions. Offer an explicit required progression tier for administrators who want to restrict ocean travel/resources. Do not arbitrarily treat Ocean as later than Ashlands. A shoreline resource still uses its actual resource biome.
- Custom biomes and future areas require administrator tier mappings; unknown mappings are reported rather than silently assigned a progression rank.

## Optional harvesting and item-use restrictions

Provide independent server switches for restricted-biome harvesting and above-progression item use. Proposed defaults: both enabled for this server's intended setup; either can be disabled independently.

### Harvesting

When enabled, block resource acquisition in restricted biomes, including mining, tree chopping, gathering pickables, harvesting crops, and collecting world resource drops. Cover normal interactions and relevant authoritative damage/drop paths. Environmental or creature destruction must not turn a blocked node into collectible unrestricted resources.

Players may fight and move through the biome. Enemy kills are permitted; collection/use of advanced loot is restricted by item tier. Chest and merchant acquisitions of advanced items must follow the same acquisition policy, including mod-assisted transfers where supported. Low-tier supplies stored in a chest are not inherently advanced because the chest is in a restricted biome.

Always permit recovering one's tombstone, even when it contains restricted items. Permit carrying, storing, transferring, and dropping existing items so inventories are not trapped; tier restrictions continue to follow those items. Block fresh world acquisition as appropriate without deleting drops or inventory contents.

### Item use everywhere

Determine eligibility from the item's required progression tier, not where it was acquired or where the player currently stands. Receiving silver or advanced equipment from another player, importing a character, or moving materials to Meadows does not unlock their use.

Block restricted items from:

- Equipping and using weapons, armor, shields, tools, and utility equipment.
- Consuming food, mead, potions, and ammunition, including automatic ammunition selection.
- Crafting and upgrading, checking both ingredients and the resulting item.
- Building with restricted materials or placing restricted-tier pieces.
- Cooking, smelting, fermenting, and other processing, checking inputs and outputs.
- Planting restricted seeds/crops and making item offerings that use restricted resources.

Revalidate equipped items and active use on login, progression changes, and configuration changes. Unequip invalid equipment safely without deleting or dropping it. Prevent further restricted ammunition/tool use. Do not abruptly remove already-consumed food health in a way that kills a player; an implementation must define and test a safe transition for existing timed effects. Existing structures remain intact; blocking already-built station interaction should be based on the attempted restricted operation.

Maintain an explicit vanilla item/piece tier catalog, with exact-prefab overrides for custom content and administrative allowlists. Recipe relationships can assist catalog validation but must not be the sole classifier: multi-biome drops, boss rewards, and progression-enabling items need explicit handling. Items legally needed to fight the current boss must remain usable. Shared resources should use their earliest intended availability tier. The catalog must cover advanced finished equipment, foods, and ammunition as well as raw materials.

Unknown modded items require a configurable policy and clear diagnostics. Proposed default: allow unknown items and log their unmapped prefab names once; strict servers can block unknown items until mapped. This limitation must be visible to administrators.

Denied actions show a throttled reason such as: "Silver requires Mountains progression. Defeat Bonemass or ask the administrator to advance the progression cap." Adapt the message when a cap, rather than a boss, is blocking progression.

## Server policy and compatibility

- Install on server and clients; synchronize server-owned settings and effective progression. Clients cannot override them through local configuration.
- Require a matching compatible mod/protocol at connection. Do not claim that client mod checks alone provide protection against deliberately modified clients.
- Validate client-originated policy/state messages and ownership. Persist tombstone markers and recovery budgets with the correct world/character identities.
- Provide admin configuration reload, a current-policy/status command, item-tier diagnostics, and concise audit logging of applied penalties and restricted actions.
- Check compatibility with world death modifiers, death/skill mods, RunicInventory, RunicSafety, RunicCharacterVault, and crafting/storage/production mods that perform direct or remote item consumption.
- Publish supported integrations and unverified bypass paths. Do not claim complete enforcement until multiplayer and direct-operation paths have been exercised.

## Acceptance checks

1. Elder defeated, Bonemass alive: Swamp death is normal; Mountains and Plains deaths are enhanced.
2. Bonemass defeated: Mountains becomes normal; Plains remains restricted.
3. Administrator cap keeps the group at Swamp even after Bonemass is defeated.
4. Existing protection from a normal death cannot shield a restricted death or combat in a restricted biome.
5. A marked tombstone grants no Corpse Run after reconnect, restart, movement, or progression advancement; ordinary tombstones retain normal behavior.
6. Recovery safeguard caps only extra loss and cannot be reset through reconnect or repeated pickups.
7. Dungeon and coastal classifications are correct; a border cannot bypass mining restrictions.
8. Imported/gifted silver, silver equipment, advanced food, and ammunition remain unusable in Meadows until unlocked.
9. Crafting, building, processing, farming, automatic ammunition, and supported remote-storage consumption enforce the same tier policy.
10. Tombstone recovery and storage never destroy, duplicate, or trap items; normal gameplay resumes when progression unlocks them.
11. Two clients receive the same server policy; local edits and forged progression messages cannot advance it.
12. All independent switches, multiplier bounds, zero base death loss, unknown-prefab policies, progression changes, and mod coexistence are covered before release.

## Delivery scope

This is larger than a death-only mod: it combines death behavior, persistent recovery state, server progression policy, a maintained resource catalog, and restrictions across multiple gameplay systems. Implement and verify those as distinct layers, then run a dedicated-server/two-client acceptance pass before packaging a release. No production deployment is part of this specification.
