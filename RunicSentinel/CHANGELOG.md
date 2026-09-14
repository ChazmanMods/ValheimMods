# Changelog

## 1.4.2 - 2026-09-14

- Fixed administrator connection recognition with ServerSync and Conditional Config Sync buffering sockets, including nested wrappers.
- Preserved native Steam and PlayFab identity verification without changing configuration-sync queues or permissions.
- Added specific diagnostics for unsupported socket wrappers and mismatched connections.

## 1.4.1 - 2026-09-14

- Fixed administrator setup rejecting Steam session-authenticated players when a connection certificate is unavailable.
- Kept authentication tied to the current connection, with session revocation and disconnect cleanup.
- Added specific connection-verification details to administrator error messages.

## 1.4.0 - 2026-09-12

- Added an F3 Server Cap tab for RunicWorldEngine 1.2.x, showing connected players, the running cap, saved settings, and restart status.
- Added administrator-verified controls to enable the cap override and save a 2–64 player limit for the next server restart.
- Added configuration backups and stale-edit protection without changing the live cap, disconnecting players, or modifying admission policy.
- Added clear guidance when the host needs an updated Sentinel or enabled World Engine.

## 1.3.2 - 2026-09-10

- Added one-click Set Up Sentinel in the F3 panel for verified server administrators and local listen hosts.
- Added server-controlled administrator access through adminlist.txt, including Steam raw, Steam_, and Valheim 1.0 V_ ID formats.
- Preserved existing signed roles, bans, policies, and keys; first-time setup does not change admission mode.
- Added clearer administrator access guidance and an option to require signed Sentinel roles only.
- Recheck administrator access before returning cached panel responses.

## 1.3.1 - 2026-09-09

- Updated the exact native admission seam for Valheim 1.0.7's two-argument server handshake and
  preserved the invite secret through the one approved Required-mode resume.
- Moved the fail-closed transition gate to the common server world-load boundary, covering both
  legacy saves and Valheim 1.0 chunked saves.
- Backed up the complete active `World.GetSavePaths()` set, including recursively verified chunked
  directories with a hard 1,024-file ceiling; Steam Cloud staging no longer deletes source files.
- Updated the release dependency floor to BepInExPack Valheim 5.4.2350 and Runic Safety 1.0.2.

## 1.3.0 - 2026-09-07

- Fixed Optional-mode retries so every new observation uses a fresh challenge and reset attempt
  budget, and retained a full-client responder across the pre-`PeerInfo` connection phase.
- Added the server half of the direct, server-initiated Sentinel admission protocol used by the
  separate Runic Sentinel Client package.
- Required mode now withholds Valheim's native server handshake until a bounded client report is
  received, so a client with no Sentinel responder can no longer pass silently.
- Gated `RPC_PeerInfo` at the actual native world-admission boundary, started the deadline for every
  accepted transport, and bounded both the approved-handshake resume and follow-on `PeerInfo`
  windows while allowing two minutes for Valheim's password prompt.
- Made direct RPC registration collision-safe and ownership-checked during cleanup.
- Switched connection-duration deadlines to a monotonic clock so host wall-clock corrections cannot
  stretch or prematurely expire an admission window.
- Delayed the first full plugin snapshot until Unity `Start`, after BepInEx finishes loading plugin
  components, so later-loaded plugins cannot escape the inventory.
- Made the effective Remote Admission mode explicit and reject F3 mode changes until restart, so a
  transition cannot strand an intercepted native handshake.
- Bound every admission report to the exact direct `ZRpc` connection and a fresh server nonce;
  routed sender IDs and client-supplied peer IDs are not admission authority.
- Moved remote F3 administrator requests and responses to exact direct peer connections so routed
  sender IDs cannot select another player's authenticated administrator identity.
- Made the server reconstruct and evaluate the reported client plugin inventory against its own
  verified signed policy. Client and server plugin sets may now differ intentionally.
- Kept remote inventory claims explicitly self-reported compatibility evidence, not proof that a
  hostile client is running unmodified code.
- Aligned the package, plugin, file, and informational versions and updated the Runic Safety pin.

## 1.2.5 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.2.4.

## 1.2.4 - 2026-09-01

- Made the F3 administrator panel fully modal: it keeps the OS cursor visible and unlocked, prevents
  the game camera from reclaiming mouse capture, and blocks local movement/use input while open.
- Suppressed every local combat/build path that could consume a panel click, including queued attack
  processing, new `StartAttack` calls, placement actions, and build-menu input. F3 and Escape still
  close the panel normally.

## 1.2.3 - 2026-09-01

- Rebuilt the authenticated F3 administrator panel with an opaque Valheim-style carved-wood frame,
  dark inset content wells, cream/gold text, selected wooden tabs, higher-contrast fields, and a
  compact action footer patterned after the native F2 connection panel.
