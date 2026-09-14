# Changelog

## 1.1.2 - 2026-09-14

- Fixed administrator connection recognition with ServerSync and Conditional Config Sync buffering sockets, including nested wrappers.
- Preserved native Steam and PlayFab identity verification without changing configuration-sync queues or permissions.
- Added specific diagnostics for unsupported socket wrappers and mismatched connections.

## 1.1.1 - 2026-09-14

- Fixed administrator setup rejecting Steam session-authenticated players when a connection certificate is unavailable.
- Kept authentication tied to the current connection, with session revocation and disconnect cleanup.
- Added specific connection-verification details to administrator error messages.

## 1.1.0 - 2026-09-12

- Added the authenticated server handler for full RunicSentinel 1.4.0's F3 Server Cap tab and RunicWorldEngine 1.2.x.
- Added running-cap, saved-cap, connected-player, and restart-status reporting.
- Added backed-up, stale-edit-protected saves for the cap override and 2–64 player limit, applied on the next restart without changing admission policy.
- Kept the server-only package free of client UI and admission-reporting code.

## 1.0.2 - 2026-09-10

- Added server-verified adminlist.txt access and first-time setup from the full Sentinel F3 panel.
- Added support for Steam raw, Steam_, and Valheim 1.0 V_ administrator ID formats.
- Preserved existing signed roles, bans, policies, and keys; setup leaves admission mode unchanged.
- Added an option to require signed Sentinel roles only and recheck access before cached panel responses.

## 1.0.1 - 2026-09-09

- Updated Required-mode admission for Valheim 1.0.7's invite-secret server handshake and reject a
  changed secret on the approved native resume.
- Moved the permanent fail-closed world gate to Valheim 1.0's common server-load boundary so both
  legacy and chunked saves are covered.
- Added complete chunked-world transition backups through Runic Safety 1.0.2's verified directory
  sources, with a hard 1,024-file ceiling.
- Updated the release dependency floor to BepInExPack Valheim 5.4.2350.

## 1.0.0 - 2026-09-09

- Added a distinct gold-and-ember authority icon with a fortified server-network badge.
- Added a distinct authority-only Sentinel package for dedicated servers and listen hosts.
- Reused the full Sentinel policy, admission, enforcement, identity, backup, reporting, and
  administrator-backend source at compile time without adding a shared runtime DLL.
- Compiled out client profile reporting, client challenge/decision handling, native client-handshake
  resume, the F3 administrator GUI, cursor control, and player-input patches.
- Added earliest-role preparation, exact authoritative-network lifetime tracking, and permanent
  Required-mode world-load and native-admission gates for authority-start failures; accidental
  player-only installations remain inert.
- Kept the v2 direct admission wire name compatible with Runic Sentinel Client and full Sentinel.
- Made the server-only and full authority packages mutually incompatible.
