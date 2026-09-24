## 1.3.9

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.3.8 - 2026-09-14

- Fixed Use/equip removing your worn cape when the stand's Cape slot was empty. An empty stand slot now leaves your cape equipped.
- When only the stand has a cape, it moves to your inventory and equips. When both sides have a cape, they swap as before. Unequipped spare capes remain in your inventory.
## 1.3.7 - 2026-09-14

This update fixes armor that appeared on the stand in the world but was missing from its inventory panel.
The installed Valheim armor-stand prefab has fourteen internal attachment entries, including repeated
equipment roles. The old panel used that internal count as a single row's width, pushing some armor
slots outside the visible panel. Those internal entries are now presented as nine clearly named slots.

The equipment swap also needed to work with RunicInventory's protected armor row. Previously, outgoing
gear was removed before it was unequipped, and incoming equipment was equipped only after the transfer
had already committed. A failed equip could therefore leave transferred gear unworn. Ordinary empty-slot
searches also skipped protected equipment cells, even when those cells could hold the replacement.

The complete behavior in this release is:

- Show Helmet, Chest, Legs, Cape, and Utility on the top row, with Right hand, Left hand, Shield, and Weapon beneath them.
- Keep Right hand and Left hand items on the stand during **Use/equip**, so display items are not pulled into a loadout swap.
- Swap Weapon and Shield only when both the player's inventory and the corresponding stand slot contain one. Prefer equipped gear, then sheathed gear, then carried gear.
- Swap Utility when both sides have one; equip the stand's utility when the player has none; keep the player's utility equipped when the stand's slot is empty.
- Unequip outgoing gear before removal, reuse available protected equipment cells, and equip replacements inside the transfer transaction. Failed transfers or equips restore the original inventories and equipment state.
- Preserve named slot assignments when saving and reopening the stand, including the distinction between display-hand items and Weapon/Shield.
- Centered the four lower stand slots beneath the five upper slots. Icons, labels, and pointer targets move together, while saved roles remain unchanged.
- Restored normal grid alignment when the container panel is reused for another container.

## 1.3.6 - 2026-09-14

- Replaced the native attachment grid with nine named slots: Helmet, Chest, Legs, Cape, Utility, Right hand, Left hand, Shield, Weapon.
- Right hand and Left hand stay on the stand during Use/equip. Weapon and Shield swap only when both sides have that category.
- Utility swaps when both sides have one, equips from the stand when only the stand has one, and stays equipped on the player when the stand has none.
- Added protected-role lookup for receiving armor or utility into an empty RunicInventory equipment cell with full ordinary inventory.
- Saved role assignments keep display hands separate from loadout reserves. Existing attachments migrate by item type without discarding unrepresentable items.
- Named slot labels, compatible quick-transfer routing, and wrong-slot drop rejection keep panel placement explicit.

## 1.3.5 - 2026-09-14

- Fixed armor hidden outside the panel: Valheim's fourteen native stand slots now wrap within eight columns.
- Use/equip unequips outgoing gear before removal, reuses its freed inventory cells (including RunicInventory armor roles), and equips incoming items inside the transfer transaction.
- Equip rejection or insufficient space rolls back both inventories and restores exact drawn/sheathed equipment references.
- Swaps use currently equipped or sheathed weapons/shields instead of arbitrary hotbar items.
- Empty alternate visual attachment slots are cleared before occupied ones are rendered.

## 1.3.4 - 2026-09-11

- Fixed item transfers saving incomplete intermediate inventory states.
- Added ownership, access, item-data, and save-result checks before committing a move.
- Failed moves restore the original inventory items, positions, and metadata.
- Full inventories cancel equipment swaps without dropping or discarding equipment.
- Invalid equipment and oversized stacks are rejected without changing either inventory.
- Closing a stand no longer writes an outdated copy of its contents.
- Corrupt or mismatched saved item data no longer silently falls back to replacement item data.
- Take items into your inventory before dropping them on the ground.

## 1.3.3 - 2026-09-09

- Updated item-stand and armor-stand item identifiers to Valheim 1.0 integer hashes, with safe migration and removal of stale legacy string values.
- Updated inventory notification, peer configuration synchronization, compact-grid bounds, and vanilla Stack All restoration for Valheim 1.0.
## 1.3.2 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.3.1.

## 1.3.1

- Added `CHANGELOG.md` to the Thunderstore package so release history is available
  directly from the mod listing.

## 1.3.0

- Excluded the Forsaken altars at Spawn from container-style interactions, preserving
  Valheim's normal boss-trophy placement behavior.
- Excluded boss-offering holders, quest props, and other location-scripted item stands
  that reuse vanilla item-stand components but should retain their original interactions.
- Restricted armor stands to their intended loadout categories: one helmet, one chest,
  one pair of legs, one cape, one shield, and compatible hand/back-mounted equipment.
- Added safe rejection of incompatible items and duplicate equipment categories; rejected
  items are returned to the player or dropped safely when the inventory is full.
- Added Discord support, update, and bug-report information to the package documentation.
- Updated the package support link to the Chazman Mods Discord server.

## 1.2.4

- Added bounds protection for compact stand inventory grids, preventing out-of-range hover
  errors with inventory utility mods such as AzuAutoStore.

## 1.2.3

- Initial Thunderstore release.
- Added compact container-style interfaces for configured item stands and armor stands.
- Added single-item removal and armor-loadout swapping.
- Preserved item quality, durability, variants, crafter data, and modded custom item data.
- Added host synchronization for the managed stand-prefab configuration.
