# Runic Portals

Runic Portals 1.0.0 adds permission-aware named portal networks for Valheim 0.221.12. Ordinary
**Standard Pair** portals remain vanilla. A portal joins a Runic network only after an explicit edit.

The mod is independently installable. Its only runtime dependency is BepInExPack Valheim 5.4.2333;
it does not require Runic Core, Persistence, Permissions, Transactions, or another shared Runic DLL.

## Portal modes

- **Standard Pair** uses Valheim's normal tag pairing and teleport path.
- **Public** is the default and permits every player who passes the route and ward checks.
- **Private** permits only the portal's recorded Valheim player owner.
- **Group** permits current members of the group bound when the portal is edited. Membership comes
  from the authenticated server Group snapshot and fails closed while that snapshot is unavailable.

The recorded owner may edit any Runic portal they created, subject to native object ownership and
the local ward. Public/private/Group travel policy is evaluated independently from ward evidence, so
a ward or stale route is not reported as an owner or Group permission failure.

Arrival and departure are independent. A network endpoint may be `both`, `arrive`, or `depart`.
Routing always stays inside the exact case-sensitive NetworkName.

## Use

Aim at a portal to see its setup panel. Normal Use edits a Standard Pair tag. Alternate Place + Use
opens the Runic editor. With Valheim's default keyboard bindings these are E and Left Shift + E.

Enter one of these one-line commands:

- `network|public|NETWORK|NAME|both`
- `network|private|NETWORK|NAME|both`
- `network|group|NETWORK|NAME|both`
- `standard`

The legacy public form `network|NETWORK|NAME|both` remains supported. Replace `both` with `arrive`
or `depart` as needed. A connected Standard Pair must first be disconnected with a unique vanilla
tag before conversion. Replacing existing Network metadata or restoring Standard mode requires the
same command a second time within four seconds.

A Group command binds the player's currently active group; no group UUID is typed into the portal
editor. Use normal chat commands such as `/group create`, `/group list`, `/group use`, `/group
invite`, `/group accept`, and `/group leave`. `/group` displays the complete command syntax for
member roles, removal, ownership transfer, rename, invitation cancellation, and deletion.

Walking into an authorized `depart` or `both` endpoint opens a map picker containing authorized,
online `arrive` or `both` endpoints in the same NetworkName. The local player is held in place while
the picker is open. Clicking a marker selects the destination and acknowledges a one-way route; Esc
cancels. The final action rechecks the source, destination, permissions, wards, revisions, and
Valheim's native teleportability rule before calling the local player's native teleport path.

The source portal and portal edits require definite local ward permission. A distant destination's
zone is normally unloaded before teleport, so the absence of a local `PrivateArea` instance is not
treated as a destination denial. If the destination ward is loaded and is known to deny the player,
the route remains blocked. The same rule is used by the picker and world-wide directory, allowing
ordinary long-distance Public, owner, and current-member Group routes to remain discoverable.

On Valheim's normal large map, unmodified P toggles a temporary world-wide portal directory. It
includes Standard Pair portals and Runic endpoints visible to the current player. Temporary pins are
never saved or shared.

## Ownership and multiplayer

Portal metadata changes run only when Valheim says the local portal `ZNetView` is its current owner.
Travel runs only for the locally owned player. Runic Portals never calls `SetOwner` or
`ClaimOwnership`, and it does not add portal edit, directory, travel, or owner-command RPCs. If
native ownership or current replicated evidence is unavailable, the action is rejected without
creating a deferred operation.

Public, private, and Group checks are evaluated directly inside this DLL. Ward checks use the
currently loaded native `PrivateArea` state at the action point. A known hostile destination ward
fails closed; an unloaded remote zone is governed by its explicit portal policy rather than being
mistaken for a permission denial. Route plans and map visibility are advisory; authority is reread
immediately before a metadata change or teleport.

Remote `/group` commands and membership refreshes use the mod-owned RPC names
`RunicPortals.Groups.Request.v1` and `RunicPortals.Groups.Response.v1`. The server derives the actor
from the current `ZNetPeer`, its exact owned character ZDO, the Player prefab, and the ZDO's positive
`s_playerID`; payloads do not supply an authority identity. The channel is bounded to 32 pending
requests, 128 thirty-second replay entries, 32 KiB envelopes, two attempts in a six-second request
window, and short-lived in-memory snapshots. Timeouts and shutdown clear their own state and never
lock inventory, tools, or unrelated gameplay.

## Persistent compatibility

Portal world data keeps the existing `runic.portals.*` ZDO keys, schema-1 compatibility reads, and
schema-2 record format. Removing the DLL does not delete that metadata; Valheim simply ignores it.

Group membership and active selection keep the prior catalog codecs and world-scoped location under
`BepInEx/config/RunicPermissions/groups`. These are genuine feature data. They are written
synchronously with bounded files and atomic replacement; there is no gameplay transaction journal,
durable command ledger execution, quarantine, account binding, join-time recovery, or global lock.

Runic Portals also exposes `RunicPortals.Api.GroupIntegrationApi.TryIsMember` so another gameplay mod
may query local membership through optional reflection. No mod is required to consume it.

## Bounds and defaults

- Editor: 256 characters; network IDs and names: 64 characters each.
- Indexed portal ZDOs: 4096; graph endpoints: 2048.
- `MaximumNetworkEndpoints`: 1024 by default, clamped to 16-2048.
- `CyclePageSize`: 32 by default, clamped to 1-128.
- Return and selection caches: 256 identities each, memory only.
- Return route lifetime: 15 minutes; one-way acknowledgement: 10 seconds.
- Index refresh: 2 seconds; edit range: 5 metres.

Malformed, stale, unknown-schema, disabled, destroyed, offline, or unauthorized endpoints are
excluded. Standard Pair discovery remains Standard-to-Standard and never routes through a Runic
network.
