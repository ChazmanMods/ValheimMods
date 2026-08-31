# Runic World Engine 1.0.0

Runic World Engine is a standalone, conservative world-state observatory for Valheim 0.221.12. It
measures aggregate ZDO population, peer count, create/destroy intervals, send/receive rates, and
save/load call duration at a hard maximum of one aggregate sample per second.
Its only runtime requirement is BepInExPack Valheim 5.4.2333.

## Authority and performance bounds

This release is observe-only:

- it never deletes, rewrites, compacts, quarantines, reprioritizes, or force-sends a ZDO;
- unknown third-party data is always preserved;
- it creates no persistent world keys of its own;
- it publishes no registry, capability, protocol, ownership declaration, or cross-mod service;
- it has no client UI or asset load, so dedicated servers use the same small runtime;
- optional summaries are off by default and rate-limited to 5–600 seconds.

The observatory uses constant-time collection counts and event counters. It does not perform a base
scan, sector walk, prefab enumeration, heat-map build, or save clone on the game thread. Save/load
completion cannot force an extra sample through the one-second ceiling. A failed sample is discarded
without changing world state or blocking any gameplay mod.

## Configuration

| Setting | Default | Meaning |
|---|---:|---|
| `General.Enabled` | `true` | Enables aggregate observation; false creates no patches or observatory state. |
| `Diagnostics.LogPeriodicSummary` | `false` | Writes bounded aggregate summaries. |
| `Diagnostics.SummaryIntervalSeconds` | `30` | Summary cadence, clamped to 5–600 seconds. |

Planned later work—world heat maps, compaction, and synchronization scheduling experiments—is not
claimed by 1.0.0 and remains disabled until separately designed and tested.
