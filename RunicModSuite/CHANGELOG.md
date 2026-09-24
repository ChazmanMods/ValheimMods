## 1.2.44

- Updated included mods for language-file support and the September 24 fixes.

# Changelog

## 1.2.43 - 2026-09-20

- Updated RunicProduction to 1.0.12 to fix fermenter content storage on current Valheim.
- Updated RunicInteraction to 1.0.9: auto-close only affects player-built doors, excluding generated dungeon, cave, and ruin doors.
- Existing stranded fermenter batches may need recovery; the update does not automatically recover them.
- All other dependency versions and the client/server module split are retained.

## 1.2.42 - 2026-09-19

- Updated dependencies: RunicCrafting 1.1.3, RunicProduction 1.0.11, RunicSigns 1.2.6, RunicStorage 1.3.4.
- RunicStorage adds configurable ItemDrawers Quick Stack/restocking, the spawned-item deposit fix, and curved container labels with fine +/- bend controls.
- RunicCrafting adds independent configurable custom-container sources for crafting and building.
- RunicProduction adds independent custom-container support while retaining beehive collection, cooking XP, station sounds, and existing production workflows.
- RunicSigns adds finer bending with +/- controls.
- These component versions were confirmed working in-game by the author.
- Preserved the suite module split and Sentinel roles. Update participating clients and servers together and restart after updating.

## 1.2.41 - 2026-09-18

- Updated dependencies: RunicAwareness 1.0.3, RunicInteraction 1.0.8, RunicInventory 1.1.9, RunicPortals 1.2.9, RunicSafety 1.0.8, RunicSigns 1.2.5, RunicWorldEngine 1.2.2.
- Includes the latest Valheim 1.0.15 compatibility updates and live sign designer.
- Preserved the existing client/server module split and Sentinel roles.

## 1.2.40 - 2026-09-17

- Updated RunicWorldEngine to 1.2.1 to restore the optional player-cap override on Valheim 1.0.14.
- All other dependency versions are unchanged.

## 1.2.39 - 2026-09-17

- Updated RunicBuildCamera to 1.0.4 for activation focus recovery, visible toggle feedback and camera failure logging.
- Preserves all other dependency versions, including the Valheim 1.0.14 fixes.

## 1.2.38 - 2026-09-17

- Updated Valheim 1.0.14 compatibility fixes: Chazman-RunicInteraction-1.0.6, Chazman-RunicInventory-1.1.7, Chazman-RunicPortals-1.2.7, Chazman-RunicSafety-1.0.6.
- Preserved emoji, precision rotation and other existing dependency versions.

## 1.2.37 - 2026-09-17

- Updated to Chazman-RunicPrecisionBuildTool-2.0.6.
- Updated to Chazman-RunicSigns-1.1.0.
- Updated to Chazman-RunicStorage-1.3.0.
- Adds full-color emoji pickers to signs and chest labels, preserving sign placement sizing and Quick Stack-only slot protection.
- Adds the F4 Local/World rotation toggle and matching axis guides.
- Preserves all other dependency versions, including RunicProduction 1.0.9.

## 1.2.36 - 2026-09-16

- Updated RunicProduction to 1.0.9 for native automated station sounds: kiln/smelter loading and output, cooking/oven loading and collection, fermenter filling and tapping, and crafting effects.
- Preserves existing fire, torch, and lamp refueling sounds without duplicating them. Effects do not replay completed item transfers.
- Update participating clients and servers together and restart after updating. Existing links and configuration are retained.

## 1.2.35 - 2026-09-15

- Updated to Chazman-RunicAgriculture-1.0.4.
- Updated to Chazman-RunicCrafting-1.1.1.
- Updated to Chazman-RunicInteraction-1.0.5.
- Updated to Chazman-RunicInventory-1.1.6.
- Updated to Chazman-RunicPortals-1.2.6.
- Updated to Chazman-RunicProduction-1.0.7.
- Updated to Chazman-RunicSentinel-1.4.2.
- Added Chazman-RunicSigns-1.0.4.
- Updated to Chazman-RunicStorage-1.2.5.
- Corrected dedicated-server, player, and listen-host installation guidance to prevent the full Sentinel / SentinelServer conflict that disables F3.
- Preserved dependency-only packaging and existing configuration.

## 1.2.34 - 2026-09-14

- Updated RunicStorage from 1.0.6 to the in-game-confirmed, Thunderstore-approved 1.2.3 release.
- Adds remembered chest contents, exact-item and group rules, biome filters including Deep North, custom groups, exclusions and priorities for Quick Stack.
- Includes named-color chest labels with optional backgrounds, corrected face/lid placement, hover help, and movement/camera input blocking while Storage editors are open. Vanilla chest stacking remains accessible.
- Existing chest rules and label settings are preserved. All other dependency versions are unchanged.

## 1.2.33 - 2026-09-14

- Updated RunicDisplayStands to 1.3.8 because Use/equip could remove a worn cape when the stand's Cape slot was empty. Your cape now stays equipped in that case.
- When only the stand has a cape, Use/equip takes and equips it. When both sides have capes, they swap. Unequipped spare capes remain in your inventory.
- Retained RunicInventory 1.1.5 with Better Archery compatibility and RunicProduction 1.0.6.

