## 1.0.3

- Server administrators in adminlist.txt can enroll multiple characters while normal players retain the configured character limit. Other enrollment validation still applies.
- Added per-mod language files using Valheim's selected language, with English fallback and no new plugin dependency.

# Changelog

## 1.0.2 - 2026-09-10

- Added AllowExistingCharacters (default true) so existing players can enroll without creating replacement characters.
- Existing-character enrollment grants no starter items and still enforces the account's character limit.
- First enrollment preserves the complete validated profile bytes in the vault and a permanent enrollment backup; it cannot overwrite an existing vault profile.
- Concurrent enrollment for the same account is serialized, including when multiple character names are allowed.
- Existing-character joins create and verify a local safety backup. Backup failure now stops profile replacement.

## 1.0.1 - 2026-09-10

- Reused Valheim's live minimap text template for the character-save status display, preventing
  Unity 6 from assigning the removed LiberationSans default font while the label is created.
- The status display now fails closed when the live Valheim font template is unavailable instead
  of creating an unreadable label.

## 1.0.0

- Added server-authoritative character download and upload for Valheim 1.0.
- Added admission gating, identity validation, transfer hashing, size limits, and chunk validation.
- Added durable server commits plus rolling recent and daily backups.
- Added client safety backups before authoritative profile replacement.
- Added protected saves for checkpoints, logout, disconnect, kick, and graceful shutdown.
- Added Steam and crossplay transport diagnostics.
- Added server configuration for single- or multi-character enrollment and optional starting items.
