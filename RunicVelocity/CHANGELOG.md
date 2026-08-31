# Changelog

## 1.0.0

- Made `Enabled = false` startup-inert: no timeline or scan worker is created.
- Replaced the cache string reader with a strict bounded UTF-8 decoder that rejects oversized,
  truncated, invalid, and noncanonical length encodings before string allocation; schema is now 2.
- Made cache identity case-sensitive off Windows and case-insensitive on Windows.
- Hashes and parses metadata from the same open file stream, with cancellation gates through
  enumeration, metadata traversal, and atomic cache publication.
- Renamed the cached timestamp to the honest `LastManifestScanUtcTicks` and made diagnostic states
  distinguish fresh, warm, mixed, metadata-partial, bounded-partial, and cache-write-failed results.
- Removed the Core registry/capability/service facade and made BepInExPack Valheim the only runtime
  dependency.

## 0.1.0

- Made bounded hashing enforce the exact pre-admitted file length through one reusable 128 KiB
  buffer; early EOF, growth, or concurrent replacement now rejects the entry before cache publish.
- Replaced the cache reader's size-check-plus-`ReadAllBytes` race with an exact-length bounded read
  and final size/timestamp proof.

- Added a bounded local startup timeline.
- Added background, change-aware SHA-256/plugin-metadata manifest generation.
- Added a canonical 8 MiB cache with precise path/size/time invalidation and atomic publication.
- Added fail-closed caps for directory count, DLL count, file size, paths, fields, and dependencies.
- Documented the normal-plugin measurement boundary and deferred preloader/Safe Start work honestly.
- Hard-capped total admitted DLL bytes at 4 GiB, re-proved file evidence after metadata parsing,
  and made oversized identities/dependency sets fail closed instead of silently truncating them.
