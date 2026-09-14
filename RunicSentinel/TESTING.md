# Private Sentinel verification — 2026-09-14

## Buffering socket compatibility (full 1.4.2 / server 1.1.2)

The AMP retest of server 1.1.1 returned `unsupported-or-missing-socket`, before Steam certificate
or session verification. Thus 1.1.1 did not solve the reported failure. The old message also
covered a PlayFab-derived wrapper with an empty remote entity field and did not identify its type.
The installed ConditionalConfigSync 1.0.6 DLL and upstream ServerSync ConfigSync.cs expose
PlayFab-derived BufferingSocket wrappers carrying a readonly ISocket Original, which may be Steam.
Nested RPC_PeerInfo config-sync hooks can leave one of these wrappers on the peer after login.
The affected user's logs load Conditional Config Sync and mods embedding ServerSync. Exact
remaining wrapper type on their server is not directly logged by 1.1.1.

The new resolver supports only the two audited full type names and their readonly Original field.
It walks at most 32 wrappers, rejects cycles/null/unknown layouts, and requires the peer and RPC
chains to end at the SAME native socket object. It resolves wrappers before testing native types;
it never uses inherited PlayFab fields from wrappers as identity evidence. It does not replace
sockets, flush queued messages, alter sync configuration, or change permissions. The ticket
observer uses the same resolver while native PeerInfo is still inside nested synchronization hooks.
Unknown-type error messages now name the type; actual PlayFab identity failure has its own reason.

Sources audited:
- https://github.com/blaxxun-boop/ServerSync/blob/master/ConfigSync.cs (SendConfigsAfterLogin)
- Installed shudnal-ConditionalConfigSync/ConditionalConfigSync.dll (ZNetRpcPeerInfoSyncPatch).

Results: full 47/47; server 22/22; expanded HarmonyX harness 83/83, including raw sockets,
ServerSync wrapper, nested CCS+ServerSync wrappers, asymmetric peer/RPC wrapper chains, exact
object binding, preserved queues/references, misleading inherited PlayFab fields, real wrapped
PlayFab, unknown/null/cyclic/overdeep wrappers, certificate-less ticket auth, rejection/revocation,
and both callback backends. Compiled checks also verify the actual installed CCS wrapper contract.
Controlled Steam/game seams are not a real AMP or Steam acceptance test.

Send the player ONLY server 1.1.2 for the next AMP test: stop instance, preserve a DLL backup
outside plugins, replace the existing SentinelServer DLL, restart, reconnect and try F3. Keep
client Sentinel 1.4.0 and SentinelClient 1.0.1 and all config/adminlist/key/policy files unchanged.
If failure persists, obtain the exact new reason including its type name. Full 1.4.2 is the
corresponding listen-host candidate for the owner's separate test. No live deployment or upload.

## Steam authentication compatibility (full 1.4.1 / server 1.1.1)

The AMP player's logs prove the supported package versions, Steamworks transport and a Jotunn
Admin response. Their earlier screenshot identifies the failing Sentinel identity gate. The logs
do not expose connection flags or the failing branch. Certificate-versus-session authentication
is a reproduced code gap, not a confirmed observation of that player's connection flags.

Implementation observes Valheim's VerifySessionTicket result and Steam's native
ValidateAuthTicketResponse_t callback. A missing transport certificate may be replaced only by
successful native ticket acceptance AND a successful asynchronous Steam validation, associated
with the same network/socket/connection handle/account. Remote socket, peer and Steam connection
identities must still agree. Rejected/revoked sessions deny even if a certificate is present.
Adminlist/signed-role checks and signed bans remain after identity verification. The Steam API
reports callback identity by account, not connection handle; callback association follows its
auth-session lifecycle, with EndAuthSession, socket-close and world/shutdown cleanup. No ticket
bytes, private keys, passwords or client-supplied administrator claims are stored or logged.

Regression commands:

- dotnet run --project RunicSentinel.Tests/RunicSentinel.Tests.csproj -c Release --no-restore
- dotnet run --project RunicSentinelServer.Tests/RunicSentinelServer.Tests.csproj -c Release --no-restore
- dotnet run --project tools/RunicSentinel.SteamRegression/RunicSentinel.SteamRegression.csproj -c Release

Results: full 47/47; server 22/22; HarmonyX harness 24/24. The harness executes the linked production
resolver, lifecycle patches and evidence registry using installed HarmonyX, but controlled game
and Steam seams. It is NOT a live Steam or AMP acceptance test. Windows client/dedicated and
saved Linux 1.0.12 game-member contracts are checked using Cecil. Existing policy, crypto,
administrator authorization, bans, admission and server-only packaging checks pass unchanged.

Player acceptance:

1. Back up the current server Sentinel DLL and its config/policy directory. Stop the AMP Valheim
   instance, replace ONLY RunicSentinelServer.dll with 1.1.1 and restart. Do not install full Sentinel
   alongside SentinelServer. Preserve adminlist, keys, policies, admission mode, worlds and vault.
2. Keep the administrator client's full Sentinel 1.4.0 (1.4.1 also works) and SentinelClient 1.0.1.
   Reconnect and open F3. If not already bootstrapped, use Set Up Sentinel. This intentional setup
   creates policy/key/backups as documented; do not reset an existing setup.
3. Verify an admin can view the panel and an ordinary non-admin remains denied. Verify the existing
   signed bans/roles still apply, and removal of native admin rights takes effect where no signed
   role independently grants access. Use a backed-up test setup for permission changes.
4. Disconnect/reconnect and repeat. Also test a Windows listen host and a crossplay session; the
   PlayFab resolution path is unchanged. Preserve the exact new parenthesized identity reason if
   the AMP user's failure persists. Pending authentication means wait briefly and reopen F3.

Client-only users have no required update for this host-side correction. No changes were deployed
to a live client/server, no server was restarted, no suite was updated, and nothing was uploaded.
Public ZIPs contain only the normal package files, not these private test notes.
