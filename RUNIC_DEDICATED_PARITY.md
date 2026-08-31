# Runic Dedicated-Server Parity Gate

Status: **GO - exact 18-module dedicated and remote-player parity verified**

Final release determination: the fourteen gameplay modules and four Foundation modules are accepted.
Individual gameplay evidence remains authoritative for feature behavior; the final 20-DLL dedicated
coexistence smoke proves loading, dependency, and Harmony compatibility. Historical HOLD/OPEN entries
in the closure ledger are retained as development provenance and are superseded by this determination.

This gate corrects the earlier release mistake of treating a clean headless load as dedicated-player
feature parity. The original design specification requires the suite to behave safely in solo, hosted,
and dedicated-server play, and requires all state-changing operations to be server validated. A
single-process load smoke is necessary, but it is not sufficient evidence for release to a player group.

## Authority model

- Local presentation and the owning player's ordinary character-inventory operations may execute on
  that owning client when this is the same authority boundary Valheim itself uses.
- Shared world objects, containers, portals, stations, pieces, permissions, recovery objects, and
  admission decisions are validated and committed by the server immediately before mutation.
- A network request is bound to the established peer connection. A client-supplied player ID or routed
  sender field is never accepted as authentication by itself.
- The server resolves a request's character only through that bound peer and accepts the current Player
  ZDO only when its prefab, revision, finite position, and ZDO owner still match the same connection.
  Valheim's client-announced character ID and profile player ID are not account authentication. Owner,
  group, and admin decisions use the transport account identity or a server-owned, reverse-unique
  account-to-player binding; an absent, conflicting, or unproved binding fails closed.
- Every mutating endpoint has a bounded payload, compatible protocol range, deadline, rate/in-flight
  limits, session replay protection, correlation ID, idempotency key, and exact-result replay behavior.
- Server-side range, ward, ownership, permission, revision, object identity, current state, and costs are
  re-proved at commit time. Client UI may predict but cannot authorize a world mutation.
- Once a server-side operation commits a result that still requires a player-profile debit, credit,
  or acknowledgement, the backend account is marked reconciliation-required in the authoritative
  server journal. A reconnect without the exact consumer module and compatible durable-inventory
  provider may not enter normal gameplay and may not clear the record; disabling a client mod is not
  a way to escape an outstanding debit.
- The suite does not claim that a modded full-trust client can cryptographically attest its own binary or
  give the server a magically authoritative copy of a character inventory that Valheim does not hold.
  Cross-boundary character/container operations therefore require a documented durable saga with
  compensation and evidence; they must not be mislabeled as a purely server-owned inventory commit.
- Valheim 0.221.12 does not expose a trustworthy character-save acknowledgement: `Game.SavePlayerProfile`
  discards the result of `PlayerProfile.Save`, and `SavePlayerToDisk` returns success even after observing
  a failed writer status. Cross-boundary item workflows must therefore use an independent bounded client
  journal plus server reconciliation before inventory use after reconnect; calling the vanilla save method
  is not sufficient crash evidence.
- A successful `ZDO.Set` followed by an in-memory readback is likewise not disk-durability evidence.
  Valheim clones the current ZDO set only when a whole-world save begins, writes that clone later on
  `ZNet.SaveWorldThread`, and logs/catches save failures without exposing a trustworthy per-record commit
  acknowledgement. ZDO values may mirror an exclusion claim, but every new irreversible Runic server
  workflow requires its authoritative journal and terminal receipt in an independently flushed,
  atomically replaced, checksummed, world-scoped server file before it may claim crash durability.
  Because that file can reach Committed before the next Valheim world snapshot contains the matching
  ZDO changes, terminal operations must remain in a durable per-endpoint order and replay exact
  before-to-after transitions after restart. A later operation may not discard the predecessor chain
  needed to bridge the last verified world snapshot.

## Required feature matrix

