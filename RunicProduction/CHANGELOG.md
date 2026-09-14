# Changelog

## 1.0.7 - 2026-09-14

- Fixed production stopping after the player who configured it leaves and station/chest ownership splits between peers.
- Added coordinated chest handoff for linked ingredients, fuel, outputs, replenishment, and eligible nearby recipe inputs.
- Removed the original player's presence requirement from pending and legacy replenishment activation.
- Preserved ward and personal-chest access checks, shared-chest serialization, and the link-click fixes from 1.0.6.

## 1.0.6 - 2026-09-14

- Fixed a repeated mouse-press signal across frames cancelling a newly selected production link.
- Kept each accepted linking click captured until its button is released and the press signal clears, including fast clicks.
- Preserved normal station-to-chest linking, Shift+Alt unlinking, and deliberate cancellation with a separate click.

## 1.0.5 - 2026-09-11

- Fixed production's native item-status checks after Valheim 1.0.12 changed the underlying game API. Supports both the 1.0.7 field and the 1.0.12 property without changing the game's item-status rules.

## 1.0.4 - 2026-09-10

- Fixed production link setup rejecting players who have chest and ward access but do not already own the station and chest's network state.

## 1.0.3 - PreProduction

- Read container contents from Valheim 1.0's byte-array storage rather than the retired string field.
- Accept the exact native one-load durability conversion while rejecting unrelated inventory differences.
- Check the replicated integer busy flag before and after synchronization.
- Cross-peer station/chest ownership remains a limitation; this update does not add remote transfers.

## 1.0.2 - 2026-09-09

- Updated cheated-state provenance, queue, slot, item insertion, and inventory notification integration for Valheim 1.0.
- Re-audited the installed Valheim 1.0.7 client and dedicated-server assemblies and updated the BepInEx dependency to 5.4.2350.

## 1.0.1 - 2026-09-04

- Fixed one Alt+mouse press being observed by both `Player.SetControls` and `Player.Update`, which
  could immediately cancel a newly armed production link before the player could select a chest.
- Kept an accepted link gesture captured until its physical mouse button is released, preserving
  action suppression and the intended two-step station-to-chest workflow.
- Rewrote the package introduction and description around player-facing production automation,
  connected workflows, and existing Valheim machines while retaining the technical safety details.

## 1.0.0 - 2026-08-29

- Made the plugin independently installable with BepInEx as its only runtime dependency while
  keeping version 1.0.0 and the established package identity.
- Removed the shared registry, capability negotiation, permissions service, transaction composites,
  remote-owner handoff, custom Production RPC transport, persistent operation records, recovery,
  claims, global inventory guards, and account/global quarantine paths.
- Rebuilt smelter, cooking, recipe-station, and fermenter automation around Valheim's exact loaded
  native owner. No path claims or transfers ZDO ownership.
- Kept persistent explicit links, stable endpoint tokens, bounded multi-destination catalogs,
  physical exemplar plans, target signatures, and fairness cursors in their existing world-data
  formats.
- Kept valid singleton-link migration. Tokenless singleton links upgrade only from one exact loaded
  local-owner chest, while tokenless catalog destinations require an explicit refresh.
- Added bounded, expiring local link selections with fresh range, chest, ward, access, identity,
  creator, ownership, and synchronization checks at commit and use.
- Preserved throughput Input, Fuel, and Output links, complete-batch smelter fallback, exact reserves,
  direct recipe replenishment, cooking/fermenting replenishment, and optional bounded nearby
  ingredient sourcing.
- Added exact persisted-state compensation for multi-object mutations without leaving restart-time
  work or locking unrelated inventory. A proven rollback retains vanilla smelter fallback; an
  indeterminate publication suppresses duplicate fallback and pauses only that station until reload.
- Replaced obsolete transport and crash-recovery coverage with focused standalone, native-owner,
  persistence-format, boundedness, rollback, and vanilla-fallback tests.
- Added exact native Fireplace fuel service, so explicitly linked fires draw Wood and lamps draw
  Resin from player-facing Input chests with the existing reserve and rollback rules.
- Added station-specific hover guidance for Input, throughput Output, and exemplar-backed
  Replenishment setup; enabled bounded nearby recipe ingredients by default.
- Verified exemplar-driven food and mead-base recipes plus fermenter input and matching multi-chest
  mead output in the focused workflow regressions.
- Added exact station-level Alt mouse links: Left selects Input, Right selects Output, and Middle
  selects Replenishment. Repeating the same gesture on a chest within 30 seconds commits it;
  Shift+Alt at both steps removes that exact role link.
- Reserved accepted mouse gestures through release so combat and build actions cannot leak through,
  and added vivid green Input, yellow Output, and turquoise multi-Replenishment chest rings on
  successful links and while their station is pointed.
- Made role selection independent of a station's physical sub-control: aiming anywhere on the
  station uses the mouse button alone to select Input, Output, or Replenishment.
- Added multiple persistent chests per role, station hover counts for all three roles, one unified
  per-role link limit, reserve-driven dynamic exemplar refresh, and server-side reconciliation.
- Added exact composite nearby recipe sourcing across accessible shared chests, protected linked
  destinations, in-flight reserve admission, completed-output routing, and rollback readback guards.
- Made empty Replenishment links crash-safe by publishing their signed principal plan while inert
  before the authoritative role link. Orphan plan bodies remain inert, and no link success is shown
  unless both publications complete.
- Fixed linked-chest rendering by placing each LineRenderer on its own child GameObject. An invalid
  root or ring now tears that marker down once, preventing repeated UpdateVisuals exceptions without
  affecting Production links or transfers; a later lease may create a fresh marker.
- Separated Fuel Input from ingredient Input when the player aims Alt+Left Mouse at a native fuel
  control, with distinct selection, link, removal, and hover text while retaining legacy Input-fuel
  compatibility until an explicit Fuel Input link is made.
- Enabled Output links for allowed cauldron, mead-kettle, and preparation-table recipe stations.
- Expanded ordinary cooking-station and oven ingredient sourcing from an explicit Input link to the
  same bounded eligible nearby-chest pool used by recipe automation, and exposed exact validation
  failures through the station status and verbose log instead of silently doing nothing.
- Allowed one physical chest to carry independent roles, including serving as one station's Output
  and another station's Input for true chained production lines such as mead ketill to fermenter.
- Kept finite native oven fuel states above the display capacity operational; automation stops
  adding fuel at capacity without disabling food input, cooking, or cooked-output routing.
