# Runic Sentinel Server 1.1.2

Runic Sentinel Server is the authority-only Raven's Gate package for a dedicated server or a
listen host. It verifies and signs the approved mod policy, challenges each connecting player,
enforces admission and bans, accepts authenticated remote-administrator requests, creates bounded
reports, and coordinates verified transition backups.

Uses BepInExPack Valheim 5.4.2350 or newer. It gates Valheim 1.0's native
invite-secret handshake and backs up both legacy world files and the 1.0 chunked-world directory.

This binary contains no client profile reporter, native client-handshake resume path, administrator
window, cursor control, or player-input patch. When accidentally installed in a non-authoritative
client profile it watches Valheim's role selection and remains inert. Install **Runic Sentinel
Client** there instead.

## Setup

1. Install this package and Runic Safety on the server. Do not install the full Runic Sentinel
   package alongside it; the two authority packages are intentionally incompatible.
2. Start with `Remote Admission > Policy = Optional`.
3. Add your account ID to the server's `adminlist.txt`. For Steam on Valheim 1.0, use
   `V_<SteamID64>`; Sentinel also accepts raw SteamID64 and `Steam_<SteamID64>`.
4. Install full Sentinel 1.4.0 or newer on your administrator client, join, press **F3**, and click
   **Set Up Sentinel** once. The server verifies your account, backs up the world, and creates its
   own signing key and initial administrator policy without a console command. Existing policy/key
   files are never replaced by this setup. Ordinary players keep Runic Sentinel Client 1.0.1;
   they cannot claim administrator access. A listen host that wants the panel uses full Sentinel
   instead of this authority-only package.
5. Review and sign the client profile, confirm `runic_sentinel status`, then restart with Required
   admission when ready.

The managed private key remains under
`BepInEx/config/RunicSentinel/server-private/RunicSentinel.private.key`. Never distribute or package
that directory. Existing full-Sentinel policy, signature, public-key, history, report, and backup
paths are retained so switching authority packages does not fork server security state.

Server administrators inherit Sentinel access by default. Set `[Administrator Access]
UseServerAdminList = false` on the server to require only signed Sentinel roles. Signed roles are
independent: removing someone from adminlist.txt does not remove a separately granted signed role;
remove that role in the People tab too. Signed bans take precedence. New adminlist entries are read
by Valheim on permission checks, with a roughly ten-second refresh interval. Close/reopen F3 after
editing the list. Sentinel access does not itself enable another mod's devcommands.

The manual `runic_sentinel bootstrap <authority> <subject>` server-console workflow remains available.
Keep admission Optional until the intended client profile has been reviewed; first-time setup is
not an automatic strict allowlist.

Required mode withholds native admission until a fresh, bounded report from the exact direct
connection passes the signed server policy. A missing responder, malformed report, stale challenge,
banned identity, or policy mismatch reaches a finite denial. Client-reported DLL information remains
compatibility evidence from a client-controlled process, not unforgeable anti-cheat proof.

Authority preparation begins at Valheim's earliest server-role selection and binds to the exact
server network instance when it is created. If authority startup fails while Required mode is
selected, permanent safety gates block world loading and native client admission instead of silently
running an unprotected server. Restart after correcting the logged startup error.

## Remote server-cap administration

With **RunicWorldEngine 1.2.x** installed and enabled on this host, administrators using full
**RunicSentinel 1.4.0** can open **F3 → Server Cap**. The tab shows current players, the running
cap, saved settings, and whether a restart is needed. Choose an override of **2–64** human players
and click **Save for Next Restart**, or turn the override off to restore the vanilla limit after
restart. World Engine accounts for the dedicated PlayFab host slot automatically.

The server rechecks administrator access for every request. Saves update only the two World Engine
player-cap settings, retain the previous configuration in
`chazman.RunicWorldEngine.cfg.sentinel-cap.bak`, and reject stale edits. Nobody is disconnected and
the host is not restarted by this action. Restart it when ready to apply the saved cap and run
World Engine's startup integrity validation. No policy signing or role changes are involved.

World Engine remains optional for other Sentinel features. This server-only package contains the
cap handler, not the panel; RunicSentinelClient remains admission-only.

Questions and logs: [Runic Mods Discord](https://discord.gg/7HKHTCdFqY)