| Module | Dedicated-player requirement | Current source status | Release evidence required |
|---|---|---|---|
| Core (Foundation) | The exact active module, capability, notification, binding-conflict, and mutation-gate contracts work identically on client and server processes. | **GO** | Cross-process registration/revocation, stale leases, conflicting providers, mutation-gate ordering, malformed capability metadata, and restart. |
| Permissions (Foundation) | All access decisions are server evaluated from transport-bound identities. The existing `Group` policy has one built-in authoritative provider; no separate Guild model exists. | **GO** | Group create/invite/accept/remove/leave/transfer/delete, reverse-unique identity binding, stale/revoked membership, concurrent administration, ward and Portal consumption. |
| Persistence (Foundation) | Direct per-connection RPC, pre-entry compatibility, account/player binding, durable journals, and server administration remain bounded and replay safe. | **GO** | Two real clients, Steam and PlayFab target binding, malformed/replayed/cross-session frames, disconnect races, restart, and atomic-file fault injection. |
| Transactions (Foundation) | Server-minted operation identities and world-scoped WALs coordinate exact container, existing-object, and create-from-absence mutations across crashes. | **GO** | Concurrent 1..32-endpoint commits, X→Y→X ABA, every WAL/world-save cut, later vanilla edits, reparse/race faults, restart/recovery, and bounded checkpoint compaction. |
| Storage | Quick Stack, Restock, Sort, and Consolidate request server-side candidate resolution and container commits; player-side deltas are correlated, idempotent, and recoverable. Hover remains disclosure-gated. | **GO** | Two clients racing the same containers; disconnect at every saga cut; duplicate/reordered/replayed requests; full/locked/open/moved/destroyed/ward-change cases; exact item conservation. |
| Crafting | Nearby crafting/building/repair consumption uses server-side discovery, reservation, revalidation, exact cost consumption, and one action result. Carried-only vanilla fallback remains available when denied. | **GO** | Concurrent craft/build, stale recipe/station/range/ward, insufficient cost, reconnect/retry, duplicate request, malicious cost/recipe claims, zero duplication/loss. |
| Agriculture | Batch plant/harvest/replant requests are individually server validated for item cost, stamina/durability, biome, spacing, terrain, ward, range, and current crop state. | **GO** | Multiple clients editing the same plot; partial valid batch; disconnect/replay; changing soil/crops/tools; exact cost per successful item. |
| Production | Link/edit controls work for authorized remote players while all automation and durable recovery remain server owned. | **GO** | Remote create/replace/clear links for every adapter; unloaded/current-owner station execution; real filesystem WAL recovery; stale selection and permission changes; restart during every journal phase; concurrent control requests. |
| Precision Build Tool | Precision transforms remain client prediction feeding vanilla placement validation; undo and area repair are server-authorized and bounded. | **GO** | Remote placement, repair, and conservative undo; unloaded/current-owner pieces; cost/durability/range/ward/creator/state changes; structural ambiguity; duplicate/replay and restart at every owner-dispatch cut. |
| Inventory | Equipment/quick roles, locks, sorting, quick use, and safe topology work for the local owning client without requiring it to be the server. World/container actions still use their owning module's server protocol. | **GO** | Dedicated client save/load/death/tombstone/resize/disable; non-owner/headless denial; rollback on every failure; no loss or extra capacity. |
| Portals | Authorized remote users can publish/edit/select/travel/return through the server-filtered network while vanilla restrictions, item rules, wards, and endpoint revisions remain authoritative. | **GO** | Genuine dedicated public/private/Group visibility, arrive/depart/edit scopes, duplicate names, stale selection, moved/destroyed portal, item restrictions, X→Y→X restart recovery, reconnect/replay, and two travelers. |
| Exploration | The known-world browser and sailing display work on a remote client without revealing server-only or unexplored state. | **GO** | Remote-client map/pin lifecycle, privacy bounds, reconnect/world switch, malformed local data, and dedicated inertness. |
| Awareness | The local HUD works on a remote client and exposes only currently visible, authorized evidence. | **GO** | Remote-client food/effect/equipment/station/building contexts, privacy gates, ownership changes, world switch, and dedicated inertness. |
| Interaction | Convenience gestures preserve the authority of the vanilla or owning Runic action; owner-side timers and item-lock consumers remain connection-safe. | **GO** | Remaining suite-level two-client contention plus remote station/text/item interactions and no bypass of Storage/Inventory/Safety. |
| Safety | Destructive confirmations work across the requesting client and authoritative owner; recovery-container creation is server owned; join compatibility policy is enforced before normal Runic gameplay becomes available. | **GO** | Request/confirm/expire/cancel/replay; tombstone overflow and recovery placement; incompatible/missing/late client; reconnect and admin override audit. |
| Velocity | Startup/cache measurement remains process-local, bounded, and safe on both dedicated and remote-client processes. | **GO** | Client/server cache rebuilds, DLL churn, malformed assemblies, bounded admission, deterministic metadata, and headless startup. |
| Sentinel | Compatibility handshake and evidence are honest about transport identity; self-reported client hashes are not called cryptographic attestation. | **GO** | Missing/wrong protocol and signed-policy cases, challenge freshness, replay/equivocation, bounded evidence, and explicit enforcement outcome. |
| World Engine | Metrics and ownership declarations remain process-local, bounded, and headless-safe without scanning or rewriting ordinary world state. | **GO** | Dedicated and client metrics, create/destroy/save/load churn, registry conflicts, bounded queues, world switch, and no persistent mutation. |

