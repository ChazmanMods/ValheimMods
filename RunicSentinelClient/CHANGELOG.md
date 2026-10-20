## 1.0.5

- Receive host-controlled building, terrain-raise and digging limits without full Sentinel on ordinary players' computers.
- Apply terrain and support limits, including existing excavations, and reset received settings when leaving the host.
- Defer to an active full Sentinel installation to avoid duplicate support scaling.
- Removed recipient administrator-action popups and synchronous action-generated HUD notices; normal gameplay notices remain active.
- Retained the transparent Client icon. No administration interface or host configuration is required on ordinary clients.

## 1.0.4

- Added server-authorized God, Ghost, Fly and No-cost actions and mode-status reporting.
- Added duration and supported intensity for status effects.
- Restored previous mode settings on disconnect or character change.
- Deferred character actions to the full Sentinel client when both packages are installed.

## 1.0.3

- Add selected-player actions through a bounded, exact-server/character RPC receiver: raise/reset skills, heal, clear food/status, adrenaline and registered status effects.
- Preserve affected-player cheat confirmation, reject replays and show acknowledged completion or failure.

## 1.0.2

- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.0.1 - 2026-09-09

- Updated the native handshake resume for Valheim 1.0.7's new invite-secret argument and capture
  that bounded value before the initial handshake is sent.
- Updated the release dependency floor to BepInExPack Valheim 5.4.2350.

## 1.0.0 - 2026-09-07

- Added a distinct cool-blue connected-player icon so the Client package is immediately
  distinguishable from Full and Server Sentinel profiles.
- Fixed Harmony discovery of the pre-admission connection hook, retained the exact connecting peer
  before `PeerInfo`, and removed the circular dependency on an already-ready server peer.
- Added bounded transport diagnostics for handler binding, challenge receipt, report delivery, and
  profile collection failure.
- Added a minimal client-only Runic Sentinel admission profile reporter.
- Added a bounded source-linked v2 challenge, profile report, and authoritative decision protocol.
- Added direct per-peer `ZRpc` transport with an explicit one-time native-handshake resume result.
- Retries a failed native-handshake resume at most three times and records completion only after the
  direct invoke succeeds.
- Kept the plugin inert on authoritative processes and made its direct handler coexist safely with
  full or legacy Runic Sentinel installations without relying on load-order suppression.
- Excluded server policy, administration, private keys, backups, enforcement, reports, and UI.


