## 1.2.0
- Integrated Server Devcommands 1.109 command and feature baseline into Sentinel; no separate Server Devcommands plugin is required.
- Added status-effect duration and intensity, command aliases/chains/waits, bindings, parameter autocomplete, command permissions and gameplay options.
- Preserved Sentinel backend administrator checks and connection-bound command requests; expanded chains authorize each executable command separately.
- Adapted Valheim 1.0 API changes; YAML configuration support is embedded in the shipped DLL.
- Command configuration files use RunicSentinel-prefixed names. Automatic devcommands is available but defaults off.

## 1.1.6

- Add selected-player actions through a bounded, exact-server/character RPC receiver: raise/reset skills, heal, clear food/status, adrenaline and registered status effects.
- Preserve affected-player cheat confirmation, reject replays and show acknowledged completion or failure.

## 1.1.5

- Repair the player roster transport with Valheim's bundled JSON serializer.
- Add authenticated native administrator/ban file changes, exact-peer kicking, file backups and role-source visibility.
- Add actual server/world metadata and recent enforcement findings.
- Generate distinct readable health/network reports and authenticated chunked downloads.

## 1.1.3

- Added server authorization for the F3 command dashboard, named server mod metadata, and administrator-only player reports.
- Added Server Devcommands as an installation dependency. Provider permissions remain required.
- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

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

- Added earliest-role preparation, exact authoritative-network lifetime tracking, and permanent
  Required-mode world-load and native-admission gates for authority-start failures; accidental
  player-only installations remain inert.
- Kept the v2 direct admission wire name compatible with Runic Sentinel Client and full Sentinel.
- Made the server-only and full authority packages mutually incompatible.

## Authority package boundary

- Compiled out client profile reporting and administrator GUI code; the dedicated package only contains authority-side handlers.