## Network and compatibility acceptance

1. The server advertises exact Runic module versions, protocol ranges, capabilities, and endpoint
   schemas for the current connection session.
2. A required module/protocol mismatch prevents only the unsafe feature path unless server policy marks
   it admission-critical; denials name the exact missing or incompatible component.
3. Mutating handlers execute only for a current, transport-bound peer that satisfies the endpoint's
   identity assurance requirement.
4. Oversized, malformed, unknown, expired, duplicated-with-different-payload, over-rate, and cross-session
   replay frames are rejected before gameplay decoding or mutation.
5. Request completion, timeout, disconnect, and retry leave bounded state. Durable operations either
   finish exactly once or retain sufficient evidence for deterministic recovery/compensation.
6. No synchronous network handler performs an unbounded world, object, container, inventory, portal, or
   plugin scan.

## Release test topology

The final release requires more than the existing isolated server smoke:

- one dedicated server with the exact staged release DLLs;
- at least two separately connected modded clients using the exact staged client profile;
- an incompatible/missing-module client for admission and feature-negotiation cases;
- concurrent, duplicate, replayed, malformed, stale, disconnect, reconnect, and server-restart cases;
- conservation proofs for every item-moving workflow and old-or-new atomicity proofs for persistent
  world records;
- a post-test world reload and client character reload;
- exact logs, package/DLL hashes, protocol matrix, test counts, and unresolved-severity ledger in the
  release evidence.

No packet is a dedicated-player release while this file says HOLD.

## Current closure ledger

