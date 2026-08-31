# Changelog

## 1.0.0

- Kept constant-time aggregate ZDO observability and interval event counters.
- Kept installed-Valheim-verified save/load timing hooks with exception-preserving finalizers.
- Made `Enabled = false` startup-inert and deferred Harmony construction until after the setting gate.
- Verified exact installed field shapes and kept load completion within the one-second sample ceiling.
- Removed the Core registry, capability, protocol, service, and world-data ownership declaration
  architecture; BepInExPack Valheim is the only runtime dependency.
- Preserved all unknown data and retained no deletion, compaction, or sync-scheduling path.
