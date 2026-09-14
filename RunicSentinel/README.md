# Runic Sentinel 1.4.2

Running a private or modded Valheim server requires more than a password and a request that everyone install the right files. Server owners need to know who may enter, which mod profile is acceptable, who has administrative authority, and what happens when a client does not match.

**Runic Sentinel: Raven's Gate gives the server an enforceable admission and administration layer.** Define the approved mod environment, authenticate administrators, manage bans and roles, investigate evidence, and operate the system through a secure in-game F3 panel.

## Major features

- Define required, optional, allowed, and forbidden mod rules.
- Enforce admission against signed server policy.
- Manage authenticated administrators, roles, and banned identities.
- Detect relevant runtime changes and record bounded security evidence.
- Create transition backups, generate support reports, and administer in-game.
- Set World Engine's server player cap from the F3 Server Cap tab.

## How it feels in-game

Players receive a bounded admission result through Runic Sentinel Client, while administrators manage routine policy from Valheim instead of treating every change like a command-line incident. Raven's Gate stays mostly invisible when the server and client agree.

## Safety and compatibility

The signed server policy is authoritative; client file claims are treated as compatibility evidence, not impossible-to-forge proof. Consequential requests remain server-validated against transport identity, permission, bounds, and replay state. A new server begins in Optional admission mode to avoid accidental lockout. Admission uses the exact connection's direct `ZRpc`, before routed world networking exists.

## What Raven's Gate enforces

- Plugin ID, version, and SHA-256 rules from the signed passport.
- Deny-by-default unknown plugins when `unknownMods` is `Forbidden`.
- Signed administrator and banned-user lists bound to authenticated platform identity, not a
  changeable character name.
- A bounded server-initiated compatibility exchange on the exact direct connection; Required mode
  withholds Valheim's native handshake and gates `RPC_PeerInfo` world admission until the client
  report passes signed policy.
- Server request blocking, bounded evidence, and graduated disconnects for repeated high-confidence
  or conclusive violations.
- Low-frequency runtime detection when a loaded plugin DLL or active passport asset changes.
- A verified Runic Safety world backup before a server loads an existing world under a different
  signed policy or plugin snapshot.
- Clear client denial explanations, a bounded persistent security flight recorder, and a bounded
  support report.

A client controls its own process and can falsify self-reported file evidence. The signed server
policy is authentic; a client's DLL claim is compatibility evidence, not unforgeable proof. The
authoritative protection is that consequential Runic requests are revalidated by the server against
transport identity, permissions, bounds, replay state, and durable transaction rules.

## Runtime enforcement coverage

- Sentinel rejects malformed, stale, oversized, replay-conflicting, and profile-incompatible
  reports. In Required mode a missing responder also reaches a finite deadline and is disconnected.
- Runic Portals keeps its feature-owned server checks for exact peer identity, group authority,
  ward access, source distance, endpoint revision, and destination permission. Malformed envelopes,
  unbound identities, and conflicting request replays are additionally reported to Sentinel after
  they have already been rejected.
- Production, Crafting, Agriculture, Storage, Inventory, Interaction, Safety, and precision-building
  mutations do not expose a general client-to-server command channel: their changes retain native
  local ZDO ownership, range, ward, inventory, or transaction checks. Sentinel admission still
  requires their exact configured plugin hashes.
- Awareness, Exploration, Build Camera, Velocity, and the observatory portion of World Engine are
  client-local or read-only and do not create a server gameplay mutation request to authorize.

Ordinary gameplay mistakes—such as lacking a ward permission—are denied but are not treated as
cheating. Automatic disconnection is reserved for repeated high-confidence protocol violations or
conclusive evidence.

## Administrator setup

1. Install Sentinel Server 1.1.2 on the dedicated server and full Sentinel 1.4.2 on the administrator's
   client. A listen host uses full Sentinel only. Start with `Remote Admission > Policy = Optional`.
2. Add your account ID to the **server's** `adminlist.txt`, one ID per line. For Steam, use
   `V_<SteamID64>` on Valheim 1.0; Sentinel also recognizes raw SteamID64 and `Steam_<SteamID64>`.
   A local listen host is recognized automatically. These are account IDs, not character names.
3. Join and press **F3**, then click **Set Up Sentinel** once. The server verifies your account,
   creates its private signing key, backs up the loaded world, and registers your signed Sentinel
   role. No server-console bootstrap command is needed. Setup can take a moment; it does not
   change admission mode or copy the server's mods into a client allowlist. Existing policies and
   keys are preserved; already-configured servers open the normal panel instead.
4. Use the panel to manage required/optional/gray/forbidden mods, administrators, banned users,
   runtime checks, graduated disconnection thresholds, transition backups,
   support reports, and administrator-only production/portal network snapshots. The Mods tab shows
   both the server inventory and the most recent bounded client report; only the latter is a useful
   starting point for client rules. The panel reports the effective admission mode; change that
   startup setting in the server configuration and restart before admitting more players.
5. Select **Apply & Sign Policy**. The server validates all fields, creates a verified world backup
   when a world is loaded, generates the next sequence and issue time, signs with its private key,
   archives the prior public policy assets, and reloads the verified policy.
6. Install **Runic Sentinel Client** 1.0.1 in ordinary player profiles. It reports only plugin GUID,
   version, and SHA-256 evidence and does not need any policy or key file. A remote F3 administrator
   installs full Sentinel alongside it; collision-safe registration leaves one active v2 responder.
   Never distribute
   `BepInEx/config/RunicSentinel/server-private/RunicSentinel.private.key`.
