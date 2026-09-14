# Runic Portals

## Group invitations

Use `/group invite <player name>` as usual. The invited player receives a popup with the
inviter's name, the group name, and **Accept** / **Decline** buttons. Accept joins and selects
the group; Decline dismisses the invitation without joining. Escape also declines.

Invitations normally appear within five seconds and wait until chat or other menus are closed.
Multiple invitations appear one at a time. The existing `/group accept <group name>` command
still works, and `/group decline <group name>` is also available.

Install RunicPortals **1.2.4 on the server/host and participating clients** for invitation popups.
Older servers continue using the existing chat workflow.

A few paired portals are convenient. A large world turns them into a switchboard of duplicated frames, temporary tags, and rooms built mainly to hold transportation infrastructure.

**Runic Portals lets explicitly configured portals join named travel networks.** Choose a destination from the network you are using, control who may travel, and decide whether an endpoint handles arrivals, departures, or both.

## Major features

- Create case-sensitive named portal networks and destinations.
- Use public, private, or authenticated group access.
- Configure endpoints for arrival, departure, or two-way travel.
- Browse authorized destinations from an in-world or map-based picker.
- Keep ordinary Standard Pair portals completely vanilla.

## How it feels in-game

Your portal room can serve your world instead of growing with every destination. Walk to a network endpoint, choose an authorized place, and travel without retagging half the building or maintaining a permanent physical pair for every route.

## Safety and compatibility

A portal joins Runic networking only through an explicit edit. Travel rechecks route state, source distance, endpoint revision, ownership, permissions, and wards; ambiguous or stale routes fail closed. Unconfigured portals retain Valheim's normal tag pairing and teleport behavior. The mod is independently installable with BepInEx.

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
opens the Runic Portal editor. With Valheim's default keyboard bindings these are E and Left Shift +
E.

The editor keeps setup point-and-click:

- Check **Standard Pair** and enter a normal Valheim portal tag to use vanilla pairing. Standard
  tags keep Valheim's 10-character limit.
- Leave **Standard Pair** unchecked to enter a **Network** and **Portal Name** for Runic routing.
- Choose **Public**, **Private**, or **Group** access. Group access provides a dropdown containing
  the authenticated groups available to the current player; no internal group identifier is typed.
- Choose **Both**, **Arrivals Only**, or **Departures Only** to control how the
  endpoint participates in its network.

Groups are still created and managed through chat. Use commands such as `/group create`, `/group
list`, `/group use`, `/group invite`, `/group accept`, and `/group leave`. `/group help` lists the
other management commands. Reopen the portal editor after changing membership if the group list
needs to refresh.

Invite using the connected character's name, for example `/group invite Bulvye`, not their Steam
display name. The invited player joins with `/group accept Builders` (replace Builders with the
group name). `/group whoami` returns the exact `valheim.player:...` identity, which also works as
an invitation target. Negative character IDs are valid; keep the minus sign. If a character is
still spawning or synchronizing, the server reports that state so you can retry after it finishes.

Saving a new setup applies immediately. Replacing existing Runic metadata or returning a configured
portal to Standard Pair mode asks for the same save a second time within four seconds. This visible
confirmation protects established routes from accidental replacement while keeping all editing in
the form.

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

On Valheim's normal large map, unmodified P toggles a temporary world-wide portal directory. On a
dedicated server, opening the map requests a bounded authenticated directory warm-up so the result
does not depend on zones that this client has already visited. It includes Standard Pair portals and
Runic endpoints visible to the current player. Temporary pins are never saved or shared.

## Ownership and multiplayer

Portal metadata changes run only when Valheim says the local portal `ZNetView` is its current owner.
Travel runs only for the locally owned player. Runic Portals never calls `SetOwner` or
`ClaimOwnership`, and it does not add portal edit or travel mutation RPCs. Its bounded directory RPC
only force-sends already-authorized portal records; it never mutates a portal. If native ownership
or current replicated evidence is unavailable, a mutation is rejected without creating a deferred
operation.

Public, private, and Group checks are evaluated directly inside this DLL. Ward checks use the
currently loaded native `PrivateArea` state at the action point. A known hostile destination ward
fails closed; an unloaded remote zone is governed by its explicit portal policy rather than being
mistaken for a permission denial. Route plans and map visibility are advisory; authority is reread
immediately before a metadata change or teleport.

Remote `/group` commands and membership refreshes use the mod-owned RPC names
`RunicPortals.Groups.Request.v2` and `RunicPortals.Groups.Response.v2`. The server derives the actor
from the current `ZNetPeer`, its exact owned character ZDO, the Player prefab, and the ZDO's nonzero signed
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

- Standard Pair tags: Valheim's 10-character limit; Runic network and portal names: 64 characters
  each.
- Indexed portal ZDOs: 4096; graph endpoints: 2048.
- `MaximumNetworkEndpoints`: 1024 by default, clamped to 16-2048.
- `CyclePageSize`: 32 by default, clamped to 1-128.
- Return and selection caches: 256 identities each, memory only.
- Return route lifetime: 15 minutes; one-way acknowledgement: 10 seconds.
- Index refresh: 2 seconds; edit range: 5 metres.

Malformed, stale, unknown-schema, disabled, destroyed, offline, or unauthorized endpoints are
excluded. Standard Pair discovery remains Standard-to-Standard and never routes through a Runic
network.
