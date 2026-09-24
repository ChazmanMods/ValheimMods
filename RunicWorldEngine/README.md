# Runic World Engine 1.2.3

A Valheim world is constantly synchronizing objects, changing state, communicating with peers, and saving to disk. When those systems collide with an expensive frame or overlapping save work, everyone can feel the hitch even though nobody did anything wrong.

**Runic World Engine watches that infrastructure, offers a coordinated player-cap override, and smooths asynchronous saves.** See which connections are struggling, spot growing queues, and receive early warnings without changing world ownership or synchronization behavior.

Version 1.2.2 is verified with Valheim 1.0.15 and requires BepInExPack Valheim 5.4.2350, including support for the chunked-world save and load pipeline.

## Major features

- Observe aggregate ZDO population, peer counts, traffic, and lifecycle rates.
- Optionally configure a 2–64 player cap with coordinated admission, Steam, and PlayFab limits.
- View each connected peer's RTT, send/receive rate, queued traffic, and heartbeat age.
- Observe ownership assignments, releases, transfers, and rapid repeated transfers.
- Identify possible synchronization starvation and sustained unhealthy server conditions.
- Measure save and load call duration without scanning world contents.
- Coalesce overlapping asynchronous periodic save requests.
- Briefly defer save preparation after an already-expensive frame.
- Offer optional, rate-limited diagnostic summaries.

## How it feels in-game

There is no new gameplay interface demanding attention. The world continues to save normally, but avoidable overlapping work and poorly timed preparation are reduced so persistence announces itself less dramatically.

## Quick setup

Install on the **dedicated server or the player hosting the world** for server-wide monitoring and the player-cap override. Connecting clients do not need World Engine for those features. An optional client installation reports that client's local connection only; it does not fetch the server's telemetry.

Start once to generate `BepInEx/config/chazman.RunicWorldEngine.cfg`. To host up to 20 players, configure the host and restart it:

```ini
[Player Capacity]
Enabled = true
MaximumPlayers = 20
```

The override is off by default. The configured cap is frozen at startup. A dedicated PlayFab host gets a separate reserved transport slot; that slot does not reduce the human player cap. Raising the cap does not add CPU or bandwidth capacity.

In the local F5 console, enter **`runicworld_status`** for the most recent read-only report. No devcommands or administrator grant is needed to inspect your own process. On a headless server, enable log summaries instead:

```ini
[Diagnostics]
LogPeriodicSummary = true
SummaryIntervalSeconds = 30
LogPeerDetails = true
```

Read the server's BepInEx log for its complete report. Peer details include player names and peer IDs, but no IP addresses, passwords, or authentication tokens. Warnings are enabled by default even when periodic summaries are off.

## Player-cap validation

World Engine audits the Valheim 1.0.12 / 1.0.14 method bodies and every identified literal hosting-limit site before installing the override: five sites on a listen host, six on a dedicated server. The dedicated set includes Steam's initial maximum-player setting. It preserves the old remote-server browser fallback rather than changing the displayed capacity of other servers.

The override requires the complete audited set. Unknown method bodies, missing sites, additional detected limit sites, or competing transpilers reject the override and block hosting until corrected and restarted. Integrity is checked again before hosting and admission. If the patch set changes afterward, new admissions are blocked and a restart-required error is logged; existing players are not kicked and the cap is not silently reduced. A rejected connection may receive Valheim's server-full message; the host log gives the actual integrity error.

This validates the audited game-side limit patches, not arbitrary behavior added by other plugins or external platform-service limits. Authentication and character-vault checks remain in effect. Do not combine this override with another player-limit mod. A new game version alone does not disable the override. If the audited capacity method bodies change, the override requires a fresh code audit before it can run.

## Reading health reports

