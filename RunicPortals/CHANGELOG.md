# Changelog

## 1.0.0 - 2026-08-29

- Fixed the first network-portal entry on a dedicated client reporting that no destinations were
  available until the player had traveled through a vanilla portal. The authenticated client now
  asks the server to force-send only the matching portal records it may discover and arrive at,
  then waits briefly for the complete authorized network before drawing the picker map.
- Kept the first-use directory request bounded, retry-limited, source-revision checked,
  peer-character authenticated, network-scoped, and filtered by portal and ward permissions.

- Fixed dedicated-client long-distance Runic routes being denied whenever the destination zone was
  unloaded and therefore had no local `PrivateArea` evidence.
- Separated Public/private/Group policy authorization, source ward authorization, destination ward
  authorization, and route-revision evidence so failures report the correct reason.
- Public remains the default travel policy; Private remains recorded-owner-only; Group travel uses
  the current authenticated server membership snapshot and fails closed when it is unavailable.
- Kept source/edit wards strict and kept known hostile loaded destination wards denied, while an
  unloaded remote destination is no longer rejected solely because its ward state is unknown.
- Applied the same remote-destination rule to the picker, world-wide directory, and final commit.

- Made Runic Portals independently installable with BepInEx as its only runtime dependency.
- Removed the Core registry, capability negotiation, protocol admission, account binding, portal
  transport, durable edit intents, mutation WAL, recovery, quarantine, and source-protection leases.
- Moved portal edits and travel to Valheim's native local portal/player ownership paths without
  claiming or transferring ownership.
- Kept Standard Pair routing separate from public, private, and Group named networks.
- Kept permission-filtered map directories, walk-in destination selection, direction rules,
  one-way acknowledgement, return routes, setup UI, and bounded diagnostics.
- Compiled the limited group domain code needed by this mod into `RunicPortals.dll`; no shared Runic
  runtime assembly is produced.
- Added one bounded, namespaced Group request/response channel. The server authenticates its actor
  from the current peer's owned Player character ZDO and keeps request/replay state only in memory.
- Preserved existing portal ZDO keys and record schemas plus the prior world-scoped group catalog and
  active-selection file formats and locations.
- Added an optional reflection-friendly Group membership API for other gameplay mods.
- Replaced the obsolete durability and remote-owner harness with 19 focused format, routing,
  ownership, RPC-authentication, and architecture tests.