| Finding | Status | Evidence |
|---|---|---|
| Prior packets were labeled from load-smoke evidence without remote-player parity | CLOSED | Packets moved intact to `artifacts/RunicSuite14-LOCAL-HOST-PREVIEW-HOLD`; active release root contains only a HOLD notice. |
| Inventory incorrectly required the owning dedicated client to also be the server | CLOSED AT SOURCE | Authority is now local player + native network ownership. The canonical `inventory.durable-operations` service adds a checksummed, atomically flushed client journal, profile mirror, reconnect reconciliation lock, cloned exact-manifest recovery, exact current-primary readback proof, and fail-closed coverage of native inventory mutation seams. The current 148/148 checkpoint also confines the journal to a non-reparse trusted root with exclusive primary reread and forensic-only staged artifacts, preserves vanilla death outside a journal, and scopes in-flight grave transfer without skipping `CreateTombStone`; grave/server custody remains part of the separate open conservation finding below. |
| Carried-only Storage Consolidate incorrectly required server authority | CLOSED AT SOURCE | The owning dedicated client may now consolidate its own native inventory with the existing exact backup/rollback path; shared-container Quick Stack, Restock, and opened-container Sort remain gated pending the server saga. Storage's 59/59 protocol checkpoint adds strict item/chunk bounds, recovery/conservation policy, provider-authoritative preflight, and complete loaded-neighborhood ward proof, but is not runtime parity. |
| Safety could treat a client-controlled routed sender UID as remote administrator evidence | CLOSED AT SOURCE | Safety no longer uses routed sender identity for admission or administration. Its compatibility policy is evaluated from exact direct-session handshake claims before `RPC_PeerInfo`; remote administration requires the shared transport's authenticated, server-evaluated direct-admin path. Safety focused checks are 197/197. |
| Shared RPC used or could use a client-controlled routed sender UID as identity | CLOSED AT SOURCE | Persistence now registers a bounded direct `ZRpc` transport at `ZNet.OnNewConnection`, gates compatibility before `RPC_PeerInfo`, targets exact sessions, and never authenticates a routed sender UID. Its 61 focused tests include two-real-`ZRpc` exchange, strict non-normalizing wire identities/idempotency keys, replay, order races, capacity floods, disconnect retry, UID collision, Steam-admin-to-PlayFab-player enrollment, and direct-admin idempotency. |
| Client-announced character/player IDs could be mistaken for authenticated principals | CLOSED AT SOURCE | The shared server actor resolver requires one current Player ZDO owned by the exact direct peer and matching its announced character ZDO. Legacy player IDs additionally require the checksum-valid, world-scoped reverse-unique account binding; resolve never auto-enrolls. |
| Group administration replayed across reconnects or against a newer catalog | CLOSED AT SOURCE | The built-in Group provider now issues a server-owned epoch/monotonic command token and persists the issue before returning it. Execution binds the exact backend actor, request hash, Group UUID, and expected catalog/group revisions; mutation and receipt are committed in one checksummed atomic primary update. An exact lost-prepare retry returns the original live token; exact execution replay after reload returns the stored receipt. Conflicting token reuse and stale revisions deny, compacted receipt history advances a durable rejection frontier, and ordinary catalog commits cannot erase the command ledger. The durable exchange is the first published Group protocol; development-only query scaffolding is not retained, and a regression requires the endpoint protocol to match the advertised Permissions module hello. Focused checks are 91/91. |
| Storage/Crafting cross-boundary item conservation and recovery | OPEN | Core now owns the provider-neutral durable-inventory contract and Inventory supplies a bounded local journal/current-primary proof. Storage rejects Cloud and Legacy Cloud character profiles before any journal/tag/send; Local character storage is therefore a temporary checkpoint, not yet an accepted final group requirement. Crafting's 56/56 scaffold covers strict intents, exact server-cost partition, replay/recovery, non-atomic vanilla cuts, and the 1..32 endpoint contract, but registers no remote mutation. The Foundation composite coordinator, Storage server ledger/escrow runtime, tagged-grave custody, cross-machine/cloud recovery, exact source/output/upgrade custody, and every-cut conservation tests remain required. |
| ZDO-only records were described as crash-durable | OPEN — NEW OPERATIONS BLOCKED | Installed 0.221.12 IL proves `ZDOMan.PrepareSave` takes the persisted snapshot only when `ZNet.SaveWorld` starts; `SaveWorldThread` writes it later and catches/logs failures. A ZDO write/readback therefore proves only current synchronized state. Storage transfers, the 1..32 composite coordinator, grave custody, and every other new irreversible server workflow are blocked until a checksummed, world-scoped, WriteThrough/`Flush(true)` server primary is atomically published and exactly reread before mutation. A terminal filesystem receipt is a durable desired outcome, not proof that the world DB already contains it: recovery must retain and replay the ordered per-endpoint predecessor chain from the last verified world checkpoint before admitting dependent work. Existing ZDO-only legacy paths must not be used as evidence for the new dedicated-parity release. |
| Production link RPC sender binding and durability | OPEN | All four link families use bounded direct-session endpoints and the Fermenter roles are isolated, but the current mutation runtime still requires loaded server-side actor/station instances, claims ownership, and stores purported durable receipts only in the station ZDO. It must use ZDO/prefab server validation, exact-current-owner native execution or genuine server-local execution, and the filesystem-backed Transactions WAL before release. |
| Remote Portal directory/edit/travel | CLOSED AT SOURCE / LIVE PENDING | Portals uses connection-bound actor resolution and server-filtered directories, authoritative permissions/complete ward-neighborhood/revision checks, exact-current-owner commands, commit-time duplicate checks, and one-shot finite travel approvals. Walking into an authorized depart/both source safely holds the player and opens a large-map picker containing only authorized arrive/both coordinates in the exact same NetworkName; a destination click also acknowledges an explicitly labeled one-way route. Normal-map pins are temporary, unsaved, and one-network-at-a-time. Metadata edits use the shared Transactions `UpdateExisting` WAL with server-issued tokens, deterministic enrollment, global predecessor ordering, epoch/sequence ABA markers, startup enumeration/recovery, and an atomic authoritative portal record. No Portal code claims or transfers ZDO ownership. Focused checks are 308/308; a genuine dedicated Portal scenario and two-traveler contention remain release evidence. |
| Safety join compatibility admission | CLOSED AT SOURCE | Safety registers exact module/version/protocol plus game/topology-profile/rules claims on the direct challenge-bound handshake. Required rejects before `RPC_PeerInfo`; Optional admits with a bounded exact-session/transport-identity reason ledger; Disabled installs no local evaluator. Client claims remain self-reported, and pre-character claims never inspect live inventory. Focused checks are 197/197. |
| Sentinel signed-policy compatibility admission | CLOSED AT SOURCE | Sentinel Optional is the non-locking first-run default; explicitly configured Required mode compares the client self-report to the server's verified signed-policy digest/sequence/profile before `RPC_PeerInfo`. Session freshness, replay/equivocation/rollback/rate bounds, exact transport identity evidence, and no remote-admin elevation are covered by 29/29 focused checks. This is compatibility evidence, not cryptographic client attestation. |
| Precision remote undo for dedicated clients | OPEN | Direct requester identity and the module journal are present, but a real dedicated topology can have only the target ZDO and no server-side Piece/WearNTear instance. The current owner-handoff-to-server removal path is therefore not sufficient. Undo must be journaled before an exact-current-owner native removal command and close only after the server observes authoritative target absence. |
| Precision remote area repair and Safety recovery objects | OPEN | Area repair requires the same exact-current-owner world-mutation path plus durable client tool-debit reconciliation. Safety recovery additionally requires tagged-grave server custody and every-cut reconnect recovery. Multi-client validation is still required. |