- **RTT:** Steam's native ping or PlayFab Party's per-endpoint round-trip latency. `n/a` means unavailable or not yet measured, not zero.
- **Send/receive:** bytes per second. Steam reports native transport rates; PlayFab reports socket payload rates, not compressed wire bandwidth. These are not directly interchangeable.
- **Queues:** native queued bytes and unacknowledged/in-flight bytes are shown separately from application queued messages. PlayFab in-flight bytes are an outstanding payload indicator, not an exact wire backlog; layers can overlap and should not be added as a bandwidth measurement.
- **Priority/invalid ZDOs:** pending priority synchronization work and invalidation notices, not every unsent world object. An idle connection with no pending work is not starvation.
- **Send-window pressure:** queued application sends or traffic approaching Valheim 1.0.12's ZDO send-budget boundary. The PlayFab reading uses the game's discounted queue estimate; it is not an exact wire measurement.
- **Possible starvation:** pending priority/invalidation work or send-window pressure with no successful ZDO send for the configured interval. It is an indicator, not proof that a specific object is stuck.
- **Ownership:** read-only transition counters. Rapid-transfer detection tracks up to 1,024 object IDs over a ten-second window; assignments and releases are counted separately.

Warnings cover sustained high RTT, queue growth, overdue heartbeats, slow mean frames, rapid ownership churn, and possible starvation. Network warnings allow a 15-second peer warm-up. Conditions must persist for five seconds by default; repeated warnings are limited to once per minute per category, with a recovery message after sustained improvement. High player occupancy is reported when the validated override is active. Thresholds are configurable under `[Server Health]`.

## Safety and compatibility

Valheim remains responsible for snapshot preparation and disk writing. Synchronous shutdown saves are never deferred, and periodic deferral has a hard maximum. World Engine never deletes, rewrites, compacts, reprioritizes, or force-sends world data; unknown mod data is preserved. It has no client UI or custom protocol and runs on dedicated servers with the same small footprint.

## Authority and performance bounds

World-data handling remains conservative:

- it never deletes, rewrites, compacts, quarantines, reprioritizes, or force-sends a ZDO;
- unknown third-party data is always preserved;
- it creates no persistent world keys of its own;
- it publishes no registry, capability, protocol, ownership declaration, or cross-mod service;
- it has no client UI or asset load, so dedicated servers use the same small runtime;
- optional summaries are off by default and rate-limited to 5–600 seconds.

The world observatory uses constant-time collection counts and event counters. Network health samples
at most 128 peers per second and keeps a bounded ownership history. It does not perform a base
scan, sector walk, prefab enumeration, heat-map build, or save clone on the game thread. Save/load
completion cannot force an extra sample through the one-second ceiling. A failed sample is discarded
without changing world state or blocking any gameplay mod.

## Save smoothing

Asynchronous periodic saves are coalesced while an earlier save is still active and their
main-thread preparation is deferred briefly when the previous frame exceeded the configured frame
budget. The deferral is bounded (five seconds by default), so a persistently busy server still saves.
Synchronous shutdown saves are never deferred. The smoother never touches Unity objects from a
worker thread; Valheim retains ownership of both snapshot preparation and its normal disk writer.
This reduces avoidable save-time spikes, but no mod can promise a literally zero-frame hitch for
every world size and storage device.

## Configuration

| Setting | Default | Meaning |
|---|---:|---|
| `General.Enabled` | `true` | Startup gate for the mod; false creates no patches or observatory state. Restart to change. |
| `Diagnostics.LogPeriodicSummary` | `false` | Writes bounded aggregate summaries. |
| `Diagnostics.SummaryIntervalSeconds` | `30` | Summary cadence, clamped to 5–600 seconds. |
| `Diagnostics.LogPeerDetails` | `false` | Include per-peer details in periodic summaries. |
| `Player Capacity.Enabled` | `false` | Opt in to the audited cap override; restart required. |
| `Player Capacity.MaximumPlayers` | `10` | Human player cap, 2–64; restart required. |
| `Server Health.Enabled` | `true` | Enable read-only connection and ownership monitoring. |
| `Server Health.Warnings` | `true` | Enable sustained warnings in the host log. |
| `Save Smoothing.Enabled` | `true` | Coalesces overlapping asynchronous save requests and waits for a stable frame. |
| `Save Smoothing.FrameBudgetMilliseconds` | `24` | Preferred maximum previous-frame duration before save preparation starts. |
| `Save Smoothing.MaximumDeferralSeconds` | `5` | Hard maximum delay before a pending save must start. |

World heat maps, compaction, and synchronization rescheduling are not enabled by this mod.

Compatibility: startup validates required game APIs rather than rejecting an unfamiliar game version. Actual API incompatibilities still disable safely.

## Language files

This version follows Valheim's selected language using files in `Translations/RunicWorldEngine` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
