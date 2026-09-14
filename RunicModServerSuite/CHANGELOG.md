# Changelog

## 1.0.16 - 2026-09-14

- Updated RunicProduction to 1.0.6 because repeated mouse-press signals could immediately cancel a newly selected production link. Linking now keeps each accepted click captured until release while preserving deliberate cancellation and unlinking.
- Updated RunicDisplayStands to 1.3.7 because the native stand's fourteen internal attachment entries pushed armor outside the visible panel. Nine named slots now show Helmet, Chest, Legs, Cape, and Utility above a centered row of Right hand, Left hand, Shield, and Weapon.
- The display-stand update also fixes Use/equip with RunicInventory's protected equipment row: outgoing gear is unequipped before transfer, replacements are equipped as part of the swap, and failures restore inventory and equipment state.
- Display-hand items stay on the stand. Weapon and Shield swap only when both sides have that category. Utility swaps when available on both sides, equips from the stand when absent from the player, and remains equipped when the stand has none.
- Aligned these shared dependencies across Full, Client, and Server suites. Update participating clients and servers together and restart them after updating.

## 1.0.15 - 2026-09-12

- Updated the RunicSafety dependency to 1.0.5.

## 1.0.14 - 2026-09-12

- Updated RunicWorldEngine to 1.2.0 with a configurable host player cap, validated hosting limits, and expanded network-health diagnostics.
- Updated RunicSentinelServer to 1.1.0 with authenticated server-cap administration for the remote F3 panel in RunicSentinel 1.4.0.
- Player-cap changes are saved for the next server restart; existing settings are preserved.

## 1.0.13 - 2026-09-11

- Updated RunicPortals to 1.2.4 with server-validated group-invitation acceptance and decline.

## 1.0.12 - 2026-09-11

- Updated RunicDisplayStands to 1.3.4 for verified transfers, failed-move rollback, and safer loadout swaps.

## 1.0.11 - 2026-09-11

- Updated Portals, Production, and Safety dependencies for the Valheim 1.0.12 compatibility fixes.

## 1.0.10 - 2026-09-10

- Updated RunicSafety to 1.0.3 for optional protection-provider discovery.

## 1.0.9 - 2026-09-10

- Updated RunicProduction to 1.0.4 so permitted players can configure production without already owning the station and chest's network state.

## 1.0.8 - 2026-09-10

- Updated RunicPortals to 1.2.2 for signed character-ID support in groups and clearer group-request errors.

## 1.0.7 - 2026-09-10

- Updated RunicSentinelServer to 1.0.2 for server-admin recognition and one-click setup from an administrator's full Sentinel client.

## 1.0.6 - 2026-09-10

- Updated RunicProduction to 1.0.3.

## 1.0.5 - 2026-09-10

- Updated Character Vault to 1.0.2 for configurable existing-character enrollment and permanent initial backups.
- Documented first-enrollment trust and the option to require fresh characters after migration.

## 1.0.4 - 2026-09-10

- Updated Runic Character Vault to 1.0.1, keeping the dedicated server and connecting clients on
  the same corrected vault protocol package.

## 1.0.3 - 2026-09-09

- Added Runic Character Vault 1.0.0 for server-authoritative player characters and rolling server backups.
- Expanded the authoritative Valheim 1.0 dependency set to eight Runic packages.

## 1.0.2 - 2026-09-09

- Updated all seven server-relevant Runic packages for Valheim 1.0 and BepInExPack Valheim 5.4.2350.
- Updated Runic Sentinel Server to 1.0.1 for Valheim 1.0 admission and chunked-world backup support.
- Revalidated the exact authoritative dependency set while retaining the client/server role boundary.

## 1.0.1 - 2026-09-09

- Added a distinct gold-and-ember authority icon with a fortified server-network badge.
- Updated Runic Portals to 1.2.0 with the latest server-side portal routing and authorization
  support.
- Replaced the combined Runic Sentinel dependency with Runic Sentinel Server 1.0.0, keeping
  administration, enforcement, policy, and private-key services on the authoritative process.
- Revalidated all seven exact server-relevant dependency pins and continued to omit Configuration
  Manager from headless installations.

## 1.0.0 - 2026-09-07

- Created the server/operator edition of the Runic Mod Suite.
- Included the seven Runic packages with useful authoritative, host-synchronized, automation,
  security, startup-diagnostic, or world-save behavior on a dedicated or listen server.
- Kept client-only camera, UI, input, inventory, building, crafting, exploration, interaction, and
  storage tools out of the dedicated-server dependency set.
- Kept Configuration Manager in the Client Suite because its in-game UI is not needed by a
  headless server.
