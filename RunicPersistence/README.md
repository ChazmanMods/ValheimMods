# Runic Persistence

Runic Persistence is a non-gameplay foundation for the Runic mod suite. It gives later modules a
consistent way to name persistent keys, migrate schemas without partial mutation, preserve unknown
data, negotiate optional capability protocols, and exchange bounded direct-peer requests without
trusting a client-controlled routed sender UID.

## Current 1.0.0 scope

- Canonical `runic.<module>.<key>` namespacing.
- Record headers carrying module, schema, owner, editor, and modification metadata.
- Registered forward-only migration chains.
- Backup-before-mutation hooks and atomic snapshot replacement.
- Idempotent no-op behavior when the target schema is already active.
- Preservation of unknown keys unless a module's registered migration explicitly removes its own key.
- Per-capability protocol negotiation so one incompatible integration can disable without breaking peers.
- Correlation and idempotency fields for Runic RPC envelopes.
- A bounded direct `ZRpc` request/response transport with per-session sequence, replay, rate,
  pending-request, payload, endpoint, and provisional-connection limits.
- A compatibility challenge/offer gate that completes before vanilla `RPC_PeerInfo` world
  admission, including bounded namespaced compatibility claims. Claims detect honest mismatches;
  they are self-reported compatibility evidence, not anti-cheat attestation. Claim values retain
  their exact canonical UTF-8 bytes: leading/trailing whitespace is rejected rather than silently
  normalized before an evaluator sees it. The evidence-visible hello nonce must also be the exact
  lowercase encoding of the binary connection nonce or the frame is rejected.
- Server-side actor resolution from the exact direct session to one current Player ZDO owned by
  that peer, with a separately labeled server-evaluated Steam admin fact.
- A checksum-protected, atomically replaced, world-scoped, reverse-unique backend-account to
  Valheim-player binding used only when a caller explicitly requests `AccountBoundPlayer`.

Persistence stores are explicitly module-scoped. Their IDs use exactly `runic.<module>` (two
segments), while local keys may add validated dot-separated segments. This makes namespace
ownership unambiguous even though Runic Core permits deeper identifiers for non-persistence uses.
Migrations atomically compare-and-swap a fresh snapshot, reject concurrent overwrites, and reject
any transform that changes keys outside its owning module prefix.

The RPC service registers directly on each connection's `ZRpc`. It does not use `ZRoutedRpc` sender
UIDs as authentication. A server-to-client send requires the caller's exact `RpcPeerSnapshot`,
including its session ID, so a stale command cannot hit a later connection that reuses a peer UID.
Mutations use explicit idempotency keys; durable handlers remain responsible for their own
persistent operation journal across reconnects and restarts. Idempotency keys are exact canonical
UTF-8 wire identities: leading/trailing whitespace, controls, invalid UTF-16, and values beyond the
256-byte bound are rejected before send and again at receive. They are never trimmed into an alias.

Fresh ownership metadata should store `RpcPeerIdentity` directly. Legacy Valheim profile-player IDs
are accepted only after the server resolves a transport-owned Player and the binding store verifies
the account-to-player bijection in both directions. `Resolve` never creates a binding. A corrupt,
wrong-world, duplicate, or truncated binding file fails closed, and the diagnostic `.bak` is never
restored automatically.

Trusted Steam admins can manage dedicated-server bindings from their connected client's F5 console:

```text
runic_bind peers
runic_bind enroll <peerUid>
runic_bind list [offset]
runic_bind revoke-player <playerId>
```

These commands use the direct session RPC. The server rechecks the requesting Steam backend
identity against its own admin list, re-resolves the target's current transport-owned Player, and
rechecks both exact sessions immediately before the atomic commit. Only a canonical Steam admin can
exercise this remote admin path. The target may be an exact current Steam or `playfab.entity`
identity with `BackendAccount` assurance; PlayFab cannot authorize or initiate enrollment, and a
raw/connection-only/display identity cannot be enrolled. A local-host F5 console can also use
`revoke-account <authority> <subject>` and `reload`; arbitrary chat/Terminal/RCON contexts are not
treated as the process console.

### Dedicated player-group setup

1. Install the same coordinated Runic Persistence/Foundation build on the dedicated server and
   every player client. The compatibility gate rejects a missing or mismatched required module
   before vanilla `RPC_PeerInfo` admits that client to the world.
2. Put the administrator's canonical 17-digit SteamID64 in the server's vanilla `adminlist.txt`,
   then restart the server or otherwise apply the vanilla admin-list change.
3. Enable Valheim's local console for that client, join the world with that exact Steam account,
   open F5, and run
   `runic_bind peers`.
4. While each account/character is connected, run `runic_bind enroll <peerUid>` using the peer UID
   shown by the server query. Repeat for every player who needs legacy player-ID owner/group/ward
   features, including the administrator's own character when applicable.
5. Run `runic_bind list` (and subsequent pages with `list <offset>`) to verify the committed
   world-scoped pairs. A backend account and a Valheim player ID can each appear in only one pair.

Enrollment is explicit and world-specific; joining or resolving an actor never enrolls anyone.
Owner/group/ward endpoints that require `AccountBoundPlayer` remain fail-closed until the relevant
pair is present. A connected canonical Steam admin may enroll an exact current PlayFab
`BackendAccount` target for crossplay groups. PlayFab identities cannot authorize enrollment;
raw/connection-only sessions, display names, and Steam-shaped values reported through another
backend cannot be enrolled or elevated.

Runic Persistence does not add recipes, items, pieces, UI, or gameplay behavior.

## Discovery

Runic Persistence requires Runic Core 1.0.0 or later. It registers these typed services through the
shared registry:

- `persistence.migrate` as `IMigrationService`.
- `network.protocol` as `IProtocolNegotiationService`.
- `network.rpc` as `IRunicRpcService`.

Its active module lease also owns `network.actor-identity-binding`; the single persisted resolver is
installed inside `IRunicRpcService`, which fails closed if that resolver or its module lease is
absent.

Consumers should resolve those interfaces through `RunicRegistry.Shared` instead of reaching into
the plugin lifecycle or relying on plugin load order beyond the declared hard dependency.