## 1.2.32 - 2026-09-14

- Updated RunicInventory from 1.1.2 to 1.1.5 for compatibility with Better Archery's Thunderstore 1.9.99 build (reported as 1.9.9 in-game).
- This update is needed because Better Archery could resize the inventory after Runic added its equipment row, leaving items outside the grid and triggering automatic drops. The compatibility guards keep quiver ammunition and equipment in separate reserved rows and prevent conflicting slot checks.
- Includes the compact quiver layout, which removes unused panel space without changing inventory capacity, saved item positions, or keybindings. Better Archery remains optional; its 2.0.0 build is not covered by the adapter.
- Retained RunicProduction 1.0.6 and RunicDisplayStands 1.3.7. The matching Server Suite remains 1.0.16 because it does not include RunicInventory.

## 1.2.31 - 2026-09-14

- Updated RunicProduction to 1.0.6 because repeated mouse-press signals could immediately cancel a newly selected production link. Linking now keeps each accepted click captured until release while preserving deliberate cancellation and unlinking.
- Updated RunicDisplayStands to 1.3.7 because the native stand's fourteen internal attachment entries pushed armor outside the visible panel. Nine named slots now show Helmet, Chest, Legs, Cape, and Utility above a centered row of Right hand, Left hand, Shield, and Weapon.
- The display-stand update also fixes Use/equip with RunicInventory's protected equipment row: outgoing gear is unequipped before transfer, replacements are equipped as part of the swap, and failures restore inventory and equipment state.
- Display-hand items stay on the stand. Weapon and Shield swap only when both sides have that category. Utility swaps when available on both sides, equips from the stand when absent from the player, and remains equipped when the stand has none.
- Aligned these shared dependencies across Full, Client, and Server suites. Update participating clients and servers together and restart them after updating.

## 1.2.30 - 2026-09-12

- Updated RunicInventory to 1.1.2 so locked ammunition, tools, weapons, and consumables remain usable while retaining Quick Stack protection. Matching pickups can replenish locked stacks.
- Updated RunicSafety to 1.0.5 for cooking, refueling, smelting, and fermenting with locked supplies while preserving its other item safeguards.

## 1.2.29 - 2026-09-12

- Updated RunicCrafting to 1.1.0 with corrected nearby-material checks for building and optional hotkey area repair for structures, while retaining the crafting-menu performance improvements.
- Updated RunicWorldEngine to 1.2.0 with a configurable host player cap, validated hosting limits, and expanded network-health diagnostics.
- Updated RunicSentinel to 1.4.0 with the authenticated F3 Server Cap tab for viewing and configuring the host's player limit.

## 1.2.28 - 2026-09-12

- Updated RunicCrafting to 1.0.8 with cached chest material counts, shared menu-refresh queries, and short-lived availability caching for crafting and building previews.
- Added RunicClock 1.0.1 with game time, world day, sun/moon indicator, optional local time, and the matching Runic Suite icon.

## 1.2.27 - 2026-09-11

- Updated RunicPortals to 1.2.4 with Accept/Decline group-invitation popups.

## 1.2.26 - 2026-09-11

- Updated RunicBuildCamera to 1.0.3, fixing hammer unequip and hotbar switching while the detached camera is active.

## 1.2.25 - 2026-09-11

- Updated RunicDisplayStands to 1.3.4 for verified transfers, failed-move rollback, and safer loadout swaps.

## 1.2.24 - 2026-09-11

- Updated Interaction, Inventory, Portals, Precision Build Tool, Production, and Safety dependencies for the Valheim 1.0.12 compatibility fixes.

## 1.2.23 - 2026-09-10

- Updated RunicInventory to 1.1.0 with an automatic additional equipment and quick-use row, restricted slot types, and safe row migration.

## 1.2.22 - 2026-09-10

- Updated RunicInventory to 1.0.5 and RunicSafety to 1.0.3 for slot-lock and protection-provider fixes.

## 1.2.21 - 2026-09-10

- Updated RunicProduction to 1.0.4 so permitted players can configure production without already owning the station and chest's network state.

## 1.2.20 - 2026-09-10

- Updated RunicInteraction to 1.0.3 and RunicInventory to 1.0.4 for Linux startup compatibility.

## 1.2.19 - 2026-09-10

- Updated RunicCrafting to 1.0.6 for manual cooking and refueling from nearby chests.

## 1.2.18 - 2026-09-10

- Updated RunicPortals to 1.2.2 for signed character-ID support in groups and clearer group-request errors.

## 1.2.17 - 2026-09-10

- Updated RunicSentinel to 1.3.2 for server-admin recognition and one-click F3 setup.

## 1.2.16 - 2026-09-10

- Updated RunicCrafting to 1.0.5.
- Updated RunicStorage to 1.0.6.
- Updated RunicAgriculture to 1.0.3.
- Updated RunicProduction to 1.0.3.

## 1.2.15 - 2026-09-10

- Updated RunicStorage to 1.0.5 to fix durability-rounding transfer failures and improve Quick Stack diagnostics without bypassing chest permissions.

