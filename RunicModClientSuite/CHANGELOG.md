# Changelog

## 1.0.24 - 2026-09-14

- Updated RunicStorage from 1.0.6 to the in-game-confirmed, Thunderstore-approved 1.2.3 release.
- Adds remembered chest contents, exact-item and group rules, biome filters including Deep North, custom groups, exclusions and priorities for Quick Stack.
- Includes named-color chest labels with optional backgrounds, corrected face/lid placement, hover help, and movement/camera input blocking while Storage editors are open. Vanilla chest stacking remains accessible.
- Existing chest rules and label settings are preserved. All other dependency versions are unchanged.

## 1.0.23 - 2026-09-14

- Updated RunicDisplayStands to 1.3.8 because Use/equip could remove a worn cape when the stand's Cape slot was empty. Your cape now stays equipped in that case.
- When only the stand has a cape, Use/equip takes and equips it. When both sides have capes, they swap. Unequipped spare capes remain in your inventory.
- Retained RunicInventory 1.1.5 with Better Archery compatibility and RunicProduction 1.0.6.

## 1.0.22 - 2026-09-14

- Updated RunicInventory from 1.1.2 to 1.1.5 for compatibility with Better Archery's Thunderstore 1.9.99 build (reported as 1.9.9 in-game).
- This update is needed because Better Archery could resize the inventory after Runic added its equipment row, leaving items outside the grid and triggering automatic drops. The compatibility guards keep quiver ammunition and equipment in separate reserved rows and prevent conflicting slot checks.
- Includes the compact quiver layout, which removes unused panel space without changing inventory capacity, saved item positions, or keybindings. Better Archery remains optional; its 2.0.0 build is not covered by the adapter.
- Retained RunicProduction 1.0.6 and RunicDisplayStands 1.3.7. The matching Server Suite remains 1.0.16 because it does not include RunicInventory.

## 1.0.21 - 2026-09-14

- Updated RunicProduction to 1.0.6 because repeated mouse-press signals could immediately cancel a newly selected production link. Linking now keeps each accepted click captured until release while preserving deliberate cancellation and unlinking.
- Updated RunicDisplayStands to 1.3.7 because the native stand's fourteen internal attachment entries pushed armor outside the visible panel. Nine named slots now show Helmet, Chest, Legs, Cape, and Utility above a centered row of Right hand, Left hand, Shield, and Weapon.
- The display-stand update also fixes Use/equip with RunicInventory's protected equipment row: outgoing gear is unequipped before transfer, replacements are equipped as part of the swap, and failures restore inventory and equipment state.
- Display-hand items stay on the stand. Weapon and Shield swap only when both sides have that category. Utility swaps when available on both sides, equips from the stand when absent from the player, and remains equipped when the stand has none.
- Aligned these shared dependencies across Full, Client, and Server suites. Update participating clients and servers together and restart them after updating.

## 1.0.20 - 2026-09-12

- Updated RunicInventory to 1.1.2 so locked ammunition, tools, weapons, and consumables remain usable while retaining Quick Stack protection. Matching pickups can replenish locked stacks.
- Updated RunicSafety to 1.0.5 for cooking, refueling, smelting, and fermenting with locked supplies while preserving its other item safeguards.

## 1.0.19 - 2026-09-12

- Updated RunicCrafting to 1.1.0 with corrected nearby-material checks for building and optional hotkey area repair for structures, while retaining the crafting-menu performance improvements.
- Updated RunicWorldEngine to 1.2.0 with expanded network-health diagnostics and configurable player-cap support for authoritative hosts.
- Retained RunicSentinelClient 1.0.1 for lightweight admission; server administration remains a separate install for administrators.

## 1.0.18 - 2026-09-12

- Updated RunicCrafting to 1.0.8 with cached chest material counts, shared menu-refresh queries, and short-lived availability caching for crafting and building previews.
- Added RunicClock 1.0.1 with game time, world day, sun/moon indicator, optional local time, and the matching Runic Suite icon.

## 1.0.17 - 2026-09-11

- Updated RunicPortals to 1.2.4 with Accept/Decline group-invitation popups.