7. Verify independently with the optional external Forge if desired:

   ```text
   RunicSentinel.Forge verify <policy> <signature> <public-key> <public-key-pin>
   ```

8. Confirm `runic_sentinel status`, then use Required admission for the group.

Server administrators inherit Sentinel access by default. Set `[Administrator Access]`
`UseServerAdminList = false` on the server to require only signed Sentinel roles. Signed roles are
independent: removing someone from `adminlist.txt` does not remove a separately granted signed role;
remove that role in the People tab too. Signed bans take precedence. Valheim refreshes the adminlist
on permission checks at roughly ten-second intervals; close/reopen F3 after editing it.
Sentinel access does not itself enable another mod's devcommands.

Every intentional policy update must increase `sequence`. A lower sequence or different signed
payload at an already accepted sequence is rejected for that process lifetime.

## Server Cap tab

Install **RunicWorldEngine 1.2.x** on the host, then open **F3 → Server Cap** from full
**RunicSentinel 1.4.2** on your administrator client. The host needs **RunicSentinelServer 1.1.2**
or full **RunicSentinel 1.4.2**, never both authority packages. Ordinary players keep
RunicSentinelClient; it has no administration panel.

The tab shows the connected-player count, running cap, saved cap, and restart status. Check
**Enable World Engine's player-cap override after restart**, enter **2–64** players, then click
**Save for Next Restart**. Enter the number of human players; World Engine handles the dedicated
PlayFab host slot automatically. Uncheck the override to return to the vanilla limit after restart.

Saving changes only the host's two player-cap settings and keeps the previous configuration in
`chazman.RunicWorldEngine.cfg.sentinel-cap.bak`. It does not restart the server, kick players, or
change the running limit. Restart the host when ready; World Engine validates its cap patches at
startup. Use **Refresh Server Settings** to reload the saved values and discard tab edits.

Every save requires a verified server administrator or signed Sentinel administrator role;
signed bans still take precedence. This tab does not grant roles or require **Apply & Sign Policy**.
If another administrator changes the configuration, refresh before saving again. World Engine is
optional for Sentinel's other features; without a supported, enabled host version, the tab explains
why cap administration is unavailable.

## Lists and meaning

- `requiredMods`: must be present and match version/hash.
- `optionalMods`: may be absent; if present, version/hash must match.
- `grayListMods`: explicitly known and allowed, while remaining named policy evidence.
- `forbiddenMods`: denied when present.
- `unknownMods`: normally `Forbidden`; `Unmanaged` permits unknown entries and is not recommended
  for Raven's Gate.
- `administrators` and `bannedUsers`: `{ "authority": "steam", "subject": "<SteamID64>" }` or
  another transport authority/subject pair supported by Sentinel's Valheim transport binding.

## Commands

- `runic_sentinel status` shows integrity, policy profile/sequence, admission transport, and the last
  bounded denial code.
- `runic_sentinel report` writes a maximum 512 KiB report under
  `BepInEx/config/RunicSentinel/reports`. Reports include plugin IDs/versions/hashes, Runic
  integrity and bounded evidence. They omit paths, passwords, tokens, private
  keys, and raw chat.
- Accepted security evidence is also written automatically under
  `BepInEx/config/RunicSentinel/flight-recorder`. The recorder keeps only a 512 KiB current file and
  one 512 KiB previous file. An audit-write failure is logged once and never disables request
  blocking or interrupts gameplay.
- `runic_sentinel networks` is accepted only by the authoritative server/host console. It writes a
  bounded point-in-time topology snapshot of loaded Runic portal endpoints and production links;
  it is not a continuously running overlay and is never broadcast to ordinary clients.

## Performance and bounds

- The first DLL snapshot starts in Unity `Start`, after BepInEx finishes plugin `Awake` loading, so
  later-loaded plugins are included. Hashing then runs on a background worker, not every frame.
- Runtime integrity checks compare stable file metadata every 5–300 seconds (15 by default).
- Required compatibility evaluation runs before the native server handshake and `RPC_PeerInfo`
  world admission are released. A client has 20 seconds and at most three challenge/report attempts,
  then 10 seconds to deliver the approved native-handshake resume and up to two minutes to finish
  Valheim's password prompt and reach `PeerInfo`.
- Remote admission mode is sampled at startup. The F3 workflow reports the effective value and
  rejects mode changes until the server is restarted, avoiding half-released handshakes.
- Evidence is fixed at 32 providers with eight entries each (256 total).
- Persistent flight-recorder storage is capped at two 512 KiB files.
- Policy is capped at 1 MiB; at most 512 plugins are observed; each DLL is capped at 512 MiB and
  total unique DLL input is capped at 4 GiB.
- No continuous chest, production, portal, player, or world-object scan is performed. The
  administrator topology command scans at most 16,384 already-loaded ZDOs only when requested.

Runic Sentinel 1.4.2 requires Runic Safety 1.0.2 for verified legacy and Valheim 1.0 chunked-world
transition backups. It targets Valheim 1.0.7 and BepInExPack Valheim 5.4.2350, and has no Runic
Core or Runic Persistence dependency.
In the F3 workflow the private RSA-3072 key is server-managed and never sent through the panel or
network. Offline-key operators may continue using the separate Forge workflow instead.