## 1.2.14 - 2026-09-10

- Updated Character Vault to 1.0.2 for existing-character enrollment, permanent initial backups, and no starter-item grants on imported characters.
- Corrected first-join instructions so established players retain their existing characters.

## 1.2.13 - 2026-09-10

- Updated Configuration Manager to 1.1.17; Thunderstore installs its declared Conditional Config
  Sync dependency automatically.
- Updated Runic Character Vault to 1.0.1 so its in-game save status uses Valheim's live font
  template and no longer triggers Unity 6's removed LiberationSans default-font error.
- Updated Runic Crafting to 1.0.3 and Runic Storage to 1.0.4 for guarded native container
  ownership, multiplayer-safe busy-chest exclusion, and synchronized read-only chest contents.

## 1.2.12 - 2026-09-09

- Added Runic Character Vault 1.0.0 for validated server-authoritative character storage, durable saves, and rolling backups.
- Expanded the complete Valheim 1.0 suite to 17 canonical Runic mods.
- Updated the split Client and Server Suite guidance for vault deployment on both sides of a connection.

## 1.2.11 - 2026-09-09

- Updated every bundled Runic package for Valheim 1.0 and BepInExPack Valheim 5.4.2350.
- Updated the full Sentinel package to 1.3.1, including the Valheim 1.0 admission handshake and chunked-world backup path.

## 1.2.10 - 2026-09-09

- Updated Runic Inventory to 1.0.2, including the corrected item-protection availability path used
  by cooking, crafting, storage, and interaction consumers.
- Updated Runic Portals to 1.2.0 with its point-and-click portal configuration interface.
- Updated the combined Runic Sentinel package to 1.3.0.

## 1.2.9 - 2026-09-07

- Updated all 15 remaining Runic dependencies to their current Thunderstore releases, bringing
  every bundled Runic mod up to date. Runic Production remains current at 1.0.1.
- Updated Runic Agriculture, Awareness, Build Camera, Crafting, Exploration, Interaction,
  Inventory, Safety, and Velocity to 1.0.1.
- Updated Runic Display Stands to 1.3.2, Runic Portals to 1.1.4, Runic Precision Build Tool to
  2.0.2, Runic Sentinel to 1.2.5, Runic Storage to 1.0.2, and Runic World Engine to 1.1.1.
- These dependency updates refresh player-facing package descriptions and READMEs; their plugin
  DLLs and gameplay behavior are unchanged from the versions pinned by 1.2.8.

## 1.2.8 - 2026-09-04

- Updated Runic Production to 1.0.1, preventing one physical Alt+mouse press from immediately
  selecting and cancelling the same production link before a chest can be chosen.
- Reworked the suite description and README to lead with player-facing benefits, in-game workflows,
  and the Runic philosophy while retaining safety, compatibility, and architecture details.

## 1.2.7 - 2026-09-01

- Updated Runic Storage to 1.0.1 so the first Alt+Q after world entry automatically waits for
  Valheim's nearby chest index instead of falsely reporting that no authorized chest exists.

## 1.2.6 - 2026-09-01

- Updated Runic Sentinel to 1.2.4 so the F3 administrator panel owns the cursor and suppresses
  attacks, placement, movement, and build input while it is open.

## 1.2.5 - 2026-09-01

- Updated Runic Sentinel to 1.2.3 with a readable Valheim-style carved-wood F3 administrator
  panel modeled after the native F2 connection panel.

## 1.2.4 - 2026-09-01

- Updated Runic Portals to 1.1.3 so first-use dedicated-server ward loading is retried without
  weakening genuine source-ward denials.

## 1.2.3 - 2026-09-01

- Updated Runic Portals to 1.1.2 so an outdated client receives a clear compatibility rejection
  without Sentinel misclassifying the legacy wire format or connection-time identity handoff as
  cheating.

## 1.2.2 - 2026-09-01

- Updated Runic Sentinel to 1.2.2 so RSA-3072 bootstrap works under the dedicated server's
  Unity/Mono cryptography provider.

## 1.2.1 - 2026-09-01

- Updated Runic Portals to 1.1.1 with reliable first-use dedicated-server routing and an
  authoritative world-wide P map directory.
- Updated Runic Sentinel to 1.2.1 with typeable bounded commands in the real batch-mode dedicated
  server console.

## 1.2.0 - 2026-08-31

- Updated Runic Sentinel to 1.2.0 with the authenticated F3 server-administrator panel, managed
  signed policy workflow, automatic enforcement controls, backups, reports, and network maps.

## 1.1.0 - 2026-08-31

- Added Raven's Gate signed admission, signed roles and bans, automatic enforcement, runtime
  integrity monitoring, a bounded persistent flight recorder, transition backups, bounded reports,
  and administrator topology snapshots.
- Added safe bounded asynchronous world-save smoothing through Runic World Engine 1.1.0.

## 1.0.0

- Initial Runic Mod Suite modpack release.
- Added exact dependencies for all 16 public Runic packages.
- Added shudnal Configuration Manager 1.1.16 for in-game configuration access.
- Kept the package dependency-only so it does not duplicate DLLs or overwrite configuration files.
