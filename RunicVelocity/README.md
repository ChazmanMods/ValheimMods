# Runic Velocity 1.0.3

A heavily modded Valheim profile can spend a long time between pressing Play and reaching the world, with little indication of what happened or what changed since the previous launch.

**Runic Velocity makes startup behavior measurable.** It records meaningful loading milestones and builds a validated local manifest of installed plugin files so unchanged metadata can be recognized and startup changes can be diagnosed.

## Major features

- Record bounded milestones from plugin entry through player readiness.
- Inventory installed plugin identities, versions, dependencies, and file hashes.
- Reuse validated cache records only when file evidence still matches.
- Detect changed, replaced, truncated, corrupt, or oversized inputs safely.
- Keep diagnostics local without publishing a cross-mod authority service.

## How it feels in-game

Velocity is quiet during play. Its value appears when you are maintaining a large profile: startup becomes something you can inspect rather than guess about, and unchanged plugin information does not have to be treated as unknown every time.

## Safety and compatibility

The background scanner never loads third-party plugin code, resolves plugins on their behalf, calls Unity from its worker, changes gameplay, or authorizes a server connection. Invalid cache evidence is discarded and rebuilt. Velocity is a diagnostic and validated-cache layer—not a promise that every profile will load faster—and requires only BepInEx.

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

## Language files

This version follows Valheim's selected language using files in `Translations/RunicVelocity` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.

## Support My Work

Enjoying the mods? You can support my work and future creations. Thank you for playing!

[Support My Work](https://buymeacoffee.com/the_artful_engineer)