- Kept the administrator transport, per-request server re-authorization, policy validation,
  signing, backup, and tool behavior unchanged; this release changes presentation only.

## 1.2.2 - 2026-09-01

- Fixed dedicated-server bootstrap failing with `rsa-3072-unavailable` when Unity/Mono ignored a
  `KeySize = 3072` assignment on the provider returned by `RSA.Create()`.
- Added a validated `RSACryptoServiceProvider(3072)` fallback with CSP persistence disabled, and
  reused the same provider fallback when importing the managed private key for later signing.
- Kept exact RSA-3072 requirements: a 384-byte modulus, exponent 65537, private-key export,
  SHA-256, PKCS#1 v1.5 signatures, and immediate verification of the generated policy.

## 1.2.1 - 2026-09-01

- Added bounded stdin handling for the real batch-mode dedicated-server window, making
  `runic_sentinel status`, `bootstrap`, `report`, and `networks` directly typeable there.
- Kept all command execution on Unity's main thread with a 32-line queue, 1,024-character line
  bound, and eight-command per-frame drain limit.

## 1.2.0 - 2026-08-31

- Added a ConfigManager-style F3 administrator panel with signed mod lists, administrators, bans,
  admission policy, runtime integrity, and graduated enforcement controls.
- Added server-side backend-account authorization on every panel read and mutation; ordinary
  players receive no policy document and cannot invoke reports, maps, backups, or signing.
- Added one-time server-console bootstrap and a server-managed RSA-3072 key kept under the
  non-package `server-private` directory; the key is never returned to the client panel.
- Added panel actions for bounded support reports, administrator-only production/portal maps, and
  immediate verified Runic Safety world backups.
- Made the high/very-high escalation counts and rolling enforcement window effective server settings.
- Preserved Runic Sentinel as a standalone plugin: no Runic Core or Runic Persistence package,
  project, manifest, or assembly dependency. The F3 channel uses Valheim's routed networking and
  exact current Steam/PlayFab peer identity.

## 1.1.0 - 2026-08-31

- Kept the first-run Optional/monitor-only workflow usable when no signed passport exists yet;
  transition backup enforcement now fails closed only when Raven's Gate admission is Required.
- Added explicit player explanations for expired passports and protected transition-backup failures.
- Added a crash-surviving bounded security flight recorder with one 512 KiB current file and one
  512 KiB previous file; recorder I/O is isolated from request blocking and gameplay.
- Hardened the standalone routed profile comparison and Required-mode disconnection behavior.
- Added signed v3 plugin lists, administrators, and banned identities.
- Added runtime integrity monitoring, automatic request enforcement, support reports, clear denial UI,
  offline Forge tooling, current-profile export, and verified transition backups.
- Added an authoritative-server-only, on-demand bounded portal and production topology snapshot.

## 1.0.0 - 2026-08-22

- Added a private bounded standalone compatibility exchange with explicit Disabled,
  Optional-default, and Required outcomes. Required enforces the server's verified signed policy
  digest/sequence/profile; all client snapshot/hash/disposition values remain explicitly
  self-reported compatibility evidence.
- Added timestamp freshness, bounded current-peer validation, and replay/equivocation/rollback
  detection without a remote-administration claim.

- Replaced the forgeable same-process HMAC design with strict RSA-3072/SHA-256 PKCS#1 v1.5
  verification of exact `RUNIC-SENTINEL/2` bytes. Sentinel loads only a public key whose exact
  canonical-file SHA-256 is pinned in configuration.
- Added strict public-key and signature-file canonicalization, policy sequence/issue/expiry fields,
  and in-process rollback/equivocation rejection.
- Kept attestation, admission, and evidence contracts private to Runic Sentinel and corrected the
  canonical capability from `security.attestation` to `security.attest`.
- Renamed the public nonce digest to an unauthenticated nonce binding and explicitly reports that it
  is neither client-authenticity proof nor an authoritative transport.
- Added exact local-lease evidence-provider registration, per-provider fair queues, immutable reads,
  requested/effective action, policy sequence, and saturating accepted/drop counters.
- Made worker publication generation-safe, cancellation-gated, platform-path-correct, and deduplicated
  so multiple plugin descriptors sharing one path hash that file only once.
- Made `Enabled = false` startup-inert: no worker, network handler, or service is created.

## 0.1.0

- Hardened plugin and signed-policy input reads against size-check/read races: hashing consumes the
  exact admitted length through one reusable bounded buffer, and policy/signature/key streams must
  remain byte-exact and metadata-stable through EOF before verification.

- Added bounded signed-policy parsing and HMAC-SHA256 verification with fail-closed monitor-only fallback.
- Added deterministic loaded-plugin attestation, fresh-nonce response, admission policy, and a 256-entry evidence ledger.
- Published `security.attestation`, `security.admission`, and `security.evidence` protocol 1.0 services.
- Deliberately deferred connection enforcement until an authenticated server/client transport exists.