## 1.0.16 - 2026-09-11

- Updated RunicBuildCamera to 1.0.3, fixing hammer unequip and hotbar switching while the detached camera is active.

## 1.0.15 - 2026-09-11

- Updated RunicDisplayStands to 1.3.4 for verified transfers, failed-move rollback, and safer loadout swaps.

## 1.0.14 - 2026-09-11

- Updated Interaction, Inventory, Portals, Precision Build Tool, Production, and Safety dependencies for the Valheim 1.0.12 compatibility fixes.

## 1.0.13 - 2026-09-10

- Updated RunicInventory to 1.1.0 with an automatic additional equipment and quick-use row, restricted slot types, and safe row migration.

## 1.0.12 - 2026-09-10

- Updated RunicInventory to 1.0.5 and RunicSafety to 1.0.3 for slot-lock and protection-provider fixes.

## 1.0.11 - 2026-09-10

- Updated RunicProduction to 1.0.4 so permitted players can configure production without already owning the station and chest's network state.

## 1.0.10 - 2026-09-10

- Updated RunicInteraction to 1.0.3 and RunicInventory to 1.0.4 for Linux startup compatibility.

## 1.0.9 - 2026-09-10

- Updated RunicCrafting to 1.0.6 for manual cooking and refueling from nearby chests.

## 1.0.8 - 2026-09-10

- Updated RunicPortals to 1.2.2 for signed character-ID support in groups and clearer group-request errors.

## 1.0.7 - 2026-09-10

- Updated RunicCrafting to 1.0.5.
- Updated RunicStorage to 1.0.6.
- Updated RunicAgriculture to 1.0.3.
- Updated RunicProduction to 1.0.3.

## 1.0.6 - 2026-09-10

- Updated RunicStorage to 1.0.5 to fix durability-rounding transfer failures and improve Quick Stack diagnostics without bypassing chest permissions.

## 1.0.5 - 2026-09-10

- Updated Character Vault to 1.0.2 for existing-character enrollment, permanent initial backups, and no starter-item grants on imported characters.
- Corrected first-join instructions so established players retain their existing characters.

## 1.0.4 - 2026-09-10

- Updated Configuration Manager to 1.1.17; Thunderstore installs its declared Conditional Config
  Sync dependency automatically.
- Updated Runic Character Vault to 1.0.1 so its in-game save status uses Valheim's live font
  template and no longer triggers Unity 6's removed LiberationSans default-font error.
- Updated Runic Crafting to 1.0.3 and Runic Storage to 1.0.4 for guarded native container
  ownership, multiplayer-safe busy-chest exclusion, and synchronized read-only chest contents.

## 1.0.3 - 2026-09-09

- Added Runic Character Vault 1.0.0 so clients can exchange validated authoritative character saves with configured servers.
- Expanded the player-facing Valheim 1.0 dependency set to 17 Runic packages.

## 1.0.2 - 2026-09-09

- Updated every player-facing Runic package for Valheim 1.0 and BepInExPack Valheim 5.4.2350.
- Updated Runic Sentinel Client to 1.0.1 for Valheim 1.0's admission handshake.
- Revalidated all 16 exact player-facing dependency pins and retained Configuration Manager 1.1.16.

## 1.0.1 - 2026-09-09

- Added a distinct cool-blue connected-player icon for quick client-profile recognition.
- Updated Runic Inventory to 1.0.2, including the corrected item-protection availability path used
  by cooking, crafting, storage, and interaction consumers.
- Updated Runic Portals to 1.2.0 with its point-and-click portal configuration interface.
- Revalidated all 16 player-facing Runic dependency pins while retaining the lightweight Runic
  Sentinel Client 1.0.0 and Configuration Manager 1.1.16.

## 1.0.0 - 2026-09-07

- Created the player/client edition of the Runic Mod Suite.
- Included all 16 player-facing Runic packages at their current release versions.
- Included Configuration Manager 1.1.16 for convenient in-game settings.
- Included Runic Sentinel Client for admission reporting while deliberately excluding the full
  Runic Sentinel server-administration package.
