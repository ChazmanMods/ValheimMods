## 1.2.10

- Keep input binding identifiers stable when translated language files are installed.
- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.
- Destination pins no longer force a delayed map zoom-out or recenter while choosing a portal.

# Changelog

## 1.2.9 - 2026-09-18

- Replaced the exact game-version allowlist with startup API validation. Compatible future patches no longer require a version-only update.
- Incompatible APIs still fail closed; World Engine retains its exact method-body audit.

## 1.2.8 - 2026-09-18

- Added Valheim 1.0.15 support while retaining exact API checks and support for 1.0.7, 1.0.12, and 1.0.14.

## 1.2.7 - 2026-09-17

- Added Valheim 1.0.14 support while retaining compatibility with 1.0.7 and 1.0.12.
- Retained exact game API checks and rejection of unknown versions.
## 1.2.6 - 2026-09-14

- Include vanilla Standard Pair destinations in the Runic picker, dedicated-server directory, and final travel validation. Vanilla portals retain their normal tag pairing when entered.
- Show all authorized Public, owned Private, and current-member Group destinations across network names.
- Protect the local player from incoming hits and ongoing damage throughout an authorized Runic picker session, including directory loading; cancel restores normal damage and travel uses native teleport protection.
- Expand directory capacity to the graph's 2,048 endpoint ceiling rather than truncating at 128 destinations (the configured graph limit still applies).
- Recheck source discovery and departure access before opening the protected picker.

## 1.2.5 - 2026-09-14

- Include Public Runic destinations from every network alongside authorized destinations in the entry portal's network.
- Apply the same destination scope to the picker, dedicated-server directory, name lookup, and travel validation.
- Warn when crossing to a public destination leaves no reverse route to the original private or group network.
- Keep permission, ward, availability, revision, and native travel restrictions in place.

## 1.2.4 - 2026-09-11

- Added group-invitation popups showing the inviter and group, with Accept and Decline buttons.
- Accept joins the group and selects it as your active group; Decline removes only your invitation.
- Added `/group decline <group name>` as a chat alternative.
- Invitations wait while other menus are open and are checked again by the server when answered.
- Cancelled, expired, and replaced invitations cannot be accepted through an old popup.

## 1.2.3 - 2026-09-11

- Fixed portals and group features being disabled by the Valheim 1.0.12 version check. Retained Valheim 1.0.7 support.

## 1.2.2 - 2026-09-10

- Fixed group invitations and `/group whoami` failing for characters with valid negative Valheim IDs.
- Fixed those characters being excluded from group membership, the group dropdown, and portal access/map checks.
- Added explicit character-readiness errors instead of silently dropping group requests.
- Clarified when an invited player is connected but their character identity is not yet available.

## 1.2.1 - 2026-09-09

- Updated dialog, statistics, map-coordinate, pin-removal, and portal-registry integration for Valheim 1.0.
## 1.2.0 - 2026-09-08

- Added a native in-game portal editor with a Standard Pair checkbox, separate Network and Portal
  Name fields, Public/Private/Group access controls, and arrival/departure direction choices.
- Added an authenticated Group dropdown populated from the player's chat-managed groups, removing
  the need to enter internal group identifiers.
- Standard Pair editing now uses the same form while preserving Valheim's 10-character portal-tag
  limit and vanilla pairing behavior.
- Kept destructive mode or metadata changes behind a visible second-save confirmation window so an
  established route is not replaced by a stray click.

## 1.1.4 - 2026-09-05

- Reworked the Thunderstore description and README opening to lead with the player problem, the mod's core benefit, major features, in-game feel, and then safety and compatibility details.
- Description-only package update. The plugin DLL and gameplay behavior are byte-for-byte unchanged from 1.1.3.

## 1.1.3 - 2026-09-01

- Fixed first-use network portals incorrectly reporting `source-ward-denied` while the dedicated
  server was still loading the source zone's native ward objects.
- Kept source ward checks fail-closed: an ambiguous source requests the exact nearby server zone
  and receives up to eight bounded retries over eight seconds, while a known hostile ward is still
  rejected immediately as `source-ward-denied`.
- Transient missing ward evidence is now reported accurately as `source-ward-unavailable` instead
  of being mislabeled as a permission denial.

## 1.1.2 - 2026-09-01

- A valid 1.1.0 directory request received by a 1.1.2 server is now rejected with the explicit
  `client-update-required` compatibility reason instead of being counted as malformed cheat
  evidence. The older protocol still cannot authorize or enumerate portals.
- Identity-unbound requests during the short connection/character handoff remain denied and
  recorded, but no longer contribute high-confidence automatic-disconnection strikes. Truly
  malformed envelopes, malformed authenticated commands, and replay conflicts retain escalation.

## 1.1.1 - 2026-09-01

- Fixed first-use Runic portal entry on a dedicated server by binding the request to the exact
  nearby source ZDO and using the server record as authority. Client revision/network values are
  now freshness hints, so normal replication lag no longer becomes `source-unavailable`.
- Fixed the normal large-map P directory so it warms authorized world-wide portal records from the
  authoritative server instead of showing only zones that this client had previously visited.
- Added bounded, authenticated, read-only map-directory transport and precise server rejection
  diagnostics without adding any portal mutation or ownership-transfer RPC.

## 1.1.0 - 2026-08-31

- Added an optional, reflection-only Runic Sentinel security bridge without making Sentinel a
  required dependency. Rejected malformed, identity-unbound, or replay-conflicting portal requests
  can now contribute trusted evidence to Sentinel's bounded automatic-enforcement policy.
- Kept ordinary portal permission denials out of cheat escalation: being outside a ward, group, or
  network permission is a normal authorization failure and is never treated as proof of cheating.

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

- Added one bounded, namespaced Group request/response channel. The server authenticates its actor
  from the current peer's owned Player character ZDO and keeps request/replay state only in memory.
- Preserved existing portal ZDO keys and record schemas plus the prior world-scoped group catalog and
  active-selection file formats and locations.
- Added an optional reflection-friendly Group membership API for other gameplay mods.
