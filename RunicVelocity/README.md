# Runic Velocity 1.0.0

Runic Velocity is a standalone startup-measurement and local cache diagnostic. Version 1.0.0 is a
normal BepInEx plugin: it begins measuring when its own assembly is initialized, retains a bounded
local timeline, and builds an integrity-checked manifest of installed plugin DLLs on a background
worker. Its only runtime requirement is BepInExPack Valheim 5.4.2333.

## What it does now

- Records up to 64 named milestones from Runic Velocity entry through first update, main-menu,
  network-session, game-world, and local-player readiness.
- Scans at most 4,096 directories and a configurable 1–4,096 DLLs, never following directory reparse
  points. A single DLL over 512 MiB is rejected.
- Caches relative path, size, UTC modification time, SHA-256, BepInEx identity/version/dependencies,
  classification, and the last manifest-scan time in an 8 MiB bounded canonical cache.
- Reads that cache through an exact-length, metadata-stable stream; concurrent growth, replacement,
  truncation, or an appended byte discards it and triggers a clean rebuild.
- Reuses a cached hash and metadata only when path, size, modification time, and cached SHA format
  all validate. Changed files are hashed and parsed by Mono.Cecil from the same open byte stream, so
  the published hash and metadata describe one file image rather than two path lookups.
- Keeps its timeline and completed scan state private to the plugin; it publishes no registry,
  capability, protocol, or cross-mod status service.
- Treats path identity like the host filesystem: ordinal case-insensitive on Windows and ordinal
  case-sensitive on other supported hosts.

## Safety and honest boundaries

The scanner never loads plugin code, never resolves plugin dependencies, never invokes Unity from its
worker, and never changes gameplay or third-party initialization. Corrupt, oversized, incomplete, or
unreadable cache/file evidence is ignored or reported as a bounded partial manifest.

Cache schema 2 uses a strict, manually bounded UTF-8 string decoder; declared byte lengths are
validated against both the field limit and remaining payload before allocation. Schema-1 caches are
discarded and rebuilt. Cancellation is checked during enumeration, hashing, metadata walking, and
cache publication; a cancelled scan publishes neither a manifest snapshot nor a pending cache file.

This 0.1 plugin cannot observe BepInEx discovery before Runic Velocity itself loads, cannot make an
arbitrary third-party plugin initialize lazily, and is not a preloader. The full preloader, dependency
graph preparation, duplicate-profile repair, Safe Start, and Server Forge handoff remain future work.
One scan admits at most 4 GiB total and 512 MiB per DLL; changed-file hashing is cancellation-aware
at 128 KiB chunks, consumes exactly the pre-admitted length through one reused buffer, rejects any
early EOF or appended byte, and re-proves file size/time again after metadata inspection before a
cache record is published. Oversized dependency/identity metadata is rejected rather than truncated.
Local warm-cache reuse is a startup hint only: a fresh Sentinel or server challenge may require a new
hash and never trusts the local cache as authorization.

No client UI or asset bundle is loaded, so the same DLL is safe for a dedicated server.
Startup settings are sampled once when the plugin begins; change them before the next launch rather
than starting competing scans during an active boot.
`Enabled = false` is startup-inert: Runic Velocity creates no timeline or worker.
