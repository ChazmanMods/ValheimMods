# Changelog

## 1.0.1 - 2026-09-09

- Updated the native handshake resume for Valheim 1.0.7's new invite-secret argument and capture
  that bounded value before the initial handshake is sent.
- Added an installed-assembly contract test for the exact handshake and private field used by the
  responder.
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
